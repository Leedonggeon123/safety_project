namespace SafeEye;

/// <summary>
/// 화면 4: 설정
/// 신뢰도 기준, 알림 지속 시간, 파일/폴더 위치를 바꾸고 [저장]을 누르면 app/settings.json 에 저장된다.
/// </summary>
public class SettingsPanel : ScreenBase
{
    readonly MainForm _main;

    readonly ToolStripButton _btnSaveTool = new("설정 저장");
    readonly TrackBar _track = new() { Minimum = 5, Maximum = 95, TickFrequency = 10, SmallChange = 5, LargeChange = 10, Dock = DockStyle.Fill, TickStyle = TickStyle.None };
    readonly NumericUpDown _numConf = Ui.Num(0.05m, 0.95m, 0.05m, 2);
    readonly NumericUpDown _numSeconds = Ui.Num(1, 60, 1, 0);
    readonly NumericUpDown _numCamera = Ui.Num(0, 9, 1, 0);
    readonly CheckBox _chkCapture = new() { Text = "알림이 뜰 때 캡처 이미지를 자동 저장", Dock = DockStyle.Fill };
    readonly TextBox _txtVideo = Ui.Txt(), _txtModel = Ui.Txt(), _txtPython = Ui.Txt(), _txtCapture = Ui.Txt(), _txtDb = Ui.Txt();
    readonly Label _lblEngine = Ui.Lbl("");
    bool _loading;

    public override ToolStripItem[] ToolItems => [_btnSaveTool];

    public SettingsPanel(MainForm main)
    {
        _main = main;
        BuildLayout();

        _btnSaveTool.Click += (_, _) => Save();
        // 슬라이더와 숫자 칸을 서로 맞춘다
        _track.ValueChanged += (_, _) => { if (!_loading) _numConf.Value = _track.Value / 100m; };
        _numConf.ValueChanged += (_, _) => _track.Value = (int)Math.Round(_numConf.Value * 100);

        AppSettings.Changed += LoadValues;
        LoadValues();
    }

    void BuildLayout()
    {
        var root = Ui.Table(["50%", "50%"], ["170", "130", "130", "100%", "40"]);
        root.Padding = new Padding(8);

        // 탐지 설정
        var gbDetect = Ui.Group("탐지");
        var d = Ui.Table(["130", "100%", "70"], ["34", "34", "30", "30"]);
        d.Controls.Add(Ui.Lbl("신뢰도 기준"), 0, 0);
        d.Controls.Add(_track, 1, 0);
        d.Controls.Add(_numConf, 2, 0);
        d.Controls.Add(Ui.Lbl("알림 지속 시간(초)"), 0, 1);
        d.Controls.Add(_numSeconds, 2, 1);
        d.Controls.Add(_chkCapture, 0, 2);
        d.SetColumnSpan(_chkCapture, 3);
        d.Controls.Add(Ui.Lbl("미착용이 이 시간 이상 이어질 때만 경고를 표시합니다."), 0, 3);
        d.SetColumnSpan(d.GetControlFromPosition(0, 3)!, 3);
        gbDetect.Controls.Add(d);
        root.Controls.Add(gbDetect, 0, 0);

        // 영상 설정
        var gbVideo = Ui.Group("영상");
        var videoRows = (TableLayoutPanel)PathRows(("기본 영상 폴더", _txtVideo, BrowseFolder), ("탐지 모델 파일", _txtModel, BrowseModel));
        videoRows.Controls.Add(Ui.Lbl("웹캠 번호 (보통 0)"), 0, 2);        // 세 번째 줄: 웹캠 번호
        videoRows.Controls.Add(_numCamera, 2, 2);
        videoRows.RowStyles[2] = new RowStyle(SizeType.Absolute, 34);        // 빈 줄(남는 공간)이던 자리를 한 줄로 쓰고
        videoRows.RowCount = 4;
        videoRows.RowStyles.Add(new RowStyle(SizeType.Percent, 100));        // 남는 공간용 빈 줄을 다시 하나 둔다
        gbVideo.Controls.Add(videoRows);
        root.Controls.Add(gbVideo, 1, 0);

        // 탐지 엔진
        var gbEngine = Ui.Group("탐지 엔진");
        var e = Ui.Table(["110", "100%", "90", "130"], ["32", "32"]);
        e.Controls.Add(Ui.Lbl("Python 경로"), 0, 0);
        e.Controls.Add(_txtPython, 1, 0);
        e.SetColumnSpan(_txtPython, 2);
        var btnBrowse = Ui.Btn("찾아보기...");
        btnBrowse.Click += (_, _) => BrowseFile(_txtPython, "Python|python.exe|모든 파일|*.*");
        e.Controls.Add(btnBrowse, 3, 0);
        e.Controls.Add(Ui.Lbl("상태"), 0, 1);
        e.Controls.Add(_lblEngine, 1, 1);
        var btnRestart = Ui.Btn("엔진 다시 시작");
        btnRestart.Click += (_, _) => { Save(); _main.RestartEngine(); };
        e.Controls.Add(btnRestart, 3, 1);
        gbEngine.Controls.Add(e);
        root.Controls.Add(gbEngine, 0, 1);

        // 저장 위치
        var gbSave = Ui.Group("저장 위치");
        gbSave.Controls.Add(PathRows(("캡처 이미지 폴더", _txtCapture, BrowseFolder), ("기록 데이터베이스", _txtDb, BrowseDb)));
        root.Controls.Add(gbSave, 1, 1);

        // 저장 / 취소
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var btnCancel = new Button { Text = "취소", Width = 90, Height = 30 };
        var btnSave = new Button { Text = "저장", Width = 90, Height = 30 };
        btnCancel.Click += (_, _) => LoadValues();
        btnSave.Click += (_, _) => Save();
        buttons.Controls.Add(btnCancel);
        buttons.Controls.Add(btnSave);
        root.Controls.Add(buttons, 0, 4);
        root.SetColumnSpan(buttons, 2);

        Controls.Add(root);
    }

