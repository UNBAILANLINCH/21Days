---
type: extension-guide
module: monster
layer: runtime
maturity: stable
---

# Monster 扩展指南

## 增加状态规则

1. 先确认新状态是否会持续跨 tick；瞬时攻击和受击保持动作结果或意图。
2. 若是持续状态，在 `MonsterMode` 和 `MonsterRules.Step` 加转换。
3. 需要跨 tick 的数据放 `MonsterModel`，同步序列化顺序与回放格式版本。
4. 在 `MonsterRulesTests` 覆盖进入、退出和快照恢复。
5. 在 `EncounterSceneView` 加可见反馈，并扩展 `MonsterShowcase` 的检查点。

## 调整感知、路径或战斗

感知阈值和时间放 `MonsterConfig`；数学判定留在 `MonsterRules`。
保留红区最高优先级，以及潜行与伪装各自的免疫范围。
巡逻点从场景 `EncounterSceneView` 输入，运行时复制到规则并进入回放快照。
随机时间用 `IRandomService.Stream("logic.monster.patrol")`，不能换 Unity 全局随机数。
规则只返回攻击动作；给玩家扣血的接线留在 `EncounterStep`。

## 新场景或表现

遭遇场景通过 `SceneGameState` Additive 加载，地址需在 Addressables `Scenes` 组登记。
Boot 根作用域注册规则和状态，不能把它们注册到玩法场景的子容器。
占位图、警戒条、触屏控件均为视图层；不得用屏幕位置反推感知状态。
编辑器接线完成后运行 Monster Showcase，查看巡逻、警戒、追击、命中和死亡截图。
