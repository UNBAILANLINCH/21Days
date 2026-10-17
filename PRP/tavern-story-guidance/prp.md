# 酒肆划拳与正式剧情接线设计

日期：2026-10-18。状态：用户已确认，实施与验证中。需求与固定验收见 [prd.md](prd.md)，实际进度见 [tasks.md](tasks.md)。

## 实施前核对的基线

| 位置 | 实际状态及本轮影响 |
| --- | --- |
| `Runtime/Tavern/` | 只有直接饮酒意图、分对象醉意与倍率，没有划拳规则或界面；现配每杯 10，玩家／主人上限 100、客人上限 20 |
| `Runtime/ShopPlaytest/ShopPlaytestState.cs` | 已接现有场景、身份、三店成果和酒肆；酒肆选项仍直接加醉意。挑战通过 `StartAsync` 新开独立故事，会覆盖主线，必须一起修正 |
| `Tables/Data/narrative/spotlight_current.json` | 正式稿已入表，共 42 个阶段；20 个等待点尚无实际完成来源，也都未开放局部遭遇 |
| `Tables/Data/dialogue/21000.json` 至 `21011.json` | 包含本轮开场、接案、入镜和会面。21010 是楼梯口相遇；21011 虽标为阶段二，却包含本轮要完成的巷中关系对白，结束于殷红带路 |
| `Runtime/Session/` | 新游戏先显示全屏操作说明，再直接进五店；存档已有剧情稳定边界和跨槽代际守卫 |
| `Runtime/ShopPlaytest/ShopPlaytestWorld.cs` | 已有五店活动区、操作距离与离区缓冲，可复用区域事实；不能用“访问过店铺”替代玩法完成 |
| `Runtime/World/` | 世界表和两张灰盒可加载，但正常 Session 尚未接世界路径。泾阳灰盒不是正式稿中的苏州，不能直接改显示名冒充 |

以上为设计时的源码和配置读取结果，实施后的状态及Unity实测另记tasks.md。工作区已有其他任务的场景、字体、生成表和素材改动；本轮只增量接入并保留这些改动。

## 六项设计决定

### 1. 划拳只增加一套纯规则，复用醉酒结算

在 Tavern 中增加纯 C# `TavernFingerGuessRules`，判定出指、喊数及结果；扩展现有不可变 `TavernIntent` 和控制器，将每局唯一身份、目标、双方选择一起交给规则处理，再复用 `TavernDrunkRules` 施加一杯。视图只发选择／确认／退出事件，不改醉意、不调服务。

合法出指为 0～5，喊数只接受“自己的出指数到自己的出指数加 5”，避免喊自己不可能猜中的数。每局在玩家确认前固定对手的出指和喊数，确认后才揭晓；对手不读取玩家答案反推结果。随机沿用工程现有随机能力及战斗会话派生流约定，不借表现流决定输赢，也不消费探索 tick 的随机流。

用户实施中追加动态难度：三名对手有不同基础强度与可观察偏好；每名对手保存最近 24 局的玩家出指／喊数，使用近期加权频率估计玩家下一手，计算 36 种合法选择的期望净胜负。按强度概率使用最佳历史应对，否则使用带个人偏好的基础策略；前三局收集样本。玩家非平局胜负更新平滑表现估计，SO 曲线把估计映射为强度修正，每局强度变化不超过 0.035。平局不当失败，不根据当前答案改拳；历史、估计和强度随槽保存。曲线不保证指定胜率。

每局只允许一次确认。开始、确认和结算均检查当前会话、有效目标、距离、酒客身份及三组成果；取消、离区、卸载或换槽使未完成局作废。参数、醉意和消息阈值放现有 `TavernConfig`，规则不读 Unity 时间、不写 SO。客人消息在达到 20 时解锁，主人喝过一杯即可挑战；消息已获事实进入当前槽，离区清醉意不抹掉已获消息。

### 2. 正式文本仍以既有 Narrative／Dialogue 为唯一进度来源

增加位于 Narrative 适配层的 `SpotlightStoryFlow`，负责当前章节的场景绑定、等待点结果接入和正常入口启动；它不另建状态机或通用脚本解释器。启动前等待 Session 和实际场景就绪，只有未开始的正式新局启动 `spotlight_current`；继续游戏沿原阶段恢复，不能再次从开场覆盖存档。

主目标使用场景中预摆、整个探索流程都有效的稳定剧情锚点，不绑会被暗杀或隐藏的店员。五店填写稿的对白迁到既有 Dialogue 内容表，沿用现有分角色选项与真实玩法返回；演出说明和任务建议不直接当台词播放。占位对白 1004 不再作为正式酒肆战前台词。

