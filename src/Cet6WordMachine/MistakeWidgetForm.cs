using System.Runtime.InteropServices;

namespace Cet6WordMachine;

internal sealed record MistakeItem(
    string Word,
    string Phonetic,
    string Meaning,
    string UserAnswer,
    int SelfGrade);

internal sealed class MistakeWidgetForm : Form
{
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExTopmost = 0x00000008;
    private static readonly Dictionary<(float Size, FontStyle Style), Font> FontCache = [];

    private readonly Label _title;
    private readonly FlowLayoutPanel _list;
    private readonly TableLayoutPanel _root;
    private readonly Button _restButton;
    private readonly Label _restStatus, _quizCover;
    private readonly System.Windows.Forms.Timer _restTimer;
    private readonly ToolTip _tips = new();
    private readonly MistakeRestPolicy _rest;
    private readonly Func<int, int> _choose;
    private IReadOnlyList<MistakeItem> _items = [];
    private MistakeRestQuizForm? _quiz;
    private MistakeItem? _question;
    private string? _context;
    private bool _keepInside;
    private int _layoutDpi;

    private bool _positioned;
    private bool _userMoved;
    private bool _allowClose;
    private bool _resizingRows;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(
        IntPtr hWnd,
        int message,
        IntPtr wParam,
        IntPtr lParam);

