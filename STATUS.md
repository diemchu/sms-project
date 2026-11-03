# SMS_DiemDev (project SolapiSender) — Trạng thái dự án

> Cập nhật: 2026-11-03. **Đọc file này trước khi làm tiếp.** Cập nhật lại sau mỗi bước.

App WPF .NET 8 gửi SMS hàng loạt qua SOLAPI cho chủ cửa hàng điện thoại tại Hàn Quốc.
Firebase gói Spark (Auth + Firestore) để quản lý tài khoản/giấy phép.

## 1. Tiến độ

| Bước | Nội dung | Trạng thái |
|---|---|---|
| 1 | Solution + **SolapiSender.Core** + unit test | ✅ Xong |
| 2 | `firebase/firestore.rules` + **SolapiSender.AdminTool** | ✅ Xong (test trên emulator) |
| 3–6 | **SolapiSender.UserApp**: đăng nhập, kết nối SOLAPI, Dashboard, Gửi hàng loạt, Số gửi, Mẫu tin, Lịch sử, Cài đặt | ✅ Xong (kiểm tra bằng chế độ `--preview`) |
| 7 | **README** + lệnh publish single-file (nén, ~72 MB/exe) | ✅ Xong (đã chạy thử exe publish) |
| 8 | Firebase thật: project `sms-bb1cc` (Auth Email/Password + Firestore), rules + indexes đã deploy, admin + user thử đã tạo, UserApp đăng nhập thật | ✅ Xong |
| 9 | Đổi tên app → **SMS_DiemDev**; Gửi hàng loạt: tải lại mẫu mỗi lần mở trang (sửa lỗi mẫu mới không hiện), xóa từng người nhận, tự lưu nháp | ✅ Xong |

Chưa chạy với **SOLAPI key thật** (người dùng chưa cung cấp).

## 2. Môi trường

- Máy dev hiện tại: Windows 11, .NET SDK 10.0.403 cài ở `%LOCALAPPDATA%\Microsoft\dotnet` (ngoài PATH hệ thống); runtime x64 8/9/10 ở `C:\Program Files\dotnet`.
  Chạy exe đã build cần `DOTNET_ROOT=C:\Program Files\dotnet` (nếu không báo thiếu framework 8.0). Firebase CLI cài qua npm (`%APPDATA%\npm`), đã `firebase login`.
  (Máy trước đó: SDK 8.0.427, Windows 10, có Java 21 cho emulator.)
- Firebase project: `sms-bb1cc`. `appsettings.json` của UserApp đã điền (WebApiKey không phải bí mật).
- `nuget.config` trong repo trỏ nuget.org (máy không có nguồn NuGet mặc định).
- Quản lý phiên bản package tập trung ở `Directory.Packages.props`; thuộc tính chung ở `Directory.Build.props` (net8.0-windows).
- Package chính: WPF-UI 3.0.5, CommunityToolkit.Mvvm 8.3.2, Microsoft.Extensions.Hosting 8.0.1, ClosedXML 0.104.2,
  System.Security.Cryptography.ProtectedData 8.0.0, FirebaseAdmin 3.1.0, Google.Cloud.Firestore 3.9.0 (2 cái cuối chỉ AdminTool).
- Font Pretendard v1.3.9 (OFL) trong `src/SolapiSender.UserApp/Assets/Fonts/` (AdminTool link dùng chung).

## 3. Cấu trúc

```
src/SolapiSender.Core        Thư viện dùng chung (không UI)
  Solapi/      SolapiClient (balance, senderid, send-many/detail, storage, list, groups, statistics), SolapiSigner (HMAC), lỗi
  Messaging/   ByteCounter (EUC-KR), MessageComposer (biến #{이름}, (광고)/080), AdPolicy (21–08h KST), CostCalculator, BulkSender (lô ≤10.000), RecipientListBuilder
  Firebase/    FirebaseAuthService (REST), FirestoreService (REST) + FirestoreMapper, LicenseService, ActivityLogService
  Security/    DpapiStore, MachineId, PasswordHasher (PBKDF2), AppPaths
  Excel/       ExcelService (xlsx/csv UTF-8/CP949, export)
src/SolapiSender.UserApp     exe "SMS_DiemDev.exe" cho chủ quán (ko mặc định + vi, resx)
src/SolapiSender.AdminTool   exe "SMS_DiemDevAdmin.exe" chỉ cho dev (Admin SDK, giao diện tiếng Việt)
tests/SolapiSender.Core.Tests        unit + test emulator (Category=Emulator, tự skip nếu không có emulator)
tests/SolapiSender.AdminTool.Tests   test đầu-cuối Admin ⇄ UserApp trên emulator
firebase/   firestore.rules, firestore.indexes.json, firebase.json, rules-tests/ (Node, @firebase/rules-unit-testing)
```

