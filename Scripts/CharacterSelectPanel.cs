using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace EnemyScaling.Scripts;

/// <summary>
/// 选人界面右上角的倍率面板：两条滑动条（生命值 / 伤害），范围 100%–500%。
///
/// <para><b>可拖动</b>：根节点是 <see cref="DraggablePanel"/>，按住**标题栏**即可拖；
/// 拖完位置会存进配置，下次进界面保持原样（见 <see cref="ScalingConfig.SetPanelPosition"/>）。</para>
///
/// <para><b>为什么用代码建控件而不是 .tscn</b>：本 mod 没有 PCK（<c>has_pck: false</c>），
/// 全部用 Godot 内建控件（<c>PanelContainer</c> / <c>VBoxContainer</c> / <c>HSlider</c>）代码构建，
/// 不依赖任何场景资源。</para>
///
/// <para><b>挂载方式</b>：由 <c>CharacterSelectPanelPatch</c> 在
/// <c>NCharacterSelectScreen._Ready</c> 之后把它加为子节点。</para>
/// </summary>
internal static class CharacterSelectPanel
{
    private const string NodeName = "EnemyScalingPanel";

    /// <summary>在选人界面根节点下建面板；已存在则跳过（防止重复进入界面时叠加）。</summary>
    public static void Attach(Node host)
    {
        try
        {
            if (host.GetNodeOrNull<Node>(NodeName) != null)
            {
                return;
            }

            var root = new DraggablePanel
            {
                Name = NodeName,
                // 位置先放 (0,0)，等布局算完尺寸后再定位（见文件末尾）
                Position = new Vector2(0, 0),
                MouseFilter = Control.MouseFilterEnum.Stop,
            };

            var style = new StyleBoxFlat
            {
                BgColor = new Color(0.08f, 0.07f, 0.06f, 0.88f),
                BorderColor = new Color(0.55f, 0.45f, 0.25f, 1f),
                CornerRadiusTopLeft = 6,
                CornerRadiusTopRight = 6,
                CornerRadiusBottomLeft = 6,
                CornerRadiusBottomRight = 6,
                ContentMarginLeft = 12,
                ContentMarginRight = 12,
                ContentMarginTop = 0,
                ContentMarginBottom = 10,
            };
            style.SetBorderWidthAll(1);
            root.AddThemeStyleboxOverride("panel", style);

            var box = new VBoxContainer { Name = "Box" };
            box.AddThemeConstantOverride("separation", 6);
            root.AddChild(box);

            // ── 标题栏：就是拖动把手 ────────────────────────────────────────
            // 高度与 DraggablePanel.TitleBarHeight 对应，只有这一条能拖。
            // 这样滑条区域不会被拖动抢走输入，两者互不干扰。
            box.AddChild(BuildTitleBar());

            AddSlider(box, "生命值", ScalingConfig.HpPercent, ScalingConfig.SetHpPercent);
            AddSlider(box, "伤害", ScalingConfig.DamagePercent, ScalingConfig.SetDamagePercent);

            var hint = new Label
            {
                Text = "100% = 原始数值 · 改动立即保存",
                AutowrapMode = TextServer.AutowrapMode.Word,
                CustomMinimumSize = new Vector2(240, 0),
            };
            hint.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
            hint.AddThemeFontSizeOverride("font_size", 11);
            box.AddChild(hint);

            // 拖动结束就把位置记下来，下次进来保持原样
            root.OnDragFinished = pos => ScalingConfig.SetPanelPosition(pos.X, pos.Y);

            host.AddChild(root);

            // 放到右上角：等父节点算完尺寸再定位（此时 size 才有效）
            // 定位：优先用玩家上次拖动后保存的位置，否则默认右上角。
            // 必须 CallDeferred —— 此刻 panel.Size 还没算出来，直接定位会算错边界。
            if (ScalingConfig.HasPanelPosition)
            {
                var saved = new Vector2(ScalingConfig.PanelX, ScalingConfig.PanelY);
                root.CallDeferred(Control.MethodName.SetPosition, saved);
            }
            else
            {
                root.CallDeferred(Control.MethodName.SetPosition, new Vector2(
                    host is Control c ? c.Size.X - 280 : 100, 16));
            }

            Log.Info($"[EnemyScaling] 已在选人界面挂载倍率面板（{ScalingConfig.Summary}）"
                     + (ScalingConfig.HasPanelPosition ? "（使用上次保存的位置）" : "（默认右上角，可拖动）"));
        }
        catch (Exception ex)
        {
            // 面板挂不上不该拦住选人流程
            Log.Error($"[EnemyScaling] 挂载选人界面面板失败：{ex}");
        }
    }

    /// <summary>
    /// 标题栏：拖动把手。
    ///
    /// <para>高度必须与 <see cref="DraggablePanel.TitleBarHeight"/> 一致 ——
    /// 那边按 Y 坐标判断"是否在标题栏内"，两边不一致会出现
    /// "看着能拖却拖不动"或"拖到滑条上被抢走"的错位。</para>
    ///
    /// <para>用一个有背景色的小 PanelContainer 让把手**看得出来**：
    /// 玩家不知道面板能拖，功能就等于没有。</para>
    /// </summary>
    private static Control BuildTitleBar()
    {
        var bar = new PanelContainer
        {
            Name = "TitleBar",
            CustomMinimumSize = new Vector2(0, 22),
            // 让鼠标事件冒泡到父面板，拖动由 DraggablePanel 统一处理
            MouseFilter = Control.MouseFilterEnum.Pass,
        };

        var barStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.22f, 0.18f, 0.10f, 0.95f),
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 3,
            ContentMarginBottom = 3,
        };
        bar.AddThemeStyleboxOverride("panel", barStyle);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        bar.AddChild(row);

        var grip = new Label
        {
            Text = "⠿",
            TooltipText = "按住这里拖动面板",
        };
        grip.AddThemeColorOverride("font_color", new Color(0.85f, 0.75f, 0.45f));
        row.AddChild(grip);

        var title = new Label
        {
            Text = "敌人数值倍率",
            TooltipText = "按住标题栏拖动 · 拖动后会记住位置",
        };
        title.AddThemeColorOverride("font_color", new Color(0.95f, 0.85f, 0.55f));
        row.AddChild(title);

        return bar;
    }

    /// <summary>一行 = 名称 + 滑条 + 当前百分比。</summary>
    private static void AddSlider(Node parent, string label, int initial, Action<int> onChanged)
    {
        var row = new HBoxContainer { Name = label + "Row" };
        row.AddThemeConstantOverride("separation", 8);

        var name = new Label { Text = label, CustomMinimumSize = new Vector2(52, 0) };
        row.AddChild(name);

        var slider = new HSlider
        {
            MinValue = ScalingConfig.MinPercent,
            MaxValue = ScalingConfig.MaxPercent,
            Step = ScalingConfig.StepPercent,
            Value = initial,
            CustomMinimumSize = new Vector2(120, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            // 让键盘/手柄也能调
            FocusMode = Control.FocusModeEnum.All,
        };
        row.AddChild(slider);

        var value = new Label
        {
            Text = $"{initial}%",
            CustomMinimumSize = new Vector2(44, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        value.AddThemeColorOverride("font_color", new Color(0.95f, 0.85f, 0.55f));
        row.AddChild(value);

        // 拖动时即时写回配置（配置本身很小，同步保存没有可感的卡顿）
        slider.ValueChanged += v =>
        {
            int percent = (int)v;
            value.Text = $"{percent}%";
            onChanged(percent);
        };

        parent.AddChild(row);
    }
}
