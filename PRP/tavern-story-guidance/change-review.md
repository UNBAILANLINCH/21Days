# 划拳、正式剧情与软引导待审清单

状态：功能已实施；相关EditMode413/413，连续Showcase2/2及划拳专项1/1通过，Unity资产体检四项0问题，lint零违规。2026-10-18用户明确授权暂存提交，提交身份以Git历史为准；人工视觉及手感尚未确认。设计依据见[PRP](prp.md)，逐次实测见[tasks.md](tasks.md)。

## 代码与接线

下表路径前缀为 `Assets/_Project/`，表源、主场景与Addressables另写完整路径。新增Unity资产均须带编辑器生成的同名.meta。

| 文件路径 | 位置 | 本轮改动 |
| --- | --- | --- |
| `Scripts/Runtime/Tavern/TavernFingerGuessRules.cs` | Begin／TryResolve／Cancel | 合法组合、隐藏选择、单局身份和一次性结算 |
| `Scripts/Runtime/Tavern/TavernOpponentLearning.cs`、`TavernLearningData.cs` | Choose／Observe／AdjustStrength／Capture | 最近24局加权学习、表现平滑、强度缓变与深拷贝 |
| `Scripts/Runtime/Tavern/TavernFingerGuessView.cs` | 输入与结果显示 | 六种出指、六种合法喊数、确认、下一局和离席 |
| `Scripts/Runtime/Tavern/TavernConfig.cs`、`TavernIntent.cs`、`TavernController.cs` | 配置／开始／确认／恢复 | 不同对手基础强度、可调曲线、实际罚酒、学习保存与埋点 |
| `Scripts/Runtime/ShopPlaytest/ShopPlaytestState.cs` | Greeting／OpenGuessAsync／StartBattleAsync／Persist／UnbindAsync | 正式角色对白、划拳生命周期、局部BOSS续接、消息和学习随槽保存 |
| `Scripts/Runtime/ShopPlaytest/ShopProgressSaveData.cs` | v5／Migrate | v3成果兼容，v4消息、v5学习记录提供旧档默认值 |
| `Scripts/Runtime/ShopPlaytest/ShopRewardRules.cs` | MissingDisguise／HasDisguise | 位图查固定字符串，避免教程每帧分配 |
| `Scripts/Runtime/ShopPlaytest/ShopPlaytestInstaller.cs` | TavernController工厂 | 会话主种子派生划拳流，不消费探索随机流 |
| `Scripts/Runtime/Narrative/SpotlightStoryFlow.cs` | Start／Tick／InteractAsync／UpdateGuide | 新局自动开正式故事、真实结果回写、任务900与非阻断提示 |
| `Scripts/Runtime/Narrative/SpotlightSceneBindings.cs`、`SpotlightStoryConfig.cs` | 点位／范围／节点配置 | 九个稳定锚点、有效节点显隐、近区与离区缓冲 |
| `Scripts/Runtime/Narrative/StoryGuideRules.cs`、`StoryGuideHudView.cs` | 选择／显示 | 按节点、身份、店铺和实际成果提示；不暂停或挡射线 |
| `Scripts/Runtime/Narrative/NarrativeInstaller.cs`、`NarrativeService.cs` | 注册／只读查询／ResultIntent | 根作用域接线与提交前固定结果身份 |
| `Scripts/Runtime/Session/SessionTitleRouter.cs`、`SaveSlotsController.cs` | 两条新局入口 | 全屏说明由正式开场分段提示承接，保留原转场 |
| `Scripts/Runtime/Session/SessionStateAdapter.cs`、`SessionInstaller.cs` | NarrativeStable／可选依赖 | 开场启动与未揭晓拳局参与保存门闸 |
| `Scripts/Editor/ShopPlaytest/TavernFingerGuessBuilder.cs`、`SpotlightStoryBuilder.cs` | 人工接线菜单 | 创建既有舞台内的章节锚点及两张UI，保留原布局 |
| `Data/Narrative/SpotlightStoryConfig.asset` | 正式节点／教程 | 已由Unity编辑器生成并接线 |
| `Data/Battle/BossRosterConfig.asset` | spotlight_mentor行 | 24血教学对局，保留现有BOSS档案 |
| `Data/Identity/IdentityConfig.asset` | ledgerLimit | 当前演示由3调9，避免连续换第四身份立刻露馅；纯规则默认3不变 |
| `Prefabs/UI/TavernFingerGuessView.prefab`、`StoryGuideHudView.prefab` | UI引用 | 已接按钮、文案、非阻断HUD及地址 |
| `Scenes/Boot.unity` | NarrativeInstaller.spotlight | 正式流程配置引用；共享场景中的其它改动保留 |
| `Assets/Scenes/SampleScene.unity` | SpotlightStory根／酒肆目标 | 追加节点、教学目标、会面角色占位和局部战斗种类 |
| `Assets/AddressableAssetsData/AssetGroups/UI.asset` | 新视图条目 | 两个地址与视图类名一致 |
| `Tables/Data/dialogue/21000.json`～`21020.json` | 已导入正式稿 | 本轮消费21000～21011；其余仍为后续文本数据 |
| `Tables/Data/dialogue/21100.json`、`21101.json` | 教学／挑战桥接、五店角色文本 | 复用Dialogue，五店台词来自填写稿 |
| `Tables/Data/narrative/spotlight_current.json`、`spotlight_mentor_duel.json`、`shop_boss_battle.json` | 等待点／出口／胜利标记 | 两处允许局部遭遇，真实胜利才推进主线 |
| `Tables/Data/narrative_encounters.json`、`Tables/Data/quest/900.json` | 资格／主线任务 | 按阶段限定遭遇、按对白与真实成果推进任务 |
| `Data/Config/dialogue_tbdialogue.bytes`、`narrative_tbnarrativestory.bytes`、`narrative_tbnarrativeencounter.bytes`、`quest_tbquest.bytes` | 表生成物 | 已通过正式生成脚本更新；生成C#无内容差异，仅工作区换行状态 |

