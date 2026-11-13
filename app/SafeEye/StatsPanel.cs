namespace SafeEye;

/// <summary>그림(그래프)을 직접 그리는 판. 그리는 방법은 Draw 에 넣어 준다.</summary>
class ChartPanel : Panel
{
    public Action<Graphics, Rectangle>? Draw;

    public ChartPanel()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
        BorderStyle = BorderStyle.FixedSingle;
        Dock = DockStyle.Fill;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        Draw?.Invoke(e.Graphics, ClientRectangle);
    }
}

/// <summary>
/// 화면 3: 통계
/// 기간을 고르면 수치 카드 5개와 그래프 3개(착용/미착용 비율, 영상별 미착용 건수, 신뢰도 추이)를 보여 준다.
/// 그래프는 별도 부품 없이 GDI+ 로 직접 그린다.
/// </summary>
public class StatsPanel : ScreenBase
{
    readonly ToolStripButton _btnRefresh = new("새로 고침");
    readonly DateTimePicker _from = Ui.Date();
    readonly DateTimePicker _to = Ui.Date();
    readonly Button _btnApply = Ui.Btn("적용");
    readonly Label[] _metric = Enumerable.Range(0, 5).Select(_ => new Label { Dock = DockStyle.Fill, Font = new Font("Malgun Gothic", 18f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }).ToArray();
    readonly ChartPanel _pie = new(), _bars = new(), _trend = new();

    // 그래프에 쓸 값 (Refresh 때 DB에서 채운다)
    long _helmet, _head;
    List<(string Video, long Count)> _perVideo = [];
    List<double> _recent = [];

    public override ToolStripItem[] ToolItems => [_btnRefresh];

    public StatsPanel()
    {
        _from.Value = DateTime.Today.AddDays(-7);
        _to.Value = DateTime.Today;
        BuildLayout();

        _pie.Draw = DrawPie;
        _bars.Draw = DrawBars;
        _trend.Draw = DrawTrend;
        _btnApply.Click += (_, _) => Refresh2();
        _btnRefresh.Click += (_, _) => Refresh2();
    }

    void BuildLayout()
    {
        var root = Ui.Table(["100%"], ["70", "84", "290", "100%"]);
        root.Padding = new Padding(8);

        var gbPeriod = Ui.Group("기간");
        var p = Ui.Table(["50", "130", "50", "130", "70", "100%"], ["100%"]);
        p.Controls.Add(Ui.Lbl("시작일"), 0, 0);
        p.Controls.Add(_from, 1, 0);
        p.Controls.Add(Ui.Lbl("종료일"), 2, 0);
        p.Controls.Add(_to, 3, 0);
        p.Controls.Add(_btnApply, 4, 0);
        gbPeriod.Controls.Add(p);
        root.Controls.Add(gbPeriod, 0, 0);

        // 수치 카드 5개
        var cards = Ui.Table(["20%", "20%", "20%", "20%", "20%"], ["100%"]);
        string[] titles = ["전체 탐지", "착용", "미착용", "미착용률", "평균 처리 시간"];
        for (int i = 0; i < 5; i++)
        {
            var box = new Panel { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 0, 8, 6) };
            box.Controls.Add(_metric[i]);
            box.Controls.Add(new Label { Text = titles[i], Dock = DockStyle.Top, Height = 22, Padding = new Padding(8, 4, 0, 0), ForeColor = Color.FromArgb(0x44, 0x44, 0x44) });
            cards.Controls.Add(box, i, 0);
        }
        root.Controls.Add(cards, 0, 1);

        // 그래프 두 개 (비율 / 영상별)
        var charts = Ui.Table(["36%", "64%"], ["100%"]);
        var gbPie = Ui.Group("착용 / 미착용 비율");
        gbPie.Controls.Add(_pie);
        var gbBars = Ui.Group("영상별 미착용 건수");
        gbBars.Controls.Add(_bars);
        charts.Controls.Add(gbPie, 0, 0);
        charts.Controls.Add(gbBars, 1, 0);
        root.Controls.Add(charts, 0, 2);

        // 신뢰도 추이
        var gbTrend = Ui.Group("최근 미착용 알림 신뢰도 추이 (빨강 점선: 신뢰도 기준)");
        gbTrend.Controls.Add(_trend);
        root.Controls.Add(gbTrend, 0, 3);

        Controls.Add(root);
    }

    public override void OnShown() => Refresh2();

    /// DB에서 값을 다시 읽어 수치와 그래프를 새로 그린다.
    void Refresh2()
    {
        DateTime from = _from.Value.Date, to = _to.Value.Date.AddDays(1);
        var (helmet, head, avgMs) = Db.Stats(from, to);
        _helmet = helmet;
        _head = head;
        _perVideo = Db.AlertsPerVideo(from, to);
        _recent = Db.RecentConfidences(20);

        long total = helmet + head;
        _metric[0].Text = total.ToString();
        _metric[1].Text = helmet.ToString();
        _metric[1].ForeColor = Ui.GreenText;
        _metric[2].Text = head.ToString();
        _metric[2].ForeColor = Ui.Red;
        _metric[3].Text = total == 0 ? "-" : $"{100.0 * head / total:0.0} %";
        _metric[4].Text = $"{avgMs:0} ms";

        _pie.Invalidate();
        _bars.Invalidate();
        _trend.Invalidate();
    }

