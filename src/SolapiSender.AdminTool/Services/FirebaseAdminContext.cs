using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Api.Gax;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;

namespace SolapiSender.AdminTool.Services;

/// <summary>Kết nối Firebase Admin SDK (Auth) + Firestore bằng service account.</summary>
public sealed class FirebaseAdminContext : IDisposable
{
    private FirebaseApp? _app;

    public FirebaseAuth Auth => _auth ?? throw new InvalidOperationException("Firebase chưa được khởi tạo.");
    public FirestoreDb Db => _db ?? throw new InvalidOperationException("Firebase chưa được khởi tạo.");
    public ServiceAccountInfo Info { get; private set; } = new("", "");
    public bool IsReady => _auth is not null && _db is not null;

    private FirebaseAuth? _auth;
    private FirestoreDb? _db;

    /// <summary>Khởi tạo từ nội dung JSON service account.</summary>
    public void Initialize(string serviceAccountJson)
    {
        var info = ServiceAccountStore.Parse(serviceAccountJson);
        var credential = GoogleCredential.FromJson(serviceAccountJson);
        Initialize(info, credential, EmulatorDetection.None);
    }

    /// <summary>Dùng với Firebase Emulator (biến môi trường FIRESTORE_EMULATOR_HOST, FIREBASE_AUTH_EMULATOR_HOST).</summary>
    public void InitializeForEmulator(string projectId) =>
        Initialize(new ServiceAccountInfo(projectId, "emulator"), GoogleCredential.FromAccessToken("owner"), EmulatorDetection.EmulatorOnly);

    private void Initialize(ServiceAccountInfo info, GoogleCredential credential, EmulatorDetection emulator)
    {
        Dispose();
        _app = FirebaseApp.Create(new AppOptions { Credential = credential, ProjectId = info.ProjectId },
            "solapisender-admin-" + Guid.NewGuid().ToString("N"));
        _auth = FirebaseAuth.GetAuth(_app);
        _db = new FirestoreDbBuilder
        {
            ProjectId = info.ProjectId,
            GoogleCredential = emulator == EmulatorDetection.EmulatorOnly ? null : credential,
            EmulatorDetection = emulator,
        }.Build();
        Info = info;
    }

    public void Dispose()
    {
        _app?.Delete();
        _app = null;
        _auth = null;
        _db = null;
    }
}