## 验证与文档

| 文件路径 | 位置 | 本轮改动 |
| --- | --- | --- |
| `Scripts/Tests/EditMode/Tavern/TavernFingerGuessTests.cs`、`TavernOpponentLearningTests.cs` | 判定／学习测试 | 组合穷举、拒绝重复、适应习惯变化、强度步长、深拷贝与切槽 |
| `Scripts/Tests/EditMode/Narrative/NarrativeCatalogTests.cs`、`NarrativeServiceTests.cs`、`StoryGuideRulesTests.cs` | 真实表／恢复／提示 | 检查正式内容、失败保存重建重试、节点与区域闸门 |
| `Scripts/Tests/EditMode/Quest/QuestCatalogTests.cs`、`QuestServiceTests.cs` | 正式任务与示例夹具 | 新任务900覆盖；示例测试隔离营地任务 |
| `Scripts/Tests/EditMode/Forging/ForgingProgressTests.cs`、`ShopPlaytest/ShopPlaytestStateTests.cs` | v5迁移／角色文本 | 版本兼容断言及正式对白节点映射 |
| `Scripts/Tests/Showcase/ShopPlaytest/ShopPlaytestShowcase.cs` | 正式序章与行走助手 | 从标题进入、实际交互／切磋、正常跳过对白、近身停在碰撞体外 |
| `Scripts/Tests/Showcase/Narrative/NarrativeShowcase.cs`、`NarrativeShowcase.Spotlight.cs` | 连续回放／另一顺序 | 实际三店、划拳、BOSS、会面、继续与可见软引导 |
| `Scripts/Tests/Showcase/Tavern/TavernShowcase.cs`、`Clothing/ClothingShowcase.cs` | 划拳专项／正式文本断言 | 在途保存拒绝、消息／学习／战斗／离区；衣肆对白同步 |
| `ai-docs/docs/modules/{tavern,narrative,shopplaytest,session}/` | 各三件套 | 同步接口、扩展方式、生命周期及当前范围 |
| `ai-docs/docs/modules/identity/identity-module-guide.md` | 演示账簿数值 | 记录9的范围与两皮容量未实现 |
| `HANDOVER.md`、`docs/design/spotlight/writer-import.md`、`PRP/tavern-story-guidance/` | 当前完成／待办 | 更新正式接线和实测，保留后续阶段队列 |
| `ai-docs/docs/modules/battle/`、`turnbased/turnbased-module-guide.md`、`ai-docs/docs/stage-structure-spec.md` | 相关引用 | 修复健康扫描发现的失效源码行号，不改战斗规则 |

