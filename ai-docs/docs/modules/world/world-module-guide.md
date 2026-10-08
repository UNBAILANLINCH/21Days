---
type: module-guide
module: world
layer: runtime
maturity: seed
---

# World 模块指南

A4「多场景流转」与 A6「相机边界与死区」的**数据层 + 纯逻辑层**：两界 × 场景、场景里的区域、
区域之间的传送点、出生点选择、跨场景状态按场景键存取、相机约束的纯数学。

**机制层已落地（2026-10-07，`PRP/world-scenes` 机制波）**：待处理转场 `IWorldTransition`、数据驱动的场景状态
`WorldSceneState`、出生点放置策略 `ISpawnPlacement`、相机约束接表现层都已实现并有 EditMode 用例。

**接线波已落地（2026-10-07，`PRP/world-scenes` 接线波）**：`WorldInstaller` 已挂到 Boot 的 `GameBootstrap`；
两张灰盒场景 `Assets/_Project/Scenes/{HumanJingyang,YaoFangshi}.unity` 建好并登记进 Addressables 的 Scenes 组
（地址 = 场景键）；`Tables/Data/world/scene/*.json` 的 `implemented` 已改 `true` 并填 `scene_address`；
传送点的调用方 `WorldSceneDriver` 与真实现 `WorldSpawnPlacement` 补上；`World` 组有了自己的 Showcase。
详见文末「接线波落地」。

**统一交互（2026-10-08，`PRP/interaction` 第二波 D9）**：按键型传送点改走 `Game.Interaction` 的统一焦点——
`PortalAnchor` 实现 `IInteractable`，`WorldSceneDriver` 只负责登记 / 注销，不再读交互键；底部提示「[E] 前往 · 目的地」。
详见下文「按键型传送点与统一交互」。

## 出生点锚点命名约定（本波定，后续场景照这套摆）

一张世界场景的接线由四样东西描述，其余全是几何：

| 摆什么 | 怎么摆 | 谁读它 |
| --- | --- | --- |
| **出生点锚点** | 空物体，**物体名 `Spawn_<spawnId>`**，并挂 `SpawnAnchor` 组件、把 `spawnId` 填成逐字等于 `TbScene.spawn_points` 里那一项 | `WorldSpawnPlacement` 按 id 查它 |
| **玩家根物体** | 一个物体（`Player`），**把它设成相机 `SmoothCameraFollow` 的 `Target`** —— 这个关系就是「谁是玩家根物体」的判定；**再挂一个 `InteractionActor`**（统一交互的玩家标记，没有它按键型出口永远不成焦点） | `WorldSceneBinder.PlayerRoot`；`IInteractionRegistry.Actor` |
| **世界相机** | 一台相机挂 `SmoothCameraFollow`，`Target` = 玩家根物体，`Config` = `IsometricExplorationConfig.asset`；要边界就勾 `Use Bounds` 并摆好四个数值 | `WorldSceneBinder.Camera` |
| **传送点** | 空物体挂 `PortalAnchor`，摆在 `TbPortal.anchor_id` 指的那个出生点位置上（与玩家根同高：按键型由统一焦点按三维距离测） | 进入范围型：`WorldSceneDriver` 逐帧按 XZ 距离判定；按键型：`WorldSceneDriver` 登记进统一交互，焦点按 `triggerRadius` 测距 |

**真源是组件字段，不是物体名**：`SpawnAnchor.spawnId` 才作数；物体名只是给人看的（改名不会破坏查找，但会误导下一个人）。
`SpawnAnchor` 找不到时会**点名是哪个 spawnId、场景里现有的锚点有哪些**，不静默。

**逻辑平面固定 XZ**：玩家逻辑坐标 = (锚点世界 x, 世界 z)，与 `EncounterSceneView.ToLogicPosition` 同一套约定。
`WorldSceneBinder` 认定「这是世界场景」的判据是**场景里至少有一个 `SpawnAnchor`**——
不能用「有没有 `SmoothCameraFollow`」当判据：遭遇原型场景 `SampleScene` 也挂了它，用它会把遭遇场景误认成世界场景，
于是 `WorldSceneDriver` 的逐 tick 推进会与 `EncounterStep` 同时推同一个玩家（速度翻倍）。

