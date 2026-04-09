# 📍 Travel Guide — Ứng dụng khám phá phố ẩm thực Vĩnh Khánh

> **Đồ án môn học: Ngôn ngữ lập trình C# (841423)**
> Lớp: DCT123C2


## 👥 Thông tin nhóm — Nhóm 8

| MSSV       | Họ và Tên          |
|------------|--------------------|
| 3123411210 | Hồ Ngọc Phương Nhi |
| 3123411311 | Nguyễn Hoàng Trí   |


## 📖 Giới thiệu

**Travel Guide** là ứng dụng di động được xây dựng bằng **.NET MAUI Native**, hỗ trợ du khách khám phá **Phố Ẩm Thực Vĩnh Khánh (Quận 4, TP. HCM)** thông qua bản đồ tương tác, thuyết minh tự động bằng giọng nói và hệ thống đa ngôn ngữ.


## ✨ Tính năng chính

### 🗺️ Bản đồ tương tác (WebView + Leaflet/OSM)
- Hiển thị các POI (Point of Interest) trực quan trên bản đồ trong WebView
- Phân loại màu sắc: đỏ cho địa điểm nhỏ, xanh cho khu vực lớn
- Tự động highlight địa điểm gần nhất khi người dùng đến gần
- Popup thông tin địa điểm khi nhấn vào marker

### 🎙️ Thuyết minh tự động (Text-to-Speech)
- Sử dụng `Microsoft.Maui.Media.TextToSpeech` để tự động đọc thông tin địa danh
- Hỗ trợ hàng đợi phát (Queue) — phát tuần tự nhiều địa điểm
- Chức năng Skip, Stop, và Shuffle
- Giọng đọc tự động chọn đúng theo ngôn ngữ hiện tại (vi/en/ja/ko/zh)

### 📡 Geofence Engine
- Theo dõi vị trí GPS liên tục qua `GpsBackgroundService`
- Tự động phát thuyết minh khi người dùng bước vào vùng bán kính của POI
- Cơ chế **Debounce** (3 giây) tránh kích hoạt khi chỉ đi ngang qua
- Cơ chế **Cooldown** (60 giây) tránh phát lặp lại liên tục
- Gửi sự kiện `OnPoiEntered`, `OnPoiTriggered`, `OnPoiExited` ra UI

### 🔊 Mini Player
- Hiển thị cố định ở đáy màn hình khi đang phát thuyết minh
- Hoạt động liên tục khi điều hướng giữa các trang
- Nút Stop và Skip ngay trên thanh player

### 🌐 Đa ngôn ngữ (i18n)
- Hỗ trợ 5 ngôn ngữ: 🇻🇳 Tiếng Việt, 🇬🇧 English, 🇯🇵 日本語, 🇰🇷 한국어, 🇨🇳 中文
- Giao diện dùng `.resx` + LocalizationResourceManager
- Nội dung địa điểm được tự dịch qua **MyMemory API** và lưu vào backend/SQLite
- Chọn ngôn ngữ bằng grid button cờ quốc gia — không cần gõ tìm kiếm
- Bản dịch được lưu lại, không dịch lại khi mở app lần sau

### 💱 Đơn vị tiền tệ
- Tìm kiếm và chọn tiền tệ từ toàn bộ danh sách `CultureInfo` hệ thống
- Lưu lựa chọn qua `Preferences`

---

## 🛠️ Công nghệ sử dụng

| Thành phần      | Công nghệ                                      |
|-----------------|------------------------------------------------|
| Framework       | .NET 9.0 MAUI (Native)                         |
| Bản đồ          | WebView + Leaflet + OpenStreetMap              |    
| Text-to-Speech  | Microsoft.Maui.Media.TextToSpeech              |
| Cơ sở dữ liệu   | SQLite-net-pcl                                 |
| Dịch thuật      | MyMemory API                                   |
| Đa ngôn ngữ UI  | LocalizationResourceManager.Maui + .resx       |
| Messaging       | CommunityToolkit.Mvvm (WeakReferenceMessenger) |
| Lưu trữ cài đặt | Microsoft.Maui.Storage.Preferences             |
| Định vị         | Microsoft.Maui.Devices.Sensors.Geolocation     |

---