没有新模块目录，沿用四个既有模块注册，不重复增加catalog或modules.json条目。原计划的独立SpotlightStoryFlowTests复用已有Narrative测试与真实回放，Title及Session教程测试无需修改，其入口行为由正常标题连续回放覆盖。

## 埋点门

实际扫描Tavern、Narrative、ShopPlaytest、Session。扫描器仍有候选，逐类结论如下：

- Tavern新增规则参数、学习快照和曲线非法值属于参数／配置守卫；由外层异常及已有core.log捕获，不在纯计算中重复埋。TryResolve的消费与拒绝由Controller统一记录，UI缺引用由guess_failed记录；原醉意校验为历史点。
- Tavern的guess_started、guess_resolved、guess_rejected、difficulty_updated记录真实开始、结算、拒绝及学习变化；ShopPlaytest以guess_open量UI耗时、guess_failed记异常。
- Narrative新增ResultIntent只创建身份快照，消费与拒绝已在SubmitAsync；Loaded缺引用为场景配置错误，Unity异常通道已有。章节交互、启动和HUD异常均有TrackError，剧情迁移由原stage_entered记录。
- ShopLine缺表由ConverseAsync的conversation_failed捕获；GreetingKey／CommandsFor为只读映射，不是状态迁移。Confirm由TavernController处理意图；门槛和距离拒绝沿已有command_rejected／drink_target_rejected。其它候选为未改历史代码。
- Session本轮只接入口和只读保存门闸，转场、保存拒绝、加载失败与UI开关均复用已有事件；扫描余项为历史点，不扩成全模块埋点整治。
- 教程Tick、距离和每帧显隐不记日志。

## 沉淀门与边界

已把会改变接手决策的结论写入模块文档和交接：正式主线必须用局部遭遇；失败存读档后的重入身份必须持久；学习控制策略能力而非指定胜率；编辑器DSP停走时先核验设备时钟，不能放宽音游判定护栏。本轮未新增定时器或自动化。

工作区原有角色包、音效、字体、环境、战斗道具、其他流程文档与飞书监控改动均不属于本轮提交清单；未回退、未整仓暂存。Boot／SampleScene／UI地址等共享文件以后提交时还需按内容拆分。没有Windows包构建和真机性能结论。

## 本轮提交信息

划拳、剧情和引导共用五店接线及保存边界，按用户本轮授权整合为一个功能提交。Boot既有测试路线、庭院额外显隐覆盖和文档原有道具编号修订保留在原工作态。提交信息：

```text
feat(narrative): 接入划拳动态难度、正式剧情与软引导

- 三名对手先定拳后揭晓，输赢决定实际饮酒与醉后消息
- 最近24局历史学习、表现曲线与强度步长随槽保存
- 正式新局经教学切磋、三店成果和酒肆胜利推进到会面结束
- 五店角色对白入表，局部战斗续回父帧，任务与节点绑定真实结果
- 节点与区域提示跟随身份和输入绑定，补两种顺序与继续回放
- 同步模块文档、交接和验证记录
- 留：两皮回收、后续调查关卡及最新独立包验收
```