## 职责边界

| 类型 | 职责 |
| --- | --- |
| `WorldCatalog` | 世界表**只读查询**（惰性读表、白名单校验、`Invalidate`） |
| `WorldCatalogValidator` | 全表校验（9 类：枚举白名单、引用完整性、两界同构成对、未实装不许写地址…） |
| `WorldValidationIssue` / `WorldValidationResult` | 问题条目 / 结果；**阻断问题与「未实装场景」分开**（后者是状态不是错误） |
| `WorldRules` | 出生点选择**纯规则**：`Resolve`（失败抛 `WorldResolveException`）+ `TryResolveSpawn`（不抛，供调用方当分支处理） |
| `SpawnResolution` / `WorldSpawnTarget` / `WorldResolveException` | 解析结果（失败是正常分支）/ 成功结果 / 抛异常入口 |
| `PortalAnchor` | 场景里的传送点组件：两种触发（进入范围 / 交互键）；**交互键不由组件读**、不依赖 Physics、不做转场；`triggerRadius` 只是「我有多大」这个场地参数，判定在调用方。按键型实现 `Game.Interaction.IInteractable`（显式实现，`PortalAnchor.cs:192`–`:223`）：成为焦点 = `NotifyEntered`、按键 = `TryInteract`；提示动词 `interactVerb`（默认「前往」）+ 目的地名（`SetDestinationName`，由 Driver 下发） |
| `PortalTriggerKind` / `PortalTriggerKindMap` | 运行期触发器枚举 + 表枚举映射（组件不引用生成物，避免生成物变更动到场景序列化） |
| `SpawnAnchor` | **接线波新增**：出生点锚点（场景侧，只带 `spawnId`）。按 id 查落点；命名约定见上 |
| `WorldSceneBinder` | **接线波新增**：世界场景登记器（`IStartable`）。扫已加载场景里的 `SpawnAnchor` / `PortalAnchor` / 相机 / 玩家根物体；判定「这是不是世界场景」只此一处 |
| `WorldSpawnPlacement` | **接线波新增**：`ISpawnPlacement` 的真实现。按 id 找锚点 → `PlayerRules.Reset` 摆逻辑位置 → 摆场景根物体 → `SetTarget` + 立刻对准相机；任何一步不成立都返回 false 并点名缺什么 |
| `WorldSceneDriver` | **接线波新增**：世界场景驱动（`IStartable` + `ITickable` + `ISimulationStep`）。逻辑 tick 按 `InputCommand` 推玩家；渲染帧投影玩家位置、轮询进入范围型传送点（`UpdatePortals`，`WorldSceneDriver.cs:170`）、随 `PortalsVersion` 订阅 / 登记按键型传送点（`SyncPortalSubscriptions`，`:204`）、触发时写待处理转场并 `GoToAsync<WorldSceneState>()`。**`PortalAnchor` 的调用方就是它**；**不读交互键**（2026-10-08 起） |
| `WorldSaveData` | **本模块唯一的存档分区**（版本 1）：按场景键记已开箱 / 已死怪 |
| `SceneStateKey` / `SceneStateScope` | 场景作用域键拼法 / 一个场景的状态视图 |
| `IWorldTransition` / `WorldTransition` | **待处理转场**（机制波新增）：一次只挂一个目标，重复请求后到覆盖并 Warn，`TryConsume` 校验「目标场景可加载」并**消费即清空**；`Peek` 只校验不消费（`SceneKey` 用） |
| `WorldTransitionRequest` / `WorldTransitionResolution` / `WorldTransitionFailure` | 请求内容（场景键 + 出生点 + 到达方式 + 触发来源）/ 取用结果 / 四类失败原因（没目标 / 表没就绪 / 不在表里 / 未实装） |
| `WorldSceneState` | **数据驱动的场景状态**：`SceneKey` 从待处理转场读地址（无转场 → 抛，不悄悄进默认图）；`EnterScene()` = 消费转场 → 选出生点 → 交给放置策略 → 绑场景作用域 |
| `WorldSceneEntry` / `WorldSceneEntryFailure` | 进入一张图的判定结果 / 失败档位（无目标 / 不可加载 / 出生点解析不出 / 放置失败） |
| `ISpawnPlacement` / `UnwiredSpawnPlacement` | 出生点放置策略的契约 / **未接线的默认实现**（一定返回 false 并说清缺什么，绝不假装成功） |
| `WorldInstaller` | 本模块的根作用域注册器（**接线波已挂到 Boot 的 `GameBootstrap`，组件列表末尾**）。注册：`WorldCatalog` / `WorldSceneBinder` / `IWorldTransition` / `ISpawnPlacement`（真实现）/ `CameraConstraintPolicy` / `WorldSceneState` / `WorldSceneDriver` |
| `CameraConstraintPolicy`（在 `Game.IsometricExploration`） | 镜头约束的共享策略：**没有边界 = 不约束**；`WorldSceneState` 进场景时把它推给 `SmoothCameraFollow` |

