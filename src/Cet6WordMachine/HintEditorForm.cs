namespace Cet6WordMachine;

internal sealed class HintEditorForm : UiDialog
{
    private readonly WordEntry _entry;
    private readonly AiServices _services;
    private readonly TextBox _textBox;
    private readonly Button _generate, _settings, _undo, _save;
    private readonly Label _status;
    private CancellationTokenSource? _request;
    private string? _undoText;
    private bool _hasGenerated, _closing;

    public HintEditorForm(WordEntry entry, string hint, AiServices? services = null)
    {
        _entry = entry; _services = services ?? AiServices.CreateDefault();
        Text = "我的联想"; Name = "HintEditorForm";
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false; MaximizeBox = false; MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.None; ClientSize = new Size(650, 450); MinimumSize = new Size(520, 420);
        BackColor = UiTheme.Page; Font = UiTheme.Font();
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(24) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        var title = UiTheme.Label(entry.Word, 22, true); UiLayout.FitWord(title); root.Controls.Add(title, 0, 0);
        var tools = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
        tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130)); tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        tools.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _generate = UiTheme.Button("AI 生成"); _generate.Name = "GenerateAiHint"; _generate.Size = new Size(120, 40);
        _generate.Click += async (_, _) => await GenerateAsync();
        _undo = UiTheme.Button("撤回生成"); _undo.Name = "UndoAiHint"; _undo.Size = new Size(120, 40); _undo.Enabled = false;
        _undo.Click += (_, _) => UndoGeneration();
        _settings = UiTheme.Button("AI 设置"); _settings.Name = "HintAiSettings"; _settings.Dock = DockStyle.Fill; _settings.Margin = new Padding(0, 0, 0, 10);
        _settings.Click += (_, _) => OpenSettings();
        tools.Controls.Add(_generate, 0, 0); tools.Controls.Add(_undo, 1, 0); tools.Controls.Add(_settings, 3, 0); root.Controls.Add(tools, 0, 1);
        _textBox = new TextBox { Name = "HintInput", Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical,
            Font = UiTheme.Font(12), Text = hint, BorderStyle = BorderStyle.FixedSingle, PlaceholderText = "写下帮助你记忆的联想" };
        root.Controls.Add(_textBox, 0, 2);
        _status = UiTheme.Label("清空后恢复词库原有联想。", 11); _status.Name = "HintStatus"; _status.ForeColor = UiTheme.Muted;
        root.Controls.Add(_status, 0, 3);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty };
        _save = UiTheme.Button("保存", true); _save.Name = "SaveHint"; _save.DialogResult = DialogResult.OK; _save.Width = 112; _save.Margin = Padding.Empty;
        var cancel = UiTheme.Button("取消"); cancel.Name = "CancelHint"; cancel.DialogResult = DialogResult.Cancel; cancel.Width = 112;
        actions.Controls.Add(_save); actions.Controls.Add(cancel); root.Controls.Add(actions, 0, 4);
        FinishLayout(root); AcceptButton = _save; CancelButton = cancel;
    }

    public string HintText => _textBox.Text.Trim();

    private void UndoGeneration()
    {
        if (_undoText is null) return;
        _textBox.Text = _undoText; _undoText = null; _undo.Enabled = false;
        Status("已恢复上一次生成前的内容。", false);
    }

    private void OpenSettings()
    {
        using var dialog = new AiSettingsForm(_services);
        if (dialog.ShowDialog(this) == DialogResult.OK) Status("设置已保存，点击 AI 生成即可。", false);
    }

    private async Task GenerateAsync()
    {
        if (_request is not null)
        {
            _request.Cancel(); _generate.Enabled = false;
            Status("正在取消…", false); return;
        }
        AiSettings settings;
        try { settings = _services.Settings.Load(); }
        catch (AiHintException ex) { Status(ex.Message, true); return; }
        if (string.IsNullOrWhiteSpace(settings.ApiKey)) { OpenSettings(); return; }
        using var request = new CancellationTokenSource(); _request = request;
        Busy(true); Status("正在构思联想…", false);
        try
        {
            var content = await _services.Generator.GenerateAsync(_entry, settings, request.Token);
            if (_closing || IsDisposed || request.IsCancellationRequested) return;
            _undoText = _textBox.Text; _textBox.Text = content; _hasGenerated = true;
            Status("已生成，可继续修改；点击保存后生效。", false);
        }
        catch (OperationCanceledException) { if (!_closing) Status("已取消，编辑内容未改变。", false); }
        catch (AiHintException ex) { if (!_closing) Status(ex.Message, true); }
        catch (Exception) { if (!_closing) Status("生成未成功，请稍后重试。编辑内容未改变。", true); }
        finally
        {
            _request = null;
            if (!_closing && !IsDisposed)
            {
                Busy(false);
                if (request.IsCancellationRequested) Status("已取消，编辑内容未改变。", false);
            }
        }
    }

    private void Busy(bool busy)
    {
        _textBox.ReadOnly = busy; _save.Enabled = _settings.Enabled = !busy;
        _undo.Enabled = !busy && _undoText is not null;
        _generate.Enabled = true; _generate.Text = busy ? "取消生成" : _hasGenerated ? "换一种" : "AI 生成";
    }
    private void Status(string text, bool error) { _status.Text = text; _status.ForeColor = error ? UiTheme.Error : UiTheme.Muted; }
    protected override void OnFormClosing(FormClosingEventArgs e)
    { base.OnFormClosing(e); if (!e.Cancel) { _closing = true; _request?.Cancel(); } }
    protected override void Dispose(bool disposing)
    { if (disposing) { _closing = true; _request?.Cancel(); } base.Dispose(disposing); }
}
