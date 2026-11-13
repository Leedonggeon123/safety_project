using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SafeEye;

/// <summary>
/// Python 탐지 엔진(engine/detect_engine.py)을 '자식 프로그램'으로 실행하고 대화한다.
/// 주고받는 방식: 한 줄에 JSON 하나. (자세한 내용은 detect_engine.py 맨 위 설명)
/// </summary>
public class EngineClient : IDisposable
{
    Process? _proc;
    readonly Queue<string> _errTail = new();      // 엔진이 오류 출력으로 남긴 마지막 글 (문제 원인 확인용)

    public bool Ready { get; private set; }
    public string Device { get; private set; } = "";
    public string LastError { get; private set; } = "";

    /// 엔진이 메시지를 보낼 때마다 알린다. (주의: 화면 스레드가 아니므로 화면에 쓰려면 BeginInvoke 를 쓸 것)
    public event Action<JsonNode>? MessageReceived;
    /// 엔진이 끝났을 때 알린다.
    public event Action? Exited;

    /// 엔진을 실행한다. 준비가 끝나면 {"type":"ready"} 메시지가 온다.
    public bool Start()
    {
        Stop();
        var s = AppSettings.Current;
        if (!File.Exists(s.PythonPath)) { LastError = $"Python을 찾을 수 없습니다: {s.PythonPath}"; return false; }
        if (!File.Exists(s.EngineScript)) { LastError = $"엔진 파일을 찾을 수 없습니다: {s.EngineScript}"; return false; }

        var psi = new ProcessStartInfo
        {
            FileName = s.PythonPath,
            WorkingDirectory = AppSettings.Root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-u");                       // 출력을 바로바로 보내도록
        psi.ArgumentList.Add(s.EngineScript);
        psi.Environment["PYTHONIOENCODING"] = "utf-8";

        try { _proc = Process.Start(psi); }
        catch (Exception e) { LastError = e.Message; return false; }
        if (_proc == null) { LastError = "엔진을 실행하지 못했습니다."; return false; }

        Ready = false;
        var proc = _proc;
        Task.Run(() => ReadOutput(proc));
        Task.Run(() => ReadErrors(proc));
        return true;
    }

    void ReadOutput(Process proc)
    {
        try
        {
            string? line;
            while ((line = proc.StandardOutput.ReadLine()) != null)
            {
                JsonNode? msg;
                try { msg = JsonNode.Parse(line); } catch { continue; }
                if (msg == null) continue;
                if ((string?)msg["type"] == "ready")
                {
                    Ready = true;
                    Device = (string?)msg["device"] ?? "cpu";
                }
                MessageReceived?.Invoke(msg);
            }
        }
        catch { /* 엔진이 끝나면 여기로 온다 */ }
        if (proc == _proc)
        {
            Ready = false;
            Exited?.Invoke();
        }
    }

    // 엔진이 오류 출력으로 내보내는 글을 계속 읽어 줘야 엔진이 멈추지 않는다.
    void ReadErrors(Process proc)
    {
        try
        {
            string? line;
            while ((line = proc.StandardError.ReadLine()) != null)
            {
                lock (_errTail)
                {
                    _errTail.Enqueue(line);
                    while (_errTail.Count > 15) _errTail.Dequeue();
                    LastError = string.Join("\n", _errTail);
                }
            }
        }
        catch { }
    }

    /// 엔진에 명령을 보낸다. 예: Send(new Dictionary&lt;string, object?&gt; { ["cmd"] = "stop" })
    public void Send(Dictionary<string, object?> command)
    {
        if (_proc == null || _proc.HasExited) return;
        try
        {
            lock (_proc)
            {
                _proc.StandardInput.WriteLine(JsonSerializer.Serialize(command));
                _proc.StandardInput.Flush();
            }
        }
        catch { }
    }

    public void Stop()
    {
        var p = _proc;
        _proc = null;
        Ready = false;
        if (p == null) return;
        try
        {
            if (!p.HasExited)
            {
                try { p.StandardInput.WriteLine("{\"cmd\":\"quit\"}"); p.StandardInput.Flush(); } catch { }
                if (!p.WaitForExit(800)) p.Kill(entireProcessTree: true);
            }
        }
        catch { }
        p.Dispose();
    }

    public void Dispose() => Stop();
}