本轮仍以 `Assets/Scenes/SampleScene.unity` 为可玩与回放舞台：补独立的序章演出／切磋区、接案与书库交互点、入镜点、轮回堂问询点和战后会面点；非操控的地点段用既有对白与分段转场承接。配置区分故事地点与场景资产，不把同一场景资产称为一张完整人间地图，不修改泾阳灰盒的地理身份。正式美术缺件沿用已有表现，本轮交付连续可玩内容接线。

### 3. 教学切磋和酒肆 BOSS 都通过局部遭遇回到主线

`action_r0070` 开放李诏阳切磋局部遭遇，复用已有战斗流程和独立教学战斗档；只有真实胜利才能进入 21005，失败回当前等待并可重试。它是序章切磋，不改变五店普通对象只走潜行取皮的规则。

`action_r0197` 开放 `shop_boss_battle` 局部遭遇，挑战目标仍是 `shop_boss`，保留现场 `sample_boss` 醉意输入。主线 `Success` 出口续到战后，`Downed` 不设推进出口，沿现有父帧恢复机制返回原等待。门槛由当前成果、酒客身份与主人醉意共同校验；不能直接提交“已来过五店”成功。

只在两个明确等待点开放对应遭遇，遭遇候选按主线阶段限定，避免允许酒肆时误开其他样例剧情。异步发起前固定 `Generation / ActivationId / TargetId / ActionRequestId`；战后不再用“当前故事为 null”判断成功，也不依赖独立 `StartAsync` 的代际加一。通知回调只标记待处理，推进放在协调流程中，避免在 `OnChanged` 内重入。

### 4. 软引导由节点、区域和已有事实共同选择

新增纯 C# `StoryGuideRules` 从只读上下文选出当前一条提示；上下文包含故事阶段、有效区域、角色／皮、三店结果及酒肆进度。区域位置、范围、入区／出区缓冲和目标引用由预摆 `SpotlightSceneBindings` 提供，节点到提示／区域的映射放 `SpotlightStoryConfig`；不每帧搜索场景对象，不在硬编码世界坐标上判教程。

使用独立的 `StoryGuideHudView` 承载一条操作提示，复用现有 HUD 层与统一交互提示；当前目标由正式任务配置接既有 Quest HUD，不让两个控制器同时写同一块文字。输入提示读取绑定。三店并行段按当前所在店选择未完成动作，离店回到三组成果概览；取皮和穿皮分成两步，不能拿到皮就提示玩法资格已满足。

教程完成由真实玩法事实推导，不新增“看过教程即完成玩法”的进度。进入对白、小游戏、战斗、菜单或沉浸模式时隐藏，退出后重算。HUD 不进阻挡操作的面板栈、不暂停、不抢焦点；现有新游戏两条入口的全屏操作说明改由这套分段提示承接，保留标题转场。已完成事实读档后仍有效；未完成动作重入区域可以再次提示。

### 5. 关键事实进入原有槽位，保存沿用稳定边界

Narrative 继续保存主／父帧与剧情标记，ShopProgress 继续保存成果、装备皮、分对象醉意及酒肆完成，新增已获消息记录并提供兼容默认值；Quest 保存正式目标。短暂的选择、UI 打开状态和未确认划拳局不落盘。每次写分区重新取当前槽，不缓存读档前的数据对象。

在正式故事等待点加载时，按节点配置还原有效舞台、NPC、落点和引导；五店段保留当前探索快照。旧五店档缺正式故事时，保留原五店流程和成果，继续可玩，不清空背包或强制重播序章；完整正式开场从新局体验。旧档没有主线时可保留原独立挑战入口，已经运行正式主线时必须走局部续接。

对白、战斗和在途划拳不生成半成品存档，保存请求等完整结算后再处理。划拳打开期间显式参与现有保存闸门，不能仅依赖 UI 是否盖屏；消息／醉意与本局结算一并更新，避免退出只保存其中一半。回标题和换槽撤销旧订阅、目标与在途任务；本轮终点停在 `enter_r0293` 的稳定等待，显示本段完成，不自动进入 21012 名簿调查，不把它标为全游戏结案。

### 6. 沿现有模块增量接线，验证真实入口与失败恢复

不新建程序集或新章节框架。通过 Boot 根作用域既有 Installer 注册；视图地址等于类名，加入 UI 组。场景、SO、预制体与 `.meta` 由 Unity 编辑器保存；Luban 表从源 JSON 重新生成，不能手改生成 C# 或 bytes。意图、迁移、拒绝、异常和耗时记录到现有遥测；不按帧记教程或距离日志。

