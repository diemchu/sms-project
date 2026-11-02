using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SolapiSender.Core.Firebase;
using SolapiSender.Core.Solapi;
using SolapiSender.UserApp.Localization;
using SolapiSender.UserApp.Services;

namespace SolapiSender.UserApp.ViewModels.Pages;

public sealed partial class SenderRow : ObservableObject
{
    public required string Number { get; init; }
    public string Display => SenderNumberService.Display(Number);
    [ObservableProperty] private bool _isDefault;
}

public sealed partial class SenderNumbersViewModel : ObservableObject
{
    private readonly SenderNumberService _senders;
    private readonly UiService _ui;
    private bool _initialized;

    public SenderNumbersViewModel(SenderNumberService senders, UiService ui)
    {
        _senders = senders;
        _ui = ui;
    }

    public ObservableCollection<SenderRow> Rows { get; } = [];

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isEmpty;

    public async Task OnShownAsync()
    {
        if (_initialized) return;
        _initialized = true;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        ErrorMessage = null;
        try
        {
            await _senders.RefreshAsync();
            Rebuild();
        }
        catch (SolapiException ex)
        {
            ErrorMessage = ErrorText.Describe(ex);
        }
    }

    private void Rebuild()
    {
        Rows.Clear();
        var def = _senders.DefaultNumber;
        foreach (var n in _senders.Numbers) Rows.Add(new SenderRow { Number = n, IsDefault = n == def });
        IsEmpty = Rows.Count == 0;
    }

    [RelayCommand]
    private async Task SetDefaultAsync(SenderRow row)
    {
        try
        {
            await _senders.SetDefaultAsync(row.Number);
            foreach (var r in Rows) r.IsDefault = r == row;
            _ui.Success(Loc.F("Sender_DefaultSet", row.Display));
        }
        catch (Exception ex) when (ex is FirestoreException or FirebaseAuthException)
        {
            _ui.Error(ex);
        }
    }

    [RelayCommand]
    private static void OpenConsole() =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://console.solapi.com/senderids") { UseShellExecute = true });
}
