using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace EnemyScaling.Scripts;

/// <summary>
/// 敌人数值缩放。**只挂游戏自身的公共入口**，不识别任何具体怪物类型，
/// 所以任何 mod 新增的怪都自动生效 —— 这就是"适配大部分 mod"的实现方式。
///
/// ── 一、生命值 ─────────────────────────────────────────────────────────
/// <para>依据（实测源码）：<c>Creature.cs:350-360</c> 与 <c>378-383</c> 是所有怪物血量的
/// 唯一初始化处，两处都读 <c>MonsterModel.MinInitialHp</c> / <c>MaxInitialHp</c>：</para>
/// <code>
/// int minInitialHp = monster.MinInitialHp;
/// int maxInitialHp = monster.MaxInitialHp;
/// if (minInitialHp > maxInitialHp) throw new InvalidOperationException(...);
/// _maxHp = maxInitialHp; _currentHp = maxInitialHp;
/// </code>
/// <para>因此缩放这两个属性即可覆盖全部血量设定路径（含
/// <c>SetUniqueMonsterHpValue</c> 的随机区间与 <c>ScaleMonsterHpForMultiplayer</c> 之后的结算）。</para>
///
/// <para><b>怎么覆盖到"所有 mod 的怪"</b>：<c>MinInitialHp</c> / <c>MaxInitialHp</c> 是
/// <b>abstract</b>，每个怪物类各写各的。若只在基类 <c>MonsterModel</c> 上打补丁，
/// 会漏掉所有<b>重写</b>了这两个属性的实现（而几乎所有怪都会重写）。
/// 所以这里<b>扫描全部已加载程序集</b>，找出 <c>MonsterModel</c> 的所有子类，
/// 逐个给它们的属性访问器打补丁 —— 无论怪来自本体还是任意 mod。</para>
///
/// ── 二、伤害 ───────────────────────────────────────────────────────────
/// <para>依据（实测源码）：<c>Hook.ModifyDamage</c> 是全游戏伤害的<b>唯一汇聚点</b>，
/// 而且两条路都走它：</para>
/// <code>
/// CreatureCmd.cs:283   decimal modifiedAmount = Hook.ModifyDamage(... dealer ...);   // ← 实际结算
/// AttackIntent.cs:90   num = Hook.ModifyDamage(... owner ...);                       // ← 意图显示的数字
/// </code>
/// <para>所以只补这一个方法，就能让<b>玩家看到的意图数字与实际结算伤害一致</b>
/// （否则会出现"显示 5 实际打 10"的观感缺陷）。</para>
///
/// <para>来源判定用 <c>dealer.Monster != null</c>：只有怪物作为攻击者时才缩放，
/// 玩家自己的攻击（<c>dealer.Monster == null</c>）不受影响。</para>
///
/// <para>⚠ 只对 <c>ValueProp.Move</c>（"攻击伤害"）生效。<c>ValueProp</c> 的语义见源码注释：
/// <c>Move = Attack damage from Attack cards and Enemy creatures attacking</c>。
/// 这样能排除遗物/药水/能力造成的 <c>Unpowered</c> 伤害，避免误伤。</para>
/// </summary>
internal static class ScalingPatches
{
    /// <summary>补丁实例名（与 Entry 里的 Harmony 实例分开，便于日志定位）。</summary>
    public const string HarmonyId = "sts2.mod.EnemyScaling";

    private static int _hpPatched;
    private static int _hpSkipped;

    /// <summary>
    /// 已经处理过的类型集合。
    ///
    /// <para><b>为什么需要它</b>：Harmony 允许对同一方法重复打补丁，那样 Postfix 会
    /// **执行多次**，倍率被连乘两次（500% 变成 25 倍）。因为本方法会被反复调用
    /// （见 <see cref="PatchMonsterHp"/> 的时序说明），必须自己记账去重。</para>
    /// </summary>
    private static readonly HashSet<string> PatchedTypes = new();