## 2026-10-18 历史：双场景人间连续叙事与入镜修正（已被下节替代）

本节替代本文件上方涉及人间跑点、强制教学切磋的旧入口说明。用户授权本轮实施与测试，没有授权新的提交；本轮未切分支、拉取、提交或推送，也未清理其它会话改动。

正常入口为标题新局→SpotlightHuman连续对白与小过渡→戏班右侧平台按E→真实卸载人间并加载SampleScene妖界→原五店暗杀取皮、换皮及后续玩法。两界共用CourtyardLatest地图和布局，不使用HumanJingyang／YaoFangshi灰盒。人间只有李诏阳与入镜两处交互；旧24血教学配置保留供追溯，正常入口不触发它。

### 逐文件改动

以下路径除表源、脚本、文档及Addressables外，以 `Assets/_Project/` 为前缀。SampleScene是本轮开始前已有的共享未跟踪文件，不能把相对HEAD的整份场景当成本轮独占改动。

| 文件 | 本轮改动 |
| --- | --- |
| `Scripts/Runtime/Narrative/SpotlightStoryFlow.cs` | 自动衔接四个已改为叙事边界的旧ID；教学短淡出；镜前E捕获原意图并真实换场，成功后才提交；失败回镜前，继续读档取消旧请求，保存锁随会话释放；人间穿越提示 |
| `Scripts/Runtime/Narrative/SpotlightSceneBindings.cs` | 人间标记、两点显隐；自动衔接节点不显示成现场目标 |
| `Scripts/Runtime/Narrative/StoryGuideRules.cs` | 明确四个自动叙事节点，入镜和玩法节点仍需实际完成 |
| `Scripts/Runtime/Monster/MonsterEncounterState.cs` | 选择两界地址，按SceneKey恢复；只读实际场景与高度；人间不接处决；恢复玩家表现与相机，揭幕即在保存位置 |
| `Scripts/Runtime/Monster/EncounterSaveData.cs` | v3可空场景高度及非有限值校验；缺字段旧档不填造高度 |
| `Scripts/Runtime/Monster/EncounterSceneView.cs` | 人间主怪停用时隐藏遭遇战斗／驯服调试块 |
| `Scripts/Runtime/Session/SessionStateAdapter.cs` | 正式新局选人间；捕获实际绑定场景与表现高度；不用待加载地址污染存档 |
| `Scripts/Runtime/Quest/QuestSaveData.cs` | v2只迁移任务900：旧人间七项归零，妖界序号减7、保留成果与计数，其它任务不变 |
| `Scripts/Runtime/Quest/QuestService.cs` | 增加暂时关闭任务表现的公开开关，不清除任务数据 |
| `Scripts/Runtime/Quest/QuestHudPresenter.cs` | 人间隐藏任务HUD、目标标记与导航 |
| `Scripts/Runtime/Quest/QuestPanelController.cs` | 人间拒绝打开任务面板 |
| `Scripts/Runtime/Quest/QuestNotificationPresenter.cs` | 人间不弹任务通知 |
| `Tables/Data/quest/900.json` | 移除前七项人间对白目标，从妖界三店成果开始，共五项目标 |
| `scripts/import-writer-story.py` | 仅明确登记的14处边界产生地点等待，不再由地点文字变化推导玩家任务；新增负对照自检 |
| `Data/Config/quest_tbquest.bytes` | 由正式Luban生成脚本更新任务数据 |
| `Data/Narrative/SpotlightStoryConfig.asset` | 镜前“入镜·妖界”与路线软提示；旧叙事节点改为前情说明 |
| `Scenes/SpotlightHuman.unity`、`.meta` | Unity保存同地图副本：保留原出生／教学位置，移除五店、旧示例和战斗入口；两处交互，镜点(29.4,3.235,-16.5)；主怪按原移动契约停用且被动 |
| `Scenes/SampleScene.unity` | 仅本轮追加MirrorApproach／MirrorStairs／MirrorPlatform三块不可见Ground碰撞，与人间同坐标；原五店、位置和草坪修复引用保留 |
| `Assets/AddressableAssetsData/AssetGroups/Scenes.asset` | 新增SpotlightHuman场景地址 |
| `Scripts/Tests/EditMode/Quest/QuestSaveDataTests.cs` | 旧序号0、6、7、8、11迁移与其它任务不受影响 |
| `Scripts/Tests/EditMode/Quest/QuestCatalogTests.cs` | 真表900从三店成果开始、目标数5 |
| `Scripts/Tests/EditMode/Narrative/StoryGuideRulesTests.cs` | 自动叙事边界与必须手动完成的镜面／五店负对照 |
| `Scripts/Tests/EditMode/Monster/EncounterSaveDataSceneKeyTests.cs` | v3缺高度兼容、NaN／无穷高度拒绝 |
| `Scripts/Tests/Showcase/Framework/ShowcaseScenario.BootFlow.cs` | 允许用例显式等待人间场景，其它调用默认值保持原样 |
| `Scripts/Tests/Showcase/ShopPlaytest/ShopPlaytestShowcase.cs` | 公共前奏走真实人间对白与入镜；经虚拟摇杆沿街面／短连接到镜点，不瞬移、不打教学战斗 |
| `Scripts/Tests/Showcase/Narrative/NarrativeShowcase.Spotlight.cs` | 人间无任务、两界继续、真实暗杀换皮；注入一次目标接线失败验证恢复；穿越途中继续读档取消、再E成功；镜前高度与揭幕时角色在画内、软教程恢复 |
| `ai-docs/docs/modules/narrative/narrative-module-guide.md` | 当前入口、自动衔接与真实换场约定 |
| `ai-docs/docs/modules/narrative/narrative-extension-guide.md` | 禁止把旧对白边界重建为人间跑点／强制战斗 |
| `ai-docs/docs/modules/monster/monster-module-guide.md` | 两界承载、被动主怪、v3高度与恢复相机构图 |
| `ai-docs/docs/modules/monster/monster-external-api.md` | 两界选择及实际场景捕获接口 |
| `ai-docs/docs/modules/session/session-module-guide.md` | 新局／继续两界路由、镜前高度与旧档兼容 |
| `ai-docs/docs/modules/quest/quest-module-guide.md` | 人间无任务表现、900迁移与新目标范围 |
| `ai-docs/docs/modules/quest/quest-external-api.md` | PresentationEnabled公开约定 |
| `ai-docs/docs/modules/shopplaytest/shopplaytest-module-guide.md` | 五店只在妖界接入，更新正常入口 |
| `ai-docs/docs/modules/interaction/interaction-external-api.md` | 移除因Monster源码变化失效的易漂移行号，保留方法引用 |
| `ai-docs/docs/modules/stealth/stealth-module-guide.md` | 同上，仅修引用，不改潜行规则 |
| `docs/design/spotlight/writer-import.md` | 等待边界与玩家任务的区别、人间叙事、真入镜和900迁移 |
| `HANDOVER.md` | 当前入口、地图、任务、战斗和历史验证口径同步 |
| `PRP/tavern-story-guidance/change-review.md` | 本节实施、逐文件范围、证据与未验边界 |

