using System.Text.Json;
using MegaCrit.Sts2.Core.Logging;

namespace EnemyScaling.Scripts;

/// <summary>
/// 倍率配置：HP 与伤害各自 100%–500%（100 = 不改动）。
///
/// <para><b>为什么设置存在这里而不是走 RitsuLib 的 ModDataStore</b>：
/// 我们需要在 <b>选人界面</b>直接读值（每局开始时定档），
/// 而选人界面的 <c>_Ready</c> 可能早于 RitsuLib 的 Profile DataStore 就绪。
/// 用一个极小的独立 JSON 文件即可避免时序耦合，且不依赖对方 API 版本。
/// 依据：工作区规范允许"只读/独立文件"，且此处只有一个 int 级别的配置。</para>
/// </summary>
internal static class ScalingConfig
{
    private const string FileName = "enemy-scaling.json";

    /// <summary>敌人生命值百分比（100 = 原始数值，500 = 5 倍）。</summary>
    public static int HpPercent { get; private set; } = 100;

    /// <summary>敌人伤害百分比（100 = 原始数值，500 = 5 倍）。</summary>
    public static int DamagePercent { get; private set; } = 100;

    /// <summary>
    /// 面板位置（负值 = "还没拖过，用默认位置"）。
    ///
    /// <para>为什么要持久化：玩家把面板拖到顺手的地方后，重进选人界面它又跳回右上角会很烦。
    /// 存的是"面板左上角相对选人界面根节点的坐标"。</para>
    /// </summary>
    public static float PanelX { get; private set; } = -1f;

    /// <summary>面板 Y 坐标（负值 = 未设置）。</summary>
    public static float PanelY { get; private set; } = -1f;

    /// <summary>是否已保存过面板位置。</summary>
    public static bool HasPanelPosition => PanelX >= 0f && PanelY >= 0f;

    /// <summary>HP 倍率（已钳制到 1.0–5.0）。</summary>
    public static decimal HpMultiplier => Clamp(HpPercent);

    /// <summary>伤害倍率（已钳制到 1.0–5.0）。</summary>
    public static decimal DamageMultiplier => Clamp(DamagePercent);

    public const int MinPercent = 100;
    public const int MaxPercent = 500;
    public const int StepPercent = 10;

    private static string Path => System.IO.Path.Combine(ConfigDir, FileName);

    /// <summary>用户数据目录；取不到时退回到临时目录，保证不因写盘失败而崩。</summary>
    private static string ConfigDir
    {
        get
        {
            try
            {
                string baseDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SlayTheSpire2", "enemy-scaling");
                Directory.CreateDirectory(baseDir);
                return baseDir;
            }
            catch
            {
                return System.IO.Path.GetTempPath();
            }
        }
    }

    private static decimal Clamp(int percent)
    {
        int v = Math.Clamp(percent, MinPercent, MaxPercent);
        return v / 100m;
    }

    private sealed class Dto
    {
        public int HpPercent { get; set; } = 100;
        public int DamagePercent { get; set; } = 100;
        public float PanelX { get; set; } = -1f;
        public float PanelY { get; set; } = -1f;
    }

    public static void Load()
    {
        try
        {
            if (!File.Exists(Path))
            {
                return;
            }

            Dto? dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(Path));
            if (dto == null)
            {
                return;
            }

            HpPercent = Math.Clamp(dto.HpPercent, MinPercent, MaxPercent);
            DamagePercent = Math.Clamp(dto.DamagePercent, MinPercent, MaxPercent);
            PanelX = dto.PanelX;
            PanelY = dto.PanelY;
            Log.Info($"[EnemyScaling] 已载入配置：HP {HpPercent}% / 伤害 {DamagePercent}%"
                     + (HasPanelPosition ? $" / 面板位置 ({PanelX:0},{PanelY:0})" : ""));
        }
        catch (Exception ex)
        {
            // 配置坏了不该拦住游戏，用默认值继续
            Log.Warn($"[EnemyScaling] 载入配置失败，使用默认 100%：{ex.Message}");
        }
    }

    public static void SetHpPercent(int value)
    {
        HpPercent = Math.Clamp(value, MinPercent, MaxPercent);
        Save();
    }

    public static void SetDamagePercent(int value)
    {
        DamagePercent = Math.Clamp(value, MinPercent, MaxPercent);
        Save();
    }

    /// <summary>记住玩家拖动后的面板位置。</summary>
    public static void SetPanelPosition(float x, float y)
    {
        // 忽略异常值（NaN / Infinity），避免写坏配置
        if (float.IsNaN(x) || float.IsNaN(y) || float.IsInfinity(x) || float.IsInfinity(y))
        {
            return;
        }

        PanelX = x;
        PanelY = y;
        Save();
    }

    private static void Save()
    {
        try
        {
            File.WriteAllText(Path, JsonSerializer.Serialize(new Dto
            {
                HpPercent = HpPercent,
                DamagePercent = DamagePercent,
                PanelX = PanelX,
                PanelY = PanelY,
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Log.Warn($"[EnemyScaling] 保存配置失败：{ex.Message}");
        }
    }

    /// <summary>两行摘要，供界面与日志显示。</summary>
    public static string Summary => $"HP {HpPercent}%  ·  伤害 {DamagePercent}%";
}
