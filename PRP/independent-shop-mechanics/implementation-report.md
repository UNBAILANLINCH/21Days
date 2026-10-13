# 四店独立玩法实施记录

> 2026-10-13更正：用户否定了本批的独立场景/房间接法。下述测试通过只证明规则和旧适配器，不代表主场景需求已满足。现阶段按用户要求先整理[主场景执行文档](main-scene-execution-plan.md)，尚未迁接或删除旧资产。

范围：用户于2026-10-12确认的戏班、衣肆、打造、酒肆独立试玩；new-feature设计门已通过。

当前证据：Unity编译控制台无错误；EditMode回归402/402通过（job 7bbc6b3da115423cbde6f8700270c555）。
包含本批四店规则及Battle、Monster、Rhythm回归；不是整个项目全量测试。
首轮真实界面Showcase衣肆、打造、酒肆PASS；戏班FAIL（准备条件过早、事件未接齐便点击开始），无运行时异常。
报告批次20261012-225757。已修回放就绪判据、开始确认，等待第二轮。历史失败保留，不用后续通过覆盖描述。

入口：Assets/_Project/Scenes/ShopPlaytest.unity。运行时加载ShopPlaytestWorld与独立Cube战斗舞台；所有占位房间、角色、物件均经Unity编辑器制作预制体并预摆。

## 本次实现与范围调整

- 四店独立状态，不建十二阶段或五店总阶段机；客栈沿用原对白，无专属新增系统。
- 配置集中在Data/ShopPlaytest，明确为隔离fixture；不在每个正式模块目录额外创建重复配置。
- 试玩及回放使用已批准独立入口；未在正式SampleScene预摆示例，以免改正常新游戏表现。模块预制体本身是可复用接线样例。
- Tavern规则测试实际文件名为TavernRulesTests.cs；其它PRP预计核心文件均已落地。
- 本地衣物/钥匙为成果标记，尚不写正式背包、主线事实或存档；剧情、正式音效与美术接入后置。

## 审查闭环

第一轮纯规则审查发现打造最大额度可能不足完成，已用long最低总成本校验并补负对照。
第二轮完整只读审查发现酒肆缺叙事目标与End阶段Current清空导致漏结果；已补预制体NarrativeTrigger，并按本次Generation消费结束Outcome。
身份时限/冷却已接固定tick推进，不会因为配置非零而永久卡冷却。
状态显示已改用角色名字及中文完成/潜行状态；TMP名字尺寸缩小，酒肆完成标记更名；收掉独立入口中的原主线HUD。
第二轮四模块真实Showcase最终4/4通过、失败/跳过0（job 66820ce60fd1413099cc80a0c3d95a90；批次20261012-232418）。
完整58枚现有四轨谱面完成回店、四店重试与角色数量不增长均通过；关键1920×1080实际截图已查看。
只读复审PASS，无剩余BLOCK。定向补验2/2通过（job 63a0e525c55c42c09adc5ffe4401e890；批次20261012-233718）：戏班低分/暂停退出均回店且可重玩，酒肆胜负结果消费、重试恢复BOSS均通过，无运行时异常。
末次仅统一准备页与暂停页退出文案，已编译检查；没有再次重跑长谱，回店逻辑未改。未提交，开发者视觉与手感确认仍待。

## 回放证据与验收状态

| 模块/范围 | 检查结果 | 报告 |
| --- | --- | --- |
| 衣肆唯一领衣与重试 | PASS，角色数量不增加 | Logs/verify/clothing/20261012-232418/report.md |
| 额度、取消、打造、交付、重试 | PASS，角色数量不增加 | Logs/verify/forging/20261012-232418/report.md |
| 戏班完整现有谱面、结果回店 | PASS | Logs/verify/theater/20261012-232418/report.md |
| 戏班低分和暂停主动退出 | PASS | Logs/verify/theater/20261012-233718/report.md |
| 酒肆饮酒、实际减速、战斗醉酒、结案、离区、重试 | PASS | Logs/verify/tavern/20261012-233718/report.md |

实际1920×1080截图已查看；正式视觉确认仍由开发者决定，不把自动输入当真人手感。
没有打包或真机性能测量；技术中断延用Rhythm已测契约，本批没有另外模拟音频设备断开。

## 埋点门结论

已按四类尺子扫描Clothing、Forging、Tavern、Theater、ShopPlaytest，以及改到的Rhythm、Monster、Battle、Session。
- 衣肆/打造/酒肆的规则意图与拒绝由Controller的intent_resolved覆盖；不在纯规则重复记录。
- 构造、配置和安装参数守卫由启动错误/接线错误覆盖，不每个参数再写一条业务事件。
- 身份借入/退出、控制切换由Identity/Taming已有事件覆盖；距离与不可用拒绝补borrow_rejected。
- 戏班前置和结果由TheaterController覆盖，根状态不重复埋；加载面板、打开演奏、开战入口、离场用span。
- 每tick Player/Monster意图、Step与线框表现不埋；Rhythm/Monster/Battle/Session历史候选点不在本批补齐。

