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
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 244));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(root);
        root.Controls.Add(BuildSidebar(), 0, 0);

        var content = Theme.Table(1, 3);
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        root.Controls.Add(content, 1, 0);
        content.Controls.Add(BuildHeader(), 0, 0);
        content.Controls.Add(_pageHost, 0, 1);
        content.Controls.Add(BuildFooter(), 0, 2);

        RegisterPage("editor", BuildEditorPage());
        RegisterPage("library", BuildLibraryPage());
        RegisterPage("merge", BuildMergePage());
        RegisterPage("settings", BuildSettingsAndDocsPage());
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

        var fallback = new Bitmap(160, 160);
        using (var g = Graphics.FromImage(fallback))
        using (var ring = new Pen(Theme.Blue, 6))
        using (var bg = new SolidBrush(Theme.SidebarAccent))
        using (var font = new Font("Segoe UI Semibold", 26, FontStyle.Bold))
        using (var brush = new SolidBrush(Color.White))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            g.FillEllipse(bg, 8, 8, 144, 144);
            g.DrawEllipse(ring, 8, 8, 144, 144);
            g.DrawString("LKH", font, brush, new RectangleF(16, 52, 128, 56),
                new StringFormat { Alignment = StringAlignment.Center });
        }
        return fallback;
    }

    private Control BuildSidebar()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Sidebar, Padding = new Padding(12, 14, 12, 14) };
        var grid = Theme.Table(1, 4);
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 14));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        panel.Controls.Add(grid);

        var brandCard = new SurfacePanel
        {
            Dock = DockStyle.Fill,
            Radius = 10,
            ShowBorder = true,
            BackColor = Theme.SidebarAccent,
            Padding = new Padding(10)
        };
        var brandLogo = new PictureBox
        {
            Image = LoadBrandLogo(),
            SizeMode = PictureBoxSizeMode.Zoom,
            Bounds = new Rectangle(10, 14, 50, 50),
            BackColor = Color.Transparent
        };
        brandCard.Controls.Add(brandLogo);
        brandCard.Controls.Add(new Label
        {
            Text = "LĂNG KÍNH HƯ KHÔNG",
            Bounds = new Rectangle(68, 14, 148, 20),
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI Semibold", 9.25f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent
        });
        brandCard.Controls.Add(new Label
        {
            Text = "CRIMSON STUDIO v1.4",
            Bounds = new Rectangle(68, 35, 148, 18),
            ForeColor = Theme.Muted,
            Font = new Font("Segoe UI", 8f, FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent
        });
        brandCard.Controls.Add(new Label
        {
            Text = "● Multi-Stream · Tray Ready",
            Bounds = new Rectangle(68, 53, 148, 18),
            ForeColor = Theme.AccentText,
            Font = new Font("Segoe UI Semibold", 7.75f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent
        });
        grid.Controls.Add(brandCard, 0, 0);

        var navHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 0), BackColor = Theme.Sidebar };
        grid.Controls.Add(navHost, 0, 2);
        var items = new[]
        {
            ("editor",   "1. Biên tập (0)"),
            ("library",  "2. Nơi lưu"),
            ("merge",    "3. Ghép Audio"),
            ("settings", "4. Cài đặt và tài liệu")
        };
        var y = 4;
        foreach (var (id, title) in items)
        {
            var key = id;
            var button = Btn(title, ModernButton.Kind.Ghost, 220);
            button.Location = new Point(0, y);
            button.Size = new Size(220, 40);
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            button.Click += (_, _) => ShowPage(key);
            navHost.Controls.Add(button);
            _navigation[key] = button;
            y += 46;
        }

        var bottom = new SurfacePanel
        {
            Dock = DockStyle.Fill,
            Radius = 10,
            ShowBorder = true,
            BackColor = Theme.SidebarAccent,
            Padding = new Padding(12)
        };
        bottom.Controls.Add(new Label
        {
            Text = "CRIMSON MULTI-STREAM",
            ForeColor = Theme.AccentText,
            Font = new Font("Segoe UI Semibold", 7.75f, FontStyle.Bold),
            BackColor = Color.Transparent,
            Location = new Point(10, 8),
            Size = new Size(200, 16)
        });
        bottom.Controls.Add(new Label
        {
            Text = "• Biên tập + Đa luồng hợp nhất\n• Render song song tối đa công suất\n• Clarity Phoneme 44.1kHz 1-Pass\n• Chạy ngầm ẩn Taskbar ở Tray",
            ForeColor = Theme.Muted,
            Font = new Font("Segoe UI", 8f),
            BackColor = Color.Transparent,
            Location = new Point(10, 26),
            Size = new Size(200, 80)
        });
        grid.Controls.Add(bottom, 0, 3);
        return panel;
    }

    private Control BuildHeader()
    {
        var top = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Background,
            Padding = new Padding(18, 8, 18, 4)
        };
        var box = Theme.Table(2, 2);
        box.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        box.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 366));
        box.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        box.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        top.Controls.Add(box);
        _headerTitle = Theme.Label("Biên tập Kịch bản & Đa luồng Render", 14.5f, bold: true, color: Theme.Text);
        _headerSubtitle = Theme.Label("Soạn thảo kịch bản, điều khiển giọng đọc và theo dõi Đa luồng Render ngay trên cùng một màn hình.", 8.5f, color: Theme.Muted);
        box.Controls.Add(_headerTitle, 0, 0);
        box.Controls.Add(_headerSubtitle, 0, 1);

        var headerActions = Theme.Table(2, 1);
        headerActions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 186));
        headerActions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        box.SetRowSpan(headerActions, 2);

        _headerQueueButton = Btn("⚡ Đa luồng: 0 xong", ModernButton.Kind.Secondary, 180);
        _headerQueueButton.Dock = DockStyle.Fill;
        _headerQueueButton.Margin = new Padding(0, 6, 8, 8);
        _headerQueueButton.Click += (_, _) =>
        {
            ShowPage("editor");
            _renderQueueList?.Focus();
        };

        _headerHideTrayButton = Btn("▾ Ẩn xuống Toolbar", ModernButton.Kind.Gold, 176);
        _headerHideTrayButton.Dock = DockStyle.Fill;
        _headerHideTrayButton.Margin = new Padding(0, 6, 0, 8);
        _headerHideTrayButton.Click += (_, _) => HideToSystemTray();

        headerActions.Controls.Add(_headerQueueButton, 0, 0);
        headerActions.Controls.Add(_headerHideTrayButton, 1, 0);
        box.Controls.Add(headerActions, 1, 0);
        return top;
    }

    private Control BuildFooter()
    {
        var bottom = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Background,
            Padding = new Padding(18, 4, 18, 10)
        };
        var holder = new SurfacePanel
        {
            Dock = DockStyle.Fill,
            Radius = 10,
            Padding = new Padding(14, 5, 14, 5),
            BackColor = Theme.Surface
        };
        var grid = Theme.Table(1, 2);
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        holder.Controls.Add(grid);

        // Row 0: Global Studio Transport Player Bar
        var transport = Theme.Table(5, 1);
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 106));
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));

        _playerPlayButton = Btn("▶  Phát", ModernButton.Kind.Primary, 98);
        _playerPlayButton.Dock = DockStyle.Fill;
        _playerPlayButton.Margin = new Padding(0, 2, 8, 2);
        _playerPlayButton.Click += (_, _) => TogglePlayer();
        transport.Controls.Add(_playerPlayButton, 0, 0);

        var stop = Btn("■  Dừng", ModernButton.Kind.Secondary, 74);
        stop.Dock = DockStyle.Fill;
        stop.Margin = new Padding(0, 2, 10, 2);
        stop.Click += (_, _) => StopPlayback();
        transport.Controls.Add(stop, 1, 0);

        _playerTitle = Theme.Label("Chưa phát âm thanh (Review / Mẫu giọng / Nơi lưu)", 8.75f, true, Theme.Text);
        transport.Controls.Add(_playerTitle, 2, 0);

        _playerSeek = new TrackBar
        {
            AutoSize = false,
            Height = 24,
            Dock = DockStyle.Fill,
            Minimum = 0,
            Maximum = 1000,
            TickStyle = TickStyle.None,
            BackColor = Theme.Surface,
            ForeColor = Theme.Blue,
            Margin = new Padding(6, 5, 6, 3)
        };
        _playerSeek.MouseDown += (_, _) => _isSeeking = true;
        _playerSeek.MouseUp += (_, _) =>
        {
            try
            {
                var length = _player.DurationMs;
                if (length > 0) _player.Seek((int)(length * _playerSeek.Value / 1000d));
            }
            catch (Exception ex) { SetStatus(ex.Message, Theme.DangerText); }
            finally { _isSeeking = false; }
        };
        transport.Controls.Add(_playerSeek, 3, 0);

        _playerTime = Theme.Label("00:00 / 00:00", 8.75f, color: Theme.Muted);
        _playerTime.TextAlign = ContentAlignment.MiddleRight;
        transport.Controls.Add(_playerTime, 4, 0);
        grid.Controls.Add(transport, 0, 0);

        // Row 1: Status bar & output count
        var statusRow = Theme.Table(2, 1);
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75));
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        _footerStatus = Theme.Label("Sẵn sàng xử lý tại máy.", 8.5f, color: Theme.Muted);
        _footerCount = Theme.Label("0 MP3 trong Nơi lưu", 8.5f, true, Theme.AccentText);
        _footerCount.TextAlign = ContentAlignment.MiddleRight;
        statusRow.Controls.Add(_footerStatus, 0, 0);
        statusRow.Controls.Add(_footerCount, 1, 0);
        grid.Controls.Add(statusRow, 0, 1);

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

    private void ShowSettingsTab(string subTab)
    {
        if (!_settingsSubPages.ContainsKey(subTab)) subTab = "settings";
        _activeSettingsTab = subTab;
        foreach (var (key, ctrl) in _settingsSubPages)
            ctrl.Visible = key == subTab;
        _settingsSubPages[subTab].BringToFront();

        foreach (var (key, btn) in _settingsTabButtons)
        {
            btn.Style = key == subTab ? ModernButton.Kind.Primary : ModernButton.Kind.Ghost;
            btn.Invalidate();
        }

        if (_activePage != "settings")
        {
            ShowPage("settings");
            return;
        }

        UpdateHeaderForCurrentPage();
        if (subTab == "settings") RefreshDependencies();
        if (subTab == "voices") RefreshVoiceLabCatalog();
    }

    private void ShowPage(string id)
    {
        if (id == "queue") id = "editor";
        if (id is "voices" or "guide")
        {
            var targetTab = id;
            _activeSettingsTab = targetTab;
            id = "settings";
        }
        if (!_pages.ContainsKey(id)) return;

        _activePage = id;
        foreach (var (key, page) in _pages) page.Visible = key == id;
        _pages[id].BringToFront();
        foreach (var (key, button) in _navigation)
        {
            button.Style = key == id ? ModernButton.Kind.Primary : ModernButton.Kind.Ghost;
            button.Invalidate();
        }

        if (id == "settings")
            ShowSettingsTab(_activeSettingsTab);
        else
            UpdateHeaderForCurrentPage();

        if (id == "editor") RefreshRenderQueueUi();
        if (id == "library") RefreshLibrary();
        if (id == "merge") RefreshMergeList();
    }

    private void UpdateHeaderForCurrentPage()
    {
        (_headerTitle.Text, _headerSubtitle.Text) = _activePage switch
        {
            "library" => ("Nơi lưu Bản thu Audio (Output Folder)",
                "Quản lý các bản thu MP3 + SRT đã xuất trong thư mục Output, nghe thử, tải bản sao hoặc đưa sang Ghép Audio."),
            "merge" => ("Ghép Audio MP3 + Đồng bộ Phụ đề SRT",
                "Tốc độ 1× nối bitstream nguyên gốc (-c:a copy). Đổi tốc độ: giải mã FLAC lossless và mã hóa MP3 một lần duy nhất."),
            "settings" => _activeSettingsTab switch
            {
                "voices" => ("Cài đặt & Tài liệu · Danh sách Giọng đọc (Voice Lab)",
                    "Nghe thử từng giọng, tạo mẫu hàng loạt trong Voices/example và đổi tên chuẩn hóa cặp file .onnx + .onnx.json."),
                "guide" => ("Cài đặt & Tài liệu · Hướng dẫn Dùng & Bảng Câu lệnh Text",
                    "Tra cứu toàn bộ câu lệnh [pause], [nghi], [[ghi chú]], quy tắc dấu câu, kỹ thuật nhấn/ngân giọng và sao chép DNA cho AI."),
                _ => ("Cài đặt & Tài liệu · Cấu hình Đường dẫn & Chế độ Toolbar",
                    "Quản lý 3 thư mục chuẩn Output, Voices, Runtime và tùy chọn ẩn khỏi Taskbar chạy ngầm dưới khay hệ thống (Ctrl+M).")
            },
            _ => ("Biên tập Kịch bản & Đa luồng Render Song Song",
                "Soạn thảo kịch bản, chọn giọng đọc ở trên và quản lý hàng đợi Đa luồng Render trực tiếp ngay bên dưới.")
        };
    }

    private Control BuildEditorPage()
    {
        var page = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Background,
            Padding = new Padding(18, 4, 18, 8)
        };

        // Vertical split: Top = Manuscript Editor + Voice Inspector (62%), Bottom = Multi-Stream Render Queue (38%)
        var mainSplit = Theme.Table(1, 2);
        mainSplit.RowStyles.Add(new RowStyle(SizeType.Percent, 62f));
        mainSplit.RowStyles.Add(new RowStyle(SizeType.Percent, 38f));
        page.Controls.Add(mainSplit);

        var cols = Theme.Table(2, 1);
        cols.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58f));
        cols.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));
        mainSplit.Controls.Add(cols, 0, 0);

        // Top-Left: Manuscript Editor + Review Snippet Toolbar
        var left = new SurfacePanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 10, 14, 10),
            Margin = new Padding(0, 0, 6, 0)
        };
        var editing = Theme.Table(1, 4);
        editing.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        editing.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        editing.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        editing.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        left.Controls.Add(editing);
        cols.Controls.Add(left, 0, 0);

        var editorTop = Theme.Table(2, 2);
        editorTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editorTop.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        editorTop.RowStyles.Add(new RowStyle(SizeType.Absolute, 21));
        editorTop.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        editorTop.Controls.Add(Theme.Label("KỊCH BẢN / VĂN BẢN ĐẦU VÀO", 9.75f, true), 0, 0);
        editorTop.Controls.Add(Theme.Label("Hỗ trợ [pause:500ms], [nghi:1s], [[ghi chú]] · Bôi đen hoặc chọn số từ để Review", 8f, color: Theme.Muted), 0, 1);
        _charCount = Theme.Label("0 ký tự", 9.75f, true, Theme.AccentText);
        _charCount.TextAlign = ContentAlignment.MiddleRight;
        editorTop.Controls.Add(_charCount, 1, 0);
        editing.Controls.Add(editorTop, 0, 0);

        var toolbarGrid = Theme.Table(7, 1);
        toolbarGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));  // Import single
        toolbarGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108)); // Batch multi-file render
        toolbarGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 102)); // Normalize
        toolbarGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));  // Limit combo
        toolbarGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));  // Review btn
        toolbarGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));  // Spacer
        toolbarGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));  // Clear

        var import = Btn("⊕ Nhập TXT", ModernButton.Kind.Secondary, 92);
        import.Dock = DockStyle.Fill;
        import.Margin = new Padding(0, 2, 4, 2);
        import.Click += (_, _) => ImportText();

        var batchImport = Btn("⚡ Nhiều file", ModernButton.Kind.Primary, 104);
        batchImport.Dock = DockStyle.Fill;
        batchImport.Margin = new Padding(0, 2, 4, 2);
        batchImport.Click += (_, _) => ImportMultipleFilesToQueue();

        var normalize = Btn("✓ Chuẩn hóa", ModernButton.Kind.Secondary, 98);
        normalize.Dock = DockStyle.Fill;
        normalize.Margin = new Padding(0, 2, 4, 2);
        normalize.Click += (_, _) => _editor.Text = TextProcessor.NormalizeForTts(_editor.Text);

        _reviewLimitCombo = Theme.Combo();
        _reviewLimitCombo.Margin = new Padding(0, 3, 4, 3);
        foreach (var limit in new[] { 25, 50, 100, 150, 250, 500 })
            _reviewLimitCombo.Items.Add(new WordLimitOption(limit));
        _reviewLimitCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_reviewLimitCombo.SelectedItem is WordLimitOption opt)
            {
                _storage.Settings.ReviewWordLimit = opt.Words;
                _storage.SaveSettings();
                if (_reviewButton is not null)
                    _reviewButton.Text = $"🎧 Nghe thử ({opt.Words} từ)";
            }
        };

        _reviewToolbarButton = Btn("🎧 Nghe thử", ModernButton.Kind.Gold, 90);
        _reviewToolbarButton.Dock = DockStyle.Fill;
        _reviewToolbarButton.Margin = new Padding(0, 2, 4, 2);
        _reviewToolbarButton.Click += async (_, _) => await StartReviewSnippetAsync();

        var clear = Btn("✕ Xóa", ModernButton.Kind.Secondary, 56);
        clear.Dock = DockStyle.Fill;
        clear.Margin = new Padding(0, 2, 0, 2);
        clear.Click += (_, _) =>
        {
            if (MessageBox.Show("Xóa toàn bộ nội dung đang nhập?", "Xác nhận",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) _editor.Clear();
        };

        toolbarGrid.Controls.Add(import, 0, 0);
        toolbarGrid.Controls.Add(batchImport, 1, 0);
        toolbarGrid.Controls.Add(normalize, 2, 0);
        toolbarGrid.Controls.Add(_reviewLimitCombo, 3, 0);
        toolbarGrid.Controls.Add(_reviewToolbarButton, 4, 0);
        toolbarGrid.Controls.Add(clear, 6, 0);
        editing.Controls.Add(toolbarGrid, 0, 1);

        _editor = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.InputBg,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 10.25f),
            DetectUrls = false,
            WordWrap = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            MaxLength = int.MaxValue,
            AcceptsTab = true,
            EnableAutoDragDrop = false,
            Text = "",
            HideSelection = false,
            Margin = new Padding(0, 4, 0, 4)
        };
        _editor.TextChanged += (_, _) => UpdateCounts();
        _editor.AllowDrop = true;
        _editor.DragEnter += (_, e) => { if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
        _editor.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) ImportTextFile(files[0]);
        };
        editing.Controls.Add(_editor, 0, 2);

        var below = Theme.Table(1, 2);
        below.RowStyles.Add(new RowStyle(SizeType.Absolute, 19));
        below.RowStyles.Add(new RowStyle(SizeType.Absolute, 19));
        _wordCount = Theme.Label("0 từ", 8.5f, true, Theme.Text);
        below.Controls.Add(_wordCount, 0, 0);
        _textWarning = Theme.Label("Ctrl+O mở tệp · Ctrl+Enter thêm vào Đa luồng Render bên dưới · Ctrl+M ẩn xuống Toolbar · Ctrl+1..4 chuyển mục", 8f, color: Theme.Muted);
        below.Controls.Add(_textWarning, 0, 1);
        editing.Controls.Add(below, 0, 3);

        // Top-Right: Compact Voice & Synthesis Inspector
        var right = new SurfacePanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 8, 14, 8),
            Margin = new Padding(6, 0, 0, 0)
        };
        cols.Controls.Add(right, 1, 0);

        var options = Theme.Table(1, 13);
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 21));  // 0 Header
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));  // 1 Language + Category row
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));  // 2 Quick search box
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));  // 3 Voice combo
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));  // 4 Quick voice actions
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));  // 5 Speaker + Speed + Engine label
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));  // 6 Speaker + Speed + Engine row
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));  // 7 Title + Parallel threads label
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));  // 8 Title + Parallel threads row
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));  // 9 SRT checkbox
        options.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 10 Status & progress card
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));  // 11 Review + Cancel row
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));  // 12 Enqueue Multi-Render CTA
        right.Controls.Add(options);

        options.Controls.Add(Theme.Label("BẢNG ĐIỀU KHIỂN GIỌNG ĐỌC", 9.75f, true), 0, 0);

        var filterGrid = Theme.Table(2, 1);
        filterGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        filterGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        _languageCombo = Theme.Combo();
        _languageCombo.Margin = new Padding(0, 2, 4, 2);
        _languageCombo.SelectedIndexChanged += (_, _) => { if (!_loadingVoices) RefilterVoices(); };
        _groupCombo = Theme.Combo();
        _groupCombo.Items.AddRange(["Tất cả", "Nữ", "Nam", "Khác"]);
        _groupCombo.Margin = new Padding(4, 2, 0, 2);
        _groupCombo.SelectedIndexChanged += (_, _) => { if (!_loadingVoices) RefilterVoices(); };
        filterGrid.Controls.Add(_languageCombo, 0, 0);
        filterGrid.Controls.Add(_groupCombo, 1, 0);
        options.Controls.Add(filterGrid, 0, 1);

        _voiceSearchBox = Theme.Textbox();
        _voiceSearchBox.Margin = new Padding(0, 1, 0, 2);
        _voiceSearchBox.PlaceholderText = "🔍 Lọc nhanh tên giọng hoặc tên file .onnx...";
        _voiceSearchBox.TextChanged += (_, _) => { if (!_loadingVoices) RefilterVoices(); };
        options.Controls.Add(_voiceSearchBox, 0, 2);

        _voiceCombo = Theme.Combo();
        _voiceCombo.Margin = new Padding(0, 2, 0, 2);
        _voiceCombo.SelectedIndexChanged += (_, _) => UpdateSpeakers();
        options.Controls.Add(_voiceCombo, 0, 3);

        var voiceActionsGrid = Theme.Table(3, 1);
        voiceActionsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        voiceActionsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        voiceActionsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));

        _previewVoiceButton = Btn("▶ Nghe mẫu", ModernButton.Kind.Gold);
        _previewVoiceButton.Dock = DockStyle.Fill;
        _previewVoiceButton.Margin = new Padding(0, 2, 4, 2);
        _previewVoiceButton.Click += async (_, _) => await PreviewSelectedEditorVoiceAsync();

        _renameVoiceQuickButton = Btn("✎ Đổi tên", ModernButton.Kind.Secondary);
        _renameVoiceQuickButton.Dock = DockStyle.Fill;
        _renameVoiceQuickButton.Margin = new Padding(3, 2, 3, 2);
        _renameVoiceQuickButton.Click += (_, _) =>
        {
            if (_voiceCombo.SelectedItem is VoiceModel v) PromptRenameVoice(v);
        };

        var openVoiceLabBtn = Btn("☷ DS Giọng", ModernButton.Kind.Secondary);
        openVoiceLabBtn.Dock = DockStyle.Fill;
        openVoiceLabBtn.Margin = new Padding(4, 2, 0, 2);
        openVoiceLabBtn.Click += (_, _) => ShowSettingsTab("voices");

        voiceActionsGrid.Controls.Add(_previewVoiceButton, 0, 0);
        voiceActionsGrid.Controls.Add(_renameVoiceQuickButton, 1, 0);
        voiceActionsGrid.Controls.Add(openVoiceLabBtn, 2, 0);
        options.Controls.Add(voiceActionsGrid, 0, 4);

        options.Controls.Add(Theme.Label("Speaker  ·  Tốc độ xuất MP3  ·  Phần cứng", 8f, color: Theme.Muted), 0, 5);
        var speakerSpeedEngineRow = Theme.Table(3, 1);
        speakerSpeedEngineRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        speakerSpeedEngineRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));
        speakerSpeedEngineRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));

        _speakerCombo = Theme.Combo();
        _speakerCombo.Margin = new Padding(0, 2, 4, 2);

        _speedCombo = Theme.Combo();
        foreach (var speed in AudioPipeline.SupportedSpeeds.OrderBy(x => x))
            _speedCombo.Items.Add(new SpeedOption(speed));
        _speedCombo.Margin = new Padding(3, 2, 3, 2);
        _speedCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_speedCombo.SelectedItem is SpeedOption speed)
            { _storage.Settings.Speed = speed.Value; _storage.SaveSettings(); }
        };

        _engineCombo = Theme.Combo();
        _engineCombo.Margin = new Padding(4, 2, 0, 2);
        _engineCombo.Items.AddRange(["CPU (Ổn định)", "CUDA (GPU)"]);
        _engineCombo.SelectedIndex = 0;

        speakerSpeedEngineRow.Controls.Add(_speakerCombo, 0, 0);
        speakerSpeedEngineRow.Controls.Add(_speedCombo, 1, 0);
        speakerSpeedEngineRow.Controls.Add(_engineCombo, 2, 0);
        options.Controls.Add(speakerSpeedEngineRow, 0, 6);

        options.Controls.Add(Theme.Label("Tên bản thu (tùy chọn)  ·  Công suất Đa luồng", 8f, color: Theme.Muted), 0, 7);
        var titleParallelRow = Theme.Table(2, 1);
        titleParallelRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
        titleParallelRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));

        _renderTitleInput = Theme.Textbox();
        _renderTitleInput.PlaceholderText = "Tên Audio (bỏ trống lấy dòng đầu)...";
        _renderTitleInput.Margin = new Padding(0, 1, 4, 2);

        _studioParallelCombo = Theme.Combo();
        _studioParallelCombo.Margin = new Padding(4, 1, 0, 2);
        foreach (var n in new[] { 0, 1, 2, 4, 6, 8, 12, 16, 24, 32, 64 })
            _studioParallelCombo.Items.Add(new ParallelOption(n));
        _studioParallelCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_syncingParallelCombo || _studioParallelCombo.SelectedItem is not ParallelOption opt) return;
            ApplyParallelConcurrency(opt.Count);
        };

        titleParallelRow.Controls.Add(_renderTitleInput, 0, 0);
        titleParallelRow.Controls.Add(_studioParallelCombo, 1, 0);
        options.Controls.Add(titleParallelRow, 0, 8);

        _srtOnRender = new CheckBox
        {
            Text = "Xuất kèm phụ đề .SRT chuẩn thời gian thực",
            Checked = true,
            AutoSize = false,
            Dock = DockStyle.Fill,
            ForeColor = Theme.Text,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI Semibold", 8.75f, FontStyle.Bold)
        };
        _srtOnRender.CheckedChanged += (_, _) =>
        {
            if (_renderButton is not null)
                _renderButton.Text = _srtOnRender.Checked
                    ? "▶  THÊM VÀO ĐA LUỒNG RENDER (MP3 + SRT)"
                    : "▶  THÊM VÀO ĐA LUỒNG RENDER (MP3)";
        };
        options.Controls.Add(_srtOnRender, 0, 9);

        var statusCard = new SurfacePanel
        {
            Dock = DockStyle.Fill,
            Radius = 8,
            BackColor = Theme.Inset,
            Padding = new Padding(8, 4, 8, 4),
            Margin = new Padding(0, 1, 0, 3)
        };
        var statusPanel = Theme.Table(1, 3);
        statusPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        statusPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        statusPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _voiceLanguageHint = Theme.Label("Chọn ngôn ngữ và model ONNX tương ứng.", 8f, color: Theme.Muted);
        statusPanel.Controls.Add(_voiceLanguageHint, 0, 0);
        _renderStatus = Theme.Label("Sẵn sàng xử lý đa luồng.", 8.25f, true, Theme.Text);
        statusPanel.Controls.Add(_renderStatus, 0, 1);
        _progress = new ModernProgressBar { Dock = DockStyle.Top, Margin = new Padding(0, 4, 0, 0), BackColor = Theme.Inset };
        statusPanel.Controls.Add(_progress, 0, 2);
        statusCard.Controls.Add(statusPanel);
        options.Controls.Add(statusCard, 0, 10);

        var secondaryActions = Theme.Table(2, 1);
        secondaryActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        secondaryActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));

        _reviewButton = Btn($"🎧 Nghe thử ({_storage.Settings.ReviewWordLimit} từ)", ModernButton.Kind.Gold);
        _reviewButton.Dock = DockStyle.Fill;
        _reviewButton.Margin = new Padding(0, 1, 4, 2);
        _reviewButton.Click += async (_, _) => await StartReviewSnippetAsync();

        _cancelButton = Btn("■ Dừng tất cả", ModernButton.Kind.Destructive);
        _cancelButton.Dock = DockStyle.Fill;
        _cancelButton.Margin = new Padding(4, 1, 0, 2);
        _cancelButton.Enabled = false;
        _cancelButton.Click += (_, _) =>
        {
            _jobCancellation?.Cancel();
            _renderQueue.CancelAll();
        };

        secondaryActions.Controls.Add(_reviewButton, 0, 0);
        secondaryActions.Controls.Add(_cancelButton, 1, 0);
        options.Controls.Add(secondaryActions, 0, 11);

        _renderButton = Btn("▶  THÊM VÀO ĐA LUỒNG RENDER (MP3 + SRT)", ModernButton.Kind.Primary);
        _renderButton.Dock = DockStyle.Fill;
        _renderButton.Margin = new Padding(0, 2, 0, 0);
        _renderButton.Click += (_, _) => EnqueueCurrentEditorRender();
        options.Controls.Add(_renderButton, 0, 12);

        // Bottom Integrated Section: Đa Luồng Render (0) directly below Editor & Voice Inspector
        var queueCard = new SurfacePanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 8, 14, 10),
            Margin = new Padding(0, 8, 0, 0)
        };
        var queueLayout = Theme.Table(1, 4);
        queueLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        queueLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
        queueLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        queueLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        queueCard.Controls.Add(queueLayout);

        var queueHeaderRow = Theme.Table(2, 1);
        queueHeaderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        queueHeaderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        _queueSectionTitle = Theme.Label("ĐA LUỒNG RENDER (0)", 9.5f, true);
        queueHeaderRow.Controls.Add(_queueSectionTitle, 0, 0);
        _queueSummaryLabel = Theme.Label("0 luồng đang chạy · 0 đang chờ · Sẵn sàng", 8.75f, true, Theme.AccentText);
        _queueSummaryLabel.TextAlign = ContentAlignment.MiddleRight;
        queueHeaderRow.Controls.Add(_queueSummaryLabel, 1, 0);
        queueLayout.Controls.Add(queueHeaderRow, 0, 0);

        _queueOverallProgress = new ModernProgressBar { Dock = DockStyle.Top, Height = 7, Margin = new Padding(0, 2, 0, 3) };
        queueLayout.Controls.Add(_queueOverallProgress, 0, 1);

        var queueActions = Theme.Table(8, 1);
        queueActions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));  // Label
        queueActions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 182)); // Parallel combo
        queueActions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 168)); // Batch import files
        queueActions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118)); // Play finished
        queueActions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122)); // Cancel selected
        queueActions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 106)); // Cancel all
        queueActions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126)); // Clear finished
        queueActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var parallelLabel = Theme.Label("Công suất:", 8.5f, true, Theme.Text);
        _queueParallelCombo = Theme.Combo();
        _queueParallelCombo.Margin = new Padding(0, 2, 6, 2);
        foreach (var n in new[] { 0, 1, 2, 4, 6, 8, 12, 16, 24, 32, 64 })
            _queueParallelCombo.Items.Add(new ParallelOption(n));
        _queueParallelCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_syncingParallelCombo || _queueParallelCombo.SelectedItem is not ParallelOption opt) return;
            ApplyParallelConcurrency(opt.Count);
        };

        var addFilesBtn = Btn("⚡ Chọn nhiều file TXT/MD", ModernButton.Kind.Primary);
        addFilesBtn.Dock = DockStyle.Fill;
        addFilesBtn.Margin = new Padding(0, 2, 5, 2);
        addFilesBtn.Click += (_, _) => ImportMultipleFilesToQueue();

        var playBtn = Btn("▶ Nghe bản xong", ModernButton.Kind.Gold);
        playBtn.Dock = DockStyle.Fill;
        playBtn.Margin = new Padding(0, 2, 5, 2);
        playBtn.Click += (_, _) => PlaySelectedQueueItem();

        var cancelOneBtn = Btn("■ Hủy luồng chọn", ModernButton.Kind.Secondary);
        cancelOneBtn.Dock = DockStyle.Fill;
        cancelOneBtn.Margin = new Padding(0, 2, 5, 2);
        cancelOneBtn.Click += (_, _) => CancelSelectedQueueItems();

        var cancelAllBtn = Btn("✕ Hủy tất cả", ModernButton.Kind.Destructive);
        cancelAllBtn.Dock = DockStyle.Fill;
        cancelAllBtn.Margin = new Padding(0, 2, 5, 2);
        cancelAllBtn.Click += (_, _) => _renderQueue.CancelAll();

        var clearDoneBtn = Btn("♧ Xóa luồng xong", ModernButton.Kind.Secondary);
        clearDoneBtn.Dock = DockStyle.Fill;
        clearDoneBtn.Margin = new Padding(0, 2, 0, 2);
        clearDoneBtn.Click += (_, _) => _renderQueue.ClearFinished();

        queueActions.Controls.Add(parallelLabel, 0, 0);
        queueActions.Controls.Add(_queueParallelCombo, 1, 0);
        queueActions.Controls.Add(addFilesBtn, 2, 0);
        queueActions.Controls.Add(playBtn, 3, 0);
        queueActions.Controls.Add(cancelOneBtn, 4, 0);
        queueActions.Controls.Add(cancelAllBtn, 5, 0);
        queueActions.Controls.Add(clearDoneBtn, 6, 0);
        queueLayout.Controls.Add(queueActions, 0, 2);

        _renderQueueList = NewAudioList(checkBoxes: false);
        _renderQueueList.Columns.Add("LUỒNG", 76);
        _renderQueueList.Columns.Add("TÊN AUDIO XUẤT", 280);
        _renderQueueList.Columns.Add("GIỌNG ĐỌC & CẤU HÌNH", 220);
        _renderQueueList.Columns.Add("KÝ TỰ", 110);
        _renderQueueList.Columns.Add("TIẾN ĐỘ", 170);
        _renderQueueList.Columns.Add("TRẠNG THÁI", 185);
        _renderQueueList.Columns.Add("THỜI GIAN", 105);
        _renderQueueList.ItemActivate += (_, _) => PlaySelectedQueueItem();
        queueLayout.Controls.Add(_renderQueueList, 0, 3);

        mainSplit.Controls.Add(queueCard, 0, 1);
        return page;
    }

    private Control BuildLibraryPage()
    {
        var page = new Panel { BackColor = Theme.Background, Padding = new Padding(18, 4, 18, 8) };
        var card = new SurfacePanel { Dock = DockStyle.Fill, Padding = new Padding(16) };
        page.Controls.Add(card);
        var layout = Theme.Table(1, 3);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(layout);

        var heading = Theme.Table(2, 1);
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        heading.Controls.Add(Theme.Label("NƠI LƯU BẢN THU MP3 & SRT ĐÃ XUẤT (OUTPUT)", 10f, true), 0, 0);
        _searchLibrary = Theme.Textbox();
        _searchLibrary.PlaceholderText = "🔍 Tìm theo tên bản thu, giọng đọc hoặc ID...";
        _searchLibrary.TextChanged += (_, _) => RefreshLibrary();
        heading.Controls.Add(_searchLibrary, 1, 0);
        layout.Controls.Add(heading, 0, 0);

        var actions = Theme.Table(7, 1);
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var listen = Btn("▶  Nghe thử", ModernButton.Kind.Primary);
        listen.Dock = DockStyle.Fill;
        listen.Margin = new Padding(0, 3, 6, 3);
        listen.Click += (_, _) => PlaySelected();

        var copy = Btn("↓  Tải bản sao", ModernButton.Kind.Secondary);
        copy.Dock = DockStyle.Fill;
        copy.Margin = new Padding(0, 3, 6, 3);
        copy.Click += (_, _) => ExportSelected();

        var rename = Btn("✎  Đổi tên", ModernButton.Kind.Secondary);
        rename.Dock = DockStyle.Fill;
        rename.Margin = new Padding(0, 3, 6, 3);
        rename.Click += (_, _) => RenameSelected();

        var addMerge = Btn("＋  Đưa vào ghép", ModernButton.Kind.Secondary);
        addMerge.Dock = DockStyle.Fill;
        addMerge.Margin = new Padding(0, 3, 6, 3);
        addMerge.Click += (_, _) => AddSelectedToMerge();

        var remove = Btn("✕  Xóa", ModernButton.Kind.Destructive);
        remove.Dock = DockStyle.Fill;
        remove.Margin = new Padding(0, 3, 6, 3);
        remove.Click += (_, _) => DeleteSelected();

        var folder = Btn("📂  Mở thư mục Nơi lưu", ModernButton.Kind.Gold);
        folder.Dock = DockStyle.Fill;
        folder.Margin = new Padding(0, 3, 0, 3);
        folder.Click += (_, _) => OpenFolder(_storage.Settings.OutputFolder);

        actions.Controls.Add(listen, 0, 0);
        actions.Controls.Add(copy, 1, 0);
        actions.Controls.Add(rename, 2, 0);
        actions.Controls.Add(addMerge, 3, 0);
        actions.Controls.Add(remove, 4, 0);
        actions.Controls.Add(folder, 5, 0);
        layout.Controls.Add(actions, 0, 1);

        _library = NewAudioList(checkBoxes: false);
        _library.Columns.Add("TÊN HIỂN THỊ", 280);
        _library.Columns.Add("LOẠI", 100);
        _library.Columns.Add("GIỌNG ĐỌC", 180);
        _library.Columns.Add("TỐC ĐỘ", 90);
        _library.Columns.Add("NGÀY TẠO", 145);
        _library.Columns.Add("DUNG LƯỢNG", 120);
        _library.Columns.Add("ID", 150);
        _library.ItemActivate += (_, _) => PlaySelected();
        layout.Controls.Add(_library, 0, 2);
        return page;
    }

    private Control BuildMergePage()
    {
        var page = new Panel { BackColor = Theme.Background, Padding = new Padding(18, 4, 18, 8) };
        var card = new SurfacePanel { Dock = DockStyle.Fill, Padding = new Padding(16) };
        page.Controls.Add(card);
        var grid = Theme.Table(1, 4);
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        card.Controls.Add(grid);

        var titleRow = Theme.Table(2, 1);
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        titleRow.Controls.Add(Theme.Label("GHÉP CHUỖI AUDIO MP3 + ĐỒNG BỘ SRT", 10f, true), 0, 0);
        _mergeHint = Theme.Label("0 đoạn được chọn", 9f, true, Theme.AccentText);
        _mergeHint.TextAlign = ContentAlignment.MiddleRight;
        titleRow.Controls.Add(_mergeHint, 1, 0);
        grid.Controls.Add(titleRow, 0, 0);

        var actions = Theme.Table(9, 1);
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 218));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 102));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 98));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var mergeSpeedLabel = Theme.Label("Tốc độ xuất:", 8.75f, true, Theme.Text);
        _mergeSpeedCombo = Theme.Combo();
        _mergeSpeedCombo.Margin = new Padding(0, 3, 8, 3);
        foreach (var speed in new[] { 0.5, 1.0, 1.25, 1.5, 2.0 })
            _mergeSpeedCombo.Items.Add(new SpeedOption(speed));
        _mergeSpeedCombo.SelectedIndex = 1;

        var advanced = Btn("⚙ Ghép nâng cao / chọn file lẻ", ModernButton.Kind.Gold);
        advanced.Dock = DockStyle.Fill;
        advanced.Margin = new Padding(0, 3, 6, 3);
        advanced.Click += (_, _) => OpenAdvancedMergeDialog();

        var all = Btn("✓ Chọn tất cả", ModernButton.Kind.Secondary);
        all.Dock = DockStyle.Fill;
        all.Margin = new Padding(0, 3, 6, 3);
        all.Click += (_, _) => CheckAllMerge(true);

        var none = Btn("□ Bỏ chọn", ModernButton.Kind.Secondary);
        none.Dock = DockStyle.Fill;
        none.Margin = new Padding(0, 3, 6, 3);
        none.Click += (_, _) => CheckAllMerge(false);

        var up = Btn("↑ Đưa lên", ModernButton.Kind.Secondary);
        up.Dock = DockStyle.Fill;
        up.Margin = new Padding(0, 3, 6, 3);
        up.Click += (_, _) => MoveMergeSelection(-1);

        var down = Btn("↓ Đưa xuống", ModernButton.Kind.Secondary);
        down.Dock = DockStyle.Fill;
        down.Margin = new Padding(0, 3, 6, 3);
        down.Click += (_, _) => MoveMergeSelection(1);

        var reset = Btn("⟳ Tải lại", ModernButton.Kind.Secondary);
        reset.Dock = DockStyle.Fill;
        reset.Margin = new Padding(0, 3, 0, 3);
        reset.Click += (_, _) => RefreshMergeList();

        actions.Controls.Add(mergeSpeedLabel, 0, 0);
        actions.Controls.Add(_mergeSpeedCombo, 1, 0);
        actions.Controls.Add(advanced, 2, 0);
        actions.Controls.Add(all, 3, 0);
        actions.Controls.Add(none, 4, 0);
        actions.Controls.Add(up, 5, 0);
        actions.Controls.Add(down, 6, 0);
        actions.Controls.Add(reset, 7, 0);
        grid.Controls.Add(actions, 0, 1);

        _mergeList = NewAudioList(checkBoxes: true);
        _mergeList.Columns.Add("CHỌN / THỨ TỰ", 155);
        _mergeList.Columns.Add("TÊN FILE", 360);
        _mergeList.Columns.Add("LOẠI", 108);
        _mergeList.Columns.Add("SRT", 95);
        _mergeList.Columns.Add("NGÀY TẠO", 150);
        _mergeList.Columns.Add("DUNG LƯỢNG", 120);
        _mergeList.ItemChecked += (_, _) => { if (!_suppressMergeRefresh) RefreshMergeHint(); };
        grid.Controls.Add(_mergeList, 0, 2);

        var export = Theme.Table(3, 2);
        export.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        export.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 185));
        export.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        export.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        export.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        var nameLabel = Theme.Label("TÊN FILE MP3 SAU KHI GHÉP", 8.5f, true, Theme.Muted);
        export.Controls.Add(nameLabel, 0, 0);
        _mergeProgress = new ModernProgressBar { Dock = DockStyle.Fill, Margin = new Padding(12, 10, 4, 8) };
        export.Controls.Add(_mergeProgress, 1, 0);
        export.SetColumnSpan(_mergeProgress, 2);
        _mergeTitle = Theme.Textbox("Tổng hợp các chương");
        _mergeTitle.Margin = new Padding(0, 8, 0, 8);
        export.Controls.Add(_mergeTitle, 0, 1);
        _mergeButton = Btn("♫ GHÉP MP3 + SRT", ModernButton.Kind.Primary, 175);
        _mergeButton.Dock = DockStyle.Fill;
        _mergeButton.Margin = new Padding(10, 4, 5, 4);
        _mergeButton.Click += async (_, _) => await StartMergeAsync();
        export.Controls.Add(_mergeButton, 1, 1);
        var cancel = Btn("■ Dừng", ModernButton.Kind.Destructive, 118);
        cancel.Dock = DockStyle.Fill;
        cancel.Margin = new Padding(5, 4, 0, 4);
        cancel.Click += (_, _) => _jobCancellation?.Cancel();
        export.Controls.Add(cancel, 2, 1);
        grid.Controls.Add(export, 0, 3);
        return page;
    }

    private Control BuildSettingsAndDocsPage()
    {
        var page = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Background,
            Padding = new Padding(18, 4, 18, 8)
        };
        var layout = Theme.Table(1, 2);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(layout);

        // 3-Tab Segmented Bar: Cài đặt | Danh sách giọng | Hướng dẫn dùng
        var tabBar = new SurfacePanel
        {
            Dock = DockStyle.Fill,
            Radius = 9,
            BackColor = Theme.Sidebar,
            Padding = new Padding(6, 4, 12, 4),
            Margin = new Padding(0, 0, 0, 8)
        };
        var tabGrid = Theme.Table(4, 1);
        tabGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165));
        tabGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 195));
        tabGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 195));
        tabGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var tabs = new[]
        {
            ("settings", "⚙  Cài đặt"),
            ("voices",   "🎙  Danh sách giọng"),
            ("guide",    "📖  Hướng dẫn dùng")
        };
        for (int i = 0; i < tabs.Length; i++)
        {
            var (tabKey, tabLabel) = tabs[i];
            var btn = Btn(tabLabel, i == 0 ? ModernButton.Kind.Primary : ModernButton.Kind.Ghost);
            btn.Dock = DockStyle.Fill;
            btn.Margin = new Padding(0, 1, 6, 1);
            btn.Click += (_, _) => ShowSettingsTab(tabKey);
            _settingsTabButtons[tabKey] = btn;
            tabGrid.Controls.Add(btn, i, 0);
        }

        var tabHint = Theme.Label("Phím tắt nhanh: Ctrl+4 Cài đặt  ·  Ctrl+5 Danh sách giọng  ·  Ctrl+6 Hướng dẫn dùng", 8.25f, color: Theme.Muted);
        tabHint.TextAlign = ContentAlignment.MiddleRight;
        tabGrid.Controls.Add(tabHint, 3, 0);
        tabBar.Controls.Add(tabGrid);
        layout.Controls.Add(tabBar, 0, 0);

        RegisterSettingsSubPage("settings", BuildSettingsTabContent());
        RegisterSettingsSubPage("voices", BuildVoiceLabTabContent());
        RegisterSettingsSubPage("guide", BuildGuideTabContent());
        layout.Controls.Add(_settingsTabHost, 0, 1);

        return page;
    }

    private void RegisterSettingsSubPage(string id, Control subPage)
    {
        subPage.Dock = DockStyle.Fill;
        subPage.Visible = id == "settings";
        _settingsSubPages[id] = subPage;
        _settingsTabHost.Controls.Add(subPage);
    }

    private Control BuildSettingsTabContent()
    {
        var card = new SurfacePanel { Dock = DockStyle.Fill, Padding = new Padding(20) };
        var layout = Theme.Table(1, 8);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(layout);

        layout.Controls.Add(Theme.Label("CẤU HÌNH 3 THƯ MỤC CHUẨN (OUTPUT / VOICES / RUNTIME)", 10.25f, true), 0, 0);
        layout.Controls.Add(Theme.Label("1. Thư mục Nơi lưu - Output (Nơi lưu thư viện MP3 & SRT xuất ra)", 9f, true), 0, 1);
        _outputPath = AddFolderBrowser(layout, 2, _storage.Settings.OutputFolder, () => "output");
        layout.Controls.Add(Theme.Label("2. Thư mục Voices (Chứa model .onnx/.onnx.json và thư mục con Voices/example)", 9f, true), 0, 3);
        _voicesPath = AddFolderBrowser(layout, 4, _storage.Settings.VoicesFolder, () => "voices");
        layout.Controls.Add(Theme.Label("3. Thư mục Runtime (Chứa piper/piper.exe và ffmpeg/ffmpeg.exe + ffprobe.exe)", 9f, true), 0, 5);
        _runtimePath = AddFolderBrowser(layout, 6, _storage.Settings.RuntimeFolder, () => "runtime");

        var info = Theme.Table(1, 6);
        info.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        info.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        info.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        info.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        info.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        info.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _dependencies = Theme.Label("Đang kiểm tra trạng thái thành phần...", 9f, true, Theme.Muted);
        info.Controls.Add(_dependencies, 0, 0);

        _minimizeToTrayCheck = new CheckBox
        {
            Text = "Khi bấm Thu nhỏ cửa sổ (_) sẽ tự ẩn khỏi Taskbar và hiển thị ở Toolbar (Khay hệ thống / System Tray)",
            Checked = _storage.Settings.MinimizeToTray,
            Dock = DockStyle.Fill,
            ForeColor = Theme.Text,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold)
        };
        _minimizeToTrayCheck.CheckedChanged += (_, _) =>
        {
            _storage.Settings.MinimizeToTray = _minimizeToTrayCheck.Checked;
            _storage.SaveSettings();
        };
        info.Controls.Add(_minimizeToTrayCheck, 0, 1);

        _closeToTrayCheck = new CheckBox
        {
            Text = "Khi đang có luồng Render chạy ngầm mà bấm nút Đóng [X], hỏi ẩn xuống Toolbar thay vì tắt ngang",
            Checked = _storage.Settings.CloseToTrayWhenBusy,
            Dock = DockStyle.Fill,
            ForeColor = Theme.Text,
            BackColor = Color.Transparent,
            Font = Theme.Body
        };
        _closeToTrayCheck.CheckedChanged += (_, _) =>
        {
            _storage.Settings.CloseToTrayWhenBusy = _closeToTrayCheck.Checked;
            _storage.SaveSettings();
        };
        info.Controls.Add(_closeToTrayCheck, 0, 2);

        var controls = Theme.Table(5, 1);
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 265));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 178));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 138));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var resetFolders = Btn("⌂ Đặt về 3 thư mục mặc định cạnh EXE", ModernButton.Kind.Primary);
        resetFolders.Dock = DockStyle.Fill;
        resetFolders.Margin = new Padding(0, 3, 8, 3);
        resetFolders.Click += (_, _) =>
        {
            _storage.BindDefaultAppFolders();
            _outputPath.Text = _storage.Settings.OutputFolder;
            _voicesPath.Text = _storage.Settings.VoicesFolder;
            _runtimePath.Text = _storage.Settings.RuntimeFolder;
            ReloadVoices();
            RefreshLibrary();
            RefreshDependencies();
            SetStatus("Đã trỏ lại về 3 thư mục chuẩn: Output, Voices, Runtime cạnh ứng dụng.", Theme.Green);
        };

        var reload = Btn("↻ Quét lại Voices & Engine", ModernButton.Kind.Secondary);
        reload.Dock = DockStyle.Fill;
        reload.Margin = new Padding(0, 3, 8, 3);
        reload.Click += (_, _) => { ReloadVoices(); RefreshDependencies(); };

        var history = Btn("♧ Dọn bản ghi mất file", ModernButton.Kind.Secondary);
        history.Dock = DockStyle.Fill;
        history.Margin = new Padding(0, 3, 8, 3);
        history.Click += (_, _) => { _storage.PurgeMissing(); RefreshLibrary(); RefreshMergeList(); SetStatus("Đã dọn các bản ghi mất file."); };

        var appData = Btn("📂 Mở AppData", ModernButton.Kind.Secondary);
        appData.Dock = DockStyle.Fill;
        appData.Margin = new Padding(0, 3, 0, 3);
        appData.Click += (_, _) => OpenFolder(_storage.DataFolder);

        controls.Controls.Add(resetFolders, 0, 0);
        controls.Controls.Add(reload, 1, 0);
        controls.Controls.Add(history, 2, 0);
        controls.Controls.Add(appData, 3, 0);
        info.Controls.Add(controls, 0, 3);

        info.Controls.Add(Theme.Label(
            "Mẹo: Bạn có thể bấm nút '▾ Ẩn xuống Toolbar' ở góc phải trên cùng (hoặc phím tắt Ctrl + M) bất cứ lúc nào để ẩn ứng dụng khỏi Taskbar trong khi hệ thống vẫn tiếp tục chạy Đa luồng Render ngầm.",
            8.5f, color: Theme.Muted), 0, 4);
        layout.Controls.Add(info, 0, 7);
        return card;
    }

    private Control BuildVoiceLabTabContent()
    {
        var page = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        var mainGrid = Theme.Table(1, 2);
        mainGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 144));
        mainGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(mainGrid);

        // Top Card: Common Sample Paragraph & Batch Example Generator (Voices/example)
        var sampleCard = new SurfacePanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            Margin = new Padding(0, 0, 0, 8)
        };
        var sampleLayout = Theme.Table(1, 3);
        sampleLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        sampleLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        sampleLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        sampleCard.Controls.Add(sampleLayout);

        var sampleHeader = Theme.Table(2, 1);
        sampleHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        sampleHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        sampleHeader.Controls.Add(Theme.Label("PHÒNG NGHE THỬ · ĐOẠN VĂN MẪU CHUNG (LƯU TẠI VOICES/EXAMPLE)", 9.75f, true), 0, 0);
        _voiceLabSummary = Theme.Label("0 giọng · 0 mẫu đã tạo", 8.75f, true, Theme.AccentText);
        _voiceLabSummary.TextAlign = ContentAlignment.MiddleRight;
        sampleHeader.Controls.Add(_voiceLabSummary, 1, 0);
        sampleLayout.Controls.Add(sampleHeader, 0, 0);

        _sampleTextBox = Theme.Textbox(_storage.Settings.SampleText);
        _sampleTextBox.Leave += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_sampleTextBox.Text))
            {
                _storage.Settings.SampleText = _sampleTextBox.Text.Trim();
                _storage.SaveSettings();
            }
        };
        sampleLayout.Controls.Add(_sampleTextBox, 0, 1);

        var sampleButtonsGrid = Theme.Table(5, 1);
        sampleButtonsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 236));
        sampleButtonsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 206));
        sampleButtonsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 198));
        sampleButtonsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        sampleButtonsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _renderAllExamplesButton = Btn("⚡ Render mẫu cho tất cả giọng", ModernButton.Kind.Primary, 228);
        _renderAllExamplesButton.Dock = DockStyle.Fill;
        _renderAllExamplesButton.Margin = new Padding(0, 3, 8, 2);
        _renderAllExamplesButton.Click += async (_, _) => await RenderAllVoiceExamplesAsync(forceOverwrite: false);

        var rerenderAllBtn = Btn("⟳ Tạo lại toàn bộ (Ghi đè)", ModernButton.Kind.Secondary, 198);
        rerenderAllBtn.Dock = DockStyle.Fill;
        rerenderAllBtn.Margin = new Padding(0, 3, 8, 2);
        rerenderAllBtn.Click += async (_, _) => await RenderAllVoiceExamplesAsync(forceOverwrite: true);

        var openExampleDirBtn = Btn("📂 Mở Voices/example", ModernButton.Kind.Gold, 190);
        openExampleDirBtn.Dock = DockStyle.Fill;
        openExampleDirBtn.Margin = new Padding(0, 3, 8, 2);
        openExampleDirBtn.Click += (_, _) => OpenFolder(_storage.ExampleVoicesFolder);

        var stopBatchBtn = Btn("■ Dừng", ModernButton.Kind.Destructive, 84);
        stopBatchBtn.Dock = DockStyle.Fill;
        stopBatchBtn.Margin = new Padding(0, 3, 12, 2);
        stopBatchBtn.Click += (_, _) => _jobCancellation?.Cancel();

        _voiceLabProgress = new ModernProgressBar { Dock = DockStyle.Top, Height = 10, Margin = new Padding(4, 15, 0, 0) };

        sampleButtonsGrid.Controls.Add(_renderAllExamplesButton, 0, 0);
        sampleButtonsGrid.Controls.Add(rerenderAllBtn, 1, 0);
        sampleButtonsGrid.Controls.Add(openExampleDirBtn, 2, 0);
        sampleButtonsGrid.Controls.Add(stopBatchBtn, 3, 0);
        sampleButtonsGrid.Controls.Add(_voiceLabProgress, 4, 0);
        sampleLayout.Controls.Add(sampleButtonsGrid, 0, 2);
        mainGrid.Controls.Add(sampleCard, 0, 0);

        // Bottom Card: Voice Catalog Table, Filters, Rename & Organize
        var catalogCard = new SurfacePanel { Dock = DockStyle.Fill, Padding = new Padding(14) };
        var catalogLayout = Theme.Table(1, 3);
        catalogLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        catalogLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        catalogLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        catalogCard.Controls.Add(catalogLayout);

        var filterBar = Theme.Table(4, 1);
        filterBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        filterBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        filterBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
        filterBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));

        _voiceLabSearch = Theme.Textbox();
        _voiceLabSearch.PlaceholderText = "🔍 Tìm theo tên giọng, tên file .onnx, ngôn ngữ hoặc chất lượng...";
        _voiceLabSearch.TextChanged += (_, _) => RefreshVoiceLabCatalog();

        _voiceLabLanguage = Theme.Combo();
        _voiceLabLanguage.Margin = new Padding(8, 3, 8, 3);
        _voiceLabLanguage.SelectedIndexChanged += (_, _) => { if (!_loadingVoices) RefreshVoiceLabCatalog(); };

        _voiceLabCategory = Theme.Combo();
        _voiceLabCategory.Items.AddRange(["Tất cả nhóm", "Nữ", "Nam", "Khác"]);
        _voiceLabCategory.SelectedIndex = 0;
        _voiceLabCategory.Margin = new Padding(0, 3, 8, 3);
        _voiceLabCategory.SelectedIndexChanged += (_, _) => { if (!_loadingVoices) RefreshVoiceLabCatalog(); };

        var refreshVoicesBtn = Btn("↻ Quét lại", ModernButton.Kind.Secondary, 104);
        refreshVoicesBtn.Dock = DockStyle.Fill;
        refreshVoicesBtn.Margin = new Padding(0, 2, 0, 2);
        refreshVoicesBtn.Click += (_, _) => ReloadVoices();

        filterBar.Controls.Add(_voiceLabSearch, 0, 0);
        filterBar.Controls.Add(_voiceLabLanguage, 1, 0);
        filterBar.Controls.Add(_voiceLabCategory, 2, 0);
        filterBar.Controls.Add(refreshVoicesBtn, 3, 0);
        catalogLayout.Controls.Add(filterBar, 0, 0);

        var actionRow = Theme.Table(6, 1);
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 168));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 178));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 182));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 226));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 156));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var playSampleBtn = Btn("▶ Nghe mẫu giọng chọn", ModernButton.Kind.Gold);
        playSampleBtn.Dock = DockStyle.Fill;
        playSampleBtn.Margin = new Padding(0, 2, 6, 2);
        playSampleBtn.Click += async (_, _) => await PlayOrGenerateSelectedVoiceLabExampleAsync(forceRegenerate: false);

        var regenSampleBtn = Btn("⚡ Tạo lại mẫu giọng chọn", ModernButton.Kind.Secondary);
        regenSampleBtn.Dock = DockStyle.Fill;
        regenSampleBtn.Margin = new Padding(0, 2, 6, 2);
        regenSampleBtn.Click += async (_, _) => await PlayOrGenerateSelectedVoiceLabExampleAsync(forceRegenerate: true);

        var useInStudioBtn = Btn("✓ Dùng trong Biên tập", ModernButton.Kind.Primary);
        useInStudioBtn.Dock = DockStyle.Fill;
        useInStudioBtn.Margin = new Padding(0, 2, 6, 2);
        useInStudioBtn.Click += (_, _) => UseSelectedVoiceInEditor();

        var renameFileBtn = Btn("✎ Đổi tên file & Phân loại (.onnx)", ModernButton.Kind.Secondary);
        renameFileBtn.Dock = DockStyle.Fill;
        renameFileBtn.Margin = new Padding(0, 2, 6, 2);
        renameFileBtn.Click += (_, _) =>
        {
            var v = SelectedVoiceInCatalog();
            if (v is null) { SetStatus("Chọn một giọng trong danh sách để đổi tên file / phân loại.", Theme.GoldText); return; }
            PromptRenameVoice(v);
        };

        var openVoicesDirBtn = Btn("📂 Mở thư mục Voices", ModernButton.Kind.Secondary);
        openVoicesDirBtn.Dock = DockStyle.Fill;
        openVoicesDirBtn.Margin = new Padding(0, 2, 0, 2);
        openVoicesDirBtn.Click += (_, _) => OpenFolder(_storage.Settings.VoicesFolder);

        actionRow.Controls.Add(playSampleBtn, 0, 0);
        actionRow.Controls.Add(regenSampleBtn, 1, 0);
        actionRow.Controls.Add(useInStudioBtn, 2, 0);
        actionRow.Controls.Add(renameFileBtn, 3, 0);
        actionRow.Controls.Add(openVoicesDirBtn, 4, 0);
        catalogLayout.Controls.Add(actionRow, 0, 1);

        _voiceCatalogList = NewAudioList(checkBoxes: false);
        _voiceCatalogList.MultiSelect = false;
        _voiceCatalogList.Columns.Add("TÊN GIỌNG HIỂN THỊ", 210);
        _voiceCatalogList.Columns.Add("NHÓM", 85);
        _voiceCatalogList.Columns.Add("NGÔN NGỮ", 185);
        _voiceCatalogList.Columns.Add("CHẤT LƯỢNG", 100);
        _voiceCatalogList.Columns.Add("SPEAKERS", 85);
        _voiceCatalogList.Columns.Add("FILE MẪU (EXAMPLE)", 165);
        _voiceCatalogList.Columns.Add("TÊN FILE (.ONNX)", 290);
        _voiceCatalogList.ItemActivate += async (_, _) => await PlayOrGenerateSelectedVoiceLabExampleAsync(forceRegenerate: false);
        catalogLayout.Controls.Add(_voiceCatalogList, 0, 2);

        mainGrid.Controls.Add(catalogCard, 0, 1);
        return page;
    }

    private sealed record GuideEntry(
        string Category,
        string CommandSyntax,
        string EffectValue,
        string Meaning,
        string InsertSample,
        string ExampleDetail);

    private static IReadOnlyList<GuideEntry> GetAllGuideEntries() =>
    [
        // 1. Câu lệnh Text (Kịch bản TTS)
        new("1. Câu lệnh Text (Kịch bản TTS)", "[pause:500ms]", "Nghỉ 500 ms",
            "Chèn khoảng lặng chính xác 500 mili-giây vào âm thanh PCM. Không đọc thành tiếng và tự động ẩn khỏi phụ đề .SRT.",
            "[pause:500ms] ",
            "Ví dụ: Đêm đã khuya. [pause:500ms] Ngoài hiên chỉ còn tiếng gió thổi.\n→ Hệ thống tách 2 câu và chèn đúng 500ms khoảng lặng giữa hai câu."),
        new("1. Câu lệnh Text (Kịch bản TTS)", "[pause:1.5s]", "Nghỉ 1500 ms (1.5 giây)",
            "Chèn khoảng lặng tính theo giây (hỗ trợ từ 0.05s đến 10s, viết dấu chấm hoặc dấu phẩy đều được).",
            "[pause:1.5s] ",
            "Ví dụ: Chương 1: Khởi đầu mới. [pause:1.5s] Năm ấy, tại vùng biên ải xa xôi..."),
        new("1. Câu lệnh Text (Kịch bản TTS)", "[nghi:800ms]  /  [nghi:2s]", "Nghỉ 800 ms / 2000 ms",
            "Cú pháp tiếng Việt tương đương [pause:...]. Dùng để điều chỉnh nhịp thở hoặc chuyển cảnh kịch tính.",
            "[nghi:800ms] ",
            "Ví dụ: Hắn nhìn thẳng vào mắt đối phương... [nghi:1.2s] rồi bất ngờ bật cười."),
        new("1. Câu lệnh Text (Kịch bản TTS)", "[break:1000ms]  /  [im:1s]", "Nghỉ 1000 ms (1 giây)",
            "Bí danh (alias) của lệnh ngắt nhịp. Tương thích với kịch bản chuyển từ các phần mềm audiobook khác.",
            "[break:1000ms] ",
            "Ví dụ: Kết thúc hồi thứ nhất. [break:2s] Mời quý thính giả nghe tiếp hồi hai."),
        new("1. Câu lệnh Text (Kịch bản TTS)", "[[Ghi chú kịch bản]]", "Bỏ qua hoàn toàn",
            "Ghi chú dành cho người biên tập nằm trong cặp ngoặc vuông kép [[...]]. Khi chuẩn hóa hoặc đọc TTS sẽ tự động loại bỏ, không phát âm và không vào SRT.",
            "[[Đoạn này kiểm tra lại tên nhân vật]] ",
            "Ví dụ: [[Lưu ý: Đọc chậm đoạn này]] Hôm nay trời trong xanh.\n→ Chỉ đọc: 'Hôm nay trời trong xanh.'"),
        new("1. Câu lệnh Text (Kịch bản TTS)", "[note: ... ]  /  [ghichu: ... ]", "Bỏ qua hoàn toàn",
            "Câu lệnh ghi chú biên tập có từ khóa đầu. Tự động lọc sạch trước khi đưa vào Piper và phụ đề SRT.",
            "[note: nhớ ghép nhạc nền] ",
            "Ví dụ: [ghichu: hết cảnh 1] Nàng bước lên xe ngựa."),
        new("1. Câu lệnh Text (Kịch bản TTS)", "// Dòng chú thích đầu dòng", "Bỏ qua cả dòng",
            "Mọi dòng bắt đầu bằng hai dấu gạch chéo // được hiểu là dòng comment kịch bản và được bỏ qua hoàn toàn.",
            "// --- GHI CHÚ PHÂN CẢNH ---\n",
            "Ví dụ:\n// Cảnh 2: Trong thư phòng\nLão nhân chậm rãi đặt tách trà xuống bàn."),

        // 2. Dấu câu & Nhịp nghỉ tự nhiên
        new("2. Dấu câu & Nhịp nghỉ tự nhiên", "(Bắt đầu bản thu)", "Nghỉ mở đầu 160 ms",
            "Tự động chèn 160ms khoảng lặng chuẩn ở đầu file audio để câu đầu tiên không bị cụt âm đầu khi phát.",
            "",
            "Mọi bản thu MP3 và phụ đề SRT đều tự động tính mốc khởi đầu sau 160ms PCM silence."),
        new("2. Dấu câu & Nhịp nghỉ tự nhiên", "Dấu phẩy ( , ) · Hai chấm ( : ) · Chấm phẩy ( ; )", "Giữ liền mạch / tách thông minh",
            "Giữ nguyên dấu phẩy trong câu ngắn để mô hình ONNX giữ trọn ngữ cảnh âm tiết tiếng Việt; tự động ngắt mệnh đề êm ở câu nhiều vế dài.",
            ", ",
            "Ví dụ: 'Không, nàng chưa bao giờ tin điều đó.' → Đưa nguyên câu vào Piper trong 1 lần đọc duy nhất."),
        new("2. Dấu câu & Nhịp nghỉ tự nhiên", "Dấu chấm ( .  /  。 )", "Ngắt câu + Nghỉ 310 ms",
            "Kết thúc câu trần thuật chuẩn. Tự động chèn 310ms khoảng lặng PCM trước khi đọc câu tiếp theo.",
            ". ",
            "Nếu một dòng văn bản quên đặt dấu kết câu ở cuối dòng, hệ thống tự động thêm dấu chấm '.' khi chuẩn hóa."),
        new("2. Dấu câu & Nhịp nghỉ tự nhiên", "Dấu chấm hỏi ( ?  /  ？ )", "Ngắt câu + Nghỉ 330 ms",
            "Kết thúc câu nghi vấn. Chèn 330ms khoảng lặng tự nhiên.",
            "? ",
            "Giữ nguyên cả dấu ngoặc kép đóng đi kèm như: \"Ngươi là ai?\""),
        new("2. Dấu câu & Nhịp nghỉ tự nhiên", "Dấu chấm than ( !  /  ！ )", "Ngắt câu + Nghỉ 350 ms",
            "Kết thúc câu cảm thán / mệnh lệnh. Chèn 350ms khoảng lặng.",
            "! ",
            "Ví dụ: \"Dừng lại ngay!\" → Giữ dấu '!' cho Piper nhấn giọng và nghỉ 350ms sau câu."),
        new("2. Dấu câu & Nhịp nghỉ tự nhiên", "Dấu ba chấm / Lửng ( ...  /  … )", "Ngân nguyên âm + Nghỉ 480 ms",
            "Tạo nhịp ngập ngừng, tự động kéo giãn nguyên âm cuối bằng WSOLA và chèn khoảng lặng lắng đọng.",
            "... ",
            "Hỗ trợ cả ký tự đơn '…' và chuỗi nhiều dấu chấm liên tiếp '...'."),
        new("2. Dấu câu & Nhịp nghỉ tự nhiên", "Xuống 1 dòng mới (Enter / \\n)", "Nghỉ hết dòng 350 ms",
            "Khi xuống dòng đơn (ví dụ giữa các câu thoại), hệ thống tự động ngắt đoạn và nghỉ 350ms.",
            "\n",
            "Phù hợp khi trình bày hội thoại từng dòng."),
        new("2. Dấu câu & Nhịp nghỉ tự nhiên", "Xuống 2 dòng trở lên (Đoạn văn mới / \\n\\n)", "Nghỉ chuyển đoạn 550 ms",
            "Khi có dòng trống giữa 2 đoạn văn hoặc chuyển cảnh, hệ thống tự động nâng khoảng nghỉ lên 550ms.",
            "\n\n",
            "Giúp người nghe phân biệt rõ ràng các đoạn văn và chương mục."),
        new("2. Dấu câu & Nhịp nghỉ tự nhiên", "Số thập phân & Thời gian ( 3.14 · 1,5 · 10:30 )", "Không tách nhầm",
            "Khi dấu chấm, phẩy hoặc hai chấm nằm giữa hai chữ số, bộ phân tích tự động nhận biết là con số/giờ và KHÔNG ngắt câu.",
            "10:30",
            "Ví dụ: 'Giá 1,5 triệu đồng lúc 10:30 sáng.' → Đọc liền một mạch chính xác ('10 giờ 30 sáng')."),

        // 2B. Kỹ thuật Nhấn âm, Lên/Xuống giọng & Ngân âm cuối
        new("2B. Nhấn âm, Lên/Xuống giọng & Ngân âm", "Ép LÊN GIỌNG tại 1 từ:  Từ! [pause:120ms] ...", "Bật cao độ (Pitch Rise)",
            "Để ép AI vút cao giọng ngay tại 1 từ giữa câu (VD: chữ 'Chính'), đặt dấu '!' hoặc '?!' ngay sau từ đó kèm [pause:120ms] rồi viết tiếp vế sau.",
            "Kẻ phản bội... [pause:350ms] Chính! [pause:120ms] Là ngươi!",
            "Ví dụ:\n• Cách 1 (Bật cao dứt khoát): Kẻ phản bội... [pause:350ms] Chính! [pause:120ms] Là ngươi!\n• Cách 2 (Treo cao liền hơi bằng dấu phẩy): Kẻ phản bội... [pause:300ms] Chính, là ngươi!\n• Cách 3 (Điệp từ leo thang): Kẻ phản bội... [pause:400ms] Chính là... [pause:200ms] chính là ngươi!"),
        new("2B. Nhấn âm, Lên/Xuống giọng & Ngân âm", "Nhấn âm từ khóa:  , từ khóa,   hoặc  ... [pause:300ms] từ!", "Tăng trọng lượng từ",
            "Bao từ cần nhấn bằng cặp dấu phẩy ', từ,' (nhấn nhẹ liền hơi) hoặc đặt '... [pause:300ms]' ngay trước từ khóa (nhấn kịch tính kiểu Audiobook).",
            "... [pause:300ms] ",
            "Ví dụ:\n• Nhấn liền hơi: Hắn, tuyệt đối, sẽ không bao giờ quay lại.\n• Nhấn kịch tính: Người đứng sau toàn bộ chuyện này chính là... [pause:300ms] ngươi!"),
        new("2B. Nhấn âm, Lên/Xuống giọng & Ngân âm", "Lên giọng cuối câu vs. Xuống giọng trầm", "? / ?!  (Lên)   |   ...  (Xuống)",
            "Dấu '?' hoặc '?!' kèm từ hỏi (sao, ư, à, hả) đẩy cao độ cuối câu lên; dấu ba chấm '...' hạ giọng trầm lắng xuống.",
            "sao? [pause:450ms] Không... ",
            "Ví dụ tương phản Lên - Xuống giọng:\nNgươi nghĩ ta sợ ngươi sao? [pause:450ms] Không... ta chỉ thấy tiếc cho ngươi thôi..."),
        new("2B. Nhấn âm, Lên/Xuống giọng & Ngân âm", "Ngân âm cuối (Kéo dài):  ơi... / Giết... + [pause:800ms]", "WSOLA kéo giãn nguyên âm",
            "Dùng dấu '...' sau từ cần ngân kết hợp [pause:800ms] để giữ dư âm. KHÔNG viết lặp chữ kiểu 'ơiiii' để tránh lỗi đánh vần.",
            "ơi... ơ-i... [pause:700ms]",
            "Ví dụ ngân âm:\n• Tiếng gọi ngân xa: Sư phụ ơi... ơ-i... [pause:600ms]\n• Tiếng vọng láy âm: Tiếng chuông ngân vang... vang mãi... vang mãi... [pause:900ms]"),

        // 3. Cú pháp Markdown (.md)
        new("3. Cú pháp Markdown (.md)", "# Tiêu đề  /  ## Chương 1  /  > Trích dẫn", "Lọc ký hiệu, giữ lời",
            "Khi nhập tệp .md, các ký hiệu #, ##, ### và dấu trích dẫn '>' đầu dòng được tự động loại bỏ để Piper không đọc chữ 'thăng' hay 'lớn hơn'.",
            "## Chương 1: ",
            "Tự động thêm dấu chấm cuối dòng tiêu đề để tạo nhịp nghỉ chuyển sang nội dung chương."),
        new("3. Cú pháp Markdown (.md)", "**In đậm** · *In nghiêng* · __Gạch chân__ · `Code ngắn`", "Giữ chữ, bỏ dấu sao/gạch",
            "Tự động bóc tách ký hiệu định dạng Markdown (** , * , __ , `) và chỉ giữ lại từ ngữ bên trong.",
            "",
            "Ví dụ: '**Quan trọng**' → chuẩn hóa thành 'Quan trọng'."),
        new("3. Cú pháp Markdown (.md)", "[Tên liên kết](https://...)  /  ![Mô tả ảnh](url)", "Chỉ đọc tên, bỏ URL",
            "Loại bỏ đường dẫn URL trong ngoặc đơn, chỉ giữ lại phần văn bản mô tả trong ngoặc vuông.",
            "",
            "Tránh việc TTS đọc nguyên chuỗi địa chỉ web dài dòng."),
        new("3. Cú pháp Markdown (.md)", "```Khối mã nguồn```  /  --- (Đường kẻ ngang)", "Bỏ qua hoàn toàn",
            "Các khối code đặt giữa ```...``` và đường kẻ phân cách '---', '***' được tự động loại bỏ khi nhập file Markdown.",
            "",
            "Giúp nhập tài liệu kỹ thuật hoặc truyện định dạng Markdown sạch sẽ ngay lập tức."),

        // 4. Quy tắc tên file Voice (.onnx)
        new("4. Quy tắc tên file Voice (.onnx)", "<ngôn-ngữ>_<nhóm>_<tên>-<chất-lượng>.onnx", "Chuẩn hóa tự động",
            "Định dạng tên file chuẩn khuyến nghị trong thư mục Voices (luôn đi kèm file .onnx.json cùng tên).",
            "vi-VN_nu_ngochuyen-high",
            "Bạn có thể đổi tên trực tiếp trên giao diện ở mục '4. Cài đặt và tài liệu → Danh sách giọng' → bấm '✎ Đổi tên file & Phân loại'."),
        new("4. Quy tắc tên file Voice (.onnx)", "Tiền tố nhóm giọng: nu_ / female_  |  nam_ / male_", "Tự phân loại Nữ / Nam",
            "Tên file bắt đầu bằng 'nu_', 'female_' hoặc nằm trong thư mục con 'Voices/Nu/' được xếp vào nhóm Nữ; 'nam_', 'male_' hoặc 'Voices/Nam/' được xếp vào nhóm Nam.",
            "",
            "Ngoài ra thuộc tính 'display_name', 'category', 'language.code' trong file .onnx.json luôn được ưu tiên cao nhất."),
        new("4. Quy tắc tên file Voice (.onnx)", "Thư mục Voices/example/<tên-file>.mp3", "Lưu mẫu thử từng giọng",
            "Chứa file âm thanh mẫu của từng giọng được render từ 'Đoạn văn mẫu chung' trong tab Danh sách giọng để nghe thử tức thì (0ms).",
            "",
            "Khi bạn đổi tên file .onnx bằng công cụ trong phần mềm, file .mp3 mẫu trong Voices/example cũng tự động đổi tên theo."),

        // 5. Phím tắt & Thao tác Studio
        new("5. Phím tắt & Thao tác Studio", "Ctrl + 1  →  Ctrl + 4  (Ctrl + 5 / 6 mở Tab phụ)", "Chuyển mục nhanh",
            "Chuyển nhanh giữa 4 mục: (1) Biên tập + Đa luồng, (2) Nơi lưu, (3) Ghép Audio, (4) Cài đặt & Tài liệu (Ctrl+5 Danh sách giọng, Ctrl+6 Hướng dẫn).",
            "",
            "Hoạt động ở mọi vị trí trong cửa sổ ứng dụng."),
        new("5. Phím tắt & Thao tác Studio", "Ctrl + M  (hoặc nút ▾ Ẩn xuống Toolbar)", "Ẩn Taskbar xuống Khay hệ thống",
            "Ẩn hoàn toàn cửa sổ khỏi thanh Taskbar Windows, chỉ hiển thị biểu tượng ở Toolbar (System Tray) và báo thông báo khi render xong.",
            "",
            "Nhấp chuột trái hoặc nhấp đúp vào biểu tượng trên Toolbar để mở lại cửa sổ bất cứ lúc nào."),
        new("5. Phím tắt & Thao tác Studio", "Ctrl + Enter  /  Nút ⚡ Nhiều file", "Đa luồng Render song song",
            "Đẩy văn bản hiện tại hoặc hàng loạt file .TXT/.MD xuống bảng Đa luồng Render ngay bên dưới mà không khóa màn hình soạn thảo.",
            "",
            "Hỗ trợ chế độ Tối đa công suất (Không giới hạn) hoặc chọn số luồng cố định; vừa render vừa gõ chương mới mượt mà."),
        new("5. Phím tắt & Thao tác Studio", "Ctrl + O", "Nhập tệp .TXT / .MD",
            "Mở hộp thoại chọn tệp văn bản (.txt hoặc .md) và tự động chuẩn hóa dấu câu đưa vào trình biên tập.",
            "",
            "Cũng có thể kéo-thả (Drag & Drop) trực tiếp tệp .txt/.md từ Windows Explorer vào ô soạn thảo."),
        new("5. Phím tắt & Thao tác Studio", "Ctrl + P  (hoặc nút 🎧 Nghe thử)", "Review giới hạn từ",
            "Nghe thử nhanh theo giới hạn số từ đã chọn (25 / 50 / 100 / 150 / 250 / 500 từ). Nếu đang bôi đen văn bản thì ưu tiên đọc đoạn bôi đen.",
            "",
            "Bản nghe thử phát ngay trên thanh Player đáy màn hình và không làm rác thư mục Nơi lưu."),

        // 6. Lệnh CLI / PowerShell
        new("6. Lệnh CLI / PowerShell", "BUILD-VA-CHAY.cmd", "Đóng gói & Chạy ngay",
            "Biên dịch bản Release Single-File vào Publish\\win-x64, tự tạo 3 thư mục Runtime, Voices, Output và mở ứng dụng.",
            "BUILD-VA-CHAY.cmd",
            "Chạy trực tiếp từ Command Prompt hoặc nhấp đúp chuột trong Windows Explorer."),
        new("6. Lệnh CLI / PowerShell", "powershell -ExecutionPolicy Bypass -File Scripts\\Build-Windows.ps1", "Lệnh Publish chuẩn",
            "Đóng gói ứng dụng cho win-x64 (hoặc thêm -Runtime win-arm64), bảo toàn dữ liệu hiện có trong Runtime/Voices/Output.",
            "powershell -ExecutionPolicy Bypass -File Scripts\\Build-Windows.ps1 -Runtime win-x64",
            "Tự động cấu hình settings.json trỏ vào Publish\\win-x64\\Output, Voices và Runtime."),
        new("6. Lệnh CLI / PowerShell", "Scripts\\Import-Engine-From-Web.ps1 -OldProjectFolder \"<đường_dẫn>\"", "Chép nhanh Runtime & Voices",
            "Sao chép toàn bộ thư mục Runtime (piper, ffmpeg) và Voices (.onnx, .onnx.json) từ thư mục dự án cũ sang dự án hiện tại.",
            "powershell -ExecutionPolicy Bypass -File Scripts\\Import-Engine-From-Web.ps1 -OldProjectFolder \"D:\\Path\\To\\OldFolder\"",
            "Tiết kiệm thời gian cài đặt thủ công khi nâng cấp phiên bản."),
        new("6. Lệnh CLI / PowerShell", "dotnet run --project Tests/SubtitleAddon.Tests.csproj", "Chạy kiểm thử tự động",
            "Thực thi bộ kiểm thử tự động kiểm tra tách câu, chuẩn hóa văn bản, tính toán SRT từ PCM, đổi tên Voice và Review giới hạn từ.",
            "dotnet run --project Tests/SubtitleAddon.Tests.csproj",
            "Đảm bảo toàn bộ thuật toán lõi hoạt động chính xác 100%.")
    ];

    private Control BuildGuideTabContent()
    {
        var page = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        var mainGrid = Theme.Table(1, 2);
        mainGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 56));
        mainGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 44));
        page.Controls.Add(mainGrid);

        var allEntries = GetAllGuideEntries();

        // Top Card: Interactive Command Table + Filter & Quick Insert
        var topCard = new SurfacePanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            Margin = new Padding(0, 0, 0, 8)
        };
        var topLayout = Theme.Table(1, 3);
        topLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        topLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        topLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        topCard.Controls.Add(topLayout);

        var filterRow = Theme.Table(3, 1);
        filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));

        var searchBox = Theme.Textbox();
        searchBox.PlaceholderText = "🔍 Tìm kiếm câu lệnh text (VD: [pause, [nghi, [[, markdown, dấu phẩy, Ctrl+P...)";

        var categoryCombo = Theme.Combo();
        categoryCombo.Margin = new Padding(10, 3, 10, 3);
        categoryCombo.Items.Add("Tất cả nhóm hướng dẫn & câu lệnh");
        foreach (var cat in allEntries.Select(x => x.Category).Distinct())
            categoryCombo.Items.Add(cat);
        categoryCombo.SelectedIndex = 0;

        var countLabel = Theme.Label($"{allEntries.Count} mục tra cứu", 9f, true, Theme.AccentText);
        countLabel.TextAlign = ContentAlignment.MiddleRight;

        filterRow.Controls.Add(searchBox, 0, 0);
        filterRow.Controls.Add(categoryCombo, 1, 0);
        filterRow.Controls.Add(countLabel, 2, 0);
        topLayout.Controls.Add(filterRow, 0, 0);

        var cmdList = NewAudioList(checkBoxes: false);
        cmdList.MultiSelect = false;
        cmdList.Columns.Add("NHÓM LỆNH / QUY TẮC", 215);
        cmdList.Columns.Add("CÂU LỆNH TEXT / CÚ PHÁP", 265);
        cmdList.Columns.Add("TÁC ĐỘNG / NHỊP NGHỈ", 165);
        cmdList.Columns.Add("Ý NGHĨA & CÁCH SỬ DỤNG", 520);

        var actionBar = Theme.Table(5, 1);
        actionBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 228));
        actionBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 296));
        actionBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 182));
        actionBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 246));
        actionBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var insertCmdBtn = Btn("✦ Chèn lệnh đang chọn vào Biên tập", ModernButton.Kind.Primary);
        insertCmdBtn.Dock = DockStyle.Fill;
        insertCmdBtn.Margin = new Padding(0, 2, 6, 2);
        insertCmdBtn.Click += (_, _) =>
        {
            if (cmdList.SelectedItems.Count == 0 || cmdList.SelectedItems[0].Tag is not GuideEntry entry)
            {
                SetStatus("Hãy chọn một dòng câu lệnh trong bảng trước khi chèn.", Theme.GoldText);
                return;
            }
            var toInsert = string.IsNullOrEmpty(entry.InsertSample) ? entry.CommandSyntax : entry.InsertSample;
            int pos = _editor.SelectionStart;
            _editor.Text = _editor.Text.Insert(pos, toInsert);
            _editor.SelectionStart = pos + toInsert.Length;
            ShowPage("editor");
            _editor.Focus();
            SetStatus($"Đã chèn '{toInsert.Trim()}' vào trình soạn thảo Biên tập.", Theme.Green);
        };

        var loadDemoBtn = Btn("📄 Nạp kịch bản mẫu vào Biên tập", ModernButton.Kind.Gold);
        loadDemoBtn.Dock = DockStyle.Fill;
        loadDemoBtn.Margin = new Padding(0, 2, 6, 2);
        loadDemoBtn.Click += (_, _) =>
        {
            var demoScript =
                "// KỊCH BẢN MẪU HƯỚNG DẪN SỬ DỤNG CÂU LỆNH TEXT & NGỮ ĐIỆU TRONG STUDIO\n" +
                "[[Ghi chú đạo diễn: Đoạn trong ngoặc vuông kép này sẽ tự động bị bỏ qua khi đọc]]\n" +
                "Chương 1: Tiếng vọng từ hư không. [pause:1.2s]\n\n" +
                "Đêm đã về khuya, sương mù bao phủ khắp sườn núi. [nghi:700ms] Trong căn phòng nhỏ, ánh nến vẫn lập lòe bên trang sách cũ.\n" +
                "\"Ngươi thực sự muốn bước vào cánh cổng đó sao?\" [pause:600ms] Lão nhân trầm giọng hỏi.\n" +
                "Hắn đột ngột quay phắt lại, chỉ thẳng tay và quát lớn:\n" +
                "\"Kẻ phản bội... [pause:350ms] Chính! [pause:120ms] Là ngươi!\" [pause:700ms]\n\n" +
                "[note: Chuyển cảnh - Ngân âm cuối câu]\n" +
                "Giữa khoảng không mênh mông ấy, chỉ còn tiếng chuông ngân vang khắp sườn núi... vang mãi... vang mãi... [break:1.5s] Một hành trình mới chính thức bắt đầu.";
            if (!string.IsNullOrWhiteSpace(_editor.Text) &&
                MessageBox.Show("Thay thế nội dung đang có trong Biên tập bằng kịch bản mẫu?", "Nạp kịch bản mẫu",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            _editor.Text = demoScript;
            ShowPage("editor");
            SetStatus("Đã nạp kịch bản mẫu có tích hợp các câu lệnh [pause], [nghi], [[ghi chú]] vào Biên tập.", Theme.Green);
        };

        var copySyntaxBtn = Btn("📋 Sao chép cú pháp lệnh", ModernButton.Kind.Secondary);
        copySyntaxBtn.Dock = DockStyle.Fill;
        copySyntaxBtn.Margin = new Padding(0, 2, 6, 2);
        copySyntaxBtn.Click += (_, _) =>
        {
            if (cmdList.SelectedItems.Count == 0 || cmdList.SelectedItems[0].Tag is not GuideEntry entry) return;
            Clipboard.SetText(entry.CommandSyntax);
            SetStatus($"Đã sao chép vào Clipboard: {entry.CommandSyntax}", Theme.Green);
        };

        var copyDnaBtn = Btn("🤖 Sao chép DNA đào tạo AI (ChatGPT)", ModernButton.Kind.Secondary);
        copyDnaBtn.Dock = DockStyle.Fill;
        copyDnaBtn.Margin = new Padding(0, 2, 0, 2);
        copyDnaBtn.Click += (_, _) =>
        {
            string[] candidates =
            [
                Path.Combine(AppContext.BaseDirectory, "DNA_LANG_KINH_HU_KHONG_TTS.md"),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "DNA_LANG_KINH_HU_KHONG_TTS.md"))
            ];
            var found = candidates.FirstOrDefault(File.Exists);
            var textToCopy = found is not null ? File.ReadAllText(found, System.Text.Encoding.UTF8) : BuildFullManualText();
            Clipboard.SetText(textToCopy);
            SetStatus("Đã sao chép toàn bộ nội dung DNA_LANG_KINH_HU_KHONG_TTS.md vào Clipboard để dán cho ChatGPT/AI!", Theme.Green);
        };

        actionBar.Controls.Add(insertCmdBtn, 0, 0);
        actionBar.Controls.Add(loadDemoBtn, 1, 0);
        actionBar.Controls.Add(copySyntaxBtn, 2, 0);
        actionBar.Controls.Add(copyDnaBtn, 3, 0);
        topLayout.Controls.Add(actionBar, 0, 1);
        topLayout.Controls.Add(cmdList, 0, 2);
        mainGrid.Controls.Add(topCard, 0, 0);

        // Bottom Card: Detailed Manual & Live Command Inspector
        var bottomCard = new SurfacePanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14)
        };
        var bottomLayout = Theme.Table(1, 2);
        bottomLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        bottomLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bottomCard.Controls.Add(bottomLayout);

        var detailTitle = Theme.Label("CHI TIẾT CÂU LỆNH & SỔ TAY HƯỚNG DẪN TOÀN DIỆN", 9.75f, true);
        bottomLayout.Controls.Add(detailTitle, 0, 0);

        var manualBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.InputBg,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 9.75f),
            DetectUrls = false,
            WordWrap = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Text = BuildFullManualText()
        };
        bottomLayout.Controls.Add(manualBox, 0, 1);
        mainGrid.Controls.Add(bottomCard, 0, 1);

        void PopulateGuideList()
        {
            var q = searchBox.Text.Trim();
            var cat = categoryCombo.SelectedIndex <= 0 ? "" : categoryCombo.SelectedItem?.ToString() ?? "";
            cmdList.BeginUpdate();
            try
            {
                cmdList.Items.Clear();
                foreach (var entry in allEntries)
                {
                    if (!string.IsNullOrEmpty(cat) && entry.Category != cat) continue;
                    if (!string.IsNullOrWhiteSpace(q) &&
                        !(entry.CommandSyntax.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
                          entry.Meaning.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
                          entry.Category.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
                          entry.EffectValue.Contains(q, StringComparison.CurrentCultureIgnoreCase)))
                        continue;

                    var item = new ListViewItem(entry.Category) { Tag = entry };
                    item.SubItems.Add(entry.CommandSyntax);
                    item.SubItems.Add(entry.EffectValue);
                    item.SubItems.Add(entry.Meaning);
                    cmdList.Items.Add(item);
                }
                countLabel.Text = $"{cmdList.Items.Count}/{allEntries.Count} mục tra cứu";
            }
            finally { cmdList.EndUpdate(); }
        }

        cmdList.SelectedIndexChanged += (_, _) =>
        {
            if (cmdList.SelectedItems.Count == 0 || cmdList.SelectedItems[0].Tag is not GuideEntry entry)
                return;
            detailTitle.Text = $"CHI TIẾT LỆNH: {entry.CommandSyntax}   ({entry.EffectValue})";
            manualBox.Text =
                $"■ CÚ PHÁP / KÝ HIỆU :  {entry.CommandSyntax}\n" +
                $"■ PHÂN NHÓM          :  {entry.Category}\n" +
                $"■ TÁC ĐỘNG HỆ THỐNG  :  {entry.EffectValue}\n" +
                $"■ Ý NGHĨA CHI TIẾT   :  {entry.Meaning}\n" +
                $"■ VÍ DỤ MINH HỌA     :\n{entry.ExampleDetail}\n" +
                $"────────────────────────────────────────────────────────────────────────────────────\n\n" +
                BuildFullManualText();
        };

        searchBox.TextChanged += (_, _) => PopulateGuideList();
        categoryCombo.SelectedIndexChanged += (_, _) => PopulateGuideList();
        PopulateGuideList();

        return page;
    }

    private static string BuildFullManualText() =>
        """
        ================================================================================
        SỔ TAY HƯỚNG DẪN SỬ DỤNG TOÀN DIỆN – LANGKINHHUKHONG.TTS STUDIO v1.4
        ================================================================================

        I. CÁC CÂU LỆNH TEXT TRONG TRÌNH BIÊN TẬP (INLINE SCRIPT COMMANDS)
        --------------------------------------------------------------------------------
        Bạn có thể gõ trực tiếp các câu lệnh dưới đây xen kẽ trong văn bản truyện/kịch bản.
        Hệ thống sẽ tự động phân tích để điều khiển nhịp nghỉ âm thanh mà KHÔNG đọc thành lời
        và KHÔNG làm xuất hiện ký tự lệnh trong phụ đề .SRT:

        1. Lệnh chèn khoảng nghỉ tùy chỉnh (50ms -> 10.000ms):
           • [pause:500ms]   hoặc  [pause:1.5s]   -> Nghỉ chính xác 500 mili-giây hoặc 1.5 giây.
           • [nghi:800ms]    hoặc  [nghi:2s]      -> Cú pháp tiếng Việt tương đương [pause:...].
           • [break:1000ms]  hoặc  [im:1s]        -> Bí danh tương đương để chèn khoảng lặng giữa câu/đoạn.

        2. Lệnh ghi chú kịch bản (tự động bỏ qua khi đọc TTS & tạo SRT):
           • [[Nội dung ghi chú]]                 -> Mọi chữ trong cặp ngoặc vuông kép [[...]] sẽ bị bỏ qua.
           • [note: Nội dung] / [ghichu: Nội dung]-> Ghi chú có nhãn đầu, tự động lọc bỏ khi đọc.
           • // Ghi chú cả dòng                   -> Dòng bắt đầu bằng '//' sẽ được bỏ qua hoàn toàn.

        II. QUY TẮC DẤU CÂU & NHỊP NGHỈ TỰ ĐỘNG (AUTOMATIC PACING)
        --------------------------------------------------------------------------------
        • Mở đầu file Audio          : Tự động chèn 160ms khoảng lặng để không bị mất âm đầu.
        • Dấu phẩy (,), hai chấm (:) : Giữ liền mạch trong câu ngắn; tách thông minh câu nhiều vế dài.
        • Dấu chấm ( . / 。 )        : Kết thúc câu + chèn 310ms khoảng lặng PCM.
        • Dấu chấm hỏi ( ? / ？ )    : Kết thúc câu hỏi + chèn 330ms khoảng lặng PCM.
        • Dấu chấm than ( ! / ！ )   : Kết thúc câu cảm thán + chèn 350ms khoảng lặng PCM.
        • Dấu ba chấm ( ... / … )    : Ngân nguyên âm cuối bằng WSOLA + chèn 480ms khoảng lặng PCM.
        • Xuống 1 dòng (Enter)       : Ngắt câu + chèn 350ms khoảng lặng.
        • Xuống 2 dòng (Đoạn mới)    : Ngắt đoạn văn + chèn 550ms khoảng lặng.
        • Số & Giờ (3.14, 1,5, 10:30): Tự động nhận diện chữ số hai bên, không cắt câu nhầm.

        II-B. KỸ THUẬT NHẤN ÂM, LÊN/XUỐNG GIỌNG & KÉO DÀI (NGÂN ÂM) CUỐI CÂU
        --------------------------------------------------------------------------------
        1. Ép LÊN GIỌNG tại một từ cụ thể giữa câu (Ví dụ: "Kẻ phản bội... Chính (lên giọng) là ngươi"):
           • Cách 1 (Bật cao chữ "Chính" dứt khoát bằng ! hoặc ?! kèm nhịp nghỉ ngắn):
             Kẻ phản bội... [pause:350ms] Chính! [pause:120ms] Là ngươi!
             (Hoặc ngỡ ngàng phát hiện): Kẻ phản bội... [pause:350ms] Chính?! [pause:150ms] Chính là ngươi!
           • Cách 2 (Treo cao chữ "Chính" liền hơi trong 1 câu bằng dấu phẩy):
             Kẻ phản bội... [pause:300ms] Chính, là ngươi!
           • Cách 3 (Điệp từ leo thang 2 nhịp kiểu phim/truyện kiếm hiệp):
             Kẻ phản bội... [pause:400ms] Chính là... [pause:200ms] chính là ngươi!

        2. Kỹ thuật NHẤN ÂM vào từ khóa:
           • Đóng khung bằng dấu phẩy: "Hắn, tuyệt đối, sẽ không bao giờ quay lại."
           • Đặt [pause:300ms] ngay trước từ khóa: "Người đứng sau toàn bộ chuyện này chính là... [pause:300ms] ngươi!"

        3. Điều khiển LÊN GIỌNG vs. XUỐNG GIỌNG cuối câu:
           • Lên giọng (nghi vấn/chất vấn): Dùng từ hỏi (sao, ư, à, hả) + dấu '?' hoặc '?!'.
           • Xuống giọng (trầm buồn/lắng đọng): Kết câu bằng dấu ba chấm '...' + [pause:700ms].
           • Tương phản Lên - Xuống: "Ngươi nghĩ ta sợ ngươi sao? [pause:450ms] Không... ta chỉ thấy tiếc cho ngươi thôi..."

        4. Kỹ thuật KÉO DÀI / NGÂN ÂM CUỐI (Tuyệt đối KHÔNG gõ lặp chữ kiểu 'ơiiii' để tránh lỗi đánh vần):
           • Dùng dấu '...' sau từ cần ngân (VD: "Giết...", "Đợi ta với... [pause:600ms]").
           • Nhân âm bằng gạch nối '-' hoặc tách nhịp '...':
             "Sư phụ ơi... ơ-i... [pause:600ms]"  |  "Haiz... ài... [pause:500ms]"
           • Lặp từ láy âm cuối kèm '...' và [pause:900ms] để giữ dư âm:
             "Tiếng chuông ngân vang khắp sườn núi... vang mãi... vang mãi... [pause:900ms]"

        III. BỐ CỤC GIAO DIỆN 4 PHÂN KHU
        --------------------------------------------------------------------------------
        1. Biên tập (0)          : Tích hợp Trình soạn thảo Kịch bản + Bảng điều khiển giọng ở trên
                                   và Bảng Đa luồng Render trực tiếp ngay bên dưới.
        2. Nơi lưu               : Quản lý, nghe thử, sao lưu các bản thu MP3 + SRT trong Output.
        3. Ghép Audio            : Nối nhiều chương MP3 + đồng bộ phụ đề SRT chuẩn xác.
        4. Cài đặt và tài liệu   : Gồm 3 tab: Cài đặt | Danh sách giọng | Hướng dẫn dùng.
        """;

    private TextBox AddFolderBrowser(TableLayoutPanel layout, int row, string path, Func<string> folderKind)
    {
        var grid = Theme.Table(3, 1);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 136));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 114));
        var input = Theme.Textbox(path);
        input.Margin = new Padding(0, 3, 0, 3);
        grid.Controls.Add(input, 0, 0);

        var browse = Btn("Chọn thư mục", ModernButton.Kind.Secondary, 126);
        browse.Dock = DockStyle.Fill;
        browse.Margin = new Padding(8, 5, 0, 5);
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Chọn thư mục cho " + folderKind(),
                UseDescriptionForTitle = true,
                InitialDirectory = Directory.Exists(input.Text) ? input.Text : AppContext.BaseDirectory
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                input.Text = dialog.SelectedPath;
                UpdateFolderSetting(folderKind(), input.Text);
            }
        };
        grid.Controls.Add(browse, 1, 0);

        var openBtn = Btn("📂 Mở", ModernButton.Kind.Gold, 104);
        openBtn.Dock = DockStyle.Fill;
        openBtn.Margin = new Padding(8, 5, 0, 5);
        openBtn.Click += (_, _) => OpenFolder(input.Text);
        grid.Controls.Add(openBtn, 2, 0);

        input.Leave += (_, _) => UpdateFolderSetting(folderKind(), input.Text);
        layout.Controls.Add(grid, 0, row);
        return input;
    }

    private static ListView NewAudioList(bool checkBoxes) => new DarkListView(checkBoxes);

    private sealed record SpeedOption(double Value)
    {
        public override string ToString() => $"{Value:0.##}×";
    }

    private sealed record WordLimitOption(int Words)
    {
        public override string ToString() => $"{Words} từ";
    }

    private sealed record ParallelOption(int Count)
    {
        public override string ToString() => Count <= 0
            ? "⚡ Tối đa (Không giới hạn)"
            : $"{Count} luồng song song";
    }
}
