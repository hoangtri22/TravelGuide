# Top 5 POI / Visit History: vì sao còn dính POI đã xóa

## Vấn đề

Sau khi xóa POI ở admin, một số khu vực thống kê vẫn còn hiện dữ liệu POI cũ:

- Heatmap / doanh thu theo POI
- Biểu đồ `Lượt truy cập theo ngày` (Top 5 POI)
- Bảng `Visit history`

Nguyên nhân chính: dữ liệu log trong `TouristPoiQrScanLog` vẫn còn, và một số truy vấn trước đây vẫn lấy các dòng log này dù POI đã bị xóa khỏi bảng `Poi`.

## Nguyên nhân kỹ thuật

Trong `TravelGuide.AdminWeb/Data/TravelGuideDb.cs`, các truy vấn thống kê/lịch sử trước đây:

- tổng hợp trực tiếp từ `TouristPoiQrScanLog`, hoặc
- dùng `LEFT JOIN Poi`

`LEFT JOIN` cho phép giữ lại bản ghi log mồ côi (PoiId không còn trong `Poi`), nên UI vẫn có thể hiển thị tên/ID cũ trong chart và bảng.

## Đã sửa

Đã cập nhật truy vấn để chỉ lấy log của POI còn tồn tại:

1. `GetPoiQrScanRevenueByPoiAsync()`
   - thêm `INNER JOIN Poi p ON p.Id = l.PoiId`
   - chỉ thống kê doanh thu/scan cho POI còn trong bảng `Poi`

2. `GetPoiQrScanTotalCountAsync()`
   - thêm `INNER JOIN Poi p ON p.Id = l.PoiId`
   - tổng scan không còn tính bản ghi mồ côi

3. `GetTouristVisitHistoryAsync()` (nhánh non-SQL Server)
   - đổi `LEFT JOIN Poi p` -> `INNER JOIN Poi p`

4. `GetTouristVisitHistorySqlServerAsync()` (nhánh SQL Server)
   - đổi `LEFT JOIN dbo.Poi p` -> `INNER JOIN dbo.Poi p`

Kết quả: heatmap/revenue, chart Top 5 POI, và bảng visit history đều tự động loại POI đã xóa.

## Vì sao không cần xóa log ngay

Không bắt buộc phải xóa log lịch sử vật lý trong `TouristPoiQrScanLog`.

- Dữ liệu log vẫn có thể giữ để audit.
- UI thống kê đã được lọc bằng `INNER JOIN` nên không còn hiển thị POI đã xóa.

Nếu muốn dọn DB sạch hơn, có thể xóa log mồ côi bằng SQL ở bước bảo trì riêng.

## Cách kiểm tra lại

1. Hard reload trang admin (`Ctrl + F5`).
2. Vào tab dữ liệu du khách/heatmap.
3. Ở `Visit history`, bấm `Mặc định` hoặc `Áp dụng` để gọi lại API.
4. Xác nhận POI đã xóa không còn xuất hiện trong:
   - chart Top 5,
   - bảng visit history,
   - heatmap summary/table.

## (Tùy chọn) SQL dọn log mồ côi

### SQL Server

```sql
DELETE l
FROM dbo.TouristPoiQrScanLog l
LEFT JOIN dbo.Poi p ON p.Id = l.PoiId
WHERE p.Id IS NULL;
```

### SQLite

```sql
DELETE FROM TouristPoiQrScanLog
WHERE PoiId NOT IN (SELECT Id FROM Poi);
```

