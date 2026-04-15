# Scan History Implementation Notes

Tai lieu nay tong hop nhung thay doi da duoc trien khai de ho tro yeu cau:
"Khach da quet QR thi khong can quet lai, chi can xem trong lich quet de mo lai noi dung."

## Muc tieu

- Luu lich su quet QR POI theo tai khoan du khach.
- Cho phep du khach mo lai chi tiet POI tu "Lich quet" ma khong can quet lai QR.
- Khong thay doi luong chinh dang chay, chi bo sung endpoint va man hinh.

## Thay doi ben API (`TravelGuide.API`)

### 1) Them DTO tra ve lich quet
- File: `TravelGuide.API/Models/TouristDtos.cs`
- Them record:
  - `MyPoiScanHistoryItemDto(int PoiId, string PoiNameVi, string EventType, decimal AmountVnd, DateTime LastScannedAtUtc)`

### 2) Them truy van lich quet moi nhat theo tung POI
- File: `TravelGuide.API/Data/TouristDb.cs`
- Them method:
  - `GetMyPoiScanHistoryAsync(int touristUserId, int take = 200)`
- Logic:
  - Doc du lieu tu `dbo.TouristPoiQrScanLog`
  - Dung `ROW_NUMBER() OVER (PARTITION BY PoiId ORDER BY CreatedAtUtc DESC)` de lay dong moi nhat cho moi POI
  - Gioi han ket qua toi da 200 ban ghi (toi da 500 neu truyen tham so lon hon)

### 3) Them endpoint cho app lay lich quet
- File: `TravelGuide.API/Program.cs`
- Them endpoint:
  - `GET /api/tourist/pois/my-scan-history`
- Yeu cau:
  - Co Bearer token
  - Xac dinh user qua `AuthHelper.Authenticate(...)`
  - Tra ve danh sach lich quet cua user dang dang nhap

## Thay doi ben app MAUI (`TravelGuide`)

### 1) Them API client lay lich quet
- File: `TravelGuide/TouristAuthService.cs`
- Them:
  - `GetMyScanHistoryAsync()`
  - Model `MyScanHistoryRow`
- Ket qua:
  - Tra ve danh sach POI da quet
  - Neu chua dang nhap thi tra ve thong bao phu hop

### 2) Them man hinh lich quet
- Files:
  - `TravelGuide/QrScanHistoryPage.xaml`
  - `TravelGuide/QrScanHistoryPage.xaml.cs`
- Chuc nang:
  - Hien danh sach lich quet
  - Bam vao 1 muc se mo `PlaceDetailPage`
  - Neu POI khong con trong danh sach cong khai thi hien thong bao

### 3) Dang ky route va DI
- File: `TravelGuide/AppShell.xaml.cs`
  - Dang ky `Routing.RegisterRoute(nameof(QrScanHistoryPage), typeof(QrScanHistoryPage));`
- File: `TravelGuide/MauiProgram.cs`
  - Dang ky `builder.Services.AddTransient<QrScanHistoryPage>();`

### 4) Cap nhat dieu huong giao dien
- File: `TravelGuide/HomePage.xaml`
  - Doi tab cuoi thanh dieu huong thanh "Lich quet"
- File: `TravelGuide/HomePage.xaml.cs`
  - Them ham mo trang `QrScanHistoryPage`
- File: `TravelGuide/QrScannerPage.xaml`
  - Them nut "Lich quet" ngay tren man quet de mo nhanh
- File: `TravelGuide/QrScannerPage.xaml.cs`
  - Them su kien `OnOpenScanHistoryClicked`

### 5) Bo sung da ngon ngu (ResX)
- Files:
  - `TravelGuide/Resources/AppResources.resx`
  - `TravelGuide/Resources/AppResources.vi.resx`
  - `TravelGuide/Resources/AppResources.ja.resx`
  - `TravelGuide/Resources/AppResources.ko.resx`
  - `TravelGuide/Resources/AppResources.zh.resx`
- Them cac key:
  - `NavTabScanHistory`
  - `ScanHistoryTitle`
  - `ScanHistoryEmpty`
  - `ScanHistoryLoginRequired`
  - `ScanHistoryPlaceMissing`

## Kiem tra build

- MAUI app: build thanh cong tren target Windows.
- API: logic compile OK; co the gap loi copy file neu process `TravelGuide.API` dang chay va lock `bin/Debug`.
- Giai phap khi gap lock:
  - Dung process API dang chay roi build lai, hoac
  - Build ra thu muc output tam (da verify duoc).

## Tac dong toi project

- Chi bo sung code va file tai lieu.
- Khong xoa luong cu.
- Viec them file `.md` nay khong anh huong runtime, project van chay binh thuong.
