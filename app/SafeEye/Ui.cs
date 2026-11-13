namespace SafeEye;

/// <summary>화면을 코드로 만들 때 자주 쓰는 색과 도우미 모음</summary>
static class Ui
{
    // 경고(빨강) / 안전(초록) / 포인트(파랑) 색
    public static readonly Color Red = Color.FromArgb(0xD3, 0x2F, 0x2F);
    public static readonly Color Green = Color.FromArgb(0x2E, 0x9E, 0x4F);
    public static readonly Color GreenText = Color.FromArgb(0x15, 0x80, 0x3D);
    public static readonly Color Blue = Color.FromArgb(0x00, 0x78, 0xD7);
    public static readonly Color Gray = Color.FromArgb(0x7A, 0x7A, 0x7A);

    /// 제목이 붙은 상자(GroupBox)
    public static GroupBox Group(string text) =>
        new() { Text = text, Dock = DockStyle.Fill, Padding = new Padding(10, 8, 10, 10) };

    /// 줄/칸 크기가 정해진 표 모양 배치판. 크기를 "100%"(남는 공간) 또는 숫자(픽셀)로 적는다.
    public static TableLayoutPanel Table(string[] cols, string[] rows)
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = cols.Length, RowCount = rows.Length, Margin = Padding.Empty };
        foreach (var c in cols) t.ColumnStyles.Add(ColStyle(c));
        foreach (var r in rows) t.RowStyles.Add(RowStyleOf(r));
        // 줄 크기가 모두 고정이면 마지막 줄이 남는 공간만큼 늘어나 버린다. 빈 줄 하나를 더해 막는다.
        if (!rows.Any(r => r.EndsWith('%')))
        {
            t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        }
        return t;
    }

    static ColumnStyle ColStyle(string s) => s.EndsWith('%')
        ? new ColumnStyle(SizeType.Percent, float.Parse(s.TrimEnd('%')))
        : new ColumnStyle(SizeType.Absolute, float.Parse(s));

    static RowStyle RowStyleOf(string s) => s.EndsWith('%')
        ? new RowStyle(SizeType.Percent, float.Parse(s.TrimEnd('%')))
        : new RowStyle(SizeType.Absolute, float.Parse(s));

    public static Label Lbl(string text, ContentAlignment align = ContentAlignment.MiddleLeft) =>
        new() { Text = text, Dock = DockStyle.Fill, TextAlign = align, AutoEllipsis = true, Margin = new Padding(0, 0, 6, 0) };

    public static Button Btn(string text) =>
        new() { Text = text, Dock = DockStyle.Fill, UseVisualStyleBackColor = true, Margin = new Padding(4, 2, 0, 2) };

    public static NumericUpDown Num(decimal min, decimal max, decimal step, int decimals) =>
        new() { Minimum = min, Maximum = max, Increment = step, DecimalPlaces = decimals, TextAlign = HorizontalAlignment.Right, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 3, 0, 3) };

    public static TextBox Txt() =>
        new() { Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 3) };

    public static DateTimePicker Date() =>
        new() { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 3, 6, 3) };

    public static ListView List(params (string Title, int Width)[] columns)
    {
        var lv = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, Dock = DockStyle.Fill };
        foreach (var (title, width) in columns) lv.Columns.Add(title, width);
        return lv;
    }
}
