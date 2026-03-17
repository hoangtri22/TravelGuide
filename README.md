📍 Travel Guide: Ứng dụng Hướng dẫn Du lịch

** Đồ án môn học: Ngôn ngữ lập trình C# (841423)

*Lớp: DCT123C2

Thông tin nhóm - Nhóm 8:
1. 3123411210 - Hồ Ngọc Phương Nhi
2. 3123411311 - Nguyễn Hoàng Trí

Ứng dụng di động được xây dựng bằng .NET MAUI Native và giúp du khách khám phá những địa điểm mới thông qua chế độ thuyết minh tự động và bản đồ tương tác hoạt động liên tục trong nền ứng dụng.

Các tính năng chính:

🗺️ Bản đồ tương tác (Mapbox): Hiển thị các POI (Point of Interest) trực quan trên nền bản đồ Mapbox API, phân loại màu sắc cho địa điểm tham quan du lịch và ẩm thực.

🎙️ Thuyết minh tự động (TTS): Sử dụng công nghệ Text-to-Speech để tự động thuyết minh thông tin địa danh khi người dùng đến gần.

🔄 Chạy ngầm (Background Service): Hỗ trợ theo dõi vị trí và phát thuyết minh ngay cả khi ứng dụng đã thoát ra màn hình chính (Tối ưu cho Android 14+).

📍 Định vị POI gần nhất: Tự động tính toán và highlight địa điểm du lịch ở gần vị trí hiện tại của người dùng nhất.

🔊 Điều khiển thông minh: Cho phép nghe lại thuyết minh hoặc dừng phát nhanh chóng để tiết kiệm tài nguyên hệ thống.

📍 Thuật toán tự động xác định vị trí gần nhất với người dùng, tự động tính toán khoảng cách thực tế từ người dùng đến các vị trí.

🛠️ Quốc tế hóa (i18n): Tìm kiếm ngôn ngữ và tiền tệ có sẵn ở tất cả các quốc gia trên toàn cầu thông qua CultureInfo.

🛠️ Công nghệ được sử dụng:

Khung ứng dụng - .NET 9.0 (MAUI) (Ứng dụng gốc - không sử dụng thư viện bên ngoài)

Giao diện bản đồ - Mapbox GL JS được nhúng trong WebView.

Nhân Android - Dịch vụ nền trước (Vị trí và Đồng bộ dữ liệu)

Truyền thông dữ liệu - MessagingCenter (Hệ thống gốc)

Lưu trữ - Microsoft.Maui.Storage.Preferences.

🚀 Hướng dẫn cài đặt và Chạy dự án: 
Để chạy thử ứng dụng trên máy tính của bạn, hãy làm theo các bước đơn giản sau:

1. Tải mã nguồn
Truy cập vào liên kết GitHub https://github.com/hoangtri22/demo-git.
Nhấn vào nút Code (màu xanh).
Chọn Download ZIP.
Sau khi tải về, hãy Giải nén (Extract) tệp tin ra một thư mục trên máy tính.

2. Mở dự án
Khởi động Visual Studio 2022 (v17.12 trở lên).
Chọn Open a project or solution.
Tìm đến thư mục vừa giải nén và chọn file TravelGuide.sln.

3. Cấu hình môi trường
Đảm bảo bạn đã cài đặt bộ công cụ .NET Multi-platform App UI development trong Visual Studio Installer.
Kiểm tra xem máy đã có .NET 9 SDK chưa.

4. Triển khai và Chạy (Deploy)
Chọn thiết bị mục tiêu là Android Emulator (khuyên dùng API 30 - 34).
Nhấn nút Run (F5) hoặc biểu tượng tam giác xanh để bắt đầu quá trình Build.

Lưu ý: Khi ứng dụng khởi động lần đầu, hãy chấp nhận các yêu cầu về Quyền vị trí (Location Permission) để tính năng bản đồ và thuyết minh hoạt động chính xác.


