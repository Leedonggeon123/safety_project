using System.Text.Json.Nodes;

namespace SafeEye;

/// <summary>
/// 화면 1: 영상 탐지
/// 영상을 고르고 [탐지 시작]을 누르면 엔진(Python)이 프레임마다 탐지한 결과를 보내오고,
/// 이 화면은 그 결과를 영상 위 박스, 판정 표시, 수치, 최근 알림으로 보여 준다.
/// 미착용 알림이 오면 DB(alerts 표)에 저장한다.
/// </summary>
public class DetectionPanel : ScreenBase
{
    readonly EngineClient _engine;

    // 도구 줄 버튼
    readonly ToolStripButton _btnOpen = new("영상 열기");
    readonly ToolStripButton _btnCamera = new("웹캠 켜기");
    readonly ToolStripButton _btnStart = new("탐지 시작");
    readonly ToolStripButton _btnStop = new("탐지 중지") { Enabled = false };
    readonly ToolStripButton _btnCapture = new("캡처");

    // 화면 요소
    readonly GroupBox _gbVideo = Ui.Group("탐지 영상");
    readonly PictureBox _pic = new() { Dock = DockStyle.Fill, BackColor = Color.Black, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };
    readonly ProgressBar _bar = new() { Dock = DockStyle.Fill, Margin = new Padding(0, 8, 8, 8) };
    readonly Label _lblProgress = Ui.Lbl("");
    readonly Label _lblVerdict = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Malgun Gothic", 22f, FontStyle.Bold), ForeColor = Color.White, BackColor = Ui.Gray, Text = "대기" };
    readonly Label _lblPeople = Ui.Lbl("0", ContentAlignment.MiddleRight);
    readonly Label _lblHelmet = Ui.Lbl("0", ContentAlignment.MiddleRight);
    readonly Label _lblHead = Ui.Lbl("0", ContentAlignment.MiddleRight);
    readonly Label _lblRate = Ui.Lbl("-", ContentAlignment.MiddleRight);
    readonly NumericUpDown _numConf = Ui.Num(0.05m, 0.95m, 0.05m, 2);
    readonly NumericUpDown _numSeconds = Ui.Num(1, 60, 1, 0);
    readonly CheckBox _chkCapture = new() { Text = "알림 시 캡처 저장", Dock = DockStyle.Fill };
    readonly ListView _lvAlerts = Ui.List(("시각", 90), ("구분", 70), ("신뢰도", 70), ("지속", 50));

    // 상태
    string? _videoPath;
    bool _running;
    long _runId;
    bool _loading;                       // 설정 값을 화면에 채우는 중에는 저장하지 않는다

    /// 화면 아래 상태 줄에 보여 줄 글 (처리 속도 등)
    public event Action<string>? InfoChanged;

    public override ToolStripItem[] ToolItems =>
        [_btnOpen, _btnCamera, new ToolStripSeparator(), _btnStart, _btnStop, new ToolStripSeparator(), _btnCapture];

    public DetectionPanel(EngineClient engine)
    {
        _engine = engine;
        BuildLayout();

        _btnOpen.Click += (_, _) => OpenVideo();
        _btnCamera.Click += (_, _) => StartDetection(camera: true);   // 웹캠으로 바로 탐지
        _btnStart.Click += (_, _) => StartDetection();
        _btnStop.Click += (_, _) => _engine.Send(new() { ["cmd"] = "stop" });
        _btnCapture.Click += (_, _) => CaptureNow();

        _numConf.ValueChanged += (_, _) => SaveSettingsFromControls();
        _numSeconds.ValueChanged += (_, _) => SaveSettingsFromControls();
        _chkCapture.CheckedChanged += (_, _) => SaveSettingsFromControls();
        AppSettings.Changed += LoadSettingsToControls;     // 설정 화면에서 값을 바꾸면 여기도 따라 바뀐다
        LoadSettingsToControls();
    }

