# W0 摸底：巡夜人落位与四份资产现值

只读检索产物（T0.1 + T0.2），未改任何场景 / 资产 / 代码。距离全部是自己用脚本按下面的几何算的，不是编辑器实测。

## 0. 坐标约定与处理方式

- **俯视平面 = 场景 XZ**。2D 逻辑坐标 `Vector2(x, y)` 的 y 就是场景 z：`EncounterSceneView.ToScenePosition` 返回 `new Vector3(position.x, ResolveGroundY(...), position.y)`（`Assets/_Project/Scripts/Runtime/Monster/EncounterSceneView.cs:617`），场景里 `Encounter.useXZPlane: 1`（`SampleScene.unity` 的 EncounterSceneView 块）。回放侧同口径：`ShowcaseScenario.PlayerDrive.cs:13-15`。下文所有坐标写 `(x, z)`，等同回放里的 `Vector2(x, y)`。
- **层级已处理**：用脚本把 `SampleScene.unity` 的 Transform 父链逐级累加。所有有位移的父物体（`Environment_Graybox`、`Crates`、`Encounter`、`MultiLevel`、`Stairs`、`Fence` 等）本身都在 (0,0,0)，所以子物体 `m_LocalPosition` 就等于世界坐标；仅 `Bench_1/2` 有位移，只影响其腿 / 座，不用于路线计算。场景里共 11 个 PrefabInstance，都是小人外观 / 井 / 气泡，位置不影响本表。
- **可走地面的依据**：地面碰撞盒在 Ground 层（layer 8），`EncounterSceneView.groundMask = 256`（= layer 8）。`Ground`：中心 (4, 3.4)、缩放 30×30 → x −11..19、z −11.6..18.4（`SampleScene.unity:11161`）；`Ground_East`：中心 (26, 3.4)、缩放 14×30 → x 19..33、z −11.6..18.4（`:17674`）。两块顶面同高（y≈4.89）。实心阻挡（Ground 层，且高于 `maxStepHeight 0.32`）：`Wall_Left`、`Wall_Back`、`Tower`、`Fence_Rail`、楼梯 / 坡道 / 平台。**未用编辑器碰撞查询验证**，路径是否被小摆件挡住没实测。
- 玩家速度（`Assets/_Project/Data/Player/PlayerConfig.asset`）：走 3、潜行 1.5、跑 5 m/s。

## 1. 各模块回放占用表（SampleScene 内）

来源列写 `文件:行`，路径均在 `Assets/_Project/Scripts/Tests/Showcase/` 下。「覆盖范围」是把路线和停留点包起来的 x/z 范围。

公共常量全在 `Framework/ShowcaseScenario.DemoScene.cs`：

| 常量 | 值 | 行 |
| --- | --- | --- |
| `DemoSpawnPoint` | (−4, 3.4) | :47 |
| `VillageEntranceTriggerArea` | x 8..11、z 1..4 | :53 |
| `RouteToPatrol` | (3.5,5) → (8.5,7.2) → (15.5,7.2) | :60-65 |
| `PatrolLookout` | (15.5, 7.6) | :87 |
| `StairsLaneX` / `StairsCorner` / 楼梯口 / 楼梯顶 | x 18.5 / (16, 5.5) / (18.5, 5.5) / (18.5, 8.8) | :90、:96；IsometricExploration:100-101 |
| `ElderStandOffset` | (−1.2, 0)，加在长者位置上 | :109 |

`GoToPatrolLookout`（:162）= 切跑 → `RouteToPatrol` 三点（到点 0.4）→ `PatrolLookout`。

