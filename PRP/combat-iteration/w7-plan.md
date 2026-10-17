# W7 计划：BOSS 新位置 + 回放名单

只读分析产物，没改场景 / 资产 / 代码，没跑任何回放。坐标约定同 `w0-placement.md` §0：`(x, z)` = 回放里的 `Vector2(x, y)`。
距离是我用脚本按 YAML 里的 Transform / 碰撞盒（把 `SampleScene.unity` 工作区版本的父链累加后）和回放代码里读出的常量算的，**不是编辑器实测**。

## 0. 与 w0 文档不一致的事实（以当前代码 / 工作区场景为准）

| 项 | w0 写的 | 当前 | 依据 |
| --- | --- | --- | --- |
| `Wall_Back` | x −6..14、z 9.4..11.4 | x −6..14、**z 9.9..10.9**（高 y 4.89..7.89） | `SampleScene.unity` BoxCollider 展开 |
| `Tower` | 实心方块 | **CapsuleCollider**，中心 (12, 10.4)、半径 2.5（含 x 9.5..14.5、z 7.9..12.9） | 同上，对象 `Tower` |
| 地面 | 顶面 ≈ 4.89 | `Ground` / `Ground_East` BoxCollider y 4.69..4.89，顶面 **4.89**；`Npc_SampleBoss` 的 Transform y = 4.8884 | 同上 |
| 回放共享件 | — | W6 后 `Framework/ShowcaseScenario.DemoScene.cs` 多了南侧走廊 / 缝口穿越（`GoThroughSouthGap`、`SouthStagingPoint` 等，:106-162、:245-280）与 `ShowcasePatrolForecast.cs` | 当前代码 |
| `StrikeMonster` | 只管打怪 | 打中市令升级回合制后立即返回（`IsEscalating`，`DemoScene.cs:307-309、326、351`） | 当前代码 |

## 任务 A：BOSS 新位置

### A.0 几个会影响选点的事实

1. **BOSS 冲刺不碰撞、不跟地面**：`BossAggro.TickCharging` 只做 `transform.position = (x + dx*scale, 原 y, z + dz*scale)` 直线走到接触距离，高度恒定（`Assets/_Project/Scripts/Runtime/Battle/BossAggro.cs:331-343`）；拴绳按水平距离量（:355）。所以「墙把 BOSS 卡住」在物理上不会发生，真正要管的是：**原位的 y 要等于地面顶面**，以及**冲刺直线不要从视觉上穿墙**（玩家在发难半径 5 米内时，直线最长 5 米）。
2. **院子（出生点所在）里没有满足「≥8 米」的位置**：院子西 / 北 / 南被 `Wall_Left`、`Wall_Back`、`Fence_Rail` 封死，东半院被对白 / 气泡 / 任务 / 演出 / 镜 / 会话回放占满（见 A.3 的占用表）。所以 BOSS 必然挪到**院外**；院外只有两片：院南（z < −1.1，经缝口过去）和院北（z > 10.9，墙后）。
3. **院北不可取**：BOSS 在 `Wall_Back`（高 3 米）后面，探索相机在南面往北看，约 3.6 米进深会被墙挡住（相机 40° 俯角，是照 `ExplorationShowcase` 注释里的数反推的，未实测）；去那里要从观察点北上再穿塔与西楼梯之间只有 1.1 米宽的走廊（塔 z ≤ 12.9、楼梯 z ≥ 14）。**只在院南选点。**
4. 「落在可走地面上」里，**10 米内地面连续在南区做不到字面意义**：`Ground` 西沿 x = −11、南沿 z = −11.6，任何院南点离地图边缘都 < 10 米。依据 1，BOSS 只朝玩家直线走，玩家一定在地面上，线段整段在地面矩形内，所以实际不受影响；各候选列出离边缘的距离。

### A.1 判据与用到的占用点（非 Battle 模块，当前代码）

