using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace EnemyScaling.Scripts;

/// <summary>
/// 选人界面倍率面板的宿主与定位（停靠到屏幕正中或四个角落）。
///
/// ── ★ 为什么用"每帧检查尺寸变化"而不是一次性定位（本项目最终结论）────────
/// <para>面板位置曾经反复定不对，根因是**一次性定位遇上布局时序**：</para>
/// <list type="number">
///   <item>面板的父节点 <c>NCharacterSelectScreen</c> 继承 <c>NSubmenu</c>，
///         是 Godot <c>Control</c> 容器 —— 容器会接管子节点位置；</item>
///   <item>用 <c>CallDeferred</c> 一次性定位时，那一刻**视口/面板尺寸往往还是 0**，
///         守卫一 <c>return</c> 就再也没机会重算（日志实测：<c>面板已定位到</c> 一次都没打印）；</item>
///   <item>玩家启动后还会改窗口尺寸（日志实测 <c>Window changed! New size: (2560, 1440)</c>），
///         更加说明位置必须能**跟随尺寸重新计算**，不能只算一次。</item>
/// </list>
/// <para><b>现在的做法</b>：在 <c>_Process</c> 里盯着视口尺寸，
/// 尺寸一变就重算位置 —— 不需要依赖任何"调用的时机刚好正确"，
/// 尺寸一就位就会自动摆好，而且窗口缩放也能跟着适应。</para>
///
/// <para><b>为什么用停靠而不是自由拖动</b>：位置控制被容器布局挡住，
/// 而按钮点击是可靠的（实测 ButtonDown 每次都触发）。</para>
/// </summary>
public sealed partial class DragHost : Control
{
    /// <summary>面板内容宽度（固定值，保证定位可预测）。</summary>
    private const float PanelWidth = 230f;

    /// <summary>离屏幕边缘的留白。</summary>
    private const float Margin = 24f;

    private PanelContainer? _panel;

    /// <summary>是否已经用**真实尺寸**完成过定位（只用于日志）。</summary>
    private bool _loggedFirstPlace;

    /// <summary>是否已经提示过"尺寸未就绪、先用兜底值"（只用于日志）。</summary>
    private bool _loggedFallback;

    /// <summary>由注册时的 setup 回调调用。</summary>
    public void Build()
    {
        MouseFilter = MouseFilterEnum.Ignore;

        // ★★ 关键：先把**宿主自己**撑满屏幕 ★★
        //
        // 锚点是相对**父节点尺寸**算的：实际位置 = 父节点尺寸 × 锚点 + 偏移。
        // 而 DragHost 只是个普通 Control，游戏容器不给它尺寸（实测 Size 恒为 (0,0)），
        // 于是 (0,0) × 0.5 + 偏移 = 偏移 —— 面板被摆到屏幕外。
        // 实测日志正是这个特征：`当前位置 (-138, -65)` 与 `偏移 (-138, -65)` 完全相同。
        //
        // 所以必须自己把宿主铺满视口，让锚点有正确的基准尺寸。
        FullRect();
        Resized += FullRect;

        _panel = CharacterSelectPanel.Build();
        AddChild(_panel);

        // 注入宿主引用：角落按钮被点击时要让宿主重新贴角
        CharacterSelectPanel.HostRef = this;

        // 用 Godot 锚点让布局系统自己把面板摆好（居中 = 锚点 (0.5,0.5) + 退回半个自身尺寸）
        ApplyAnchors();

        // 尺寸变化（改窗口/换分辨率）时重算一次 offset
        Resized += ApplyAnchors;

        // ⚠ 锚点的 offset 依赖面板**真实尺寸**，首次调用时 Size 还是 0，只能用兜底值算。
        //   下一帧尺寸就位后重算一次纠正。
        CallDeferred(nameof(ReapplyAfterLayout));

        Log.Info($"[EnemyScaling] 面板已挂载（{ScalingConfig.Summary} · 停靠 {ScalingConfig.CornerLabel}）");
    }

    /// <summary>把宿主铺满整个视口（用锚点 + 零偏移，布局系统会随之调整尺寸）。</summary>
    private void FullRect()
    {
        AnchorLeft = 0f;
        AnchorRight = 1f;
        AnchorTop = 0f;
        AnchorBottom = 1f;
        OffsetLeft = 0f;
        OffsetRight = 0f;
        OffsetTop = 0f;
        OffsetBottom = 0f;
    }