回放从 Boot／标题进入 `SampleScene`，使用隔离测试槽，真实取得三店成果并完成划拳、战斗和会面；不能把临时发三件套或直接跳过等待点当连续通关。规则穷举与故障分支用定向测试，视觉回放只覆盖需要看见的交互与恢复。

## 本轮节点绑定

| 正式等待点 | 实际完成来源 | 软引导范围与内容 |
| --- | --- | --- |
| `enter_r0026` | 到客店院落的有效点位并交互李诏阳 | 院落入口显示移动／靠近对话 |
| `action_r0070` | 李诏阳教学切磋真实胜利，失败可再试 | 切磋入口及战斗界面显示对应操作，结束后清理 |
| `enter_r0083` | 到西城接案交互点 | 接案点近区显示对话，远区显示当前目标 |
| `enter_r0129` | 到书库交互点完成正式交互 | 书库范围显示查阅／对话 |
| `enter_r0171` | 完成入镜交互并到轮回堂问询点 | 入镜点与轮回堂分步提示，不提前弹出五店教程 |
| `enter_r0188` | 到坊市入口有效范围 | 显示寻妻目标，准备进入五店自由探索 |
| `action_r0197` | 三店成果门槛后，经划拳与局部遭遇击败酒肆主人 | 按皮是否拥有／穿戴、当前店下一步、缺件、酒客和醉意选择提示 |
| `enter_r0198` | BOSS 胜利后到酒肆楼梯口 | 战后提示前往楼梯口，触发 21010 相遇 |
| `enter_r0215` | 到巷口会面点 | 仅战后显示会面操作，完成 21011 后本轮收束 |

## 预计影响文件

以下为拟新建／修改范围，实施前逐项读对应模块规则。`N` 新建，`M` 修改；现有文件只改本轮职责，不覆盖工作区其他改动。

| 文件路径 | 类型与用途 |
| --- | --- |
| `Assets/_Project/Scripts/Runtime/Tavern/TavernFingerGuessRules.cs` | N，纯划拳判定与局身份 |
| `Assets/_Project/Scripts/Runtime/Tavern/TavernFingerGuessView.cs` | N，选择、揭晓与结算表现 |
| `Assets/_Project/Scripts/Runtime/Tavern/TavernOpponentLearning.cs`、`TavernLearningData.cs` | N，用户追加的历史学习、动态强度与保存数据 |
| `Assets/_Project/Scripts/Runtime/Tavern/TavernIntent.cs` | M，划拳确认的不可变输入 |
| `Assets/_Project/Scripts/Runtime/Tavern/TavernController.cs` | M，会话与饮酒结算 |
| `Assets/_Project/Scripts/Runtime/Tavern/TavernConfig.cs` | M，沿用醉意配置，补正式门槛 |
| `Assets/_Project/Scripts/Runtime/ShopPlaytest/ShopPlaytestState.cs` | M，正式店铺对白、划拳入口及局部 BOSS 遭遇 |
| `Assets/_Project/Scripts/Runtime/ShopPlaytest/ShopProgressSaveData.cs` | M，已获消息兼容恢复 |
| `Assets/_Project/Scripts/Runtime/Narrative/SpotlightStoryFlow.cs` | N，正式开场与等待结果接线 |
| `Assets/_Project/Scripts/Runtime/Narrative/SpotlightStoryConfig.cs` | N，本轮节点、目标、区域与提示配置 |
| `Assets/_Project/Scripts/Runtime/Narrative/SpotlightSceneBindings.cs` | N，预摆目标、落点与区域引用 |
| `Assets/_Project/Scripts/Runtime/Narrative/StoryGuideRules.cs` | N，当前提示的纯规则选择 |
| `Assets/_Project/Scripts/Runtime/Narrative/StoryGuideHudView.cs` | N，非阻断教程 HUD |
| `Assets/_Project/Scripts/Runtime/Narrative/NarrativeInstaller.cs` | M，根作用域注册正式协调器 |
| `Assets/_Project/Scripts/Runtime/Narrative/NarrativeService.cs` | M，只读节点查询和外部结果身份快照 |
| `Assets/_Project/Scripts/Runtime/Session/SessionTitleRouter.cs`、`SaveSlotsController.cs` | M，两条新游戏入口改接分段教程 |
| `Assets/_Project/Scripts/Runtime/Session/SessionStateAdapter.cs`、`SessionInstaller.cs` | M，划拳在途保存闸门及依赖接线 |
| `Assets/_Project/Data/ShopPlaytest/Tavern.asset` | 保留，新增字段取TavernConfig默认值；Inspector可调，无需手写资产 |
| `Assets/_Project/Data/Narrative/SpotlightStoryConfig.asset` | N，序章至会面节点和提示配置 |
| `Assets/_Project/Data/Battle/BossRosterConfig.asset` | M，增加独立李诏阳教学档，保留酒肆档 |
| `Assets/_Project/Prefabs/UI/TavernFingerGuessView.prefab`、`StoryGuideHudView.prefab` | N，UI 组地址与类名一致 |
| `Assets/_Project/Scenes/Boot.unity` | M，根 Installer 配置引用 |
| `Assets/_Project/Scripts/Tests/EditMode/Tavern/TavernFingerGuessTests.cs` | N，对应 A1、A3 |
| `Assets/_Project/Scripts/Tests/EditMode/Tavern/TavernOpponentLearningTests.cs` | N，学习、强度上限、深拷贝、拒绝重复确认与切槽隔离 |
| `Assets/_Project/Scripts/Tests/EditMode/Narrative/NarrativeCatalogTests.cs`、`NarrativeServiceTests.cs` | M，对应 A2、A5，复用已有类，不另建SpotlightStoryFlowTests |
| `Assets/_Project/Scripts/Tests/EditMode/Narrative/StoryGuideRulesTests.cs` | N，对应 A4、A5 |
| `Assets/_Project/Scripts/Tests/EditMode/Session/` | 既有测试保留，本轮改动由真实标题入口、划拳在途拒绝保存及继续回放验证 |
| `Assets/_Project/Scripts/Tests/EditMode/ShopPlaytest/ShopPlaytestStateTests.cs` | M，对应 A3、A5 |
| `Assets/_Project/Scripts/Tests/Showcase/Tavern/TavernShowcase.cs` | M，划拳与醉意视觉回放，对应 A1、A3、A6 |
| `Assets/_Project/Scripts/Tests/Showcase/Narrative/NarrativeShowcase.cs`、`NarrativeShowcase.Spotlight.cs` | M／N，正式完整流程、另一种三店顺序、区域提示和恢复，对应 A2、A3、A4、A5、A6 |
| `Assets/_Project/Scripts/Tests/Showcase/Title/TitleShowcase.cs` | 未改，真实新局入口由上述连续回放覆盖 |

