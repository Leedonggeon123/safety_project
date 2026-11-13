using Microsoft.Data.Sqlite;

namespace SafeEye;

/// <summary>미착용 알림 한 건 (기록 조회 화면의 한 줄)</summary>
public record AlertRow(long Id, long RunId, string AlertedAt, string VideoName, string Kind,
                       double Confidence, int DurationSec, int FrameNo, string? CapturePath);

/// <summary>
/// 기록 저장용 SQLite DB. 표는 2개 (docs의 DB 정의서와 같다)
///   runs   : 영상 한 개를 탐지한 실행 기록
///   alerts : 미착용 알림 기록 (runs 와 1:N)
/// SQL에 값을 직접 이어 붙이지 않고 $매개변수로 넣는다.
/// </summary>
public static class Db
{
    const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

    static SqliteConnection Open()
    {
        var c = new SqliteConnection($"Data Source={AppSettings.Current.DbPath}");
        c.Open();
        Run(c, "PRAGMA foreign_keys = ON");       // 외래키 검사를 켠다 (연결할 때마다 필요)
        return c;
    }

    static void Run(SqliteConnection c, string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// 프로그램을 시작할 때 표와 인덱스를 만든다. 이미 있으면 그대로 쓴다.
    public static void Init()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AppSettings.Current.DbPath)!);
        using var c = Open();
        Run(c, @"CREATE TABLE IF NOT EXISTS runs (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    started_at TEXT NOT NULL,
                    video_name TEXT NOT NULL,
                    video_path TEXT NOT NULL,
                    model_name TEXT NOT NULL,
                    conf_threshold REAL NOT NULL,
                    alert_seconds INTEGER NOT NULL,
                    frames INTEGER NOT NULL,
                    avg_ms INTEGER NOT NULL,
                    helmet_people INTEGER NOT NULL,
                    head_people INTEGER NOT NULL)");
        Run(c, @"CREATE TABLE IF NOT EXISTS alerts (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    run_id INTEGER NOT NULL,
                    alerted_at TEXT NOT NULL,
                    kind TEXT NOT NULL,
                    frame_no INTEGER NOT NULL,
                    confidence REAL NOT NULL,
                    duration_sec INTEGER NOT NULL,
                    capture_path TEXT,
                    CONSTRAINT fk_alerts_run FOREIGN KEY (run_id) REFERENCES runs(id))");
        Run(c, "CREATE INDEX IF NOT EXISTS idx_alerts_time ON alerts(alerted_at)");
        Run(c, "CREATE INDEX IF NOT EXISTS idx_alerts_run ON alerts(run_id)");
    }

    // ---- 저장 ----------------------------------------------------------------

    /// 탐지를 시작할 때 runs 에 한 줄을 넣고, 그 번호(id)를 돌려준다.
    public static long InsertRun(string videoPath, string videoName, string modelName, double conf, int alertSeconds)
    {
        using var c = Open();
        Run(c, @"INSERT INTO runs (started_at, video_name, video_path, model_name, conf_threshold,
                                   alert_seconds, frames, avg_ms, helmet_people, head_people)
                 VALUES ($t, $name, $path, $model, $conf, $sec, 0, 0, 0, 0)",
            ("$t", DateTime.Now.ToString(TimeFormat)), ("$name", videoName),
            ("$path", videoPath), ("$model", modelName), ("$conf", conf), ("$sec", alertSeconds));
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT last_insert_rowid()";
        return (long)cmd.ExecuteScalar()!;
    }

    /// 탐지가 끝나면 결과 요약(프레임 수, 평균 시간, 인원 수)을 채운다.
    public static void UpdateRun(long runId, int frames, int avgMs, int helmetPeople, int headPeople)
    {
        using var c = Open();
        Run(c, @"UPDATE runs SET frames = $frames, avg_ms = $ms, helmet_people = $h, head_people = $d WHERE id = $run",
            ("$frames", frames), ("$ms", avgMs), ("$h", helmetPeople), ("$d", headPeople), ("$run", runId));
    }

    /// 미착용 알림 한 건을 저장한다.
    public static void InsertAlert(long runId, string alertedAt, int frameNo, double confidence, int durationSec, string? capturePath)
    {
        using var c = Open();
        Run(c, @"INSERT INTO alerts (run_id, alerted_at, kind, frame_no, confidence, duration_sec, capture_path)
                 VALUES ($run, $t, 'head', $frame, $conf, $dur, $cap)",
            ("$run", runId), ("$t", alertedAt), ("$frame", frameNo), ("$conf", confidence),
            ("$dur", durationSec), ("$cap", capturePath));
    }

    // ---- 조회 ----------------------------------------------------------------

    static SqliteCommand Cmd(SqliteConnection c, string sql, params (string Name, object Value)[] args)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        return cmd;
    }

    /// 기록 조회: 기간(from 이상, to 미만)과 구분(비우면 전체)으로 찾는다. 최신순, 최대 2000건.
    public static List<AlertRow> QueryAlerts(DateTime from, DateTime to, string? kind)
    {
        var sql = @"SELECT a.id, a.run_id, a.alerted_at, r.video_name, a.kind, a.confidence,
                           a.duration_sec, a.frame_no, a.capture_path
                    FROM alerts a JOIN runs r ON r.id = a.run_id
                    WHERE a.alerted_at >= $from AND a.alerted_at < $to";
        if (!string.IsNullOrEmpty(kind)) sql += " AND a.kind = $kind";
        sql += " ORDER BY a.alerted_at DESC, a.id DESC LIMIT 2000";

        using var c = Open();
        using var cmd = Cmd(c, sql, ("$from", from.ToString(TimeFormat)), ("$to", to.ToString(TimeFormat)));
        if (!string.IsNullOrEmpty(kind)) cmd.Parameters.AddWithValue("$kind", kind);
        var list = new List<AlertRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new AlertRow(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3), r.GetString(4),
                                  r.GetDouble(5), r.GetInt32(6), r.GetInt32(7), r.IsDBNull(8) ? null : r.GetString(8)));
        return list;
    }

    /// 전체 알림 개수 (기록 조회 화면의 "전체 N건")
    public static long CountAlerts()
    {
        using var c = Open();
        using var cmd = Cmd(c, "SELECT COUNT(*) FROM alerts");
        return (long)cmd.ExecuteScalar()!;
    }

    /// 통계 카드: 기간 안의 착용 인원 합, 미착용 인원 합, 평균 처리 시간(ms)
    public static (long Helmet, long Head, double AvgMs) Stats(DateTime from, DateTime to)
    {
        using var c = Open();
        using var cmd = Cmd(c, @"SELECT COALESCE(SUM(helmet_people), 0), COALESCE(SUM(head_people), 0), COALESCE(AVG(avg_ms), 0)
                                 FROM runs WHERE started_at >= $from AND started_at < $to",
                            ("$from", from.ToString(TimeFormat)), ("$to", to.ToString(TimeFormat)));
        using var r = cmd.ExecuteReader();
        r.Read();
        return (r.GetInt64(0), r.GetInt64(1), r.GetDouble(2));
    }

    /// 영상별 미착용 알림 건수
    public static List<(string Video, long Count)> AlertsPerVideo(DateTime from, DateTime to)
    {
        using var c = Open();
        using var cmd = Cmd(c, @"SELECT r.video_name, COUNT(*) FROM alerts a JOIN runs r ON r.id = a.run_id
                                 WHERE a.alerted_at >= $from AND a.alerted_at < $to GROUP BY r.video_name ORDER BY COUNT(*) DESC",
                            ("$from", from.ToString(TimeFormat)), ("$to", to.ToString(TimeFormat)));
        var list = new List<(string, long)>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((r.GetString(0), r.GetInt64(1)));
        return list;
    }

    /// 최근 알림 n건의 신뢰도 (오래된 것부터)
    public static List<double> RecentConfidences(int n)
    {
        using var c = Open();
        using var cmd = Cmd(c, "SELECT confidence FROM alerts ORDER BY alerted_at DESC, id DESC LIMIT $n", ("$n", n));
        var list = new List<double>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetDouble(0));
        list.Reverse();
        return list;
    }
}
