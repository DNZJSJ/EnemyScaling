using System.Text.Json;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib.Networking.Sidecar;

namespace EnemyScaling.Scripts;

/// <summary>
/// 多人联机下的倍率同步：**以房主的设置为准**。
///
/// ── ★ 为什么必须同步（不是可选优化）──────────────────────────────────
/// <para>实测源码：<c>CreatureCmd.Damage</c> 里伤害是**每个客户端各自算**的 ——</para>
/// <code>
/// decimal modifiedAmount = Hook.ModifyDamage(...);   // 我们的补丁就在这一层
/// creature.LoseHpInternal(unblockedDamage, props);    // 本地改 HP
/// </code>
/// <para>所以如果房主 500%、客户端 100%，两端算出的伤害不同 → HP 分叉 →
/// 游戏判定不同步并断开。这就是"参数和房主不一样就掉线"的根因。</para>
///
/// ── 做法（按官方指南选型）────────────────────────────────────────────
/// <para>指南 <c>RitsuLib/02 - 玩法基底/14 - 网络与联机</c> 的选型表写着：
/// "传任意数据给队友/主机（通知、UI、**自定义配置**）→ 用 <b>Sidecar</b>"。
/// 因此这里用 Sidecar 做两件事：</para>
/// <list type="number">
///   <item><b>客户端请求</b>（<c>req</c>）：客户端一进选人界面就向主机要当前倍率；</item>
///   <item><b>主机广播</b>（<c>cfg</c>）：主机把自己的倍率广播给所有客户端。</item>
/// </list>
///
/// <para><b>防回环</b>：客户端收到广播后只应用、不转发；主机收到请求后只广播、不应用。
/// 两端角色由 <c>RunManager.Instance.NetService.Type</c> 判定，不靠消息内容猜。</para>
///
/// <para><b>客户端不写盘</b>：房主的倍率是"本局规则"，不该覆盖客户端自己的本地设置，
/// 所以应用时走 <see cref="ScalingConfig.ApplyFromHost"/>（只改内存，不存文件）。</para>
/// </summary>
internal static class NetworkSync
{
    /// <summary>主机 → 全体：当前生效的倍率。</summary>
    private static readonly RitsuLibSidecarMessageDescriptor<ScalingSnapshot> ConfigDescriptor = new(
        ModuleId: Entry.ModId,
        MessageKey: "scaling_cfg",
        Serialize: static msg => JsonSerializer.SerializeToUtf8Bytes(msg),
        Deserialize: static bytes => JsonSerializer.Deserialize<ScalingSnapshot>(bytes),
        Delivery: RitsuLibSidecarDeliverySemantics.StableSync);

    /// <summary>客户端 → 主机：请求下发当前倍率。</summary>
    private static readonly RitsuLibSidecarMessageDescriptor<ScalingSnapshot> RequestDescriptor = new(
        ModuleId: Entry.ModId,
        MessageKey: "scaling_req",
        Serialize: static msg => JsonSerializer.SerializeToUtf8Bytes(msg),
        Deserialize: static bytes => JsonSerializer.Deserialize<ScalingSnapshot>(bytes),
        Delivery: RitsuLibSidecarDeliverySemantics.StableSync);

    /// <summary>传给 Sidecar 的倍率快照。</summary>
    public record struct ScalingSnapshot(int HpPercent, int DamagePercent);

    private static IDisposable? _configSub;
    private static IDisposable? _requestSub;

    private static INetGameService? Net => RunManager.Instance?.NetService;

    /// <summary>本机是不是联机客户端（单机 / 主机都不是）。</summary>
    private static bool IsClient => Net?.Type == NetGameType.Client;

    /// <summary>本机是不是联机主机。单机时也返回 false（单机用自己的设置即可）。</summary>
    public static bool IsHost => Net?.Type == NetGameType.Host;

    /// <summary>只订阅一次。</summary>
    public static void Init()
    {
        try
        {
            _configSub ??= RitsuLibSidecarTypedMessageRegistry.Subscribe(ConfigDescriptor, OnConfigReceived);
            _requestSub ??= RitsuLibSidecarTypedMessageRegistry.Subscribe(RequestDescriptor, OnRequestReceived);
        }
        catch (Exception ex)
        {
            Log.Warn($"[EnemyScaling] 网络同步订阅失败（不影响单机）：{ex.Message}");
        }
    }

    /// <summary>
    /// 进入选人界面时调用：客户端要一次配置，主机广播一次。
    /// 单机时不做事。
    /// </summary>
    public static void SyncIfNeeded()
    {
        try
        {
            if (Net == null)
            {
                return;
            }

            if (IsClient)
            {
                // 客户端：向主机要当前倍率（重复请求是无害的，主机会重发）
                RitsuLibSidecarTypedMessageRegistry.SendToHost(
                    RunManager.Instance, RequestDescriptor, Snapshot());
                Log.Info("[EnemyScaling] 联机客户端：已向主机请求倍率设置");
            }
            else if (IsHost)
            {
                BroadcastConfig("进入选人界面");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[EnemyScaling] 网络同步失败（不影响本局）：{ex.Message}");
        }
    }

    /// <summary>
    /// 设置被改动时调用。
    /// 主机 → 广播新值；客户端 → 转告主机（自己的改动在本机不生效，以主机为准）。
    /// </summary>
    public static void NotifyChanged()
    {
        try
        {
            if (Net == null)
            {
                return;   // 单机：直接用自己的设置
            }

            if (IsClient)
            {
                RitsuLibSidecarTypedMessageRegistry.SendToHost(
                    RunManager.Instance, RequestDescriptor, Snapshot());
            }
            else if (IsHost)
            {
                BroadcastConfig("设置变更");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[EnemyScaling] 通知同步失败：{ex.Message}");
        }
    }

    private static void BroadcastConfig(string reason)
    {
        RitsuLibSidecarTypedMessageRegistry.Broadcast(
            RunManager.Instance, ConfigDescriptor, Snapshot());
        Log.Info($"[EnemyScaling] 联机主机：已广播倍率设置（{reason}）");
    }

    private static ScalingSnapshot Snapshot() =>
        new(ScalingConfig.HpPercent, ScalingConfig.DamagePercent);

    /// <summary>客户端收到主机的倍率：应用（不写盘、不再转发）。</summary>
    private static void OnConfigReceived(RitsuLibSidecarTypedDispatchContext<ScalingSnapshot> ctx)
    {
        // 主机自己不该被自己的广播改设置（正常不会到达，防一手）
        if (IsClient)
        {
            ScalingConfig.ApplyFromHost(ctx.Message.HpPercent, ctx.Message.DamagePercent);
            CharacterSelectPanel.RefreshSliders();
            Log.Info($"[EnemyScaling] 已应用主机倍率：HP {ctx.Message.HpPercent}% / 伤害 {ctx.Message.DamagePercent}%");
        }
    }

    /// <summary>主机收到客户端的请求：回广播当前倍率（不采纳客户端的值）。</summary>
    private static void OnRequestReceived(RitsuLibSidecarTypedDispatchContext<ScalingSnapshot> ctx)
    {
        if (IsHost)
        {
            BroadcastConfig($"客户端 {ctx.SenderNetId} 请求");
        }
    }
}
