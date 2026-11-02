using SolapiSender.Core.Firebase;
using SolapiSender.Core.Solapi;
using SolapiSender.UserApp.Localization;

namespace SolapiSender.UserApp.Services;

/// <summary>Chuyển exception thành thông báo thân thiện theo ngôn ngữ hiện tại.</summary>
public static class ErrorText
{
    public static string Describe(Exception ex) => ex switch
    {
        SolapiException s => Solapi(s),
        FirebaseAuthException a => Loc.T(a.Kind switch
        {
            FirebaseAuthErrorKind.Network => "Err_Network",
            FirebaseAuthErrorKind.InvalidCredentials => "Err_Login_Invalid",
            FirebaseAuthErrorKind.UserDisabled => "Err_Login_Locked",
            FirebaseAuthErrorKind.TooManyAttempts => "Err_Login_TooMany",
            FirebaseAuthErrorKind.SessionExpired => "Err_SessionExpired",
            FirebaseAuthErrorKind.NotConfigured => "Err_FirebaseNotConfigured",
            _ => "Err_Unknown",
        }),
        FirestoreException f when f.IsNetwork => Loc.T("Err_Network"),
        FirestoreException f when f.IsPermissionDenied => Loc.T("Err_PermissionDenied"),
        OperationCanceledException => Loc.T("Err_Cancelled"),
        _ => Loc.T("Err_Unknown"),
    };

    private static string Solapi(SolapiException s)
    {
        var text = Loc.T(s.Kind switch
        {
            SolapiErrorKind.Network => "Err_Network",
            SolapiErrorKind.Timeout => "Err_Timeout",
            SolapiErrorKind.InvalidCredentials => "Err_Solapi_InvalidKey",
            SolapiErrorKind.ClockSkew => "Err_Solapi_ClockSkew",
            SolapiErrorKind.Forbidden => "Err_Solapi_Forbidden",
            SolapiErrorKind.InsufficientBalance => "Err_Solapi_Balance",
            SolapiErrorKind.SenderNotRegistered => "Err_Solapi_Sender",
            SolapiErrorKind.InvalidRequest => "Err_Solapi_Invalid",
            SolapiErrorKind.RateLimited => "Err_Solapi_RateLimit",
            SolapiErrorKind.Server => "Err_Solapi_Server",
            _ => "Err_Unknown",
        });
        // Kèm mã lỗi gốc để người dùng báo lại cho hỗ trợ.
        return string.IsNullOrEmpty(s.ErrorCode) ? text : $"{text} ({s.ErrorCode})";
    }
}
