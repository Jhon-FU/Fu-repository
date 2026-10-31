using System.Text.Json;
using System.Xml;

namespace Cet6WordMachine;

internal sealed class WordBankManagerForm : UiDialog
{
    private readonly string _activeId;
    private readonly AppState _state;
    private readonly ListBox _banks = new() { Dock = DockStyle.Fill, DisplayMember = "Name", IntegralHeight = false, HorizontalScrollbar = true, BorderStyle = BorderStyle.None };
    private readonly TextBox _details = new() { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, BorderStyle = BorderStyle.None, BackColor = Color.White, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox _search = new() { Dock = DockStyle.Fill, PlaceholderText = "搜索单词或释义" };
    private readonly DataGridView _preview = CreateGrid();
    private readonly CheckBox _reuse = new() { Text = "首次使用时沿用同词同义的学习进度", AutoSize = true, Checked = true };
    private readonly Label _notice = new() { AutoSize = true, ForeColor = Color.FromArgb(92, 108, 124), MaximumSize = new Size(850, 0) };
    private readonly Button _use = Button("使用所选词库");
    public WordBankPackage? SelectedBank { get; private set; }
    public bool ReuseMatchingProgress => _reuse.Checked;

    public WordBankManagerForm(AppState state)
    {
        _state = state;
        _activeId = state.ActiveWordBankId;
        Text = "词库管理 · 单词机社区版";
        Name = "WordBankManager";
        Font = UiTheme.Font(11);
        AutoScaleMode = AutoScaleMode.None;
        BackColor = UiTheme.Page;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1040, 700);
        MinimumSize = new Size(780, 560);
        ShowInTaskbar = false;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), RowCount = 5, ColumnCount = 1 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var top = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 0, 0, 14) };
        var import = Button("导入词库…"); import.Name = "ImportWordBank";
        import.Click += (_, _) => Import();
        var export = Button("导出所选词库…"); export.Name = "ExportWordBank";
        export.Click += (_, _) => Export();
        var information = Button("词库说明与来源"); information.Name = "WordBankSources";
        information.Click += (_, _) =>
        {
            if (_banks.SelectedItem is not WordBankPackage bank) return;
            UiInformation.Show(this, bank.Name,
                $"{bank.Words.Count:N0} 个词 · {bank.Version}\r\n\r\n{bank.Description}\r\n\r\n来源：{bank.Source}\r\n\r\n授权：{bank.License}\r\n\r\n切换词库时保留当前任务和进度。首次使用可沿用同词同义的进度，以后各词库独立复习。\r\n\r\n搜索预览最多显示 200 条，学习和导出使用全部词条。");
        };
        top.Controls.Add(import); top.Controls.Add(export); top.Controls.Add(information);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 69));
        _banks.Name = "WordBankList";
        _banks.DrawMode = DrawMode.OwnerDrawFixed;
        _banks.DrawItem += (_, e) =>
        {
            if (e.Index < 0 || e.Index >= _banks.Items.Count) return;
            var selected = (e.State & DrawItemState.Selected) != 0;
            using var fill = new SolidBrush(selected ? UiTheme.Soft : Color.White);
            e.Graphics.FillRectangle(fill, e.Bounds);
            var bounds = Rectangle.Inflate(e.Bounds, -UiLayout.Pixels(this, 12), 0);
            TextRenderer.DrawText(e.Graphics, ((WordBankPackage)_banks.Items[e.Index]).Name, Font, bounds,
                selected ? UiTheme.Accent : UiTheme.Ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
        };
        _banks.SelectedIndexChanged += (_, _) => ShowSelected();
        body.Controls.Add(_banks, 0, 0);
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(14, 0, 0, 0) };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _details.BackColor = UiTheme.Page;
        right.Controls.Add(_details, 0, 0); right.Controls.Add(_search, 0, 1); right.Controls.Add(_preview, 0, 2);
        _search.TextChanged += (_, _) => Filter();
        body.Controls.Add(right, 1, 0);
        var bottom = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 12, 0, 0) };
        var close = Button("返回"); close.DialogResult = DialogResult.Cancel;
        CancelButton = close;
        _use.Name = "UseSelectedWordBank";
        ((CleanButton)_use).Primary = true;
        _use.Click += (_, _) =>
        {
            if (_banks.SelectedItem is not WordBankPackage bank) return;
            SelectedBank = bank; DialogResult = DialogResult.OK; Close();
        };
        bottom.Controls.Add(_use); bottom.Controls.Add(close);
        _reuse.Margin = new Padding(0, 9, 0, 0);
        _notice.Margin = new Padding(0, 12, 0, 0);
        root.Controls.Add(top, 0, 0); root.Controls.Add(body, 0, 1); root.Controls.Add(_notice, 0, 2); root.Controls.Add(_reuse, 0, 3); root.Controls.Add(bottom, 0, 4);
        root.SizeChanged += (_, _) => _notice.MaximumSize = new Size(Math.Max(300, root.ClientSize.Width - root.Padding.Horizontal), 0);
        FinishLayout(root);
        _banks.ItemHeight = UiLayout.Pixels(this, 42);
        Reload(_activeId);
    }

    private void Reload(string selectId)
    {
        var catalog = WordBankCatalog.Load();
        // If an imported file was moved/deleted, the saved card snapshot is still
        // a usable library. Do not strand that bank's progress or current task.
        foreach (var (id, saved) in _state.SavedWordBanks)
            if (catalog.Banks.All(b => b.Id != id)) catalog.Banks.Add(new WordBankPackage { Id = id, Name = saved.Name, Words = saved.Entries, IsImported = true, Description = "从已保存的词库快照恢复；原导入文件不可用。" });
        if (catalog.Banks.All(b => b.Id != _activeId)) catalog.Banks.Add(new WordBankPackage { Id = _activeId, Name = _state.ActiveWordBankName, Words = _state.ActiveWordEntries, IsImported = true, Description = "当前已保存的词库快照。" });
        foreach (var bank in catalog.Banks)
        {
            if (bank.Id == _activeId && _state.ActiveWordEntries.Count > 0)
            {
                bank.Name = _state.ActiveWordBankName;
                bank.Words = _state.ActiveWordEntries;
            }
            else if (_state.SavedWordBanks.TryGetValue(bank.Id, out var saved))
            {
                bank.Name = saved.Name;
                bank.Words = saved.Entries;
            }
        }
        _banks.DataSource = catalog.Banks;
        _banks.SelectedItem = catalog.Banks.FirstOrDefault(b => b.Id == selectId) ?? catalog.Banks.FirstOrDefault();
        _notice.Text = catalog.Warnings.Count == 0
            ? ""
            : "有词库文件无法读取，未删除其学习记录：" + string.Join("；", catalog.Warnings.Take(3));
        _notice.Visible = catalog.Warnings.Count > 0;
        _notice.ForeColor = UiTheme.Error;
        ShowSelected();
    }

    private void ShowSelected()
    {
        if (_banks.SelectedItem is not WordBankPackage bank) return;
        var current = bank.Id == _activeId;
        var saved = _state.SavedWordBanks.ContainsKey(bank.Id);
        _details.Text = $"{bank.Name}{(current ? "  · 当前使用" : "")}\r\n{bank.Words.Count:N0} 个词 · {(bank.IsImported ? "我的词库" : "推荐词库")}";
        _reuse.Enabled = !current && !saved;
        _use.Text = current ? "使用当前词库" : saved ? "恢复此词库" : "使用所选词库";
        Filter();
    }

    private void Filter()
    {
        _preview.Rows.Clear();
        if (_banks.SelectedItem is not WordBankPackage bank) return;
        var search = _search.Text.Trim();
        foreach (var word in bank.Words.Where(w => w.Word.Contains(search, StringComparison.OrdinalIgnoreCase) || w.Meaning.Contains(search, StringComparison.OrdinalIgnoreCase)).Take(200))
            _preview.Rows.Add(word.Word, word.Meaning, word.Phonetic);
        _preview.AccessibleDescription = "最多展示 200 条匹配记录，导入和学习使用全部词条。";
    }

    private void Import()
    {
        using var dialog = new WordBankImportForm();
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Imported is not null) Reload(dialog.Imported.Id);
    }

    private void Export()
    {
        if (_banks.SelectedItem is not WordBankPackage bank) return;
        using var dialog = new SaveFileDialog { Filter = "JSON 词库（完整字段）|*.json|CSV 表格（基础字段）|*.csv", FileName = string.Join("_", bank.Name.Split(Path.GetInvalidFileNameChars())), DefaultExt = "json", AddExtension = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            WordBankImporter.Export(bank, dialog.FileName);
            MessageBox.Show(this, $"已导出 {bank.Words.Count:N0} 个词条。", "导出完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
        { MessageBox.Show(this, ex.Message, "导出未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    internal static Button Button(string text)
    {
        var button = UiTheme.Button(text);
        button.AutoSize = true; button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        button.Padding = new Padding(12, 8, 12, 8); button.Margin = new Padding(0, 0, 8, 0);
        return button;
    }

    internal static DataGridView CreateGrid()
    {
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoGenerateColumns = false, RowHeadersVisible = false, BackgroundColor = Color.White, BorderStyle = BorderStyle.None, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Word", HeaderText = "单词", FillWeight = 28 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Meaning", HeaderText = "释义", FillWeight = 50 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Phonetic", HeaderText = "音标", FillWeight = 22 });
        grid.DefaultCellStyle.Padding = new Padding(3);
        grid.RowTemplate.Height = 32;
        grid.ColumnHeadersHeight = 36;
        grid.EnableHeadersVisualStyles = false;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.GridColor = UiTheme.Line;
        grid.ColumnHeadersDefaultCellStyle.BackColor = UiTheme.Soft;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = UiTheme.Muted;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = UiTheme.Soft;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = UiTheme.Muted;
        grid.DefaultCellStyle.ForeColor = UiTheme.Ink;
        grid.DefaultCellStyle.SelectionBackColor = UiTheme.Soft;
        grid.DefaultCellStyle.SelectionForeColor = UiTheme.Accent;
        return grid;
    }
}

internal sealed class WordBankImportForm : UiDialog
{
    private readonly TextBox _name = new() { Dock = DockStyle.Fill, Text = "我的词库", MaxLength = 80 };
    private readonly RichTextBox _paste = new() { Dock = DockStyle.Fill, Multiline = true, AcceptsTab = true, ScrollBars = RichTextBoxScrollBars.Both, WordWrap = false, DetectUrls = false };
    private readonly TextBox _report = new() { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(245, 247, 250) };
    private readonly DataGridView _preview = WordBankManagerForm.CreateGrid();
    private readonly Button _save = WordBankManagerForm.Button("确认导入");
    private WordBankImportResult? _result;
    public WordBankPackage? Imported { get; private set; }

    public WordBankImportForm()
    {
        Text = "导入词库"; Name = "ImportWordBankDialog";
        Font = UiTheme.Font(11); AutoScaleMode = AutoScaleMode.None; BackColor = UiTheme.Page;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(900, 720); MinimumSize = new Size(740, 600); ShowInTaskbar = false;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), RowCount = 7, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label { AutoSize = true, Text = "至少包含单词和释义。", ForeColor = UiTheme.Muted, Margin = new Padding(0, 0, 0, 12) }, 0, 0);
        var nameRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        nameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85)); nameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        nameRow.Controls.Add(new Label { Text = "词库名称", AutoSize = true, Margin = new Padding(0, 5, 0, 0) }, 0, 0); nameRow.Controls.Add(_name, 1, 0);
        root.Controls.Add(nameRow, 0, 1); root.Controls.Add(_paste, 0, 2);
        _paste.Name = "PasteWordBank";
        _paste.TextChanged += (_, _) => { _result = null; _save.Enabled = false; _report.Text = "内容已更改，请重新预览。"; };
        var toolbar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 10) };
        var file = WordBankManagerForm.Button("选择文件并预览…"); file.Name = "ChooseWordBankFile";
        file.Click += (_, _) =>
        {
            using var open = new OpenFileDialog { Filter = "词库文件|*.csv;*.tsv;*.txt;*.xlsx;*.json", CheckFileExists = true };
            if (open.ShowDialog(this) == DialogResult.OK)
            {
                _name.Text = Path.GetFileNameWithoutExtension(open.FileName)[..Math.Min(80, Path.GetFileNameWithoutExtension(open.FileName).Length)];
                Parse(() => WordBankImporter.ReadFile(open.FileName));
            }
        };
        var paste = WordBankManagerForm.Button("解析粘贴内容"); paste.Name = "ParsePastedWordBank";
        paste.Click += (_, _) => Parse(() => WordBankImporter.ReadDelimited(_paste.Text));
        toolbar.Controls.Add(file); toolbar.Controls.Add(paste);
        var format = WordBankManagerForm.Button("格式说明"); format.Name = "ImportFormatHelp";
        format.Click += (_, _) => UiInformation.Show(this, "词库导入格式",
            "必填字段：单词、释义\r\n可选字段：音标、联想、例句\r\n\r\n支持 CSV、TSV、TXT、XLSX 和 JSON，也可直接粘贴表格。\r\n支持 1–50,000 词，最大 20 MB。\r\n\r\nXLSX 读取第一张工作表，公式需先转为值。\r\n预览最多显示 200 条，确认后保存全部有效词条。\r\n\r\n示例：\r\nword,meaning\r\nexample,例子\r\n\r\n同词同义会合并；同词异义请先整理为一条。发现错误时不会导入，修改后重新预览。");
        toolbar.Controls.Add(format);
        root.Controls.Add(toolbar, 0, 3); root.Controls.Add(_preview, 0, 4); root.Controls.Add(_report, 0, 5);
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        _save.Enabled = false; _save.Name = "ConfirmWordBankImport";
        ((CleanButton)_save).Primary = true;
        _save.Click += (_, _) =>
        {
            if (_result is null || !_result.CanImport) return;
            try { Imported = WordBankCatalog.Install(_result, _name.Text); DialogResult = DialogResult.OK; Close(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
            { _report.Text = "保存失败，未覆盖已有词库，可重试：" + ex.Message; }
        };
        var cancel = WordBankManagerForm.Button("取消"); cancel.DialogResult = DialogResult.Cancel; CancelButton = cancel;
        bottom.Controls.Add(_save); bottom.Controls.Add(cancel); root.Controls.Add(bottom, 0, 6);
        FinishLayout(root);
    }

    private void Parse(Func<WordBankImportResult> parse)
    {
        _result = null; _save.Enabled = false; _preview.Rows.Clear();
        try
        {
            _result = parse();
            foreach (var word in _result.Words.Take(200)) _preview.Rows.Add(word.Word, word.Meaning, word.Phonetic);
            _report.Text = $"有效词条：{_result.Words.Count:N0}；重复行：{_result.DuplicateCount}；错误：{_result.Errors.Count}。预览最多 200 条。\r\n" +
                string.Join("\r\n", _result.Warnings.Concat(_result.Errors.Take(30)));
            _save.Enabled = _result.CanImport;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or XmlException or InvalidOperationException or OverflowException or InvalidDataException)
        { _report.Text = "读取失败，未保存词库：" + ex.Message; }
    }
}