| 模块（Showcase 文件） | 用到的点 / 路线 | 覆盖的 x/z 范围 |
| --- | --- | --- |
| **Player** `Player/PlayerShowcase.cs` | 出生点右走 0.7 s、跑左 0.7 s（:29、:57、:68）；`GoToPatrolLookout`（:135）；`StrikeMonster` 追主怪，站着挨打（:139、:145） | 出生行 x −5.5..−1.9、z 3.4；北侧路线；主怪区 |
| **Monster** `Monster/MonsterShowcase.cs` | `GoToPatrolLookout`（:51、:115、:140、:186）；绕背、迎面、「往北逃 1.2 s」`Walk(up)`（:159）；`StrikeMonster`（:190） | 北侧路线 + 主怪区；北逃约到 z 7 |
| **Disguise** `Disguise/DisguiseShowcase.cs` | `GoToPatrolLookout`（:47、:88）；伪装贴到怪身边、`StrikeMonster`（:71） | 同上 |
| **Identity** `Identity/IdentityShowcase.cs` | `GoToPatrolLookout`（:60）；绕到主怪正前方 `HostileApproachDistance`（:74）；贴着敌对怪站（:99） | 同上 |
| **Stealth** `Stealth/StealthShowcase.cs` | 观察点 → 绕到主怪背后 1 m（`BehindDistance` :86 附近常量）；视锥站位 = 怪前方 3 m、北偏 1.5 m（`DriveIntoCone` :414）；`KeepDistance` 后退框 x 12.5..18、z 2.2..8（:430）；掩体 `Cover_SightDemo` 运行时挪到玩家与怪连线中点（:446-452）；处决后退场回观察点（:714-746）；动身前等**其余巡逻者**同时停稳（:634-660） | x 12..25、z 1.5..8.5（主怪巡逻线 + 观察点 + 绕背 + 视锥站位）；随主怪相位变化 |
| **Battle** `Battle/BattleShowcase.cs` | `BossStandPoint` (−4.2, 7.0)（:101）；`DrillStandPoint` (0.6, 8.8)（:89）；均从出生点直线走去；「被击倒」那条：观察点后向南推 3.5 s（:349-350，3 m/s 理论到 z≈−2.9，实际被主怪在 z≈3.4 附近打倒） | 西北角 x −4.2..0.6、z 3.4..8.8；北侧路线；观察点正南 x 15.5、z −2.9..7.6 |
| **Dialogue** `Dialogue/DialogueShowcase.cs` / `DialogueBubbleShowcase.cs` | 瞬移站位：长者左 1 → (−2, 3.4)，右 1 → (0, 3.4)；旅人 (1, 3.4)；村民 (4, 3.4)（DialogueShowcase:35、:41、:479-484）。气泡：(−4,2.1) → (6.4,2.1) → 村民东 1.4 → (6.4, 3.4)；井边妇人 (6.4,7.3) → (7.4,7.4)，正面 (8, 7.4)（BubbleShowcase:48-61）。Dialogue 用 `ScenePath = DemoScenePath`（:53） | x −4..8、z 2.1..7.4 |
| **Exploration** `Exploration/ExplorationShowcase.cs` | 出生点南 1.4 → (−4, 2) 向右推 0.6 s（:63、:135）；`OpenGround` (0.5, 2)（:69）；`NearCrateA` (7.5, 4.9)（:75）；围栏北侧 (2, 1) 向南推 2 s 撞围栏（:347-352）；坡道脚 (23.5, −2.5) 向北上甲板（:368-375）；桥后 (17.25, 12.3)、开阔地 (17.25, 3.4)（:402、:407）；西楼梯下段 (13.8, 15.5)（:427） | 全图散点：x −4..23.5、z −2.5..15.5 |
| **Inventory** `Inventory/InventoryShowcase.cs` | 瞬移到 `Crate_C` 南 0.9 → (16, −8.9)（:41、:44、:231） | 点 (16, −8.9) |
| **IsometricExploration** `IsometricExploration/IsometricExplorationShowcase.cs` | `RouteToPatrol`（:105）；楼梯口拐点 (16, 5.5)、楼梯口 (18.5, 5.5)、楼梯顶 (18.5, 8.8)（:100-113）；等主怪过 x 16.3 | x 3.5..18.5、z 5..8.8 + 主怪区 |
| **Mirror** `Mirror/MirrorShowcase.cs` | 长者西侧 (−2.2, 3.4)（:164、:258、:283）；井边妇人昏暗区（`SpiritSightZone_Well`，站位离她 1.3~2 m，:200-229）；线索箱 = `Crate_A`（itemId 1005），站位离箱 0.9 m（:222、:321、:331）；`PlanRoute` 绕触发区外扩 0.6（:1037、:1060，即 x 7.4..11.6、z 0.4..4.6）；`GoToPatrolLookout`（:335）后走近主怪挨三刀（:348） | x −2.2..12、z 0.4..12.8（含昏暗区）+ 北侧路线 + 主怪区 |
| **Narrative** `Narrative/NarrativeShowcase.cs` | 不走位；在出生点右 0.8 处生成验证见证人（:207） | 点 (−4..−3.2, 3.4) |
| **Performance** `Performance/PerformanceShowcase.cs` / `ScenePerformanceShowcase.cs` | 瞬移进触发区：`Trigger_VillageEntrance` 位置 (9.5, 2.5)（Performance:189；Scene:52、:273）；演出舞台锚点世界 (10.5, 7.0)；`home` = 出生点；演出会隐藏长者 / 旅人 / 村民 / enerme / Crate_A 标记（hiddenDuringPlay） | 触发区 x 8..11、z 1..4；舞台 (10.5, 7.0) |
| **Quest** `Quest/QuestShowcase.cs` | 瞬移进 `QuestLocation_Camp` (5, 3.4) 与 `QuestLocation_Lookout` (25, 12)（:130-160）；Quest 用 `ScenePath = DemoScenePath`（:51） | 点 (5, 3.4)、(25, 12) |
| **Session** `Session/SessionShowcase.cs` | 长者南 1 → (−1, 2.4)（:150）；`NearCrateA` (7.5, 4.9)（:59、:162）；开箱后向右推 1 s（:56、:184）约到 (10.5, 4.9) | x −1..10.5、z 2.4..4.9 |
| **Taming** `Taming/TamingShowcase.cs` | 不走位；接管 `patrol-a` / `patrol-b` 后各向右推 0.4 s（:77）；**断言 `view.PatrolActors.Length == 2`（:52），并按下标 [0]/[1] 取（:53-54、:100-105）** | 主怪线 + Patrol_B 线，右移约 1.2 m |
| **CharacterPuppet** `CharacterPuppet/CharacterPuppetShowcase.cs` | 出生点右走 0.8 s、跑左 0.6 s（:43-44、:107-143） | x −4.6..−1.6、z 3.4 |
| **TurnBased** `TurnBased/TurnBasedShowcase.cs` | 不走位（逻辑直调开战接口） | 出生点 |
| World / Replay / Rhythm / LailaFace / SelfTest | 不占 SampleScene 的位置：World 用另外两张世界场景（WorldShowcase:80-96）；Replay 的 DemoWorld 在代码里搭（Replay/DemoWorld.cs）；Rhythm、LailaFace 各有独立场景（Rhythm*:33/39、LailaFace:15）；SelfTest 是框架自测 | — |