## 三张表

`Tables/Defines/world.xml` + `Tables/Data/world/{scene,region,portal}/*.json`（当前 **2 场景 / 26 区域 / 2 传送点**）。
逐列真源出处写在该 xml 的列注释里（每条都带 `10_两界与场景结构.md` 的行号）。

**关键列**：
- `TbScene.implemented` + `scene_address`：**必须有这一列**——Addressables 的 Scenes 组目前**只有
  `IsometricEncounter` 一条**，其它地址写了就是空头支票。表里允许写未实装场景，校验器把它们**单独报出来**
  而不是当成地址合法。
- `TbRegion.mirror_region_id`：两界同构的落点，主图区域必须**成对互指**且指向**对面世界**
  （`10_两界与场景结构.md:132` R4）。
- `TbPortal.trigger_kind`：`EnterRange` / `Interact` 两种都留——原文「楼梯是走过去还是交互切换」是 `[待定]`。

## 按键型传送点与统一交互（2026-10-08，`PRP/interaction` D9）

| 环节 | 落点 |
| --- | --- |
| 登记 | `WorldSceneDriver.SyncPortalSubscriptions`：`PortalsVersion` 变了才重订；按键型先 `SetDestinationName(TbScene.display_name)` 再 `IInteractionRegistry.Register`（`WorldSceneDriver.cs:227`–`:228`）；换图 / `Dispose` 时逐个 `Unregister`（按引用，伪空也能注销，`:239`） |
| 目的地名 | 只有 `TbScene.display_name`（「泾阳」「镜中妖界长安·坊市」）；`TbPortal` 没有显示名字段，**不为提示新增表字段**。表没就绪 / 查不到时提示只显示「前往」（`DestinationNameOf`，`:253`） |
| 进入范围 | 统一焦点按 `InteractionRadius`（= `triggerRadius`；`requirePlayerInRange = false` 时不限距离）从 `InteractionActor` 测三维距离，成为焦点时回调 `OnFocusChanged(true)` → `NotifyEntered`；进入范围型仍由 Driver 按 XZ 距离轮询 |
| 按键 | 焦点系统读 `Gameplay/Interact`，本帧焦点没变才调 `Interact()` → `TryInteract()` → `OnTriggered` → Driver `HandleTriggered`（与进入范围型同一条转场路） |
| 让位 | 沿用统一焦点的四条：没有玩家标记 / Gameplay 图关 / 沉浸 / 世界暂停时出口不成焦点（旧实现不看这些，是行为收紧） |

`PortalAnchor` 的契约不变（不读输入、不查物理、不做转场），`PortalAnchorTests` 仍 `AddComponent` 直测；新增「统一交互」一节用例。

