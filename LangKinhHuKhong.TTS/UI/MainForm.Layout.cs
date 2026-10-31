using System.Drawing;
using System.Windows.Forms;
using LangKinhHuKhong.TTS.Core;
using LangKinhHuKhong.TTS.Models;

namespace LangKinhHuKhong.TTS.UI;

public sealed partial class MainForm
{
    private void BuildLayout()
    {
        var root = Theme.Table(2, 1);
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 270));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(root);
        root.Controls.Add(BuildSidebar(), 0, 0);

        var content = Theme.Table(1, 3);
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.Controls.Add(content, 1, 0);
        content.Controls.Add(BuildHeader(), 0, 0);
        content.Controls.Add(_pageHost, 0, 1);
        content.Controls.Add(BuildFooter(), 0, 2);

        RegisterPage("editor", BuildEditorPage());
        RegisterPage("library", BuildLibraryPage());
        RegisterPage("merge", BuildMergePage());
        RegisterPage("settings", BuildSettingsPage());
    }

    private static Image LoadBrandLogo()
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "Assets", "logo-sidebar.png"),
            Path.Combine(AppContext.BaseDirectory, "Assets", "logo.png")
        ];
        foreach (var path in candidates)
        {
            try
            {
                if (File.Exists(path))
                {
                    using var loaded = Image.FromFile(path);
                    return new Bitmap(loaded);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { }
        }

        var fallback = new Bitmap(220, 220);
        using (var g = Graphics.FromImage(fallback))
        using (var gold = new Pen(Theme.Gold, 8))
        using (var red = new SolidBrush(Theme.Blue))
        using (var font = new Font("Times New Roman", 34, FontStyle.Bold))
        using (var brush = new SolidBrush(Color.White))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            g.FillEllipse(red, 8, 8, 204, 204);
            g.DrawEllipse(gold, 8, 8, 204, 204);
            g.DrawString("LKHK", font, brush, new RectangleF(22, 76, 176, 70),
                new StringFormat { Alignment = StringAlignment.Center });
        }
        return fallback;
    }

    private Control BuildSidebar()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Sidebar, Padding = new Padding(14, 14, 14, 14) };
        var grid = Theme.Table(1, 4);
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 252));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 136));
        panel.Controls.Add(grid);

        var brandCard = new SurfacePanel
        {
            Dock = DockStyle.Fill,
            Radius = 24,
            ShowBorder = true,
            BackColor = Theme.SidebarAccent,
            Padding = new Padding(16)
        };
        var brandLogo = new PictureBox
        {
            Image = LoadBrandLogo(),
            SizeMode = PictureBoxSizeMode.Zoom,
            Bounds = new Rectangle(52, 10, 150, 150),
            BackColor = Color.Transparent
        };
        brandCard.Controls.Add(brandLogo);
        brandCard.Controls.Add(new Label
        {
            Text = "LĂNG KÍNH HƯ KHÔNG",
            Bounds = new Rectangle(18, 160, 210, 28),
            ForeColor = Theme.Gold,
            Font = new Font("Segoe UI Semibold", 12.5f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent
        });
        brandCard.Controls.Add(new Label
        {
            Text = "TTS STUDIO 4.5  ·  HOÀNG GIA",
            Bounds = new Rectangle(16, 191, 214, 24),
            ForeColor = Color.FromArgb(245, 236, 220),
            Font = new Font("Segoe UI", 8.6f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent
        });
        grid.Controls.Add(brandCard, 0, 0);

        var navHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 0), BackColor = Color.Transparent };
        grid.Controls.Add(navHost, 0, 2);
        var items = new[] {
            ("editor", "✦  Biên tập & TTS"),
            ("library", "♫  Thư viện MP3"),
            ("merge", "☷  Ghép âm thanh"),
            ("settings", "⚙  Cài đặt")
        };
        var y = 10;
        foreach (var (id, title) in items)
        {
            var key = id;
            var button = Btn(title, ModernButton.Kind.Ghost, 236);
            button.Location = new Point(0, y);
            button.Size = new Size(236, 49);
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            button.Click += (_, _) => ShowPage(key);
            navHost.Controls.Add(button);
            _navigation[key] = button;
            y += 59;
        }

        var bottom = new SurfacePanel
        {
            Dock = DockStyle.Fill,
            Radius = 20,
            ShowBorder = true,
            BackColor = Color.FromArgb(101, 15, 29),
            Padding = new Padding(18)
        };
        bottom.Controls.Add(new Label
        {
            Text = "●  CHẠY OFFLINE TRÊN MÁY",
            ForeColor = Theme.Gold,
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
            BackColor = Color.Transparent,
            Location = new Point(10, 12), Size = new Size(214, 24)
        });
        bottom.Controls.Add(new Label
        {
            Text = "Giữ nguyên nghiệp vụ TTS / MP3 / SRT\nTập trung giao diện vàng · đỏ · trắng chuyên nghiệp",
            ForeColor = Color.FromArgb(245, 236, 220),
            Font = Theme.Small,
            BackColor = Color.Transparent,
            Location = new Point(10, 42), Size = new Size(214, 50)
        });
        grid.Controls.Add(bottom, 0, 3);
        return panel;
    }

    private Control BuildHeader()
    {
        var top = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background,
            Padding = new Padding(26, 16, 28, 12) };
        var box = Theme.Table(2, 2);
        box.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        box.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 235));
        box.RowStyles.Add(new RowStyle(SizeType.Absolute, 41));
        box.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        top.Controls.Add(box);
        _headerTitle = Theme.Label("TTS Studio phong cách hoàng gia", 21, bold: true, color: Theme.Text);
        _headerSubtitle = Theme.Label("Giữ nguyên nghiệp vụ vận hành · nâng cấp giao diện vàng / đỏ / trắng · v1.1.", 9.5f, color: Theme.Muted);
        box.Controls.Add(_headerTitle, 0, 0);
        box.Controls.Add(_headerSubtitle, 0, 1);
        var chip = Theme.Label("★  PREMIUM LOCAL UI", 9, true, Theme.Gold);
        chip.TextAlign = ContentAlignment.MiddleRight;
        box.Controls.Add(chip, 1, 0);
        return top;
    }

    private Control BuildFooter()
    {
        var bottom = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background,
            Padding = new Padding(26, 4, 26, 10) };
        var holder = new SurfacePanel { Dock = DockStyle.Fill, Radius = 16, Padding = new Padding(18, 6, 18, 6), BackColor = Theme.Surface };
        var row = Theme.Table(2, 1);
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        _footerStatus = Theme.Label("Sẵn sàng xử lý tại máy.", 9, color: Theme.Muted);
        _footerCount = Theme.Label("0 MP3", 9, color: Theme.Gold);
        _footerCount.TextAlign = ContentAlignment.MiddleRight;
        row.Controls.Add(_footerStatus, 0, 0);
        row.Controls.Add(_footerCount, 1, 0);
        holder.Controls.Add(row);
        bottom.Controls.Add(holder);
        return bottom;
    }

    private void RegisterPage(string id, Control page)
    {
        page.Dock = DockStyle.Fill;
        page.Visible = false;
        _pages[id] = page;
        _pageHost.Controls.Add(page);
    }

    private void ShowPage(string id)
    {
        if (!_pages.ContainsKey(id)) return;
        _activePage = id;
        foreach (var (key, page) in _pages) page.Visible = key == id;
        _pages[id].BringToFront();
        foreach (var (key, button) in _navigation)
        {
            button.Style = key == id ? ModernButton.Kind.Primary : ModernButton.Kind.Ghost;
            button.Invalidate();
        }
        (_headerTitle.Text, _headerSubtitle.Text) = id switch
        {
            "library" => ("Thư viện MP3", "Quản lý thư viện âm thanh với giao diện mới sang trọng, dễ nhìn và chuyên nghiệp."),
            "merge" => ("Ghép MP3 + SRT đồng bộ | v1.1", "1× nối MP3 nguyên gốc (-c copy). Đổi tốc độ: mã hóa một lần chất lượng cao, đồng bộ SRT."),
            "settings" => ("Cấu hình ứng dụng", "Quản lý đường dẫn lưu trữ, model giọng và engine offline với giao diện mới."),
            _ => ("TTS đa ngôn ngữ | v1.1", "Giao diện vàng · đỏ · trắng mới; vẫn giữ nguyên luồng TTS 1× → FFmpeg → MP3 + SRT.")
        };
        if (id == "library") RefreshLibrary();
        if (id == "merge") RefreshMergeList();
        if (id == "settings") RefreshDependencies();
    }

    private Control BuildEditorPage()
    {
        var page = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background,
            Padding = new Padding(24, 5, 24, 20) };
        var cols = Theme.Table(2, 1);
        cols.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        cols.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        page.Controls.Add(cols);

        var left = new SurfacePanel { Dock = DockStyle.Fill, Padding = new Padding(19), Margin = new Padding(0, 0, 10, 0) };
        var editing = Theme.Table(1, 4);
        editing.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        editing.RowStyles.Add(new RowStyle(SizeType.Absolute, 53));
        editing.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        editing.RowStyles.Add(new RowStyle(SizeType.Absolute, 105));
        left.Controls.Add(editing);
        cols.Controls.Add(left, 0, 0);

        var editorTop = Theme.Table(2, 2);
        editorTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editorTop.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 156));
        editorTop.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        editorTop.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        editorTop.Controls.Add(Theme.Label("NỘI DUNG VĂN BẢN", 12, true), 0, 0);
        editorTop.Controls.Add(Theme.Label("UTF-8  ·  TXT / Markdown  ·  Giao diện mới", 9, color: Theme.Muted), 0, 1);
        _charCount = Theme.Label("0 ký tự", 12, true, Theme.Gold);
        _charCount.TextAlign = ContentAlignment.MiddleRight;
        editorTop.Controls.Add(_charCount, 1, 0);
        editing.Controls.Add(editorTop, 0, 0);

        var editorButtons = new FlowLayoutPanel { Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight, WrapContents = true, BackColor = Color.Transparent };
        var import = Btn("⊕  Nhập TXT / MD", ModernButton.Kind.Secondary, 152);
        import.Click += (_, _) => ImportText();
        var normalize = Btn("✓  Chuẩn hóa dấu câu", ModernButton.Kind.Secondary, 172);
        normalize.Click += (_, _) => _editor.Text = TextProcessor.NormalizeForTts(_editor.Text);
        var clear = Btn("✕  Xóa", ModernButton.Kind.Ghost, 87);
        clear.Click += (_, _) => { if (MessageBox.Show("Xóa nội dung đang nhập?", "Xác nhận",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) _editor.Clear(); };
        editorButtons.Controls.AddRange([import, normalize, clear]);
        editing.Controls.Add(editorButtons, 0, 1);

        _editor = new RichTextBox
        {
            Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Inset,
            ForeColor = Theme.Text, Font = new Font("Segoe UI", 11f),
            DetectUrls = false, WordWrap = true, ScrollBars = RichTextBoxScrollBars.Vertical,
            MaxLength = int.MaxValue, AcceptsTab = true, EnableAutoDragDrop = false,
            Text = "", HideSelection = false
        };
        _editor.TextChanged += (_, _) => UpdateCounts();
        _editor.AllowDrop = true;
        _editor.DragEnter += (_, e) => { if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
        _editor.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) ImportTextFile(files[0]);
        };
        editing.Controls.Add(_editor, 0, 2);

        var below = Theme.Table(1, 3);
        below.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        below.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        below.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        _wordCount = Theme.Label("0 từ", 9, color: Theme.Muted);
        below.Controls.Add(_wordCount, 0, 0);
        _textWarning = Theme.Label("Tệp TXT/MD được chuẩn hóa dấu câu trước khi render.", 9, color: Theme.Muted);
        below.Controls.Add(_textWarning, 0, 1);
        below.Controls.Add(Theme.Label("MẸO: Ctrl+O để nhập tệp · Ctrl+Enter để tạo MP3", 8.5f, color: Theme.Muted), 0, 2);
        editing.Controls.Add(below, 0, 3);

        var right = new SurfacePanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20), Margin = new Padding(10, 0, 0, 0) };
        cols.Controls.Add(right, 1, 0);
        var options = Theme.Table(1, 14);
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));  // 0 header
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));  // 1 language label
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 41));  // 2 language selection
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));  // 3 group label
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 41));  // 4 group selection
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));  // 5 voice label
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));  // 6 voice selection
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));  // 7 speaker label
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));  // 8 speaker selection
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));  // 9 speed label
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));  // 10 speed selection
        options.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 11 SRT & info
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 63));  // 12 render
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));  // 13 cancel
        // Right-hand configuration scrolls at 1100x730 so all language/voice controls remain accessible.
        options.Dock = DockStyle.Top;
        options.Height = 755;
        right.Controls.Add(options);
        options.Controls.Add(Theme.Label("CẤU HÌNH GIỌNG ĐỌC  ·  ĐA NGÔN NGỮ  ·  PREMIUM UI", 11, true), 0, 0);
        options.Controls.Add(Theme.Label("Ngôn ngữ đọc (lọc theo model ONNX)", 9, color: Theme.Muted), 0, 1);
        _languageCombo = Theme.Combo();
        _languageCombo.Dock = DockStyle.Fill;
        _languageCombo.SelectedIndexChanged += (_, _) => { if (!_loadingVoices) RefilterVoices(); };
        options.Controls.Add(_languageCombo, 0, 2);
        options.Controls.Add(Theme.Label("Nhóm giọng", 9, color: Theme.Muted), 0, 3);
        _groupCombo = Theme.Combo();
        _groupCombo.Items.AddRange(["Tất cả", "Nữ", "Nam", "Khác"]);
        _groupCombo.SelectedIndexChanged += (_, _) => { if (!_loadingVoices) RefilterVoices(); };
        _groupCombo.Dock = DockStyle.Fill;
        options.Controls.Add(_groupCombo, 0, 4);
        options.Controls.Add(Theme.Label("Giọng đọc (ONNX phù hợp ngôn ngữ)", 9, color: Theme.Muted), 0, 5);
        _voiceCombo = Theme.Combo();
        _voiceCombo.Dock = DockStyle.Fill;
        _voiceCombo.SelectedIndexChanged += (_, _) => UpdateSpeakers();
        options.Controls.Add(_voiceCombo, 0, 6);
        options.Controls.Add(Theme.Label("Speaker (nếu model hỗ trợ)", 9, color: Theme.Muted), 0, 7);
        _speakerCombo = Theme.Combo();
        _speakerCombo.Dock = DockStyle.Fill;
        options.Controls.Add(_speakerCombo, 0, 8);
        options.Controls.Add(Theme.Label("Tốc độ MP3 (FFmpeg, sau TTS 1×)", 9, color: Theme.Muted), 0, 9);
        _speedCombo = Theme.Combo();
        foreach (var speed in AudioPipeline.SupportedSpeeds.OrderBy(x => x))
            _speedCombo.Items.Add(new SpeedOption(speed));
        _speedCombo.Dock = DockStyle.Fill;
        _speedCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_speedCombo.SelectedItem is SpeedOption speed)
            { _storage.Settings.Speed = speed.Value; _storage.SaveSettings(); }
        };
        options.Controls.Add(_speedCombo, 0, 10);

        var messagePanel = Theme.Table(1, 6);
        messagePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        messagePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        messagePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        messagePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        messagePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        messagePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _engineCombo = Theme.Combo();
        _engineCombo.Dock = DockStyle.Fill;
        _engineCombo.Items.AddRange(["CPU · Persistent Piper (ổn định)", "CUDA · GPU (nếu Piper hỗ trợ)"]);
        _engineCombo.SelectedIndex = 0;
        messagePanel.Controls.Add(_engineCombo, 0, 0);
        _srtOnRender = new CheckBox
        {
            Text = "Xuất kèm phụ đề .SRT (từ văn bản gốc)",
            Checked = true, AutoSize = true, Dock = DockStyle.Fill,
            ForeColor = Theme.Green, BackColor = Color.Transparent, Font = Theme.Body
        };
        _srtOnRender.CheckedChanged += (_, _) =>
        {
            if (_renderButton is not null)
                _renderButton.Text = _srtOnRender.Checked ? "▶  TẠO MP3 + SRT" : "▶  TẠO MP3";
        };
        messagePanel.Controls.Add(_srtOnRender, 0, 1);
        _voiceLanguageHint = Theme.Label("Chọn ngôn ngữ và giọng ONNX tương ứng.", 8.5f, color: Theme.Muted);
        messagePanel.Controls.Add(_voiceLanguageHint, 0, 2);
        messagePanel.Controls.Add(Theme.Label("Piper nạp model 1 lần; checkpoint theo 80 câu; MP3 1× → FFmpeg.", 8.5f, color: Theme.Muted), 0, 3);
        _renderStatus = Theme.Label("Chưa có tác vụ.", 9, color: Theme.Muted);
        messagePanel.Controls.Add(_renderStatus, 0, 4);
        _progress = new ModernProgressBar { Dock = DockStyle.Fill, Margin = new Padding(0, 5, 0, 0) };
        messagePanel.Controls.Add(_progress, 0, 5);
        options.Controls.Add(messagePanel, 0, 11);

        var doRender = Theme.Table(1, 1);
        _renderButton = Btn("▶  TẠO MP3 + SRT", ModernButton.Kind.Primary);
        _renderButton.Dock = DockStyle.Fill;
        _renderButton.Click += async (_, _) => await StartRenderAsync();
        doRender.Controls.Add(_renderButton, 0, 0);
        options.Controls.Add(doRender, 0, 12);
        var cancelPanel = Theme.Table(1, 2);
        cancelPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        cancelPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        _cancelButton = Btn("■  Dừng xử lý", ModernButton.Kind.Destructive);
        _cancelButton.Dock = DockStyle.Fill;
        _cancelButton.Enabled = false;
        _cancelButton.Click += (_, _) => _jobCancellation?.Cancel();
        cancelPanel.Controls.Add(_cancelButton, 0, 0);
        cancelPanel.Controls.Add(Theme.Label("Offline · Cần model riêng cho từng ngôn ngữ.", 8.5f, color: Theme.Muted), 0, 1);
        options.Controls.Add(cancelPanel, 0, 13);
        return page;
    }

    private Control BuildLibraryPage()
    {
        var page = new Panel { BackColor = Theme.Background, Padding = new Padding(24, 5, 24, 20) };
        var card = new SurfacePanel { Dock = DockStyle.Fill, Padding = new Padding(18) };
        page.Controls.Add(card);
        var layout = Theme.Table(1, 4);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 134));
        card.Controls.Add(layout);
        var heading = Theme.Table(2, 1);
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        heading.Controls.Add(Theme.Label("DANH SÁCH MP3 ĐÃ TẠO", 12, true), 0, 0);
        _searchLibrary = Theme.Textbox();
        _searchLibrary.Dock = DockStyle.Fill;
        _searchLibrary.PlaceholderText = "Tìm theo tên, giọng đọc hoặc ID...";
        _searchLibrary.TextChanged += (_, _) => RefreshLibrary();
        heading.Controls.Add(_searchLibrary, 1, 0);
        layout.Controls.Add(heading, 0, 0);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, AutoScroll = true, WrapContents = false };
        var listen = Btn("▶  Nghe", ModernButton.Kind.Primary, 103);
        listen.Click += (_, _) => PlaySelected();
        var copy = Btn("↓  Tải bản sao", ModernButton.Kind.Secondary, 130);
        copy.Click += (_, _) => ExportSelected();
        var rename = Btn("✎  Đổi tên", ModernButton.Kind.Secondary, 110);
        rename.Click += (_, _) => RenameSelected();
        var addMerge = Btn("＋  Đưa vào ghép", ModernButton.Kind.Secondary, 143);
        addMerge.Click += (_, _) => AddSelectedToMerge();
        var remove = Btn("✕  Xóa", ModernButton.Kind.Destructive, 93);
        remove.Click += (_, _) => DeleteSelected();
        var folder = Btn("▣  Mở thư mục", ModernButton.Kind.Ghost, 128);
        folder.Click += (_, _) => OpenFolder(_storage.Settings.OutputFolder);
        actions.Controls.AddRange([listen, copy, rename, addMerge, remove, folder]);
        layout.Controls.Add(actions, 0, 1);
        _library = NewAudioList(checkBoxes: false);
        _library.Columns.Add("TÊN HIỂN THỊ", 255);
        _library.Columns.Add("LOẠI", 79);
        _library.Columns.Add("GIỌNG ĐỌC", 157);
        _library.Columns.Add("TỐC ĐỘ", 90);
        _library.Columns.Add("NGÀY TẠO", 140);
        _library.Columns.Add("DUNG LƯỢNG", 120);
        _library.Columns.Add("ID", 152);
        _library.ItemActivate += (_, _) => PlaySelected();
        layout.Controls.Add(_library, 0, 2);
        layout.Controls.Add(BuildPlayerBar(), 0, 3);
        return page;
    }

    private Control BuildPlayerBar()
    {
        var player = new SurfacePanel { Dock = DockStyle.Fill, BackColor = Theme.Surface,
            Margin = new Padding(0, 15, 0, 0), Padding = new Padding(12) };
        var table = Theme.Table(3, 2);
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 106));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
        player.Controls.Add(table);
        _playerPlayButton = Btn("▶  Nghe", ModernButton.Kind.Primary, 96);
        _playerPlayButton.Dock = DockStyle.Fill;
        _playerPlayButton.Click += (_, _) => TogglePlayer();
        table.Controls.Add(_playerPlayButton, 0, 0);
        _playerTitle = Theme.Label("Chưa phát MP3", 10, true);
        table.Controls.Add(_playerTitle, 1, 0);
        var stop = Btn("■  Dừng", ModernButton.Kind.Ghost, 108);
        stop.Dock = DockStyle.Fill;
        stop.Click += (_, _) => StopPlayback();
        table.Controls.Add(stop, 2, 0);
        _playerSeek = new TrackBar
        {
            Dock = DockStyle.Fill, Minimum = 0, Maximum = 1000, TickStyle = TickStyle.None,
            BackColor = Theme.Surface, ForeColor = Theme.Blue, Margin = new Padding(0, 4, 4, 0)
        };
        _playerSeek.MouseDown += (_, _) => _isSeeking = true;
        _playerSeek.MouseUp += (_, _) =>
        {
            try
            {
                var length = _player.DurationMs;
                if (length > 0) _player.Seek((int)(length * _playerSeek.Value / 1000d));
            }
            catch (Exception ex) { SetStatus(ex.Message, Theme.Danger); }
            finally { _isSeeking = false; }
        };
        table.Controls.Add(_playerSeek, 1, 1);
        _playerTime = Theme.Label("00:00 / 00:00", 9, color: Theme.Muted);
        _playerTime.TextAlign = ContentAlignment.MiddleRight;
        table.Controls.Add(_playerTime, 2, 1);
        return player;
    }

    private Control BuildMergePage()
    {
        var page = new Panel { BackColor = Theme.Background, Padding = new Padding(24, 5, 24, 20) };
        var card = new SurfacePanel { Dock = DockStyle.Fill, Padding = new Padding(18) };
        page.Controls.Add(card);
        var grid = Theme.Table(1, 4);
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 146));
        card.Controls.Add(grid);
        var titleRow = Theme.Table(2, 1);
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        titleRow.Controls.Add(Theme.Label("DANH SÁCH GHÉP MP3 + SRT  |  v1.1", 12, true), 0, 0);
        _mergeHint = Theme.Label("0 đoạn được chọn", 9, color: Theme.Gold);
        _mergeHint.TextAlign = ContentAlignment.MiddleRight;
        titleRow.Controls.Add(_mergeHint, 1, 0);
        grid.Controls.Add(titleRow, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, WrapContents = false, AutoScroll = true };
        var all = Btn("✓  Chọn tất cả", ModernButton.Kind.Secondary, 131);
        all.Click += (_, _) => CheckAllMerge(true);
        var none = Btn("□  Bỏ chọn", ModernButton.Kind.Ghost, 120);
        none.Click += (_, _) => CheckAllMerge(false);
        var up = Btn("↑  Đưa lên", ModernButton.Kind.Secondary, 110);
        up.Click += (_, _) => MoveMergeSelection(-1);
        var down = Btn("↓  Đưa xuống", ModernButton.Kind.Secondary, 118);
        down.Click += (_, _) => MoveMergeSelection(1);
        var reset = Btn("⟳  Tải lại danh sách", ModernButton.Kind.Ghost, 158);
        reset.Click += (_, _) => RefreshMergeList();
        var mergeSpeedLabel = Theme.Label("Tốc độ (1×: không mã hóa lại):", 9, true, Theme.Gold);
        mergeSpeedLabel.AutoSize = true;
        mergeSpeedLabel.Margin = new Padding(15, 12, 4, 0);
        _mergeSpeedCombo = Theme.Combo();
        _mergeSpeedCombo.Width = 96;
        foreach (var speed in new[] { 0.5, 1.0, 1.25, 1.5, 2.0 })
            _mergeSpeedCombo.Items.Add(new SpeedOption(speed));
        _mergeSpeedCombo.SelectedIndex = 1;
        actions.Controls.Add(mergeSpeedLabel);
        actions.Controls.Add(_mergeSpeedCombo);
        grid.Controls.Add(actions, 0, 1);
        _mergeList = NewAudioList(checkBoxes: true);
        _mergeList.Columns.Add("CHỌN / THỨ TỰ", 170);
        _mergeList.Columns.Add("TÊN FILE", 380);
        _mergeList.Columns.Add("LOẠI", 108);
        _mergeList.Columns.Add("SRT", 92);
        _mergeList.Columns.Add("NGÀY TẠO", 155);
        _mergeList.Columns.Add("DUNG LƯỢNG", 120);
        _mergeList.ItemChecked += (_, _) => { if (!_suppressMergeRefresh) RefreshMergeHint(); };
        grid.Controls.Add(_mergeList, 0, 2);

        var export = Theme.Table(3, 2);
        export.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        export.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 177));
        export.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        export.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        export.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        var nameLabel = Theme.Label("TÊN FILE MP3 SAU GHÉP", 9, true, Theme.Muted);
        export.Controls.Add(nameLabel, 0, 0);
        _mergeProgress = new ModernProgressBar { Dock = DockStyle.Fill, Margin = new Padding(12, 15, 5, 12) };
        export.Controls.Add(_mergeProgress, 1, 0);
        export.SetColumnSpan(_mergeProgress, 2);
        _mergeTitle = Theme.Textbox("Tổng hợp các chương");
        _mergeTitle.Dock = DockStyle.Fill;
        export.Controls.Add(_mergeTitle, 0, 1);
        _mergeButton = Btn("♫ GHÉP MP3 + SRT", ModernButton.Kind.Primary, 170);
        _mergeButton.Dock = DockStyle.Fill;
        _mergeButton.Margin = new Padding(10, 6, 5, 6);
        _mergeButton.Click += async (_, _) => await StartMergeAsync();
        export.Controls.Add(_mergeButton, 1, 1);
        var cancel = Btn("■  Dừng", ModernButton.Kind.Destructive, 136);
        cancel.Dock = DockStyle.Fill;
        cancel.Margin = new Padding(5, 6, 0, 6);
        cancel.Click += (_, _) => _jobCancellation?.Cancel();
        export.Controls.Add(cancel, 2, 1);
        grid.Controls.Add(export, 0, 3);
        var advanced = Btn("⚙ Tốc độ từng file / thêm MP3", ModernButton.Kind.Secondary, 250);
        advanced.Click += (_, _) => OpenAdvancedMergeDialog();
        actions.Controls.Add(advanced);
        actions.Controls.AddRange([all, none, up, down, reset]);
        return page;
    }

    private Control BuildSettingsPage()
    {
        var page = new Panel { BackColor = Theme.Background, Padding = new Padding(24, 5, 24, 20) };
        var card = new SurfacePanel { Dock = DockStyle.Fill, Padding = new Padding(24) };
        page.Controls.Add(card);
        var layout = Theme.Table(1, 8);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 57));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(layout);
        layout.Controls.Add(Theme.Label("ĐƯỜNG DẪN & THÀNH PHẦN ENGINE  ·  STUDIO UI", 12, true), 0, 0);
        layout.Controls.Add(Theme.Label("Nơi lưu thư viện MP3", 10, true), 0, 1);
        _outputPath = AddFolderBrowser(layout, 2, _storage.Settings.OutputFolder, () => "output");
        layout.Controls.Add(Theme.Label("Thư mục chứa model ONNX (Voices)", 10, true), 0, 3);
        _voicesPath = AddFolderBrowser(layout, 4, _storage.Settings.VoicesFolder, () => "voices");
        layout.Controls.Add(Theme.Label("Thư mục Runtime (chứa piper/ và ffmpeg/)", 10, true), 0, 5);
        _runtimePath = AddFolderBrowser(layout, 6, _storage.Settings.RuntimeFolder, () => "runtime");
        var info = Theme.Table(1, 4);
        info.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        info.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
        info.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        info.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _dependencies = Theme.Label("Đang kiểm tra engine...", 10, color: Theme.Muted);
        info.Controls.Add(_dependencies, 0, 0);
        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        var reload = Btn("↻  Tải lại giọng", ModernButton.Kind.Secondary, 159);
        reload.Click += (_, _) => { ReloadVoices(); RefreshDependencies(); };
        var history = Btn("♧  Dọn bản ghi thiếu file", ModernButton.Kind.Ghost, 216);
        history.Click += (_, _) => { _storage.PurgeMissing(); RefreshLibrary(); RefreshMergeList(); SetStatus("Đã dọn các bản ghi mất file."); };
        var appData = Btn("▣  Mở AppData", ModernButton.Kind.Ghost, 146);
        appData.Click += (_, _) => OpenFolder(_storage.DataFolder);
        controls.Controls.AddRange([reload, history, appData]);
        info.Controls.Add(controls, 0, 1);
        info.Controls.Add(Theme.Label("Lưu ý: file MP3 được giữ khi thoát app. Bạn có thể xóa thủ công trong Thư viện.\nKhông xóa file nguồn khi ghép; dữ liệu và cài đặt được lưu trên máy.", 9, color: Theme.Muted), 0, 2);
        layout.Controls.Add(info, 0, 7);
        return page;
    }

    private TextBox AddFolderBrowser(TableLayoutPanel layout, int row, string path, Func<string> folderKind)
    {
        var grid = Theme.Table(2, 1);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        var input = Theme.Textbox(path);
        input.Dock = DockStyle.Fill;
        grid.Controls.Add(input, 0, 0);
        var browse = Btn("▣  Chọn thư mục", ModernButton.Kind.Secondary, 132);
        browse.Dock = DockStyle.Fill;
        browse.Margin = new Padding(12, 6, 0, 6);
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { Description = "Chọn thư mục cho " + folderKind(),
                UseDescriptionForTitle = true, InitialDirectory = Directory.Exists(input.Text) ? input.Text : AppContext.BaseDirectory };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                input.Text = dialog.SelectedPath;
                UpdateFolderSetting(folderKind(), input.Text);
            }
        };
        grid.Controls.Add(browse, 1, 0);
        input.Leave += (_, _) => UpdateFolderSetting(folderKind(), input.Text);
        layout.Controls.Add(grid, 0, row);
        return input;
    }

    private static ListView NewAudioList(bool checkBoxes)
    {
        return new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
            HideSelection = false, CheckBoxes = checkBoxes, MultiSelect = true,
            BackColor = Theme.Inset, ForeColor = Theme.Text, Font = new Font("Segoe UI", 10),
            BorderStyle = BorderStyle.FixedSingle, GridLines = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable
        };
    }

    private sealed record SpeedOption(double Value)
    {
        public override string ToString() => $"{Value:0.##}×";
    }
}