**与新巡夜人无关但要知道的几何事实**

- 主怪巡逻线 `enerme`：(13.86, 3.4) ↔ (21.86, 3.4)；回放注释说「巡逻怪 18 米外」「x 13.86..21.86、z 3.4」（DemoScene.cs:68）。
- 回放对主怪区的假设写死在 `PatrolLookout` 的推导里（DemoScene.cs:67-86）：观察点对主怪安全；Stealth 回放已按「场上所有巡逻者」避让（StealthShowcase.cs:488 `CollectPatrollers`）。

## 2. SampleScene 现有物体坐标表

来源均为 `Assets/Scenes/SampleScene.unity`（世界坐标 = 本地坐标，理由见 §0）。

| 物体 | (x, z) | 备注 | 行 |
| --- | --- | --- | --- |
| player / 出生 | (−4, 3.4)；`Encounter/PlayerSpawn` 同 | 玩家逻辑出生点 | :12486、:17640 |
| `Npc_Elder` | (−1, 3.4) | DialogueInteractable 1001，半径 2 | :12046 |
| `Npc_Traveler` | (2, 3.4) | 半径 2 | :10341 |
| `Npc_Villager` | (5, 3.4) | 半径 2 | :8138 |
| `Yao_WellWoman` | (8, 8.8) | 半径 2 | :4104 |
| `Npc_SampleBoss` | (−4.2, 8.4) | NarrativeTrigger `sample_boss_npc` 半径 2；DialogueInteractable 动词「挑战」半径 2；挂 `BossEntryContext` | :17222 |
| `Npc_BattleDrill` | (1.5, 8.8) | NarrativeTrigger `sample_battle_drill_npc` 半径 2；动词「较量」半径 2 | :10000 |
| `Trigger_VillageEntrance` | 中心 (9.5, 2.5)，盒 3×3 → x 8..11、z 1..4 | PerformanceTrigger `perf_sample_scene_talk`，once=0，进入即演出 + 冻世界 | :2812 |
| `StageAnchor`（触发区子物体） | (10.5, 7.0) | 演出舞台锚点 | :3336 |
| `SpiritSightZone_Well` | 中心 (8, 8.8)，盒 8×8 → x 4..12、z 4.8..12.8 | 昏暗区触发盒 | :5911 |
| `QuestLocation_Camp` | (5, 3.4) | 半径 2 | :4233 |
| `QuestLocation_Lookout` | (25, 12) | 半径 2（在上层平台） | :5108 |
| `Crate_A` | (7.5, 5.8) | itemId 1005（Mirror 线索箱） | :9340 |
| `Crate_B` | (28, 15) | itemId 1002（上层平台） | :6858 |
| `Crate_C` | (16, −8) | itemId 1004（Inventory 用） | :5332 |
| `Crate_D` | (12, −8) | itemId 1401 | :15670 |
| `Crate_E` | (13.5, −8) | itemId 1102 | :2848 |
| `enerme`（主怪） | 起点 (13.86, 3.4)；巡逻点 `PatrolPoint0` (13.86, 3.4)、`PatrolPoint1` (21.86, 3.4) | `EncounterSceneView.patrolPoints` 与 `patrolActors[0]` | :7600、:15770、:3721 |
| `Patrol_B` | 起点 (13.86, 5)；`PatrolBPoint0` (13.86, 5)、`PatrolBPoint1` (17.86, 5) | `patrolActors[1]`，stableId `patrol-b` | :11559、:7326、:6723 |
| `Cover_SightDemo` | (14.2, 6.3)，尺寸 4×0.8 | 登记在 `sightOccluders`；Stealth 回放运行时会挪它 | :8994 |
| 楼梯 `Stairs_Step_1..4` | x 18（宽 3 → 16.5..19.5）、z 6.4 / 7.2 / 8.0 / 8.8 | 台阶 0.3 高递增 | :7360、:9723 |
| `Tower` | (12, 10.4)，5×5 → x 9.5..14.5、z 7.9..12.9 | 实心 | :15952 |
| `Wall_Left` | x −7..−6、z −1.1..10.9 | 实心 | :3605 |
| `Wall_Back` | x −6..14、z 9.4..11.4 | 实心 | :5756 |
| `Fence_Rail` | x −6..10、z −1（细杆） | 实心，Exploration 回放验证撞墙 | :13797 |
| `Ramp_South` | x 22..25、z −1.2..7.3 | 通往 `Deck_Upper` | :5436 |
| `Deck_Upper` | x 20..30、z 7..17（高 7.69） | 上层平台 | :15805 |
| `Bridge_West` | x 14.5..20、z 9..11.5 | | :14988 |
| 西楼梯 `Stairs_West_Step_1..9` | x 12.8..20、z 14..17 | 通上层平台 | :17524、:15395 |
| 传送点 / Portal | **SampleScene 里没有** | 传送点在世界场景（WorldShowcase），不在这张图里 | — |
| 地面 | `Ground` x −11..19；`Ground_East` x 19..33；z 均 −11.6..18.4 | 见 §0 | :11161、:17674 |
| 远景装饰 `室内前景 / (1) / (2)`、`室内大铁门`、`室内墙壁`(z 19.4) 等 | 在可玩区外或无碰撞 | 不参与 | — |