## 依赖方向

只依赖 `Game.Core`（存档 / 流程 / 配置 / 埋点 / 确定性内核 / 输入）。
`PortalAnchor` / `SpawnAnchor` 依赖 `UnityEngine`（MonoBehaviour），其余为纯逻辑。
不依赖 Quest / Narrative / Session / Monster；**反向**由它们调本模块。

**接线波新增的两条边**（都在 `Game.Runtime` 这同一个程序集里，asmdef 层面不需要改动）：

| 边 | 为什么 | 落点 |
| --- | --- | --- |
| `Game.World` → `Game.IsometricExploration` | 出生点放置要 `SetTarget` + 对准 `SmoothCameraFollow`，场景登记要读它的 `Target`；A6 的约束策略本来就由本模块推给它 | `WorldSpawnPlacement` / `WorldSceneBinder` |
| `Game.World` → `Game.Player`、`Game.Core.Simulation` | 世界场景要走确定性内核推玩家：`ISimulationStep` 读 `InputCommand` 交给既有的 `PlayerRules`（**不重写移动规则**） | `WorldSceneDriver` |
| `Game.World` → `Game.Interaction`（2026-10-08） | 按键型传送点实现统一交互契约、由驱动登记进登记表（`PRP/interaction` D1、D9） | `PortalAnchor` / `WorldSceneDriver` |

**为什么不复用 Monster 的 `EncounterStep` 推玩家**：那条路会连怪物与潜行结算一起激活，
而世界场景只该推玩家；两条路各推各的玩家会变成速度翻倍。两条路用「场景里有没有 `SpawnAnchor`」区分开。

## 跨场景状态怎么存（复用优先）

| 类别 | 落点 | 为什么 |
| --- | --- | --- |
| 任务点 | **复用 `Game.Quest.QuestSaveData`**（不动） | 任务进度本来就是全局的，跨场景天然成立；`QuestLocation.locationKey` 与本模块 `region_id` 用**同一套区域编号** |
| 箱子 / 怪物是否已死 | **新增 `WorldSaveData`** | `LootSaveData.CollectedCrates` 是扁平列表、`EncounterSaveData` 是单个遭遇快照且 `Validate()` 写死只认地址 `IsometricEncounter`，两者都表达不了「同一 id 在两界各记一份」 |
| NPC 状态 | **没有落点** | `DialogueSaveData` 是「当前对话稳定恢复点」（单棵树）、`NarrativeSaveData.StoryFlags` 是剧情标记不是「按场景记 NPC」。**没有硬塞**，见「待拍板」 |

**场景键**：`SceneStateKey.Scoped(sceneKey, entryId)` → `"{scene_key}::{entry_id}"`；两段都不许为空、
实体标识里不许再出现 `::`（否则拼出两义键，当场抛）。

⚠️ **分区实例读档会被整体替换**：`SceneStateScope` **每次操作都重新 `saves.Get<WorldSaveData>()`**，
进程内缓存只用来省重复扫描、**不做准**（真相在分区）。

## A6 做到了哪一半

**纯规则那一半**：`CameraConstraintRules`（`readonly struct`，无 MonoBehaviour、不读 `Time`、不碰 `Transform`）。
判定顺序固定：**先死区 → 再逐轴推 → 再边界钳制 → 最后防抖回退**；给了视口尺寸就收缩半视野，
**边界比视口小时退化成边界中点**（保证结果确定不抖）；`y` 轴不参与。

**表现层那一半（2026-10-07 机制波接上）**：边界的数据源选**方案①——作者在场景里摆**：
`SmoothCameraFollow` 上的 `Use Bounds` / `Bounds Center` / `Bounds Size` / `Dead Zone Size` / `Viewport Size`
五列（PRP/world-scenes §2.3 选①，理由是 `TbRegion` 没有世界坐标列，方案②要先给表加列）。
判定收在 `Game.IsometricExploration.CameraConstraintPolicy`（容器单例，`WorldInstaller` 注册、
`WorldSceneState` 进场景时推给相机），`SmoothCameraFollow.ResolveDesiredPosition()` 是唯一调用点。
**不设边界（`Use Bounds` 没勾）= 不约束 = 接线前的行为逐字一致**——所以没摆边界的场景零变化。