    public MistakeWidgetForm(MistakeRestPolicy? rest = null, Func<int, int>? choose = null)
    {
        _rest = rest ?? new MistakeRestPolicy(new MistakeRestStore(StateStore.DataDirectory));
        _choose = choose ?? Random.Shared.Next;
        _layoutDpi = DeviceDpi;
        Name = "MistakeWidgetForm";
        Text = "今日易错词";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = UiTheme.Ink;
        Padding = new Padding(1);
        Size = new Size(UiLayout.Pixels(this, 400), UiLayout.Pixels(this, 200));

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.White,
        };
        _root = root;
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));


        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Accent,
            Cursor = Cursors.SizeAll,
        };
        _title = new Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 0, 12, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = FontFor(12, FontStyle.Bold),
            ForeColor = Color.White,
        };
        header.Controls.Add(_title);
        header.MouseDown += (_, eventArgs) => BeginDrag(eventArgs);
        _title.MouseDown += (_, eventArgs) => BeginDrag(eventArgs);

        _list = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.White,
            Padding = new Padding(8, 8, 8, 4),
        };
        _list.SizeChanged += (_, _) => ResizeRows();

        var body = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        _quizCover = UiTheme.Label("正在回忆中文意思…", 12); _quizCover.Name = "MistakeQuizCover";
        _quizCover.TextAlign = ContentAlignment.MiddleCenter; _quizCover.Visible = false;
        body.Controls.Add(_list); body.Controls.Add(_quizCover);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(10, 4, 10, 8), Margin = Padding.Empty, BackColor = UiTheme.Page };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 158)); footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _restStatus = UiTheme.Label("随机抽查 1 词", 11); _restStatus.Name = "MistakeRestStatus"; _restStatus.ForeColor = UiTheme.Muted;
        _restButton = UiTheme.Button("答题隐藏 1 小时"); _restButton.Name = "HideMistakeWidget"; _restButton.Dock = DockStyle.Fill; _restButton.Margin = Padding.Empty;
        _restButton.Click += (_, _) => TryRest();
        footer.Controls.Add(_restStatus, 0, 0); footer.Controls.Add(_restButton, 1, 0);
        root.Controls.Add(header, 0, 0); root.Controls.Add(body, 0, 1); root.Controls.Add(footer, 0, 2);

        UiLayout.Scale(root, DeviceDpi / 96f);
        Controls.Add(root);
        _keepInside = true;
        _restTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _restTimer.Tick += (_, _) => RefreshRestVisibility();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            // Use the native topmost style for this passive window. Form.TopMost
            // focuses a child during Show(), even with ShowWithoutActivation;
            // reassigning it on timer ticks also activates via SetWindowPos.
            parameters.ExStyle |= WsExNoActivate | WsExToolWindow | WsExTopmost;
            return parameters;
        }
    }

    public void UpdateItems(IReadOnlyList<MistakeItem> items, string? context = null)
    {
        _items = items.ToArray();
        var invalidated = _context != context || (_question is not null && !_items.Contains(_question));
        _context = context;
        if (invalidated) _quiz?.CancelForUpdate();
        if (items.Count == 0)
        {
            _restTimer.Stop();
            Hide();
            return;
        }

        _title.Text = $"今日易错词 · {items.Count}";
        _list.SuspendLayout();
        var oldRows = _list.Controls.Cast<Control>().ToArray();
        _list.Controls.Clear();
        foreach (var control in oldRows)
        {
            control.Dispose();
        }

        var rows = new List<Panel>();
        foreach (var item in items)
        {
            var row = BuildRow(item);
            rows.Add(row);
            _list.Controls.Add(row);
        }

        _list.ResumeLayout();
        var workingArea = Screen.FromControl(this).WorkingArea;
        Width = Math.Max(1, Math.Min(UiLayout.Pixels(this, 400), workingArea.Width - UiLayout.Pixels(this, 24)));
        ResizeRows();
        var desiredHeight = UiLayout.Pixels(this, 116) + rows.Sum(row => row.Height + row.Margin.Vertical);
        var targetHeight = Math.Clamp(
            desiredHeight,
            Math.Min(UiLayout.Pixels(this, 200), workingArea.Height),
            Math.Max(Math.Min(UiLayout.Pixels(this, 200), workingArea.Height), workingArea.Height - UiLayout.Pixels(this, 40)));
        Height = targetHeight;

        if (!_userMoved ||
            !_positioned ||
            !Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(Bounds)))
        {
            var area = Screen.PrimaryScreen?.WorkingArea ?? SystemInformation.WorkingArea;
            Location = new Point(
                area.Right - Width - 24,
                area.Bottom - Height - 24);
            _positioned = true;
        }

        _restTimer.Start();
        RefreshRestVisibility();
        ResizeRows();
    }

    private void TryRest()
    {
        if (_allowClose || _quiz is not null || _items.Count == 0) return;
        if (!_rest.TryBegin()) { RefreshRestVisibility(); return; }
        _question = _items[_choose(_items.Count)];
        using var quiz = new MistakeRestQuizForm(_question); _quiz = quiz;
        _list.Visible = false; _quizCover.Visible = true; _quizCover.BringToFront();
        RefreshRestVisibility();
        try
        {
            quiz.ShowDialog(this);
            if (!_allowClose && !IsDisposed && !quiz.WasInvalidated)
                _rest.Finish(quiz.SelfGrade == 2);
        }
        finally
        {
            _quiz = null; _question = null;
            if (!_allowClose && !IsDisposed)
            {
                _list.Visible = true; _quizCover.Visible = false;
                RefreshRestVisibility(); ResizeRows();
            }
        }
    }

    internal void RefreshRestVisibility()
    {
        if (_allowClose || IsDisposed) return;
        var cooldown = _rest.RetryRemaining;
        _restButton.Enabled = _quiz is null && _items.Count > 0 && (_rest.CanAttempt || _rest.NeedsReload);
        _restButton.Text = _quiz is not null ? "答题中" : _rest.NeedsReload ? "重试读取" : cooldown > TimeSpan.Zero ? "稍后再试" : "答题隐藏 1 小时";
        var seconds = (int)Math.Ceiling(cooldown.TotalSeconds);
        _restStatus.Text = _quiz is not null ? "正在答题…" : _rest.Error.Length > 0 ? "状态读写失败" : cooldown > TimeSpan.Zero ? $"再等 {seconds / 60:00}:{seconds % 60:00}" : "随机抽查 1 词";
        _restStatus.ForeColor = _rest.Error.Length > 0 ? UiTheme.Error : UiTheme.Muted;
        _tips.SetToolTip(_restStatus, _rest.Error);
        if (_items.Count == 0 || _rest.IsHidden) { if (Visible) Hide(); return; }
        KeepOnScreen();
        if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
        if (!Visible) Show();
    }

    private void KeepOnScreen()
    {
        if (!_keepInside || IsDisposed) return;
        var bounded = WidgetScreenBounds.Clamp(Bounds, Screen.FromRectangle(Bounds).WorkingArea);
        if (Bounds != bounded) Bounds = bounded;
    }

    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
    {
        if (_keepInside && WindowState == FormWindowState.Normal)
        {
            var bounded = WidgetScreenBounds.Clamp(new Rectangle(x, y, width, height), Screen.FromRectangle(new Rectangle(x, y, width, height)).WorkingArea);
            x = bounded.X; y = bounded.Y; width = bounded.Width; height = bounded.Height;
        }
        base.SetBoundsCore(x, y, width, height, specified);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPosition { internal IntPtr Window, InsertAfter; internal int X, Y, Width, Height; internal uint Flags; }

    protected override void WndProc(ref Message message)
    {
        if (_keepInside && message.Msg == 0x216 && message.LParam != IntPtr.Zero) // WM_MOVING: constrain before painting.
        {
            var rectangle = Marshal.PtrToStructure<NativeRect>(message.LParam);
            var bounded = WidgetScreenBounds.Clamp(Rectangle.FromLTRB(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom), Screen.FromPoint(Cursor.Position).WorkingArea);
            rectangle.Left = bounded.Left; rectangle.Top = bounded.Top; rectangle.Right = bounded.Right; rectangle.Bottom = bounded.Bottom;
            Marshal.StructureToPtr(rectangle, message.LParam, false); message.Result = (IntPtr)1; return;
        }
        if (_keepInside && message.Msg == 0x46 && message.LParam != IntPtr.Zero && WindowState == FormWindowState.Normal)
        {
            var position = Marshal.PtrToStructure<WindowPosition>(message.LParam);
            var proposed = new Rectangle((position.Flags & 2) == 0 ? position.X : Left, (position.Flags & 2) == 0 ? position.Y : Top,
                (position.Flags & 1) == 0 ? position.Width : Width, (position.Flags & 1) == 0 ? position.Height : Height);
            var bounded = WidgetScreenBounds.Clamp(proposed, Screen.FromRectangle(proposed).WorkingArea);
            if (bounded.Location != proposed.Location) { position.Flags &= ~2u; position.X = bounded.X; position.Y = bounded.Y; }
            if (bounded.Size != proposed.Size) { position.Flags &= ~1u; position.Width = bounded.Width; position.Height = bounded.Height; }
            Marshal.StructureToPtr(position, message.LParam, false);
        }
        if (_keepInside && message.Msg == 0x112 && ((long)message.WParam & 0xFFF0) == 0xF020) return; // No minimize escape.
        base.WndProc(ref message);
        if (_keepInside && message.Msg is 0x7E or 0x1A && IsHandleCreated && !IsDisposed)
            BeginInvoke(new Action(KeepOnScreen)); // Display topology or taskbar changes.
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        if (_root is null) return;
        UiLayout.Scale(_root, e.DeviceDpiNew / (float)_layoutDpi); _layoutDpi = e.DeviceDpiNew;
        KeepOnScreen(); ResizeRows();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _allowClose = true; _restTimer?.Dispose(); _quiz?.CancelForUpdate(); _tips.Dispose(); }
        base.Dispose(disposing);
    }

    public void CloseForApplication()
    {
        _allowClose = true;
        _restTimer.Stop();
        _quiz?.CancelForUpdate();
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose && e.CloseReason is not (CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing))
        {
            e.Cancel = true;
            return;
        }

        base.OnFormClosing(e);
    }

    private Panel BuildRow(MistakeItem item)
    {
        var accent = UiTheme.Accent;
        var row = new TableLayoutPanel
        {
            Height = 90,
            Width = CurrentRowWidth(),
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 6),
            Padding = new Padding(10, 4, 8, 4),
            BackColor = UiTheme.Soft,
        };
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        row.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = string.IsNullOrWhiteSpace(item.Phonetic) ? item.Word : $"{item.Word}   /{item.Phonetic.Trim('/')}/",
            TextAlign = ContentAlignment.MiddleLeft,
            Font = FontFor(11, FontStyle.Bold),
            ForeColor = accent,
        }, 0, 0);

        row.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = item.Meaning,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = false,
            AutoEllipsis = false,
            Font = FontFor(11),
            ForeColor = UiTheme.Ink,
            UseCompatibleTextRendering = true,
            Padding = new Padding(0, 3, 0, 8),
        }, 0, 1);
        UiLayout.Scale(row, DeviceDpi / 96f);
        return row;
    }

    private int CurrentRowWidth() => Math.Max(UiLayout.Pixels(this, 160), _list.ClientSize.Width - _list.Padding.Horizontal
        - SystemInformation.VerticalScrollBarWidth - UiLayout.Pixels(this, 8));

    private void ResizeRows()
    {
        if (_resizingRows) return;
        _resizingRows = true;
        try
        {
            var width = CurrentRowWidth();
            foreach (var row in _list.Controls.OfType<TableLayoutPanel>())
            {
                row.Width = width;
                var word = (Label)row.GetControlFromPosition(0, 0)!;
                var meaning = (Label)row.GetControlFromPosition(0, 1)!;
                var available = width - row.Padding.Horizontal;
                var wordHeight = UiLayout.TextHeight(word, available - word.Margin.Horizontal) + word.Margin.Vertical;
                row.RowStyles[0].Height = Math.Max(UiLayout.Pixels(this, 30), wordHeight);
                row.Height = (int)row.RowStyles[0].Height + UiLayout.TextHeight(meaning, available - meaning.Margin.Horizontal)
                    + meaning.Margin.Vertical + row.Padding.Vertical + UiLayout.Pixels(this, 8);
            }
        }
        finally { _resizingRows = false; }
    }

    private void BeginDrag(MouseEventArgs eventArgs)
    {
        if (eventArgs.Button != MouseButtons.Left)
        {
            return;
        }

        ReleaseCapture();
        _userMoved = true;
        SendMessage(Handle, 0xA1, (IntPtr)0x2, IntPtr.Zero);
    }

    private static Font FontFor(float size, FontStyle style = FontStyle.Regular)
    {
        var key = (size, style);
        if (!FontCache.TryGetValue(key, out var font))
        {
            font = new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point);
            FontCache[key] = font;
        }

        return font;
    }
}
