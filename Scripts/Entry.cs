using System.Reflection;
using Godot.Bridge;
using HarmonyLib;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using STS2RitsuLib.Scaffolding.Godot.NodeAttachments;

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

            // ③ 选人界面面板 —— 用 RitsuLib **官方的**节点附加机制
            //    （指南 RitsuLib/03 - 模组工具/07 - 节点附加）。
            //
            //    ⚠ 为什么不再自己写 Harmony 补丁 + 裸 AddChild（实际踩过）：
            //      第一版用 [HarmonyPatch(NCharacterSelectScreen, "_Ready")] + host.AddChild 手工挂载，
            //      面板能显示、滑条也能用，但**完全拖不动，而且日志没有任何报错** ——
            //      挂载时机/加入方式不对，导致节点拿不到输入回调，且完全静默。
            //      官方机制把挂载时机、重名策略、AddChildSafely 都交给 RitsuLib 处理，
            //      不需要自己重复实现。
            //    ⚠ 挂的是 DragHost（普通 Control）而不是面板本身：
            //      选人界面 NCharacterSelectScreen 继承 NSubmenu，**是 Control 容器**，
            //      容器的布局会接管直接子节点的位置 —— 面板位置会被覆盖回 (0,0)，
            //      表现为"拖动信号正常但面板钉在左上角不动"（实测日志确认）。
            //      DragHost 是非容器 Control，它的位置归我们管，面板相对它拖动即可。
            ModNodeAttachmentRegistry.For(ModId)
                .RegisterReadyChild<NCharacterSelectScreen, DragHost>(
                    "enemy_scaling_panel",
                    static _ => new DragHost(),
                    static (_, host) => host.Build(),
                    new NodeAttachmentOptions
                    {
                        Name = "EnemyScalingHost",
                        // 重复进入选人界面时复用已有节点，避免叠加出多个面板
                        DuplicatePolicy = NodeAttachmentDuplicatePolicy.ReuseExistingByName,
                        // 内容在加入树之后再填：要用到宿主尺寸算默认位置
                        SetupTiming = NodeAttachmentSetupTiming.AfterAdd,
                    });

            // ④ 自检改在「进入选人界面」时做 —— Init 阶段 ModelDb 还没初始化，
            //    那时读不到怪物列表（模组指南明确的时序坑）。

            // ④ 关键：让 Godot 认识本程序集里的 Godot 节点类。
            //    ⚠ DraggablePanel 必须靠这一步注册，否则面板能显示、滑条能用，
            //      但 **拖不动**（Godot 不会把 _GuiInput 派发给未注册的脚本方法），
            //      而且不会有任何报错 —— 属于很难从日志看出来的静默失效。
            ScriptManagerBridge.LookupScriptsInAssembly(typeof(Entry).Assembly);

            Log.Info($"[{ModId}] 初始化完成；当前 {ScalingConfig.Summary}"
                     + "（在选人界面右上角调整，可拖动）");
        }
        catch (Exception ex)
        {
            // 初始化失败不该拖垮游戏，但必须写进日志便于排查
            Log.Error($"[{ModId}] 初始化失败：{ex}");
        }
    }
}
