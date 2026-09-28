---
type: module-guide
module: player
layer: runtime
maturity: stable
---

# Player 模块指南

## 职责与边界

Player 提供本次 Monster 遭遇所需的最小玩家行为：移动（走 / 跑切换）、朝向、潜行、伪装、攻击、受伤和死亡。
需求、暂定数值和验收口径见 `PRP/monster-ai/`；背包、装备、成长、正式动画与背后处决没有实现。

| 层 | 类型 | 职责 |
| --- | --- | --- |
| 输入 | `PlayerIntent` | 一个逻辑 tick 的移动向量与四个按钮状态（潜行、伪装、攻击、奔跑） |
| 配置 | `PlayerConfig` | 不变的原型数值，编辑器可调 |
| 规则 | `PlayerRules` | 固定步长推进、按键边缘、伤害 |
| 运行数据 | `PlayerModel` | 位置、朝向、状态、生命、计时器与回放字段 |
| 跨模块读口 | `PlayerSnapshot` | Monster 感知所需的只读值 |
| 跨模块写口 | `DamageIntent` | 经 `PlayerRules.ApplyDamage` 施加正伤害 |
| 根注册 | `PlayerInstaller` | 把配置、模型、规则注册进 Boot 根作用域 |

这些类型均在 `Game.Runtime` 程序集。依赖方向为 Player → Game.Core；Game.Core 不引用 Player。
规则类为纯 C# 对象，不继承 MonoBehaviour，不读取 Unity 的帧时间或场景单例。

## 数据流

1. `GameInput.inputactions` 的 Gameplay 动作由 `LiveInputSource` 采样。
2. `InputCommand` 保持原有 31 字节布局，使用按钮位 3、4、5、6（潜行、伪装、攻击、奔跑）。
3. `EncounterStep` 把轴与按钮位映射为 `PlayerIntent`。
4. 同一固定 tick 内先执行 `PlayerRules.Step`，再由 Monster 读取 `PlayerModel.Snapshot`。
5. 玩家攻击命中后通过 `MonsterRules.ApplyDamage` 写怪物状态。
6. 怪物命中后通过 `PlayerRules.ApplyDamage` 写玩家状态。
7. `EncounterSceneView` 在 `LateUpdate` 读取模型，只更新占位图和界面。

`PlayerRules.Step` 返回本 tick 是否产生攻击动作；命中距离、朝向与目标由 `EncounterStep` 判定。
伪装为按下边缘切换，攻击为按下边缘触发且受冷却限制，潜行为按住生效。
奔跑为按下边缘切换（长按只切一次，记录 `run_changed` 遥测）；速度三档：潜行按住 > 奔跑模式 > 步行。
潜行按住时临时按潜行速度走，但不关闭奔跑模式，松开潜行即恢复奔跑；`PlayerSnapshot.IsRunning` 是有效奔跑（模式开且未潜行）。
伪装开启后 Monster 的攻击统一被 DisguiseRules 禁止（含已敌对敌人），但不禁止警戒或追击；切换记录 `disguise_changed` 遥测。
独立场景的 StandaloneEncounterController 缓存 J/G/左 Ctrl 按下事件，避免短按落在两个物理帧之间被漏读。
玩家生命归零后不再移动或攻击；死亡视觉由遭遇场景的占位图反馈。
玩家 `Health` 归零后，若 `Mirror` 模块已接线，会弹出镜碎页并调 `EncounterStep.End()` 结束本场遭遇、重进场景恢复满生命，取代这里描述的「原地不再移动」旧表现，见 [`mirror-module-guide.md`](../mirror/mirror-module-guide.md)。
遭遇场景调试面板（`OnGUI`）也已按 Mirror 的 V8 验收去掉玩家生命数字，只显示潜行 / 伪装状态（`Assets/_Project/Scripts/Runtime/Monster/EncounterSceneView.cs:257-258`）。

## 运行数据与回放

| 数据 | 所属 | 快照 |
| --- | --- | --- |
| 位置与朝向 | `PlayerModel` | 是 |
| 潜行、伪装与奔跑模式（`IsRunning`） | `PlayerModel` | 是 |
| 当前生命 | `PlayerModel` | 是 |
| 攻击剩余冷却 | `PlayerModel` | 是 |
| 上 tick 伪装、攻击与奔跑按钮（`PreviousRun`） | `PlayerModel` | 是 |
| 移动速度、伤害等常量 | `PlayerConfig` | 否 |
| `PreviousPosition`（渲染插值用） | `PlayerModel` | 否 |

