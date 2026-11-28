# LangKinhHuKhong.TTS Studio (v1.4)

**Trạm sản xuất Sách nói & Truyện Audio dài 100% Offline trên Windows (`WinForms .NET 9`)** — tích hợp Đa luồng Render không giới hạn, xử lý âm thanh Clarity 44.1 kHz 1-Pass, đồng bộ phụ đề `.SRT` chính xác tới từng frame PCM và bộ tiêu chuẩn **DNA Biên tập Kịch bản cho AI**.

---

## MỤC LỤC PHÂN LOẠI TÀI LIỆU

1. **[Phần I — Tổng quan Kiến trúc, Giao diện & Cài đặt Vận hành](#phần-i--tổng-quan-kiến-trúc-giao-diện--cài-đặt-vận-hành)**
   - [1.1. Công nghệ Lõi & Xử lý Âm thanh](#11-công-nghệ-lõi--xử-lý-âm-thanh)
   - [1.2. Bố cục Giao diện 4 Phân khu (Crimson Dark UI)](#12-bố-cục-giao-diện-4-phân-khu-crimson-dark-ui)
   - [1.3. Cấu trúc Thư mục, Build & Kiểm thử](#13-cấu-trúc-thư-mục-build--kiểm-thử)
   - [1.4. Bản quyền & Giới hạn Hệ thống](#14-bản-quyền--giới-hạn-hệ-thống)
2. **[Phần II — Hệ thống Câu lệnh Kịch bản, Nhịp nghỉ & Đa ngôn ngữ](#phần-ii--hệ-thống-câu-lệnh-kịch-bản-nhịp-nghỉ--đa-ngôn-ngữ)**
   - [2.1. Cơ chế Tách câu & Bảng Khoảng lặng Tự động (PCM Silence)](#21-cơ-chế-tách-câu--bảng-khoảng-lặng-tự-động-pcm-silence)
   - [2.2. Bảng Câu lệnh Text Điều khiển Trực tiếp (Inline Commands)](#22-bảng-câu-lệnh-text-điều-khiển-trực-tiếp-inline-commands)
   - [2.3. Thư viện Giọng Tiếng Trung (`Voices/TrungQuoc` - `zh-CN`)](#23-thư-viện-giọng-tiếng-trung-voicestrungquoc---zh-cn)
3. **[Phần III — DNA Chuẩn hóa Văn bản & Huấn luyện AI Biên tập Audio](#phần-iii--dna-chuẩn-hóa-văn-bản--huấn-luyện-ai-biên-tập-audio)**
   - [3.1. Chỉ thị Vai trò dành cho AI (System Instruction)](#31-chỉ-thị-vai-trò-dành-cho-ai-system-instruction)
   - [3.2. Quy tắc Chuẩn hóa Ngôn ngữ & Âm vị (Text Normalization)](#32-quy-tắc-chuẩn-hóa-ngôn-ngữ--âm-vị-text-normalization)
   - [3.3. Kỹ thuật Điều khiển Ngữ điệu: Nhấn âm, Lên/Xuống giọng & Ngân âm cuối](#33-kỹ-thuật-điều-khiển-ngữ-điệu-nhấn-âm-lênxuống-giọng--ngân-âm-cuối)
   - [3.4. Khung Định dạng Chuẩn (Blueprint) & Ví dụ Chuyển đổi Thực tế](#34-khung-định-dạng-chuẩn-blueprint--ví-dụ-chuyển-đổi-thực-tế)
   - [3.5. Mẫu Câu lệnh (Prompt) Gửi cho ChatGPT / Claude / Gemini](#35-mẫu-câu-lệnh-prompt-gửi-cho-chatgpt--claude--gemini)

---

# PHẦN I — TỔNG QUAN KIẾN TRÚC, GIAO DIỆN & CÀI ĐẶT VẬN HÀNH

## 1.1. Công nghệ Lõi & Xử lý Âm thanh

- **Persistent Piper ONNX (`--json-input`)**: Giữ tiến trình `piper.exe` sống xuyên suốt vòng đời worker, nạp model `.onnx` một lần duy nhất. Hỗ trợ chọn **CPU** (mặc định ổn định) hoặc **CUDA GPU** (nếu binary Piper đi kèm `onnxruntime_providers_cuda.dll` và hỗ trợ `--use-cuda` / `--cuda`).
- **Clarity Config v2 & Chống nuốt âm tiếng Việt**:
  - Tự động ánh xạ dấu trọng âm phụ `ˌ` sang trọng âm chính `ˈ` trên bản sao cấu hình runtime (không sửa file `.onnx.json` gốc), giữ trọn thanh điệu các hư từ tiếng Việt (*mà, được, với, của, thì, là...*) và tinh chỉnh thông số VITS (`noise_scale = 0.42`, `noise_w = 0.45`, `length_scale = 1.06`).
  - **Carrier Syllable Warmup**: Tự động mồi âm khép vô thanh (`Út:` / `Ất:`) rồi cắt chính xác tại rãnh im lặng cho câu ngắn 1–2 từ (*"Giết!"*, *"Đánh!"*) để triệt tiêu lỗi biến dạng phụ âm đầu của mô hình VITS.
  - **YIN + TD-PSOLA Vowel Sustain**: Tự động dò chu kỳ thanh quản cơ bản bằng thuật toán YIN và kéo giãn mượt hạt nhân nguyên âm cuối câu có dấu ba chấm (`...` / `…`) bằng nội suy khóa đỉnh chu kỳ **TD-PSOLA** không bị rè tiếng, có bù trừ theo tốc độ phát (`1.25×`, `1.5×`, `2×`).
- **Streaming PCM 1-Pass 44.1 kHz (Không giới hạn WAV 4 GB)**:
  - Stream trực tiếp raw PCM 16-bit (`s16le`) vào một tiến trình FFmpeg duy nhất qua chuỗi lọc 32-bit float (`aresample=44100`, `highpass=65Hz`, presence EQ `3.2kHz`, `atempo`, `alimiter`) và mã hóa MP3 `192 kbps` đúng **1 lần**.
- **Đa luồng Render Song Song & Smart Batch Checkpoint**:
  - Chế độ **Tối đa công suất (Không giới hạn)** tự động bão hòa toàn bộ nhân CPU cho cả trường hợp render nhiều file cùng lúc lẫn co giãn đa worker bên trong một tiểu thuyết dài (>1.500.000 ký tự).
  - Chia checkpoint mỗi **80 câu** theo mã băm nội dung tại `%TEMP%\LKH-TTS-Cache\resumable-v12`; khi dừng giữa chừng hoặc sửa một phần văn bản, các batch không đổi được tái sử dụng tức thì; khi hoàn tất tự động dọn sạch file tạm.
- **Ghép MP3 Nguyên bản & Đồng bộ Phụ đề `.SRT`**:
  - Tốc độ `1×`: Nối trực tiếp gói tin nén MP3 (`-c:a copy`), không giải mã hay nén lại.
  - Đổi tốc độ (`0.5×`, `1.25×`, `1.5×`, `2×`): Giải mã sang FLAC lossless trung gian rồi mã hóa MP3 VBR chất lượng cao (`-q:a 0`) đúng một lần.
  - Phụ đề `.SRT` được tính trực tiếp từ số sample PCM thực tế của từng đoạn WAV và hiệu chỉnh khớp thời lượng MP3 cuối từ `ffprobe` (không dùng nhận dạng giọng nói).

## 1.2. Bố cục Giao diện 4 Phân khu (Crimson Dark UI)

Ứng dụng sử dụng bảng màu **Đỏ Đậm Đen (Deep Crimson & Obsidian)** dịu mắt khi làm việc lâu, chia thành 4 mục chính trên thanh điều hướng trái:

| Mục điều hướng | Phím tắt | Chức năng chính |
| :--- | :--- | :--- |
| **`1. Biên tập (0)`** | `Ctrl + 1` | Màn hình hợp nhất: phía trên gồm **Kịch bản đầu vào** (soạn/dán kịch bản, nghe thử giới hạn 25–500 từ `Ctrl+P`) và **Bảng điều khiển giọng đọc**; ngay phía dưới là bảng **Đa luồng Render (0)** quản lý tiến độ trực tiếp (`Ctrl+Enter` đẩy bài mới mà không khóa trình soạn thảo). |
| **`2. Nơi lưu`** | `Ctrl + 2` | Quản lý toàn bộ file `.mp3` + `.srt` đã xuất trong thư mục `Output`: tìm kiếm, nghe thử trên thanh Player toàn cục, tải bản sao, đổi tên hiển thị, xóa hoặc chuyển sang ghép chương. |
| **`3. Ghép Audio`** | `Ctrl + 3` | Sắp xếp thứ tự nhiều chương MP3, kiểm tra cặp file `.srt` đi kèm, chọn tốc độ xuất hoặc mở hộp thoại nâng cao để gán SRT và tốc độ riêng từng chương. |
| **`4. Cài đặt và tài liệu`** | `Ctrl + 4..6` | Tích hợp 3 tab con: **Cài đặt** (`Ctrl+4`: cấu hình 3 thư mục & chạy ngầm System Tray `Ctrl+M`) · **Danh sách giọng** (`Ctrl+5`: tạo mẫu `Voices/example`, lọc giọng, đổi tên chuẩn hóa `.onnx` + `.onnx.json`) · **Hướng dẫn dùng** (`Ctrl+6`: tra cứu lệnh kịch bản & sao chép DNA cho AI). |

## 1.3. Cấu trúc Thư mục, Build & Kiểm thử

### Yêu cầu & Cấu trúc 3 Thư mục Chuẩn cạnh `.exe`
- Cần **.NET 9 SDK** trên Windows để biên dịch từ mã nguồn.
- Cấu trúc thư mục vận hành (tự động tạo trong `Publish\win-x64` khi chạy script build):
  - `Runtime\piper\`: Đặt `piper.exe`, các thư viện `.dll` và `espeak-ng-data`.
  - `Runtime\ffmpeg\`: Đặt `ffmpeg.exe` và `ffprobe.exe`.
  - `Voices\`: Đặt các cặp model giọng `.onnx` đi kèm `.onnx.json` (thư mục con `Voices\example\` lưu mẫu thử MP3 của từng giọng).
  - `Output\`: Nơi lưu mặc định cho các bản thu `.mp3` và `.srt`.

### Các lệnh Build, Import & Kiểm thử
- **Đóng gói và chạy ngay**:
  ```bat
  BUILD-VA-CHAY.cmd
  ```
- **Chỉ đóng gói bản Release Single-File (`win-x64` hoặc `win-arm64`)**:
  ```powershell
  powershell -NoProfile -ExecutionPolicy Bypass -File Scripts\Build-Windows.ps1 -Runtime win-x64
  ```
- **Sao chép nhanh `Runtime` & `Voices` từ dự án cũ**:
  ```powershell
  powershell -NoProfile -ExecutionPolicy Bypass -File Scripts\Import-Engine-From-Web.ps1 -OldProjectFolder "D:\DuongDan\DuAnCu"
  ```
- **Chạy bộ kiểm thử tự động (Smoke Tests & MP3 Stream-Copy Integration Tests)**:
  ```powershell
  dotnet run --project Tests/TextProcessing.SmokeTests.csproj -c Release
  dotnet run --project Tests/Mp3Merge.IntegrationTests.csproj -- "D:\Path\To\ffmpeg.exe"
  ```

## 1.4. Bản quyền & Giới hạn Hệ thống

- **Không giới hạn phần mềm**: Ứng dụng không áp giới hạn số ký tự đầu vào và không áp bộ lọc từ ngữ nội bộ; dung lượng kịch bản chỉ phụ thuộc vào RAM, ổ cứng và giới hạn tài nguyên của Windows.
- **Trách nhiệm nội dung**: Quyền sở hữu trí tuệ đối với văn bản nguồn, giấy phép sử dụng từng model giọng `.onnx` và quy định của nền tảng phát hành vẫn áp dụng bên ngoài phần mềm.

---

<!-- DNA_START -->
# PHẦN II — HỆ THỐNG CÂU LỆNH KỊCH BẢN, NHỊP NGHỈ & ĐA NGÔN NGỮ

## 2.1. Cơ chế Tách câu & Bảng Khoảng lặng Tự động (PCM Silence)

Bộ phân tích kịch bản (`TextProcessor` & `SpeechPacing`) xử lý dấu câu và khoảng nghỉ theo các quy tắc chuẩn xác sau:

1. **Nguyên tắc Dấu phẩy (`,`), Hai chấm (`:`), Chấm phẩy (`;`)**:
   - Giữ nguyên dấu phẩy bên trong các câu ngắn và vừa để mô hình Piper giữ trọn ngữ cảnh âm tiết tiếng Việt, không đọc giật cục.
   - Ở các câu dài nhiều mệnh đề nối tiếp, phần mềm tự động tách mệnh đề thông minh tại dấu `,` (`95ms`) hoặc `;` / `:` (`135ms`) khi mệnh đề đã đủ độ dài ngữ nghĩa, giữ số phoneme mỗi lần đọc trong vùng nét nhất của VITS.
   - Nếu gặp câu siêu dài không chấm câu (`> 180–240` ký tự), phần mềm tự động ngắt mềm tại khoảng trắng/dấu phẩy (`100ms`).
   - **Khuyến nghị độ dài câu tối ưu khi biên tập**: Mỗi câu hoàn chỉnh nên dài từ **40 đến 180 ký tự** (khoảng **10 – 35 từ**, tối đa không quá 220 ký tự/câu).

2. **Bảng Thời lượng Khoảng lặng Tự động (Automatic PCM Silence)**:

   | Ký hiệu trong văn bản | Tác động trong phần mềm | Thời lượng nghỉ (PCM) |
   | :--- | :--- | :--- |
   | Đầu file âm thanh | Khoảng lặng mở đầu tránh cụt âm đầu | `160ms` |
   | Dấu phẩy tách mệnh đề dài (`,`) | Ngắt nhịp mệnh đề thông minh (+ đuôi câu Piper) | `95ms` |
   | Chấm phẩy / Hai chấm tách vế dài (`;` / `:`) | Ngắt vế câu lớn | `135ms` |
   | Dấu chấm (`.` hoặc `。`) | Kết thúc câu trần thuật | `310ms` |
   | Dấu chấm hỏi (`?` hoặc `？`) | Kết thúc câu nghi vấn | `330ms` |
   | Dấu chấm than (`!` hoặc `！`) | Kết thúc câu cảm thán / mệnh lệnh | `350ms` |
   | Dấu ba chấm (`...` hoặc `…`) | Kéo giãn nguyên âm cuối (WSOLA) + nghỉ lắng đọng | `480ms` |
   | Xuống 1 dòng mới (`Enter` / `\n`) | Ngắt câu cuối dòng | `350ms` |
   | Xuống 2 dòng trở lên (`\n\n` - có dòng trống) | Ngắt chuyển đoạn văn | `550ms` |
   | Câu dài không dấu kết câu | Tự động ngắt mềm tại khoảng trắng/dấu phẩy | `100ms` |
   | Số thập phân & giờ (`3.14`, `1,5`, `10:30`) | Không cắt câu nhầm (`10:30` tự đọc `10 giờ 30` ở tiếng Việt) | `0ms` (đọc liền) |

## 2.2. Bảng Câu lệnh Text Điều khiển Trực tiếp (Inline Commands)

Các câu lệnh dưới đây được gõ trực tiếp vào văn bản. **Đặc điểm quan trọng**: Chúng **KHÔNG BỊ ĐỌC THÀNH TIẾNG** và **KHÔNG XUẤT HIỆN TRONG PHỤ ĐỀ `.SRT`**.

### Nhóm lệnh Ngắt nghỉ tùy chỉnh (`50ms` đến `10000ms` / `10s`)

| Câu lệnh Text | Ý nghĩa & Tác dụng | Khi nào nên chèn vào văn bản? |
| :--- | :--- | :--- |
| `[pause:500ms]` | Ngắt câu tại vị trí đặt lệnh và chèn đúng `500ms` khoảng lặng. | Giữa lời dẫn và lời thoại quan trọng, hoặc sau mệnh đề cần nhấn mạnh. |
| `[pause:1.5s]` | Ngắt câu và chèn `1500ms` (1.5 giây) khoảng lặng. | Sau tiêu đề chương (`Chương 1: ... [pause:1.5s]`), hoặc khi chuyển cảnh lớn. |
| `[nghi:800ms]` | Cú pháp tiếng Việt tương đương `[pause:800ms]`. | Sau câu độc thoại nội tâm, câu hỏi tu từ hoặc tình huống kịch tính. |
| `[nghi:2s]` | Chèn `2000ms` (2 giây) khoảng lặng. | Khi kết thúc một hồi, phân đoạn lớn hoặc chuyển đổi không gian/thời gian. |
| `[break:1000ms]` | Bí danh tương đương `[pause:1000ms]`. | Dùng thay thế linh hoạt với `[pause:...]` và `[nghi:...]`. |
| `[im:1s]` | Bí danh tương đương `[pause:1s]`. | Chèn khoảng im lặng đúng 1 giây. |

> **Lưu ý cú pháp**:
> - Hỗ trợ đơn vị `ms` (mili-giây) hoặc `s` (giây), viết dấu chấm hoặc phẩy thập phân đều hợp lệ (VD: `[pause:400ms]`, `[pause:1.2s]`, `[nghi:1,5s]`).
> - Khi đặt ngay sau dấu chấm câu (VD: `Đêm đã khuya. [pause:800ms]`), phần mềm dùng khoảng nghỉ `800ms` của lệnh thay cho `310ms` mặc định (không cộng dồn thừa).

### Nhóm lệnh Ghi chú ẩn (Invisible Notes & Comments)

| Câu lệnh / Cú pháp | Ý nghĩa & Tác dụng | Khi nào nên dùng? |
| :--- | :--- | :--- |
| `[[Nội dung ghi chú]]` | Bỏ qua hoàn toàn khi đọc TTS và khi xuất SRT. | Ghi chú bối cảnh, cảm xúc nhân vật hoặc đánh dấu vị trí biên tập. |
| `[note: Nội dung]` | Bỏ qua hoàn toàn (tương đương `[[...]]`). | Ghi chú kỹ thuật (VD: `[note: Đoạn này tốc độ 1.25x]`). |
| `[ghichu: Nội dung]` | Cú pháp tiếng Việt của `[note: ...]`, bỏ qua hoàn toàn. | Ghi chú tiếng Việt cho người kiểm duyệt kịch bản. |
| `// Cả dòng ghi chú` | Mọi dòng bắt đầu bằng `//` đều bị bỏ qua. | Dùng làm tiêu đề phân cảnh ẩn hoặc phân tách phần trong kịch bản. |

## 2.3. Thư viện Giọng Tiếng Trung (`Voices/TrungQuoc` - `zh-CN`)

Phần mềm hỗ trợ đồng bộ văn bản và phụ đề song ngữ Việt – Trung với các bộ giọng Nam/Nữ Tiếng Trung (`zh-CN`):
- **Giọng Nam**: `zh_CN-chaowen-medium` (**Siêu Văn - Chaowen**, 1 giọng chuẩn Phổ thông) và `zh_CN-aishell3-nam-medium` (**Tuyển Tập 36 Giọng Nam AISHELL-3**, chọn từng giọng trong ô `Speaker`).
- **Giọng Nữ**: `zh_CN-huayan-medium` (**Hoa Nghiên - Huayan**), `zh_CN-xiao_ya-medium` (**Tiểu Nhã - Xiao Ya**) và `zh_CN-aishell3-nu-medium` (**Tuyển Tập 138 Giọng Nữ AISHELL-3**).
- **Xử lý Dấu câu & Từ đa âm Tiếng Trung**:
  - Giữ dấu phẩy toàn giác `，` trong câu ngắn và dùng `；`, `。` / `！` / `？` kết hợp `[pause:...]` để đồng bộ nhịp nghỉ với bản dịch tiếng Việt.
  - Bộ tiền xử lý `SpeechPacing.PrepareTextForPiper` tự động phân giải các từ đa âm phổ biến (`重来` / `重新` → `chóng`, trợ từ `一遍地` / `慢慢地` → `de`, `长大` → `zhǎng`, `银行` → `háng`, `音乐` → `yuè`, `归还` → `huán`, `了解` → `liǎo`...) khi gửi sang `piper.exe` nhưng vẫn **giữ nguyên 100% mặt chữ Hán gốc** trên trình soạn thảo và file `.srt`.

---

# PHẦN III — DNA CHUẨN HÓA VĂN BẢN & HUẤN LUYỆN AI BIÊN TẬP AUDIO

*(Tài liệu huấn luyện chuẩn — Dùng để đính kèm hoặc dán trực tiếp cho ChatGPT, Claude, Gemini, Custom GPT hoặc AI Agent)*

## 3.1. Chỉ thị Vai trò dành cho AI (System Instruction)

Khi bạn (AI / ChatGPT / Claude / Gemini) nhận được tài liệu **`README.md`** này của **LangKinhHuKhong.TTS Studio**, nhiệm vụ của bạn là đóng vai trò **Biên tập viên Kịch bản Audio & Chuyển ngữ chuyên biệt**.

Mọi văn bản (truyện chữ, tiểu thuyết, sách nói, tài liệu Markdown, bản dịch thô, truyện convert) khi người dùng yêu cầu **"Format theo chuẩn DNA"** hoặc **"Dịch/Chuẩn hóa cho LangKinhHuKhong.TTS"** PHẢI tuân thủ tuyệt đối các quy tắc cú pháp, câu lệnh điều khiển (`[pause:...]`, `[nghi:...]`, `[[...]]`), độ dài câu và quy tắc chuẩn hóa âm vị tại **Phần II** và **Phần III** của tài liệu này.

## 3.2. Quy tắc Chuẩn hóa Ngôn ngữ & Âm vị (Text Normalization)

Vì model Piper ONNX chạy **100% Offline** theo từ điển âm vị cố định của từng ngôn ngữ, AI **BẮT BUỘC** phải xử lý sạch các yếu tố sau khi biên tập văn bản:

### 1. Tuyệt đối bung chữ viết tắt & ký hiệu đặc biệt
- **Từ viết tắt tiếng Việt / chữ chat**:
  - `ko`, `k`, `khg` → `không`
  - `dc`, `đc` → `được`
  - `nv` → `nhân vật` / `nhân viên` (tùy ngữ cảnh)
  - `TP.HCM` → `Thành phố Hồ Chí Minh`
  - `UBND` → `Ủy ban nhân dân`
  - `THPT` → `Trung học phổ thông`
- **Số La Mã (Roman Numerals)**:
  - Model TTS dễ đọc chữ La Mã (`I, II, III, IV, V, IX, X, XX`) thành chữ cái tiếng Anh (`i, v, x`).
  - **Quy tắc bắt buộc**: Chuyển toàn bộ số La Mã sang số Ả Rập hoặc chữ viết.
    - Sai: `Chương IV: Đại chiến thế kỷ XIX`
    - Đúng: `Chương 4: Đại chiến thế kỷ 19. [pause:1.5s]`
- **Đơn vị đo lường, Tiền tệ, Phần trăm, Ngày tháng**:
  - `50%` → `50 phần trăm`
  - `100km/h` → `100 ki-lô-mét trên giờ`
  - `500m2` → `500 mét vuông`
  - `\$500` → `500 đô la`
  - `20/10/2026` → `ngày 20 tháng 10 năm 2026`
  - Giờ giấc dạng `10:30` hoặc số thập phân `1,5` có thể giữ nguyên hoặc chuyển thành `10 giờ 30 phút` để tự nhiên nhất.

### 2. Làm sạch rác ký tự trong Truyện dịch / Truyện Convert / Markdown
- **Xóa bỏ hoàn toàn** các dòng trang trí vô nghĩa: `================`, `****************`, `~~~~~~~~~~~~~~~~`, `----------------`, `❀ ❀ ❀`, `★ ★ ★`. Thay thế bằng dòng trống `\n\n` kèm `[pause:1.2s]`.
- **Chuyển đổi dấu ngoặc ngoại lai**: Ngoặc Trung/Nhật `「...」`, `『...』`, `【...】` → Đổi sang ngoặc kép chuẩn `"..."`.
- **Cú pháp Hán-Việt bị đảo ngược (nếu nguồn là truyện convert)**: Viết lại thành câu tiếng Việt thuần túy, đúng ngữ pháp Chủ ngữ – Vị ngữ, mượt mà khi đọc thành tiếng.

### 3. Quy tắc Độ dài câu & Nhịp thở (Breath-Group Formatting)
- Mỗi câu nên dài từ **10 đến 35 từ** (khoảng **50 – 180 ký tự**).
- Nếu gặp câu gốc quá dài (trên 220 ký tự), AI phải chủ động tách thành 2–3 câu ngắn hơn bằng dấu chấm `.` hoặc chèn `[pause:300ms]` tại điểm ngắt mạch ý.

## 3.3. Kỹ thuật Điều khiển Ngữ điệu: Nhấn âm, Lên/Xuống giọng & Ngân âm cuối

Mô hình **Piper ONNX (VITS Neural TTS)** không dùng thẻ XML SSML mà điều khiển **trọng âm (stress), cao độ (pitch) và độ ngân (duration)** trực tiếp qua cấu trúc dấu câu, cách tách mệnh đề, lệnh `[pause:...]` và dấu ba chấm `...`.

### 1. Kỹ thuật Nhấn Âm (Nhấn mạnh vào 1 từ hoặc cụm từ khóa)
1. **Đóng khung bằng dấu phẩy `,` (Nhấn nhẹ, tự nhiên liền hơi)**:
   - *Ví dụ*: `Hắn, tuyệt đối, sẽ không bao giờ quay đầu lại.`
2. **Đặt `[pause:250ms]` – `[pause:350ms]` ngay trước từ khóa (Nhấn kịch tính kiểu Audiobook)**:
   - *Ví dụ*: `Người đứng sau toàn bộ âm mưu này chính là... [pause:300ms] ngươi!`
3. **Tách từ cần nhấn thành nhịp câu ngắn có dấu `!` (Nhấn gắt, dứt khoát)**:
   - *Ví dụ*: `Không! [pause:250ms] Tuyệt đối không thể như vậy!`

### 2. Kỹ thuật Lên Giọng – Xuống Giọng (Pitch & Intonation Control)
- **Ép LÊN GIỌNG tại một từ cụ thể giữa câu** (VD gốc: *"Kẻ phản bội... Chính (lên giọng) là ngươi"*):
  - **Cách A (Bật cao chữ "Chính" dứt khoát bằng `!` hoặc `?!`)**:
    `Kẻ phản bội... [pause:350ms] Chính! [pause:120ms] Là ngươi!`
    *(Hoặc ngỡ ngàng phát hiện)*: `Kẻ phản bội... [pause:350ms] Chính?! [pause:150ms] Chính là ngươi!`
  - **Cách B (Treo cao chữ "Chính" liền hơi bằng dấu phẩy `,`)**:
    `Kẻ phản bội... [pause:300ms] Chính, là ngươi!`
  - **Cách C (Điệp từ leo thang 2 nhịp)**:
    `Kẻ phản bội... [pause:400ms] Chính là... [pause:200ms] chính là ngươi!`
- **Lên giọng cuối câu (nghi vấn, kinh ngạc, chất vấn)**:
  - Kết hợp từ để hỏi (`sao`, `ư`, `à`, `hả`, `chăng`) + dấu `?` hoặc `?!`.
  - *Ví dụ*: `Ngươi thật sự dám bước vào cánh cổng đó sao?` / `Chuyện này là thật ư?!`
- **Xuống giọng trầm, lắng đọng (buồn, suy tư, huyền bí)**:
  - Kết thúc câu bằng dấu ba chấm `...` (hoặc `…`), theo sau là `[pause:600ms]` – `[pause:1s]` và **viết hoa chữ cái đầu của câu tiếp theo**.
  - **Lưu ý quan trọng**: Không đặt `...` hay `[pause:...]` ngay trước các hư từ viết thường (`thì`, `là`, `mà`, `nhưng`, `như`, `lúc`) ở giữa câu; hãy dùng dấu phẩy `,` (VD: `giá như lúc đầu thế này, thì tốt biết mấy...`).
  - *Ví dụ*: `Rồi tất cả, cũng chỉ còn lại hư không... [pause:800ms]`
- **Tương phản Lên giọng rồi Hạ giọng ngay câu sau**:
  - *Ví dụ*: `Ngươi nghĩ ta sợ ngươi sao? [pause:450ms] Không... ta chỉ thấy tiếc cho ngươi thôi...`

### 3. Kỹ thuật Kéo Dài / Ngân Âm Cuối (Vowel & Ending Elongation)
> **CẢNH BÁO QUAN TRỌNG CHO AI**: Tuyệt đối **KHÔNG** viết lặp chữ cái liền nhau kiểu `"điiii"`, `"ơiiii"`, `"khôngggg"` vì bộ từ điển âm vị `eSpeak-ng` sẽ tưởng là từ lạ và đọc đánh vần từng chữ cái.

Để kéo dài / ngân âm cuối mượt mà, hãy dùng 4 kỹ thuật chuẩn sau:
1. **Dùng dấu ba chấm `...` ở cuối từ/câu cần ngân** (kích hoạt bộ kéo giãn chu kỳ thanh quản **YIN + TD-PSOLA** trong trẻo của phần mềm):
   - *Ví dụ*: `Giết...` / `Đợi ta với...` / `Trời đất ơi... [pause:500ms] Sao lại ra nông nỗi này...`
2. **Nhân âm bằng gạch nối `-` hoặc tách nhịp bằng `...` (Cho tiếng gọi xa, tiếng thở dài, tiếng cười)**:
   - *Tiếng gọi ngân xa*: `Sư phụ ơi... ơ-i... [pause:600ms]`
   - *Tiếng thở dài ngân*: `Haiz... ài... [pause:500ms]`
   - *Tiếng cười ngân*: `Ha ha... ha...`
3. **Lặp từ láy âm cuối kèm dấu `...` (Tạo hiệu ứng tiếng vọng / ngân xa dần)**:
   - *Ví dụ*: `Tiếng chuông ngân vang khắp sườn núi... vang mãi... vang mãi... [pause:900ms]`
4. **Khóa dư âm (Decay) bằng `[pause:600ms]` – `[pause:1.2s]` sau dấu `...`**:
   - *Ví dụ*: `Giữa khoảng không vô tận ấy, chỉ còn tiếng thở dài vọng lại... xa xăm... mờ mịt... [pause:1s]`

## 3.4. Khung Định dạng Chuẩn (Blueprint) & Ví dụ Chuyển đổi Thực tế

### Khung kịch bản mẫu (Blueprint)
```text
// BẢN KỊCH BẢN ĐÃ CHUẨN HÓA CHO LANGKINHHUKHONG.TTS STUDIO
[[Ghi chú: Đã chuẩn hóa số La Mã, đơn vị, nhịp nghỉ và ngữ điệu nhấn/lên/ngân giọng]]

Chương 1: Tên chương truyện rõ ràng. [pause:1.5s]

Đoạn mở đầu dẫn nhập bối cảnh với câu văn ngắn gọn, giàu hình ảnh. Giữa các câu có dấu chấm chuẩn xác để tạo nhịp nghỉ 310 mili-giây tự nhiên. [pause:600ms]

"Kẻ phản bội... [pause:350ms] Chính! [pause:120ms] Là ngươi!" [nghi:450ms] Người kể chuyện tiếp lời ngay sau nhịp nghỉ ngắn.

"Nếu là câu hỏi đầy nghi hoặc, hãy kết thúc bằng dấu chấm hỏi?"
"Và câu trả lời trầm lắng ngân dài ở dòng tiếp theo..." [pause:800ms]

// Chuyển cảnh
[note: Chuyển sang phân cảnh buổi tối]
Đêm hôm đó, tại tòa thành cổ... gió thổi lạnh lẽo... [nghi:1s] Một bóng người lặng lẽ xuất hiện dưới ánh trăng. [pause:2s]
```

### Ví dụ 1 — Chuyển đổi văn bản thô hiện đại (Before vs. After)

**❌ Văn bản thô ban đầu (Chưa qua DNA):**
> Chương IV: Quyết định lúc 2h sáng!!!
> ========================
> Ngày 15/08/2025, tại TP.HCM, hắn nhìn đồng hồ — đã 2h15p sáng, ngoài trời mưa tầm tã ko dứt, gió thổi ào ào qua khung cửa sổ tầng 12A làm tấm rèm bay phần phật, trong lòng hắn ngổn ngang trăm mối tơ vò vì hợp đồng \$50,000 với cty đối tác vẫn chưa dc ký kết dù thời hạn chỉ còn 12h nữa là kết thúc...
> 【Hệ thống nhắc nhở: Ký chủ còn 5% năng lượng】
> Hắn chỉ tay hét lớn: "Kẻ phản bội... Chính (lên giọng) là ngươi!!!" rồi thở dài: "Ta phải làm gì bây giờ???" (Ghi chú: đoạn này đọc giọng trầm buồn, ngân dài cuối câu)

**✅ Văn bản sau khi chuẩn hóa theo DNA:**
```text
// Chương 4 - Đã chuẩn hóa theo DNA LangKinhHuKhong.TTS
Chương 4: Quyết định lúc hai giờ sáng. [pause:1.5s]

Ngày 15 tháng 8 năm 2025, tại Thành phố Hồ Chí Minh. [pause:500ms] Hắn nhìn đồng hồ, đã hai giờ mười lăm phút sáng. Ngoài trời mưa tầm tã không dứt, gió thổi ào ào qua khung cửa sổ tầng 12A làm tấm rèm bay phần phật. [nghi:600ms] Trong lòng hắn ngổn ngang trăm mối tơ vò. Hợp đồng 50 nghìn đô la với công ty đối tác vẫn chưa được ký kết, dù thời hạn chỉ còn 12 giờ nữa là kết thúc... [pause:1s]

"Hệ thống nhắc nhở: Ký chủ còn 5 phần trăm năng lượng." [pause:700ms]

Hắn chỉ tay hét lớn: [pause:250ms]
"Kẻ phản bội... [pause:350ms] Chính! [pause:120ms] Là ngươi!" [pause:700ms]

[[Ghi chú: đoạn này đọc giọng trầm buồn, ngân dài cuối câu]]
Rồi hắn buông thõng bàn tay, thở dài... [nghi:600ms] "Ta phải làm gì bây giờ đây..." [pause:1.5s]
```

### Ví dụ 2 — Văn phong triết lý / cổ phong hào sảng
```text
Đời người vốn dĩ, [pause:250ms] chẳng có hai chữ: thất bại. [pause:500ms]
Hoặc là lập nên đại nghiệp, hoặc là tôi luyện nên thân; [pause:250ms] há có chuyện công quả nửa vời! [pause:650ms]

Việc đáng mừng, thì ta mở tiệc ăn mừng; việc đáng ghi, thì ta khắc cốt ghi tâm. [pause:450ms]
Phàm làm việc gì, tất phải tận lực đến cùng; tuyệt không làm chuyện vô ích, hoài phí tâm lực. [pause:650ms]

Thế sự vô thường, việc chưa đạt được mục đích, thì ắt cũng thu về được một bài học xương máu. [pause:500ms]
Ta cho phép thiên hạ vạn sự tùy duyên, nhưng tin chắc rằng: [pause:300ms]
Phàm những gì đã xảy ra trên đời, chung quy, [nghi:350ms] đều ẩn chứa cơ duyên giúp ích cho chí hướng của ta... [pause:900ms]
```

## 3.5. Mẫu Câu lệnh (Prompt) Gửi cho ChatGPT / Claude / Gemini

Đính kèm file **`README.md`** này (hoặc bấm nút **`🤖 Sao chép DNA đào tạo AI`** trong mục `4. Cài đặt và tài liệu → Hướng dẫn dùng`) vào ChatGPT / Claude / Gemini kèm một trong các câu lệnh sau:

### Mẫu 1: Chuẩn hóa văn bản / truyện tiếng Việt có sẵn (Đầy đủ Nhịp nghỉ + Nhấn âm + Ngân giọng)
> *"Hãy đọc kỹ Phần II & Phần III trong file `README.md` của LangKinhHuKhong.TTS. Sau đó biên tập và format lại toàn bộ văn bản dưới đây theo đúng chuẩn DNA: bung toàn bộ từ viết tắt/số La Mã/ký hiệu/đơn vị sang chữ tiếng Việt dễ đọc, ngắt lại các câu dài quá 200 ký tự, xóa ký tự rác, chuyển ghi chú vào `[[...]]`, đồng thời áp dụng kỹ thuật Nhấn âm, Lên/Xuống giọng, Ngân âm cuối bằng dấu `,`, `!`, `...` và `[pause:...]` / `[nghi:...]` ở các đoạn hội thoại cao trào và chuyển cảnh. Giữ nguyên 100% cốt truyện gốc."*

### Mẫu 2: Dịch truyện nước ngoài (Trung / Anh / Nhật / Hàn) sang chuẩn TTS Diễn cảm
> *"Dựa theo tiêu chuẩn DNA trong file `README.md` của LangKinhHuKhong.TTS, hãy dịch chương truyện sau sang tiếng Việt thuần mượt chuyên dùng cho sách nói (Audiobook). Tuyệt đối không để sót cấu trúc câu convert, đổi toàn bộ ngoặc ngoại lai sang `"..."`, viết rõ các con số/đơn vị, giữ độ dài mỗi câu dưới 180 ký tự, đồng thời xử lý nhấn giọng, lên/xuống giọng ở lời thoại nhân vật và chèn các lệnh `[pause:300ms]` – `[pause:1.5s]`, `[nghi:...]` vào các điểm ngắt nhịp cảm xúc."*

### Mẫu 3: Chỉ chèn nhịp nghỉ `[pause]` / `[nghi]` và nhấn giọng mà không sửa từ vựng
> *"Theo quy tắc DNA trong `README.md` của LangKinhHuKhong.TTS, hãy giữ nguyên từ vựng của văn bản dưới đây, chỉ chuẩn hóa dấu câu, đưa các chú thích đạo diễn như `(lên giọng)`, `(nhấn mạnh)`, `(ngân dài)` thành cấu trúc dấu câu + lệnh `[pause:...]` / `[nghi:...]` tương ứng để đưa vào phần mềm LangKinhHuKhong.TTS."*
<!-- DNA_END -->