「占用」= 路线折线 / 停留点；分两类：出生点附近（Player 出生行、Narrative、CharacterPuppet、Taming / TurnBased 站桩）、北侧路线（`RouteToPatrol` + `PatrolLookout`）、主怪区外框（x 12..25、z 1.5..8.5）、缝口穿越与南走廊（`SoutheastHoldRoute` → `SoutheastGapCrossing` → z −1.5 走廊 x 13..21 → `SoutheastTransit` (18, −4) → `XunyeStagingPoint` (22.5, −7.5)）、Dialogue（(−2,3.4)…(4,3.4) 与气泡路线 (−4,2.1)→(6.4,2.1)→(6.4,7.3)→(8,7.4)）、Exploration 的各瞬移点（(−4,2)、(0.5,2)、(7.5,4.9)、围栏前 (2,1)→(2,−0.7)、坡道 (23.5,−2.5)→(23.5,9)、桥 (17.25,12.3)/(17.25,3.4)、(13.8,15.5)）、Inventory (16,−8.9)、IsometricExploration 楼梯路线、Mirror（长者西 (−2.2,3.4)、井边、Crate_A）、Performance (9.5,2.5)/(10.5,7)、Quest (5,3.4)/(25,12)、Session (−1,2.4)/(7.5,4.9)/(10.5,4.9)。
Battle 自己的演练桩站位 (2.4, 8.8)、拐点 (0.5, 5.5) 与 `Npc_BattleDrill` (1.5, 8.8) 单独算。

### A.2 候选（y 都取 4.8884，与现 BOSS 同；地面顶面 4.89）

| | **A（推荐）(−4.0, −8.0)** | B (−8.5, −6.5) | C (−1.0, −9.0) |
| --- | --- | --- | --- |
| 离出生点 (−4, 3.4) | 11.40 | 10.87 | 12.76 |
| 离非 Battle 回放最近占用 | **9.45**（Exploration 围栏用例终点 (2, −0.7)）；其次 10.0（Exploration 出生行南 1.4）、10.1（Dialogue 气泡路线）、10.8（Session） | 9.62（Exploration 出生行）；9.71（Dialogue 气泡）；10.3（Player 出生行） | **8.83**（Exploration 围栏用例）；11.1（Dialogue 气泡 / Exploration (0.5,2)） |
| 离演练桩站位 (2.4,8.8) / 拐点 / NPC | 14.2；`Npc_BattleDrill` 17.7 | 15.0；18.3 | 14.6；18.0 |
| 离巡逻线：主怪 / 乙 / 巡夜人 | 21.2 / 22.1 / 33.1 | 24.5 / 25.1 / 37.7 | 19.4 / 20.4 / 30.0 |
| 离村口触发区 | 15.0 | 18.1 | 13.5 |
| 离 NPC（长者 / 旅人 / 村民 / 井边妇人） | 11.8 / 12.9 / 14.5 / 20.6 | 12.4 / 14.4 / 16.7 / 22.5 | 12.4 / 12.8 / 13.8 / 19.9 |
| 离宝箱 C / D / E | 20.0 / 16.0 / 17.5 | 24.5 / 20.6 / 22.1 | 17.0 / 13.0 / 14.5 |
| 离传送点 | SampleScene 里没有 | 同左 | 同左 |
| 离最近碰撞体 | `Fence_Rail` 6.96、`Wall_Left` 7.18；5 米半径内无任何碰撞体 | `Wall_Left` 5.6、`Fence_Rail` 6.01 | `Fence_Rail` 7.96 |
| 离地图边缘（西 x=−11 / 南 z=−11.6） | 7.0 / 3.6 | 2.5 / 5.1 | 10.0 / 2.6 |
| 院内玩家会不会进发难 / 察觉圈 | 院内最南点（围栏前 z≈−0.7）离它 7.3 米，> 6 | 同量级（围栏 6.0） | 8.0 |
| 离旧位置 (−4.2, 8.4) | 16.4 | 15.5 | 17.7 |

三个都满足「≥8 米」「≥10 米离巡逻线」「≥6 米离触发区 / NPC / 箱子」。

**碰撞盒依据（均为 `SampleScene.unity` 展开值）**：A 点所在是 `Ground`（x −11..19、z −11.6..18.4，顶面 y 4.89）。以 A 为圆心 7 米内没有任何碰撞体（最近是 `Fence_Rail`，z = −1，7.0 米；`Wall_Left` 西南角 (−6.5, −1.1) 7.2 米；宝箱 D 在 (12, −8) 16 米）。冲刺最长 5 米（发难半径）+ 接触距离，不会碰到围栏。不确定处见 §不确定项。