沉淀：已在模块指南记录“叙事End清空Current”和“UI实例出现不等于事件已就绪”，不另重复灌入pitfalls。

## 本批待审清单（按内容归属；未提交）

下表路径均相对项目根；所有新Unity资产和C#的.meta一并归入对应条目。

| 文件路径 | 位置 | 改了什么 |
| --- | --- | --- |
| Assets/_Project/Scripts/Runtime/Clothing/、Forging/、Tavern/、Theater/ | 规则、只读意图、配置与Controller | 四项独立玩法及一次发放、额度、醉酒、资格边界 |
| Assets/_Project/Scripts/Runtime/ShopPlaytest/ | Installer、State、World、Actor、View、Entry | 独立入口、实际借控、固定tick、重试与既有系统接线 |
| Assets/_Project/Scripts/Editor/ShopPlaytest/ | Builder | 编辑器预制并预摆Cube＋TMP，不在游玩时生成房间/角色/道具 |
| Assets/_Project/Scripts/Runtime/Rhythm/RhythmState.cs、RhythmInstaller.cs、RhythmView.cs | 活动生命周期、路由与退出文案 | 保留世界，结果后关闭，隔离Profile，禁止独立入口争用 |
| Assets/_Project/Scripts/Runtime/Battle/BattleSetup.cs、BattleArena.cs、BattleInstaller.cs | 开战快照与场景地址 | 真实场外醉酒来源和独立预摆Cube舞台 |
| Assets/_Project/Scripts/Runtime/Monster/MonsterIntent.cs、MonsterRules.cs | 可选感知/速度倍率 | 默认1保留旧行为，醉酒效果进入真实判定 |
| Assets/_Project/Scripts/Runtime/Session/SessionInstaller.cs | 标题注册开关 | 独立启动关闭正式存档标题路由，默认保持开启 |
| Assets/_Project/Scripts/Tests/EditMode/Clothing/、Forging/、Tavern/、Theater/，Battle/BattleSetupTests.cs，Monster/MonsterRulesTests.cs | 正负对照 | 一次领奖、额度退款/溢出、醉酒、门槛和接口回归 |
| Assets/_Project/Scripts/Tests/Showcase/Clothing/、Forging/、Tavern/、Theater/、ShopPlaytest/ | 真实UI和输入 | 完成/低分/退出/重试/结案及角色数量不增长 |
| Assets/_Project/Scenes/ShopPlaytest.unity、ShopPlaytestWorld.unity、ShopPlaytestBattle.unity | 三个独立场景 | 启动、四个房间、独立战斗舞台 |
| Assets/_Project/Data/ShopPlaytest/、Prefabs/ShopPlaytest/、Prefabs/UI/ShopPlaytestView.prefab | 配置、角色、柜台、成果、界面 | 可改的试玩默认值与显式引用 |
| Assets/AddressableAssetsData/AddressableAssetSettings.asset、AssetGroups/Scenes.asset、AssetGroups/UI.asset | 本批新增条目 | ShopPlaytestBoot、ShopPlaytestWorldScene、ShopPlaytestBattleScene、ShopPlaytestView四地址；混合改动按条目拆分 |
| ai-docs/docs/modules/clothing/、forging/、tavern/、theater/、shopplaytest/ | 三件套 | 职责、调用前置、配置、接线与扩展限制 |
| ai-docs/docs/modules/battle/、monster/、rhythm/、session/session-module-guide.md、turnbased/turnbased-module-guide.md | 本批增量段落 | 同步新增接口、隔离边界与被移动源码的失效行号 |
| .claude/skills/generate-doc/modules.json、ai-docs/docs/catalog.md、PRP/independent-shop-mechanics/ | 登记和实施记录 | 新模块入口、设计确认、验证、待审清单 |

范围外：标题阳/阴、美术、原有字体、HANDOVER、其它PRP/工具/规划文档属于其它工作，不归本批。
共享catalog、Session guide与Addressables文件按本批内容审查，不能整份暂存混入其它会话的改动。

拟提交信息（仅提案）：

```text
feat(shopplaytest): 四店独立玩法与预摆占位试玩入口

- 戏班、衣肆、打造和酒肆分别提供操作、结果与重试
- Cube与TMP角色、房间、道具通过编辑器制作预制体并预摆
- 戏班接现有下落音游，酒肆接实际醉酒效果与既有战斗
- 补齐规则回归、真实回放、资产检查与模块文档
- 留：主线接入、正式美术音乐与真人手感验收
```

## 静态检查

本批新运行时和编辑器脚本手动project-lint通过。
资产体检四项通过；本批新文件meta缺失0，范围内diff空白检查通过。
gc最终扫描只剩动态字体：开工前已脏约8366KB，工作区最后扫描约18611KB；没有清除其它会话未提交的字体数据，不纳入本批提交清单。提交前仍须处理这份动态缓存，不能称全局gc全过。
本批导致的旧Battle源码行号失效已修正，其余引用检查通过。全局diff空白检查只报字体原生序列化尾空白。
不把其它会话原有字体、文档或工具改动归入本批。
