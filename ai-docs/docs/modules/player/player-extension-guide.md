---
type: extension-guide
module: player
layer: runtime
maturity: stable
---

# Player 扩展指南

## 增加玩家动作

1. 在 `GameInput.inputactions` 的 Gameplay 图增加动作和键鼠、手柄绑定。
2. 优先使用 `InputCommand` 空闲按钮位；若改变序列化布局，升级回放格式。
3. 在 `LiveInputSource` 采样动作，在 `EncounterStep` 转为 `PlayerIntent`。
4. 在 `PlayerRules` 用固定 `deltaTime` 处理动作；需要跨 tick 的字段放入 `PlayerModel`。
5. 在 `PlayerModel.Serialize/Deserialize` 同顺序在末尾追加字段，升回放格式版本，并写恢复用例（先例：走 / 跑切换追加 `IsRunning`、`PreviousRun`，升到 v4）。
6. 表现留在 `EncounterSceneView` 或以后新增的玩家视图，不能反向驱动规则。

## 增加与 Monster 的交互

Monster 读取 `PlayerModel.Snapshot`，不能改玩家字段。
怪物施加伤害时构造 `DamageIntent` 并调用 `PlayerRules.ApplyDamage`。
若需要新玩家状态，先决定它是否影响未来 tick；影响则加入快照与回放版本。
若只影响颜色、音效等表现，就放在视图中，不进回放状态。

## 调整数值与验证

数值字段加在 `PlayerConfig`，用私有序列化字段和公开只读属性。
修改有效范围时同步 `PlayerRules` 构造校验。
在 `PlayerRulesTests` 覆盖新状态与恢复边界，并跑 `PlayerShowcase` 观察反馈。
配置资产由 Unity 编辑器创建，放 `Assets/_Project/Data/Player/` 并赋给 Boot 的 `PlayerInstaller`。