**还没做**：进对话时的构图切换（A6 的另一半，依赖边界已知，排在接线之后）。

## 已知约束

- **两张灰盒场景已实装（2026-10-07 接线波）**：`human_jingyang` / `yao_fangshi` 都在 Addressables 的
  Scenes 组里（地址 = 场景键 = `scene_address`），`WorldSceneState` 进得去。
  ⚠️ **`implemented=true` 的语义是「场景资产已存在且可加载」，不是「这个场景做完了」**：
  两张图当前是**灰盒**（地形只有能走通的方块、没有美术），正式美术要等美术输入（PRP/world-scenes §2.4）。
  这一句同时写在 `Tables/Defines/world.xml` 的 `implemented` 列注释里。
- **世界路径还没有碰撞层**：两张灰盒图能走、也能走出地面（`EncounterSceneView.obstacleMask` 那套碰撞解算
  只在遭遇原型场景里接），围墙只是视觉上的边界。要真挡住人得给世界路径接一层碰撞（见「建议补丁」）。
- **`EncounterSaveData` 的场景键已去写死**（2026-10-07）：判据从「必须等于 `IsometricEncounter`」放宽成
  **非空 + 格式合法**（`Runtime/Monster/EncounterSaveData.cs` 的 `Validate` / `IsValidSceneKey`）；
  **分区 `Version` 没升**（字段与 `Migrate` 都没变），旧档照旧通过（`EncounterSaveDataSceneKeyTests` 钉住）。
  「地址在不在表里」**不由它判**（`Validate` 必须保持纯数据）：留给调用方——世界场景入口
  `WorldTransition.TryConsume` 按 `WorldCatalog` 校验并给出四类失败原因。
  ⚠️ **遗留地址 `IsometricEncounter` 不在 `TbScene` 里**：`MonsterEncounterState`（遗留原型路径）照旧写常量，
  两条并存；但**它走不了世界转场**，拿它去 `WorldTransition` 会被判 `SceneUnknown`。
- **未实装场景不许写地址这条规则现在两半都有人管**：表内自洽归 `WorldCatalogValidator`，
  「地址真的在 Addressables 的 Scenes 组里」归编辑器工具 `Scripts/Editor/World/WorldAddressValidator.cs`
  （菜单 `21Days/世界/校验场景地址`）。接线波之后它报的是
  「通过：2 个已实装场景的地址都在「Scenes」组里（组内 3 条地址）」（第 3 条是遗留原型的 `IsometricEncounter`）。
- `locationKey` 命名口径：地图场景里的 `QuestLocation.locationKey` 要与 `TbScene.scene_key` / `TbRegion.region_id`
  用同一套编号与前缀（`{world_id}_{region}`），否则 `WorldCatalog` 查不到对应区域。

## 建议补丁（本波没改，接线波之后剩这些）