**与可走范围有关的观察**：院子（出生点所在）西侧被 `Wall_Left` 封死，南侧被 `Fence_Rail`（x −6..10）封住，北侧被 `Wall_Back` + `Tower` 封住。想去 z<−1 的南区（含 Crate_C/D/E）必须从 x≥10 的缺口出去，走法是从触发区东侧 (x 11..13.86) 往南，这条路擦着主怪巡逻线西端（距离 2.9 m，在它橙区 6 m 内）。这条路能不能走通（路径上有无小阻挡）**没实测**，仅凭碰撞盒坐标推断。

## 3. 候选路线与距离

### 3.1 约束与算法

- 巡夜人察觉参数（`Tables/Data/monster_species/1001.json`）：`alert_radius 6`、`vision_angle 75`、`near_sense_radius 1.5`、`attack_range 0.8`、`health 3`。
- 要求：整条路线（含路点之间的线段）离所有回放占用、触发区、NPC ≥ 8 m，且在可走地面上。
- 算法：把 §1 的点 / 线段 / 矩形当禁区，对路线逐段 200 次采样取最小距离（脚本在会话临时目录，不入库）。
- **「回放占用」的保守口径**：Stealth / Monster / Player / Disguise / Identity / Battle 共用的主怪区取外框 x 12..25、z 1.5..8.5（含视锥站位）；Exploration 取坡道脚 → 甲板那条线 `x 23.5, z −2.5..9`；Mirror 取触发区外扩 0.6 与昏暗区矩形。
- 三套全部落在南区 / 东南角 / 西南角，因为 z≥−1 的院子和东半场已被回放铺满，找不到离所有点 8 米以上的空地。