### A.3 从出生点到 A 的建议路线（折线）与离巡逻线最近距离

院子唯一出口是演出触发区（x 8..11）以东、巡逻线西端（x 13.86）以西那条缝，所以必须复用 W6 的 `SoutheastHoldRoute` + `SoutheastGapCrossing`（等预判窗口再冲）：

| 段 | 起 → 止 | 离主怪线 | 离乙线 | 离触发区 |
| --- | --- | --- | --- | --- |
| 1 | 出生点 (−4, 3.4) → (3.5, 5) | 10.5 | 10.4 | 4.6 |
| 2 | (3.5, 5) → (8.5, 7.2) | 6.6 | 5.8 | 2.7 |
| 3 | (8.5, 7.2) → (9.6, 7.4)（缝口候场点，停下等窗口） | 5.8 | 4.9 | 3.2 |
| 4 | (9.6, 7.4) → (11.6, 4.6) | 2.6 | 2.3 | 0.9 |
| 5 | (11.6, 4.6) → (11.6, −2)（**穿缝，预判窗口内一口气跑过**） | 2.3 | 2.3 | 0.6 |
| 6 | (11.6, −2) → (6, −5) | 5.9 | 7.4 | 3.1 |
| 7 | (6, −5) → (−1, −6.6) → 站到 BOSS 正北 1.4 米 (−4, −6.6) | 11.5 | 12.7 | 6.3 |

- 离巡夜人线全程 ≥ 19.1 米。全长约 41 米，跑（5 m/s）约 9 秒，走约 14 秒，加上预判窗口等待（≤ 40 秒，`GapCrossingTimeoutSeconds`）。
- **穿过橙区是绕不开的**：第 3–5 段 ≈ 6 米路程离两条巡逻线西端只有 2.3–2.6 米（红区 2、近距 1.5 之外，靠正面视野锥才会被发现），与 Monster / Identity / Stealth 回放现用的是同一条缝、同一套预判，不新增风险。其余段都在两只的警戒半径（主怪 4.5 / 乙 6）之外或视野锥之外。
- 对「手玩」：同一条路，没有别的选择；北侧路线（观察点北上）见 A.0 第 3 点，不推荐。

### A.4 推荐 A (−4.0, −8.0)

理由：
1. 离非 Battle 回放最近 9.45 米，余量 1.45 米；B 余量 1.6 米但贴着西墙 / 地图西沿（离 x = −11 只有 2.5 米，离 `Wall_Left` 5.6 米，刚好压在半径边缘），C 余量只有 0.83 米。
2. 它在院子正南方 7 米，院内任何位置（含围栏前）都在 BOSS 的发难半径 5 与察觉范围 6 之外，院内回放不会因为 BOSS 再被打扰；同时在探索相机前景（不被 `Wall_Back` 挡），5 米内无碰撞体。
3. 路线比 B 短 6 米，且到 BOSS 的最后一段（(−1,−6.6) → (−4,−6.6)）在发难半径外 5 米处起步，「进半径 → 站位 → 按 E」仍能在 3 秒反应期内完成（4 米到接触站位，跑 / 走都 < 3 秒）。
4. 剧情一句话：站在村子南面围栏外的空地上，像守着村外进出口的头目。

Battle 回放要随之改的常量（`BattleShowcase.cs`）：`BossStandPoint` :126 → (−4, −6.6)；`AggroWatchPoint` :138 → 原位正北 4 米 (−4, −4.0)；`AggroBetweenRingsPoint` :141 → 5.5 米 (−4, −2.5)；`AggroOutsidePoint` :144 → 6.5 米 (−4, −1.5)（离围栏 z = −1 太近，需改为 (−6.5, −3) 或把「圈外」点放在西 / 东侧，留给实现者定）；去 BOSS 的走位从直线改为 `GoThroughSouthGap` + 自南向北；文件头 :9、:38、:41-43 的 5.004 米 / 演练桩 (2.4, 8.8) 说明作废（演练桩站位可以改回 (0.6, 8.8)，但**不改也不影响**，BOSS 已离它 14 米）。这些由实现波定，本文件只给数。