    /// <summary>
    /// 布局算完后重算锚点（此时 <c>Size</c> 才是真值）。
    ///
    /// <para>不用 <c>_Process</c> / <c>_Input</c> 这类引擎回调 —— 本 mod 的节点是运行时
    /// 由 C# 注册的，那些回调不保证被调用（实测都没触发）。
    /// <c>CallDeferred</c> 则是我们**主动排队**的一次调用，由 Godot 在主线程空闲时执行，
    /// 可靠性只依赖"节点还在树里"，不依赖引擎是否认得我们的脚本类。</para>
    /// </summary>
    public void ReapplyAfterLayout()
    {
        // 同时把宿主再撑一次：容器可能在我们之后才给它尺寸，
        // 这里补一次保证锚点的基准尺寸始终等于视口。
        FullRect();
        ApplyAnchors();

        // 面板尺寸/宿主尺寸都可能要几帧才稳定，多试几次（用我们在树里的状态做条件）
        if (_retryPlace < 10)
        {
            _retryPlace++;
            CallDeferred(nameof(ReapplyAfterLayout));
        }
    }

    private int _retryPlace;

    /// <summary>
    /// 用锚点把面板摆到当前停靠位。
    ///
    /// <para>锚点 <c>(ax, ay)</c> 表示"以父容器尺寸的哪个比例位置为基准"，
    /// <c>offset_left/top</c> 是该基准点到面板左上角的偏移。
    /// 居中 = 锚点 (0.5, 0.5) + offset 去掉自身尺寸的一半。</para>
    /// </summary>
    public void ApplyAnchors()
    {
        if (_panel == null || !GodotObject.IsInstanceValid(_panel))
        {
            return;
        }

        // 面板尺寸：优先用布局算好的真实尺寸；还没算出来时才用兜底值。
        // ⚠ 兜底值算出的 offset 会偏（面板会被摆到屏幕外），所以 Build 里安排了
        //   "布局算完再重算一次"来纠正 —— 见 ReapplyAfterLayout。
        bool sizeReady = _panel.Size.X > 0f && _panel.Size.Y > 0f;
        float w = sizeReady ? _panel.Size.X : PanelWidth + 24f;
        float h = sizeReady ? _panel.Size.Y : 160f;

        float ax, ay, left, top;

        switch (ScalingConfig.Corner)
        {
            case "tl":
                ax = 0f; ay = 0f; left = Margin; top = Margin;
                break;
            case "tr":
                ax = 1f; ay = 0f; left = -w - Margin; top = Margin;
                break;
            case "bl":
                ax = 0f; ay = 1f; left = Margin; top = -h - Margin;
                break;
            case "br":
                ax = 1f; ay = 1f; left = -w - Margin; top = -h - Margin;
                break;
            default:   // center：锚点在正中，再退回半个自身尺寸
                ax = 0.5f; ay = 0.5f; left = -w / 2f; top = -h / 2f;
                break;
        }

        _panel.AnchorLeft = ax;
        _panel.AnchorRight = ax;
        _panel.AnchorTop = ay;
        _panel.AnchorBottom = ay;
        _panel.OffsetLeft = left;
        _panel.OffsetTop = top;
        _panel.OffsetRight = left + w;
        _panel.OffsetBottom = top + h;

        // 只在"尺寸真值首次就绪"时打一条：
        //   之前的日志只在第一次调用时打，而第一次恰好是兜底值，看不出真实尺寸何时到位。
        if (sizeReady && !_loggedFirstPlace)
        {
            _loggedFirstPlace = true;
            Log.Info($"[EnemyScaling] 面板定位完成：停靠 {ScalingConfig.CornerLabel}，"
                     + $"锚点 ({ax},{ay})，偏移 ({left},{top})，"
                     + $"尺寸 {w}×{h}，当前位置 {_panel.Position}");
        }
        else if (!sizeReady && !_loggedFallback)
        {
            _loggedFallback = true;
            Log.Info($"[EnemyScaling] 面板尺寸尚未就绪，先用兜底值 {w}×{h} 定位，"
                     + "等布局算完会再纠正一次");
        }
    }

    /// <summary>角落按钮点击后立刻重新贴锚点。</summary>
    public void Reposition()
    {
        ApplyAnchors();
    }
}
