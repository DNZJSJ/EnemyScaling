using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace EnemyScaling.Scripts;

/// <summary>
/// 选人界面右上角的倍率面板：两条滑动条（生命值 / 伤害），范围 100%–500%。
///
/// <para><b>为什么用代码建控件而不是 .tscn</b>：本 mod 没有 PCK（<c>has_pck: false</c>），
/// 全部用 Godot 内建控件（<c>PanelContainer</c> / <c>VBoxContainer</c> / <c>HSlider</c>）代码构建，
/// 不依赖任何自定义脚本或场景资源。</para>
///
/// <para><b>挂载方式</b>：由 <see cref="ScalingPatches"/> 之外的一条补丁，
/// 在 <c>NCharacterSelectScreen</c> 就绪后把它加为子节点（见 <c>CharacterSelectPanelPatch</c>）。</para>
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

            var root = new PanelContainer
            {
                Name = NodeName,
                // 右上角固定定位；用 offset 而不是 anchors，避免影响原有布局
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
                ContentMarginTop = 10,
                ContentMarginBottom = 10,
            };
            style.SetBorderWidthAll(1);
            root.AddThemeStyleboxOverride("panel", style);

            var box = new VBoxContainer { Name = "Box" };
            box.AddThemeConstantOverride("separation", 6);
            root.AddChild(box);

            var title = new Label { Text = "敌人数值倍率" };
            title.AddThemeColorOverride("font_color", new Color(0.95f, 0.85f, 0.55f));
            box.AddChild(title);

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

            host.AddChild(root);

            // 放到右上角：等父节点算完尺寸再定位（此时 size 才有效）
            root.CallDeferred(Control.MethodName.SetPosition, new Vector2(
                host is Control c ? c.Size.X - 280 : 100, 16));

            Log.Info($"[EnemyScaling] 已在选人界面挂载倍率面板（{ScalingConfig.Summary}）");
        }
        catch (Exception ex)
        {
            // 面板挂不上不该拦住选人流程
            Log.Error($"[EnemyScaling] 挂载选人界面面板失败：{ex}");
        }
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
