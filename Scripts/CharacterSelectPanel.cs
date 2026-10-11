using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace EnemyScaling.Scripts;

/// <summary>
/// 选人界面的倍率面板：生命值 / 伤害 两条滑动条 + 标题栏上的四个角落按钮。
///
/// <para><b>挂载方式</b>：RitsuLib 官方的节点附加机制（指南
/// <c>RitsuLib/03 - 模组工具/07 - 节点附加</c>），注册的是 <see cref="DragHost"/>。</para>
///
/// <para><b>为什么用角落按钮而不是自由拖动</b>：位置控制被游戏 UI 架构挡住
/// （容器覆盖子节点位置），而按钮点击的信号由引擎保证派发 —— 详见 <see cref="DragHost"/>。</para>
///
/// <para><b>为什么用代码建控件</b>：本 mod 没有 PCK（<c>has_pck: false</c>），
/// 全部用 Godot 内建控件构建，不依赖任何场景资源。</para>
/// </summary>
internal static class CharacterSelectPanel
{
    /// <summary>面板内容宽度（固定值，让定位可预测）。</summary>
    public const float PanelWidth = 230f;

    /// <summary>"选中的角落"视觉：亮色边框，一眼看出当前停靠在哪。</summary>
    private static StyleBoxFlat ActiveCornerStyle() => new()
    {
        BgColor = new Color(0.45f, 0.36f, 0.16f, 0.95f),
        CornerRadiusTopLeft = 4,
        CornerRadiusTopRight = 4,
        CornerRadiusBottomLeft = 4,
        CornerRadiusBottomRight = 4,
        ContentMarginLeft = 6,
        ContentMarginRight = 6,
        ContentMarginTop = 2,
        ContentMarginBottom = 2,
    };

    /// <summary>未选中的角落视觉。</summary>
    private static StyleBoxFlat IdleCornerStyle() => new()
    {
        BgColor = new Color(0.14f, 0.12f, 0.08f, 0.85f),
        CornerRadiusTopLeft = 4,
        CornerRadiusTopRight = 4,
        CornerRadiusBottomLeft = 4,
        CornerRadiusBottomRight = 4,
        ContentMarginLeft = 6,
        ContentMarginRight = 6,
        ContentMarginTop = 2,
        ContentMarginBottom = 2,
    };

    /// <summary>构建整个面板。</summary>
    public static PanelContainer Build()
    {
        var panel = new PanelContainer
        {
            Name = "EnemyScalingPanel",
            MouseFilter = Control.MouseFilterEnum.Stop,
            CustomMinimumSize = new Vector2(PanelWidth + 24f, 0f),
        };
        panel.AddThemeStyleboxOverride("panel", BuildPanelStyle());

        var box = new VBoxContainer { Name = "Box" };
        box.AddThemeConstantOverride("separation", 6);
        panel.AddChild(box);

        box.AddChild(BuildTitleBar());
        box.AddChild(BuildCornerRow());

        AddSlider(box, "生命值", ScalingConfig.HpPercent,
            ScalingConfig.MinPercent, ScalingConfig.MaxPercent, ScalingConfig.StepPercent,
            ScalingConfig.SetHpPercent);

        AddSlider(box, "伤害", ScalingConfig.DamagePercent,
            ScalingConfig.MinPercent, ScalingConfig.MaxPercent, ScalingConfig.StepPercent,
            ScalingConfig.SetDamagePercent);

        return panel;
    }

    /// <summary>标题栏（纯展示，不可拖动）。</summary>
    private static Control BuildTitleBar()
    {
        var bar = new PanelContainer
        {
            Name = "TitleBar",
            CustomMinimumSize = new Vector2(0, 24),
            // 不吃鼠标，避免影响下面的滑条/按钮
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.22f, 0.18f, 0.10f, 0.95f),
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 3,
            ContentMarginBottom = 3,
        };
        bar.AddThemeStyleboxOverride("panel", style);

        var title = new Label
        {
            Text = "敌人数值倍率",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        title.AddThemeColorOverride("font_color", new Color(0.95f, 0.85f, 0.55f));
        bar.AddChild(title);

        return bar;
    }