## 任务 B：W7 回放名单

①=打中市令或被市令打到（升级回合制，世界暂停）；②=走到巡夜人 6 米内；③=在 BOSS 现位置 (−4.2, 8.4) 5 米内停留 > 3 秒（挪位前的影响）；④=在推荐位置 A 5 米内停留（挪位后的影响）；⑤=依赖 Taming 选目标。
「是 / 否」后括号是 `path:line`，路径省略前缀 `Assets/_Project/Scripts/Tests/Showcase/`。

### B.1 结论表

| 模块 | 文件 | ① 市令 | ② 巡夜人 6 米 | ③ 现 BOSS 5 米内 >3 秒 | ④ 新位置 5 米内 | ⑤ Taming | 结论 | 一句理由 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Battle | Battle/BattleShowcase.cs | 是（`Drill_DownedEndsTheStageInsteadOfSticking` :376-412 走巡逻线被市令打倒，已改过） | 否 | 本模块就是 BOSS 的回放：站位 :126、:138-144 全以 (−4.2, 8.4) 为圆心 | **要改**：全部站位常量要随 BOSS 重算（见 A.4） | 否 | **必须跑** | BOSS 挪位后所有 BOSS 站位 / 路线都变了，发难三条用例的时序要重验 |
| Player | Player/PlayerShowcase.cs | **是**：:135-141 `StrikeMonster` 打市令，:140-141 断言「怪物生命 −1 且 Hostile」；:144-148 站着挨打。打中即升级，怪物不掉血 | 否 | 否：`RouteToPatrol` 起头 4.93 米，约 1 米 / 不到 0.3 秒（:135 `GoToPatrolLookout`）；出生行 5.00（:28-29） | 否（11.4） | 否 | **必须跑（预期红，先改回放再跑）** | W6 只改了 Identity / Monster / Stealth / Battle，Player 的打怪断言还是旧规则 |
| Disguise | Disguise/DisguiseShowcase.cs | **是**：:69-72 `StrikeMonster` 循环；:62-64「取消伪装后怪物立刻出手」= 市令打玩家；:96-100 短按攻击断言 `monster.Health == before-1` | 否 | 否（同 Player，路线擦边） | 否 | 否 | **必须跑（预期红，先改回放再跑）** | 同上，两条用例都要市令掉血 / 持续交手，升级后世界暂停 |
| Mirror | Mirror/MirrorShowcase.cs | **是**：`ThreeHits_ShatterAndRestart` :317-400，:335 `GoToPatrolLookout`，:346-349 走近 `ApproachUntilHostile` 站着挨三刀（`enerme`，:42）；期望三道裂痕 → 镜碎，第一下命中即升级回合制 | 否 | 否：长者西 (−2.2, 3.4) 离 BOSS 5.39 米（:164、:258、:283） | 否 | 否 | **必须跑（预期红，先改回放再跑；仅 `ThreeHits_ShatterAndRestart`）** | 其余 5 条（村民 / 井边妇人 / 自照 / 对白挡镜 / 剧情裂痕）不碰市令，不必跑 |
| Exploration | Exploration/ExplorationShowcase.cs | **可能**（推断，没跑）：:407 `Reset(17.25, 3.4)` 直接站在主怪巡逻线上（线 x 13.86..21.86、z 3.4），回放里等 3 秒以上；:368-375 坡道脚 (23.5, −2.5) 往北推到 z ≥ 9，擦过线东端 (21.86, 3.4) 1.6 米（在红区 2 之内） | 否（坡道脚离巡夜人 9.2 米） | 否（最近 6.4 米） | 否（≥ 9.45） | 否 | **跑 2 条**：`Occluder_FadesBridgeWhenPlayerBeneath`、`MultiLevel_RampLeadsToDeck` | 站在 / 穿过市令巡逻线，打中玩家会升级并关掉 Gameplay 输入，影响推杆类断言；其余 6 条站位离巡逻线 ≥ 5.9 米不用跑 |
| Session | Session/SessionShowcase.cs | **可能**（推断）：:162 `Reset(7.5, 4.9)`，:184 往右推 1 秒到约 (10.5, 4.9)，随后保存 / 离场 / 继续，一直站着；离主怪线西端 3.7 米（市令警戒 4.5），主怪折返西行时在视野锥内（夹角约 24° < 37.5°）→ 警戒 4 秒后转敌对追来 | 否 | 否（最近 6.8） | 否 | 否 | **跑 1 条**：`SaveQuitContinue_RestoresSameProgress` | 在市令警戒圈里长时间站桩，升级会打断存档 / 读档流程；批处理节奏倍率 0 时时间短，标准节奏风险更大 |
| IsometricExploration | IsometricExploration/IsometricExplorationShowcase.cs | 否：楼梯路线 :100-113、等主怪过 x 16.3（`MonsterLeavesStairsFoot`）正是为避它设计的；楼梯口离**乙**线 0.5 米是旧状况，乙不升级 | 否 | 否（最近 8.4） | 否 | 否 | 不用跑 | 新规则只改「打中 / 被市令打到」，这条不交手 |
| Identity | Identity/IdentityShowcase.cs | 是（设计内，W6 已改） | 否 | 否 | 否 | 仅读 `Taming.TargetIds` 取巡逻者名单（`ShowcasePatrolForecast.cs:84-102`），不用选目标 / 驯服 | 不用跑 | W6 PASS；BOSS 挪位离它的路线 ≥ 13.8 米 |
| Monster | Monster/MonsterShowcase.cs | 是（设计内，W6 已改，三条升级用例） | **是**（设计内）：`Caught_Xunye_KnockdownThenDowned` :642-690 走到 `XunyeStagingPoint` | 否 | 否 | 同 Identity | 不用跑 | W6 PASS；BOSS 挪位离它的路线 ≥ 16.7 米 |
| Stealth | Stealth/StealthShowcase.cs | 是（设计内，W6 已改） | **是**（设计内）：`ExecuteKey_OnXunye_DropsWithoutEndingEncounter` :480-509 | 否 | 否 | `CollectPatrollers` 读 `Taming.TargetIds`（:659-674），不用选目标 | 不用跑 | W6 PASS；BOSS 挪位离它的路线 ≥ 16.7 米 |
| CharacterPuppet | CharacterPuppet/CharacterPuppetShowcase.cs | 否 | 否 | 擦边：:214-218 往北推 0.5 秒到约 (−4, 4.9)，离 BOSS 3.5 米，在 5 米内约 1.3 秒（含两次 5 帧停稳，`PlayerDrive.cs:125-149`）< 3 秒 | 否 | 否 | 不用跑 | 挪位后这段擦边也消失 |
| Dialogue（含 `DialogueBubbleShowcase`） | Dialogue/DialogueShowcase.cs、Dialogue/DialogueBubbleShowcase.cs | 否：站位都离主怪线 ≥ 7.1 米（气泡路线终点 (8, 7.4)） | 否 | 否：最近 5.46（(−2, 3.4)，:35-41） | 否 | 否 | 不用跑 | 不碰任何受影响的东西 |
| Inventory | Inventory/InventoryShowcase.cs | 否 | 否（(16, −8.9) 离巡夜人线 13 米，:41、:231） | 否 | 否 | 否 | 不用跑 | 同上 |
| Narrative | Narrative/NarrativeShowcase.cs | 否 | 否 | 否：站在出生点附近，**5.004 米，余量 4 毫米**（:207 见证人在出生点右 0.8 米） | 否 | 否 | 不用跑 | 挪位后消除这 4 毫米的依赖 |
| Performance（`PerformanceShowcase` + `ScenePerformanceShowcase`） | Performance/ | 低：瞬移进触发区 (9.5, 2.5)（:189 / :273），演出冻世界；演出结束后站在离主怪线西端 4.45 米（警戒 4.5）的位置，只有几秒检查 | 否 | 否 | 否 | 否 | 不用跑 | 世界冻结期间不会升级；恢复后检查点很短 |
| Quest | Quest/QuestShowcase.cs | 否：瞬移到 (5, 3.4)、(25, 12)（上层甲板，:130-160） | 否 | 否 | 否 | 否 | 不用跑 | 离市令线 ≥ 8.9 米 |
| Taming | Taming/TamingShowcase.cs | 否：站在出生点，只推被控目标向右 0.4 秒（:77 前后），玩家本人不动 | 否 | 否：出生点 5.004 | 否 | **是**（本身就是驯服选目标；W6a FAIL，归协作者） | 不用跑 | 红是协作者的线，按约定只报告；不属于本次改动的验证对象 |
| TurnBased | TurnBased/TurnBasedShowcase.cs | 否：逻辑直调开战接口，不走位（:472 进世界后站出生点） | 否 | 否：出生点 5.004 | 否 | 否 | 不用跑 | 站桩，不碰受影响的东西 |
| Title | Title/TitleShowcase.cs | 否 | 否 | 否：进世界后站出生点 | 否 | 否 | 不用跑 | 同上 |
| World | World/WorldShowcase.cs | 否：用另外两张世界场景（:80-96） | 否 | 否 | 否 | 否 | 不用跑 | 不在 SampleScene |
| Replay / Rhythm / RhythmLibrary / LailaFace / SelfTest | Replay/、Rhythm/、LailaFace/、SelfTest/ | 否 | 否 | 否 | 否 | 否 | 不用跑 | 独立场景或代码搭场景（`Replay/DemoWorld.cs`；Rhythm 场景 `RhythmDemo.unity`），不涉及 SampleScene 的市令 / BOSS |

