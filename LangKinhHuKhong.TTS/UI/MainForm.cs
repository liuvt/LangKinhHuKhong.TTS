using System.Drawing;
using System.Windows.Forms;
using LangKinhHuKhong.TTS.Core;
using LangKinhHuKhong.TTS.Models;

namespace LangKinhHuKhong.TTS.UI;

public sealed partial class MainForm : Form
{
    private readonly Storage _storage = new();
    private readonly NativeVoicePaths _paths = new();
    private readonly AudioPipeline _audio;
    private readonly JobManager _jobs;
    private readonly WindowsMp3Player _player = new();
    private readonly List<VoiceModel> _voices = [];
    private readonly System.Windows.Forms.Timer _playTimer = new() { Interval = 350 };
    private CancellationTokenSource? _jobCancellation;
    private bool _busy;
    private bool _isSeeking;
    private bool _suppressMergeRefresh;
    private readonly System.Windows.Forms.Timer _wordTimer = new() { Interval = 350 };
    private bool _closing;
    private string _activePage = "editor";

    // Layout controls
    private readonly Panel _pageHost = new() { Dock = DockStyle.Fill, BackColor = Theme.Background };
    private readonly Dictionary<string, Control> _pages = new();
    private readonly Dictionary<string, ModernButton> _navigation = new();
    private Label _headerTitle = null!;
    private Label _headerSubtitle = null!;
    private Label _footerStatus = null!;
    private Label _footerCount = null!;
    private Label _charCount = null!;
    private Label _wordCount = null!;
    private Label _textWarning = null!;
    private RichTextBox _editor = null!;
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

    // Voice Lab (ElevenLabs style catalog & sample studio)
    private ListView _voiceCatalogList = null!;
    private TextBox _voiceLabSearch = null!;
    private ComboBox _voiceLabCategory = null!;
    private ComboBox _voiceLabLanguage = null!;
    private TextBox _sampleTextBox = null!;
    private Label _voiceLabSummary = null!;
    private ModernButton _renderAllExamplesButton = null!;

    // Library & Merge controls
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
    private Label _dependencies = null!;

    public MainForm()
    {
        _audio = new AudioPipeline(_paths, _storage);
        _jobs = new JobManager(_storage, _paths, _audio);
        Text = "LangKinhHuKhong.TTS Studio | v1.2 | ElevenLabs-Style Local Workstation";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1160, 760);
        Size = new Size(1540, 940);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        KeyPreview = true;
        DoubleBuffered = true;
        BuildLayout();
        ReloadVoices();
        _playTimer.Tick += (_, _) => UpdatePlaybackPosition();
        _wordTimer.Tick += (_, _) =>
        {
            _wordTimer.Stop();
            if (!_closing && !_editor.IsDisposed)
            {
                var words = TextProcessor.CountWords(_editor.Text);
                var estMinutes = Math.Max(0.1, words / 150.0);
                _wordCount.Text = $"{words:N0} từ · {_editor.TextLength:N0} ký tự · Ước tính đọc ~{estMinutes:0.#} phút";
            }
        };
        _playTimer.Start();
        RefreshLibrary();
        UpdateCounts();
        ShowPage("editor");
        FormClosing += OnClosing;
        KeyDown += OnHotkey;
    }

    private void OnHotkey(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.O) { ImportText(); e.SuppressKeyPress = true; }
        if (e.Control && e.KeyCode == Keys.Enter) { _ = StartRenderAsync(); e.SuppressKeyPress = true; }
        if (e.Control && e.KeyCode == Keys.P) { _ = StartReviewSnippetAsync(); e.SuppressKeyPress = true; }
        if (e.Control && e.KeyCode == Keys.D1) { ShowPage("editor"); e.SuppressKeyPress = true; }
        if (e.Control && e.KeyCode == Keys.D2) { ShowPage("voices"); e.SuppressKeyPress = true; }
        if (e.Control && e.KeyCode == Keys.D3) { ShowPage("library"); e.SuppressKeyPress = true; }
        if (e.Control && e.KeyCode == Keys.D4) { ShowPage("merge"); e.SuppressKeyPress = true; }
        if (e.Control && e.KeyCode == Keys.D5) { ShowPage("settings"); e.SuppressKeyPress = true; }
        if (e.Control && e.KeyCode == Keys.D6) { ShowPage("guide"); e.SuppressKeyPress = true; }
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        _closing = true;
        _jobCancellation?.Cancel();
        _playTimer.Stop();
        _wordTimer.Stop();
        _player.Dispose();
    }

    private void SafeUi(Action work)
    {
        if (_closing || IsDisposed || !IsHandleCreated) return;
        try
        {
            if (InvokeRequired) BeginInvoke(new Action(() => { if (!_closing && !IsDisposed) work(); }));
            else work();
        }
        catch (InvalidOperationException) { }
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