    /// "이름 [글상자] [찾아보기...]" 줄 두 개를 만든다.
    static Control PathRows(params (string Label, TextBox Box, Action<TextBox> Browse)[] rows)
    {
        var t = Ui.Table(["120", "100%", "90"], ["34", "34"]);
        for (int i = 0; i < rows.Length; i++)
        {
            var (label, box, browse) = rows[i];
            t.Controls.Add(Ui.Lbl(label), 0, i);
            t.Controls.Add(box, 1, i);
            var b = Ui.Btn("찾아보기...");
            b.Click += (_, _) => browse(box);
            t.Controls.Add(b, 2, i);
        }
        return t;
    }

    static void BrowseFolder(TextBox box)
    {
        using var dlg = new FolderBrowserDialog { SelectedPath = box.Text };
        if (dlg.ShowDialog() == DialogResult.OK) box.Text = dlg.SelectedPath;
    }

    static void BrowseModel(TextBox box) => BrowseFile(box, "모델 파일|*.pt|모든 파일|*.*");

    static void BrowseDb(TextBox box) => BrowseFile(box, "SQLite DB|*.db|모든 파일|*.*", checkExists: false);

    static void BrowseFile(TextBox box, string filter, bool checkExists = true)
    {
        using var dlg = new OpenFileDialog { Filter = filter, FileName = box.Text, CheckFileExists = checkExists };
        if (dlg.ShowDialog() == DialogResult.OK) box.Text = dlg.FileName;
    }

    public override void OnShown() => UpdateEngineLabel();

    /// 엔진 상태 글 (MainForm 이 상태가 바뀔 때 불러 준다)
    public void UpdateEngineLabel()
    {
        _lblEngine.Text = _main.EngineStatusText;
        _lblEngine.ForeColor = _main.EngineIsReady ? Ui.GreenText : Color.Black;
    }

    /// 저장된 설정 값을 화면에 채운다. ([취소] 와 같은 동작)
    void LoadValues()
    {
        _loading = true;
        var s = AppSettings.Current;
        _numConf.Value = (decimal)Math.Clamp(s.ConfThreshold, 0.05, 0.95);
        _numSeconds.Value = Math.Clamp(s.AlertSeconds, 1, 60);
        _chkCapture.Checked = s.SaveCapture;
        _numCamera.Value = Math.Clamp(s.CameraIndex, 0, 9);
        _txtVideo.Text = s.VideoDir;
        _txtModel.Text = s.ModelPath;
        _txtPython.Text = s.PythonPath;
        _txtCapture.Text = s.CaptureDir;
        _txtDb.Text = s.DbPath;
        _loading = false;
    }

    /// 화면의 값을 설정에 담아 settings.json 에 저장한다.
    void Save()
    {
        var s = AppSettings.Current;
        bool pythonChanged = !string.Equals(s.PythonPath, _txtPython.Text, StringComparison.OrdinalIgnoreCase);
        bool dbChanged = !string.Equals(s.DbPath, _txtDb.Text, StringComparison.OrdinalIgnoreCase);

        s.ConfThreshold = (double)_numConf.Value;
        s.AlertSeconds = (int)_numSeconds.Value;
        s.SaveCapture = _chkCapture.Checked;
        s.CameraIndex = (int)_numCamera.Value;
        s.VideoDir = _txtVideo.Text.Trim();
        s.ModelPath = _txtModel.Text.Trim();
        s.PythonPath = _txtPython.Text.Trim();
        s.CaptureDir = _txtCapture.Text.Trim();
        s.DbPath = _txtDb.Text.Trim();
        s.Save();

        if (dbChanged) Db.Init();                       // 새 DB 파일이면 표를 만든다
        _main.OnSettingsSaved(pythonChanged);
        MessageBox.Show(this, "설정을 저장했습니다.", "SafeEye");
    }
}
