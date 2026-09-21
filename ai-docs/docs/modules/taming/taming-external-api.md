---
type: external-api
module: taming
layer: runtime
maturity: seed
---

# Taming 外部接口

| 接口 | 契约 |
| --- | --- |
| `TamingRules(PlayerRules, MonsterRules, ITelemetryScope)` | 两个规则先 Reset；每场景创建一个实例 |
| `Step(in TamingIntent, float)` | 非负步长；同 tick 不再由其他调度器推进这两个角色 |
| `IsTamed` | 返回玩家后仍为 true；重新创建规则才清除 |
| `IsControllingEnemy` | 当前移动输入应交给敌人，镜头跟随该对象 |
| `TamingSceneController.Simulate` | 自动化验证驱动与正常输入复用的入口 |
| `ManualSimulation` | 测试设 true 后停止自动 FixedUpdate 推进 |

`TamingIntent.ToggleControl` 按下边缘触发，长按不会连续来回切换。
敌人或玩家死亡时拒绝接管；死亡时自动返回不会复活目标。