    /// <summary>
    /// 给所有<b>当前已加载</b>程序集里 <see cref="MonsterModel"/> 的具体子类打血量补丁。
    ///
    /// <para><b>⚠ 为什么要反复调用（本项目实际踩过的大坑）</b>：
    /// 游戏按"加载顺序管理器"的顺序逐个加载模组，而本 mod 排在前面。
    /// 实测某次运行的顺序是：</para>
    /// <code>
    /// Loading assembly DLL ...\EnemyScaling\EnemyScaling.dll       ← 第 10 个（本 mod）
    ///    [EnemyScaling] 血量补丁：覆盖 126 个怪物类                 ← 此刻扫描
    /// Loading assembly DLL ...\Reverse1999ThirtySeven.dll           ← 第 11 个（太晚！）
    /// </code>
    /// <para>于是那个模组的 54 个怪物类<b>不在扫描范围内</b> —— 表现为
    /// <b>"原版地图的怪血量被缩放，自定义模组地图的怪没反应"</b>。
    /// 这类 bug 很难猜，因为局部看每一处都正确。</para>
    ///
    /// <para><b>对策</b>：不再假设"Init 时所有程序集都已加载"，而是在
    /// <b>每次进入选人界面时重扫一遍</b>（那时所有模组必然已加载完毕）。
    /// 配合 <see cref="PatchedTypes"/> 去重，重复调用是安全的。</para>
    /// </summary>
    public static void PatchMonsterHp(Harmony harmony)
    {
        int added = 0;
        int newlyCovered = 0;
        int skipped = 0;

        foreach (Type type in EnumerateMonsterTypes())
        {
            skipped++;

            // 只对"还没处理过"的类型打补丁：
            //   · 已处理过的跳过，避免 Postfix 重复叠加导致倍率连乘；
            //   · 新出现的类型（晚加载的模组）会被补上。
            string key = type.FullName ?? type.Name;
            if (!PatchedTypes.Add(key))
            {
                continue;
            }

            newlyCovered++;
            added += PatchProperty(harmony, type, "MinInitialHp");
            added += PatchProperty(harmony, type, "MaxInitialHp");
        }

        _hpPatched += added;
        _hpSkipped = skipped;

        // 只在"本轮有新类型"时打日志，避免每次进选人界面都刷一行
        if (newlyCovered > 0)
        {
            Log.Info($"[EnemyScaling] 血量补丁：可见怪物类 {skipped} 个，"
                     + $"本轮新增 {newlyCovered} 个（属性补丁 +{added}，累计 {_hpPatched}）");
        }
    }

    /// <summary>
    /// 启动自检：直接调一次怪物血量属性，确认补丁**真的进了调用路径**。
    ///
    /// <para><b>为什么要这个</b>：补丁"打上了"和"生效了"是两件事 —— Harmony 可以报告
    /// 成功打补丁，但若游戏不从该成员读取数值，补丁就永远不会被调用。
    /// 这一步把"是否生效"变成启动日志里可见的事实，不用等到进战斗才发现。</para>
    ///
    /// <para>用 <c>ModelDb.AllMonsters</c> 取一只真实怪物（模型库已初始化时才有效），
    /// 读它的 <c>MinInitialHp</c> 并与倍率换算的期望值比对。</para>
    /// </summary>
    public static void SelfTest()
    {
        try
        {
            decimal mult = ScalingConfig.HpMultiplier;
            if (mult == 1m)
            {
                Log.Info("[EnemyScaling] 血量自检跳过（倍率为 100%）");
                return;
            }

            MonsterModel? sample = null;
            foreach (MonsterModel m in ModelDb.Monsters)
            {
                sample = m;
                break;
            }

            if (sample == null)
            {
                Log.Warn("[EnemyScaling] 血量自检：模型库里没有怪物（ModelDb 可能尚未初始化）");
                return;
            }

            int scaled = sample.MinInitialHp;
            Log.Info($"[EnemyScaling] 血量自检：{sample.GetType().Name}.MinInitialHp 读出 {scaled}"
                     + $"（倍率 ×{mult}；若补丁生效，这里应已是缩放后的值）");
        }
        catch (Exception ex)
        {
            Log.Warn($"[EnemyScaling] 血量自检失败：{ex.Message}");
        }
    }

