using System.Text;

namespace SafeEye;

/// <summary>
/// 화면 2: 기록 조회
/// 구분(전체/미착용)과 기간으로 미착용 알림 기록을 찾아 목록으로 보여 주고,
/// 선택한 줄의 캡처 이미지와 상세 정보를 오른쪽에 보여 준다.
/// </summary>
public class HistoryPanel : ScreenBase
{
    readonly ToolStripButton _btnRefresh = new("새로 고침");
    readonly ToolStripButton _btnExport = new("내보내기(CSV)");

    readonly ComboBox _cmbKind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 3, 6, 3) };
    readonly DateTimePicker _from = Ui.Date();
    readonly DateTimePicker _to = Ui.Date();
    readonly Button _btnSearch = Ui.Btn("조회");
    readonly Label _lblSummary = Ui.Lbl("", ContentAlignment.MiddleRight);

    readonly ListView _lv = Ui.List(("시각", 150), ("영상", 200), ("구분", 70), ("신뢰도", 70), ("지속", 60));
    readonly PictureBox _pic = new() { Dock = DockStyle.Fill, BackColor = Color.Black, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };
    readonly Label[] _detail = Enumerable.Range(0, 6).Select(_ => Ui.Lbl("", ContentAlignment.MiddleRight)).ToArray();

    List<AlertRow> _rows = [];

    public override ToolStripItem[] ToolItems => [_btnRefresh, new ToolStripSeparator(), _btnExport];

    public HistoryPanel()
    {
        _cmbKind.Items.AddRange(["전체", "미착용"]);
        _cmbKind.SelectedIndex = 0;
        _from.Value = DateTime.Today.AddDays(-7);
        _to.Value = DateTime.Today;

        BuildLayout();

        _btnSearch.Click += (_, _) => Search();
        _btnRefresh.Click += (_, _) => Search();
        _btnExport.Click += (_, _) => ExportCsv();
        _lv.SelectedIndexChanged += (_, _) => ShowSelected();
    }

    void BuildLayout()
    {
        var root = Ui.Table(["100%", "372"], ["70", "100%"]);
        root.Padding = new Padding(8);

        // 위: 검색 조건
        var gbSearch = Ui.Group("검색 조건");
        var s = Ui.Table(["40", "110", "40", "130", "30", "130", "70", "100%", "180"], ["100%"]);
        s.Controls.Add(Ui.Lbl("구분"), 0, 0);
        s.Controls.Add(_cmbKind, 1, 0);
        s.Controls.Add(Ui.Lbl("기간"), 2, 0);
        s.Controls.Add(_from, 3, 0);
        s.Controls.Add(Ui.Lbl("~", ContentAlignment.MiddleCenter), 4, 0);
        s.Controls.Add(_to, 5, 0);
        s.Controls.Add(_btnSearch, 6, 0);
        s.Controls.Add(_lblSummary, 8, 0);
        gbSearch.Controls.Add(s);
        root.Controls.Add(gbSearch, 0, 0);
        root.SetColumnSpan(gbSearch, 2);

        // 왼쪽 아래: 기록 목록
        var gbList = Ui.Group("기록 목록");
        gbList.Controls.Add(_lv);
        root.Controls.Add(gbList, 0, 1);

        // 오른쪽 아래: 캡처 이미지 + 선택한 기록
        var right = Ui.Table(["100%"], ["260", "100%"]);
        var gbPic = Ui.Group("캡처 이미지");
        gbPic.Controls.Add(_pic);
        right.Controls.Add(gbPic, 0, 0);

        var gbDetail = Ui.Group("선택한 기록");
        var d = Ui.Table(["90", "100%"], ["26", "26", "26", "26", "26", "26"]);
        string[] names = ["시각", "영상", "프레임", "신뢰도", "지속 시간", "캡처 파일"];
        for (int i = 0; i < names.Length; i++)
        {
            d.Controls.Add(Ui.Lbl(names[i]), 0, i);
            d.Controls.Add(_detail[i], 1, i);
        }
        gbDetail.Controls.Add(d);
        right.Controls.Add(gbDetail, 0, 1);
        root.Controls.Add(right, 1, 1);

        Controls.Add(root);
    }

    public override void OnShown() => Search();

    /// 조건에 맞는 기록을 DB에서 불러와 목록을 채운다.
    void Search()
    {
        string? kind = _cmbKind.SelectedIndex == 1 ? "head" : null;
        _rows = Db.QueryAlerts(_from.Value.Date, _to.Value.Date.AddDays(1), kind);   // 종료일 하루 전체 포함

        _lv.BeginUpdate();
        _lv.Items.Clear();
        foreach (var r in _rows)
        {
            var item = new ListViewItem(r.AlertedAt);
            item.SubItems.Add(r.VideoName);
            item.SubItems.Add(r.Kind == "head" ? "미착용" : r.Kind);
            item.SubItems.Add(r.Confidence.ToString("0.00"));
            item.SubItems.Add($"{r.DurationSec}초");
            _lv.Items.Add(item);
        }
        _lv.EndUpdate();
        _lblSummary.Text = $"전체 {Db.CountAlerts()}건 중 {_rows.Count}건";
        if (_lv.Items.Count > 0) _lv.Items[0].Selected = true; else ShowSelected();
    }

    /// 목록에서 고른 줄의 캡처 이미지와 상세 정보를 보여 준다.
    void ShowSelected()
    {
        var old = _pic.Image;
        _pic.Image = null;
        old?.Dispose();

        if (_lv.SelectedIndices.Count == 0)
        {
            foreach (var l in _detail) l.Text = "";
            return;
        }
        var r = _rows[_lv.SelectedIndices[0]];
        _detail[0].Text = r.AlertedAt;
        _detail[1].Text = r.VideoName;
        _detail[2].Text = r.FrameNo.ToString("N0");
        _detail[3].Text = r.Confidence.ToString("0.00");
        _detail[4].Text = $"{r.DurationSec}초";
        _detail[5].Text = r.CapturePath == null ? "(없음)" : Path.GetFileName(r.CapturePath);

        if (r.CapturePath != null && File.Exists(r.CapturePath))
        {
            // 파일을 잠그지 않도록 메모리로 읽어서 보여 준다
            using var ms = new MemoryStream(File.ReadAllBytes(r.CapturePath));
            using var tmp = Image.FromStream(ms);
            _pic.Image = new Bitmap(tmp);
        }
    }

    /// 지금 보이는 목록을 CSV 파일로 저장한다. (엑셀에서 열 수 있다)
    void ExportCsv()
    {
        if (_rows.Count == 0) { MessageBox.Show(this, "내보낼 기록이 없습니다.", "SafeEye"); return; }
        using var dlg = new SaveFileDialog { Filter = "CSV 파일|*.csv", FileName = $"기록_{DateTime.Now:yyyyMMdd_HHmm}.csv" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var sb = new StringBuilder("시각,영상,구분,신뢰도,지속(초),프레임,캡처 파일\n");
        foreach (var r in _rows)
            sb.AppendLine($"{r.AlertedAt},{r.VideoName},{(r.Kind == "head" ? "미착용" : r.Kind)},{r.Confidence:0.00},{r.DurationSec},{r.FrameNo},{r.CapturePath}");
        File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));   // BOM 포함: 엑셀에서 한글이 깨지지 않게
        MessageBox.Show(this, "저장했습니다.\n" + dlg.FileName, "SafeEye");
    }
}
