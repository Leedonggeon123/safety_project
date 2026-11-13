using System.Text.Json.Nodes;

namespace SafeEye;

/// <summary>
/// 메인 창: 메뉴 줄 + 도구 줄 + 화면 4개(영상 탐지·기록 조회·통계·설정) + 상태 줄
/// 오른쪽 위의 버튼으로 화면을 바꾼다. 탐지 엔진(Python)은 창이 열릴 때 함께 실행된다.
/// </summary>
public class MainForm : Form
{
    readonly EngineClient _engine = new();

    // 창을 이루는 부품들
    readonly ToolStrip _tool = new() { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(4, 4, 4, 4) };
    readonly StatusStrip _status = new();
    readonly ToolStripStatusLabel _lblEngine = new("엔진 준비 중…") { BorderSides = ToolStripStatusLabelBorderSides.Right };
    readonly ToolStripStatusLabel _lblModel = new() { BorderSides = ToolStripStatusLabelBorderSides.Right };
    readonly ToolStripStatusLabel _lblInfo = new() { Spring = true, TextAlign = ContentAlignment.MiddleRight };
    readonly Panel _content = new() { Dock = DockStyle.Fill };

    // 화면 4개
    readonly DetectionPanel _detect;
    readonly SettingsPanel _settings;
    readonly ScreenBase[] _screens;
    readonly string[] _names = ["영상 탐지", "기록 조회", "통계", "설정"];
    readonly ToolStripButton[] _nav;                  // 오른쪽 위 화면 이동 버튼
    readonly List<ToolStripItem> _leftItems = [];     // 지금 화면의 도구 버튼 (왼쪽)
    int _current = -1;

    /// 시험용: 프로그램을 --autostart 영상경로 로 실행하면 엔진이 준비되는 대로 탐지를 바로 시작한다.
    readonly string? _autoStartVideo;

    public string EngineStatusText { get; private set; } = "엔진 준비 중…";
    public bool EngineIsReady => _engine.Ready;

    readonly int _startScreen;

    public MainForm(string? autoStartVideo = null, int startScreen = 0)
    {
        _autoStartVideo = autoStartVideo;
        _startScreen = startScreen;
        Text = "SafeEye - 안전모 착용 탐지";
        ClientSize = new Size(1280, 780);
        MinimumSize = new Size(1100, 700);
        StartPosition = FormStartPosition.CenterScreen;

        // 화면 만들기
        _detect = new DetectionPanel(_engine);
        _settings = new SettingsPanel(this);
        _screens = [_detect, new HistoryPanel(), new StatsPanel(), _settings];
        foreach (var s in _screens) _content.Controls.Add(s);
        _detect.InfoChanged += text => _lblInfo.Text = text;

        // 오른쪽 위 화면 이동 버튼 (오른쪽 정렬 항목은 먼저 넣은 것이 맨 오른쪽에 놓이므로 거꾸로 넣는다)
        _nav = new ToolStripButton[_names.Length];
        for (int i = _names.Length - 1; i >= 0; i--)
        {
            int index = i;
            _nav[i] = new ToolStripButton(_names[i]) { Alignment = ToolStripItemAlignment.Right };
            _nav[i].Click += (_, _) => ShowScreen(index);
            _tool.Items.Add(_nav[i]);
        }

        _status.Items.AddRange([_lblEngine, _lblModel, _lblInfo]);

        // 창에 올리기 (아래에서 위로: 가운데 → 상태 줄 → 도구 줄 → 메뉴 줄 순서로 넣어야 배치가 맞는다)
        var menu = BuildMenu();
        Controls.Add(_content);
        Controls.Add(_status);
        Controls.Add(_tool);
        Controls.Add(menu);
        MainMenuStrip = menu;

        // 엔진이 보낸 메시지는 다른 스레드에서 오므로, 화면 스레드로 옮겨서 처리한다.
        _engine.MessageReceived += msg => OnUi(() => OnEngineMessage(msg));
        _engine.Exited += () => OnUi(OnEngineExited);

        Shown += (_, _) => { ShowScreen(Math.Clamp(_startScreen, 0, _screens.Length - 1)); StartEngine(); };
        FormClosing += (_, _) => { StopRunningDetection(); _engine.Dispose(); };
    }