### B.2 必须跑名单（全名，按 group 子串匹配）

| # | 名单（过滤串） | 状态 | 子串冲突 |
| --- | --- | --- | --- |
| 1 | `Game.Tests.Showcase.Battle` | BOSS 挪位后重跑，**要先改站位常量** | 无冲突（`BattleWhiteboxView` 在 `Game.Tests.Showcase.TurnBased` 命名空间，不是测试） |
| 2 | `Game.Tests.Showcase.Player` | 预期红，先改 `AttackThenTakeHits_HealthDropsToDeath`（升级回合制口径）再跑 | 无 |
| 3 | `Game.Tests.Showcase.Disguise` | 预期红，先改两条用例再跑 | 无 |
| 4 | `Game.Tests.Showcase.Mirror.MirrorShowcase.ThreeHits_ShatterAndRestart` | 预期红，先改再跑；其余 5 条不跑 | 写到用例级，避免带出另外 5 条 |
| 5 | `Game.Tests.Showcase.Exploration.ExplorationShowcase.Occluder_FadesBridgeWhenPlayerBeneath` | 站在主怪线上，验是否被升级打断 | 命名空间 `…Showcase.Exploration` 不是 `…Showcase.IsometricExploration` 的子串，但**不要只写 `Exploration`**（会带出 `IsometricExploration`） |
| 6 | `Game.Tests.Showcase.Exploration.ExplorationShowcase.MultiLevel_RampLeadsToDeck` | 同上，擦过线东端 | 同上 |
| 7 | `Game.Tests.Showcase.Session.SessionShowcase.SaveQuitContinue_RestoresSameProgress` | 在警戒圈里站桩，验存档流程是否被升级打断 | 无 |

