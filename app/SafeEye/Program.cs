namespace SafeEye;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // 예기치 못한 오류가 나면 안내하고 app/crash.log 에 기록한다.
        Application.ThreadException += (_, e) => ReportCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportCrash(e.ExceptionObject as Exception);

        AppSettings.Current = AppSettings.Load();   // 설정 불러오기
        Db.Init();                                  // 기록 DB 준비 (표가 없으면 만든다)

        // 시험용 옵션 (평소에는 쓰지 않는다)
        //   --autostart 영상경로 : 엔진이 준비되면 바로 탐지를 시작한다
        //   --screen 번호        : 시작할 때 보여 줄 화면 (0=영상 탐지, 1=기록 조회, 2=통계, 3=설정)
        string? autoVideo = null;
        int startScreen = 0;
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--autostart") autoVideo = args[i + 1];
            if (args[i] == "--screen" && int.TryParse(args[i + 1], out int n)) startScreen = n;
        }
        Application.Run(new MainForm(autoVideo, startScreen));
    }

    static void ReportCrash(Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AppSettings.CrashLog)!);
            File.AppendAllText(AppSettings.CrashLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch { }
        MessageBox.Show("예기치 못한 오류가 발생했습니다.\n내용은 app\\crash.log 에 기록했습니다.\n\n" + ex?.Message,
                        "SafeEye", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
