using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SolapiSender.Core;
using SolapiSender.Core.Excel;
using SolapiSender.Core.Firebase;
using SolapiSender.Core.Messaging;
using SolapiSender.Core.Models;
using SolapiSender.Core.Models.Firebase;
using SolapiSender.Core.Security;
using SolapiSender.Core.Solapi;
using SolapiSender.UserApp.Localization;
using SolapiSender.UserApp.Services;

namespace SolapiSender.UserApp.ViewModels.Pages;

public sealed record SendResultRow(string Phone, string? Name, RecipientSendState State, string? StatusCode, string? Message)
{
    public string StateKey => State switch
    {
        RecipientSendState.Accepted => "ok",
        RecipientSendState.NotSent => "warn",
        _ => "error",
    };
}

/// <summary>Bản nháp tự lưu (mã hóa DPAPI) để không mất người nhận/nội dung khi tắt app.</summary>
internal sealed record BulkSendDraft(List<RawRecipient> Recipients, string MessageText, string Subject, bool IsAd, string? ImagePath);

public sealed partial class BulkSendViewModel : ObservableObject
{
    private readonly DpapiStore _store;
    private readonly DispatcherTimer _draftTimer;
    private bool _draftReady;
    private readonly ExcelService _excel;
    private readonly BulkSender _sender;
    private readonly SolapiClient _solapi;
    private readonly CloudDataService _cloud;
    private readonly SessionService _session;
    private readonly UiService _ui;
    private readonly ActivityLogService _log;
    private readonly ILogger<BulkSendViewModel> _logger;
    private readonly DispatcherTimer _recalcTimer;
    private CancellationTokenSource? _sendCts;
    private bool _initialized;

    public BulkSendViewModel(ExcelService excel, BulkSender sender, SolapiClient solapi, SenderNumberService senders,
        CloudDataService cloud, SessionService session, UiService ui, ActivityLogService log, DpapiStore store, ILogger<BulkSendViewModel> logger)
    {
        _store = store;
        _excel = excel;
        _sender = sender;
        _solapi = solapi;
        Senders = senders;
        _cloud = cloud;
        _session = session;
        _ui = ui;
        _log = log;
        _logger = logger;

        _recalcTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _recalcTimer.Tick += async (_, _) =>
        {
            _recalcTimer.Stop();
            await RecalculateAsync();
        };
        _draftTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _draftTimer.Tick += (_, _) =>
        {
            _draftTimer.Stop();
            SaveDraft();
        };
        senders.DefaultChanged += (_, _) => SelectedSender = senders.DefaultNumber;
        Loc.Instance.LanguageChanged += (_, _) => ScheduleRecalc();
    }

    public SenderNumberService Senders { get; }

    // ------------------------------------------------------------------ Người nhận

    public ObservableCollection<Recipient> Recipients { get; } = [];

    [ObservableProperty] private RecipientListSummary _summary = new(0, 0, 0, 0);
    [ObservableProperty] private bool _isPasteOpen;
    [ObservableProperty] private string _pasteText = "";

    /// <summary>Tên các biến có sẵn từ cột Excel (ngoài #{이름}).</summary>
    public ObservableCollection<string> AvailableVariables { get; } = [MessageComposer.NameVariable];

    // ------------------------------------------------------------------ Soạn tin

    [ObservableProperty] private string _messageText = "";
    [ObservableProperty] private string _subject = "";
    [ObservableProperty] private bool _isAd;
    [ObservableProperty] private string _adSenderName = "";
    [ObservableProperty] private string _optOutNumber = "";
    [ObservableProperty] private string? _imagePath;
    [ObservableProperty] private string? _imageInfo;

    public ObservableCollection<MessageTemplate> Templates { get; } = [];
    [ObservableProperty] private MessageTemplate? _selectedTemplate;

    [ObservableProperty] private int _byteCount;
    [ObservableProperty] private string _typeLabel = "SMS";
    [ObservableProperty] private string _byteLimitText = "/ 90 byte";
    [ObservableProperty] private string? _unsupportedWarning;
    [ObservableProperty] private string _previewText = "";
    [ObservableProperty] private string _costText = "-";
    [ObservableProperty] private string? _composeError;

    // ------------------------------------------------------------------ Gửi

