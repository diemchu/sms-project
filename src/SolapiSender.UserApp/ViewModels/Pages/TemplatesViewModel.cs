using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SolapiSender.Core.Firebase;
using SolapiSender.Core.Messaging;
using SolapiSender.Core.Models.Firebase;
using SolapiSender.UserApp.Localization;
using SolapiSender.UserApp.Services;

namespace SolapiSender.UserApp.ViewModels.Pages;

public sealed partial class TemplatesViewModel : ObservableObject
{
    private readonly CloudDataService _cloud;
    private readonly UiService _ui;
    private bool _initialized;

    public TemplatesViewModel(CloudDataService cloud, UiService ui)
    {
        _cloud = cloud;
        _ui = ui;
    }

    public ObservableCollection<MessageTemplate> Templates { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private MessageTemplate? _selected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _editName = "";

    [ObservableProperty] private string _editSubject = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _editContent = "";

    [ObservableProperty] private bool _editIsAd;
    [ObservableProperty] private string _byteInfo = "";
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isEditing;

    public bool HasSelection => Selected is not null;

    public async Task OnShownAsync()
    {
        if (_initialized) return;
        _initialized = true;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        ErrorMessage = null;
        try
        {
            Templates.Clear();
            foreach (var t in await _cloud.GetTemplatesAsync()) Templates.Add(t);
            Selected = Templates.FirstOrDefault();
        }
        catch (Exception ex) when (ex is FirestoreException or FirebaseAuthException)
        {
            ErrorMessage = ErrorText.Describe(ex);
        }
    }

    partial void OnSelectedChanged(MessageTemplate? value)
    {
        IsEditing = value is not null;
        EditName = value?.Name ?? "";
        EditSubject = value?.Subject ?? "";
        EditContent = value?.Content ?? "";
        EditIsAd = value?.IsAd ?? false;
    }

    partial void OnEditContentChanged(string value)
    {
        var bytes = ByteCounter.CountBytes(value);
        var type = MessageComposer.DetectType(bytes, false);
        ByteInfo = $"{type} · {bytes} byte";
    }

    [RelayCommand]
    private void New()
    {
        Selected = null;
        IsEditing = true;
        EditName = Loc.T("Template_NewName");
        EditSubject = "";
        EditContent = "";
        EditIsAd = false;
    }

    private bool CanSave() => EditName.Trim().Length > 0 && EditContent.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var t = Selected ?? new MessageTemplate();
        var draft = new MessageTemplate
        {
            Id = t.Id,
            CreatedAt = t.CreatedAt,
            Name = EditName.Trim(),
            Subject = string.IsNullOrWhiteSpace(EditSubject) ? null : EditSubject.Trim(),
            Content = EditContent,
            IsAd = EditIsAd,
        };
        try
        {
            var saved = await _cloud.SaveTemplateAsync(draft);
            var idx = Selected is null ? -1 : Templates.IndexOf(Selected);
            if (idx >= 0) Templates[idx] = saved;
            else Templates.Insert(0, saved);
            Selected = saved;
            _ui.Success(Loc.T("Template_Saved"));
        }
        catch (Exception ex) when (ex is FirestoreException or FirebaseAuthException)
        {
            _ui.Error(ex);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Selected is not { } t) return;
        if (!await _ui.ConfirmAsync(Loc.T("Template_DeleteTitle"), Loc.F("Template_DeleteBody", t.Name), Loc.T("Common_Delete"), danger: true))
            return;
        try
        {
            await _cloud.DeleteTemplateAsync(t.Id);
            Templates.Remove(t);
            Selected = Templates.FirstOrDefault();
        }
        catch (Exception ex) when (ex is FirestoreException or FirebaseAuthException)
        {
            _ui.Error(ex);
        }
    }

    [RelayCommand]
    private void InsertName() => EditContent += "#{" + MessageComposer.NameVariable + "}";
}
