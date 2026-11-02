using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SolapiSender.Core.Messaging;
using SolapiSender.Core.Models;
using SolapiSender.Core.Solapi;
using SolapiSender.UserApp.Localization;
using SolapiSender.UserApp.Services;

namespace SolapiSender.UserApp.Styles;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var b = value switch
        {
            bool v => v,
            int i => i != 0,
            string s => !string.IsNullOrEmpty(s),
            null => false,
            _ => true,
        };
        return b ^ Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Visible ^ Invert;
}

/// <summary>Chuỗi khác rỗng → true (dùng cho InfoBar.IsOpen).</summary>
public sealed class HasTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string s && s.Length > 0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

/// <summary>Tô màu theo trạng thái: "ok" | "warn" | "error" | "neutral".</summary>
public sealed class StateToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value?.ToString() switch
        {
            "ok" => "SystemFillColorSuccessBrush",
            "warn" => "SystemFillColorCautionBrush",
            "error" => "SystemFillColorCriticalBrush",
            _ => "TextFillColorSecondaryBrush",
        };
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class PhoneDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string s ? SenderNumberService.Display(s) : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class RecipientStatusTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        RecipientStatus.Valid => Loc.T("Recipient_Valid"),
        RecipientStatus.Invalid => Loc.T("Recipient_Invalid"),
        RecipientStatus.Duplicate => Loc.T("Recipient_Duplicate"),
        _ => "",
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class RecipientStatusStateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        RecipientStatus.Valid => "ok",
        RecipientStatus.Duplicate => "warn",
        _ => "error",
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class SendStateTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        RecipientSendState.Accepted => Loc.T("Send_State_Accepted"),
        RecipientSendState.Rejected => Loc.T("Send_State_Rejected"),
        RecipientSendState.NotSent => Loc.T("Send_State_NotSent"),
        _ => "",
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Mã trạng thái tin (4000/2000/3000/…) → "ok"/"warn"/"error".</summary>
public sealed class StatusCodeStateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var code = value as string;
        return MessageStatusCodes.IsSuccess(code) ? "ok" : MessageStatusCodes.IsPending(code) ? "warn" : "error";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class LocalTimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        DateTimeOffset d => d.ToLocalTime().ToString(parameter as string ?? "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        _ => "",
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