### 3.2 候选 A：东南角（推荐）

路点：**(29, −10) → (32, −10) → (32, −6)**（y 取 4.99 同 PatrolPoint）。地面：`Ground_East` 范围内，离东沿 1 m、南沿 1.6 m。

| 离它最近的回放模块 | 最近距离 (m) |
| --- | --- |
| Exploration（坡道脚 (23.5,−2.5) 那条线） | 9.2 |
| Battle / Disguise / Identity / IsometricExploration / Mirror / Monster / Player / Stealth（均因主怪区外框角 (25, 1.5)） | 10.3 |
| Inventory（(16, −8.9)） | 13.0 |
| Taming（Patrol_B / 主怪线） | 13.8 |
| Quest / Performance / Session / Dialogue | 19.3 / 21.1 / 23.8 / 25.6 |

物体：`Ramp_South` 外框 8.5（不是回放停留点，只是几何）；`Crate_C` 13.2；`enerme` 巡逻线 13.8；`Crate_E` 15.6；`Crate_D` 17.1；`QuestLocation_Lookout` 19.3；出生点 35.6。

### 3.3 候选 B：南区中段（守着宝箱 C/D/E）

路点：**(1, −10) → (6.5, −10)**。地面：`Ground` 内，离南沿 1.6 m。

| 离它最近的回放模块 | 最近距离 (m) |
| --- | --- |
| Exploration（围栏前 (2, −0.7)） | 9.3 |
| Inventory（(16, −8.9)） | 9.6 |
| Mirror（触发区外扩框 (7.4, 0.4)） | 10.4 |
| Performance / Battle / Dialogue | 11.1 / 11.5 / 12.1 |

物体：`Crate_D` 5.9（< 8，在巡夜人橙区里）、`Crate_E` 7.3（< 8）、`Fence_Rail` 9.0、`Crate_C` 9.7、触发区 11.1、出生点 14.3。Crate_D / E 目前没有回放走到（只有 Inventory 用 Crate_C），所以不违反「回放 ≥ 8 m」，但以后有人给 D / E 写回放就会进橙区。

### 3.4 候选 C：西南角

路点：**(−9, −5) → (−9, −9.5) → (−3, −9.5)**。地面：`Ground` 内（x ≥ −11）。

| 离它最近的回放模块 | 最近距离 (m) |
| --- | --- |
| Exploration（围栏前 (2, −0.7)） | 8.6 |
| Dialogue（气泡路线 (−4, 2.1)） | 8.7 |
| Player（出生行 (−5.5, 3.4)） | 9.1 |
| CharacterPuppet / Battle / Narrative / Performance / Taming / TurnBased | 9.5 / 9.8 / 9.8 / 9.8 / 9.8 / 9.8 |

物体：`Fence_Rail` 5.0（隔着围栏，不影响回放）、出生点 9.8、长者 11.6。余量最小，且 x<−7 一带从院子过不去，只能绕东边缺口 + 整个南区走过来，最远。

## 4. 推荐

**推荐候选 A：(29, −10) → (32, −10) → (32, −6)。**

