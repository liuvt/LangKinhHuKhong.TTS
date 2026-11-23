using System.Drawing;
using System.Windows.Forms;
using LangKinhHuKhong.TTS.Core;
using LangKinhHuKhong.TTS.Models;

namespace LangKinhHuKhong.TTS.UI;

public sealed partial class MainForm : Form
{
    private readonly SynchronizationContext _uiContext;
    private readonly Storage _storage = new();
    private readonly NativeVoicePaths _paths = new();
    private readonly AudioPipeline _audio;
    private readonly JobManager _jobs;
    private readonly RenderQueueManager _renderQueue;
    private readonly WindowsMp3Player _player = new();
    private readonly List<VoiceModel> _voices = [];
    private readonly System.Windows.Forms.Timer _playTimer = new() { Interval = 350 };
    private readonly System.Windows.Forms.Timer _queueClockTimer = new() { Interval = 1000 };
    private CancellationTokenSource? _jobCancellation;
    private bool _busy; // Used only for exclusive foreground tasks (Review / Batch Voice Examples / Merge)
    private bool _isSeeking;
    private bool _suppressMergeRefresh;
    private bool _syncingParallelCombo;
    private readonly System.Windows.Forms.Timer _wordTimer = new() { Interval = 350 };
    private bool _closing;
    private bool _forceExit;
    private bool _shownTrayHintOnce;
    private string _activePage = "editor";
    private string _activeSettingsTab = "settings";

    // System Tray / Notification Toolbar controls
    private NotifyIcon _trayIcon = null!;
    private ContextMenuStrip _trayMenu = null!;
    private ToolStripMenuItem _trayQueueStatusItem = null!;
    private ToolStripMenuItem _trayCancelAllItem = null!;

    // Layout & Sub-tab controls
    private readonly Panel _pageHost = new() { Dock = DockStyle.Fill, BackColor = Theme.Background };
    private readonly Dictionary<string, Control> _pages = new();
    private readonly Dictionary<string, ModernButton> _navigation = new();
    private readonly Panel _settingsTabHost = new() { Dock = DockStyle.Fill, BackColor = Theme.Background };
    private readonly Dictionary<string, Control> _settingsSubPages = new();
    private readonly Dictionary<string, ModernButton> _settingsTabButtons = new();

    private Label _headerTitle = null!;
    private Label _headerSubtitle = null!;
    private ModernButton _headerQueueButton = null!;
    private ModernButton _headerHideTrayButton = null!;
    private Label _footerStatus = null!;
    private Label _footerCount = null!;
    private Label _charCount = null!;
    private Label _wordCount = null!;
    private Label _textWarning = null!;
    private RichTextBox _editor = null!;
    private TextBox _renderTitleInput = null!;
    private ComboBox _studioParallelCombo = null!;
    private ModernButton _renderButton = null!;
    private ModernButton _reviewButton = null!;
    private ModernButton _reviewToolbarButton = null!;
    private ComboBox _reviewLimitCombo = null!;
    private ModernButton _cancelButton = null!;
    private ModernButton _mergeButton = null!;
    private ComboBox _voiceCombo = null!;
    private ComboBox _groupCombo = null!;
    private ComboBox _languageCombo = null!;
    private TextBox _voiceSearchBox = null!;
    private ModernButton _previewVoiceButton = null!;
    private ModernButton _renameVoiceQuickButton = null!;
    private Label _voiceLanguageHint = null!;
    private bool _loadingVoices;
    private ComboBox _speakerCombo = null!;
    private ComboBox _speedCombo = null!;
    private ComboBox _engineCombo = null!;
    private CheckBox _srtOnRender = null!;
    private ComboBox _mergeSpeedCombo = null!;
    private ModernProgressBar _progress = null!;
    private ModernProgressBar _mergeProgress = null!;
    private ModernProgressBar _voiceLabProgress = null!;
    private Label _renderStatus = null!;

    // Multi-Stream Parallel Render Queue controls (integrated below Editor & Voice Inspector)
    private Label _queueSectionTitle = null!;
    private ListView _renderQueueList = null!;
    private ComboBox _queueParallelCombo = null!;
    private Label _queueSummaryLabel = null!;
    private ModernProgressBar _queueOverallProgress = null!;

    // Voice Catalog (inside Settings & Docs -> "Danh sách giọng")
    private ListView _voiceCatalogList = null!;
    private TextBox _voiceLabSearch = null!;
    private ComboBox _voiceLabCategory = null!;
    private ComboBox _voiceLabLanguage = null!;
    private TextBox _sampleTextBox = null!;
    private Label _voiceLabSummary = null!;
    private ModernButton _renderAllExamplesButton = null!;

