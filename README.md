# Lăng Kính Hư Không · Offline TTS Studio v3.0

**Phiên bản v3.2:** bổ sung nghe / tải / xóa MP3 và ghép playlist theo thứ tự đã chọn.

Ứng dụng Blazor Server **.NET 9**, chuyển văn bản tiếng Việt thành **MP3 offline** bằng Piper và FFmpeg trên **máy chủ Windows**. Source được nâng cấp từ TVT Team; giao diện và logo riêng của **Lăng Kính Hư Không**.

![Lăng Kính Hư Không](wwwroot/images/lang-kinh-hu-khong-logo.png)

## Tính năng đã bổ sung

| Chức năng | Cách hoạt động |
|---|---|
| Thương hiệu | Đổi tên project/solution/namespace, header, favicon, footer, logo |
| Ký tự | **100.000 ký tự** mỗi lần render (gồm xuống dòng); giới hạn JS + C# và SignalR 2 MiB |
| Tốc độ | 0,25; 0,5; 0,75; 1; 1,25; 1,75; 2; **2,5; 3** lần |
| Import | **.txt / .md** UTF-8, file tối đa 5 MB; hiện nội dung ngay trong textarea, không cắt text nếu vượt giới hạn render |
| Chuẩn hóa | Thêm dấu **.** cuối dòng thiếu dấu câu trước khi xuống dòng; giữ ngắt đoạn, không nhân đôi `.!?…;:`; MD loại bớt ký hiệu định dạng thường gặp |
| Dán văn bản | Nút **Ngắt nhịp TTS** để cập nhật textarea; khi render vẫn chuẩn hóa tự động |
| Lịch sử MP3 | Mỗi render thành công có một **Job ID (GUID)**, tên file độc nhất; danh sách theo tab cho phép tải lại file bất kỳ |
| Đồng thời | Mỗi tab render tối đa **1 lượt đang chạy**, nhưng **không giới hạn số lượt tuần tự**, nhiều tab/máy LAN có thể render song song (phụ thuộc CPU/RAM) |
| Tab session | Mỗi tab có ID và secret riêng, `Output/{sessionId}/` riêng, endpoint tải/đóng/touch xác minh khóa |
| Refresh | **F5 vẫn giữ lịch sử**: sessionStorage + tiếp tục phiên trên server; phát hiện tab nhân bản qua BroadcastChannel |
| Đóng tab | `pagehide` gửi beacon; server chờ 12 giây để phân biệt F5 với đóng tab, sau đó xóa thư mục; nếu browser crash và không gửi beacon sẽ dọn sau **45 phút không heartbeat**, theo chu kỳ quét |

## Yêu cầu

- Windows 10/11 hoặc Windows Server, **.NET 9 SDK** (build) hoặc **ASP.NET Core Runtime 9** (chạy bản framework-dependent publish).
- Piper Windows: `Runtime/piper/piper.exe` kèm các DLL/data của engine đang dùng.
- FFmpeg Windows: `Runtime/ffmpeg/ffmpeg.exe`.
- Models giọng `.onnx` và `.onnx.json`, ví dụ `Voices/Nữ/NgocHuyen/voice.onnx` và `voice.onnx.json`.
- Đảm bảo tài khoản chạy .NET/IIS có quyền ghi **Output**, **Temp**; quyền đọc **Runtime**, **Voices**.

> **Lưu ý:** ZIP là source nên **không kèm** engine Piper, FFmpeg hoặc model giọng. Cần sao chép `Runtime` và `Voices` từ bản TVT Team cũ; nếu chưa có các file này, giao diện vẫn mở nhưng không render ra MP3 được.

## Build và chạy trực tiếp

Mở PowerShell tại thư mục source:

```powershell
dotnet --version
dotnet restore .\LangKinhHuKhong.TTS.csproj
dotnet build .\LangKinhHuKhong.TTS.csproj -c Release
dotnet run --project .\LangKinhHuKhong.TTS.csproj --urls "http://0.0.0.0:4110"
```

Trên máy chủ mở `http://localhost:4110`. Máy khác trong cùng mạng LAN mở `http://<IP-MAY-CHU>:4110` (cần mở firewall TCP 4110 và không mở port này ra Internet khi chưa có xác thực người dùng).

Hoặc chạy `Scripts\start-lan.ps1` trong PowerShell.

## Publish Windows (dạng chạy độc lập)

```powershell
dotnet publish .\LangKinhHuKhong.TTS.csproj -c Release -r win-x64 --self-contained true -o .\Publish
# Sau đó chép thư mục Runtime/ và Voices/ của bạn vào Publish/.
cd .\Publish
.\LangKinhHuKhong.TTS.exe --urls "http://0.0.0.0:4110"
```

Không chạy `Set-Location ... .\LangKinhHuKhong.TTS.exe` trên cùng một lệnh. Hãy xuống dòng hoặc dùng `;` để tách lệnh.

Nếu host trên IIS: cài **ASP.NET Core Hosting Bundle 9**, tạo site/app pool với *No Managed Code*, cấu hình NTFS quyền ghi `Output` và `Temp` cho Identity của App Pool. Với site LAN, xem xét bảo vệ bằng VPN/HTTP authentication vì app cho phép render miễn đăng nhập.

## Vị trí file và dọn dẹp

