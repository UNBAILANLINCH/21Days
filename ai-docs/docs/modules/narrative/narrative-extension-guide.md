---
type: extension-guide
module: narrative
layer: runtime
maturity: seed
---

# Narrative 扩展指南

## 剧情环境首次接线与补位

1. 等 Unity 编译完成，退出 Play，打开并激活 `Assets/_Project/Scenes/SampleScene.unity`。场景需已有 `SpotlightSceneBindings`，且包括禁用对象在内恰好一个方向光。
2. 执行 `21Days/剧情/接入剧情光照与局部天气`。菜单在稳定场景根创建一个 `SpotlightEnvironmentView`，接入现有方向光并创建或复用 `Assets/_Project/Data/Narrative/SpotlightEnvironmentConfig.asset`。已有视图时选中并拒绝覆盖；不重复运行故事生成器。
3. 根据现有 Points 的界别和稳定键取真实位置：人界 `enter_r0026` 为雪/坛火预览，人界 `enter_r0129` 为烛光预览，妖界 `enter_r0384` 为薄雾预览。位置有歧义则停止；缺失时只建立禁用空标记并警告，效果引用留空，不借用另一界同名点、不在原点造特效。
4. 缺水雾位置时，作者必须先选择或摆好 SampleScene 内的真实位置锚点，再执行 `21Days/剧情/局部环境/在所选位置添加水雾`。菜单复制所选世界坐标、生成粒子并赋 `waterMist`；已有水雾引用则选中并拒绝覆盖。不要把未摆位的原点空标记当成真实水边。
5. `21Days/剧情/局部环境/在所选位置添加烛光` 在选择位置增加点光并追加 `candles` 数组，不覆盖现有灯。`21Days/剧情/局部环境/在所选位置添加静态积水` 生成无碰撞、灰蓝低光泽的 3×2 米 Plane 美术预览；已有同名预览则选中。积水是静态几何，不是实时反射水体，不修改 Terrain，也不受水雾开关控制。
6. 所选位置菜单要求场景内有效选择、非 Play、活动 SampleScene 和唯一根级环境视图。效果放在稳定环境根下，只复制位置，不挂在 `Display` 会关闭的剧情演员/交互点下面。作者再调整位置、尺寸、颜色和粒子参数，检查后手动保存场景。

菜单支持场景 Undo 与异常回滚，不自动保存场景、不调用全局 `SaveAssets`，也不改包或渲染设置。首次运行按需创建 `Assets/_Project/Art/Materials/Narrative/SpotlightSoftParticle.mat`、同目录 `SpotlightSoftParticle.asset`（程序生成柔边纹理）；静态积水菜单按需创建 `SpotlightStaticWaterPreview.mat`。已有资产复用、不覆盖；场景 Undo 不删除新资产。缺可用 URP Shader 时停止，不私自切换渲染管线。

增加表现映射应扩展 `SpotlightEnvironmentRules.Resolve` 的明确阶段键与对应纯规则测试，不按节点数字范围推断，也不增加连续时间、随机天气或独立天气存档。调整观感通过 `SpotlightEnvironmentConfig` 的 `Dawn/Day/MoonNight` 与局部参数；环境只读剧情，不替代 Narrative 的推进入口。正式流程目前停在 `enter_r0293`，后续月夜/水雾键存在不代表后续剧情或水边地图已经实现。

