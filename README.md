# 📍 Travel Guide — Ứng dụng khám phá phố ẩm thực Vĩnh Khánh

> **Đồ án môn học: Ngôn ngữ lập trình C# (841423)**

> Lớp: DCT123C2


## 👥 Thông tin nhóm — Nhóm 8

| MSSV       | Họ và Tên          |
|------------|--------------------|
| 3123411210 | Hồ Ngọc Phương Nhi |
| 3123411311 | Nguyễn Hoàng Trí   |


## 📖 Giới thiệu

**Travel Guide** là ứng dụng di động **.NET MAUI Native** phục vụ du khách khám phá **Phố Ẩm Thực Vĩnh Khánh (Quận 4, TP. HCM)**: bản đồ **Mapbox GL** trong WebView, thuyết minh (TTS và/hoặc audio URL), geofence GPS, quét QR, lịch sử quét và giao diện đa ngôn ngữ.

Hệ thống gồm 3 phần chạy cùng nhau:
- **TravelGuide** (MAUI mobile app)
- **TravelGuide.API** (ASP.NET Core Minimal API cho du khách: auth, premium, unlock POI, scan-log, public POI)
- **TravelGuide.AdminWeb** (ASP.NET Core Minimal API + SPA tĩnh trong thư mục `WEB`: `index.html`, `app.js`, `styles.css`)

Admin/owner đăng nhập và quản trị dữ liệu trên **SQL Server**: chỉnh sửa POI (tên, mô tả, tọa độ, ảnh, link bản đồ ngoài, URL audio, mức ưu tiên geofence), duyệt/từ chối bài đăng, sửa bản dịch, quản lý user (gồm **khóa/mở khóa** đăng nhập) và **xuất `extra_places.json`** để đồng bộ với app.


## ✨ Tính năng chính

