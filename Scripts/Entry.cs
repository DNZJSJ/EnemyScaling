using System.Reflection;
using Godot.Bridge;
using HarmonyLib;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace EnemyScaling.Scripts;

/// <summary>
/// 模组入口：游戏扫描带 <c>[ModInitializer]</c> 的类型并调用指定方法。
///
/// <para>本 mod 只做两件事：</para>
/// <list type="number">
///   <item>给"怪物血量"与"伤害汇聚点"打补丁（见 <see cref="ScalingPatches"/>）；</item>
///   <item>在选人界面挂一个倍率滑动条面板。</item>
/// </list>
///
/// <para>⚠ 时序（实测）：<c>Init</c> 运行在 <c>ModelDb.Init</c> <b>之前</b>，
/// 此刻模型库还是空的。所以这里只做"打补丁 + 挂号"，不检查任何模型是否已存在。</para>
/// </summary>
[ModInitializer(nameof(Init))]
public static class Entry
{
    /// <summary>模组 ID，必须与 EnemyScaling.json 里的 id 完全一致。</summary>
    public const string ModId = "EnemyScaling";

    /// <summary>本 mod 的 Harmony 实例。</summary>
    public static readonly Harmony Harmony = new($"sts2.mod.{ModId}");

    /// <summary>初始化入口。</summary>
    public static void Init()
    {
        try
        {
            ScalingConfig.Load();

            // ① 伤害：补全局唯一汇聚点（覆盖"实际结算"和"意图显示"两条路）
            Harmony.Patch(
                AccessTools.Method(typeof(Hook), nameof(Hook.ModifyDamage)),
                postfix: new HarmonyMethod(AccessTools.Method(
                    typeof(ScalingPatches), "DamagePostfix")));

            // ② 血量：扫描所有已加载程序集里的怪物类
            ScalingPatches.PatchMonsterHp(Harmony);

            // ③ 选人界面面板
            Harmony.PatchAll(typeof(CharacterSelectPanelPatch).Assembly);

            // ④ 自检改在「进入选人界面」时做 —— Init 阶段 ModelDb 还没初始化，
            //    那时读不到怪物列表（模组指南明确的时序坑）。

            // 纯 C# 模组也建议保留：让 .tscn 能加载本程序集里的 Godot 脚本
            ScriptManagerBridge.LookupScriptsInAssembly(typeof(Entry).Assembly);

            Log.Info($"[{ModId}] 初始化完成；当前 {ScalingConfig.Summary}"
                     + "（在选人界面右上角调整）");
        }
        catch (Exception ex)
        {
            // 初始化失败不该拖垮游戏，但必须写进日志便于排查
            Log.Error($"[{ModId}] 初始化失败：{ex}");
        }
    }
}