    /// <summary>
    /// 四个角落按钮：点一下把面板移到对应角落，并在配置里记住。
    ///
    /// <para>这是"自由拖动"的替代方案 —— 只依赖按钮点击，
    /// 不依赖容器布局或坐标计算，因此在游戏 UI 架构下是可靠的。</para>
    /// </summary>
    private static Control BuildCornerRow()
    {
        var row = new HBoxContainer { Name = "CornerRow" };
        row.AddThemeConstantOverride("separation", 6);

        var label = new Label { Text = "位置", CustomMinimumSize = new Vector2(52, 0) };
        row.AddChild(label);

        // 用箭头表示四个角落，鼠标悬停有提示
        var corners = new (string Id, string Text, string Tip)[]
        {
            ("center", "◎", "移到屏幕正中"),
            ("tl", "↖", "移到左上角"),
            ("tr", "↗", "移到右上角"),
            ("bl", "↙", "移到左下角"),
            ("br", "↘", "移到右下角"),
        };

        var buttons = new List<(Button Button, string Id)>();

        foreach ((string id, string text, string tip) in corners)
        {
            var b = new Button
            {
                Name = "Corner_" + id,
                Text = text,
                TooltipText = tip,
                CustomMinimumSize = new Vector2(34, 24),
                Flat = true,
            };

            b.Pressed += () =>
            {
                ScalingConfig.SetCorner(id);
                HostRef?.Reposition();

                // 刷新选中态。
                // ⚠ 用按钮自己携带的 id 做**精确比较**，不要用 Name.EndsWith(id) ——
                //   "tl"/"bl" 与 "tr"/"br" 的尾字符相同，后缀匹配会互相误判。
                foreach ((Button other, string otherId) in buttons)
                {
                    ApplyCornerLook(other, otherId == id);
                }

                Log.Info($"[EnemyScaling] 面板移到角落 {id}");
            };

            buttons.Add((b, id));
            row.AddChild(b);
        }

        // 初始选中态
        foreach ((Button b, string id) in buttons)
        {
            ApplyCornerLook(b, id == ScalingConfig.Corner);
        }

        return row;
    }

    private static void ApplyCornerLook(Button b, bool active)
    {
        var style = active ? ActiveCornerStyle() : IdleCornerStyle();
        b.AddThemeStyleboxOverride("normal", style);
        b.AddThemeStyleboxOverride("hover", style);
        b.AddThemeStyleboxOverride("pressed", style);
        b.AddThemeStyleboxOverride("focus", style);
        b.AddThemeColorOverride("font_color", active
            ? new Color(1f, 0.92f, 0.65f)
            : new Color(0.75f, 0.72f, 0.62f));
    }

    /// <summary>由宿主在挂载后注入，供角落按钮触发重新定位。</summary>
    public static DragHost? HostRef { get; set; }

    /// <summary>面板上的滑条，用于联机时把主机下发的值刷到界面上。</summary>
    private static readonly Dictionary<string, (HSlider Slider, Label Value)> Sliders = new();

    /// <summary>
    /// 联机时把主机下发的倍率刷到滑条上。
    ///
    /// <para>⚠ 必须先摘掉 <c>ValueChanged</c> 再赋值，否则会触发一次
    /// <c>SetHpPercent</c> → <c>NotifyChanged</c> → 又给主机发请求，形成回环。</para>
    /// </summary>
    public static void RefreshSliders()
    {
        ApplySlider("生命值", ScalingConfig.HpPercent);
        ApplySlider("伤害", ScalingConfig.DamagePercent);
    }

    private static void ApplySlider(string key, int percent)
    {
        if (!Sliders.TryGetValue(key, out (HSlider Slider, Label Value) entry))
        {
            return;
        }

        if (!GodotObject.IsInstanceValid(entry.Slider))
        {
            return;
        }

        entry.Slider.SetValueNoSignal(percent);
        entry.Value.Text = $"{percent}%";
    }

    private static StyleBoxFlat BuildPanelStyle()
    {
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
        return style;
    }

    /// <summary>一行 = 名称 + 滑条 + 当前值。</summary>
    private static void AddSlider(
        Node parent, string label, int initial,
        int min, int max, int step, Action<int> onChanged)
    {
        var row = new HBoxContainer { Name = label + "Row" };
        row.AddThemeConstantOverride("separation", 8);

        var name = new Label { Text = label, CustomMinimumSize = new Vector2(52, 0) };
        row.AddChild(name);

        var slider = new HSlider
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            Value = initial,
            CustomMinimumSize = new Vector2(110, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
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

        slider.ValueChanged += v =>
        {
            int percent = (int)v;
            value.Text = $"{percent}%";
            onChanged(percent);

            // 联机时把改动同步出去（单机下这调用自己会直接返回）
            NetworkSync.NotifyChanged();
        };

        // 记下来，联机时主机下发的值要能刷到界面（用 SetValueNoSignal，避免回环）
        Sliders[label] = (slider, value);

        parent.AddChild(row);
    }
}