    MenuStrip BuildMenu()
    {
        var menu = new MenuStrip();

        var file = new ToolStripMenuItem("파일(&F)");
        file.DropDownItems.Add("종료", null, (_, _) => Close());

        var view = new ToolStripMenuItem("보기(&V)");
        for (int i = 0; i < _names.Length; i++)
        {
            int index = i;
            view.DropDownItems.Add(_names[i], null, (_, _) => ShowScreen(index));
        }

        var tools = new ToolStripMenuItem("도구(&T)");
        tools.DropDownItems.Add("엔진 다시 시작", null, (_, _) => RestartEngine());

        var help = new ToolStripMenuItem("도움말(&H)");
        help.DropDownItems.Add("SafeEye 정보", null, (_, _) => MessageBox.Show(this,
            "SafeEye - 안전모 착용 탐지\n\n영상에서 안전모 착용/미착용을 탐지하고,\n미착용이 이어지면 경고와 기록을 남깁니다.\n\n개발: 이동건 (개인 프로젝트)", "SafeEye 정보"));

        menu.Items.AddRange([file, view, tools, help]);
        return menu;
    }

    // ---- 화면 바꾸기 -------------------------------------------------------------
    void ShowScreen(int index)
    {
        if (index == _current) return;
        _current = index;
        for (int i = 0; i < _screens.Length; i++)
        {
            _screens[i].Visible = i == index;
            _nav[i].Checked = i == index;               // 현재 화면 버튼은 눌린 모양
        }

        // 도구 줄 왼쪽 버튼을 이 화면의 것으로 바꾼다
        foreach (var item in _leftItems) _tool.Items.Remove(item);
        _leftItems.Clear();
        int pos = 0;
        foreach (var item in _screens[index].ToolItems)
        {
            _tool.Items.Insert(pos++, item);
            _leftItems.Add(item);
        }
        _screens[index].OnShown();
    }

    /// 창을 닫을 때 탐지가 돌고 있으면(웹캠은 끝이 없다) 먼저 멈추고, 결과를 저장한 뒤 닫는다.
    void StopRunningDetection()
    {
        if (!_detect.IsRunning) return;
        using var finished = new ManualResetEventSlim();
        void OnMessage(JsonNode m)
        {
            if ((string?)m["type"] != "done") return;
            _detect.SaveRunResult(m);          // 화면이 닫히는 중이라 화면 스레드를 거치지 않고 바로 저장한다
            finished.Set();
        }
        _engine.MessageReceived += OnMessage;
        _engine.Send(new() { ["cmd"] = "stop" });
        finished.Wait(3000);                   // 최대 3초만 기다린다
        _engine.MessageReceived -= OnMessage;
    }

    // ---- 엔진 관리 ---------------------------------------------------------------
    void OnUi(Action action)
    {
        if (IsHandleCreated && !IsDisposed) BeginInvoke(action);
    }

    void StartEngine()
    {
        SetEngineStatus("엔진 준비 중… (처음에는 오래 걸릴 수 있음)", Color.Black);
        if (!_engine.Start())
            SetEngineStatus("엔진을 시작하지 못했습니다: " + _engine.LastError, Ui.Red);
        UpdateModelLabel();
    }

    public void RestartEngine() => StartEngine();

    /// 설정 화면에서 [저장]을 눌렀을 때
    public void OnSettingsSaved(bool pythonChanged)
    {
        UpdateModelLabel();
        if (pythonChanged) RestartEngine();
    }

    void OnEngineMessage(JsonNode msg)
    {
        if ((string?)msg["type"] == "ready")
        {
            SetEngineStatus($"엔진 준비 완료 ({_engine.Device})", Ui.GreenText);
            if (_autoStartVideo != null) _detect.AutoStart(_autoStartVideo);
        }
        else
        {
            _detect.HandleMessage(msg);
        }
    }

    void OnEngineExited()
    {
        SetEngineStatus("엔진이 종료되었습니다 (도구 > 엔진 다시 시작)", Ui.Red);
        _detect.OnEngineExited();
    }

    void SetEngineStatus(string text, Color color)
    {
        EngineStatusText = text;
        _lblEngine.Text = text;
        _lblEngine.ForeColor = color;
        _settings.UpdateEngineLabel();
    }

    void UpdateModelLabel()
    {
        var path = AppSettings.Current.ModelPath;
        _lblModel.Text = File.Exists(path) ? "모델: " + Path.GetFileName(path) : "모델 없음 (설정에서 지정)";
    }
}