    // ---- 화면 배치 -------------------------------------------------------------
    void BuildLayout()
    {
        var root = Ui.Table(["100%", "372"], ["100%"]);
        root.Padding = new Padding(8);

        // 왼쪽: 영상 + 진행 줄
        var left = Ui.Table(["100%", "200"], ["100%", "34"]);
        left.Controls.Add(_pic, 0, 0);
        left.SetColumnSpan(_pic, 2);
        left.Controls.Add(_bar, 0, 1);
        left.Controls.Add(_lblProgress, 1, 1);
        _gbVideo.Controls.Add(left);
        root.Controls.Add(_gbVideo, 0, 0);

        // 오른쪽: 판정 + 수치 / 설정 / 최근 알림
        var right = Ui.Table(["100%"], ["200", "130", "100%"]);

        var gbResult = Ui.Group("탐지 결과");
        var result = Ui.Table(["100%", "70"], ["64", "26", "26", "26", "26"]);
        result.Controls.Add(_lblVerdict, 0, 0);
        result.SetColumnSpan(_lblVerdict, 2);
        string[] names = ["감지 인원", "착용", "미착용", "착용률"];
        Label[] values = [_lblPeople, _lblHelmet, _lblHead, _lblRate];
        for (int i = 0; i < 4; i++)
        {
            result.Controls.Add(Ui.Lbl(names[i]), 0, i + 1);
            result.Controls.Add(values[i], 1, i + 1);
        }
        gbResult.Controls.Add(result);
        right.Controls.Add(gbResult, 0, 0);

        var gbSet = Ui.Group("설정");
        var set = Ui.Table(["100%", "80"], ["30", "30", "30"]);
        set.Controls.Add(Ui.Lbl("신뢰도 기준"), 0, 0);
        set.Controls.Add(_numConf, 1, 0);
        set.Controls.Add(Ui.Lbl("알림 지속 시간(초)"), 0, 1);
        set.Controls.Add(_numSeconds, 1, 1);
        set.Controls.Add(_chkCapture, 0, 2);
        set.SetColumnSpan(_chkCapture, 2);
        gbSet.Controls.Add(set);
        right.Controls.Add(gbSet, 0, 1);

        var gbAlerts = Ui.Group("최근 알림");
        gbAlerts.Controls.Add(_lvAlerts);
        right.Controls.Add(gbAlerts, 0, 2);

        root.Controls.Add(right, 1, 0);
        Controls.Add(root);
    }

    // ---- 설정 값 <-> 화면 ------------------------------------------------------
    void LoadSettingsToControls()
    {
        _loading = true;
        var s = AppSettings.Current;
        _numConf.Value = (decimal)Math.Clamp(s.ConfThreshold, 0.05, 0.95);
        _numSeconds.Value = Math.Clamp(s.AlertSeconds, 1, 60);
        _chkCapture.Checked = s.SaveCapture;
        _loading = false;
    }

    void SaveSettingsFromControls()
    {
        if (_loading) return;
        var s = AppSettings.Current;
        s.ConfThreshold = (double)_numConf.Value;
        s.AlertSeconds = (int)_numSeconds.Value;
        s.SaveCapture = _chkCapture.Checked;
        _loading = true;                      // Save() 가 Changed 를 알리므로 되돌아오는 값은 무시한다
        s.Save();
        _loading = false;
    }