### 实测与证据

- EditMode：最终job `ad20dd516a0144119ba4346cff70d597`，Monster／Quest／Narrative／Session共586/586通过，0失败、0跳过。
- PlayMode：job `cc29a8e671ad4202a86a88e2cf7e718e`，两条2/2通过，0失败、0跳过；[报告20261018-121844](../../Logs/verify/narrative/20261018-121844/report.md)检查点失败0、异常0。正常标题新局、无强制战斗、真实穿越卸载、实际潜行取乙皮／换皮后继续、人间镜前高度及相机、失败回镜前、穿越途中读档取消后再次成功均已覆盖。
- 故障用例仅精确声明 `spotlight/mirror_failed` 且含“入镜目标未接入妖界五店”的预期错误，复用框架 `ExpectErrorLogs`；不把其它错误静默忽略。破坏只作用于本次加载的运行时ShopPlaytestWorld组件，不写场景资产，随后自动退订。
- 原工作簿重编译检查：478行、355条显示文本，全部阶段稳定ID与当前入表数据一致；转换器自检通过。正式表生成只产生任务二进制内容变化。
- C#手动lint零违规，本轮指定文件diff空白检查通过。两界及Boot现场缺失脚本检查均0，新场景.meta齐全。
- 两界地图现场逐节点比对：各680个环境Transform，位置／旋转／缩放／Mesh引用差异0；两边 `立方体.007` 都引用 `CourtyardGround_Outward.asset`。没有改变既定左下出生与李诏阳教学位置。
- 健康扫描尚未全绿：只剩两份已有共享Dynamic字体缓存过大（约37／17MB）；未清理或回退这些共享资产。测试运行时的一次扫描曾看到UTF临时场景，测试收尾后的复扫已不再报告它。

