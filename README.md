# SMS_DiemDev

App WPF (.NET 8) gửi SMS/LMS/MMS hàng loạt qua [SOLAPI](https://solapi.com) cho chủ cửa hàng điện thoại tại Hàn Quốc.
Firebase gói Spark (Authentication + Firestore) dùng để quản lý tài khoản và giấy phép.

| Thành phần | File chạy | Dùng cho |
|---|---|---|
| `src/SolapiSender.UserApp` | `SMS_DiemDev.exe` | Chủ cửa hàng (giao diện tiếng Hàn / Việt) |
| `src/SolapiSender.AdminTool` | `SMS_DiemDevAdmin.exe` | Chỉ nhà phát triển: tạo/khóa tài khoản, gia hạn, giới hạn thiết bị, xem nhật ký |
| `src/SolapiSender.Core` | (thư viện) | Dùng chung: SOLAPI, Firebase REST, mã hóa, Excel |

Tên project/namespace vẫn là `SolapiSender.*`; chỉ tên file chạy và tiêu đề cửa sổ là SMS_DiemDev.
Xem `STATUS.md` để biết tiến độ, quyết định thiết kế và lỗi đã gặp.

## 1. Yêu cầu

- Windows 10/11 x64.
- Để build: **.NET SDK 8 trở lên** (SDK, không phải chỉ Runtime). Kiểm tra: `dotnet --list-sdks`.
- Để deploy Firebase: Node.js + Firebase CLI (`npm install -g firebase-tools`).
- Bản publish self-contained không cần cài .NET trên máy người dùng.

## 2. Tạo Firebase project

1. Vào <https://console.firebase.google.com> → tạo project (gói **Spark**, miễn phí).
2. **Build → Authentication → Sign-in method** → bật **Email/Password**.
3. **Build → Firestore Database** → tạo database ở chế độ **Native** (production mode; rules sẽ được deploy ở bước sau).
4. **Project settings → General**: ghi lại *Project ID* và *Web API Key*.
5. **Project settings → Service accounts → Generate new private key** → lưu file JSON **ngoài thư mục repo**
   (`.gitignore` đã chặn `*service-account*.json` và `*-firebase-adminsdk-*.json`, nhưng đừng để trong repo). Chỉ dùng cho AdminTool.

## 3. Deploy rules và indexes

```powershell
firebase login
cd firebase
firebase deploy --only firestore:rules,firestore:indexes --project <PROJECT_ID>
```

Index `logs.createdAt` (collection group, DESC) cần vài phút để build xong; nó phục vụ màn hình "nhật ký tất cả người dùng".

## 4. Cấu hình UserApp

Điền `src/SolapiSender.UserApp/appsettings.json`:

```json
{
  "Firebase": {
    "WebApiKey": "<Web API Key>",
    "ProjectId": "<Project ID>"
  }
}
```

Web API Key không phải bí mật; quyền truy cập dữ liệu do `firebase/firestore.rules` kiểm soát.
File này nằm cạnh exe (không nhúng vào single-file) nên có thể sửa sau khi phát hành.

## 5. AdminTool

1. Chạy `SMS_DiemDevAdmin.exe`. Lần đầu chọn file service account JSON (được mã hóa DPAPI, lưu ở
   `%LOCALAPPDATA%\SolapiSender\secure\admin-service-account.bin`, không bao giờ đưa vào UserApp).
2. Đăng nhập bootstrap `admin` / `admin` khi collection `admins` còn trống → **bắt buộc đổi mật khẩu**
   (≥ 10 ký tự, có chữ và số, không chứa "admin").
3. Tạo người dùng (email, mật khẩu, tên cửa hàng, ngày hết hạn, số thiết bị tối đa) rồi gửi thông tin cho chủ cửa hàng.

## 6. Dùng UserApp

1. Đăng nhập bằng tài khoản do admin cấp.
2. Kết nối SOLAPI: nhập API Key/Secret (kiểm tra bằng cách tra số dư, lưu mã hóa DPAPI theo từng tài khoản).
3. Đăng ký và xác thực số gửi trong SOLAPI Console (발신번호 관리); số sẽ hiện trong mục **Số gửi**.
4. **Gửi hàng loạt**: nhập người nhận (Excel/CSV hoặc dán), soạn tin hoặc chọn mẫu, chọn số gửi, gửi ngay hoặc hẹn giờ.
   Người nhận và nội dung đang soạn được **tự lưu nháp** (mã hóa), nên tắt app không mất; có thể xóa từng người nhận bằng nút thùng rác.
   Tin quảng cáo tự thêm `(광고)` và số từ chối 080, không gửi 21:00–08:00 KST.

## 7. Build, test, xem trước

```powershell
dotnet build SolapiSender.sln
dotnet test                       # unit test (test emulator tự bỏ qua nếu không có emulator)
```

Xem trước giao diện bằng dữ liệu mẫu (chỉ bản Debug, không cần Firebase/SOLAPI):

```powershell
dotnet run --project src/SolapiSender.UserApp -- --preview
dotnet run --project src/SolapiSender.AdminTool -- --preview     # admin / admin
```

Test rules và đầu-cuối trên emulator (cần Node, Java 21, Firebase CLI):

```powershell
cd firebase/rules-tests; npm install          # lần đầu
cd ..
firebase emulators:exec --only "auth,firestore" --project demo-solapisender "cd rules-tests && node --test && cd .. && dotnet test ../tests/SolapiSender.Core.Tests --filter Category=Emulator && dotnet test ../tests/SolapiSender.AdminTool.Tests"
```

Lưu ý: nếu SDK cài vào thư mục người dùng (`%LOCALAPPDATA%\Microsoft\dotnet`) mà Runtime x64 .NET 8 nằm ở
`C:\Program Files\dotnet`, hãy đặt `DOTNET_ROOT=C:\Program Files\dotnet` khi chạy file `.exe` đã build,
nếu không app báo thiếu framework 8.0.

## 8. Phát hành (publish)

```powershell
dotnet publish src/SolapiSender.UserApp  -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish/UserApp
dotnet publish src/SolapiSender.AdminTool -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish/AdminTool
```

Kết quả (thư mục `publish/` đã được git bỏ qua):

- `publish/UserApp/SMS_DiemDev.exe` (~72 MB) + `appsettings.json` (phải để cùng thư mục với exe).
- `publish/AdminTool/SMS_DiemDevAdmin.exe` (~73 MB). **Không phát hành file này cho khách.**

Font Pretendard và resource tiếng Việt được nhúng trong exe.

## 9. Dữ liệu trên máy người dùng

`%LOCALAPPDATA%\SolapiSender\`

| Đường dẫn | Nội dung |
|---|---|
| `settings.json` | Cài đặt cục bộ (ngôn ngữ, ...) |
| `logs\app-yyyyMMdd.log` | Nhật ký ứng dụng |
| `secure\session.bin` | Phiên đăng nhập "ghi nhớ" (DPAPI) |
| `secure\solapi-<uid>.bin` | API Key/Secret SOLAPI (DPAPI) |
| `secure\draft-<uid>.bin` | Bản nháp gửi hàng loạt (DPAPI) |

Dữ liệu DPAPI chỉ giải mã được bởi đúng tài khoản Windows trên đúng máy đó.
