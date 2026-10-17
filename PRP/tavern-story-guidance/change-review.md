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
