---
type: extension-guide
module: characterpuppet
layer: runtime
maturity: stable
---

# CharacterPuppet 扩展指南

> 要在拼接小人上加东西时查这份。架构与数据流见 [`characterpuppet-module-guide.md`](characterpuppet-module-guide.md)，
> 对外接口见 [`characterpuppet-external-api.md`](characterpuppet-external-api.md)。

## 扩展点一览

| 想做的事 | 改哪 | 不动哪 |
| --- | --- | --- |
| 换正式美术 | `Art/Sprites/Characters/Puppet/*.png` + 两个预制体分件局部位置 | 动画、控制器、全部 C# |
| 加新动作 | 新 clip + `chr_chibi_puppet.controller` 状态 / 参数 + `ChibiPuppet` 一个新方法 | 走 / 停驱动与规则 |
| 给新角色接小人 | 场景：实例挂 `Visual` 下、隐藏纸片、配 `facingSource` | 任何代码 |
| 调手感 | `Data/CharacterPuppet/ChibiPuppetConfig.asset` | 规则类（阈值不写死在代码里） |
| 接 Spine | 新表现组件实现 `SetMoving` / `SetFacing` 语义 | `ChibiPuppetMotion` 判定流程、`ChibiPuppetMotionRules` |

## 换真美术分件

1. 按分件出图，**PPU 100**，整体高约 1.6 世界单位（`Body` 有 1.32 倍缩放，出图尺寸按占位图等比放大即可）。
2. **pivot 语义不能变**：头 / 发 / 躯干 = 底中（Bottom Center），臂 / 腿 = 顶中（Top Center）；pivot 就是关节，错了摆动会脱臼。
3. **左右对称**：翻面只改根 `localScale.x`；右臂靠 `flipX` 共用左臂图，不对称的图要拆成两张并去掉 `ArmR.flipX`。
4. 染色方案二选一：保持白底 + `SpriteRenderer.color` 染色（现状），或出彩图并把各分件 `color` 设回白色；`SetTint` 两种都适用。
5. **排序**保持：腿 0、躯干 1、臂 2、头 3、发 4；新增分件（如武器、披风）插到合适 order，并保留逐级 0.002 的局部 z 前移。
6. 新增分件要加进 `ChibiPuppet.parts`，否则 `SetTint` 不染它。
7. 两个预制体是独立资产，**Player 与 Patrol 都要改**。
8. `/verify-module CharacterPuppet` 看截图；SampleScene 里确认与灰盒的遮挡（材质仍用 `M_SpriteDepthClip`）。

## 加新动作（例：挥手 / 攻击）

1. 在 `Assets/_Project/Art/Animations/` 新建 clip（命名按该目录 README，如 `chr_chibi_attack.anim`），
   用 Unity MCP `execute_code` + `AnimationClip.SetCurve` 生成，不手写 YAML。
2. 曲线路径沿用现有层级（`Body/Torso/ArmR` 等）；**覆盖与 Idle / Walk 同一组属性**，否则切回时残留。
3. 控制器加参数（一次性动作用 Trigger，持续状态用 Bool）与状态、过渡；时间口径不变（Animator 仍 `UnscaledTime`）。
4. 在 `ChibiPuppet` 加对应方法（如 `PlayAttack()`），参数名哈希做 `static readonly`；**参数只在 `ChibiPuppet` 里写**。
5. 触发方是玩法模块时，它通过场景引用拿 `ChibiPuppet` 调方法；小人不订阅玩法事件（保持零玩法依赖）。
6. 若新动作要压过走路（攻击时不迈腿），在控制器用层 / 过渡优先级解决，不要让 `ChibiPuppetMotion` 感知新动作。
7. 可判定的部分（何时允许打断等）进纯 C# 规则并补 EditMode 测试；Showcase 加一步截图。

## 给新角色接小人

1. 选一个预制体（或复制出新染色的一份），实例化到角色的 `Visual` 节点下，localPosition `(0, 0, -0.01)`。
2. 原纸片 `SpriteRenderer` 设 `enabled = false`，**保留 sprite**（`EncounterSceneView.EnsureSprite` 仍依赖它）。
3. `ChibiPuppetMotion.facingSource` 指向该隐藏纸片；角色没有纸片 / 翻转逻辑时留空，走位移投影（需要场景有 `Camera.main`）。
4. `trackedRoot` 一般留空：父链上第一个非 `Visual` 节点即角色根。层级不是「根/Visual/小人」时显式指定。
5. 状态色、影子、名牌照旧挂在原节点，小人不承担。
6. 改场景走 Unity MCP，改完 Play 冒烟：静止 Idle、移动 Walk、左右翻面、对话时停回待机呼吸。

## 将来接 Spine 的替换点

- 驱动层只依赖两件事：`SetMoving(bool, float playbackRate)` 与 `SetFacing(bool left)`。
- 做法：新建 Spine 表现组件（如 `SpinePuppet`），实现同名语义（`Moving` → 切 idle / walk 轨道，`playbackRate` → `TimeScale`，
  朝向 → `Skeleton.ScaleX`）。把 `ChibiPuppetMotion` 的 `puppet` 字段抽成接口后指向新组件；`ChibiPuppetMotionRules` 与 `ChibiPuppetConfig` 原样复用。
- Spine 的时间同样要用 unscaled，才能与时停语义一致。
- 引入 Spine 运行时包是加第三方依赖，先走 PRP 定方案。

## 不该从哪扩

- 不要在 `ChibiPuppetMotion` 里读输入或玩法状态：它只看位移。输入驱动的需求另写驱动组件替换它。
- 不要给 `EncounterSceneView` 加小人引用：两者只靠场景里的 `facingSource` 接线。
- 不要把阈值写回代码常量：进 `ChibiPuppetConfig`。
