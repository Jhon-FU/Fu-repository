namespace Cet6WordMachine;

internal sealed class AiSettingsForm : UiDialog
{
    private readonly AiServices _services;
    private readonly TextBox _key;
    private readonly ComboBox _model;
    private readonly CheckBox _showKey;
    private readonly Button _test, _save;
    private readonly Label _status;
    private CancellationTokenSource? _request;
    private bool _closing;

    internal AiSettingsForm(AiServices services)
    {
        _services = services;
        Text = "AI 设置"; Name = "AiSettingsForm"; StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false; MaximizeBox = false; MinimizeBox = false; AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(580, 460); MinimumSize = new Size(520, 450); BackColor = UiTheme.Page; Font = UiTheme.Font();
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Padding = new Padding(24) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 50, 28, 42, 28, 42, 72 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.Controls.Add(UiTheme.Label("DeepSeek 联想", 22, true), 0, 0);
        root.Controls.Add(UiTheme.Label("API Key"), 0, 1);
        var keyRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        keyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); keyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        keyRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _key = new TextBox { Name = "AiApiKey", Dock = DockStyle.Fill, UseSystemPasswordChar = true, MaxLength = 4096, PlaceholderText = "粘贴你的 DeepSeek API Key", Font = UiTheme.Font() };
        _showKey = new CheckBox { Name = "ShowAiKey", Text = "显示", Dock = DockStyle.Fill, Margin = new Padding(10, 0, 0, 0) };
        _showKey.CheckedChanged += (_, _) => _key.UseSystemPasswordChar = !_showKey.Checked;
        keyRow.Controls.Add(_key, 0, 0); keyRow.Controls.Add(_showKey, 1, 0); root.Controls.Add(keyRow, 0, 2);
        root.Controls.Add(UiTheme.Label("模型"), 0, 3);
        _model = new ComboBox { Name = "AiModel", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Font = UiTheme.Font() };
        _model.Items.AddRange(AiSettings.SupportedModels); _model.SelectedIndex = 0; root.Controls.Add(_model, 0, 4);
        var disclosure = UiTheme.Label("仅点击生成时发送当前词、音标和释义，费用由你的 API 账号承担。Key 仅加密保存在本机；清空并保存可移除。", 11);
        disclosure.ForeColor = UiTheme.Muted; root.Controls.Add(disclosure, 0, 5);
        _status = UiTheme.Label("", 11); _status.Name = "AiSettingsStatus"; root.Controls.Add(_status, 0, 6);
        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _test = UiTheme.Button("测试连接"); _test.Name = "TestAiConnection"; _test.Dock = DockStyle.Fill; _test.Margin = new Padding(0, 0, 10, 2);
        _test.Click += async (_, _) => await TestAsync(); actions.Controls.Add(_test, 0, 0);
        var right = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty };
        _save = UiTheme.Button("保存", true); _save.Name = "SaveAiSettings"; _save.Width = 112; _save.Margin = Padding.Empty; _save.Click += (_, _) => SaveSettings();
        var cancel = UiTheme.Button("取消"); cancel.Name = "CancelAiSettings"; cancel.Width = 112; cancel.DialogResult = DialogResult.Cancel;
        right.Controls.Add(_save); right.Controls.Add(cancel); actions.Controls.Add(right, 2, 0); root.Controls.Add(actions, 0, 7);
        FinishLayout(root); AcceptButton = _save; CancelButton = cancel;
        try { var settings = _services.Settings.Load(); _key.Text = settings.ApiKey; _model.SelectedItem = settings.Model; }
        catch (AiHintException ex) { Status(ex.Message, true); }
    }

    private AiSettings ReadSettings() => new() { ApiKey = _key.Text.Trim(), Model = (string?)_model.SelectedItem ?? AiSettings.DefaultModel };
    private void SaveSettings()
    {
        try { _services.Settings.Save(ReadSettings()); DialogResult = DialogResult.OK; Close(); }
        catch (AiHintException ex) { Status(ex.Message, true); }
    }
    private async Task TestAsync()
    {
        if (_request is not null) { _request.Cancel(); _test.Enabled = false; Status("正在取消…", false); return; }
        using var request = new CancellationTokenSource(); _request = request;
        Busy(true); Status("正在连接…", false);
        try
        {
            await _services.Generator.TestConnectionAsync(ReadSettings(), request.Token);
            if (!_closing && !request.IsCancellationRequested) Status("连接成功，所选模型可用。", false);
        }
        catch (OperationCanceledException) { if (!_closing) Status("已取消连接测试。", false); }
        catch (AiHintException ex) { if (!_closing) Status(ex.Message, true); }
        catch (Exception) { if (!_closing) Status("连接测试失败，请稍后重试。", true); }
        finally
        {
            _request = null;
            if (!_closing && !IsDisposed)
            { Busy(false); if (request.IsCancellationRequested) Status("已取消连接测试。", false); }
        }
    }
    private void Busy(bool busy)
    {
        _key.Enabled = _model.Enabled = _showKey.Enabled = _save.Enabled = !busy;
        _test.Enabled = true; _test.Text = busy ? "取消测试" : "测试连接";
    }
    private void Status(string text, bool error) { _status.Text = text; _status.ForeColor = error ? UiTheme.Error : UiTheme.Muted; }
    protected override void OnFormClosing(FormClosingEventArgs e)
    { base.OnFormClosing(e); if (!e.Cancel) { _closing = true; _request?.Cancel(); } }
    protected override void Dispose(bool disposing)
    { if (disposing) { _closing = true; _request?.Cancel(); } base.Dispose(disposing); }
}