沿用项目既有目录的配套内容：

- `Assets/Scenes/SampleScene.unity`：连续流程舞台，补上述序章／接案／入镜／会面对象、有效碰撞与范围，保留已有五店建筑和人员引用；不添加会误启动第二套移动驱动的 `SpawnAnchor`。
- `Assets/AddressableAssetsData/AssetGroups/UI.asset`：仅登记两张新视图，以编辑器实际分组路径为准。
- `Tables/Data/narrative/spotlight_current.json`、`shop_boss_battle.json`：等待点资格、局部续接与正式战前内容；另增加教学切磋故事及对应遭遇表行。
- `Tables/Data/dialogue/`、`Tables/Data/quest/`：五店填写稿迁入正式对白、当前主目标与真实完成条件；新增编号先查重，不覆盖 21000～21020。
- `Assets/_Project/Data/Config/`：由 `scripts/gen-tables.ps1` 生成受影响配置；不手写生成物。
- Tavern、Narrative、ShopPlaytest、Session 对应模块说明和 `HANDOVER.md`：实现后同步实际完成与验证结果。本轮不提前把这些项目写成已完成。

## 验证与风险

A1 使用规则组合穷举，覆盖双方都中、各自单中、双方不中与边界输入；另测重复／迟到确认及离区取消。A2／A3 分别验真实等待结果和两种三店顺序。A4 覆盖节点、区域、事实与遮挡 UI 的组合和边界缓冲。A5 覆盖失败重试、父帧恢复、旧档兼容、继续和切槽。A6 运行定向 EditMode、相关 PlayMode／Showcase、C# lint、Unity 资产体检和文档 gc。

关键风险是主线覆盖、迟到回调、醉意结算与消息保存不同步、区域与楼层误触发。对应保护已纳入上面的决定及固定验收，不以重置存档或全自动通过等待点处理。测试用隔离槽，保留用户真实存档；不得将当前工作区的其他改动一并回退。

设计已获“做吧”确认；代码、场景和视图已落地，实施结束以 tasks.md 的实际测试与视觉验收记录为准。

设计阶段检查曾报告两份Dynamic字体缓存与九处旧战斗文档行号引用。收尾已修失效行号，最新扫描与资产检查以tasks.md为准；字体共享改动不回退。
