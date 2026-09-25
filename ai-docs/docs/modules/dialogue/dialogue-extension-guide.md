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

## 换 HUD / 气泡 / 弹窗美术

| 换什么 | 改哪 | 别动 |
| --- | --- | --- |
| 右下角「对话」卡片 | `Prefabs/UI/DialogueInteractHudView.prefab` 的 `Root` / `Icon` / 边框 Image | `root`、`button`、`label` 接线；地址 `DialogueInteractHudView` |
| 跳过确认弹窗 | `Prefabs/UI/DialogueSkipConfirmView.prefab` 的 `Panel` / 按钮 | `message` / `confirm` / `cancel` 接线；地址同名 |
| 头顶气泡 | `Prefabs/World/DialogueSpeechBubble.prefab` 的 `Frame`（`Art/Sprites/Dialogue/Bubble_Frame`）、字体 | `Content` / `Name` / `Body` / `Arrow` 与根 `CanvasGroup` 接线；两级 `VerticalLayoutGroup` + 根 `ContentSizeFitter`（高度随文字自适应，别写死高度） |
| 头顶标记 | 替换 `Marker_Idle.png` / `Marker_Focus.png` | 子物体名与标记字段接线 |
| 选项胶囊 | `DialogueView.prefab` 的 `ChoiceTemplate` 背景与 `Label` | 子物体名 `Icon`（写死）；`ChoiceRoot` 锚点 |

同名替换 PNG 不用改预制体；改了结构跑 Showcase 兜底（`Validate()` 会点名漏接字段）。

## 新增角色 / 表情

1. 立绘 PNG 放 `Assets/_Project/Art/Sprites/Dialogue/Portrait_<角色>_<表情>.png`（导入规则自动生效）。
2. 在 Addressables UI 组登记地址 `Dialogue/Portrait_<角色>_<表情>`。
3. `Tables/Data/dialogue_character.json` 加角色（`id`、`displayName`、`defaultExpression` ∈ `expressions[]`），`sprite` 填上一步地址。
4. 跑生成；`DialogueCatalogTests.Characters_EveryExpression_HasPortraitAddress` 会兜底检查地址非空。

表情不存在时首次访问 Catalog 抛 `ArgumentException`（带对话 id）。

## 接入条件源（Narrative 接线时）

1. 在 Narrative 侧（或专门的接线类）实现 `IDialogueConditionSource.Snapshot(string targetId)`，
   从真实状态拼出 `EncounterContext`。要廉价、无副作用——对白期间每 0.25 s 及每次提交都会被调。
2. 在 `DialogueInstaller.Install` 把 `builder.Register<IDialogueConditionSource, DefaultDialogueConditionSource>`
   （`DialogueInstaller.cs:51`）换成新实现，然后删掉 `DefaultDialogueConditionSource.cs` 与其 `.meta`（`git rm`）。
3. 条件事实新增时，`EncounterContext.Fact` 与 `Tables/Defines/dialogue.xml` 的 `ConditionFact` **同名**加一项，重跑生成。

依赖方向：Dialogue 已依赖 Narrative，实现类放 Narrative 目录就不能引用 `Game.Dialogue`——放第三方接线处或 Dialogue 目录内。

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

非法值在首次 `PlayAsync` 时抛 `ArgumentException`，不影响启动。

## 不该从哪扩

- **不改 `DialogueRules` 做表现**：点击节奏、速度、自动、动效进 `DialoguePlaybackPolicy`（补其测试）或 Controller。
- **不在 View 里改状态**：`DialogueView` 只显示与抛事件；新按钮 = 新 `event` + Controller 里订阅 / 退订成对。
- **不在 Core 加对话名词**；不另起一套暂停或输入切换；不在 `Game.Narrative` 里引用 `Game.Dialogue`。