### 🗺️ Bản đồ tương tác (WebView + Mapbox GL)
- Bản đồ vector **Mapbox GL JS** (style streets)
- Token Mapbox: `Preferences`, biến môi trường `MAPBOX_ACCESS_TOKEN`, hoặc file local `Resources/Raw/mapbox_token.secret.txt` (
- Hiển thị POI trên bản đồ, màu marker theo bán kính; popup và nút định vị
- Highlight địa điểm khi vào vùng geofence; đồng bộ vị trí người dùng

### 🎙️ Thuyết minh tự động (TTS + Audio URL)
- Nếu POI có **Audio URL** hợp lệ → phát qua **Plugin.Maui.Audio**; lỗi hoặc không có URL → **Text-to-Speech** (`Microsoft.Maui.Media.TextToSpeech`)
- Hàng đợi phát, Skip, Stop, Shuffle (màn **Âm thanh**)
- Giọng TTS theo ngôn ngữ UI (vi/en/ja/ko/zh)

### 📡 Geofence Engine
- Theo dõi GPS qua `GpsBackgroundService` (Android có foreground service khi cấu hình)
- Tự động kích hoạt thuyết minh khi vào bán kính POI; **ưu tiên** (`Priority`) khi nhiều vùng chồng nhau
- **Debounce** 3 giây trong vùng trước khi coi là “vào điểm”
- **Cooldown** 60 giây giữa hai lần phát cùng một POI
- Sự kiện `OnPoiEntered`, `OnPoiTriggered`, `OnPoiExited` cho UI

### 🔊 Mini Player
- Thanh phát cố định đáy màn hình khi đang thuyết minh
- Hoạt động khi chuyển trang; nút điều khiển trên thanh player

### 🌐 Đa ngôn ngữ (i18n)
- 5 ngôn ngữ: 🇻🇳 Tiếng Việt, 🇬🇧 English, 🇯🇵 日本語, 🇰🇷 한국어, 🇨🇳 中文
- Giao diện: `.resx` + LocalizationResourceManager.Maui
- Nội dung địa điểm: đồng bộ API / dịch (MyMemory qua admin) và cache SQLite trên app

### 💱 Đơn vị tiền tệ
- Chọn tiền tệ từ danh sách `CultureInfo`, lưu bằng `Preferences`

### 🖥️ Web quản trị — TravelGuide.AdminWeb
- **Đăng nhập** → nhận Bearer token (lưu RAM server); vai trò **admin** (toàn quyền) hoặc **owner** (POI của mình, trừ một số thao tác chỉ admin)
- **Quản lý POI & audio**: tạo/sửa/xóa (xóa chỉ admin), hiển thị trạng thái Published / Pending / Rejected; admin **duyệt** hoặc **từ chối** kèm lý do
- **Bản dịch**: chỉnh tên/mô tả EN, JA, KO, ZH từng POI; có thể bổ sung dịch gợi ý qua MyMemory (server)
- **Tài khoản** (admin): tạo user, xóa (không xóa `admin`), **khóa / mở khóa** — user bị khóa không đăng nhập được
- **Xuất** file JSON `extra_places.json` định dạng phù hợp app offline/seed
- **Công nghệ**: SQL Server + API REST + file tĩnh trong `WEB` (không có Razor/Blazor)

---

## 🛠️ Công nghệ sử dụng

| Thành phần       | Công nghệ                                              |
|------------------|--------------------------------------------------------|
| Framework app    | .NET 9.0 MAUI (Native)                                 |
| Bản đồ           | WebView + Mapbox GL JS                                 |
| Âm thanh         | Plugin.Maui.Audio + MAUI TextToSpeech                  |
| Cơ sở dữ liệu app| sqlite-net-pcl                                         |
| Tourist API      | ASP.NET Core 9 Minimal API + SQL Server                |
| Web admin        | ASP.NET Core 9 Minimal API + static `WEB` + SQL Server |
| Dịch thuật (CMS) | MyMemory API (trong AdminWeb)                          |
| Đa ngôn ngữ UI   | LocalizationResourceManager.Maui + `.resx`             |
| Messaging        | CommunityToolkit.Mvvm (WeakReferenceMessenger)         |
| Cài đặt          | Microsoft.Maui.Storage.Preferences                     |
| Định vị          | Microsoft.Maui.Devices.Sensors.Geolocation             |

---

## 📁 Cấu trúc project
```
TravelGuide/                                 # Thư mục gốc solution
├── TravelGuide.sln
├── README.md
├── TravelGuide/                             # Project ứng dụng MAUI
│   ├── Models/
│   │   ├── LocationMessage.cs
│   │   ├── TouristPlaceReview.cs
│   │   └── TouristPlace.cs
│   ├── Platforms/
│   │   ├── Android/   (Manifest, MainActivity, LocationService, …)
│   │   ├── iOS/
│   │   ├── MacCatalyst/
│   │   └── Windows/
│   ├── Resources/
│   │   ├── AppIcon/, Fonts/, Images/, Splash/, Styles/
│   │   └── Raw/
│   │       ├── AboutAssets.txt
│   │       ├── extra_places.json          # Seed địa điểm ban đầu
│   │       └── mapbox_token.secret.txt    # (tự tạo, local) token Mapbox pk…
│   ├── AppResources*.resx                 # Chuỗi UI đa ngôn ngữ
│   ├── App.xaml / AppShell.xaml
│   ├── MainPage.xaml / .cs                  # Chọn ngôn ngữ, tiền tệ
│   ├── HomePage.xaml / .cs                  # Trang chủ, danh sách địa điểm
│   ├── MapPage.xaml / .cs                   # WebView Mapbox, GPS, geofence UI
│   ├── PlaceDetailPage.xaml / .cs
│   ├── AudioPage.xaml / .cs
│   ├── MiniPlayerView.xaml / .cs
│   ├── DatabaseService.cs
│   ├── GeofenceEngine.cs
│   ├── GpsBackgroundService.cs
│   ├── NarrationEngine.cs
│   ├── TranslationService.cs
│   ├── MapboxConfig.cs
│   ├── MapboxTokenBootstrap.cs
│   └── MauiProgram.cs
│
├── TravelGuide.API/                         # API cho du khách
│   ├── Program.cs
│   └── Data/TouristDb.cs
│
├── TravelGuide.AdminWeb/                    # CMS: API + SPA
│   ├── Program.cs                           # Minimal API, static files
│   ├── Data/TravelGuideDb.cs                # SQL Server admin (POI, UserAccount, khóa TK…)
│   ├── Models/Dtos.cs
│   ├── Auth/
│   ├── WEB/                                 # index.html, app.js, styles.css
│   └── Properties/launchSettings.json       # http://localhost:5280
│
├── Diagrams/
├── TravelGuide.sql
├── SCAN_HISTORY_IMPLEMENTATION_NOTES.md
└── TravelGuide_PRD.docx
```

---

## 🚀 Hướng dẫn cài đặt và chạy

### Yêu cầu hệ thống
- Visual Studio 2022 (v17.12 trở lên) hoặc VS Code + .NET SDK
- **.NET 9 SDK**
- Workload **.NET Multi-platform App UI development** (khi dùng Visual Studio)
- Android Emulator API 24+ (theo `csproj`; khuyến nghị API 33–34)

### Các bước thực hiện

**1. Tải mã nguồn**
```
https://github.com/hoangtri22/TravelGuide
```
Nhấn **Code → Download ZIP** → Giải nén ra thư mục

**2. Mở dự án**
- Visual Studio: **Open a project or solution** → mở `TravelGuide.sln`
- Đảm bảo thư mục làm việc khi chạy lệnh terminal là thư mục chứa `TravelGuide.sln` và hai project con `TravelGuide/`, `TravelGuide.AdminWeb/`

**3. Restore packages**
- Visual Studio tự restore NuGet
- Hoặc: `dotnet restore`

**4. Chạy ứng dụng**
- **Mapbox:** tạo `TravelGuide/Resources/Raw/mapbox_token.secret.txt` (một dòng token `pk…` từ [Mapbox](https://account.mapbox.com/access-tokens/)) — file đã `.gitignore`, **không commit**; sau đó **Clean + Rebuild**
- **Startup project**: `TravelGuide` (không chọn AdminWeb khi chạy app điện thoại/emulator)
- Chọn **Android Emulator** (hoặc máy thật), nhấn **F5**

> ⚠️ **Lưu ý:** Cấp quyền **vị trí** để bản đồ và geofence hoạt động đúng.

### Cấu hình Android Emulator khuyến nghị (ổn định)
- Device: `Pixel 5`
- Image: `Android 14 (API 34) - x86_64 - Google APIs`
- Cold Boot lần đầu
- Kiểm tra ADB:
  - `adb kill-server`
  - `adb start-server`
  - `adb devices` → trạng thái `device`

### Chạy Web Admin
```bash
cd <duong-dan-toi-thu-muc-TravelGuide>
dotnet run --project "TravelGuide.AdminWeb/TravelGuide.AdminWeb.csproj" --launch-profile http
```
- URL: **`http://localhost:5280`**
- Mặc định: **`admin` / `admin123`**
- Demo owner:  **`owner_oc_oanh`**, **`owner_oc_dao_2`**, **`owner_sui_cao_tan_tong_loi`**, **`owner_lau_bo_khu_nha_chay`**, **`owner_oc_vu`**, **`owner_oc_linh`** — cùng mật khẩu **`VkQuan@123`** (hash được đồng bộ mỗi lần khởi động Admin Web)
- Nếu build báo **file .exe đang bị khóa**: đang có tiến trình `TravelGuide.AdminWeb` chạy — tắt cửa sổ `dotnet run` cũ hoặc `taskkill /F /IM TravelGuide.AdminWeb.exe`, rồi chạy lại
- **Quản lý tài khoản**: admin có thể **khóa / mở khóa** user (trừ tài khoản `admin`); user bị khóa không đăng nhập được

### Chạy Tourist API (cho app du khách)
```bash
cd <duong-dan-toi-thu-muc-TravelGuide>
dotnet run --project "TravelGuide.API/TravelGuide.API.csproj"
```
- Swagger: `http://localhost:5096/swagger` (tuỳ launch settings)
- Cấu hình giá mô phỏng: `TravelGuide.API/appsettings.json` (`TouristPricing`)

## 🔄 Luồng hoạt động

Khởi động app
    → Chọn ngôn ngữ, tiền tệ (MainPage)
    → Nhấn "Tiếp tục" → Tải/đồng bộ danh sách địa điểm (API + cache SQLite)
    → HomePage — danh sách, tìm kiếm, lối tới Bản đồ / Âm thanh / Gần đây

Khi mở MapPage
    → GPS cập nhật vị trí; WebView Mapbox hiển thị POI
    → GeofenceEngine so khớp vị trí với POI (debounce + priority + cooldown)
    → Phát audio URL hoặc TTS; MiniPlayer hiển thị đáy màn hình

Khi đổi ngôn ngữ
    → `AppLanguage.OnLanguageChanged` → reload chrome + dữ liệu theo ngôn ngữ
    → LocalizationResourceManager cập nhật chuỗi UI từ `.resx`

## 📝 Ghi chú

- Ứng dụng tập trung khu vực **Phố Ẩm Thực Vĩnh Khánh, Quận 4, TP. HCM**
- Dữ liệu: seed `extra_places.json` + quản lý qua **Admin Web** (CRUD POI, duyệt, bản dịch, export JSON cho app)