    // ---- 동작 ------------------------------------------------------------------
    void OpenVideo()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "탐지할 영상 선택",
            Filter = "영상 파일|*.mp4;*.avi;*.mov;*.mkv;*.wmv|모든 파일|*.*",
            InitialDirectory = Directory.Exists(AppSettings.Current.VideoDir) ? AppSettings.Current.VideoDir : AppSettings.Root,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _videoPath = dlg.FileName;
        _gbVideo.Text = "탐지 영상 - " + Path.GetFileName(_videoPath);
    }

    /// 시험용: 영상을 지정하고 바로 탐지를 시작한다. (--autostart 옵션)
    public void AutoStart(string videoPath)
    {
        _videoPath = videoPath;
        _gbVideo.Text = "탐지 영상 - " + Path.GetFileName(videoPath);
        StartDetection();
    }

    /// camera 가 true 이면 웹캠으로 실시간 탐지하고, 아니면 고른 영상 파일을 탐지한다.
    void StartDetection(bool camera = false)
    {
        if (_running) return;
        var s = AppSettings.Current;

        string source, name;                          // source: 엔진에 줄 입력("camera:0" 또는 영상 경로), name: 기록에 남길 이름
        if (camera)
        {
            source = $"camera:{s.CameraIndex}";
            name = $"웹캠 {s.CameraIndex}";
            _gbVideo.Text = "탐지 영상 - " + name;
        }
        else
        {
            if (_videoPath == null) { OpenVideo(); if (_videoPath == null) return; }
            source = _videoPath;
            name = Path.GetFileName(_videoPath);
        }
        if (!_engine.Ready) { Warn("엔진이 아직 준비되지 않았습니다.\n아래 상태 줄에 '엔진 준비 완료'가 보이면 다시 눌러 주세요."); return; }
        if (!File.Exists(s.ModelPath)) { Warn($"모델 파일이 없습니다.\n{s.ModelPath}\n\n서버에서 학습한 best.pt 를 이 위치에 두세요."); return; }

        // 1) 실행 기록(runs)을 먼저 만들어 번호를 받는다
        _runId = Db.InsertRun(source, name, Path.GetFileName(s.ModelPath), s.ConfThreshold, s.AlertSeconds);

        // 2) 엔진에게 탐지를 시작하라고 알린다
        _engine.Send(new()
        {
            ["cmd"] = "start", ["video"] = source, ["model"] = s.ModelPath,
            ["conf"] = s.ConfThreshold, ["seconds"] = s.AlertSeconds,
            ["capture_dir"] = s.CaptureDir, ["save_capture"] = s.SaveCapture,
        });
        _lvAlerts.Items.Clear();
        SetRunning(true);
    }

    void SetRunning(bool running)
    {
        _running = running;
        _btnStart.Enabled = !running;
        _btnStop.Enabled = running;
        _btnOpen.Enabled = !running;
        _btnCamera.Enabled = !running;
        if (!running) { SetVerdict("대기", Ui.Gray); _bar.Value = 0; }
    }

    void CaptureNow()
    {
        if (_pic.Image == null) { Warn("캡처할 화면이 없습니다."); return; }
        Directory.CreateDirectory(AppSettings.Current.CaptureDir);
        var path = Path.Combine(AppSettings.Current.CaptureDir, $"manual_{DateTime.Now:HHmmss}.png");
        _pic.Image.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        InfoChanged?.Invoke("캡처 저장: " + path);
    }

    // ---- 엔진이 보낸 메시지 처리 (MainForm 이 화면 스레드에서 불러 준다) -----------
    public void HandleMessage(JsonNode msg)
    {
        switch ((string?)msg["type"])
        {
            case "frame": ShowFrame(msg); break;
            case "alert": OnAlert(msg); break;
            case "done": OnDone(msg); break;
            case "warn": InfoChanged?.Invoke((string?)msg["message"] ?? ""); break;   // 예: 웹캠 화면이 너무 어두움
            case "error":
                Warn("탐지 중 오류가 났습니다.\n" + (string?)msg["message"]);
                SetRunning(false);
                break;
        }
    }

    void ShowFrame(JsonNode m)
    {
        var jpg = (string?)m["jpg"];
        if (!string.IsNullOrEmpty(jpg))
        {
            using var ms = new MemoryStream(Convert.FromBase64String(jpg));
            using var tmp = Image.FromStream(ms);
            var old = _pic.Image;
            _pic.Image = new Bitmap(tmp);
            old?.Dispose();
        }
        int helmet = m["helmet"]!.GetValue<int>(), head = m["head"]!.GetValue<int>(), warn = m["warn"]!.GetValue<int>();
        int n = m["n"]!.GetValue<int>(), total = m["total"]!.GetValue<int>(), ms2 = m["ms"]!.GetValue<int>();

        _lblPeople.Text = (helmet + head).ToString();
        _lblHelmet.Text = helmet.ToString();
        _lblHead.Text = head.ToString();
        _lblRate.Text = helmet + head == 0 ? "-" : $"{100.0 * helmet / (helmet + head):0.0} %";
        if (warn > 0) SetVerdict($"미착용 {warn}명", Ui.Red); else SetVerdict("안전", Ui.Green);

        _bar.Maximum = Math.Max(total, 1);
        _bar.Value = total == 0 ? 0 : Math.Clamp(n, 0, _bar.Maximum);       // 웹캠은 전체 길이가 없으므로 진행 줄을 비워 둔다
        _lblProgress.Text = total == 0 ? $"웹캠 · 프레임 {n}" : $"프레임 {n} / {total}";
        InfoChanged?.Invoke($"처리 속도 {ms2} ms/프레임");
    }

    void OnAlert(JsonNode m)
    {
        var time = (string)m["time"]!;
        double conf = m["conf"]!.GetValue<double>();
        int duration = m["duration"]!.GetValue<int>(), frame = m["frame"]!.GetValue<int>();
        var capture = (string?)m["capture"];

        Db.InsertAlert(_runId, time, frame, conf, duration, capture);      // DB에 저장

        var item = new ListViewItem(time[11..]);                           // "HH:mm:ss" 만 보여 준다
        item.SubItems.Add("미착용");
        item.SubItems.Add(conf.ToString("0.00"));
        item.SubItems.Add($"{duration}초");
        item.ForeColor = Ui.Red;
        _lvAlerts.Items.Insert(0, item);                                   // 맨 위가 최신
    }

    /// 탐지가 돌고 있는가
    public bool IsRunning => _running;

    /// 탐지 결과 요약(프레임 수, 평균 시간, 인원 수)을 runs 표에 저장한다. (어느 스레드에서 불러도 된다)
    public void SaveRunResult(JsonNode done) =>
        Db.UpdateRun(_runId, done["frames"]!.GetValue<int>(), done["avg_ms"]!.GetValue<int>(),
                     done["helmet_people"]!.GetValue<int>(), done["head_people"]!.GetValue<int>());

    void OnDone(JsonNode m)
    {
        SaveRunResult(m);
        SetRunning(false);
        int alerts = m["alerts"]!.GetValue<int>();
        bool stopped = m["stopped"]?.GetValue<bool>() ?? false;
        InfoChanged?.Invoke($"{(stopped ? "탐지를 중지했습니다" : "탐지를 마쳤습니다")} (착용 {m["helmet_people"]}명, 미착용 {m["head_people"]}명, 알림 {alerts}건)");
    }

    /// 엔진이 갑자기 끝났을 때
    public void OnEngineExited()
    {
        if (_running) { SetRunning(false); Warn("엔진이 종료되어 탐지가 멈췄습니다.\n도구 > 엔진 다시 시작 을 눌러 주세요."); }
    }

    void SetVerdict(string text, Color color)
    {
        _lblVerdict.Text = text;
        _lblVerdict.BackColor = color;
    }

    void Warn(string text) => MessageBox.Show(this, text, "SafeEye", MessageBoxButtons.OK, MessageBoxIcon.Information);
}