补充回归job `e63880d8e6e04ec2997e713b8a5755da`：镜前软教程、失败恢复和途中继续取消用例通过；完整三店／酒肆／会面首次未通过，[报告20261018-122229](../../Logs/verify/narrative/20261018-122229/report.md)记录5个失败检查、0异常。首个失败是戏班三次演奏未达标，衣肆和打造已成功，缺行头使后续酒肆取皮／换皮被正常门槛拒绝。

随后单独复跑完整流程，音游期间不查询编辑器，job `bf583c768dee4070926cad596bbdc1d4` 为1/1通过，0失败、0跳过；[报告20261018-122717](../../Logs/verify/narrative/20261018-122717/report.md)零失败检查、零异常，覆盖正常人间入口、三店真实成果、划拳、BOSS真实胜利、会面与会面后继续。未修改音游代码／判定、发奖励或跳过玩法等待。这个结果支持首轮可能存在回放时序干扰，但没有足够证据把原因定为MCP查询；首轮失败记录保留。共三条不同用户行为最终均有通过证据，不把复跑次数相加成测试覆盖数。

人工视觉／操作手感确认、独立包构建和移动端性能不在本轮已验证结论内。镜面当前用带“入镜·妖界”标记的现成角色作为交互占位，尚无正式镜面模型或穿镜演出；草坪朝外修复由另一会话提供，本轮只保留引用。Boot已有旧环境停用对象的保存差异属于共享前态，不由本轮回退或重新保存。

## 2026-10-18 最新：SampleScene内Z+300两界（实施中）

用户明确要求同一场景、Z方向相隔几百米。本节替代上方双场景、自动守卫／坊市对白、人间全隐藏任务及永久阶段二占位的结论。未提交、未切分支、未清理共享改动；旧SpotlightHuman资产／地址保留，本轮不再改它。

### 本轮文件范围