    // ---- 그래프 그리기 -----------------------------------------------------------
    static void NoData(Graphics g, Rectangle r)
    {
        using var f = new Font("Malgun Gothic", 10f);
        TextRenderer.DrawText(g, "데이터가 없습니다", f, r, Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    void DrawPie(Graphics g, Rectangle r)
    {
        long total = _helmet + _head;
        if (total == 0) { NoData(g, r); return; }
        int d = Math.Min(r.Height - 30, r.Width - 130);
        var rect = new Rectangle(r.X + 20, r.Y + (r.Height - d) / 2, d, d);
        float headAngle = 360f * _head / total;
        using (var green = new SolidBrush(Ui.Green)) g.FillPie(green, rect, -90 + headAngle, 360 - headAngle);
        using (var red = new SolidBrush(Color.FromArgb(0xE5, 0x33, 0x2A))) g.FillPie(red, rect, -90, headAngle);

        // 범례
        using var f = new Font("Malgun Gothic", 9f);
        int x = rect.Right + 16, y = r.Y + r.Height / 2 - 24;
        g.FillRectangle(new SolidBrush(Ui.Green), x, y, 10, 10);
        g.DrawString($"착용 {_helmet}", f, Brushes.Black, x + 16, y - 3);
        g.FillRectangle(new SolidBrush(Color.FromArgb(0xE5, 0x33, 0x2A)), x, y + 24, 10, 10);
        g.DrawString($"미착용 {_head}", f, Brushes.Black, x + 16, y + 21);
    }

    void DrawBars(Graphics g, Rectangle r)
    {
        if (_perVideo.Count == 0) { NoData(g, r); return; }
        using var f = new Font("Consolas", 9f);
        var plot = new Rectangle(r.X + 40, r.Y + 20, r.Width - 60, r.Height - 50);
        long max = Math.Max(_perVideo.Max(v => v.Count), 1);
        g.DrawLine(Pens.Gray, plot.Left, plot.Top, plot.Left, plot.Bottom);
        g.DrawLine(Pens.Gray, plot.Left, plot.Bottom, plot.Right, plot.Bottom);
        for (long v = 0; v <= max; v++)                                  // 세로 눈금 (0, 1, 2 ...)
        {
            int y = plot.Bottom - (int)(plot.Height * v / max);
            g.DrawString(v.ToString(), f, Brushes.DimGray, plot.Left - 26, y - 7);
            if (v > 0) g.DrawLine(Pens.Gainsboro, plot.Left + 1, y, plot.Right, y);
        }
        int slot = plot.Width / _perVideo.Count;
        using var red = new SolidBrush(Color.FromArgb(0xE5, 0x33, 0x2A));
        for (int i = 0; i < _perVideo.Count; i++)
        {
            int h = (int)(plot.Height * _perVideo[i].Count / max);
            var bar = new Rectangle(plot.Left + i * slot + slot / 5, plot.Bottom - h, slot * 3 / 5, h);
            g.FillRectangle(red, bar);
            var name = Path.GetFileNameWithoutExtension(_perVideo[i].Video);
            TextRenderer.DrawText(g, name, f, new Rectangle(plot.Left + i * slot, plot.Bottom + 4, slot, 20), Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    void DrawTrend(Graphics g, Rectangle r)
    {
        if (_recent.Count == 0) { NoData(g, r); return; }
        using var f = new Font("Consolas", 9f);
        var plot = new Rectangle(r.X + 46, r.Y + 14, r.Width - 66, r.Height - 34);
        const double lo = 0.5, hi = 1.0;                                 // 세로축: 신뢰도 0.5 ~ 1.0
        int Y(double v) => plot.Bottom - (int)(plot.Height * (Math.Clamp(v, lo, hi) - lo) / (hi - lo));

        g.DrawLine(Pens.Gray, plot.Left, plot.Top, plot.Left, plot.Bottom);
        g.DrawLine(Pens.Gray, plot.Left, plot.Bottom, plot.Right, plot.Bottom);
        foreach (var v in new[] { 0.5, 0.75, 1.0 })
        {
            g.DrawString(v.ToString("0.00"), f, Brushes.DimGray, plot.Left - 40, Y(v) - 7);
            g.DrawLine(Pens.Gainsboro, plot.Left + 1, Y(v), plot.Right, Y(v));
        }
        using (var dash = new Pen(Color.FromArgb(0xE5, 0x33, 0x2A), 1.6f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
            g.DrawLine(dash, plot.Left, Y(AppSettings.Current.ConfThreshold), plot.Right, Y(AppSettings.Current.ConfThreshold));

        var pts = _recent.Select((v, i) => new Point(plot.Left + 30 + (_recent.Count == 1 ? 0 : i * (plot.Width - 60) / (_recent.Count - 1)), Y(v))).ToArray();
        using var line = new Pen(Ui.Blue, 2f);
        if (pts.Length > 1) g.DrawLines(line, pts);
        foreach (var p in pts) g.FillEllipse(new SolidBrush(Ui.Blue), p.X - 4, p.Y - 4, 8, 8);
    }
}