## 4. Quyết định quan trọng (đã thống nhất / đã thay đổi so với kế hoạch)

- **SOLAPI**: tài liệu developers.solapi.com redirect → đối chiếu bằng SDK chính thức `solapi/solapi-nodejs`.
  - Header: `HMAC-SHA256 apiKey=…, date=ISO8601, salt=32 ký tự, signature=hex(HMAC_SHA256(secret, date+salt))`; ký lại mỗi lần retry.
  - Số gửi: `GET /senderid/v1/numbers/active` (từ ví dụ SDK PHP) — **schema phản hồi chưa xác nhận**, parser chấp nhận nhiều dạng → cần kiểm tra với key thật.
  - `GET /messages/v4/list` trả `messageList` là object key = messageId.
  - **Không tự retry `send-many/detail`** khi lỗi mạng/5xx (tránh gửi trùng); chỉ retry khi 429. GET retry tối đa 3 lần.
  - Map kết quả từng số qua `customFields.rid`.
  - Đơn giá ước tính mặc định SMS 18 / LMS 45 / MMS 110 원 (tạm), chỉnh trong Cài đặt.
- **Giới hạn thiết bị**: đổi `deviceCount` (kế hoạch ban đầu) → **`deviceIds: string[]`** trên `users/{uid}` vì bộ đếm cho phép đăng ký 2 PC trong 1 batch. Client đăng ký = batch tạo `devices/{machineId}` + `arrayUnion(deviceIds, machineId)`; rules kiểm tra tăng đúng 1 phần tử, ≤ maxDevices, và device tương ứng tồn tại sau batch.
- **Khóa user**: disable trên Firebase Auth + revoke refresh token + `status: "locked"`. UserApp kiểm tra lại mỗi 15 phút.
- **Mật khẩu admin**: PBKDF2-SHA256 **600.000 vòng** (kế hoạch ghi 210k, nâng theo OWASP). Bootstrap admin/admin khi collection `admins` trống, bắt buộc đổi (≥10 ký tự, chữ+số, không chứa "admin"). Khóa 5 phút sau 5 lần sai (**chỉ trong bộ nhớ**, reset khi khởi động lại).
- **Service account** của AdminTool: nội dung JSON mã hóa DPAPI (`%LOCALAPPDATA%\SolapiSender\secure\admin-service-account.bin`), không bao giờ ở UserApp.
- **API Key/Secret SOLAPI**: DPAPI theo từng uid (`secure\solapi-{uid}.bin`). Refresh token "ghi nhớ đăng nhập": `secure\session.bin`.
- **Tên**: chỉ đổi tên exe (`SMS_DiemDev.exe`, `SMS_DiemDevAdmin.exe`), `Product` và tiêu đề cửa sổ. Namespace/tên project `SolapiSender.*` và thư mục dữ liệu `%LOCALAPPDATA%\SolapiSender` **giữ nguyên** (đổi thư mục dữ liệu sẽ làm mất phiên, key SOLAPI, service account).
- **Bản nháp gửi hàng loạt**: `secure\draft-{uid}.bin` (DPAPI, vì có số điện thoại). Lưu sau 1 giây kể từ thay đổi (người nhận, nội dung, tiêu đề, cờ quảng cáo, ảnh); xóa khi trống hoặc sau khi gửi thành công. Khôi phục khi mở trang Gửi hàng loạt lần đầu.
- **AdminTool chỉ tiếng Việt** (công cụ dev); UserApp ko/vi.
- **Chế độ xem trước** `--preview` (chỉ bản Debug): UserApp thay HTTP bằng `PreviewHttpHandler` dữ liệu mẫu; AdminTool dùng `InMemoryAdminBackend`.
- Dashboard lấy số liệu từ `GET /messages/v4/statistics` (không lưu Firestore để tiết kiệm quota Spark).

## 5. Firestore schema

```
admins/{username}              passwordHash, mustChangePassword, createdAt, updatedAt, lastLoginAt   (chỉ Admin SDK)
users/{uid}                    email, storeName, phone, status("active"|"locked"), expiresAt, maxDevices,
                               deviceIds[], createdAt, updatedAt                                      (client chỉ get)
users/{uid}/devices/{machineId} machineName, appVersion, firstSeenAt, lastSeenAt
users/{uid}/templates/{id}     name, content, subject?, isAd, createdAt, updatedAt
users/{uid}/settings/app       defaultSender, lowBalanceThreshold, optOutNumber, priceSms, priceLms, priceMms, updatedAt
users/{uid}/logs/{id}          type, message, meta{}, machineId, createdAt                            (client chỉ create)
```
Tên field: camelCase; enum lưu chuỗi thường. AdminTool ghi bằng Dictionary (`FirebaseUserAdminService`) — phải khớp `UserProfile` trong Core.
Index cần deploy: `logs.createdAt` (collection group, DESC) — dùng cho "nhật ký tất cả người dùng".

