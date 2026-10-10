# 敌人数值倍率（Enemy Scaling）

《杀戮尖塔 2》模组：在**选人界面右上角**用滑动条把敌人的 **生命值** 与 **伤害** 按
**100%–500%** 缩放。

## 特性

- **两条独立滑动条**：生命值、伤害各自 100%–500%，步进 10%。
- **改动立即保存**，下次启动自动载入。
- **意图数字与实际伤害一致**：缩放作用于游戏唯一的伤害汇总点，
  所以玩家看到的攻击意图数字就是实际会承受的伤害。
- **对所有模组生效**：不识别任何具体怪物，只挂游戏自身的公共入口，
  因此任何 mod 新增的怪物都会自动被缩放。

## 设计原理（为什么能适配大部分 mod）

只挂两个游戏自身的公共抽象点，不枚举、不识别任何具体怪物类型：

| 目标 | 注入点 | 依据 |
|---|---|---|
| 生命值 | `MonsterModel.MinInitialHp` / `MaxInitialHp` 的**每个实现** | `Creature.cs` 是所有怪物血量的唯一初始化处，两条路径都读这两个属性 |
| 伤害 | `Hook.ModifyDamage`（仅当 `dealer.Monster != null`） | 全游戏伤害的**唯一汇聚点**：`CreatureCmd` 的实际结算与 `AttackIntent` 的意图显示都走它 |

两个实现要点：

1. **血量属性是 `abstract`** —— 只在基类打补丁会漏掉所有重写它的怪（几乎所有怪都会重写）。
   因此本模组**运行时扫描所有已加载程序集**，找出 `MonsterModel` 的全部子类逐个打补丁。
   由于部分模组的程序集比本模组**晚加载**，扫描会在**每次进入选人界面时重跑**并按键去重。
2. **伤害只对 `ValueProp.Move`**（攻击伤害）生效，排除遗物/药水/能力的 `Unpowered` 伤害。

## 安装

把 `EnemyScaling.dll` 与 `EnemyScaling.json` 放进：

```
<Steam>\steamapps\common\Slay the Spire 2\mods\EnemyScaling\
```

需要 [RitsuLib](https://github.com/BAKAOLC/STS2-RitsuLib)（工坊 ID `3747602295`）。

## 从源码构建

```powershell
dotnet build EnemyScaling.csproj -c Release `
  -p:Sts2Dir="<Steam>\steamapps\common\Slay the Spire 2" `
  -p:RitsuLibDir="<Steam>\steamapps\workshop\content\2868840\3747602295\compat\0.111.0"
```

## 兼容性

- 游戏 `0.111.0` 实测通过
- 需要 RitsuLib `0.6.7`+
- **不修改任何原版数据**，所有缩放都在补丁内完成；任何一步失败都会保持原值，
  不会改变原版行为

## 已知限制

- 生命值在**怪物生成时**确定，因此请在**开局前**调整；局内改动只影响之后生成的怪物。
- 与其它同样修改 `MonsterModel` 血量或 `Hook.ModifyDamage` 的模组同时使用时，
  倍率会**叠加**。

## 许可

MIT