1. **余量最大**：离任意回放点 ≥ 9.2 m，对 8 个共用主怪区的模块（Battle / Disguise / Identity / IsometricExploration / Mirror / Monster / Player / Stealth）一律 10.3 m；B、C 的最紧处只有 8.6~9.3。
2. **不挨任何东西**：附近没有 NPC、触发区、宝箱；B 会让 Crate_D / E 落进橙区，以后补宝箱回放要重算。
3. **不扰动院子与上一轮的雷区**：Patrol_B 当时的问题是压在观察点到主怪背后的必经走廊上。A 在主怪巡逻线东端 (21.86, 3.4) 东南 13.8 m，且与「观察点 (15.5, 7.6) → 主怪背后」那条走廊在不同象限。
4. **代价**：玩家得先走过主怪的警戒区才到得了南区 / 东南角（§2 末段观察）。这对「手玩试战」是延长路程，对回放没有影响。要是想让玩家一出院子就遇到它，选 B，但要接受 Crate_D / E 进橙区。

### 4.1 比距离更要紧的两条非几何风险（评审落位时必须一起决定）

1. **别把巡夜人登记进 `EncounterSceneView.patrolActors`**：
   - `TamingShowcase.cs:52` 断言 `PatrolActors.Length == 2` 并按下标取 [0]/[1]，登记第三只会让 Taming 回放直接红。
   - `StealthShowcase.CollectPatrollers`（:488-509）取的是 `step.Taming.TargetIds` 全体；`PatrollersPausedLongEnough`（:634-660）要求除主怪外**每一只**都同时处于 `PatrolPause` 且剩余 ≥ 2.5 s 才动身。注释自己写了：把主怪也算进去会让等待期望 ≈ 78 s、40 s 超时（:628-632）。巡夜人哪怕离得再远，只要在这一组里就会再加一道相位对齐。
   - 因此巡夜人要么走另一套登记路径（不进 Taming 目标组），要么同波改 Stealth 的 `CollectPatrollers` 按距离过滤。属 W1 设计决定，本摸底不动。
2. **演出隐藏名单**：`Trigger_VillageEntrance.hiddenDuringPlay` 只列长者 / 旅人 / 村民 / enerme / Crate_A 的标记（`SampleScene.unity:2812` 附近的 PerformanceTrigger 块）。A / B / C 都离舞台锚点 (10.5, 7.0) ≥ 11 m，大概率在舞台相机之外，但没实测舞台相机视野。

## 5. T0.2 四份资产现值（只读 YAML 原文）

### 5.1 `Assets/_Project/Data/Stealth/StealthConfig.asset`

字段含义对照 `Assets/_Project/Scripts/Runtime/Stealth/StealthConfig.cs:28-121`、`ChaseCaughtRules.cs:26-66`、`KnockdownRules.cs:45-63`。

**被抓（chase caught）**

| 字段 | 现值 | 含义 |
| --- | --- | --- |
| `chaseCaughtPolicy` | **5** | `ChaseCaughtPolicy.KnockdownThenDeath`：被抓先按击倒处理，被抓累计达到 `chaseCaughtFailCount` 次，或玩家当前生命 ≤ 0，才裁成 `HealthDeath`（最终失败，写 `chase.caught`）。其余枚举：1 Knockdown 只击倒（不写 caught）、2 Death、3 HealthDeath、4 ScriptedSetpiece（接线侧回调）、0 None |
| `chaseCaughtFailCount` | **2** | 同一场追逐被抓 2 次算失败：第 1 次 → 击倒（「还有 1 次机会」），第 2 次 → 判死（`ChaseCaughtRules.cs:163-170`） |
| `playerMaxHealth` | 3 | 判「血尽才死」用，须等于 `PlayerConfig.maxHealth`（现为 3） |
| `chaseSpeed` | 3.6 | 追兵速度，须 > 玩家走 3 且 ≤ 跑 5；`EncounterStep.cs:101` 另有一份同值硬编码 `PlaceholderChaseSpeed`，并未读此字段 |
| `chaseTriggerRadius` | 0 | 0 = 起追不限距离 |
| `chaseLoseSightGraceSeconds` | 2 | 跟丢 2 秒算摆脱其一，须等于 `MonsterConfig.hostileLoseSeconds`（现为 2，一致） |
| `chaseEscapeDistance` | 8 | 摆脱距离 8 m |
| `chaseTrackMemorySeconds` | 6 | 追踪记忆 6 s |
| `chaseEscapeRequiresLineOfSightLost` | 1 | 摆脱要先断视线 |
| `summonRadius` / `summonGroupMin` / `summonGroupMax` | 8 / 3 / 4 | 召唤巡逻队半径与规模（本次任务相关：「召唤」未接） |
| `fixedChaseLoops` | 1 | 固定追逐路线循环 |