## 6. Kiểm thử (lần chạy cuối: tất cả đạt)

| Bộ | Kết quả |
|---|---|
| Unit (`dotnet test`) | 91 đạt, 4 skip (test emulator) |
| Rules JS trên emulator | 23/23 |
| C# REST client + rules trên emulator | 4/4 |
| Đầu-cuối AdminTool ⇄ UserApp (Auth + Firestore emulator) | 5/5 |

Lệnh:
```powershell
dotnet build SolapiSender.sln
dotnet test                                   # unit (test emulator tự skip)
cd firebase/rules-tests; npm install          # lần đầu
cd ..   # thư mục firebase
firebase emulators:exec --only "auth,firestore" --project demo-solapisender "cd rules-tests && node --test && cd .. && dotnet test ../tests/SolapiSender.Core.Tests --filter Category=Emulator && dotnet test ../tests/SolapiSender.AdminTool.Tests"
```
Xem trước UI:
```powershell
dotnet run --project src/SolapiSender.UserApp -- --preview
dotnet run --project src/SolapiSender.AdminTool -- --preview   # admin / admin → đổi mật khẩu
```

## 7. Lỗi đã gặp & bài học (tránh lặp lại)

- **WPF-UI `PasswordBox` phát `PasswordChanged` TRƯỚC khi cập nhật `Password`** → giá trị thiếu ký tự cuối. Luôn đọc mật khẩu trực tiếp từ control lúc bấm nút (`ReadPassword` / `ReadSecret` / `ReadPasswords` trong ViewModel).
- `CurrentCulture` đặt trong hàm `async` bị hoàn lại → đặt culture trong constructor `App()`.
- Page trong NavigationView không kế thừa màu chữ theme → đặt `Foreground="{DynamicResource TextFillColorPrimaryBrush}"` trên mỗi Page.
- `MainWindow.Loaded` có thể chạy lại → chỉ điều hướng lần đầu.
- `MessageBox` của WPF-UI co theo nội dung → đặt MinWidth 420 + font app.
- `XLWorkbook(stream)` không đóng stream → đọc file vào MemoryStream trước.
- EUC-KR thay emoji bằng `??` → phát hiện ký tự không hỗ trợ bằng encode rồi decode so sánh.
- Script PowerShell 5.1 chứa tiếng Hàn phải lưu UTF-8 **có BOM**.
- Tên icon `SymbolRegular` chỉ kiểm tra lúc chạy → đối chiếu enum sau khi thêm icon mới.

## 8. Vấn đề còn mở / cần làm

1. **README + publish**: ✅ đã xong (`README.md`; publish chạy thử, exe khởi động được). Chưa kiểm tra bản publish trên máy sạch không có .NET, và chưa thử giao diện tiếng Việt trong bản publish.
2. Kiểm thử với **SOLAPI key thật** (Firebase thật đã chạy được) — đặc biệt schema `senderid/v1/numbers/active`, mã lỗi SOLAPI trong `SolapiErrorClassifier`, gửi MMS (upload ảnh), hẹn giờ.
3. Chưa thao tác thử trên UI: hộp thoại **nhập Excel** (`ImportDialog`) và **đính kèm ảnh MMS** (cần file picker).
4. Đổi ngôn ngữ trong Cài đặt: chữ đổi ngay, định dạng ngày/số chỉ đổi sau khi khởi động lại.
5. Khóa đăng nhập admin chỉ trong bộ nhớ (có thể lưu `failedAttempts` lên Firestore nếu cần).
6. Một tiến trình Java (emulator cũ) giữ cổng 8080 trên máy dev — không tắt được do quyền; emulator Firestore đã chuyển sang **8085** trong `firebase/firebase.json`.
7. `%LOCALAPPDATA%\SolapiSender\` trên máy dev có dữ liệu do chế độ preview tạo (`settings.json`, `secure\solapi-preview-user.bin`) — có thể xóa.
8. Git: nhánh `main`, remote https://github.com/diemchu/sms-project (HTTPS). Các commit từ 2026-11-03 (config Firebase, đổi tên, tính năng nháp/xóa từng dòng, README) **chưa push**.
9. Nên kiểm tra khi có SOLAPI key: tự lưu nháp + xóa từng người nhận với danh sách lớn (hàng nghìn dòng; mỗi lần xóa dựng lại toàn bộ danh sách).
10. Khi cần test trên máy dev: **đừng `Stop-Process` app đang mở** — dữ liệu chưa gửi chỉ nằm trong bộ nhớ (nay đã có nháp, nhưng vẫn mất tối đa ~1 giây).
