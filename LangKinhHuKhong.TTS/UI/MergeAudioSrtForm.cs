using System.Globalization;
using LangKinhHuKhong.TTS.Core;

namespace LangKinhHuKhong.TTS.UI;

/// <summary>
/// Standalone WinForms dialog; attach to the application's existing "Ghép MP3" action.
/// Pair by basename: Chapter01.mp3 + Chapter01.srt, or choose an SRT manually.
/// </summary>
public sealed class MergeAudioSrtForm : Form
{
    private readonly List<MergeTrack> _tracks = new();
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, MultiSelect = false, RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        BackgroundColor = Theme.Inset, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
        BorderStyle = BorderStyle.None, GridColor = Theme.Border, EnableHeadersVisualStyles = false,
        ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
        CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal
    };
    private readonly Label _status = new() { Dock = DockStyle.Fill, Text = "Chưa chọn MP3.", AutoEllipsis = true, ForeColor = Theme.Muted, BackColor = Color.Transparent };
    private readonly ModernButton _export = new() { Text = "Xuất MP3 + SRT", AutoSize = true, Style = ModernButton.Kind.Primary };
    private readonly ModernButton _cancel = new() { Text = "Hủy xử lý", AutoSize = true, Enabled = false, Style = ModernButton.Kind.Destructive };
    private CancellationTokenSource? _cts;
    private readonly string _ffmpegPath;
    private readonly string _ffprobePath;
    private readonly Storage? _storage;

    public MergeAudioSrtForm(IReadOnlyList<MergeTrack>? initialTracks = null, string ffmpegPath = "ffmpeg", Storage? storage = null)
    {
        _ffmpegPath = ffmpegPath;
        _ffprobePath = Path.Combine(Path.GetDirectoryName(ffmpegPath) ?? "", "ffprobe.exe");
        _storage = storage;
        if (initialTracks is not null) _tracks.AddRange(initialTracks);
        Text = "Ghép MP3 + đồng bộ SRT | Lăng Kính Hư Không | v1.1";
        Size = new Size(1040, 600);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(860, 500);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Body;

        _grid.BackgroundColor = Theme.Inset;
        _grid.DefaultCellStyle.BackColor = Color.White;
        _grid.DefaultCellStyle.ForeColor = Theme.Text;
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(254, 240, 221);
        _grid.DefaultCellStyle.SelectionForeColor = Theme.Text;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Blue;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
        _grid.ColumnHeadersHeight = 38;
        _grid.RowTemplate.Height = 34;

        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Mp3", HeaderText = "File MP3 (thứ tự ghép)", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 48 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Srt", HeaderText = "File SRT đi kèm", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 43 });
        var speeds = new DataGridViewComboBoxColumn { Name = "Speed", HeaderText = "Tốc độ", Width = 100, FlatStyle = FlatStyle.Flat };
        speeds.Items.AddRange("0.5", "1", "1.25", "1.5", "2");
        _grid.Columns.Add(speeds);
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 2 || e.RowIndex >= _tracks.Count) return;
            var value = _grid.Rows[e.RowIndex].Cells[2].Value?.ToString() ?? "1";
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double speed))
                _tracks[e.RowIndex] = _tracks[e.RowIndex] with { Speed = speed };
        };

        var commands = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 90, Padding = new Padding(10), WrapContents = true, AutoScroll = true, BackColor = Color.Transparent };
        var add = MakeButton("+ Thêm MP3", ModernButton.Kind.Secondary, (_, _) => AddFiles());
        var setSrt = MakeButton("Chọn SRT", ModernButton.Kind.Secondary, (_, _) => SetSrt());
        var up = MakeButton("↑ Lên", ModernButton.Kind.Ghost, (_, _) => MoveSelected(-1));
        var down = MakeButton("↓ Xuống", ModernButton.Kind.Ghost, (_, _) => MoveSelected(1));
        var remove = MakeButton("Xóa", ModernButton.Kind.Destructive, (_, _) => RemoveSelected());
        var allSpeed = new ComboBox { Width = 66, DropDownStyle = ComboBoxStyle.DropDownList };
        allSpeed.Items.AddRange(new object[] { "0.5", "1", "1.25", "1.5", "2" });
        allSpeed.SelectedItem = "1";
        var applyAll = MakeButton("Tốc độ tất cả", ModernButton.Kind.Gold, (_, _) =>
        {
            if (allSpeed.SelectedItem is not string value) return;
            var speed = double.Parse(value, CultureInfo.InvariantCulture);
            for (int i = 0; i < _tracks.Count; i++) _tracks[i] = _tracks[i] with { Speed = speed };
            RefreshGrid();
        });
        commands.Controls.AddRange(new Control[] { add, setSrt, up, down, remove, allSpeed, applyAll, _export, _cancel });
        _export.Click += async (_, _) => await ExportAsync();
        _cancel.Click += (_, _) => _cts?.Cancel();

        var statusPanel = new Panel { Height = 42, Dock = DockStyle.Bottom, Padding = new Padding(10), BackColor = Color.Transparent };
        statusPanel.Controls.Add(_status);
        Controls.Add(_grid);
        Controls.Add(commands);
        Controls.Add(statusPanel);
        RefreshGrid();
        FormClosing += (_, e) =>
        {
            if (_cts is not null)
            {
                _cts.Cancel();
                e.Cancel = true;
            }
        };
    }

    private static ModernButton MakeButton(string label, ModernButton.Kind style, EventHandler handler)
    {
        var b = new ModernButton { Text = label, AutoSize = true, Margin = new Padding(3), Style = style, Width = 132, Height = 38 };
        b.Click += handler;
        return b;
    }

    private int Selected() => _grid.SelectedRows.Count == 0 ? -1 : _grid.SelectedRows[0].Index;
    private void AddFiles()
    {
        using var dlg = new OpenFileDialog { Filter = "MP3 audio (*.mp3)|*.mp3", Multiselect = true, Title = "Chọn MP3 theo thứ tự cần ghép" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        foreach (var mp3 in dlg.FileNames)
        {
            var srt = Path.ChangeExtension(mp3, ".srt");
            _tracks.Add(new MergeTrack(mp3, srt, 1));
        }
        RefreshGrid();
    }

    private void SetSrt()
    {
        int ix = Selected();
        if (ix < 0) return;
        using var dlg = new OpenFileDialog { Filter = "SubRip (*.srt)|*.srt", Title = "Chọn SRT của file MP3 này" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _tracks[ix] = _tracks[ix] with { SrtPath = dlg.FileName };
        RefreshGrid(ix);
    }

    private void MoveSelected(int delta)
    {
        int ix = Selected();
        int other = ix + delta;
        if (ix < 0 || other < 0 || other >= _tracks.Count) return;
        (_tracks[ix], _tracks[other]) = (_tracks[other], _tracks[ix]);
        RefreshGrid(other);
    }

    private void RemoveSelected()
    {
        int ix = Selected();
        if (ix < 0) return;
        _tracks.RemoveAt(ix);
        RefreshGrid();
    }

    private void RefreshGrid(int selected = -1)
    {
        _grid.Rows.Clear();
        foreach (var item in _tracks)
        {
            int ix = _grid.Rows.Add(item.Mp3Path,
                File.Exists(item.SrtPath) ? item.SrtPath : "[THIẾU SRT] " + item.SrtPath,
                item.Speed.ToString("0.##", CultureInfo.InvariantCulture));
            if (!File.Exists(item.SrtPath))
                _grid.Rows[ix].Cells[1].Style.ForeColor = Theme.Danger;
        }
        if (_tracks.Count > 0)
        {
            int pos = selected >= 0 ? Math.Min(selected, _tracks.Count - 1) : _tracks.Count - 1;
            _grid.Rows[pos].Selected = true;
            _grid.CurrentCell = _grid.Rows[pos].Cells[0];
        }
        _status.Text = $"{_tracks.Count} MP3 | mỗi MP3 cần file SRT tương ứng | chọn tốc độ riêng từng file";
    }

    private async Task ExportAsync()
    {
        if (_cts is not null) return;
        if (_tracks.Count == 0) { MessageBox.Show(this, "Hãy thêm MP3.", "Thiếu dữ liệu", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        var missing = _tracks.FirstOrDefault(x => !File.Exists(x.SrtPath));
        if (missing is not null)
        {
            MessageBox.Show(this, $"Thiếu phụ đề ghép cặp:\n{missing.SrtPath}\n\nHãy chọn SRT đúng.");
            return;
        }
        using var dlg = new SaveFileDialog { Filter = "MP3 (*.mp3)|*.mp3", FileName = "Truyen_Audio_Full.mp3", Title = "Xuất 1 MP3 + 1 SRT" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        _cts = new CancellationTokenSource();
        _export.Enabled = false;
        _cancel.Enabled = true;
        _grid.Enabled = false;
        try
        {
            var progress = new Progress<MergeProgress>(p => _status.Text = p.Message);
            await SrtAudioMerger.MergeAsync(_tracks.ToArray(), dlg.FileName,
                ffmpegPath: _ffmpegPath, ffprobePath: _ffprobePath,
                progress: progress, ct: _cts.Token);
            if (_storage is not null)
            {
                var commonSpeed = _tracks.Select(t => t.Speed).Distinct().Count() == 1 ? _tracks[0].Speed : 0;
                _storage.Add(new Models.AudioEntry
                {
                    FilePath = Path.GetFullPath(dlg.FileName),
                    Title = Storage.SafeName(Path.GetFileNameWithoutExtension(dlg.FileName)),
                    Type = "Ghép + SRT",
                    SourceCount = _tracks.Count,
                    Voice = "Nhiều đoạn",
                    Speed = commonSpeed
                });
            }
            MessageBox.Show(this, $"Đã tạo:\n{dlg.FileName}\n{Path.ChangeExtension(dlg.FileName, ".srt")}\n\n" +
                    (_tracks.All(t => t.Speed == 1) ? "1×: nối nguyên bản không nén lại." :
                    "Tốc độ khác 1×: MP3 được mã hóa lại đúng một lần ở chất lượng cao."),
                "Xuất hoàn tất", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException) { _status.Text = "Đã hủy."; }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Lỗi xuất", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _export.Enabled = true;
            _cancel.Enabled = false;
            _grid.Enabled = true;
        }
    }
}
