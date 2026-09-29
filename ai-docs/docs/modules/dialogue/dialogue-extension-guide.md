---
type: extension-guide
module: dialogue
layer: runtime
maturity: stable
---

# Dialogue 扩展指南

> 要给对白加内容、加触发方式、接条件时看这份。架构见 [`dialogue-module-guide.md`](dialogue-module-guide.md)，
> 对外签名见 [`dialogue-external-api.md`](dialogue-external-api.md)。

## 扩展点一览

| 要加什么 | 扩展点 | 改代码吗 |
| --- | --- | --- |
| 一棵新对话树 | `Tables/Data/dialogue/<id>.json` | 否 |
| 某句台词前插播演出 | 节点 `performance` 字段（见「给一句台词插播演出」） | 否 |
| 新角色 / 新表情 | `Tables/Data/dialogue_character.json` + Addressables + 立绘 PNG | 否 |
| 真实条件来源 | 实现 `IDialogueConditionSource`，在 `DialogueInstaller` 替换注册 | 是 |
| 新的触发方式 | 调 `DialogueService.PlayAsync` 或 `DialogueInteractable.Interact` | 调用方侧 |
| 调表现手感 | `DialogueConfig.asset` 字段 | 否 |
| NPC（带树 / 无树）、选项图标、HUD / 气泡 / 弹窗美术 | 场景组件、气泡预制体实例、表里 `icon`、预制体 Sprite | 否 |

## 新增一棵对话树

1. 在 `Tables/Data/dialogue/` 新建 `<id>.json`（文件名 = `id`，一文件一棵树），照 `1001.json` 的形状写。
   **每个字段都要写**（Luban 不允许缺字段）：`revision`（填 1）、`blocking`（填 `true`）、空的 `choices: []`、空串 `""`；
   选项的 `icon` 也要写（无图标写 `""`，有图标写 Addressables 地址并在 UI 组登记，如 `Dialogue/ChoiceIcon_Go`）。
2. `Line` 必须有 `next`；`Choice` 至少一个选项，每项 `next` / `outcome` **恰好一个**；结局各用一个 `End`；`speaker` 空 = 旁白。
3. 跑 `powershell -ExecutionPolicy Bypass -File scripts/gen-tables.ps1`，生成物（`cfg.dialogue.*` 与 `dialogue_tbdialogue.bytes`）一起提交。
4. 场景里按下文「加一个带树 NPC」摆物体；在 `DialogueCatalogTests.cs` 补结构断言，跑 `/unity-test EditMode Dialogue`。

改已上线台词的文字时把该节点 `revision` +1：存档恢复会拒绝版本不符的快照，已读键也按版本区分。

## 给一句台词插播演出

1. 在该节点的 JSON 里把 `"performance": ""` 改成演出 id，如 `"performance": "perf_sample_scene_talk"`（世界舞台示例；旧叠加示例 greeting 已下架）；只改这个字段并把该节点 `revision` +1，跑 `scripts/gen-tables.ps1`。
2. 演出 id 就是演出预制体（舞台 + 时间轴）在 Addressables 的地址，由动画师在演出编辑器里建好并登记（见 `PRP/performance-pipeline/prp.md` 2.2 / 2.8）；地址不存在时演出服务报错，对白埋 `performance_failed` 后照常显示这一句。
3. 语义：进入该节点、摆台词**之前**先播完演出；玩家确认跳过对白后的快进句不插播；`End` 节点上的演出不会播（进入即结束）。
   Boot 没挂 `PerformanceInstaller` 时埋 `performance_unavailable` 并直接显示台词。
4. 时停与输入图不用管：对白与演出两边服务各自持令牌、只恢复进来前的状态。参照 `Tables/Data/dialogue/1003.json`。
5. **摆放锚点**：世界舞台演出要知道摆在哪。NPC 交互拉起的对白自动带 NPC 自身 Transform（`DialogueInteractable`）；
   代码拉起时用 `DialogueService.PlayAsync(id, 锚点)` 把说话的 NPC 传进来。只传 `id`（不带锚点）时插播不摆放，
   世界舞台会生成在世界原点、落到地面以下（演出只有世界舞台一种，带了锚点就一定按锚点摆）。
6. **场景角色自动隐藏，不用配**：插播期间场景里全部带 `ChibiPuppet` 的角色（玩家、NPC、巡逻怪，连同根下名牌 / 标记 / 光圈）
   被藏起，演出结束恢复；这由 `PerformanceService.PlayAsync` 统一处理（对直接播放、对白插播、场景触发一视同仁），
   见 `performance-module-guide.md`「世界舞台」一节，本模块不用配也不用改代码。新角色要被正确整根藏掉：优先配
   `ChibiPuppetMotion.TrackedRoot`，没配时兜底取场景顶层物体整棵藏，别把它收进装地形 / 道具的公共容器。

## 加一个带树 NPC 并配头顶标记