    [ObservableProperty] private string? _selectedSender;
    [ObservableProperty] private bool _isScheduled;
    [ObservableProperty] private DateTime? _scheduleDate = DateTime.Today.AddDays(1);
    [ObservableProperty] private int _scheduleHour = 10;
    [ObservableProperty] private int _scheduleMinute;

    public IReadOnlyList<int> Hours { get; } = Enumerable.Range(0, 24).ToList();
    public IReadOnlyList<int> Minutes { get; } = Enumerable.Range(0, 12).Select(i => i * 5).ToList();

    [ObservableProperty] private bool _isSending;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _progressText = "";

    public ObservableCollection<SendResultRow> Results { get; } = [];
    [ObservableProperty] private string? _resultSummary;

    public async Task OnShownAsync()
    {
        // Mẫu tin có thể được thêm/sửa ở trang Mẫu tin → tải lại mỗi lần mở trang.
        await ReloadTemplatesAsync();

        if (_initialized) return;
        _initialized = true;
        AdSenderName = _session.Profile?.StoreName ?? "";
        OptOutNumber = _session.CloudSettings.OptOutNumber ?? "";
        RestoreDraft();

        try
        {
            await Senders.EnsureLoadedAsync();
            SelectedSender = Senders.DefaultNumber;
        }
        catch (SolapiException ex)
        {
            _ui.Error(ex);
        }
        ScheduleRecalc();
    }

    private async Task ReloadTemplatesAsync()
    {
        try
        {
            var list = await _cloud.GetTemplatesAsync();
            Templates.Clear();
            foreach (var t in list) Templates.Add(t);
        }
        catch (Exception ex) when (ex is FirestoreException or FirebaseAuthException or System.Net.Http.HttpRequestException)
        {
            _logger.LogWarning(ex, "Không tải được mẫu tin");
        }
    }

    partial void OnMessageTextChanged(string value) => ScheduleRecalc();
    partial void OnIsAdChanged(bool value) => ScheduleRecalc();
    partial void OnAdSenderNameChanged(string value) => ScheduleRecalc();
    partial void OnOptOutNumberChanged(string value) => ScheduleRecalc();
    partial void OnImagePathChanged(string? value) => ScheduleRecalc();
    partial void OnSubjectChanged(string value) => ScheduleDraftSave();

    partial void OnSelectedTemplateChanged(MessageTemplate? value)
    {
        if (value is null) return;
        MessageText = value.Content;
        Subject = value.Subject ?? "";
        IsAd = value.IsAd;
    }

    private void ScheduleRecalc()
    {
        _recalcTimer.Stop();
        _recalcTimer.Start();
        ScheduleDraftSave();
    }

    // ------------------------------------------------------------------ Bản nháp

    private string DraftName => "draft-" + _session.Uid;

    private void ScheduleDraftSave()
    {
        if (!_draftReady) return;
        _draftTimer.Stop();
        _draftTimer.Start();
    }

