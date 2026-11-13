using System.Text.Json;
using System.Text.Json.Serialization;

namespace SafeEye;

/// <summary>
/// 프로그램 설정. app/settings.json 파일에 저장한다.
/// 프로젝트 폴더(safety_project)의 위치도 여기서 찾는다.
/// </summary>
public class AppSettings
{
    // ---- 프로젝트 폴더 -----------------------------------------------------
    /// 실행 파일에서 위로 올라가며 engine/detect_engine.py 가 있는 폴더를 찾는다.
    public static readonly string Root = FindRoot();

    static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "engine", "detect_engine.py")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return AppContext.BaseDirectory;
    }

    public static string SettingsFile => Path.Combine(Root, "app", "settings.json");
    public static string CrashLog => Path.Combine(Root, "app", "crash.log");

    /// 지금 쓰고 있는 설정 (프로그램 어디서든 AppSettings.Current 로 읽는다)
    public static AppSettings Current { get; set; } = new();

    /// 설정이 저장될 때 알린다. (화면들이 값을 다시 읽도록)
    public static event Action? Changed;

    // ---- 설정 항목 ----------------------------------------------------------
    public double ConfThreshold { get; set; } = 0.50;     // 신뢰도 기준: 이 값 이상만 탐지로 인정
    public int AlertSeconds { get; set; } = 3;            // 미착용이 이 시간(초) 이상 이어지면 알림
    public bool SaveCapture { get; set; } = true;         // 알림이 뜰 때 캡처 이미지 저장
    public int CameraIndex { get; set; } = 0;             // 웹캠 번호 (노트북 내장 웹캠은 보통 0)
    public string VideoDir { get; set; } = Path.Combine(Root, "videos");
    public string ModelPath { get; set; } = Path.Combine(Root, "models", "best.pt");
    public string PythonPath { get; set; } = Path.Combine(Root, ".venv", "Scripts", "python.exe");
    public string CaptureDir { get; set; } = Path.Combine(Root, "captures");
    public string DbPath { get; set; } = Path.Combine(Root, "records.db");

    /// 탐지 엔진(Python) 파일 위치 (설정 항목 아님)
    [JsonIgnore]
    public string EngineScript => Path.Combine(Root, "engine", "detect_engine.py");

    // ---- 불러오기 / 저장 ----------------------------------------------------
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile)) ?? new AppSettings();
        }
        catch { /* 파일이 망가졌으면 기본값으로 시작한다 */ }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        Changed?.Invoke();
    }
}