    // Library, Merge & Settings controls
    private ListView _library = null!;
    private TextBox _searchLibrary = null!;
    private ListView _mergeList = null!;
    private TextBox _mergeTitle = null!;
    private Label _mergeHint = null!;
    private Label _playerTitle = null!;
    private Label _playerTime = null!;
    private ModernButton _playerPlayButton = null!;
    private TrackBar _playerSeek = null!;
    private TextBox _outputPath = null!;
    private TextBox _voicesPath = null!;
    private TextBox _runtimePath = null!;
    private CheckBox _minimizeToTrayCheck = null!;
    private CheckBox _closeToTrayCheck = null!;
    private Label _dependencies = null!;

    public MainForm()
    {
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _audio = new AudioPipeline(_paths, _storage);
        _jobs = new JobManager(_storage, _paths, _audio);
        _renderQueue = new RenderQueueManager(_jobs, _storage.Settings.MaxParallelRenders);
        Text = "LangKinhHuKhong.TTS Studio | v1.4 | Crimson Dark Workstation";
        Icon = LoadApplicationIcon();
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1200, 820);
        Size = new Size(1560, 950);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        KeyPreview = true;
        DoubleBuffered = true;

        InitializeSystemTray();
        BuildLayout();
        SyncParallelCombos(_storage.Settings.MaxParallelRenders);
        ReloadVoices();

        _renderQueue.StateChanged += () => SafeUi(RefreshRenderQueueUi);
        _renderQueue.JobCompleted += (snap, entry) => SafeUi(() => OnQueuedJobCompleted(snap, entry));
        _renderQueue.JobFailed += (snap, error) => SafeUi(() => OnQueuedJobFailed(snap, error));

        _playTimer.Tick += (_, _) => UpdatePlaybackPosition();
        _queueClockTimer.Tick += (_, _) =>
        {
            if (_renderQueue.ActiveCount > 0) RefreshRenderQueueUi();
        };
        _wordTimer.Tick += (_, _) =>
        {
            _wordTimer.Stop();
            if (!_closing && !_editor.IsDisposed)
            {
                var snapshot = _editor.Text;
                var charLen = snapshot.Length;
                _ = Task.Run(() =>
                {
                    var words = TextProcessor.CountWords(snapshot);
                    var estMinutes = Math.Max(0.1, words / 150.0);
                    SafeUi(() =>
                    {
                        if (_editor.TextLength == charLen)
                            _wordCount.Text = $"{words:N0} từ · {charLen:N0} ký tự · Ước tính đọc ~{estMinutes:0.#} phút";
                    });
                });
            }
        };
        _playTimer.Start();
        _queueClockTimer.Start();
        RefreshLibrary();
        RefreshRenderQueueUi();
        UpdateCounts();
        ShowPage("editor");