| 落点 | 要改什么 |
| --- | --- |
| ~~`Core/Flow/SceneGameState.cs:41`~~ | ✅ 机制波已按 PRP §2.1 定案处理：**不做两个子类**，改成一个 `WorldSceneState` 覆写 `SceneKey` 从待处理转场读地址（理由：一场景一状态类在十二阶段 × 两界下会膨胀，且要维护第二份映射表） |
| ~~`Core/Flow/SceneGameState.cs:84/:87`~~ | ✅ 落点已定：`WorldSceneState.EnterScene()`（`OnSceneReadyAsync` 调它） |
| ~~`Runtime/Monster/EncounterSaveData.cs`~~ | ✅ 已改：判据放宽成「非空 + 格式合法」，`Version` 没升 |
| `Runtime/Monster/MonsterEncounterState.cs` | **仍不动**（PRP §4 的处置：遗留原型路径与本模块两条并存）；等正式内容落地再决定它退不退役 |
| ~~`Assets/AddressableAssetsData/AssetGroups/Scenes.asset`~~ | ✅ 接线波已登记两张灰盒场景（地址 = 场景键），表里 `implemented=true` + `scene_address` 也同一次改完并跑了 `gen-tables.ps1` |
| ~~新建 `Scripts/Editor/World/`~~ | ✅ 已建 `WorldAddressValidator.cs`（菜单 `21Days/世界/校验场景地址`） |
| ~~`Assets/_Project/Scenes/Boot.unity`~~ | ✅ 接线波已把 `WorldInstaller` 加到 `GameBootstrap`（组件列表末尾，无必填字段） |
| ~~新场景 + 锚点~~ | ✅ 接线波已建两张灰盒场景、按出生点 id 摆 `SpawnAnchor`、并在 `WorldInstaller` 里把注册换成 `WorldSpawnPlacement` |
| **新开局 / 读档恢复那条路** | **还没接**：现在新开局仍路由到遭遇原型状态（`SessionTitleRouter` → `MonsterEncounterState` → `SampleScene`），世界场景只能经传送点或手写一条待处理转场进。要接的话改会话标题路由那一处，写一条指向 `human_jingyang` + `default_spawn_id` 的待处理转场再 `GoToAsync<WorldSceneState>()`；**动它会连带改遭遇原型那条路的入口**，属跨模块决定 |
| 世界路径的碰撞 | 灰盒能走但走得出地面；要真挡住人得给世界场景接一层碰撞解算（`EncounterSceneView.obstacleMask` 那套只在遭遇原型里接） |

## 验证入口

- EditMode `Game.Tests.EditMode.World`：原有 5 个类（`WorldTableTests` / `WorldRulesTests` / `SceneStateScopeTests` /
  `PortalAnchorTests` / `CameraConstraintRulesTests`，共 82 条）+ 机制波新增 5 个类：
  `WorldTransitionTests` / `WorldSceneStateTests` / `CameraConstraintWiringTests` / `WorldAddressValidatorTests`
  与公共夹具 `WorldTestSupport`（真表 / 手写假表、存档与资源服务的桩）。
  负对照含：未实装写地址、实装无地址、主图不写对面、`target_spawn_id` 不存在、`EnterRange` 自称 mirror、
  `default_spawn_id` 不在 `spawn_points` 里、**重复转场请求覆盖并 Warn**、**失败的转场也清空**、
  **没有待处理转场时 `SceneAddress` 抛而不是进默认图**、**不设边界时相机跟随与接线前逐字一致**。
- EditMode `Game.Tests.EditMode.Monster`：新增 `EncounterSaveDataSceneKeyTests`（旧档仍通过 + 空/空白/带分隔符被拒
  + 「格式合法但表里查不到」不由 `Validate` 拒的边界）。
- **Showcase（接线波新增）**：`Assets/_Project/Scripts/Tests/Showcase/World/WorldShowcase.cs`
  （命名空间 `Game.Tests.Showcase.World`，`Module` 返回 `"World"`）。一条用例走完
  「Boot → 标题 → 写待处理转场 → 切世界场景 → 落在指定的出生点 → 走到出口（统一焦点落在出口、提示「[E] 前往 · 镜中妖界长安·坊市」）→ 按交互键换到妖界 → 再按一次走回来」，
  三个机器可判的检查点：当前场景键、玩家落点（逻辑坐标 = 锚点坐标）、相机对准（`SmoothCameraFollow.Target` 是玩家根物体
  且镜头已跟到落点附近）。2026-10-08 起另验统一交互：每次换图后统一登记表的玩家标记是**当前世界场景里那一个**
  （`PRP/interaction` WARN 1），换图期间玩家标记的变化序列写进步骤说明（实测两次都是「空 → 新场景/Player」，即旧场景先卸）。
  报告与截图落 `Logs/verify/world/<时间戳>/report.md`。
  跑法：`run_tests(mode="PlayMode", assembly_names=["Game.Tests.Showcase"], group_names=["^Game\.Tests\.Showcase\.World\."])`
  ——**组名要锚定**：只写 `World` 会把名字里带 World 的演出回放（`PlayById_ShowsSubtitlesAndRestoresWorld` 等）一起跑。