**击倒（knockdown）**

| 字段 | 现值 | 含义 |
| --- | --- | --- |
| `knockdownDownedSeconds` | 1.5 | 倒地期时长 |
| `knockdownCrawlSeconds` | 3 | 挣扎期（缓慢移动）时长 |
| `knockdownCrawlSpeedMultiplier` | 0.35 | 挣扎期速度倍率（相对步行，约 1.05 m/s） |
| `knockdownResetOnHitWhileDowned` | 1 | 倒地中再挨打重置计时 |
| `knockdownHitThrottleSeconds` | 0.5 | 倒地中重复命中的节流窗口，窗口内只计数 |
| `downedHitPolicy` | **3** | `DownedHitPolicy.KnockdownThenDeath`：挨打默认击倒，生命归零才死。1 只击倒（生命到底也不死）、2 生命归零即死 |
| `knockdownCountLowMax` / `knockdownCountMidMax` | 1 / 3 | 击倒次数档位上界（低 ≤ 1、中 ≤ 3、其后高） |

其余字段：`coverWritesFact 1`、`coverImpliesHidden 0`、`assassinateRearAngle 120`、`assassinateMaxDistance 1.2`、`assassinateRequiresSneak 0`、`playerWalkSpeed 3`、`playerRunSpeed 5`。

**接线现状（只读 grep 的结论）**：`ChaseCaughtRules.Resolve` 在非测试代码里**没有调用方**（`StealthKernel.cs:46` 只是把 `ChaseOutcomeSettings` 存下来）；击倒则经 `StealthKernel.Knockdown` → `EncounterStep.cs:246-252 player.UseKnockdown(...)` 已接。所以「被抓」的口径改资产不会生效，需先有接线方。

### 5.2 `Assets/_Project/Data/Monster/MonsterConfig.asset`（全部字段）

| 字段 | 现值 | 备注（取自 `MonsterConfig.cs:24-74` 的 Tooltip） |
| --- | --- | --- |
| `patrolSpeed` | 2 | 全局巡逻速度 |
| `alertSpeedMultiplier` | 1.1 | 警戒倍率（→ 2.2 m/s） |
| `hostileSpeedMultiplier` | 1.25 | 敌对倍率（→ 2.5 m/s） |
| `hostileRadius` | 2 | 红区半径，全局不开放给种类表 |
| `alertFillSeconds` | 4 | 警戒升满秒数的兜底（种类表优先） |
| `alertFallSeconds` | 6 | 脱离后警戒衰减到零 |
| `hostileLoseSeconds` | 2 | 敌对丢目标后回警戒的兜底（种类表优先） |
| `patrolPauseSeconds` | 5 | 巡逻停步时长（代码默认 2，资产改成 5） |
| `patrolMinSeconds` / `patrolMaxSeconds` | 7 / 10 | 巡逻随机区间（Tooltip 写「随机停步区间」，Stealth 回放注释按「走 7~10 s + 停 5 s ≈ 14 s 周期」使用） |
| `visionAngle` | 75 | 视野扇区总角（兜底） |
| `alertRadius` | 6 | 橙区半径（兜底） |
| `nearSenseRadius` | 1.5 | 背后近距察觉半径（兜底） |
| `attackRange` | 0.8 | 攻击距离（兜底） |
| `attackCooldown` | 1 | 攻击冷却（兜底） |
| `maxHealth` | 3 | 初始与最大生命（兜底） |
| `attackDamage` | 1 | 每次命中伤害（兜底） |

