---
type: module-guide
module: disguise
layer: runtime
maturity: stable
---

# Disguise 模块指南

## 本轮测试（2026-09-20）

EditMode 全量 191/191 通过。Disguise Showcase 2/2 通过、5 个检查点通过，无运行时异常。
覆盖已敌对时禁攻、取消伪装恢复攻击、普通攻击击杀，以及真实 Input System 短按 G/J。
报告：`Logs/verify/disguise/20260920-221546/report.md`。验证使用独立 XY 占位场景，不代表 SampleScene 整张地图视觉已验收。
截图中回放叠加层和生命 HUD 重叠，Game Gizmos 可见；视觉与手感仍待开发者确认。

独立定义“伪装期间敌人不攻击玩家”。不把伪装作为驯服前置条件。
按 G 切换伪装，状态与按键边缘继续保存在 PlayerModel，由 PlayerRules 推进。
本模块只提供攻击许可判断；MonsterRules 的实际攻击分支统一调用它。

| 类型 | 职责 |
| --- | --- |
| `DisguiseRules` | 根据伪装状态返回敌人能否攻击 |
| `PlayerRules`（复用） | 处理 G 的按下边缘与伪装状态 |
| `MonsterRules`（调用方） | 在实际攻击前检查许可 |

伪装不等于隐身：现有红区感知、警戒和追击仍可发生，但不能出手造成伤害。
即便敌人已敌对、与玩家重叠或受到伪装玩家攻击，也受同一禁攻规则约束。
解除伪装后恢复原有距离、感知和冷却判定。攻击不会自动解除伪装。
没有新增配置、存档字段或回放字段；伪装状态沿用 Player 的序列化。
规则本身每 tick 查询，不埋高频日志；玩家状态切换可用 Player 的事件记录。

## 试玩与调参

打开 `Assets/_Project/Scenes/Verify/Disguise.unity` 直接 Play。
WASD 移动，G 伪装，J 普通攻击；左上角显示双方生命和伪装状态。
敌人血量改 `Assets/_Project/Data/Monster/MonsterConfig.asset` 的 `Max Health`，停止 Play 后修改，再重新进入。
玩家伤害、攻击距离与冷却改 `Assets/_Project/Data/Player/PlayerConfig.asset`。

## 验证

- EditMode：`Assets/_Project/Scripts/Tests/EditMode/Disguise/DisguiseRulesTests.cs`，覆盖已敌对、受击、近距离禁攻及取消恢复。
- Showcase：`Assets/_Project/Scripts/Tests/Showcase/Disguise/DisguiseShowcase.cs`，使用本模块独立验证场景。
- 普通攻击回归：`Assets/_Project/Scripts/Tests/EditMode/Monster/EncounterStepTests.cs`。

不允许仅在 UI 或遭遇调度处扣掉伤害来实现伪装；敌人的攻击许可必须统一。