| 文件（源码以Assets/_Project为前缀） | 当前改动 |
| --- | --- |
| `Scripts/Runtime/Monster/EncounterSceneView.cs` | 显式人界根、妖界根组、出生与镜后落点，选择当前分组，保持单玩家移动驱动 |
| `Scripts/Runtime/Monster/MonsterEncounterState.cs` | 固定加载IsometricEncounter；同场景换界、互斥绑定／卸载、失败快照回滚、即时位置／高度／相机、停用演员不可推进、锚点事件重建地图 |
| `Scripts/Runtime/Monster/EncounterSaveData.cs` | v4世界标识及XYZ；旧SpotlightHuman玩家Z+300；旧版本来源标记当前有候选克隆丢失缺口，未完成 |
| `Scripts/Runtime/Session/SessionStateAdapter.cs` | 新局人界、捕获StoryWorld；旧v1/2人界节点迁移逻辑已写但真实候选链仍待修 |
| `Scripts/Runtime/Narrative/{SpotlightStoryFlow,SpotlightSceneBindings,StoryGuideRules}.cs` | 缓存同场景两组绑定；入镜不提交171；守卫／坊市必须实际E；171提示分世界；只70／83／129人界自动衔接；当前目标覆盖原任务HUD；293不做永久完成提示 |
| `Scripts/Runtime/Quest/{QuestConfig,QuestInstaller,QuestRules,QuestService,QuestHudPresenter,QuestPanelController,QuestPanelView}.cs` | 复用原HUD／列表；指定任务当前文本覆盖；正式只启用900，保留隐藏旧进度，完成后不接旧示例链 |
| `Scripts/Runtime/Quest/QuestSaveData.cs`、`Tables/Data/quest/900.json`、`Data/Config/quest_tbquest.bytes` | Quest v3兼容旧下标；移除永久占位，共4项，21011后完成；二进制由正式生成脚本更新 |
| `Scripts/Runtime/World/SceneMapView.cs` | 明确排除activeInHierarchy为false的几何，防停用的整张地图仍扩大小地图范围 |
| `Data/Quest/QuestConfig.asset` | AvailableQuestIds=[900]，无当前目标文字、当前段完成／后续未开放通知 |
| `Scenes/SampleScene.unity` | HumanWorld_Z300与DemonWorld分组，复制网格引用不复制大素材；共用剧情稳定目标，删除人界重复QuestLocation；妖界镜台到达点；真实左上轮回堂点(-24.5,6.95,17.7)，桥／岛原网格碰撞和西接缝坡面 |
| `Scripts/Tests/EditMode/{Monster,Quest,Session,World}/` | 世界往返／旧迁移、900终点迁移、内容作用域及旧进度保留、小地图停用根负对照；须补真实候选链测试 |
| `Scripts/Tests/Showcase/{ShopPlaytest/ShopPlaytestShowcase,Narrative/NarrativeShowcase.Spotlight}.cs` | 真实Z+300人界路线、同场景实例与落点、地图范围、镜后远E／错误对象／Continue负对照、真实守卫／坊市路线；失败注入不再等sceneLoaded；取消需补真正输入关闭后Continue及恢复走动验证 |
| 本节、tasks、HANDOVER、writer-import及Monster／Narrative／Quest／Session／ShopPlaytest模块文档 | 当前入口与历史证据分开，保留未完成项，删除已移除PresentationEnabled接口说明 |

### 已测与未测

1. 编译：安全检查点非Play、非编译，控制台错误0，Boot／SampleScene均无脏改，活动Boot；回放倍率恢复1。编辑器锁已释放给地图巡检，此后暂停Unity及Assets写入。
2. EditMode job81dd65f1f997408dab016279efdef780：Monster／Narrative／Quest／Session **604/604通过**，0失败、0跳过。随后新增的小地图场景负对照未执行，不能包含在604里。
3. 定向PlayMode job15488aaef816466caad64f046214eac4：[报告20261018-132406](../../Logs/verify/narrative/20261018-132406/report.md) **FAIL**，3个检查失败、1条BattleScenePresenter销毁UI收尾异常。已通过原HUD交谈目标、人界无妖界可用演员、同场景／玩家实例保持、对应平台落点与PreviousPosition、无自动守卫对白、远处E／错误对象拒绝、妖界171保存继续保持等待。
4. 小地图仍有两界轮廓，已定位并加activeInHierarchy过滤；新负对照未跑。上层路线在(-8.96,14.74)被判移动失败，截图明确显示进入市令战斗，**没有证据把该次失败归因于桥面碰撞**。回放改走北缘与桥北侧，西接缝同步加宽，尚未复跑守卫及坊市实际交互。
5. 只读复核确认SaveSnapshot构造、Require、CopyPartitions均JSON克隆，故EncounterSaveData的[JsonIgnore] LegacySceneRoute在ReadCandidate／Commit链中丢失。先前仅静态Migrate＋路由测试不足；必须修复后经过真实候选提交和PrepareRestore验证旧v1/2人界档。直接LoadAsync保留对象不代表Continue链也保留。
6. CrossMirrorAsync只在会话代数不变时恢复Gameplay；Continue发布SessionStarted会换代取消，可能跳过恢复。需等真正crossing且Gameplay关闭后触发Continue，再实际移动和E验证，不能只看CanSave或世界标识。
7. 当前还缺：上述两项根因修复；小地图／桥面／守卫／坊市定向复跑；失败／取消与两界Continue；完整五店到21011及900完成；最终lint、diff、meta、健康扫描；人工视觉确认。旧双场景三条PASS不抵扣这些项目。