对照 `Tables/Data/monster_species/1001.json`：`health 3`、`attack_damage 1`、`attack_range 0.8`、`attack_cooldown 1.0`、`vision_angle 75`、`alert_radius 6`、`near_sense_radius 1.5`、`alert_fill_seconds 4`、`hostile_lose_seconds 2`，与上表兜底值完全一致；所以巡夜人的察觉 / 战斗数值与主怪（兜底口径）无差异，差异只会来自 `speed` 一类 MonsterConfig 全局项（巡逻 2 / 警戒 2.2 / 敌对 2.5，都低于玩家走速 3）。

### 5.3 `Assets/_Project/Data/Identity/IdentityConfig.asset`（露馅相关）

`settings.*`（含义对照 `Assets/_Project/Scripts/Runtime/Identity/IdentitySettings.cs:55-112`）：

| 字段 | 现值 | 含义 |
| --- | --- | --- |
| `exposureLockoutSeconds` | **0** | 露馅后重新借身份的锁定时间；0 = 无影响 |
| `exposureChaseDurationSeconds` | **0** | 露馅后果为「追逐」时的追逐时长；0 = 占位 |
| `exposureDeathDelaySeconds` | **0** | 露馅后果为「死亡」时到失败的过渡时长；0 = 占位 |
| `suspicionLimit` | 100 | 怀疑度上限（≤0 = 关闭判定） |
| `suspicionCanDecay` / `suspicionDecayPerSecond` | 0 / 0 | 怀疑度只增不落 |
| `suspicionLow/Mid/HighThreshold` | 1 / 34 / 67 | 怀疑三档阈值 |
| `exposedCountLow/Mid/HighThreshold` | 1 / 2 / 3 | 露馅次数三档阈值 |
| `ledgerLimit` / `ledgerMode` | 3 / 0 | 账簿上限 3；模式 0 = `DistinctIdentities`（数不同身份个数） |
| `defaultDurationSeconds` / `cooldownSeconds` | 0 / 0 | 借身份默认时长 / 冷却（不属露馅，供参考） |

`definitions`：`dutong`（都统，origin 3，accessRights `patrol_blend`）、`zhishi_pi`（执事皮，origin 2，accessRights `fenfu_shrine`），abilities 都空。

**结论**：三个露馅惩罚旋钮全是 0，即「露馅之后锁多久 / 追多久 / 死前延迟多久」目前都没有数值，露馅到 `Chase` 或 `Death` 的后果没有时长；`IdentityRules.cs:294-300` 有按 `ExposureOutcome.Chase` 取 `exposureChaseDurationSeconds` 的分支，但值是 0。

### 5.4 `Assets/_Project/Data/Battle/BossRosterConfig.asset`（`sample_boss` 行，全部字段）

| 字段 | 现值 |
| --- | --- |
| `id` | `sample_boss` |
| `displayName` | `阶段一 BOSS（占位）` |
| `maxHealth` | 12 |
| `outOfBattleDrunk` | 50 |
| `playerHealth` | 10 |
| `stagePrefab` | guid `e830738e7b9353143a78278c4119956c`，fileID `2268993245604400291` → `Assets/_Project/Prefabs/Battle/BattleBoss_Placeholder.prefab`（由 `grep` 该 guid 命中 `BattleBoss_Placeholder.prefab.meta` 解析） |

名册里只有这一行（`bosses` 数组仅 `sample_boss`）。字段含义见 `Assets/_Project/Scripts/Runtime/Battle/BossDefinition.cs:22-38`：`maxHealth` 每次开战满血、`outOfBattleDrunk` 战斗外醉酒值开战继承、`playerHealth` 本场玩家生命（开战满血、战后不回写）、`stagePrefab` 战斗舞台外观。

## 6. 未验证 / 局限

- 所有距离是基于 YAML 里的 Transform 和碰撞盒坐标、再加回放脚本里读出的常量算的，**没跑任何回放，也没在编辑器里查碰撞**。
- 回放里依赖运行时状态的位置（主怪实际位置、`PlanRoute` 绕障碍的精确折线、怪被打倒的点）按外框保守估计，可能略大于真实范围。
- Mirror 的 `PlanRoute` 是运行时规划，没逐点展开；用「触发区外扩框 + 昏暗区矩形 + 北侧路线」包住。
- 南区 / 东南角能否从院子走通（§2 末段）是推断，未实测。
