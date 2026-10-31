using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using LangKinhHuKhong.TTS.Core;
using LangKinhHuKhong.TTS.Models;

namespace LangKinhHuKhong.TTS.UI;

public sealed partial class MainForm
{
    private void UpdateCounts()
    {
        if (_editor is null || _charCount is null) return;
        var len = _editor.TextLength;
        _charCount.Text = $"{len:N0} ký tự";
        _charCount.ForeColor = Theme.Gold;
        _wordCount.Text = $"{len:N0} ký tự · đang cập nhật số từ...";
        _wordTimer.Stop();
        _wordTimer.Start();
        _textWarning.Text = "Không giới hạn ký tự theo ứng dụng · đọc theo từng đoạn · phụ thuộc dung lượng máy.";
        _textWarning.ForeColor = Theme.Muted;
        if (_renderButton is not null)
            _renderButton.Enabled = !_busy && _voiceCombo.SelectedItem is VoiceModel && len > 0;
    }

    private void ImportText()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Nhập nội dung văn bản để tạo TTS", Filter = "Văn bản (*.txt;*.md)|*.txt;*.md|TXT (*.txt)|*.txt|Markdown (*.md)|*.md",
            CheckFileExists = true, Multiselect = false
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) ImportTextFile(dialog.FileName);
    }

    private void ImportTextFile(string file)
    {
        try
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is not ".txt" and not ".md")
                throw new InvalidOperationException("Chỉ hỗ trợ import file .txt hoặc .md.");
            using var reader = new StreamReader(file, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
            var raw = reader.ReadToEnd();
            var text = TextProcessor.FromImportedFile(raw, ext == ".md");
            if (!string.IsNullOrWhiteSpace(_editor.Text))
            {
                var confirm = MessageBox.Show("Thay thế văn bản đang nhập bằng nội dung tệp?", "Import",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (confirm != DialogResult.Yes) return;
            }
            _editor.Text = text;
            ShowPage("editor");
            SetStatus($"Đã nhập {Path.GetFileName(file)} ({text.Length:N0} ký tự).", Theme.Green);
        }
        catch (Exception ex) { ShowError(ex, "Không thể nhập văn bản."); }
    }

    private void ReloadVoices()
    {
        try
        {
            _loadingVoices = true;
            _voices.Clear();
            _voices.AddRange(VoiceCatalog.Scan(_storage.Settings.VoicesFolder));
            _languageCombo.BeginUpdate();
            try
            {
                _languageCombo.Items.Clear();
                foreach (var lang in VoiceLanguage.Choices(_voices)) _languageCombo.Items.Add(lang);
                var last = VoiceLanguage.Normalize(_storage.Settings.LastLanguage);
                // Migrating v4.2: prefer Vietnamese when installed, otherwise the first installed language.
                var choice = _languageCombo.Items.Cast<VoiceLanguage.LanguageChoice>()
                    .FirstOrDefault(x => x.Code == last && x.Count > 0)
                    ?? _languageCombo.Items.Cast<VoiceLanguage.LanguageChoice>()
                        .FirstOrDefault(x => x.Code != VoiceLanguage.All && x.Count > 0)
                    ?? _languageCombo.Items.Cast<VoiceLanguage.LanguageChoice>().First();
                _languageCombo.SelectedItem = choice;
            }
            finally { _languageCombo.EndUpdate(); }
            var selected = _storage.Settings.LastCategory;
            if (!_groupCombo.Items.Contains(selected)) selected = "Tất cả";
            _groupCombo.SelectedItem = selected;
        }
        catch (Exception ex) { ShowError(ex, "Không đọc được danh sách giọng nói."); }
        finally { _loadingVoices = false; }

        RefilterVoices();
        if (_voices.Count == 0) SetStatus("Chưa thấy model ONNX/JSON. Mở Cài đặt → chọn thư mục Voices.", Theme.Gold);
        else SetStatus($"Đã nạp {_voices.Count} model · { _voices.Count(v => v.Language != VoiceLanguage.Unknown) } model có metadata ngôn ngữ.", Theme.Green);
        foreach (var item in _speedCombo.Items)
            if (item is SpeedOption speed && speed.Value == _storage.Settings.Speed)
            { _speedCombo.SelectedItem = item; break; }
        if (_speedCombo.SelectedIndex < 0) _speedCombo.SelectedItem = _speedCombo.Items.Cast<SpeedOption>().First(x => x.Value == 1);
    }

    private void RefilterVoices()
    {
        if (_loadingVoices || _voiceCombo is null || _groupCombo is null || _languageCombo is null) return;
        var previous = (_voiceCombo.SelectedItem as VoiceModel)?.ModelPath;
        var group = _groupCombo.SelectedItem?.ToString() ?? "Tất cả";
        var language = (_languageCombo.SelectedItem as VoiceLanguage.LanguageChoice)?.Code ?? VoiceLanguage.All;
        _voiceCombo.BeginUpdate();
        try
        {
            _voiceCombo.Items.Clear();
            foreach (var voice in _voices.Where(v => (group == "Tất cả" || v.Category == group) &&
                      VoiceLanguage.Matches(v.Language, language)))
                _voiceCombo.Items.Add(voice);
            if (_voiceCombo.Items.Count > 0)
            {
                var target = _voiceCombo.Items.Cast<VoiceModel>().FirstOrDefault(v =>
                        v.ModelPath.Equals(previous, StringComparison.OrdinalIgnoreCase))
                    ?? _voiceCombo.Items.Cast<VoiceModel>().FirstOrDefault(v =>
                        v.ModelPath.Equals(_storage.Settings.LastVoicePath, StringComparison.OrdinalIgnoreCase));
                _voiceCombo.SelectedItem = target ?? _voiceCombo.Items[0];
            }
        }
        finally { _voiceCombo.EndUpdate(); }
        UpdateSpeakers(); // Also handles an empty model list.
        _storage.Settings.LastCategory = group;
        _storage.Settings.LastLanguage = language;
        _storage.SaveSettings();
    }

    private void UpdateSpeakers()
    {
        if (_speakerCombo is null || _voiceCombo is null || _voiceLanguageHint is null) return;
        _speakerCombo.Items.Clear();
        var voice = _voiceCombo.SelectedItem as VoiceModel;
        if (voice is null)
        {
            _speakerCombo.Items.Add("Không có giọng phù hợp");
            _speakerCombo.SelectedIndex = 0;
            _speakerCombo.Enabled = false;
            var language = (_languageCombo.SelectedItem as VoiceLanguage.LanguageChoice)?.Code;
            _voiceLanguageHint.Text = $"Không có giọng phù hợp ({VoiceLanguage.DisplayName(language)}). Đổi nhóm hoặc thêm model vào Voices.";
            _voiceLanguageHint.ForeColor = Theme.Gold;
            if (_renderButton is not null) _renderButton.Enabled = false;
            return;
        }
        _storage.Settings.LastVoicePath = voice.ModelPath;
        _storage.SaveSettings();
        _voiceLanguageHint.Text = voice.Language == VoiceLanguage.Unknown
            ? "Model chưa khai báo ngôn ngữ: kiểm tra file JSON trước khi đọc."
            : $"Model: {VoiceLanguage.DisplayName(voice.Language)} · không tự dịch văn bản.";
        _voiceLanguageHint.ForeColor = voice.Language == VoiceLanguage.Unknown ? Theme.Gold : Theme.Muted;
        if (voice.NumSpeakers <= 1)
        {
            _speakerCombo.Items.Add("Giọng mặc định");
            _speakerCombo.Enabled = false;
        }
        else
        {
            foreach (var speaker in voice.Speakers) _speakerCombo.Items.Add(speaker);
            _speakerCombo.Enabled = true;
        }
        if (_speakerCombo.Items.Count > 0) _speakerCombo.SelectedIndex = 0;
        if (_renderButton is not null && !_busy)
            _renderButton.Enabled = _editor.TextLength > 0;
    }

    private void SetBusy(bool busy, string status)
    {
        _busy = busy;
        _renderButton.Enabled = !busy && _voiceCombo.SelectedItem is VoiceModel &&
            _editor.TextLength > 0;
        _mergeButton.Enabled = !busy;
        _cancelButton.Enabled = busy;
        _renderStatus.Text = status;
        _renderStatus.ForeColor = busy ? Theme.Gold : Theme.Muted;
        if (busy)
        {
            _footerStatus.Text = status;
            _footerStatus.ForeColor = Theme.Gold;
        }
    }

    private async Task StartRenderAsync()
    {
        if (_busy) { SetStatus("Đang chạy tác vụ khác. Có thể chờ hoặc bấm Dừng.", Theme.Gold); return; }
        if (_voiceCombo.SelectedItem is not VoiceModel voice)
        { MessageBox.Show("Chưa có model giọng đọc. Hãy sao chép Voices hoặc chọn đường dẫn trong Cài đặt."); return; }
        if (_speedCombo.SelectedItem is not SpeedOption selectedSpeed) return;
        var selectedLanguage = (_languageCombo.SelectedItem as VoiceLanguage.LanguageChoice)?.Code ?? VoiceLanguage.All;
        if (!VoiceLanguage.Matches(voice.Language, selectedLanguage))
        { MessageBox.Show("Model đang chọn không thuộc ngôn ngữ đọc. Hãy chọn một model tương thích."); return; }
        if (voice.Language == VoiceLanguage.Unknown && MessageBox.Show(
                "Không tìm thấy thông tin ngôn ngữ trong JSON của ONNX này.\n" +
                "Nếu model không hỗ trợ văn bản đang nhập, giọng đọc sẽ bị lỗi phát âm. Tiếp tục?",
                "Cảnh báo ngôn ngữ model", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        var text = _editor.Text;
        if (string.IsNullOrWhiteSpace(text))
        { MessageBox.Show("Nhập nội dung trước khi tạo MP3."); return; }
        var speaker = _speakerCombo.SelectedItem is VoiceSpeaker s ? s.Id : (int?)null;
        _jobCancellation = new CancellationTokenSource();
        var token = _jobCancellation.Token;
        SetBusy(true, "Đang chuẩn bị xử lý...");
        _progress.Percent = 0;
        var progress = new Progress<(int done, int total)>(p =>
        {
            if (_closing || IsDisposed) return;
            _progress.Percent = (int)(p.done * 100d / Math.Max(p.total, 1));
            _renderStatus.Text = $"Đã xử lý {p.done:N0} / {p.total:N0} bước";
            _footerStatus.Text = $"Đang render {p.done:N0}/{p.total:N0} – {voice.Name} ({VoiceLanguage.DisplayName(voice.Language)})";
        });
        try
        {
            var result = await _jobs.RenderAsync(text, voice, selectedSpeed.Value, speaker, progress, token, _srtOnRender.Checked, _engineCombo.SelectedIndex == 1);
            if (_closing) return;
            _progress.Percent = 100;
            RefreshLibrary();
            RefreshMergeList();
            SetStatus($"Đã tạo {(_srtOnRender.Checked ? "MP3 + SRT" : "MP3")}: {result.Title} · ID {result.Id[..8]}", Theme.Green);
            ShowPage("library");
        }
        catch (OperationCanceledException) { SetStatus("Đã hủy lượt render.", Theme.Gold); }
        catch (Exception ex) { if (!_closing) ShowError(ex, "Render MP3 thất bại."); SetStatus(ex.Message, Theme.Danger); }
        finally
        {
            _jobCancellation.Dispose(); _jobCancellation = null;
            if (!_closing) SetBusy(false, "Sẵn sàng xử lý lượt tiếp theo.");
        }
    }

    private void RefreshLibrary()
    {
        if (_library is null || _searchLibrary is null) return;
        var ids = _library.SelectedItems.Cast<ListViewItem>()
            .Select(x => (x.Tag as AudioEntry)?.Id).Where(x => x is not null).ToHashSet();
        var search = _searchLibrary.Text.Trim();
        _library.BeginUpdate();
        try
        {
            _library.Items.Clear();
            foreach (var entry in _storage.GetHistory())
            {
                if (!string.IsNullOrWhiteSpace(search) &&
                    !(entry.Title.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                      entry.Voice.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                      entry.Id.Contains(search, StringComparison.OrdinalIgnoreCase))) continue;
                var item = new ListViewItem(entry.Title) { Tag = entry };
                item.SubItems.Add(entry.Type);
                item.SubItems.Add(entry.Voice);
                item.SubItems.Add($"{entry.Speed:0.##}×");
                item.SubItems.Add(entry.CreatedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"));
                item.SubItems.Add(FormatSize(entry.Bytes));
                item.SubItems.Add(entry.Id[..12]);
                _library.Items.Add(item);
                if (ids.Contains(entry.Id)) item.Selected = true;
            }
        }
        finally { _library.EndUpdate(); }
        _footerCount.Text = $"{_storage.Count:N0} MP3 đã lưu";
    }

    private static string FormatSize(long bytes)
        => bytes < 1024 ? $"{bytes} B" : bytes < 1048576 ? $"{bytes / 1024d:N1} KB" : $"{bytes / 1048576d:N1} MB";

    private List<AudioEntry> SelectedLibrary() => _library.SelectedItems.Cast<ListViewItem>()
        .Select(x => x.Tag).OfType<AudioEntry>().ToList();

    private void PlaySelected()
    {
        var selected = SelectedLibrary();
        if (selected.Count == 0) { SetStatus("Chọn một MP3 trong danh sách để nghe.", Theme.Gold); return; }
        try
        {
            _player.Play(selected[0].FilePath);
            _playerTitle.Text = selected[0].Title;
            _playerPlayButton.Text = "Ⅱ  Tạm dừng";
            SetStatus($"Đang phát: {selected[0].Title}", Theme.Green);
        }
        catch (Exception ex) { ShowError(ex, "Không phát được âm thanh."); }
    }

    private void TogglePlayer()
    {
        try
        {
            if (_player.CurrentFile is null) { PlaySelected(); return; }
            _player.TogglePause();
            _playerPlayButton.Text = _player.IsPlaying ? "Ⅱ  Tạm dừng" : "▶  Tiếp tục";
        }
        catch (Exception ex) { ShowError(ex, "Không điều khiển được MP3."); }
    }

    private void StopPlayback()
    {
        _player.Close();
        _playerTitle.Text = "Chưa phát MP3";
        _playerTime.Text = "00:00 / 00:00";
        _playerSeek.Value = 0;
        _playerPlayButton.Text = "▶  Nghe";
    }

    private void UpdatePlaybackPosition()
    {
        if (_closing || _player.CurrentFile is null || _isSeeking) return;
        try
        {
            var ms = _player.PositionMs;
            var duration = _player.DurationMs;
            _playerTime.Text = $"{TimeText(ms)} / {TimeText(duration)}";
            if (duration > 0)
                _playerSeek.Value = Math.Clamp((int)(1000d * ms / duration), 0, 1000);
            if (_player.IsPlaying && duration > 0 && ms >= duration - 70)
            {
                _player.Close();
                _playerPlayButton.Text = "▶  Phát lại";
            }
        }
        catch (Exception) { StopPlayback(); }
    }

    private static string TimeText(int milliseconds)
    {
        var time = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return time.TotalHours >= 1 ? time.ToString(@"hh\:mm\:ss") : time.ToString(@"mm\:ss");
    }

    private void ExportSelected()
    {
        var chosen = SelectedLibrary();
        if (chosen.Count == 0) { SetStatus("Chọn MP3 cần tải bản sao.", Theme.Gold); return; }
        try
        {
            if (chosen.Count == 1)
            {
                var file = chosen[0];
                using var dialog = new SaveFileDialog { Title = "Lưu bản sao MP3", Filter = "MP3 Audio|*.mp3",
                    FileName = Storage.SafeName(file.Title) + ".mp3", OverwritePrompt = true };
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (Path.GetFullPath(dialog.FileName).Equals(Path.GetFullPath(file.FilePath), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Đang chọn chính file gốc. Hãy chọn nơi lưu khác.");
                File.Copy(file.FilePath, dialog.FileName, overwrite: true);
                CopyCompanionSrt(file.FilePath, dialog.FileName, overwrite: true);
            }
            else
            {
                using var dialog = new FolderBrowserDialog { Description = "Chọn thư mục xuất các MP3", UseDescriptionForTitle = true };
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                foreach (var file in chosen)
                {
                    var basename = Storage.SafeName(file.Title);
                    var target = Path.Combine(dialog.SelectedPath, basename + ".mp3");
                    if (File.Exists(target)) target = Path.Combine(dialog.SelectedPath, basename + "-" + file.Id[..8] + ".mp3");
                    if (Path.GetFullPath(target).Equals(Path.GetFullPath(file.FilePath), StringComparison.OrdinalIgnoreCase)) continue;
                    File.Copy(file.FilePath, target, overwrite: false);
                    CopyCompanionSrt(file.FilePath, target);
                }
            }
            SetStatus($"Đã xuất {chosen.Count} file MP3.", Theme.Green);
        }
        catch (Exception ex) { ShowError(ex, "Không thể lưu bản sao MP3."); }
    }

    private static void CopyCompanionSrt(string sourceMp3, string targetMp3, bool overwrite = false)
    {
        var source = Path.ChangeExtension(sourceMp3, ".srt");
        if (!File.Exists(source)) return;
        var target = Path.ChangeExtension(targetMp3, ".srt");
        if (Path.GetFullPath(source).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) return;
        File.Copy(source, target, overwrite);
    }

    private void RenameSelected()
    {
        var entries = SelectedLibrary();
        if (entries.Count != 1) { SetStatus("Chọn đúng một MP3 để đổi tên.", Theme.Gold); return; }
        var name = AskName("Đổi tên hiển thị", entries[0].Title);
        if (name is null) return;
        try
        {
            _storage.Rename(entries[0].Id, name);
            RefreshLibrary();
            RefreshMergeList();
            SetStatus("Đã đổi tên hiển thị và tên khi tải bản sao.", Theme.Green);
        }
        catch (Exception ex) { ShowError(ex, "Không đổi tên được MP3."); }
    }

    private void DeleteSelected()
    {
        if (_busy) { SetStatus("Đang xử lý audio. Hãy dừng hoặc chờ xong rồi mới xóa.", Theme.Gold); return; }
        var entries = SelectedLibrary();
        if (entries.Count == 0) { SetStatus("Chọn MP3 cần xóa.", Theme.Gold); return; }
        if (MessageBox.Show($"XÓA VĨNH VIỄN {entries.Count} MP3 khỏi ổ đĩa và thư viện?\n\nKhông thể hoàn tác.",
            "Xác nhận xóa", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            if (entries.Any(x => x.FilePath.Equals(_player.CurrentFile, StringComparison.OrdinalIgnoreCase)))
                StopPlayback();
            _storage.Delete(entries.Select(x => x.Id));
            RefreshLibrary(); RefreshMergeList();
            SetStatus($"Đã xóa {entries.Count} MP3.", Theme.Green);
        }
        catch (Exception ex) { RefreshLibrary(); RefreshMergeList(); ShowError(ex, "Không xóa được MP3."); }
    }

    private void RefreshMergeList()
    {
        if (_mergeList is null) return;
        var previous = _mergeList.Items.Cast<ListViewItem>().Select(x =>
            ((x.Tag as AudioEntry)?.Id, x.Checked)).ToList();
        var savedChecks = previous.Where(x => x.Checked && x.Id is not null).Select(x => x.Id).ToHashSet();
        var all = _storage.GetHistory();
        var sorted = previous.Select(x => all.FirstOrDefault(a => a.Id == x.Id)).Where(x => x is not null)
            .Cast<AudioEntry>().ToList();
        sorted.AddRange(all.Where(x => sorted.All(s => s.Id != x.Id)));
        _mergeList.BeginUpdate();
        _suppressMergeRefresh = true;
        try
        {
            _mergeList.Items.Clear();
            foreach (var file in sorted)
            {
                var item = new ListViewItem("") { Tag = file, Checked = savedChecks.Contains(file.Id) };
                item.SubItems.Add(file.Title);
                item.SubItems.Add(file.Type);
                var companion = Path.ChangeExtension(file.FilePath, ".srt");
                item.SubItems.Add(File.Exists(companion) ? "✓ Có SRT" : "✕ Thiếu");
                item.SubItems.Add(file.CreatedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"));
                item.SubItems.Add(FormatSize(file.Bytes));
                _mergeList.Items.Add(item);
            }
            ReindexMergeList();
        }
        finally { _suppressMergeRefresh = false; _mergeList.EndUpdate(); }
        RefreshMergeHint();
    }

    private void ReindexMergeList()
    {
        var n = 0;
        foreach (ListViewItem item in _mergeList.Items)
            item.Text = item.Checked ? $"{++n:00}  ◉" : "—  ○";
    }

    private void RefreshMergeHint()
    {
        if (_mergeList is null || _mergeHint is null) return;
        ReindexMergeList();
        var count = _mergeList.CheckedItems.Count;
        var missing = _mergeList.CheckedItems.Cast<ListViewItem>().Count(i =>
            i.Tag is AudioEntry entry && !File.Exists(Path.ChangeExtension(entry.FilePath, ".srt")));
        _mergeHint.Text = missing > 0
            ? $"{count:N0} đã chọn · thiếu {missing:N0} SRT"
            : $"{count:N0} / {_mergeList.Items.Count:N0} đoạn · có SRT";
        _mergeHint.ForeColor = missing > 0 ? Theme.Danger : count >= 2 ? Theme.Green : Theme.Gold;
    }

    private void CheckAllMerge(bool check)
    {
        _mergeList.BeginUpdate();
        _suppressMergeRefresh = true;
        try { foreach (ListViewItem item in _mergeList.Items) item.Checked = check; }
        finally { _suppressMergeRefresh = false; _mergeList.EndUpdate(); RefreshMergeHint(); }
    }

    private void MoveMergeSelection(int direction)
    {
        if (_mergeList.SelectedItems.Count != 1)
        { SetStatus("Chọn đúng một dòng để thay đổi thứ tự ghép.", Theme.Gold); return; }
        var item = _mergeList.SelectedItems[0];
        var index = item.Index;
        var target = index + direction;
        if (target < 0 || target >= _mergeList.Items.Count) return;
        _mergeList.BeginUpdate();
        try
        {
            _mergeList.Items.Remove(item);
            _mergeList.Items.Insert(target, item);
            item.Selected = true;
            item.Focused = true;
            item.EnsureVisible();
            ReindexMergeList();
        }
        finally { _mergeList.EndUpdate(); }
    }

    private void AddSelectedToMerge()
    {
        var selected = SelectedLibrary();
        if (selected.Count == 0) { SetStatus("Chọn các MP3 trong Thư viện trước.", Theme.Gold); return; }
        RefreshMergeList();
        var ids = selected.Select(x => x.Id).ToHashSet();
        foreach (ListViewItem item in _mergeList.Items)
            if (item.Tag is AudioEntry file && ids.Contains(file.Id)) item.Checked = true;
        ShowPage("merge");
        SetStatus($"Đã chọn {selected.Count} MP3 để ghép. Sắp xếp lên/xuống trước khi bắt đầu.", Theme.Green);
    }

    private void OpenAdvancedMergeDialog()
    {
        if (_busy) return;
        var selected = _mergeList.Items.Cast<ListViewItem>().Where(x => x.Checked)
            .Select(x => x.Tag).OfType<AudioEntry>().ToArray();
        var speed = (_mergeSpeedCombo.SelectedItem as SpeedOption)?.Value ?? 1.0;
        var tracks = selected.Select(x => new MergeTrack(x.FilePath,
            Path.ChangeExtension(x.FilePath, ".srt"), speed)).ToArray();
        using var dialog = new MergeAudioSrtForm(tracks, _audio.FfmpegExe, _storage);
        dialog.ShowDialog(this);
        RefreshLibrary();
        RefreshMergeList();
    }

    private async Task StartMergeAsync()
    {
        if (_busy) { SetStatus("Đang có tác vụ xử lý khác.", Theme.Gold); return; }
        var selected = _mergeList.Items.Cast<ListViewItem>().Where(x => x.Checked)
            .Select(x => x.Tag).OfType<AudioEntry>().ToArray();
        if (selected.Length < 2)
        { MessageBox.Show("Tích chọn ít nhất 2 MP3 để ghép.", "Chưa đủ file", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (string.IsNullOrWhiteSpace(_mergeTitle.Text))
        { MessageBox.Show("Hãy đặt tên cho file MP3 sau ghép."); return; }
        _jobCancellation = new CancellationTokenSource();
        var token = _jobCancellation.Token;
        SetBusy(true, $"Đang ghép {selected.Length} MP3...");
        _progress.Percent = 0;
        _mergeProgress.Percent = 0;
        var progress = new Progress<(int done, int total)>(p =>
        {
            if (_closing || IsDisposed) return;
            _progress.Percent = (int)(p.done * 100d / Math.Max(1, p.total));
            _mergeProgress.Percent = _progress.Percent;
            _footerStatus.Text = $"Đang ghép audio: lô {p.done}/{p.total}";
        });
        try
        {
            var speed = (_mergeSpeedCombo.SelectedItem as SpeedOption)?.Value ?? 1.0;
            var tracks = selected.Select(entry =>
                new MergeTrack(entry.FilePath, Path.ChangeExtension(entry.FilePath, ".srt"), speed)).ToArray();
            var missing = tracks.FirstOrDefault(x => !File.Exists(x.SrtPath));
            if (missing is not null)
                throw new FileNotFoundException(
                    "MP3 chưa có SRT cùng tên: " + missing.Mp3Path +
                    "\nHãy tạo lại MP3 có tích 'Xuất kèm SRT', hoặc dùng nút nâng cao để chọn SRT thủ công.", missing.SrtPath);
            var result = await _jobs.MergeWithSubtitlesAsync(tracks, _mergeTitle.Text, progress, token);
            if (_closing) return;
            _progress.Percent = 100;
            _mergeProgress.Percent = 100;
            RefreshLibrary(); RefreshMergeList();
            ShowPage("library");
            SetStatus($"Đã ghép MP3 + SRT ({selected.Length} đoạn) · tốc độ {((_mergeSpeedCombo.SelectedItem as SpeedOption)?.Value ?? 1):0.##}×: {result.Title}.", Theme.Green);
        }
        catch (OperationCanceledException) { SetStatus("Đã hủy ghép MP3.", Theme.Gold); }
        catch (Exception ex) { if (!_closing) ShowError(ex, "Không ghép được MP3."); SetStatus(ex.Message, Theme.Danger); }
        finally
        {
            _jobCancellation.Dispose(); _jobCancellation = null;
            if (!_closing) SetBusy(false, "Đã hoàn tất tác vụ. Sẵn sàng tiếp tục.");
        }
    }

    private void UpdateFolderSetting(string kind, string folder)
    {
        folder = folder.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(folder)) return;
        if (!Directory.Exists(folder))
        { SetStatus("Không tìm thấy thư mục: " + folder, Theme.Danger); return; }
        try
        {
            var settings = _storage.Settings;
            if (kind == "output") settings.OutputFolder = folder;
            else if (kind == "voices") settings.VoicesFolder = folder;
            else if (kind == "runtime") settings.RuntimeFolder = folder;
            _storage.SaveSettings();
            if (kind == "voices") ReloadVoices();
            RefreshDependencies();
            SetStatus("Đã lưu đường dẫn " + kind + ".", Theme.Green);
        }
        catch (Exception ex) { ShowError(ex, "Không lưu được cài đặt."); }
    }

    private void RefreshDependencies()
    {
        if (_dependencies is null) return;
        var piper = File.Exists(_audio.PiperExe);
        var ffmpeg = File.Exists(_audio.FfmpegExe);
        _dependencies.Text = $"{(piper ? "✓" : "✕")} Piper    ·    {(ffmpeg ? "✓" : "✕")} FFmpeg    ·    {_voices.Count} model giọng";
        _dependencies.ForeColor = piper && ffmpeg && _voices.Count > 0 ? Theme.Green : Theme.Gold;
    }

    private static void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { ShowError(ex, "Không mở được thư mục."); }
    }

    private string? AskName(string title, string defaultText)
    {
        using var popup = new Form
        {
            Text = title, Size = new Size(490, 215), MinimumSize = new Size(490, 215),
            MaximumSize = new Size(740, 215), FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent, BackColor = Theme.Surface,
            ForeColor = Theme.Text, ShowInTaskbar = false, MaximizeBox = false, MinimizeBox = false
        };
        popup.Controls.Add(new Label { Text = "Tên hiển thị (đồng thời dùng khi xuất bản sao)",
            Left = 20, Top = 20, Width = 430, Height = 25, ForeColor = Theme.Text, Font = Theme.Body });
        var input = Theme.Textbox(defaultText);
        input.SetBounds(20, 51, 432, 32);
        input.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        popup.Controls.Add(input);
        var okay = Btn("Lưu thay đổi", ModernButton.Kind.Primary, 140);
        okay.SetBounds(203, 105, 140, 39);
        okay.DialogResult = DialogResult.OK;
        var cancel = Btn("Hủy", ModernButton.Kind.Ghost, 106);
        cancel.SetBounds(346, 105, 106, 39);
        cancel.DialogResult = DialogResult.Cancel;
        popup.Controls.AddRange([okay, cancel]);
        popup.AcceptButton = okay;
        popup.CancelButton = cancel;
        return popup.ShowDialog(this) == DialogResult.OK ? Storage.SafeName(input.Text) : null;
    }
}
