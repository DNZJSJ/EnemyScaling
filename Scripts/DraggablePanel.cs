using Godot;

namespace EnemyScaling.Scripts;

/// <summary>
/// 可拖动的面板容器。
///
/// <para><b>为什么需要自定义节点而不是直接用内建控件</b>：Godot 的内建 <c>PanelContainer</c>
/// 不处理鼠标拖动。<c>_GuiInput</c> 是 C# 侧的虚方法，**只有继承它的子类**才会收到输入回调，
/// 所以这里建一个最小的 <c>PanelContainer</c> 子类。</para>
///
/// <para><b>事件顺序（决定"能不能拖、滑条还灵不灵"）</b>：
/// Godot 的 GUI 输入是从**最上层控件向内**派发的，谁先消费谁吃掉。
/// 滑条是面板的子节点且更靠内/更靠上，所以：</para>
/// <list type="bullet">
///   <item>鼠标在**滑条**上 → 滑条先消费 → 面板收不到 → <b>滑条正常工作</b>；</item>
///   <item>鼠标在**标题栏或面板其它区域** → 面板消费 → <b>可以拖动</b>。</item>
/// </list>
///
/// <para><b>为什么要用 <c>_Input</c> 兜住鼠标释放</b>：拖动时鼠标常常移出面板，
/// 若只在 <c>_GuiInput</c> 里判断释放，会漏掉"在面板外松手"的情况，
/// 于是拖动状态卡住、面板跟着鼠标乱跑。<c>_Input</c> 是全局的，可以兜住。</para>
/// </summary>
public sealed partial class DraggablePanel : PanelContainer
{
    /// <summary>拖动结束后回调（用于把位置写进配置持久化）。</summary>
    public Action<Vector2>? OnDragFinished;

    /// <summary>标题栏高度：只有这块区域和面板其它空白处能拖，滑条区域让给滑条。</summary>
    private const float TitleBarHeight = 26f;

    private bool _dragging;
    private Vector2 _grabOffset;

    public override void _GuiInput(InputEvent @event)
    {
        // 滑条等子控件已经消费过的事件不会走到这里，所以下面的判断是安全的
        if (@event is InputEventMouseButton mb
            && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                StartDrag(mb);
            }
            else if (_dragging)
            {
                FinishDrag();
            }

            AcceptEvent();
            return;
        }

        if (@event is InputEventMouseMotion mm && _dragging)
        {
            ApplyDrag(mm);
            AcceptEvent();
        }
    }

    public override void _Input(InputEvent @event)
    {
        // 兜底：在面板外松手也要结束拖动（否则会一直跟着鼠标）
        if (_dragging
            && @event is InputEventMouseButton mb
            && mb.ButtonIndex == MouseButton.Left
            && !mb.Pressed)
        {
            FinishDrag();
        }
    }

    private void StartDrag(InputEventMouseButton mb)
    {
        float y = mb.Position.Y;

        // 只在标题栏区域内响应拖动：
        //   · 面板中的滑条已经被控件自己消费掉了，不会进来；
        //   · 但"提示文字"那类非交互区域会落到这里，若允许拖动，用户拖滑条时容易误触。
        // 所以限定在标题栏，行为最可预期。
        if (y > TitleBarHeight)
        {
            return;
        }

        _dragging = true;
        // 记录"鼠标按下点"相对面板左上角的偏移，拖动时保持这个偏移，手感才不会跳
        _grabOffset = GetLocalMousePosition();
    }

    private void ApplyDrag(InputEventMouseMotion mm)
    {
        Vector2 parentSize = GetParent<Control>().Size;

        // 用鼠标在父节点里的位置减去抓取偏移 => 面板新位置
        Vector2 mouseInParent = GetParent<Control>().GetLocalMousePosition();
        Vector2 target = mouseInParent - _grabOffset;

        // 限制在父节点范围内，别拖到看不见的地方
        target.X = Mathf.Clamp(target.X, 0f, Mathf.Max(0f, parentSize.X - Size.X));
        target.Y = Mathf.Clamp(target.Y, 0f, Mathf.Max(0f, parentSize.Y - Size.Y));

        Position = target;
    }

    private void FinishDrag()
    {
        _dragging = false;
        OnDragFinished?.Invoke(Position);
    }
}