- **⚠️ 造坏表的两种手法（接线波更新）**：`WorldRulesTests` 原来靠「反射清掉真表 `human_jingyang` 的
  `default_spawn_id`」造「没有默认出生点」的形状，前提是那一行 `implemented=false`（未实装不要求有默认出生点）。
  两张主图改成 `implemented=true` 之后，同一个动作会先撞上校验器的「实装必须有默认出生点」。
  **现在这类形状一律用手写假表**（`WorldTestSupport.CatalogWithFakeScenes` / `TablesWithFakeScenes`，
  未实装 + 无地址 = 表级合法）；真表那一侧则改成验「真表本身校验通过、两张图都 implemented 且地址非空」。
  要造「表级非法」的形状就直接问 `WorldCatalogValidator.Validate`（`TablesWithFakeScenes` 那条入口），
  **不要改共享的真实表行**——那会让校验器先抛，掩盖用例真正要验的规则。

## 机制波落地与待办（2026-10-07）

**机制波已落地**（`PRP/world-scenes` 机制层，`Assets/_Project/Scripts/Runtime/World/`）：
`IWorldTransition` + `WorldTransition`（一次一个 / 后到覆盖并 Warn / 消费即清空 / 四类失败原因）、
`WorldSceneState`（数据驱动的 `SceneKey`、`EnterScene()` 四步、失败档位）、`ISpawnPlacement` + `UnwiredSpawnPlacement`、
`WorldInstaller`、编辑器侧 `WorldAddressValidator`、`CameraConstraintPolicy` + `SmoothCameraFollow` 的约束接线。

## 接线波落地（2026-10-07，`PRP/world-scenes` 接线波）

1. **挂载**：`WorldInstaller` 已加到 `Boot.unity` 的 `GameBootstrap` 上（组件列表末尾，无必填字段）。
   它注册的七个类型里**没有一个是 `IGameService`**，所以插在哪个位置都不改变启动串行。
2. **场景与地址**：两张灰盒场景 `Assets/_Project/Scenes/{HumanJingyang,YaoFangshi}.unity` 建好，
   登记进 Addressables 的 **Scenes 组**（地址 = 场景键）；`Tables/Data/world/scene/*.json` 的
   `implemented` 改 `true`、`scene_address` 填同一串；portal 两行的 `enabled` 改 `true`（出口真的摆进场景了）；
   同一次改动里跑了 `scripts/gen-tables.ps1`（46 个 .cs / 12 个 .bytes）。
   菜单 `21Days/世界/校验场景地址` 通过。
3. **调用方**：`WorldSceneDriver` 补上传送点这一半——`PortalAnchor.OnTriggered` → 按 `anchor_id` 回查
   `TbPortal`（拿 `portal_id` 与 `arrival_method`）→ `IWorldTransition.Request` → `GameFlow.GoToAsync<WorldSceneState>()`；
   埋点 `world.portal_triggered` 一并落。**新开局 / 读档恢复那条路仍未接**（见「建议补丁」）。
4. **落地实现**：`WorldSpawnPlacement` 替换了 `WorldInstaller` 里那一行注册（`UnwiredSpawnPlacement` 仍在，
   但不再注册——它是「未接线」这个语义的载体，接线后再挂它反而是错的）。
   `WorldSceneState` 与它的测试**一行没改**，这就是机制波把「摆人」抽成接口的收益。
5. **A6 的另一半**：进对话时的构图切换**仍未做**（本波只做了边界与死区接表现层）。
6. **NPC 状态落点**：仍未做（PRP §2.5 已说明为什么不做）。