1. 根物体：`BoxCollider`（3D）或 `Collider2D`（2D）+ `DialogueInteractable`（填 Id、`Display Name`，`Interact Radius` 建议 2.0，约一个多身位，`Actor` 留空）+ `DialogueInteractableMarker`。
2. 子物体 `MarkerIdle` / `MarkerFocus`（SpriteRenderer，图 `Art/Sprites/Dialogue/Marker_Idle` / `Marker_Focus`）、
   `NameLabel`（TMP 3D）；3D 场景三者各挂 `CameraBillboard`。拖进标记的 `bubbleIdle` / `bubbleFocused` / `nameLabel`，`target` 拖自己。
3. 玩家根要有 `DialogueInteractionActor`；相机要有 `PhysicsRaycaster`（3D）或 `Physics2DRaycaster`（2D）。参照 SampleScene 的 `Npc_Elder`。
4. 从 Boot → 标题「开始」进场景验证（直接 Play 没有对白服务）：走近「…」→ 最近的变「!」+ 名字 + 右下角「对话」→ 确认键 / 点按钮拉起。

## 加一个无树 NPC（只说常驻台词）

1. 同上摆碰撞体、`DialogueInteractable`、标记；`Dialogue Id` 填 **0**，`Bubble Lines` 填台词（按序循环）。
2. `Prefabs/World/DialogueSpeechBubble.prefab` 实例作 NPC 子物体放标记之上（3D 挂 `CameraBillboard`，`target` 留空向父级找），并拖进标记的 `speechBubble`，否则「!」与气泡重叠。
3. 逐字 / 停留（`holdSeconds` 默认 4）/ 淡出时长在实例上调。参照 SampleScene 的 `Npc_Villager`。

保留气泡的预制体连接，不要 Unpack 后复制样式。SampleScene 的 `Yao_WellWoman` 曾是独立副本，
预制体缩放修为 0.0035 后仍停在旧值 0.01，导致同一相机下只有妇人的气泡过大；现已接回共用预制体。
新增无树 NPC 后，要从标题进入实际探索并覆盖该 NPC，不能只复验村民。

## 换 HUD / 气泡 / 弹窗美术

| 换什么 | 改哪 | 别动 |
| --- | --- | --- |
| 右下角「对话」卡片 | `Prefabs/UI/DialogueInteractHudView.prefab` 的 `Root` / `Icon` / 边框 Image | `root`、`button`、`label` 接线；地址 `DialogueInteractHudView` |
| 跳过确认弹窗 | `Prefabs/UI/DialogueSkipConfirmView.prefab` 的 `Panel` / 按钮 | `message` / `confirm` / `cancel` 接线；地址同名 |
| 头顶气泡 | `Prefabs/World/DialogueSpeechBubble.prefab` 的 `Frame`（`Art/Sprites/Dialogue/Bubble_Frame`）、字体 | `Content` / `Name` / `Body` / `Arrow` 与根 `CanvasGroup` 接线；两级 `VerticalLayoutGroup` + 根 `ContentSizeFitter`（高度随文字自适应，别写死高度） |
| 头顶标记 | 替换 `Marker_Idle.png` / `Marker_Focus.png` | 子物体名与标记字段接线 |
| 选项胶囊 | `DialogueView.prefab` 的 `ChoiceTemplate` 背景与 `Label` | 子物体名 `Icon`（写死）；`ChoiceRoot` 锚点 |
| 头像框位置 / 尺寸 | `DialogueView.prefab` 的 `PortraitLeft` / `PortraitRight` 的 RectTransform（`PortraitLeft` ↔ `PerformanceView.prefab` 的 `Avatar`/`AvatarFrame` 同位，`PortraitRight` ↔ `AvatarRight`/`AvatarFrameRight` 同位） | 它们的 `anchoredPosition` 即入场终点；别在其下挂子物体（换表情时会被整块复制成 `…Ghost` 残影）；左槽从左侧进出、右槽从右侧进出，没有压暗也没有缩放，改这两个位置务必同步 `PerformanceView.prefab`，否则 `TalkPanelConsistencyTests` 会挂 |
| 名牌样式 | `DialogueView.prefab` 的 `Speaker`（TMP） | punch 动效直接改它的 `localScale` 与 `alpha`，别把它放进会被布局组件改缩放的父物体 |

同名替换 PNG 不用改预制体；改了结构跑 Showcase 兜底（`Validate()` 会点名漏接字段）。

气泡根宽 400、等比缩放 0.006（父级缩放为 1 时宽 2.4 世界单位），高度继续自适应。
调整大小后用 `Bubble_ShowsAboveHead_WithoutPausing` 同时检查文字完整排版与屏幕内可见，不能只凭 TMP 没有溢出判定通过。
实际探索补跑 `DialogueBubbleShowcase.Exploration_BothSpeakers_KeepBubbleInsideViewport`：通过真实移动和交互输入，
分别验证村民侧面、井边妇人侧面和正面的完整文字、相机视口边界与淡出；复用现有相机配置，不修改镜头来迁就气泡。
`DialogueSpeechBubble` 在相机跟随和 Billboard 更新后约束气泡边界，默认保留 12 像素距离；
正常尺寸放不下时临时缩小，镜头移开后恢复原缩放和头顶锚点。调整位置或尺寸须在组件 Awake 前完成，
不要在运行中累加边缘偏移。`DialogueSpeechBubbleTests` 覆盖透视／正交相机四边、过大尺寸、无漂移及恢复。