镜面仍为角色占位，未实现正式墨晕／墨流转场；新授权的独立水墨预览由其它窗口另场景制作，禁止混接正常流程。无独立包、移动端或内存性能结论。

### 交锁后离线核对与最小修法（尚未写Assets）

- 迁移：只改Encounter DTO和Session适配，不改Core通用克隆。旧版本来源标记必须可被JSON往返克隆；PrepareRestore完成世界判定后消费／清除，防新档重复偏移。补真正版本信封文件→ReadCandidateAsync→Require→Commit→PrepareRestore，覆盖v1/2人界四节点、妖界171／188／197、v3人界镜前及新v4两界，保留XYZ、物品与五店成果。
- 输入：只在章节适配器记录本次穿镜是否拥有Gameplay关闭权。SessionStarted先换代并同步交还这份输入所有权，再取消旧请求；旧finally不得再改新会话输入，正常失败／成功仍成对恢复，Reveal之后再检查代数。测试先等待真实crossing（CanSave=false且Gameplay.disabled），再Continue，最后实际走一步及E，而非只断言可保存。
- 另发现QuestSceneBinder.TryGetLocation未过滤停用地点，TryResolveTarget也未过滤停用NPC；人界副本的重复地点已删，但同场景妖界旧地点仍可被查到。后续加活跃性守卫并做负对照，不新增世界引擎。
- 当前24个本批C#手动lint已执行零违规；这不替代未完成的编译后行为回归与最终检查。

### 用户授权阶段性提交（2026-10-18）

用户明确要求先暂存并commit当前进度，本次作为未完成的检查点，不代表行为验收通过。只提交本批源码、任务配置、转换器、测试、文档和交锁前Unity保存的场景快照；其它窗口已暂存的素材、后续地形修补及独立水墨预览保留原工作态。仓库仍跟踪旧SampleScene路径，检查点把Unity快照原样记录到该已跟踪路径；工作区进行中的场景目录迁移留给其原批次，不手改场景YAML或覆盖当前场景。引用到的原有朝外地面网格及庭院布料材质作为必需依赖一并保留。

埋点门已实际扫描Monster／Narrative／Quest／Session／World：换界成功与失败分别由mirror_crossed／mirror_failed记录；绑定异常已有core.log和外层失败通道，DTO／配置守卫不重复埋；任务激活／完成／追踪仍走既有事件。逐帧地图筛选、目标文字和只读查询不埋点，余项是未改历史候选，本次无新增应埋未埋的点。沉淀门产出为上方候选克隆丢标志、代数切换的输入所有权及回放失败证据，未另建通用框架。

本次沿用已取得的604项规则通过及首轮定向失败记录，未重跑Unity，不占用已交出的编辑器锁。健康扫描修复四处因本批行号漂移的文档引用后，仅剩两份共享动态字体缓存过大；不提交这些缓存。
