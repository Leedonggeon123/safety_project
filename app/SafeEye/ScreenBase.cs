namespace SafeEye;

/// <summary>
/// 화면 4개(영상 탐지, 기록 조회, 통계, 설정)의 공통 부모.
/// 각 화면은 자기만의 도구 줄 버튼(ToolItems)을 가진다.
/// </summary>
public abstract class ScreenBase : UserControl
{
    protected ScreenBase()
    {
        Dock = DockStyle.Fill;
        Visible = false;
    }

    /// 이 화면이 보일 때 도구 줄 왼쪽에 놓을 버튼들
    public abstract ToolStripItem[] ToolItems { get; }

    /// 화면이 열릴 때마다 호출된다 (목록을 새로 불러오는 등)
    public virtual void OnShown() { }
}
