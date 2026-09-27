---
type: external-api
module: player
layer: runtime
maturity: stable
---

# Player 外部接口

其他玩法模块只通过以下接口读玩家状态或施加伤害。

| 接口 | 用途 | 前提 |
| --- | --- | --- |
| `PlayerModel.Snapshot` | 读取位置、朝向、潜行、伪装、有效奔跑、生命与存活状态 | `PlayerRules.Reset` 已执行；`PlayerModel.cs:29` |
| `PlayerRules.ApplyDamage(in DamageIntent)` | 施加正整数伤害，生命最低为 0 | 从容器取 `PlayerRules`；`PlayerRules.cs:99` |
| `PlayerRules.Step(in PlayerIntent, float)` | 固定 tick 内推进并返回是否攻击 | 只由遭遇逻辑步骤调度；`PlayerRules.cs:45` |
| `PlayerRules.Reset(Vector2)` | 开始一次遭遇 | 场景出生点已确定；`PlayerRules.cs:31` |

`PlayerSnapshot` 是值类型，字段定义见 `PlayerSnapshot.cs:6`；`IsAlive` 等价于 `Health > 0`。
快照不提供写入口，也不含攻击冷却；后者属于玩家内部状态。
`DamageIntent` 定义见 `DamageIntent.cs:4`，`Amount <= 0` 会被 `ApplyDamage` 拒绝。
`PlayerIntent` 定义见 `PlayerIntent.cs:6`，由遭遇步骤从 `InputCommand` 构造；第五参 `run` 为 Run 键是否按着（必填），走 / 跑由规则按下沿切换。
`PlayerSnapshot.IsRunning` 为有效奔跑（奔跑模式开且未按潜行），不含奔跑模式本身。

`PlayerConfig` 见 `PlayerConfig.cs:7`，公开属性只读；不要在运行时改配置资产。
`PlayerInstaller` 见 `PlayerInstaller.cs:10`，应挂在 Boot 的 `GameBootstrap` 上。

## 调用顺序

每个 tick 先调用玩家规则，再取新的快照传给 Monster；怪物命中后可调用伤害入口。
不要直接设置 `PlayerModel` 字段，也不要从视图对象读取玩家位置参与逻辑。
跨模块只读依赖以 `PlayerSnapshot` 为准；场景组件只负责占位表现。
