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
    /// 面板停靠的角落：tl / tr / bl / br（默认右上角）。
    ///
    /// <para>用"选角落"而不是自由拖动：位置控制被游戏的 Control 容器布局挡住，
    /// 而按钮点击是可靠的 —— 详见 <see cref="DragHost"/> 的说明。</para>
    /// </summary>
    public static string Corner { get; private set; } = "center";

    /// <summary>合法取值：center（默认）/ tl / tr / bl / br。</summary>
    public static readonly string[] Corners = { "center", "tl", "tr", "bl", "br" };

    /// <summary>停靠位的中文名，仅用于日志与提示。</summary>
    public static string CornerLabel => Corner switch
    {
        "tl" => "左上角",
        "tr" => "右上角",
        "bl" => "左下角",
        "br" => "右下角",
        _ => "屏幕正中",
    };



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

    /// <summary>
    /// 应用**联机主机**下发的倍率（只改内存，**不写盘**）。
    ///
    /// <para>为什么不写盘：主机的倍率是"本局规则"，不是客户端自己的偏好。
    /// 若写盘，客户端本地设置会被别人的房规覆盖，下次单机时会莫名其妙变成房主的数值。</para>
    /// </summary>
    public static void ApplyFromHost(int hpPercent, int damagePercent)
    {
        HpPercent = Math.Clamp(hpPercent, MinPercent, MaxPercent);
        DamagePercent = Math.Clamp(damagePercent, MinPercent, MaxPercent);
    }

    private sealed class Dto
    {
        public int HpPercent { get; set; } = 100;
        public int DamagePercent { get; set; } = 100;
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
            // ⚠ 刻意**不从配置恢复 Corner**：
            //   用户要求"每次重启游戏面板都回到屏幕正中"，所以角落只在本次运行内有效，
            //   点角落按钮只是当前会话的临时调整，不跨重启保留。
            //   （HP/伤害百分比仍然持久化，那两个是需要长期保留的设置。）

            Log.Info($"[EnemyScaling] 已载入配置：HP {HpPercent}% / 伤害 {DamagePercent}%（面板每次启动居中）");
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

    /// <summary>
    /// 本次运行内切换角落。
    ///
    /// <para>⚠ **不写盘**：用户要求每次重启游戏面板都回到屏幕正中，
    /// 所以角落是"当前会话的临时状态"。写盘会让下次启动恢复旧角落，与需求相反。</para>
    /// </summary>
    public static void SetCorner(string corner)
    {
        if (Array.IndexOf(Corners, corner) < 0)
        {
            return;
        }

        Corner = corner;
    }



    private static void Save()
    {
        try
        {
            File.WriteAllText(Path, JsonSerializer.Serialize(new Dto
            {
                HpPercent = HpPercent,
                DamagePercent = DamagePercent,


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