按钮边缘也进入快照，恢复后长按不会被误判为一次新攻击。
`PreviousPosition` 不进存档、不进快照、不进回放，只供 `EncounterSceneView` 做两逻辑 tick 之间的渲染插值；
`Reset` / `Restore` / 快照 `Deserialize` 之后都会调 `SyncPreviousPosition` 把它对齐到当前 `Position`，
避免读档或重置瞬间被插值从旧位置拉一下。
运行数据不写回 ScriptableObject，也不增加 JSON 存档分区；`PlayerSaveData` 同步带 `IsRunning` / `PreviousRun`。
Player 回放状态由 `MonsterInstaller` 在固定顺序中首先注册，然后才是 Monster 与遭遇步骤。
2026-09-26 快照末尾追加 `IsRunning`、`PreviousRun`，`ReplayFormat` 当前与最低可读版本升至 4；v3 及更早的回放会被拒读。

## 配置与临时数值

`PlayerConfig` 定义在 `Assets/_Project/Scripts/Runtime/Player/PlayerConfig.cs:7`。
默认移动速度 3、潜行速度 1.5、奔跑速度 5（`runSpeed`）、攻击距离 1、攻击冷却 0.6 秒、生命 3、伤害 1。
`PlayerConfig.asset` 未序列化 `runSpeed` 时 Unity 取类默认值 5，无需手改资产；在 Inspector 改一次即写入。
它们是原型值，攻击冷却尤其需要试玩校准。
数值只应在配置资产上调整，不应在规则或视图中再写第二份。
配置运行时必须为正，唯攻击冷却允许为零；不合法配置会在规则构造时抛错。

## 接线与生命周期

`PlayerInstaller` 已挂在 Boot 场景 `GameBootstrap` 上，和 `MonsterInstaller` 一起进入根作用域。
配置资产位于 `Assets/_Project/Data/Player/PlayerConfig.asset`，已赋给 Installer 的 `config` 字段。
如果未拖配置，Installer 会记录错误并用内存中的默认 ScriptableObject，供原型调试。
进入遭遇时 `EncounterStep.Begin` 调用 `PlayerRules.Reset`，以场景出生点初始化模型。
退出遭遇时 `EncounterStep.End` 停止逻辑推进；回放仍可读写模型快照。
玩家没有单独的场景状态或子作用域；场景与标题导航由 Monster 遭遇状态管理。

输入映射：键盘 WASD/方向键移动、Shift 潜行、G 伪装、J 攻击、左 Ctrl 走 / 跑切换；
手柄左摇杆、左肩键、北面按钮、西面按钮、左摇杆按下（走 / 跑切换）。
触屏虚拟摇杆与走跑等按钮已迁到探索 HUD（`ExplorationHudView`），复用同一 Gameplay 动作，仅触屏平台显示（PC 阶段不显示，移植阶段启用），本模块不再挂触屏组件。
`GameInput.cs` 为 Unity 输入系统生成物；只改 `.inputactions`，由 Unity 重新生成。

## Showcase 回放（2026-09-28 重写，走 Boot 真实流程）

`Assets/_Project/Scripts/Tests/Showcase/Player/PlayerShowcase.cs` 四条用例，全部标题「开始」进 SampleScene
（`EnterDemoWorld`），虚拟手柄推摇杆、虚拟键盘按 Gameplay 动作驱动场景里的真实玩家 `player`（不再自己 `new`
规则、不再瞬移）：

| 用例 | 演什么 |
| --- | --- |
| `WalkThenRunToggle_RunCoversMoreGround` | 走 / 走跑切换：按走跑键前后同时长推杆，比较位移证明奔跑更快 |
| `SneakHeld_MovesSlowerThenReleases` | 按住潜行键移动变慢，松开恢复 |
| `DisguiseKey_TogglesDisguiseOnAndOff` | 按伪装键开启 / 再按关闭 |
| `AttackThenTakeHits_HealthDropsToDeath` | 打巡逻怪一下后站定挨打，真实受击直到生命归零 |

舞台统一是 `Assets/Scenes/SampleScene.unity`；死亡靠真实数值（`PlayerConfig` 生命 3、`MonsterConfig` 伤害 1、
冷却 1 秒，贴身约 3 秒即死），不额外调 `PlayerRules.ApplyDamage` 补刀。

## 已知集成状态

脚本、输入映射、配置资产、Boot Installer、遭遇场景和 Addressables 均已接线。
2026-09-20 验证结果：Unity 编译无错误，相关工程 EditMode 全量 181/181 通过；视觉表现仍需开发者确认。

## 修改时检查

- 增加一个玩家动作：先改输入资产和 `InputCommand` 的预留位，再改 `EncounterStep` 映射与意图。
- 改按键边缘或冷却：同步 `PlayerModel` 快照字段，并检查回放格式版本。
- 改跨模块数据：让 Monster 读 `PlayerSnapshot`，不要把 `PlayerModel` 可写字段暴露出去。
- 改伤害：保持 `DamageIntent` 为正，死亡统一表现为生命零。
- 改显示：只在视图读状态，不把规则搬到 `LateUpdate` 或 `OnGUI`。
- 改配置：同步 `PlayerConfig` 的值范围校验和 EditMode 测试。
- 完成编辑器接线后：跑 Player Showcase、资产体检、lint 与文档检查。