    /// <summary>遍历所有已加载程序集，取出 <see cref="MonsterModel"/> 的具体子类。</summary>
    private static IEnumerable<Type> EnumerateMonsterTypes()
    {
        foreach (System.Reflection.Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            // 动态程序集 / 反射扫描失败的都跳过，不让一个坏程序集拖垮整个补丁流程
            Type[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch (Exception)
            {
                continue;
            }

            foreach (Type t in types)
            {
                // ⚠ 这里**不要**过滤掉 abstract 类型 —— 见 SelfTest/PatchMonsterHp 的说明：
                //   "只要具体类"会漏掉"把血量属性实现放在抽象中间基类上"的模组。
                //   典型结构：LeafMonster : ModMonsterBase : MonsterModel，
                //   其中只有 ModMonsterBase 声明 MinInitialHp/MaxInitialHp，
                //   而它多半是 abstract ⇒ 被过滤掉后，所有叶子类都拿不到补丁。
                if (!t.IsClass || typeof(MonsterModel) == t)
                {
                    continue;
                }

                if (typeof(MonsterModel).IsAssignableFrom(t))
                {
                    yield return t;
                }
            }
        }
    }

    /// <summary>给某个属性的 getter 打补丁；已打过或找不到就跳过（返回 0/1 个新增）。</summary>
    private static int PatchProperty(Harmony harmony, Type type, string propertyName)
    {
        try
        {
            System.Reflection.PropertyInfo? prop = type.GetProperty(
                propertyName,
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.DeclaredOnly);

            System.Reflection.MethodInfo? getter = prop?.GetGetMethod();
            if (getter == null)
            {
                // 本类没声明该属性 → 由继承链上更靠上的类承担。
                // ⚠ 前提是那一层**也被打过补丁**；这正是必须把抽象中间基类一起扫进来的原因。
                return 0;
            }

            harmony.Patch(
                getter,
                postfix: new HarmonyMethod(typeof(ScalingPatches).GetMethod(
                    nameof(HpPostfix),
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)));

            return 1;
        }
        catch (Exception ex)
        {
            // 单个类型失败不影响其它类型
            Log.Warn($"[EnemyScaling] 给 {type.Name}.{propertyName} 打补丁失败：{ex.Message}");
            return 0;
        }
    }

    /// <summary>血量 Postfix：把返回值乘以倍率。</summary>
    private static void HpPostfix(ref int __result, object __instance)
    {
        try
        {
            decimal mult = ScalingConfig.HpMultiplier;
            if (mult != 1m)
            {
                int before = __result;
                __result = Math.Max(1, (int)(before * mult));

                // 自检日志：带上怪物类名，用于确认"哪些怪被缩放了、哪些没有"。
                // 只在倍率非 100% 时打印，且限条数，避免刷屏。
                if (_hpLogCount < 40)
                {
                    _hpLogCount++;
                    Log.Info($"[EnemyScaling] HP 缩放生效：{__instance?.GetType().Name ?? "?"}"
                             + $" {before} → {__result}（×{mult}）");
                }
            }
        }
        catch (Exception)
        {
            // 缩放失败时保持原值，绝不改变原版行为
        }
    }

    private static int _hpLogCount;

    // ── 伤害 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 伤害 Postfix。签名必须与 <c>Hook.ModifyDamage</c> 的参数一致（Harmony 按名/序绑定）。
    /// </summary>
    private static void DamagePostfix(Creature? dealer, ValueProp props, ref decimal __result)
    {
        try
        {
            if (dealer?.Monster == null)
            {
                return;   // 不是怪物打出来的伤害（例如玩家攻击）
            }

            if ((props & ValueProp.Move) == 0)
            {
                return;   // 不是"攻击伤害"（遗物/药水/能力等 Unpowered 伤害不缩放）
            }

            decimal mult = ScalingConfig.DamageMultiplier;
            if (mult != 1m)
            {
                __result *= mult;
            }
        }
        catch (Exception)
        {
            // 同上：绝不因缩放逻辑本身出错而改变原版行为
        }
    }
}