        Resize += OnFormResize;
        FormClosing += OnClosing;
        KeyDown += OnHotkey;
    }

    private static Icon? LoadApplicationIcon()
    {
        try
        {
            var icoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(icoPath)) return new Icon(icoPath);
        }
        catch { }
        try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
        catch { return SystemIcons.Application; }
    }

    private void InitializeSystemTray()
    {
        _trayMenu = new ContextMenuStrip
        {
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            Renderer = Theme.CreateMenuRenderer()
        };
        var openItem = new ToolStripMenuItem("🗔  Mở Studio Lăng Kính Hư Không")
        {
            Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
            ForeColor = Theme.Text
        };
        openItem.Click += (_, _) => RestoreFromSystemTray();

        _trayQueueStatusItem = new ToolStripMenuItem("⚡  Đa luồng Render: Sẵn sàng") { ForeColor = Theme.Text };
        _trayQueueStatusItem.Click += (_, _) =>
        {
            RestoreFromSystemTray();
            ShowPage("editor");
            _renderQueueList?.Focus();
        };

        var openOutputItem = new ToolStripMenuItem("📂  Mở thư mục Nơi lưu (Output)") { ForeColor = Theme.Text };
        openOutputItem.Click += (_, _) => OpenFolder(_storage.Settings.OutputFolder);

        _trayCancelAllItem = new ToolStripMenuItem("■  Hủy tất cả luồng đang render") { Enabled = false, ForeColor = Theme.Text };
        _trayCancelAllItem.Click += (_, _) => _renderQueue.CancelAll();

        var exitItem = new ToolStripMenuItem("✕  Thoát hoàn toàn ứng dụng") { ForeColor = Theme.DangerText };
        exitItem.Click += (_, _) =>
        {
            if (_renderQueue.ActiveCount > 0 && MessageBox.Show(
                    $"Đang có {_renderQueue.ActiveCount} luồng render chưa xong.\nBạn có chắc chắn muốn hủy tất cả và thoát hoàn toàn?",
                    "Xác nhận thoát", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            _forceExit = true;
            Close();
        };

        _trayMenu.Items.AddRange([
            openItem,
            _trayQueueStatusItem,
            new ToolStripSeparator(),
            openOutputItem,
            _trayCancelAllItem,
            new ToolStripSeparator(),
            exitItem
        ]);

        _trayIcon = new NotifyIcon
        {
            Icon = Icon ?? SystemIcons.Application,
            Text = "Lăng Kính Hư Không TTS · Sẵn sàng",
            Visible = true,
            ContextMenuStrip = _trayMenu
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromSystemTray();
        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) RestoreFromSystemTray();
        };
    }

    public void HideToSystemTray()
    {
        if (_closing || IsDisposed) return;
        ShowInTaskbar = false;
        Hide();
        if (!_shownTrayHintOnce)
        {
            _shownTrayHintOnce = true;
            var (running, queued, _, _, _) = _renderQueue.GetSummary();
            string msg = running + queued > 0
                ? $"Đang chạy ngầm {running} luồng (chờ {queued}). Nhấp chuột vào biểu tượng ở Toolbar để mở lại."
                : "Ứng dụng đã ẩn khỏi Taskbar và đang chạy ở Toolbar (Khay hệ thống). Nhấp vào biểu tượng để mở lại.";
            _trayIcon.ShowBalloonTip(2500, "Lăng Kính Hư Không TTS Studio", msg, ToolTipIcon.Info);
        }
    }

    public void RestoreFromSystemTray()
    {
        if (_closing || IsDisposed) return;
        ShowInTaskbar = true;
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    private void OnFormResize(object? sender, EventArgs e)
    {
        if (_closing) return;
        if (WindowState == FormWindowState.Minimized && _storage.Settings.MinimizeToTray && ShowInTaskbar)
        {
            BeginInvoke(HideToSystemTray);
        }
    }

    private void OnHotkey(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.O) { ImportText(); e.SuppressKeyPress = true; }
        if (e.Control && e.KeyCode == Keys.Enter) { EnqueueCurrentEditorRender(); e.SuppressKeyPress = true; }
        if (e.Control && e.KeyCode == Keys.P) { _ = StartReviewSnippetAsync(); e.SuppressKeyPress = true; }
        if (e.Control && e.KeyCode == Keys.M) { HideToSystemTray(); e.SuppressKeyPress = true; }
        if (e.Control && e.KeyCode == Keys.D1) { ShowPage("editor"); e.SuppressKeyPress = true; }
        if (e.Control && e.KeyCode == Keys.D2) { ShowPage("library"); e.SuppressKeyPress = true; }
        if (e.Control && e.KeyCode == Keys.D3) { ShowPage("merge"); e.SuppressKeyPress = true; }
        if (e.Control && e.KeyCode == Keys.D4) { ShowSettingsTab("settings"); e.SuppressKeyPress = true; }
        if (e.Control && e.KeyCode == Keys.D5) { ShowSettingsTab("voices"); e.SuppressKeyPress = true; }
        if (e.Control && e.KeyCode == Keys.D6) { ShowSettingsTab("guide"); e.SuppressKeyPress = true; }
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_forceExit && e.CloseReason == CloseReason.UserClosing &&
            _renderQueue.ActiveCount > 0 && _storage.Settings.CloseToTrayWhenBusy)
        {
            var choice = MessageBox.Show(
                this,
                $"Hiện đang có {_renderQueue.ActiveCount} luồng render âm thanh đang chạy!\n\n" +
                "• Bấm [Yes] để ẨN KHỎI TASKBAR xuống Toolbar (Khay hệ thống) và tiếp tục render ngầm.\n" +
                "• Bấm [No] để HỦY toàn bộ luồng render và thoát hẳn.\n" +
                "• Bấm [Cancel] để quay lại cửa sổ.",
                "Đa luồng Render đang hoạt động",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            if (choice == DialogResult.Yes)
            {
                e.Cancel = true;
                HideToSystemTray();
                return;
            }
            if (choice == DialogResult.Cancel)
            {
                e.Cancel = true;
                return;
            }
        }

        _closing = true;
        _jobCancellation?.Cancel();
        _renderQueue.Dispose();
        _playTimer.Stop();
        _queueClockTimer.Stop();
        _wordTimer.Stop();
        _player.Dispose();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _trayMenu?.Dispose();
    }

    private void SafeUi(Action work)
    {
        if (_closing || IsDisposed) return;
        if (!InvokeRequired)
        {
            work();
            return;
        }
        _uiContext.Post(_ =>
        {
            if (!_closing && !IsDisposed) work();
        }, null);
    }

    private static ModernButton Btn(string text, ModernButton.Kind style = ModernButton.Kind.Secondary, int width = 112)
        => new() { Text = text, Style = style, Width = width };

    private void SetStatus(string message, Color? color = null)
    {
        SafeUi(() => { _footerStatus.Text = message; _footerStatus.ForeColor = color ?? Theme.Muted; });
    }

    private static void ShowError(Exception error, string context)
    {
        MessageBox.Show($"{context}\n\n{error.Message}", "LangKinhHuKhong Studio",
            MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
