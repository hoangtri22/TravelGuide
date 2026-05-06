File environment.txt (AndroidEnvironment)
============================================

- Dong goi vao APK: bien TRAVELGUIDE_API_BASE_URL = URL TravelGuide.API tren may dev (Wi-Fi).
- Vi du: TRAVELGUIDE_API_BASE_URL=http://192.168.1.115:5096
- Doi IP: sua file environment.txt roi build lai APK / cai lai app.

- Moi lan `dotnet build` / build Android tren Windows: script tu ghi lai IP LAN vao `Resources/Raw/device_endpoints.json` (uu tien card co default gateway). Doi Wi-Fi -> build lai APK hoac nhap URL tren man Dang nhap.
- Khong can build lai khi chi doi Wi-Fi: tren man Dang nhap / Dang ky co o "API server" — dien http://IP_MAY_TINH:5096 roi dang nhap.

Ghi chu:
- May ao Android (emulator): app van dung 10.0.2.2 trong code; file nay khong anh huong emulator.
- May that: neu truoc do da luu sai 10.0.2.2 trong Preferences, phien ban moi se bo qua va dung URL trong file nay.
