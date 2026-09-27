---
type: extension-guide
module: characterpuppet
layer: runtime
maturity: stable
---

# CharacterPuppet 扩展指南

> 要在小人上加东西时查这份。架构与数据流见 [`characterpuppet-module-guide.md`](characterpuppet-module-guide.md)，
> 对外接口见 [`characterpuppet-external-api.md`](characterpuppet-external-api.md)。

## 扩展点一览

| 想做的事 | 改哪 | 不动哪 |
| --- | --- | --- |
| 换正式美术 | `Art/Sprites/Characters/<名字>/` 帧图 + 重跑 `FramePuppetGenerator` | 运行时 C#、场景接线 |
| 加新动作 | 新状态帧 + 重跑工具（孤立状态）+ `chr_<名字>.controller` 过渡 / 参数 + `ChibiPuppet` 一个新方法 | 走 / 停驱动与规则 |
| 给新角色接小人 | 场景：实例挂 `Visual` 下、隐藏纸片、配 `facingSource` | 任何代码 |
| 调手感 | `Data/CharacterPuppet/ChibiPuppetConfig.asset` | 规则类（阈值不写死在代码里） |
| 接 Spine | 新表现组件实现 `SetMoving` / `SetFacing` 语义 | `ChibiPuppetMotion` 判定流程、`ChibiPuppetMotionRules` |

## 换正式美术（序列帧）

1. 美术按 `docs/artist-guide.md` 3.4 交帧：`Art/Sprites/Characters/<名字>/chr_<名字>_<状态>_<NN>.png`，至少 `idle` + `walk`，同一画布、脚底锚点一致、默认朝右。
2. 菜单 **21Days → 角色 → 从序列帧生成小人…** 选该目录，目标高度与原来一致（现为 1.6）；或脚本调 `FramePuppetGenerator.Generate(...)`。
3. 已有资产原地更新、GUID 不变，场景实例不用重接；新角色会得到新的 `Chibi_<名字>.prefab`。
4. `/verify-module CharacterPuppet` 看截图；SampleScene 里确认与灰盒的遮挡（材质仍用 `M_SpriteDepthClip`）。

## 加新动作（例：挥手 / 攻击）

1. 美术按同一规范交新状态帧（如 `chr_amiya_attack_NN.png`），重跑生成工具：新状态作为孤立状态加进 `chr_<名字>.controller`，
   clip 生成在 `Art/Animations/Characters/<名字>/`，不手写 YAML。
2. 整帧换图没法混合，过渡时长保持 0。
3. 控制器加参数（一次性动作用 Trigger，持续状态用 Bool）与状态、过渡；时间口径不变（Animator 仍 `UnscaledTime`）。
4. 在 `ChibiPuppet` 加对应方法（如 `PlayAttack()`），参数名哈希做 `static readonly`；**参数只在 `ChibiPuppet` 里写**。
5. 触发方是玩法模块时，它通过场景引用拿 `ChibiPuppet` 调方法；小人不订阅玩法事件（保持零玩法依赖）。
6. 若新动作要压过走路（攻击时不迈腿），在控制器用层 / 过渡优先级解决，不要让 `ChibiPuppetMotion` 感知新动作。
7. 可判定的部分（何时允许打断等）进纯 C# 规则并补 EditMode 测试；Showcase 加一步截图。

## 给新角色接小人

1. 选一个 `Chibi_<名字>.prefab`（没有就先用生成工具出一份），实例化到角色的 `Visual` 节点下，localPosition `(0, 0, -0.01)`；
   纯纸片 NPC 的 `Visual` 在半身高，改挂 NPC 根下新建的 `PuppetVisual`（见 module-guide）。
2. 原纸片 `SpriteRenderer` 设 `enabled = false`；sprite 可置空（`EncounterSceneView.EnsureSprite` 会补运行时占位图）。
3. `ChibiPuppetMotion.facingSource` 指向该隐藏纸片；角色没有纸片 / 翻转逻辑时留空，走位移投影（需要场景有 `Camera.main`）。
4. `trackedRoot` 一般留空：父链上第一个非 `Visual` 节点即角色根。层级不是「根/Visual/小人」时显式指定。
5. 状态色、影子、名牌照旧挂在原节点，小人不承担。
6. 改场景走 Unity MCP，改完 Play 冒烟：静止 Idle、移动 Walk、左右翻面、对话时停回待机。

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
