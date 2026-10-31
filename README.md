# LangKinhHuKhong.TTS v1.1 – tối ưu render truyện audio dài

**Đây là source WinForms .NET 9, không phải EXE biên dịch sẵn.**

## Công nghệ

- Piper ONNX **persistent** qua `--json-input`: một tiến trình, một lần nạp model, xuất WAV cho từng đoạn; cần bản Piper CLI hỗ trợ `--json-input` và `output_file`.
- Chọn CPU hoặc CUDA. GPU chỉ hoạt động nếu binary Piper hỗ trợ `--use-cuda` / `--cuda` và có thư viện CUDA phù hợp; mặc định chọn CPU.
- Checkpoint mỗi 80 câu trong thư mục cache ASCII `%TEMP%\LKH-TTS-Cache\resumable-v11` (hoặc đường dẫn của NativeVoicePaths). Hủy ứng dụng giữa lượt render sẽ giữ MP3/SRT của các batch hoàn tất; nhập lại **cùng nội dung, model, speaker, tốc độ, CPU/CUDA và trạng thái SRT** để tiếp tục.
- Piper luôn synth native 1×, FFmpeg đổi tốc độ sau khi có MP3. MP3 batch 1× ghép bằng stream copy và SRT tính từ từng WAV, thời lượng MP3.
- Không xây dựng WAV tổng nhiều GB. Checkpoint tiêu tốn thêm dung lượng ổ đĩa trong lúc render; khi thành công xóa checkpoint.

## Khác biệt bản quyền

**Premium:** không có giới hạn số ký tự do ứng dụng đặt và không áp bộ lọc chính sách từ ngữ nội bộ. Quyền sử dụng tài liệu và quy tắc nền tảng xuất bản vẫn áp dụng ngoài phần mềm. Giới hạn thực tế phụ thuộc RAM, ổ đĩa, Windows và độ dài văn bản có thể tải vào editor.

## Build / chạy

Giải nén ZIP sang thư mục mới, chạy `BUILD-VA-CHAY.cmd` (cần .NET SDK 9 trên Windows).
Nếu `Runtime/piper` hoặc `Runtime/ffmpeg` chưa có, đặt Piper (cùng DLL), FFmpeg và FFprobe vào các thư mục tương ứng, hoặc chọn thư mục tại màn Cài đặt. Giọng ONNX phải đi cùng `.onnx.json`.

## Giới hạn kiểm thử

Đã rà soát/đóng gói source và kiểm tra chữ ký token mẫu bằng Python. Chưa thể build hoặc chạy ứng dụng WinForms/Piper native trong môi trường không có .NET SDK/Windows.
