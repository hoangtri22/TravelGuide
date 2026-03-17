using TravelGuide.Models;

namespace TravelGuide;

public static class DataService
{
    public static List<TouristPlace> GetPlaces()
    {
        return new List<TouristPlace>
        {
            new TouristPlace
            {
                Name = "Nhà Thờ Đức Bà Sài Gòn",
                Description = "Chào mừng bạn đến với Vương cung thánh đường Chính tòa Đức Bà Sài Gòn – biểu tượng bất biến của thành phố hơn 300 năm tuổi. Công trình là một kiệt tác kiến trúc Roman pha trộn Gothic tuyệt đẹp, được khánh thành vào năm 1880. Toàn bộ vật liệu xây dựng từ gạch Marseille đỏ rực đến hệ thống kính màu đều được vận chuyển trực tiếp từ Pháp sang. Trải qua gần 150 năm, sắc đỏ của gạch vẫn nguyên vẹn như minh chứng cho sự trường tồn của lịch sử giữa lòng đô thị hiện đại.",
                Latitude = 10.7797,
                Longitude = 106.6990,
                ImageUrl = "nhathoducba.jpg",
                Radius = 200
            },
            new TouristPlace
            {
                Name = "Bưu Điện Trung Tâm Thành Phố",
                Description = "Trước mắt bạn là Bưu điện Trung tâm Sài Gòn, công trình mang phong cách kiến trúc Phục Hưng độc đáo được xây dựng từ năm 1886. Bước vào bên trong, bạn sẽ choáng ngợp trước hệ thống vòm cung cao vút gợi nhớ đến các nhà ga cổ điển tại châu Âu, cùng hai bản đồ lịch sử được vẽ tay tỉ mỉ trên tường. Nơi đây không chỉ là một bưu điện đang hoạt động mà còn là sợi dây kết nối giữa quá khứ lẫy lừng và nhịp sống sôi động của Hòn ngọc Viễn Đông.",
                Latitude = 10.7799,
                Longitude = 106.6999,
                ImageUrl = "buudienthanhpho.jpg",
                Radius = 150
            },
            new TouristPlace
            {
                Name = "Dinh Độc Lập",
                Description = "Bạn đang đứng trước Dinh Độc Lập, di tích quốc gia đặc biệt mang đậm dấu ấn lịch sử dân tộc. Công trình là sự kết hợp hài hòa giữa kiến trúc hiện đại phương Tây và triết lý phương Đông do kiến trúc sư Ngô Viết Thụ chắp bút. Với khuôn viên xanh mát rộng 12 héc-ta, nơi đây ghi dấu thời khắc lịch sử trưa ngày 30 tháng 4 năm 1975, khi chiếc xe tăng 843 húc đổ cổng chính, mở ra một chương mới cho sự thống nhất đất nước.",
                Latitude = 10.7770,
                Longitude = 106.6953,
                ImageUrl = "dinhdoclap.jpg",
                Radius = 200
            },
            new TouristPlace
            {
                Name = "Phở Hòa Pasteur",
                Description = "Để hiểu về tâm hồn Sài Gòn, không thể bỏ qua Phở Hòa Pasteur – một 'huyền thoại' ẩm thực ra đời từ những năm 1960. Bí quyết nằm ở nước dùng thanh ngọt, đậm đà theo lối phở Bắc nhưng đã được tinh chỉnh khéo léo để phù hợp với khẩu vị phóng khoáng của người miền Nam. Một tô phở bốc khói nghi ngút kèm theo đĩa quẩy giòn tan sẽ giúp bạn cảm nhận trọn vẹn nét tinh tế của văn hóa ẩm thực đường phố Việt Nam.",
                Latitude = 10.7876,
                Longitude = 106.6913,
                ImageUrl = "phohoa.jpg",
                Radius = 150
            },
            new TouristPlace
            {
                Name = "Bánh Mì Huỳnh Hoa",
                Description = "Được mệnh danh là 'ổ bánh mì đắt nhất' nhưng cũng đáng thử nhất Sài Gòn, Huỳnh Hoa nổi tiếng với ổ bánh nặng gần nửa ký, dày đặc các loại thịt nguội, chả lụa và đặc biệt là lớp pate béo ngậy bí truyền. Mỗi ổ bánh mì tại đây mang đến một trải nghiệm ẩm thực bùng nổ, đúng chất nồng hậu và sảng khoái của con người vùng đất này.",
                Latitude = 10.7725,
                Longitude = 106.6932,
                ImageUrl = "banhmihuynhhoa.jpg",
                Radius = 100
            }
        };
    }
}