using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

namespace EnemyScaling.Scripts;

/// <summary>
/// 把倍率面板挂到选人界面。
///
/// <para>目标方法：<c>NCharacterSelectScreen._Ready()</c>
/// （<c>MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect</c>）。
/// 用 Postfix 是因为要等原方法把界面树建好，我们再追加一个自己的子节点。</para>
///
/// <para>用 Postfix（追加节点）而不是 Prefix，是为了零侵入：不修改、不替换原界面任何节点，
/// 即使本补丁失效，选人界面也照原样工作。</para>
/// </summary>
[HarmonyPatch(typeof(NCharacterSelectScreen), "_Ready")]
internal static class CharacterSelectPanelPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCharacterSelectScreen __instance)
    {
        CharacterSelectPanel.Attach(__instance);

        // ★ 重扫怪物类 —— 这是修复"自定义模组地图的怪血量不缩放"的关键一步。
        //
        // 原因：本 mod 的程序集比部分模组**先加载**，Init 时扫描会漏掉那些
        // 还没加载的 mod 的怪物类（实测 R1999 的 DLL 排在本 mod 之后，
        // 它的 54 个怪物类全漏了 → 表现为"原版图生效、自定义图不生效"）。
        // 进入选人界面时所有模组必然已加载完毕，此时重扫即可补上。
        // PatchMonsterHp 内部按类型去重，重复调用不会让倍率连乘。
        ScalingPatches.PatchMonsterHp(Entry.Harmony);

        // 顺便做一次血量自检：此时 ModelDb 已就绪，能读到真实怪物，
        // 把"补丁是否真的生效"写进日志（每局开始前都能看到）。
        ScalingPatches.SelfTest();
    }
}