## 新增角色 / 表情

1. 立绘 PNG 放 `Assets/_Project/Art/Sprites/Dialogue/Portrait_<角色>_<表情>.png`（导入规则自动生效）。
2. 在 Addressables UI 组登记地址 `Dialogue/Portrait_<角色>_<表情>`。
3. `Tables/Data/dialogue_character.json` 加角色（`id`、`displayName`、`defaultExpression` ∈ `expressions[]`），`sprite` 填上一步地址。
4. 跑生成；`DialogueCatalogTests.Characters_EveryExpression_HasPortraitAddress` 会兜底检查地址非空。

表情不存在时首次访问 Catalog 抛 `ArgumentException`（带对话 id）。

## 接入条件源（Narrative 接线时）

1. 在 Narrative 侧（或专门的接线类）实现 `IDialogueConditionSource.Snapshot(string targetId)`，
   从真实状态拼出 `EncounterContext`。要廉价、无副作用——对白期间每 0.25 s 及每次提交都会被调。
2. DialogueInstaller 已优先解析 NarrativeConditionSource；未装 NarrativeInstaller 时保留 DefaultDialogueConditionSource，
   不要求删除兼容实现。叙事调用方传稳定 targetId，不能将所有目标都混成 dialogue:<id>。
3. 条件事实新增时，`EncounterContext.Fact` 与 `Tables/Defines/dialogue.xml` 的 `ConditionFact` **同名**加一项，重跑生成。

依赖边界：按 2026-09-29 Narrative PRP，运行适配层可引用 Dialogue 公开入口；原七个叙事纯规则文件不能引入玩法服务或 Unity。

## 接新的触发方式

- 代码触发（遭遇、剧情、过场）：`await dialogueService.PlayAsync(id, ct)`，按 `Outcome` 分支；先看 `IsRunning`，进行中再调会抛。
- 场景物体的其它输入（进入触发区等）：调物体上的 `DialogueInteractable.Interact()`，范围 / 占用 / 绑定已判定；
  运行时生成的先 `Bind(service)`（不参与焦点）。确认键与 HUD 已由 `DialogueInteractionFocus` 接好，别再加一套按键监听。

## 改表现参数（`DialogueConfig`）

| 字段（默认） | 含义 | 字段（默认） | 含义 |
| --- | --- | --- | --- |
| `charactersPerSecond`（35） | x1 档打字速度（字 / 秒） | `revealTapCount`（3） | 打字中连点几次补全 |
| `historyLimit`（500） | 历史上限，超出丢最早并提示「已省略」 | `tapWindowSeconds`（0.5） | 相邻两次点击最大间隔 |
| `speedSteps`（1, 2, 4） | 倍速循环表，非空、全 > 0 | `autoAdvanceSeconds`（1.5） | 自动模式停留（再除以倍速） |
| `punctuationPauseSeconds`（0.12） | 标点后停顿（x1 秒，倍速下同比缩短），≥ 0 | `punctuationChars`（`，。！？…；：、,.!?`） | 算标点的字符；空 = 不停 |
| `portraitSlideDistance`（24） | 头像入场 / 退场水平滑动距离（px），≥ 0；左槽从左侧进出、右槽从右侧进出，头像在白框内宜小，主要靠淡入 | `portraitSlideSeconds`（0.25） | 入场 / 退场时长；0 = 直接到位 |
| `portraitCrossfadeSeconds`（0.15） | 同槽换表情交叉淡化时长 | `nameTagPunchSeconds`（0.15） | 说话者变化时名牌 punch 时长；0 = 不做 |
| `nameTagPunchScale`（1.15） | punch 起始缩放，> 0 | | |

**已删除**：`portraitDimSeconds` / `portraitDimColor` / `portraitDimScale`（非说话者压暗）——现在只显示说话者那一槽，非说话者直接收起，没有压暗态。

动效参数经 `ToPlaybackSettings()` → `DialoguePlaybackSettings.Motion`（`DialogueMotionSettings`）→ Controller `view.SetMotion` 下发；View 不读 Config。
要换对话框开合方式改预制体 `DialogueView` 的 Transition（现为 `SlideUp`），要预设之外的花样重写 `PlayOpenTransitionAsync` / `PlayCloseTransitionAsync`。

非法值在首次 `PlayAsync` 时抛 `ArgumentException`，不影响启动。

## 不该从哪扩

- **不改 `DialogueRules` 做表现**：点击节奏、速度、自动、动效进 `DialoguePlaybackPolicy`（补其测试）或 Controller。
- **不在 View 里改状态**：`DialogueView` 只显示与抛事件；新按钮 = 新 `event` + Controller 里订阅 / 退订成对。
- **不在 Core 加对话名词**；不另起一套暂停或输入切换；Narrative 纯规则不引用 `Game.Dialogue`，运行协调器只使用其公开服务入口。