- `Output/{sessionId}/LangKinhHuKhong-yyyyMMdd-HHmmss-{jobId}.mp3`: lịch sử **chỉ lưu tạm** trên máy server; không tạo MP3 trong source C# tĩnh.
- `Temp/jobs/{guid}/`: WAV/tệp concat được dọn trong `finally` của mỗi job.
- `Temp/voice-cache/`: alias đường dẫn ASCII cho giọng nằm trong `Voices/Nữ`, tránh một số lỗi Piper trên Windows. Cache model có thể tái sử dụng.
- F5 vẫn giữ session nếu quay lại server trong thời gian chờ 12 giây, còn đóng tab/Chrome gửi `pagehide` để dọn.
- Chrome Task Manager/Windows tắt cưỡng bức có thể **không gửi beacon**, nên không thể đảm bảo xóa ngay; bộ dọn sẽ xử lý sau 45 phút không heartbeat. Máy chủ tắt đột ngột cũng được thu gom orphan khi ứng dụng chạy trở lại.
- File MP3 đã được người dùng **tải về máy cá nhân** không bị server xóa.

## Kiểm thử

```powershell
node --check .\wwwroot\js\studio.js
node .\Tests\studio-smoke.test.js
dotnet build -c Release
```

Kiểm tra thủ công trên Windows: import 1 file TXT và 1 file MD; chuyển đoạn không dấu thành có dấu; chọn 2,5x hoặc 3x; tạo hai MP3 ở tab A, mở tab B tạo một MP3; xác nhận không xem được lịch sử của nhau; nhấn F5 ở A để kiểm tra lịch sử còn; đóng A, đợi 12–17 giây, xác nhận thư mục Output tương ứng được xóa mà B vẫn hoạt động.

Không nên gửi audio quá nhiều tab hơn khả năng CPU/RAM của server vì các lượt Piper là tiến trình hệ điều hành độc lập.

## v3.2 — Nghe MP3, quản lý thư viện và ghép audio dài

- Trong **MP3 đã tạo**, nhấn **▶ Nghe** để mở trình phát tích hợp có thanh tua, nhấn **↓ Tải** để tải lại và **✕ Xóa** để xóa cả bản ghi lẫn file MP3 của tab trên máy chủ. Nếu MP3 đang được trình duyệt mở, hệ thống có thể yêu cầu dừng phát và thử xóa lại.
- Muốn ghép, tích checkbox từng MP3. Những đoạn không tích sẽ **không** có mặt trong bản ghép. Dùng nút **↑ / ↓** trong danh sách bên dưới để thiết lập thứ tự nối, hoặc **✕** ở danh sách ghép để bỏ một đoạn mà không xóa file nguồn.
- Chọn ít nhất **2 MP3**, nhập **Tên file MP3 sau ghép**, chọn **Ghép & thêm vào danh sách**. Bản ghép được thêm vào lịch sử với **ID mới**, có thể nghe, tải, xóa, hoặc chọn để ghép tiếp. Các file nguồn được giữ nguyên.
- Ghép MP3 thực hiện tại **máy chủ LAN** bằng `Runtime/ffmpeg/ffmpeg.exe` (không upload dữ liệu lên dịch vụ bên ngoài). Phần ghép tái mã hóa âm thanh về **44.1 kHz stereo 192 kbps** để xử lý các nguồn khác sample rate / mono / stereo; chia từng lô tối đa 20 đoạn để tránh vượt giới hạn độ dài dòng lệnh Windows.
- File ghép tải về dùng **tên người dùng đặt** (loại bỏ ký tự không an toàn), còn tên lưu server theo ID bất biến để chống xung đột và truy cập chéo tab. Trạng thái phiên/tab, dọn file khi thoát, khôi phục bằng F5 và chạy nhiều tab giữ theo thiết kế v3.1.
- Nếu chọn **Dừng ghép**, FFmpeg được yêu cầu dừng và file kết quả chưa hoàn thành được dọn. Khi đóng tab trong lúc ghép, tiến trình đang chạy có thể cần thời gian để hủy; hệ thống còn cơ chế dọn file hết hạn dự phòng.

### Trình tự kiểm tra thực tế

1. `dotnet restore && dotnet build -c Release` trên Windows cài .NET 9 SDK.
2. Cài `Runtime/ffmpeg/ffmpeg.exe` và model `Voices`, chạy project theo hướng dẫn phía trên.
3. Render 3 MP3 bằng các giọng / tốc độ khác nhau. Nhấn Nghe, tải từng MP3.
4. Chọn file 1 và 3, không chọn file 2; đổi thứ tự bằng ↑ ↓. Ghép tên `Chương 1 - 3`, mở MP3 mới và nghe xem thứ tự nối có đúng.
5. Xóa MP3 đã ghép; đảm bảo hai MP3 nguồn còn trong lịch sử và nghe được.
6. Mở tab khác / máy LAN khác: không thấy file ở phiên trước; đóng tab kiểm tra cleanup sau thời gian chờ.

**Lưu ý:** Không thể chạy build .NET hoặc render Piper trên môi trường phát triển này vì thiếu .NET SDK, FFmpeg Windows và model. Kiểm tra compile + TTS end-to-end phải chạy trên Windows đã cài các thành phần trên.