## 📁 Cấu trúc project
```
TravelGuide/
├── TravelGuide.AdminWeb/              # Web admin (POI/Audio/Tài khoản/Bản dịch)
├── PRD_TravelGuide_Standard.md        # PRD chuẩn dạng Markdown
├── TravelGuide_PRD.docx               # PRD chuẩn dạng Word
│
├── Models/
│   ├── LocationMessage.cs           # Message GPS dùng cho WeakReferenceMessenger
│   └── TouristPlace.cs              # Model địa điểm, computed Name/Description theo ngôn ngữ
│
├── Platforms/
│   ├── Android/
│   │   ├── Resources/
│   │   │   └── AndroidManifest.xml  # Khai báo quyền GPS, Internet
│   │   ├── LocationService.cs       # Foreground Service theo dõi GPS nền (Android)
│   │   ├── MainActivity.cs
│   │   └── MainApplication.cs
│   ├── iOS/
│   ├── MacCatalyst/
│   ├── Tizen/
│   └── Windows/
│
├── Resources/
│   ├── AppIcon/
│   ├── Fonts/
│   ├── Images/
│   ├── Raw/
│   │   ├── AboutAssets.txt
│   │   └── extra_places.json        # Dữ liệu địa điểm gốc (seed lần đầu)
│   ├── Splash/
│   ├── Styles/
│   ├── AppResources.resx            # Chuỗi giao diện tiếng Anh (default)
│   ├── AppResources_vi.resx         # Tiếng Việt
│   ├── AppResources_ja.resx         # Tiếng Nhật
│   ├── AppResources_ko.resx         # Tiếng Hàn
│   └── AppResources_zh.resx         # Tiếng Trung
│
├── App.xaml
├── AppLanguage.cs                   # Quản lý ngôn ngữ hiện tại toàn app
├── AppShell.xaml
├── AudioPage.xaml / .cs             # Danh sách audio, phát tất cả, shuffle
├── DatabaseService.cs               # SQLite: CRUD địa điểm, seed data
├── GeofenceEngine.cs                # So sánh vị trí với POI, debounce, cooldown
├── GpsBackgroundService.cs          # Theo dõi GPS liên tục, gửi LocationMessage
├── HomePage.xaml / .cs              # Danh sách địa điểm, tìm kiếm, điều hướng
├── MainPage.xaml / .cs              # Chọn ngôn ngữ, tiền tệ, khởi động app
├── MapPage.xaml / .cs               # Bản đồ WebView (Leaflet/OSM), geofence, GPS
├── MauiProgram.cs                   # DI container, khởi tạo services
├── MiniPlayerView.xaml / .cs        # Thanh mini player cố định đáy màn hình
├── NarrationEngine.cs               # Quản lý TTS queue, skip, stop
├── PlaceDetailPage.xaml / .cs       # Chi tiết địa điểm, nghe thuyết minh
└── TranslationService.cs            # Gọi MyMemory API để dịch nội dung
```

---

## 🚀 Hướng dẫn cài đặt và chạy

### Yêu cầu hệ thống
- Visual Studio 2022 (v17.12 trở lên)
- .NET 9 SDK
- Workload **.NET Multi-platform App UI development**
- Android Emulator API 30–34 (khuyến nghị Pixel 5 API 34)

### Các bước thực hiện

**1. Tải mã nguồn**
```
https://github.com/hoangtri22/demo-git
```
Nhấn **Code → Download ZIP** → Giải nén ra thư mục

**2. Mở dự án**
- Khởi động Visual Studio 2022
- Chọn **Open a project or solution**
- Mở file `TravelGuide.sln`

**3. Restore packages**
- Visual Studio sẽ tự động restore NuGet packages
- Nếu không, chạy: `dotnet restore`

**4. Chạy ứng dụng**
- Chọn thiết bị: **Android Emulator** (API 30–34)
- Nhấn **F5** hoặc nút Run ▶

> ⚠️ **Lưu ý:** Khi khởi động lần đầu, hãy **chấp nhận quyền vị trí (Location Permission)** để tính năng bản đồ và thuyết minh tự động hoạt động đúng.

### Cấu hình Android Emulator khuyến nghị (ổn định)
- Device: `Pixel 5`
- Image: `Android 14 (API 34) - x86_64 - Google APIs`
- Cold Boot lần đầu
- Kiểm tra ADB trước khi chạy:
  - `adb kill-server`
  - `adb start-server`
  - `adb devices` phải thấy trạng thái `device`

### Chạy Web Admin
```bash
dotnet run --project "TravelGuide.AdminWeb/TravelGuide.AdminWeb.csproj"
```
- Mở URL mặc định: `http://localhost:5280`
- Tài khoản mặc định: `admin / admin123`

## 🔄 Luồng hoạt động

Khởi động app
    → Chọn ngôn ngữ (MainPage) — grid button 5 cờ quốc gia
    → Nhấn "Tiếp tục" → App gọi API để tải/dịch nội dung theo ngôn ngữ đã chọn
    → Vào HomePage — danh sách địa điểm theo ngôn ngữ đã chọn

Khi mở MapPage
    → GpsBackgroundService bắt đầu theo dõi GPS mỗi 5 giây
    → GeofenceEngine so sánh vị trí với từng POI
    → Khi vào vùng POI → debounce 3s → phát thuyết minh TTS
    → MiniPlayer hiển thị ở đáy màn hình

Khi đổi ngôn ngữ
    → AppLanguage.OnLanguageChanged fired
    → Tất cả trang tự reload nội dung
    → LocalizationResourceManager cập nhật giao diện
## 📝 Ghi chú

- Ứng dụng tập trung vào khu vực **Phố Ẩm Thực Vĩnh Khánh, Quận 4, TP. HCM**
- Dữ liệu địa điểm được seed từ `extra_places.json` và đồng bộ qua web admin
- Bản dịch được cache/lưu để tránh dịch lặp
- Tài liệu PRD chuẩn: `PRD_TravelGuide_Standard.md` và `TravelGuide_PRD.docx`