    private void RestoreDraft()
    {
        try
        {
            if (string.IsNullOrEmpty(_session.Uid)) return;
            var draft = _store.Load<BulkSendDraft>(DraftName);
            if (draft is null) return;

            foreach (var v in draft.Recipients.SelectMany(r => r.Variables?.Keys ?? []).Distinct())
                if (!AvailableVariables.Contains(v)) AvailableVariables.Add(v);
            if (draft.Recipients.Count > 0) AddRecipients(draft.Recipients);

            MessageText = draft.MessageText ?? "";
            Subject = draft.Subject ?? "";
            IsAd = draft.IsAd;
            if (draft.ImagePath is { } img && File.Exists(img))
            {
                ImagePath = img;
                ImageInfo = $"{Path.GetFileName(img)} · {new FileInfo(img).Length / 1024.0:N0} KB";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không khôi phục được bản nháp");
        }
        finally
        {
            _draftReady = true;
        }
    }

    private void SaveDraft()
    {
        if (!_draftReady || string.IsNullOrEmpty(_session.Uid)) return;
        try
        {
            if (Recipients.Count == 0 && string.IsNullOrWhiteSpace(MessageText) && string.IsNullOrWhiteSpace(Subject) && ImagePath is null)
            {
                _store.Delete(DraftName);
                return;
            }
            var raws = Recipients.Select(r => new RawRecipient(r.RawPhone, r.Name, r.Variables, r.RowNumber)).ToList();
            _store.Save(DraftName, new BulkSendDraft(raws, MessageText, Subject, IsAd, ImagePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            _logger.LogWarning(ex, "Không lưu được bản nháp");
        }
    }

    // ------------------------------------------------------------------ Người nhận: lệnh

    [RelayCommand]
    private void ImportFile()
    {
        var path = UiService.PickOpenFile(Loc.T("File_ExcelFilter"));
        if (path is null) return;

        var raws = _ui.ShowImportDialog(path, _excel);
        if (raws is null) return;

        foreach (var v in raws.SelectMany(r => r.Variables?.Keys ?? []).Distinct())
            if (!AvailableVariables.Contains(v)) AvailableVariables.Add(v);
        AddRecipients(raws);
    }

    [RelayCommand]
    private void TogglePaste() => IsPasteOpen = !IsPasteOpen;

    [RelayCommand]
    private void AddPasted()
    {
        var raws = RecipientListBuilder.ParsePasted(PasteText);
        if (raws.Count == 0) return;
        AddRecipients(raws);
        PasteText = "";
        IsPasteOpen = false;
    }

    private void AddRecipients(IEnumerable<RawRecipient> raws)
    {
        // Gộp với danh sách hiện có rồi tính lại trạng thái trùng trên toàn bộ.
        var existing = Recipients.Select(r => new RawRecipient(r.RawPhone, r.Name, r.Variables, r.RowNumber));
        var offset = Recipients.Count;
        var incoming = raws.Select((r, i) => r with { RowNumber = offset + i + 1 });
        var all = RecipientListBuilder.Build(existing.Concat(incoming));

        Recipients.Clear();
        foreach (var r in all) Recipients.Add(r);
        Summary = RecipientListBuilder.Summarize(all);
        ScheduleRecalc();
    }

    [RelayCommand]
    private void RemoveInvalid()
    {
        var keep = Recipients.Where(r => r.Status == RecipientStatus.Valid).ToList();
        Recipients.Clear();
        foreach (var r in keep) Recipients.Add(r);
        Summary = RecipientListBuilder.Summarize(keep);
        ScheduleDraftSave();
    }

    [RelayCommand]
    private void RemoveRecipient(Recipient? recipient)
    {
        if (IsSending || recipient is null || !Recipients.Contains(recipient)) return;

        // Dựng lại toàn bộ để trạng thái trùng được tính lại (dòng trùng có thể trở thành hợp lệ) và đánh số lại.
        var raws = Recipients
            .Where(r => !ReferenceEquals(r, recipient))
            .Select((r, i) => new RawRecipient(r.RawPhone, r.Name, r.Variables, i + 1));
        var all = RecipientListBuilder.Build(raws);

        Recipients.Clear();
        foreach (var r in all) Recipients.Add(r);
        Summary = RecipientListBuilder.Summarize(all);
        ScheduleRecalc();
    }

    [RelayCommand]
    private void ClearRecipients()
    {
        Recipients.Clear();
        Summary = new RecipientListSummary(0, 0, 0, 0);
        Results.Clear();
        ResultSummary = null;
        ScheduleRecalc();
    }

    // ------------------------------------------------------------------ Soạn: lệnh

    [RelayCommand]
    private void InsertVariable(string name) => MessageText += $"#{{{name}}}";

    [RelayCommand]
    private void PickImage()
    {
        var path = UiService.PickOpenFile(Loc.T("File_JpgFilter"));
        if (path is null) return;
        var size = new FileInfo(path).Length;
        if (!MessageComposer.IsValidMmsImage(path, size, out var err))
        {
            _ui.Error(Loc.T(err == "size" ? "Compose_ImageTooLarge" : "Compose_ImageFormat"));
            return;
        }
        ImagePath = path;
        ImageInfo = $"{Path.GetFileName(path)} · {size / 1024.0:N0} KB";
    }

    [RelayCommand]
    private void RemoveImage()
    {
        ImagePath = null;
        ImageInfo = null;
    }

    // ------------------------------------------------------------------ Tính toán

    private AdOptions? CurrentAd => IsAd ? new AdOptions { SenderName = AdSenderName, OptOutNumber = OptOutNumber } : null;

    private Recipient SampleRecipient =>
        Recipients.FirstOrDefault(r => r.Status == RecipientStatus.Valid)
        ?? new Recipient { RowNumber = 0, RawPhone = "01012345678", Phone = "01012345678", Name = Loc.T("Compose_SampleName"), Status = RecipientStatus.Valid };

    private async Task RecalculateAsync()
    {
        var template = MessageText;
        var ad = CurrentAd;
        var hasImage = ImagePath is not null;

        var preview = MessageComposer.Compose(template, SampleRecipient, ad);
        var count = ByteCounter.Count(preview);
        PreviewText = preview;
        ByteCount = count.Bytes;
        var type = MessageComposer.DetectType(count.Bytes, hasImage);
        TypeLabel = type.ToString();
        ByteLimitText = type == MessageType.SMS ? $"/ {MessageComposer.SmsMaxBytes} byte" : $"/ {MessageComposer.LmsMaxBytes} byte";
        UnsupportedWarning = count.UnsupportedChars.Count > 0
            ? Loc.F("Compose_Unsupported", string.Join(" ", count.UnsupportedChars))
            : null;
        ComposeError = count.Bytes > MessageComposer.LmsMaxBytes ? Loc.T("Compose_TooLong") : null;

        var valid = Recipients.Where(r => r.Status == RecipientStatus.Valid).ToList();
        if (valid.Count == 0 || string.IsNullOrWhiteSpace(template))
        {
            CostText = "-";
            return;
        }

        var prices = CurrentPrices();
        var estimate = await Task.Run(() => CostCalculator.Estimate(
            valid.Select(r => MessageComposer.DetectType(ByteCounter.CountBytes(MessageComposer.Compose(template, r, ad)), hasImage)),
            prices));
        CostText = Loc.F("Compose_Cost", estimate.TotalCount, estimate.SmsCount, estimate.LmsCount, estimate.MmsCount, estimate.Total);
    }

    private PriceTable CurrentPrices()
    {
        var s = _session.CloudSettings;
        return new PriceTable(s.PriceSms, s.PriceLms, s.PriceMms);
    }

    // ------------------------------------------------------------------ Gửi

    private DateTimeOffset? ScheduledAt =>
        IsScheduled && ScheduleDate is { } d
            ? new DateTimeOffset(d.Date.AddHours(ScheduleHour).AddMinutes(ScheduleMinute), AdPolicy.KoreaTimeZone.BaseUtcOffset)
            : null;

    private string? Validate()
    {
        if (Summary.Valid == 0) return Loc.T("Send_Err_NoRecipients");
        if (string.IsNullOrWhiteSpace(MessageText)) return Loc.T("Send_Err_NoText");
        if (string.IsNullOrEmpty(SelectedSender)) return Loc.T("Send_Err_NoSender");
        if (ComposeError is not null) return ComposeError;
        if (IsAd)
        {
            var optOut = PhoneNumberHelper.DigitsOnly(OptOutNumber);
            if (!optOut.StartsWith("080", StringComparison.Ordinal) || optOut.Length < 10) return Loc.T("Send_Err_OptOut");
        }

        var at = ScheduledAt ?? DateTimeOffset.UtcNow;
        if (IsScheduled && (ScheduledAt is null || at < DateTimeOffset.UtcNow.AddMinutes(10)))
            return Loc.T("Send_Err_ScheduleTooSoon");
        if (IsAd && AdPolicy.IsQuietHours(at))
            return Loc.F("Send_Err_QuietHours", AdPolicy.NextAllowedTime(at).ToString("yyyy-MM-dd HH:mm"));
        return null;
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        var error = Validate();
        if (error is not null)
        {
            await _ui.AlertAsync(Loc.T("Send_CannotSend"), error);
            return;
        }

        var template = MessageText;
        var ad = CurrentAd;
        var hasImage = ImagePath is not null;
        var subject = string.IsNullOrWhiteSpace(Subject) ? null : Subject.Trim();
        var valid = Recipients.Where(r => r.Status == RecipientStatus.Valid).ToList();

        var messages = await Task.Run(() => valid.Select((r, i) =>
        {
            var text = MessageComposer.Compose(template, r, ad);
            var type = MessageComposer.DetectType(ByteCounter.CountBytes(text), hasImage);
            return new OutgoingMessage(i, r.Phone!, text, type, subject);
        }).ToList());

        var estimate = CostCalculator.Estimate(messages.Select(m => m.Type), CurrentPrices());
        var when = ScheduledAt is { } s ? s.ToString("yyyy-MM-dd HH:mm") + " (KST)" : Loc.T("Send_Now");
        var confirm = Loc.F("Send_ConfirmBody", estimate.TotalCount, SenderNumberService.Display(SelectedSender!), when,
            estimate.SmsCount, estimate.LmsCount, estimate.MmsCount, estimate.Total);

        try
        {
            var balance = await _solapi.GetBalanceAsync();
            if (balance.Balance + balance.Point < estimate.Total)
                confirm += "\n\n" + Loc.F("Send_ConfirmLowBalance", balance.Balance + balance.Point);
        }
        catch (SolapiException ex)
        {
            _ui.Error(ex);
            return;
        }

        if (!await _ui.ConfirmAsync(Loc.T("Send_ConfirmTitle"), confirm, Loc.T("Send_Button")))
            return;

        IsSending = true;
        Results.Clear();
        ResultSummary = null;
        _sendCts = new CancellationTokenSource();
        try
        {
            string? imageId = null;
            if (hasImage)
            {
                ProgressText = Loc.T("Send_UploadingImage");
                imageId = await _solapi.UploadMmsImageAsync(await File.ReadAllBytesAsync(ImagePath!), Path.GetFileName(ImagePath!), _sendCts.Token);
            }

            var progress = new Progress<SendProgress>(p =>
            {
                Progress = p.Total == 0 ? 0 : 100.0 * p.Processed / p.Total;
                ProgressText = Loc.F("Send_Progress", p.Processed, p.Total, p.CompletedBatches, p.TotalBatches);
            });

            var result = await _sender.SendAsync(new BulkSendRequest
            {
                From = SelectedSender!,
                Messages = messages,
                ImageId = imageId,
                ScheduledAt = ScheduledAt,
            }, progress, _sendCts.Token);

            foreach (var r in result.Results)
            {
                var rec = valid[r.Id];
                Results.Add(new SendResultRow(rec.DisplayPhone, rec.Name, r.State, r.StatusCode, r.StatusMessage));
            }
            ResultSummary = Loc.F("Send_ResultSummary", result.AcceptedCount, result.RejectedCount, result.NotSentCount);

            if (result.StopError is not null) await _ui.AlertAsync(Loc.T("Send_Stopped"), ErrorText.Describe(result.StopError));
            else if (result.AcceptedCount > 0) _ui.Success(ScheduledAt is null ? Loc.T("Send_Done") : Loc.T("Send_Scheduled"));

            // Đã gửi: bỏ bản nháp để lần mở sau không khôi phục lại danh sách vừa gửi.
            if (result.AcceptedCount > 0)
            {
                _draftTimer.Stop();
                _store.Delete(DraftName);
            }

            _ = _log.LogAsync(_session.Uid, _session.MachineId,
                ScheduledAt is null ? ActivityLogTypes.Send : ActivityLogTypes.Schedule,
                $"{result.AcceptedCount}/{messages.Count}",
                new Dictionary<string, object?>
                {
                    ["groupIds"] = result.GroupIds.ToList(),
                    ["accepted"] = result.AcceptedCount,
                    ["rejected"] = result.RejectedCount,
                    ["notSent"] = result.NotSentCount,
                    ["estimatedCost"] = estimate.Total,
                    ["isAd"] = IsAd,
                });
        }
        catch (SolapiException ex)
        {
            _ui.Error(ex);
        }
        catch (OperationCanceledException)
        {
            _ui.Info(Loc.T("Err_Cancelled"));
        }
        finally
        {
            IsSending = false;
            Progress = 0;
            ProgressText = "";
            _sendCts.Dispose();
            _sendCts = null;
        }
    }

    [RelayCommand]
    private void CancelSend() => _sendCts?.Cancel();

    [RelayCommand]
    private void ExportResults()
    {
        if (Results.Count == 0) return;
        var path = UiService.PickSaveFile(Loc.T("File_XlsxFilter"), $"send-result-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
        if (path is null) return;
        _excel.Export(path, Loc.T("Send_ResultSheet"),
            [Loc.T("Col_Phone"), Loc.T("Col_Name"), Loc.T("Col_Result"), Loc.T("Col_Code"), Loc.T("Col_Message")],
            Results.Select(r => (IReadOnlyList<object?>)[r.Phone, r.Name, new Styles.SendStateTextConverter().Convert(r.State, typeof(string), null!, null!), r.StatusCode, r.Message]));
        _ui.Success(Loc.T("Common_Exported"));
    }
}