不跑（含 W6 已 PASS 的）：Identity、Monster、Stealth、CharacterPuppet、Dialogue、Inventory、IsometricExploration、Narrative、Performance、Quest、Taming、TurnBased、Title、World、Replay、Rhythm、LailaFace、SelfTest。`PerformanceEvidence` 类耗时实测不列。

**子串冲突清单**（写过滤串时注意）：
- `Game.Tests.Showcase.Dialogue` 带出 `DialogueShowcase` 与 `DialogueBubbleShowcase`（同命名空间，Module 也都叫 Dialogue）。
- `Game.Tests.Showcase.Performance` 带出 `PerformanceShowcase` 与 `ScenePerformanceShowcase`。
- `Game.Tests.Showcase.Rhythm` 带出 `RhythmShowcase` 与 `RhythmLibraryShowcase`。
- `Game.Tests.Showcase.TurnBased` 命名空间同时含 `TurnBasedShowcase` 与 `BattleWhiteboxView` / `ScriptedRandomStream`（后两个不是测试，无影响）。
- 裸写 `Exploration` 会带出 `IsometricExploration`，裸写 `Battle` 会匹配 TurnBased 里含 Battle 的名字；一律用 `Game.Tests.Showcase.<模块>` 全前缀。
- 派单还要遵守 guard.js：PlayMode 回放必须限定到单个模块，所以上表 7 行要**分 7 次**派，不能合并成一次。

