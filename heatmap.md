# Heatmap admin vs geofence app (`TotalAccessCount`)

Tài liệu này mô tả **hai luồng khác nhau**: (1) bản đồ nhiệt trên **Admin Web**, và (2) cách app **MAUI** chọn POI khi nhiều vùng bán kính chồng nhau —   kể cả nguồn **`TotalAccessCount`**.

---

## 1. App MAUI — chọn POI trong vùng chồng (`GeofenceEngine`)

### Thư mục / file

| Thành phần | Đường dẫn |
|-------------|-----------|
| Logic chọn POI + phát audio geofence | `TravelGuide/GeofenceEngine.cs` |
| Model POI (có `TotalAccessCount`) | `TravelGuide/Models/TouristPlace.cs` |
| Tải POI từ API, map JSON → entity | `TravelGuide/DatabaseService.cs` (`TryGetFromApiAsync`, `MapFromApi`, DTO nội bộ `ApiPoi`) |
| SQLite cục bộ + cột `TotalAccessCount` | `TravelGuide/DatabaseService.cs` (`GetLocalDbAsync` — `ALTER TABLE` nếu thiếu cột) |
| Banner “gần bạn” (sort tương tự) | `TravelGuide/MapPage.xaml.cs` (`UpdateNearbyBanner`) |
| Lọc POI lân cận | `TravelGuide/DatabaseService.cs` (`GetNearbyPlacesAsync`) |

### Luồng `ProcessLocationAsync`

1. `GetPlacesAsync()` trả về danh sách POI đã sync (từ API hoặc cache/fallback).
2. Lọc các POI mà khoảng cách GPS tới **tâm POI** ≤ **`Radius`** (mét).
3. Sắp xếp để chọn **một** POI:
   - **`TotalAccessCount` giảm dần** — tổng số dòng log truy cập POI trên server (xem mục 2).
   - **Bằng nhau** → **`Priority` giảm dần** (số admin cấu hình trên POI).
   - **Vẫn bằng** → **`DistM` tăng dần** (gần tâm POI hơn = ưu tiên).
4. `FirstOrDefault()` là POI “thắng” vòng đó.
5. Audio / TTS chỉ theo POI đó, sau **debounce** và **cooldown** (không đổi).

```csharp
// TravelGuide/GeofenceEngine.cs — ý tưởng
.OrderByDescending(x => x.P.TotalAccessCount)
.ThenByDescending(x => x.P.Priority)
.ThenBy(x => x.DistM)
```

---

## 2. API — `TotalAccessCount` lấy từ đâu

### Thư mục / file

| Thành phần | Đường dẫn |
|-------------|-----------|
| Endpoint public cho app | `TravelGuide.API/Program.cs` — `GET /api/public/pois` |
| Đọc POI + đếm log | `TravelGuide.API/Data/PoiPublicReader.cs` (`GetPublishedAsync`, `GetPublishedByIdAsync`) |
| DTO trả về | `TravelGuide.API/Models/TouristDtos.cs` — `PublicPoiDto` (tham số `TotalAccessCount`) |
| Fallback không có SQL Server | `TravelGuide.API/Program.cs` — `LoadPublicPoisAsync()` (đọc `extra_places.json`, **`TotalAccessCount = 0`**) |

### SQL (ý tưởng)

- Bảng **`dbo.Poi`**: POI đã publish.
- Bảng **`dbo.TouristPoiQrScanLog`**: mỗi lần app ghi log (QR mở quán, GPS trong vùng `poi_gps_inside`, …) là **một dòng** gắn `PoiId`.
- **`TotalAccessCount`** = `COUNT(*)` theo `PoiId` (LEFT JOIN; POI chưa có log → 0).

App gọi `GET /api/public/pois?lang=...`, JSON dùng camelCase → **`totalAccessCount`**; `DatabaseService` deserialize vào `ApiPoi.TotalAccessCount` rồi `TouristPlace.TotalAccessCount`.

---

## 3. Admin Web — heatmap (không phải `TotalAccessCount` của app)

### Thư mục / file

| Thành phần | Đường dẫn |
|-------------|-----------|
| Tab Heatmap, Leaflet + `leaflet.heat` | `TravelGuide.AdminWeb/WEB/index.html` (section heatmap) |
| Tải POI, dashboard scan, vẽ map | `TravelGuide.AdminWeb/WEB/app.js` — `refreshHeatmapTab`, `loadPois`, các helper `heatmap*` |
| API dashboard (doanh thu / scan theo POI) | `TravelGuide.AdminWeb/Program.cs` — endpoint tương ứng mà `app.js` gọi (ví dụ `/api/tourists/poi-scan-dashboard`) |
| POI + SQLite admin | `TravelGuide.AdminWeb/Data/TravelGuideDb.cs` |

### Luồng heatmap (rút gọn)

1. `loadPois()` → `GET /api/pois` (danh sách POI admin, có lat/lng/`Radius`).
2. `GET /api/tourists/poi-scan-dashboard` → bảng thống kê theo POI (scan, GPS cửa sở, QR ngày UTC, …).
3. **Lớp nhiệt** (`L.heatLayer`): điểm nóng theo **cường độ log** trong dashboard, **pixel** blur/radius — **không** phải vòng tròn mét của `Radius` POI.
4. **Vòng `L.circle`**: hiển thị **bán kính geofence (mét)** từ field `Radius` của từng POI để admin nhìn chồng vùng trên bản đồ.
5. POI chưa có dòng thống kê “có hoạt động” vẫn có thể được vẽ marker + vòng (logic đã từng mở rộng trong `app.js`).

**Kết luận:** màu nóng trên web là **trực quan thống kê**; app geofence dùng **`TotalAccessCount` từ TravelGuide.API** (`/api/public/pois`), không đọc trực tiếp heatmap admin.

---

## 4. So sánh nhanh

| | Admin heatmap | App geofence |
|--|----------------|--------------|
| Nguồn dữ liệu chính | Dashboard scan + `/api/pois` | `/api/public/pois` (+ cache SQLite) |
| “Nóng” trên map | `leaflet.heat` + tổng hợp log | Không dùng |
| Chọn 1 POI khi chồng bán kính | Không áp dụng (chỉ xem bản đồ) | `GeofenceEngine`: `TotalAccessCount` → `Priority` → `DistM` |
| Bán kính mét | `L.circle` (minh họa) | `TouristPlace.Radius` + GPS |

---

## 5. Khi debug

- App hiển thị **0** cho mọi `TotalAccessCount`: kiểm tra base URL trỏ **`TravelGuide.API`** (port **5096**), không nhầm **Admin Web** (**5280**); fallback JSON không có trường đếm → 0.
- Đồng bộ lại POI: `DatabaseService` cache ~30 giây hoặc `SeedDataAsync` / mở lại app tùy luồng bạn đang dùng.
- SQL: đảm bảo `TouristPoiQrScanLog` đã được tạo (API `TouristDb.InitializeAsync` khi khởi động API).

---

*Tài liệu phản ánh code tại thời điểm tạo file; nếu refactor, cập nhật lại đường dẫn / endpoint cho khớp repo.*
