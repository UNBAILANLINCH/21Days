---
type: external-api
module: monster
layer: runtime
maturity: stable
---

# Monster 外部接口

| 接口 | 用途 | 前提 |
| --- | --- | --- |
| `MonsterRules.Reset(Vector2[])` | 开始一场遭遇并复制巡逻点 | 数组至少一个点；`MonsterRules.cs` |
| `MonsterRules.Step(in MonsterIntent)` | 推进一个固定 tick，返回是否攻击 | 已 Reset；`MonsterRules.cs` |
| `MonsterRules.ApplyDamage(in DamageIntent, in PlayerSnapshot)` | 受伤并记录攻击者位置；生命零则死亡；**返回 false = 这次伤害被拒**（种类 `killable = false`，见 `06_怪物分层.md:121` R9） | 正伤害；`MonsterRules.cs` |
| `MonsterRules.Model` | 只读引用供视图取状态 | 不能从外部写内部字段；`MonsterRules.cs` |
| `MonsterRules.Kind` / `IsKillable` / `DropItemIds` | 本只怪的按种类数值与掉落 id（`Kind` 为 null = 没接种类表，走全局默认）；`DropItemIds` 只交 id，入背包走 `Game.Loot` | `MonsterRules.cs` / `MonsterKind.cs` |
| `MonsterKindCatalog.Get(int)` / `TryGet(int, out MonsterKind)` | 按种类 id 取数值；`Get` 查不到抛 `KeyNotFoundException` | 配置表已就绪；`MonsterKindCatalog.cs` |
| `EncounterStep.Begin/End` | 场景进入/退出时启停整场逻辑 | 根作用域已注册；`EncounterStep.cs:259` / `:262` |
| `EncounterStep.FactSink` | 同一个事实集的**写侧契约**（`IStealthFactSink`），交给背后处决写 `stealth.assassinated`；与读侧 `Facts` 是同一个对象 | `EncounterStep.cs:152` |
| `MonsterEncounterState` | 切换到遭遇 | 先把场景登记为地址 `IsometricEncounter`；`MonsterEncounterState.cs` |
| `MonsterEncounterState.BindExecution(ExecutionInteractor)` | 把场景里那只处决交互接上现场（玩家 / `MonsterRules` / `step.FactSink` / 可选判定内核 / 遭遇结算 / `stealth` 埋点 / 动作资产）；**找不到组件只打 Warn**，遭遇照跑 | 遭遇场景就绪后由 `OnSceneReadyAsync` 调；`MonsterEncounterState.cs` |

外部场景切换使用 `IGameFlow.GoToAsync<MonsterEncounterState>()`。
Monster 读 `PlayerSnapshot`，伤害玩家通过 `PlayerRules.ApplyDamage`；不能写 Player 模型字段。
`MonsterConfig` 见 `MonsterConfig.cs`（全局默认值 + 按种类数值的兜底，`Resolve*` 系列是唯一取值入口），
公开属性只读，资产由 `MonsterInstaller` 注入。
`MonsterMode` 见 `MonsterMode.cs:4`，供表现与测试读取，不供外部直接设置。

## 场景契约

`MonsterRules.MoveControlled(Vector2 movement, float deltaTime)`：驯服模块直接控制存活敌人，按巡逻速度移动；调用方不能同时推进敌人 AI，负步长抛出异常。
`EncounterSceneView.PlayerBody/MonsterBody` 提供相机目标；`PlayerScenePosition`（`EncounterSceneView.cs:118` 附近的 `PlayerBody` 同一批访问器）
只读暴露玩家纸片当前场景坐标（含贴地后的 Y；是两 tick 间插值后的渲染位置），供 Showcase 与跨模块读取而不碰私有字段；
`MonsterModel.PreviousPosition` / `PlayerModel.PreviousPosition`：上一逻辑 tick 的位置，只读，仅供渲染插值；不进存档与快照。
`StandaloneEncounterController.Simulate` 与 `ManualSimulation` 供验证场景确定性推进。

`EncounterProjection`（静态纯函数，不依赖 UnityEngine）：`ResolveGroundY(currentY, groundY, maxStepHeight)`（`EncounterProjection.cs:13`）
是贴地高度裁决的纯函数（下落不限、上抬超过阈值保持原高度）；`ResolveFlipX(previousX, currentX, currentFlipX, threshold)`
（`EncounterProjection.cs:17`）是翻转纯规则；`InterpolationAlpha(accumulator, fixedDeltaTime)`（`:37`，钳到 [0,1]，步长非正返回 1）、
`InterpolatePosition(...)`（`:58`，超过瞬移距离直接取当前位置）、`ResolveBlockedAxis` / `CorrectPreviousAxis`（`:85` / `:95`，
碰撞回写分轴裁决）；均可在测试或其它表现脚本中直接复用。

`EncounterSceneView`（`EncounterSceneView.cs`；该类在 Q1 接线后行号已大规模漂移，故本篇不再给它写行号）。
场景应配置 `playerSpawn` 与至少一个 `patrolPoints`；未配置巡逻点时 `PatrolPositions` 抛错。
`ConfigureXZ` 显式接入现有角色和 SpriteRenderer，并把逻辑 XY 坐标映射到场景 XZ。
视图在绑定后只显示模型，不推进规则；`OnBackClicked` 由遭遇状态订阅。
`Bind(PlayerModel, MonsterModel, Func<float> alphaSource = null, ExecutionInteractor executionSource = null)`（`EncounterSceneView.cs`）：`alphaSource` 每帧给两 tick 间的插值比例；`executionSource` 非空时白盒面板多画一行「此刻能不能按 F」（只读 `ExecutionInteractor.Inspect()`——视图不驱动处决、不写任何事实）；
正式流程由 `MonsterEncounterState` 读 `SimulationRunner.Accumulator / FixedDeltaTime`（`Driven` 模式按 1），为空按 1（不插值）。
`OnPlayerBlocked(Vector2)` 的参数是分轴合成的逻辑位置（被挡轴取修正值、其余轴为当前逻辑值），订阅方原样交给
`EncounterStep.CorrectPlayerPosition`（`EncounterStep.cs:291`），后者同时对齐被改写轴的 `PreviousPosition`。
场景 Addressables 地址是 `IsometricEncounter`，与状态类名不同。

## 回放契约

由 Installer 注册 `PlayerModel`、`MonsterRules`、`EncounterStep`；不得在别处重复注册。
更改三者顺序或序列化字段必须升级 `ReplayFormat` 版本并更新恢复测试。