已在 Unity 编译、执行首次菜单并保存场景；新局、夜档继续、日转夜中返回标题再继续另一槽，以及粒子在对白时停中推进的验证范围见 [本地验证](narrative-module-guide.md#本地验证2026-10-20)。重复菜单拒绝覆盖、Undo、所选位置补位菜单尚未实际验收；书库烛光和水口薄雾因缺锚点仍未接线。还须人工检查真实镜头下的摆位、亮度、粒子材质和静态积水观感，现有测试不代表视觉验收通过。

人间边界以最新用户决定为准：地点变化不等于玩家任务。导入器只保留登记的边界ID；83／129／70仅在人界自动续播，不恢复西城／书库跑点或24血战斗。正常只加载SampleScene，人界为同场景Z+300分组，妖界保留原坐标；同布局、修复网格、左下出生与李诏阳保留。171人界镜前E只传送不提交剧情，妖界须到轮回堂值守旁E；188坊市必须到场E，禁止自动衔接。旧SpotlightHuman资产只保留兼容。

正式章节新增节点时，同步源故事／对白、Quest目标及 `SpotlightStoryConfig` 节点。通过编辑器给 `SpotlightSceneBindings` 补实际锚点，保留稳定主目标；不要靠到过店铺或播过教程判玩法成功。菜单 `21Days/剧情/接入序章至酒肆会面` 仅首次接线，已有 SpotlightStory 时拒绝覆盖；后续直接编辑现有点位与配置。

软引导条件扩展在纯 `StoryGuideRules`，提示和范围在配置；节点变化才更新舞台，缺件文案按变化缓存，不能逐帧 Capture 或拼接字符串。完整回放入口 `NarrativeShowcase.Spotlight_ThreeShopsGuessBossAndReunion` 经过真实三店成果、划拳、BOSS与会面，不得用发道具／直接写任务进度替代。

> 要给剧情规则加条件类型、加阶段类型、加遭遇规则，或要把 Narrative 真正接进 Unity 时看这份。
> 架构见 [`narrative-module-guide.md`](narrative-module-guide.md)，对外签名见 [`narrative-external-api.md`](narrative-external-api.md)。
> 原七文件保持纯规则；运行入口 NarrativeService 与 NarrativeInstaller 已有源码，接线/实测状态见 tasks.md。


王录业是连续书库叙事的临时演员，不增加跑点；绑定脚本 `Tools/bind-wang-luye-dialogue.txt` 只追加人界现有组件的引用。对白选态只经ChibiPuppet门面，必须保持该实例Motion停用；新增或调整会话范围时同步SpotlightStoryFlow开始／结束监听与真实对白回放，不能只替换头像而漏掉人物关闭清理。

轮回堂值守则复用妖界原enter_r0171的Point与常驻规则，lunhuitangGuard仅引用其新视觉子物体，PresentLunhuitangGuard只选态。人界同ID为入镜点，不替换为值守；后续给值守加对白仍改源表与导入映射，不能借角色外观更换剧情出口。

## 扩展点一览

| 要加什么 | 扩展点 | 改代码吗 |
| --- | --- | --- |
| 新的条件事实类型 | `EncounterContext.Fact` 枚举 + `Read` 分支 | 是 |
| 新的阶段类型 | StageKind + 纯规则迁移 + NarrativeService 分派 + Catalog 能力检查 | 是；C5/G5 未定不可擅自实现 |
| 新的遭遇规则 | Tables/Data/narrative_encounters.json | 否，生成后跑 Catalog 校验 |
| 条件事实来源 | NarrativeConditionSource；DialogueInstaller 有注册则采用，否则保留占位 | 是 |
| NPC 入口 | NarrativeTrigger 稳定 ID/kind；运行时 Configure | 通常只需接线 |
| 任务完成标记 | Tables/Data/narrative_quest_flags.json | 否；任务 ID 必须存在 |

## 新增一种条件事实类型

1. `EncounterContext.cs`：在 `Fact` 枚举加一项；构造函数按需加对应的只读属性；`Read(Fact, string)` 的 `switch` 里加一个 `case`。
2. 若事实需要参数化（像 `StoryFlag` 用 `key`），在你的 `case` 里做和 `StoryFlag` 一样的空值校验（抛 `ArgumentException`），
   不要静默返回 `false`——内容错误要在求值时立刻暴露，不能被当成「条件不满足」吞掉。
3. Dialogue 侧如果要用这个新事实：`DialogueCatalog.cs:244` 附近的 `ConditionFact` 映射要**按名字**加同一项
   （见 `dialogue-module-guide.md`「内容表」一节），两边不同步会在翻译时抛异常或漏判。
4. 补 `NarrativeRulesTests.cs` 或新测试覆盖这个事实的求值分支；不要只加枚举不加断言。

**不要**为了「以后可能要参数化」而把 `Fact` 换成字符串或反射查找——PRP 明确禁止通用条件表达式语言，
未接入的事实类型必须在内容检查阶段报错，不能默认 `true`（见 `prp.md` 第 4.1 节）。

## 新增一种阶段类型（`StageKind`）

1. `NarrativeContent.cs`：在 `StageKind` 枚举加一项；如果这种阶段有自己的结构约束（像 `Condition` 必须有
   `True`/`False` 出口、`WaitAction` 多部分行为必须有 `Success` 出口），在构造函数的校验循环里补对应分支
   （`NarrativeContent.cs:49`–`52` 是现有两个例子：`Condition` 必须有 True / False 出口、`AllowEncounter` 只能在 `WaitAction`）。
2. `NarrativeRules.ResolveAutomatic`（`NarrativeRules.cs:69`）目前只自动处理 `Condition` 和 `End`；
   其余 `StageKind`（`Dialogue`、`WaitAction`、`Battle`，以及你新加的）都停在原地等待外部调用 `Apply` 推进——
   **不要把新阶段类型也塞进自动推进循环**，除非它确实是「无需等待任何外部输入」的纯判断阶段。
3. 阶段分派在 NarrativeService；新能力须先补 Catalog 校验、失败/取消与保存边界，再放开该类型。
   不要在 NarrativeRules 里塞玩法调用，它必须保持纯规则。
4. 补内容构造测试：非法结构应该在 `new NarrativeContent(...)` 时就抛异常，仿照 `NarrativeRulesTests.Condition_WhenAutomaticCycleExists_RejectsContent`。

## 新增一条遭遇规则

生产内容在 Tables/Data/narrative_encounters.json，结构见 Tables/Defines/narrative.xml。
先添加剧情 JSON，再添加遭遇行，运行 scripts/gen-tables.ps1 与定向 Catalog 测试。纯规则测试仍可构造：

```csharp
new EncounterRules.Rule
{
    Id = "唯一 ID", TriggerKind = "触发类型（如 seen）", TargetKind = "目标类型（空=不限）",
    Priority = 1, StoryId = "剧情 ID", EntryStageId = "入口阶段 ID",
    Repeat = EncounterRules.RepeatPolicy.Reenter,   // 或 Once / RisingCondition
    Conditions = new[] { new[] { new NarrativeCondition { Fact = ..., Expected = true } } },
};
```

- `RepeatPolicy` 三选一：`Once`（消费一次后永不再触发）、`Reenter`（每次新的 `TriggerId` + `EntryEpoch` 都可再触发）、
  `RisingCondition`（条件从「不满足」变为「满足」的那一刻才算一次，`ConsumeEdge` 会把边标记为已触发）。选错会导致重复弹窗或再也不触发。
- 同一批候选里，同优先级的多条匹配规则会被当成内容错误抛 `InvalidOperationException`——设计规则时用不同 `Priority` 显式排出优先级，不要依赖数组顺序。
- Catalog 拒绝同触发类型、目标范围重叠的同优先级规则，即使条件看似互斥也请显式区分 Priority。

## 接入条件源（替换 `DefaultDialogueConditionSource`）

1. 扩展 NarrativeConditionSource 的真实来源，读取须廉价、无副作用；每 0.25 秒及选项提交时会重新 Snapshot。
2. 目标按跨读档稳定 ID 注册；不要用 instance ID 或缓存候选中的过期事实。
3. 来源已接好并有测试后，才移除 Catalog 对相应事实的拒绝。未知来源不能默认 true。
4. 真实源不可依赖 NarrativeService，否则 Service→DialogueService→条件源→Service 会形成 DI 环。
   DefaultDialogueConditionSource 保留兼容无 NarrativeInstaller 的 Boot 与既有测试，不要求删除。

## 场景与恢复能力接线

Boot 的 GameBootstrap 挂 NarrativeInstaller；场景或运行时 NPC 使用 NarrativeTrigger。
样例用已有对白1002与任务1001/1002，验证对白9001展示条件选项；不新建章节、奖励或战斗规则。
主动交互重试只恢复同目标当前 Dialogue；不要删除 Once 消费记录来实现重试，否则已完成遭遇也会重新触发。
新增外部请求时，先固定阶段身份，再订阅，再发请求；await 后仍用之前的身份提交结果。
扩展对白恢复必须落完整 DialogueSaveData 并通过同一保存闸门，不能仅把阶段 ID 写盘后整段重播。
新状态在完整恢复得到验证前保持 CanSave=false；候选校验也须拒绝该状态。

## 不该从哪扩

- 不要把玩法判断（背包、任务、好感度……）塞进 `NarrativeRules`——它只管阶段迁移与结果校验，实际效果由所属玩法模块的公开入口应用。
- 不要新起第二套条件/规则解释框架；条件永远走 `NarrativeCondition.Matches`，遭遇仲裁永远走 `EncounterRules`。
- 不要在 `Game.Core` 里加剧情名词；`Game.Narrative` 已经是最合适的落点。
- 不要为了图省事让 `NarrativeContent` 支持运行时热改内容结构——它的校验设计前提是「构造即定型」，运行时改字段会绕过全部结构校验。

## 附录·战斗结果与外部请求的接线纪律（2026-10-07）

- **`Battle` 已经是可进表的阶段类型**：表里给它 `battleResults`（取值见 `BattleOutcome.cs`）。
  **不要**为新的战斗结果临时加字符串结果码——加一种结果＝在 `BattleOutcome` 加常量 + 在表里声明 + 补负对照测试。
- 换形态的写法是 `BossPhaseChanged:<形态>`，形态名由内容定，不参与枚举校验，但不能为空。
- **战斗阶段不能直接进另一个战斗阶段**（`NarrativeCatalog.ValidateBattleExits`）；要连续两场战斗，中间必须有非战斗阶段。
  换形态可以直接回到同一个战斗阶段（自环），那是「同一场战斗的下一形态」。
- 战斗侧拿到结果后走 `NarrativeService.CompleteBattleAsync`，不要自己拼 `NarrativeIntent` 绕过身份校验；
  身份三项从战斗开始时登记的值来，不要用回调回来时才读的当前阶段（那正是要被拒绝的旧回调）。
- **战斗阶段现在有条件地算可存档边界**：`IsStable` 对 Battle 阶段「没在打时稳定、在途时不稳定」（`NarrativeService.cs:347-349`）。
  战斗会话状态仍不落盘，所以**任何新的战斗侧实现都必须在开打前 `TryBeginBattle`、没打完 `ReleaseBattle`**，否则战斗进行中会被自动保存，读档后得到一个没有对应战斗的在途标记
  （读档时 `ReloadFromSave` 会清掉残留标记并重发通知，`NarrativeService.cs:116-122`）。
- 战斗侧订阅 `BattleStageEnteredEvent`（`NarrativeService.cs:361` 发布）而不是轮询阶段；样例见 [`battle-extension-guide.md`](../battle/battle-extension-guide.md)「新增一只 BOSS」。
- 要让场景物体随剧情标记显隐（BOSS 退场、门开关），挂 `NarrativeFlagVisibility`（`flagKey` = 阶段 `setFlags` 写的标记），不要自己订阅 `NarrativeChangedEvent`。
- 要给 NPC / BOSS 一个主动交互入口：同物体挂 `NarrativeTrigger` + `DialogueInteractable`，`NarrativeTrigger` 绑定时自动接管交互（`NarrativeTrigger.cs:55-60`）；遭遇表的 `repeat: Reenter` 对主动交互可重复触发（`NarrativeService.cs:141-154` 的纪元）。
