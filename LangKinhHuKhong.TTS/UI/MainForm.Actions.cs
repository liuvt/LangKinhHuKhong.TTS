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
        _charCount.ForeColor = Theme.Blue;
        _wordCount.Text = $"{len:N0} ký tự · đang tính số từ...";
        _wordTimer.Stop();
        _wordTimer.Start();
        _textWarning.Text = "Không giới hạn ký tự · Tự động băm cache theo từng batch 80 câu · Chọn từ để nghe thử nhanh.";
        _textWarning.ForeColor = Theme.Muted;
        bool canRun = !_busy && _voiceCombo?.SelectedItem is VoiceModel && len > 0;
        if (_renderButton is not null) _renderButton.Enabled = canRun;
        if (_reviewButton is not null) _reviewButton.Enabled = canRun;
        if (_reviewToolbarButton is not null) _reviewToolbarButton.Enabled = canRun;
    }

    private void ImportText()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Nhập nội dung văn bản để tạo TTS",
            Filter = "Văn bản (*.txt;*.md)|*.txt;*.md|TXT (*.txt)|*.txt|Markdown (*.md)|*.md",
            CheckFileExists = true,
            Multiselect = false
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
            var choices = VoiceLanguage.Choices(_voices);

            _languageCombo.BeginUpdate();
            try
            {
                _languageCombo.Items.Clear();
                foreach (var lang in choices) _languageCombo.Items.Add(lang);
                var last = VoiceLanguage.Normalize(_storage.Settings.LastLanguage);
                var choice = _languageCombo.Items.Cast<VoiceLanguage.LanguageChoice>()
                    .FirstOrDefault(x => x.Code == last && (x.Count > 0 || x.Code == VoiceLanguage.All))
                    ?? _languageCombo.Items.Cast<VoiceLanguage.LanguageChoice>().First();
                _languageCombo.SelectedItem = choice;
            }
            finally { _languageCombo.EndUpdate(); }

            if (_voiceLabLanguage is not null)
            {
                _voiceLabLanguage.BeginUpdate();
                try
                {
                    _voiceLabLanguage.Items.Clear();
                    foreach (var lang in choices) _voiceLabLanguage.Items.Add(lang);
                    _voiceLabLanguage.SelectedIndex = 0;
                }
                finally { _voiceLabLanguage.EndUpdate(); }
            }

            var selected = _storage.Settings.LastCategory;
            if (!_groupCombo.Items.Contains(selected)) selected = "Tất cả";
            _groupCombo.SelectedItem = selected;
        }
        catch (Exception ex) { ShowError(ex, "Không đọc được danh sách giọng nói."); }
        finally { _loadingVoices = false; }

        RefilterVoices();
        RefreshVoiceLabCatalog();
        RefreshDependencies();

        if (_voices.Count == 0)
            SetStatus("Chưa có model .onnx/.onnx.json trong thư mục Voices. Hãy chép model vào thư mục Voices.", Theme.Gold);
        else
            SetStatus($"Đã nạp {_voices.Count} model giọng · Sẵn sàng trong Studio và Voice Lab.", Theme.Green);

        foreach (var item in _speedCombo.Items)
            if (item is SpeedOption speed && speed.Value == _storage.Settings.Speed)
            { _speedCombo.SelectedItem = item; break; }
        if (_speedCombo.SelectedIndex < 0)
            _speedCombo.SelectedItem = _speedCombo.Items.Cast<SpeedOption>().First(x => x.Value == 1);

        if (_reviewLimitCombo is not null)
        {
            foreach (var item in _reviewLimitCombo.Items)
                if (item is WordLimitOption w && w.Words == _storage.Settings.ReviewWordLimit)
                { _reviewLimitCombo.SelectedItem = item; break; }
            if (_reviewLimitCombo.SelectedIndex < 0)
                _reviewLimitCombo.SelectedIndex = 1; // 50 từ
        }
    }

    private void RefilterVoices()
    {
        if (_loadingVoices || _voiceCombo is null || _groupCombo is null || _languageCombo is null) return;
        var previous = (_voiceCombo.SelectedItem as VoiceModel)?.ModelPath;
        var group = _groupCombo.SelectedItem?.ToString() ?? "Tất cả";
        var language = (_languageCombo.SelectedItem as VoiceLanguage.LanguageChoice)?.Code ?? VoiceLanguage.All;
        var query = _voiceSearchBox?.Text.Trim() ?? "";

        _voiceCombo.BeginUpdate();
        try
        {
            _voiceCombo.Items.Clear();
            foreach (var voice in _voices.Where(v =>
                         (group == "Tất cả" || v.Category == group) &&
                         VoiceLanguage.Matches(v.Language, language) &&
                         (string.IsNullOrWhiteSpace(query) ||
                          v.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                          v.FileName.Contains(query, StringComparison.CurrentCultureIgnoreCase))))
            {
                _voiceCombo.Items.Add(voice);
            }
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

        UpdateSpeakers();
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
            _voiceLanguageHint.Text = $"Chưa có giọng phù hợp ({VoiceLanguage.DisplayName(language)}). Hãy thêm model vào thư mục Voices.";
            _voiceLanguageHint.ForeColor = Theme.Gold;
            if (_renderButton is not null) _renderButton.Enabled = false;
            if (_reviewButton is not null) _reviewButton.Enabled = false;
            if (_reviewToolbarButton is not null) _reviewToolbarButton.Enabled = false;
            if (_previewVoiceButton is not null) _previewVoiceButton.Enabled = false;
            if (_renameVoiceQuickButton is not null) _renameVoiceQuickButton.Enabled = false;
            return;
        }

        _storage.Settings.LastVoicePath = voice.ModelPath;
        _storage.SaveSettings();

        bool hasSample = VoiceCatalog.HasExampleAudio(_storage.Settings.VoicesFolder, voice);
        var sampleBadge = hasSample ? "✓ Đã có file mẫu" : "○ Chưa tạo file mẫu";
        _voiceLanguageHint.Text = $"{VoiceLanguage.DisplayName(voice.Language)}  ·  Nhóm: {voice.Category}  ·  Chất lượng: {voice.Quality}  ·  {sampleBadge}";
        _voiceLanguageHint.ForeColor = voice.Language == VoiceLanguage.Unknown ? Theme.Gold : Theme.Muted;

        if (voice.NumSpeakers <= 1)
        {
            _speakerCombo.Items.Add("Giọng mặc định (Single Speaker)");
            _speakerCombo.Enabled = false;
        }
        else
        {
            foreach (var speaker in voice.Speakers) _speakerCombo.Items.Add(speaker);
            _speakerCombo.Enabled = true;
        }
        if (_speakerCombo.Items.Count > 0) _speakerCombo.SelectedIndex = 0;

        if (!_busy)
        {
            bool hasText = _editor.TextLength > 0;
            if (_renderButton is not null) _renderButton.Enabled = hasText;
            if (_reviewButton is not null) _reviewButton.Enabled = hasText;
            if (_reviewToolbarButton is not null) _reviewToolbarButton.Enabled = hasText;
            if (_previewVoiceButton is not null) _previewVoiceButton.Enabled = true;
            if (_renameVoiceQuickButton is not null) _renameVoiceQuickButton.Enabled = true;
        }
    }

    private void RefreshVoiceLabCatalog()
    {
        if (_voiceCatalogList is null) return;
        var selectedPath = SelectedVoiceInCatalog()?.ModelPath ?? (_voiceCombo?.SelectedItem as VoiceModel)?.ModelPath;
        var query = _voiceLabSearch?.Text.Trim() ?? "";
        var category = _voiceLabCategory?.SelectedItem?.ToString() ?? "Tất cả nhóm";
        var language = (_voiceLabLanguage?.SelectedItem as VoiceLanguage.LanguageChoice)?.Code ?? VoiceLanguage.All;

        int sampleCount = _voices.Count(v => VoiceCatalog.HasExampleAudio(_storage.Settings.VoicesFolder, v));
        if (_voiceLabSummary is not null)
            _voiceLabSummary.Text = $"{_voices.Count} model giọng · {sampleCount}/{_voices.Count} đã có mẫu trong Voices/example";

        _voiceCatalogList.BeginUpdate();
        try
        {
            _voiceCatalogList.Items.Clear();
            foreach (var voice in _voices)
            {
                if (category is "Nữ" or "Nam" or "Khác" && voice.Category != category) continue;
                if (!VoiceLanguage.Matches(voice.Language, language)) continue;
                if (!string.IsNullOrWhiteSpace(query) &&
                    !(voice.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                      voice.FileName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                      voice.Language.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                      voice.Quality.Contains(query, StringComparison.OrdinalIgnoreCase)))
                    continue;

                bool hasExample = VoiceCatalog.HasExampleAudio(_storage.Settings.VoicesFolder, voice);
                var item = new ListViewItem(voice.Name) { Tag = voice };
                item.SubItems.Add(voice.Category);
                item.SubItems.Add(VoiceLanguage.DisplayName(voice.Language));
                item.SubItems.Add(voice.Quality);
                item.SubItems.Add(voice.NumSpeakers.ToString(CultureInfo.InvariantCulture));
                item.SubItems.Add(hasExample ? "✓ Sẵn sàng (▶ Nghe)" : "○ Chưa tạo mẫu");
                item.SubItems.Add(voice.FileName);
                _voiceCatalogList.Items.Add(item);

                if (selectedPath is not null && voice.ModelPath.Equals(selectedPath, StringComparison.OrdinalIgnoreCase))
                {
                    item.Selected = true;
                    item.Focused = true;
                }
            }
            if (_voiceCatalogList.SelectedItems.Count == 0 && _voiceCatalogList.Items.Count > 0)
                _voiceCatalogList.Items[0].Selected = true;
        }
        finally { _voiceCatalogList.EndUpdate(); }
    }

    private VoiceModel? SelectedVoiceInCatalog()
        => _voiceCatalogList?.SelectedItems.Count > 0
            ? _voiceCatalogList.SelectedItems[0].Tag as VoiceModel
            : null;

    private void UseSelectedVoiceInEditor()
    {
        var voice = SelectedVoiceInCatalog();
        if (voice is null)
        {
            SetStatus("Hãy chọn một giọng trong bảng Danh mục Giọng.", Theme.Gold);
            return;
        }
        _storage.Settings.LastVoicePath = voice.ModelPath;
        _storage.Settings.LastCategory = "Tất cả";
        _storage.Settings.LastLanguage = VoiceLanguage.All;
        _storage.SaveSettings();
        ReloadVoices();
        foreach (var item in _voiceCombo.Items.Cast<VoiceModel>())
        {
            if (item.ModelPath.Equals(voice.ModelPath, StringComparison.OrdinalIgnoreCase))
            {
                _voiceCombo.SelectedItem = item;
                break;
            }
        }
        ShowPage("editor");
        SetStatus($"Đã chọn giọng '{voice.Name}' cho Studio.", Theme.Green);
    }

    private async Task PreviewSelectedEditorVoiceAsync()
    {
        if (_voiceCombo.SelectedItem is not VoiceModel voice)
        {
            SetStatus("Chưa có giọng đọc nào được chọn.", Theme.Gold);
            return;
        }
        var speaker = _speakerCombo.SelectedItem is VoiceSpeaker s ? s.Id : (int?)null;
        await PlayOrGenerateVoiceExampleCoreAsync(voice, speaker, forceRegenerate: false);
    }

    private async Task PlayOrGenerateSelectedVoiceLabExampleAsync(bool forceRegenerate)
    {
        var voice = SelectedVoiceInCatalog();
        if (voice is null)
        {
            SetStatus("Chọn một giọng trong danh sách để nghe hoặc tạo mẫu.", Theme.Gold);
            return;
        }
        int? speaker = voice.NumSpeakers > 1 && voice.Speakers.Count > 0 ? voice.Speakers[0].Id : null;
        await PlayOrGenerateVoiceExampleCoreAsync(voice, speaker, forceRegenerate);
    }

    private async Task PlayOrGenerateVoiceExampleCoreAsync(VoiceModel voice, int? speakerId, bool forceRegenerate)
    {
        var existingPath = VoiceCatalog.GetExampleFilePath(_storage.Settings.VoicesFolder, voice, speakerId);
        if (!forceRegenerate && File.Exists(existingPath) && new FileInfo(existingPath).Length > 512)
        {
            PlayAudioFile(existingPath, $"[Mẫu giọng] {voice.Name} ({voice.Category})");
            return;
        }

        if (_busy)
        {
            SetStatus("Đang có tác vụ xử lý khác. Hãy chờ hoặc bấm Dừng.", Theme.Gold);
            return;
        }

        _jobCancellation = new CancellationTokenSource();
        var token = _jobCancellation.Token;
        SetBusy(true, $"Đang tạo mẫu thử cho giọng '{voice.Name}'...");
        try
        {
            var sampleText = _sampleTextBox is not null && !string.IsNullOrWhiteSpace(_sampleTextBox.Text)
                ? _sampleTextBox.Text.Trim()
                : _storage.Settings.SampleText;
            var speed = (_speedCombo.SelectedItem as SpeedOption)?.Value ?? 1.0;
            bool useCuda = _engineCombo.SelectedIndex == 1;
            if (File.Exists(existingPath) && string.Equals(_player.CurrentFile, existingPath, StringComparison.OrdinalIgnoreCase))
                StopPlayback();

            var mp3Path = await _jobs.EnsureVoiceExampleAsync(
                voice, speakerId, sampleText, speed, useCuda, forceOverwrite: true, token);
            if (_closing) return;

            UpdateSpeakers();
            RefreshVoiceLabCatalog();
            PlayAudioFile(mp3Path, $"[Mẫu giọng] {voice.Name} ({voice.Category})");
            SetStatus($"Đã tạo và phát mẫu giọng: {voice.Name} (lưu tại Voices/example).", Theme.Green);
        }
        catch (OperationCanceledException) { SetStatus("Đã hủy tạo mẫu giọng.", Theme.Gold); }
        catch (Exception ex) { if (!_closing) ShowError(ex, "Không tạo được mẫu giọng."); SetStatus(ex.Message, Theme.Danger); }
        finally
        {
            _jobCancellation?.Dispose();
            _jobCancellation = null;
            if (!_closing) SetBusy(false, "Sẵn sàng.");
        }
    }

    private async Task RenderAllVoiceExamplesAsync(bool forceOverwrite)
    {
        if (_busy)
        {
            SetStatus("Đang chạy tác vụ khác. Hãy chờ hoặc bấm Dừng.", Theme.Gold);
            return;
        }
        if (_voices.Count == 0)
        {
            MessageBox.Show("Chưa có model giọng nào trong thư mục Voices.", "Voice Lab", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var sampleText = _sampleTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(sampleText))
        {
            MessageBox.Show("Hãy nhập đoạn văn mẫu chung trước khi tạo mẫu cho các giọng.", "Voice Lab", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _jobCancellation = new CancellationTokenSource();
        var token = _jobCancellation.Token;
        StopPlayback();
        SetBusy(true, $"Đang chuẩn bị tạo mẫu cho {_voices.Count} giọng...");
        _voiceLabProgress.Percent = 0;

        var progress = new Progress<(int done, int total, string voiceName)>(p =>
        {
            if (_closing || IsDisposed) return;
            _voiceLabProgress.Percent = (int)(p.done * 100d / Math.Max(1, p.total));
            _footerStatus.Text = $"Đang tạo mẫu ({p.done}/{p.total}): {p.voiceName}";
        });

        try
        {
            var speed = (_speedCombo.SelectedItem as SpeedOption)?.Value ?? 1.0;
            bool useCuda = _engineCombo.SelectedIndex == 1;
            int totalRendered = await _jobs.RenderAllVoiceExamplesAsync(
                _voices.ToList(), sampleText, speed, useCuda, forceOverwrite, progress, token);
            if (_closing) return;

            _voiceLabProgress.Percent = 100;
            UpdateSpeakers();
            RefreshVoiceLabCatalog();
            SetStatus($"Hoàn tất tạo mẫu cho {totalRendered}/{_voices.Count} giọng trong thư mục Voices/example.", Theme.Green);
        }
        catch (OperationCanceledException)
        {
            RefreshVoiceLabCatalog();
            SetStatus("Đã dừng tạo mẫu giọng hàng loạt.", Theme.Gold);
        }
        catch (Exception ex)
        {
            RefreshVoiceLabCatalog();
            if (!_closing) ShowError(ex, "Không hoàn tất tạo mẫu cho tất cả giọng.");
            SetStatus(ex.Message, Theme.Danger);
        }
        finally
        {
            _jobCancellation?.Dispose();
            _jobCancellation = null;
            if (!_closing) SetBusy(false, "Sẵn sàng.");
        }
    }

    private void PromptRenameVoice(VoiceModel voice)
    {
        if (_busy)
        {
            SetStatus("Đang xử lý âm thanh, không thể đổi tên file model lúc này.", Theme.Gold);
            return;
        }
        using var popup = new Form
        {
            Text = "Đổi tên file & Chuẩn hóa thông tin Giọng đọc (.onnx + .onnx.json)",
            Size = new Size(560, 355),
            MinimumSize = new Size(560, 355),
            MaximumSize = new Size(680, 355),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            ShowInTaskbar = false,
            MaximizeBox = false,
            MinimizeBox = false
        };

        var currentStem = Path.GetFileNameWithoutExtension(voice.ModelPath);
        popup.Controls.Add(new Label
        {
            Text = "1. Tên file vật lý (.onnx và .onnx.json sẽ đổi đồng bộ):",
            Left = 20, Top = 18, Width = 500, Height = 22, ForeColor = Theme.Text, Font = Theme.Body
        });
        var fileStemInput = Theme.Textbox(currentStem);
        fileStemInput.SetBounds(20, 42, 500, 30);
        popup.Controls.Add(fileStemInput);

        popup.Controls.Add(new Label
        {
            Text = "2. Tên hiển thị trên Studio (Display Name):",
            Left = 20, Top = 82, Width = 500, Height = 22, ForeColor = Theme.Text, Font = Theme.Body
        });
        var displayNameInput = Theme.Textbox(voice.Name);
        displayNameInput.SetBounds(20, 106, 500, 30);
        popup.Controls.Add(displayNameInput);

        popup.Controls.Add(new Label
        {
            Text = "3. Nhóm phân loại giọng:",
            Left = 20, Top = 148, Width = 230, Height = 22, ForeColor = Theme.Text, Font = Theme.Body
        });
        var categoryCombo = Theme.Combo();
        categoryCombo.Items.AddRange(["Nữ", "Nam", "Khác"]);
        categoryCombo.SelectedItem = voice.Category is "Nữ" or "Nam" ? voice.Category : "Khác";
        categoryCombo.SetBounds(20, 172, 230, 32);
        popup.Controls.Add(categoryCombo);

        popup.Controls.Add(new Label
        {
            Text = "4. Mã ngôn ngữ (VD: vi-VN, en-US, zh-CN):",
            Left = 270, Top = 148, Width = 250, Height = 22, ForeColor = Theme.Text, Font = Theme.Body
        });
        var langCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDown,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Theme.Text,
            Font = Theme.Body
        };
        foreach (var code in VoiceLanguage.CommonLanguages) langCombo.Items.Add(code);
        langCombo.Text = voice.Language == VoiceLanguage.Unknown ? "vi-VN" : voice.Language;
        langCombo.SetBounds(270, 172, 250, 32);
        popup.Controls.Add(langCombo);

        var autoFormatBtn = Btn("Gợi ý tên file chuẩn", ModernButton.Kind.Secondary, 165);
        autoFormatBtn.SetBounds(20, 224, 165, 38);
        autoFormatBtn.Click += (_, _) =>
        {
            var catPrefix = (categoryCombo.SelectedItem?.ToString() ?? "Khác") switch
            {
                "Nữ" => "nu",
                "Nam" => "nam",
                _ => "voice"
            };
            var langPart = VoiceLanguage.Normalize(langCombo.Text);
            if (langPart == VoiceLanguage.Unknown) langPart = "vi-VN";
            var cleanName = VoiceCatalog.SanitizeFileStem(displayNameInput.Text).Replace(' ', '_').ToLowerInvariant();
            fileStemInput.Text = $"{langPart}_{catPrefix}_{cleanName}-{voice.Quality}";
        };

        var saveBtn = Btn("Lưu & Đổi tên file", ModernButton.Kind.Primary, 165);
        saveBtn.SetBounds(235, 224, 165, 38);
        saveBtn.DialogResult = DialogResult.OK;

        var cancelBtn = Btn("Hủy", ModernButton.Kind.Secondary, 110);
        cancelBtn.SetBounds(410, 224, 110, 38);
        cancelBtn.DialogResult = DialogResult.Cancel;

        popup.Controls.AddRange([autoFormatBtn, saveBtn, cancelBtn]);
        popup.AcceptButton = saveBtn;
        popup.CancelButton = cancelBtn;

        if (popup.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            StopPlayback();
            var updatedModelPath = VoiceCatalog.RenameAndUpdateVoice(
                voice,
                fileStemInput.Text,
                displayNameInput.Text,
                categoryCombo.SelectedItem?.ToString(),
                langCombo.Text,
                _storage.Settings.VoicesFolder);

            _storage.Settings.LastVoicePath = updatedModelPath;
            _storage.SaveSettings();
            ReloadVoices();
            SetStatus($"Đã cập nhật và đổi tên file giọng thành: {Path.GetFileName(updatedModelPath)}", Theme.Green);
        }
        catch (Exception ex)
        {
            ShowError(ex, "Không thể đổi tên file model giọng.");
        }
    }

    private void SetBusy(bool busy, string status)
    {
        _busy = busy;
        bool canRun = !busy && _voiceCombo.SelectedItem is VoiceModel && _editor.TextLength > 0;
        _renderButton.Enabled = canRun;
        _reviewButton.Enabled = canRun;
        _reviewToolbarButton.Enabled = canRun;
        if (_previewVoiceButton is not null) _previewVoiceButton.Enabled = !busy && _voiceCombo.SelectedItem is VoiceModel;
        if (_renameVoiceQuickButton is not null) _renameVoiceQuickButton.Enabled = !busy && _voiceCombo.SelectedItem is VoiceModel;
        if (_renderAllExamplesButton is not null) _renderAllExamplesButton.Enabled = !busy;
        _mergeButton.Enabled = !busy;
        _cancelButton.Enabled = busy;
        _renderStatus.Text = status;
        _renderStatus.ForeColor = busy ? Theme.Blue : Theme.Muted;
        if (busy)
        {
            _footerStatus.Text = status;
            _footerStatus.ForeColor = Theme.Blue;
        }
    }

    private async Task StartReviewSnippetAsync()
    {
        if (_busy) { SetStatus("Đang chạy tác vụ khác. Có thể chờ hoặc bấm Dừng.", Theme.Gold); return; }
        if (_voiceCombo.SelectedItem is not VoiceModel voice)
        { MessageBox.Show("Chưa có model giọng đọc. Hãy thêm model vào thư mục Voices."); return; }
        if (_speedCombo.SelectedItem is not SpeedOption selectedSpeed) return;

        var rawSource = _editor.SelectionLength > 0 && !string.IsNullOrWhiteSpace(_editor.SelectedText)
            ? _editor.SelectedText
            : _editor.Text;

        if (string.IsNullOrWhiteSpace(rawSource))
        { MessageBox.Show("Hãy nhập nội dung văn bản trước khi nghe thử (Review)."); return; }

        int maxWords = (_reviewLimitCombo.SelectedItem as WordLimitOption)?.Words ?? _storage.Settings.ReviewWordLimit;
        var speaker = _speakerCombo.SelectedItem is VoiceSpeaker s ? s.Id : (int?)null;
        bool useCuda = _engineCombo.SelectedIndex == 1;

        _jobCancellation = new CancellationTokenSource();
        var token = _jobCancellation.Token;
        StopPlayback();
        SetBusy(true, $"Đang tổng hợp nghe thử (tối đa {maxWords} từ)...");
        _progress.Percent = 0;

        var progress = new Progress<(int done, int total)>(p =>
        {
            if (_closing || IsDisposed) return;
            _progress.Percent = (int)(p.done * 100d / Math.Max(p.total, 1));
            _renderStatus.Text = $"Review: đang xử lý {p.done}/{p.total}";
        });

        try
        {
            var review = await _jobs.RenderReviewSnippetAsync(
                rawSource, maxWords, voice, selectedSpeed.Value, speaker, useCuda, progress, token);
            if (_closing) return;
            _progress.Percent = 100;
            PlayAudioFile(review.Mp3Path, $"[Review {review.WordCount} từ] {voice.Name} · {selectedSpeed.Value:0.##}×");
            SetStatus(
                $"Đang phát bản nghe thử ({review.WordCount} từ{(review.Truncated ? $", cắt từ giới hạn {maxWords} từ" : "")}) – Giọng: {voice.Name}.",
                Theme.Green);
        }
        catch (OperationCanceledException) { SetStatus("Đã hủy nghe thử.", Theme.Gold); }
        catch (Exception ex) { if (!_closing) ShowError(ex, "Nghe thử đoạn ngắn thất bại."); SetStatus(ex.Message, Theme.Danger); }
        finally
        {
            _jobCancellation?.Dispose();
            _jobCancellation = null;
            if (!_closing) SetBusy(false, "Sẵn sàng.");
        }
    }

    private async Task StartRenderAsync()
    {
        if (_busy) { SetStatus("Đang chạy tác vụ khác. Có thể chờ hoặc bấm Dừng.", Theme.Gold); return; }
        if (_voiceCombo.SelectedItem is not VoiceModel voice)
        { MessageBox.Show("Chưa có model giọng đọc. Hãy sao chép Voices hoặc chọn đường dẫn trong Cấu hình."); return; }
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
            SetStatus($"Đã tạo {(_srtOnRender.Checked ? "MP3 + SRT" : "MP3")} trong Output: {result.Title} · ID {result.Id[..8]}", Theme.Green);
            ShowPage("library");
        }
        catch (OperationCanceledException) { SetStatus("Đã hủy lượt render (các batch hoàn tất được giữ trong cache).", Theme.Gold); }
        catch (Exception ex) { if (!_closing) ShowError(ex, "Render MP3 thất bại."); SetStatus(ex.Message, Theme.Danger); }
        finally
        {
            _jobCancellation?.Dispose();
            _jobCancellation = null;
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
        _footerCount.Text = $"{_storage.Count:N0} MP3 trong Output";
    }

    private static string FormatSize(long bytes)
        => bytes < 1024 ? $"{bytes} B" : bytes < 1048576 ? $"{bytes / 1024d:N1} KB" : $"{bytes / 1048576d:N1} MB";

    private List<AudioEntry> SelectedLibrary() => _library.SelectedItems.Cast<ListViewItem>()
        .Select(x => x.Tag).OfType<AudioEntry>().ToList();

    private void PlayAudioFile(string filePath, string displayTitle)
    {
        try
        {
            _player.Play(filePath);
            _playerTitle.Text = displayTitle;
            _playerPlayButton.Text = "Ⅱ  Tạm dừng";
            SetStatus($"Đang phát: {displayTitle}", Theme.Green);
        }
        catch (Exception ex) { ShowError(ex, "Không phát được âm thanh."); }
    }

    private void PlaySelected()
    {
        var selected = SelectedLibrary();
        if (selected.Count == 0) { SetStatus("Chọn một MP3 trong danh sách để nghe.", Theme.Gold); return; }
        PlayAudioFile(selected[0].FilePath, selected[0].Title);
    }

    private void TogglePlayer()
    {
        try
        {
            if (_player.CurrentFile is null)
            {
                if (_activePage == "voices")
                {
                    _ = PlayOrGenerateSelectedVoiceLabExampleAsync(forceRegenerate: false);
                    return;
                }
                PlaySelected();
                return;
            }
            _player.TogglePause();
            _playerPlayButton.Text = _player.IsPlaying ? "Ⅱ  Tạm dừng" : "▶  Tiếp tục";
        }
        catch (Exception ex) { ShowError(ex, "Không điều khiển được trình phát MP3."); }
    }

    private void StopPlayback()
    {
        _player.Close();
        if (_playerTitle is not null) _playerTitle.Text = "Chưa phát âm thanh";
        if (_playerTime is not null) _playerTime.Text = "00:00 / 00:00";
        if (_playerSeek is not null) _playerSeek.Value = 0;
        if (_playerPlayButton is not null) _playerPlayButton.Text = "▶  Phát";
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
                _playerPlayButton.Text = "▶  Phát";
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
                using var dialog = new SaveFileDialog
                {
                    Title = "Lưu bản sao MP3",
                    Filter = "MP3 Audio|*.mp3",
                    FileName = Storage.SafeName(file.Title) + ".mp3",
                    OverwritePrompt = true
                };
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
            SetStatus("Đã đổi tên hiển thị.", Theme.Green);
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
            RefreshLibrary();
            RefreshMergeList();
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
            : $"{count:N0} / {_mergeList.Items.Count:N0} đoạn · đủ SRT";
        _mergeHint.ForeColor = missing > 0 ? Theme.Danger : count >= 2 ? Theme.Green : Theme.Blue;
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
            RefreshLibrary();
            RefreshMergeList();
            ShowPage("library");
            SetStatus($"Đã ghép MP3 + SRT ({selected.Length} đoạn) · tốc độ {((_mergeSpeedCombo.SelectedItem as SpeedOption)?.Value ?? 1):0.##}×: {result.Title}.", Theme.Green);
        }
        catch (OperationCanceledException) { SetStatus("Đã hủy ghép MP3.", Theme.Gold); }
        catch (Exception ex) { if (!_closing) ShowError(ex, "Không ghép được MP3."); SetStatus(ex.Message, Theme.Danger); }
        finally
        {
            _jobCancellation?.Dispose();
            _jobCancellation = null;
            if (!_closing) SetBusy(false, "Đã hoàn tất tác vụ. Sẵn sàng tiếp tục.");
        }
    }

    private void UpdateFolderSetting(string kind, string folder)
    {
        folder = folder.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(folder)) return;
        try
        {
            Directory.CreateDirectory(folder);
            var settings = _storage.Settings;
            if (kind == "output") settings.OutputFolder = folder;
            else if (kind == "voices") settings.VoicesFolder = folder;
            else if (kind == "runtime") settings.RuntimeFolder = folder;
            _storage.EnsureActiveFolders();
            _storage.SaveSettings();
            if (kind == "voices") ReloadVoices();
            if (kind == "output") RefreshLibrary();
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
        var ffprobe = File.Exists(Path.Combine(Path.GetDirectoryName(_audio.FfmpegExe) ?? "", "ffprobe.exe"));
        var samples = _voices.Count(v => VoiceCatalog.HasExampleAudio(_storage.Settings.VoicesFolder, v));
        _dependencies.Text =
            $"{(piper ? "✓" : "✕")} Piper (Runtime/piper)   ·   {(ffmpeg && ffprobe ? "✓" : "✕")} FFmpeg + FFprobe (Runtime/ffmpeg)   ·   {_voices.Count} Voices ({samples} mẫu)   ·   Output sẵn sàng";
        _dependencies.ForeColor = piper && ffmpeg && ffprobe && _voices.Count > 0 ? Theme.Green : Theme.Gold;
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
            Text = title,
            Size = new Size(490, 215),
            MinimumSize = new Size(490, 215),
            MaximumSize = new Size(740, 215),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            ShowInTaskbar = false,
            MaximizeBox = false,
            MinimizeBox = false
        };
        popup.Controls.Add(new Label
        {
            Text = "Tên hiển thị (đồng thời dùng khi xuất bản sao)",
            Left = 20, Top = 20, Width = 430, Height = 25, ForeColor = Theme.Text, Font = Theme.Body
        });
        var input = Theme.Textbox(defaultText);
        input.SetBounds(20, 51, 432, 32);
        input.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        popup.Controls.Add(input);
        var okay = Btn("Lưu thay đổi", ModernButton.Kind.Primary, 140);
        okay.SetBounds(203, 105, 140, 39);
        okay.DialogResult = DialogResult.OK;
        var cancel = Btn("Hủy", ModernButton.Kind.Secondary, 106);
        cancel.SetBounds(346, 105, 106, 39);
        cancel.DialogResult = DialogResult.Cancel;
        popup.Controls.AddRange([okay, cancel]);
        popup.AcceptButton = okay;
        popup.CancelButton = cancel;
        return popup.ShowDialog(this) == DialogResult.OK ? Storage.SafeName(input.Text) : null;
    }
}