### B.3 跑之前要先改的（不属于本文件的执行范围，供 W7 派单参考）

- Battle：站位常量（A.4）。
- Player、Disguise、Mirror(ThreeHits)：回放里「打中市令 / 被市令打」的断言要换成升级回合制口径（参考 `Monster/MonsterShowcase.cs` 的 `Escalation_*` 三条与 `ShowcaseScenario.TurnBattle.cs` 的开战观察件）。这三个不在 W6 范围，是 W6 之后才暴露的漏网。
- Exploration / Session：不改，先跑；红了再决定是改站位（避开主怪线）还是接受。

## 不确定项

1. **没有编辑器碰撞查询**：A 点周围无碰撞体、地面连续，全部来自场景 YAML 的 BoxCollider / Capsule / Mesh 展开（`Ramp_South` 带旋转，只当外框；`Cone_*`、`Tower` 是 Mesh / Capsule 近似为包围盒）。MeshCollider 没逐个展开，但它们都在 x ≥ −0.2 的院内或东侧，离 A ≥ 10 米。
2. **南区是否真能从缝口走通**：沿用 W6 的 `GoThroughSouthGap` / `SouthStagingPoint` (17, −1.5) 能走到的结论；从缝口往西南走到 (−1, −6.6) 这一段（第 6–7 段）没有回放走过，是推断。
3. **相机是否看得到 A**：按「相机在南面往北看」推断，南区是前景；没实测 A 点是否出画。院北被墙挡视线的 3.6 米也是反推数。
4. **① 的判定**：Player / Disguise / Mirror 是读代码得出的确定结论（回放直接要求打中市令或被市令打）；Exploration、Session 是「站位在市令警戒圈内」的推断，实际会不会触发取决于主怪当时的巡逻相位（随机停步 7–10 秒），所以只列「可能」，是否红要跑了才知道。
5. **Exploration 其余用例**：我只逐条看了围栏、坡道、桥、塔的用例；`Exploration` 文件其余几条（走跑、开箱，围绕 (−4, 2)、(0.5, 2)、(7.5, 4.9)）按站位判为不受影响，没逐行读断言。
6. **IsometricExploration 的楼梯口**（18.5, 5.5）离巡逻者乙线端点 (17.86, 5) 只有 0.8 米，是旧状况，回放只避让主怪；乙不升级，但会掉血 / 追人，本次没复核它是否仍稳。
7. **Taming 红**的原因我没查，沿用 W6a 的结论（归协作者）。Monster / Identity / Stealth 读 `Taming.TargetIds` 只是取名单，若 Taming 侧改了 `TargetIds` 顺序（主怪在首位）它们会连带受影响，本次没动。
8. **BOSS 挪位的副作用没查**：`SampleSceneCombatWiringTests` 只读 BOSS 是否挂 `BossAggro`、不读坐标（已 grep：EditMode 里的 (−4.2, 8.4) 都是测试自己 new 的对象，不是读场景）；但 `Npc_SampleBoss` 的 `NarrativeTrigger`（`sample_boss_npc`）半径 2、`DialogueInteractable` 半径 2 都按物体自身位置算，不受影响；`SampleScene.unity` 工作区已有 1100+ 行别处带来的未提交改动，挪位时要按 hunk 提交。
9. **BOSS 站点余量**：发难半径比较用的是 `<=`（`BossAggro.cs:468`），出生点离现 BOSS 5.004 米，余量 4 毫米；这是挪位前各「站出生点」回放的隐含依赖，挪位后消失。
