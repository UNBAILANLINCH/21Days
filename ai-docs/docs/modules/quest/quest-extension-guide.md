---
type: extension-guide
module: quest
layer: runtime
maturity: seed
---

# Quest 扩展指南

> 要加任务、加目标类型、换美术时看这份。架构见 [`quest-module-guide.md`](quest-module-guide.md)，
> 对外签名见 [`quest-external-api.md`](quest-external-api.md)。

## 扩展点一览

| 要加什么 | 扩展点 | 改代码吗 |
| --- | --- | --- |
| 一条新任务 | `Tables/Data/quest/<id>.json` | 否 |
| 一种新的目标完成条件类型 | `quest.xml` 枚举 + `QuestObjectiveKind` + `QuestRules.Report` + 上报点 | 是 |
| 新的指引目标来源（非地点 / 非对话 NPC） | `QuestSceneBinder.TryResolveTarget` | 是 |
| 换 HUD / 面板 / 标记美术 | 对应预制体的子物体，字段名不变 | 否 |
| 调表现参数（指引留白、悬浮偏移、刷新间隔、固定文案） | `QuestConfig.asset` 字段 | 否 |

## 新增一条任务

1. 在 `Tables/Data/quest/` 新建 `<id>.json`（文件名 = id），照 `1001.json` 的形状写。**每个字段都要写**：
   `prerequisites`（空写 `[]`）、`objectives` 每项的 `count`（1 起）、`location`（空写 `""`）。
2. `kind` 主线 (`Main`) 同一时刻只有一条会被激活；要接在某条主线后面，把它的 id 填进后续主线的 `prerequisites`。
3. 跑 `powershell -ExecutionPolicy Bypass -File scripts/gen-tables.ps1`，生成物一起提交。
4. `QuestContent` 构造期会校验 id 唯一、目标非空、前置引用存在且无环；`QuestCatalog` 额外校验 TalkTo 对话存在——
   非法内容在首次访问 `QuestService.Content` 时抛出，不会半途生效。

## 新增一种目标完成条件类型

现有三种：`TalkTo`（对话）、`ReachLocation`（到达地点）、`Counter`（自定义计数）。加第四种要改四处：

1. `Tables/Defines/quest.xml` 的 `ObjectiveKind` 枚举加一项（同名同序，`comment` 写清楚 `key` 怎么解释）。
2. `Assets/_Project/Scripts/Runtime/Quest/QuestObjectiveKind.cs` 加同名枚举项（`QuestCatalog.TranslateObjectiveKind`
   按**名字**映射，两边不同名会直接抛错，不会读错类别）。
3. 如果新类型对键格式有约束（像 TalkTo 要求 `Key` 是整数字符串），在 `QuestContent.ValidateObjectives`
   （`QuestContent.cs:58`）补一段校验。
4. 找到「谁能判定这个条件被满足」，在那一侧调 `QuestService.Report(kind, key, amount)`——参照
   `QuestObjectiveDriver.HandleDialogueEnded` / `Tick`：事件驱动的一次性事实（如对话结束）在回调里上报一次；
   需要每帧判定的（如进入范围）在 `Tick` 里判、命中后 `return`（`Report` 会重建 `InProgress` 缓存，一帧只报一次）。
   `QuestRules.Report`（`QuestRules.cs:93`）本身不用改：它只按 `QuestObjectiveDefinition.Matches(kind, key)` 匹配当前目标。

## 加指引来源

`QuestHudPresenter.Tick` 通过 `QuestSceneBinder.TryResolveTarget(in QuestObjectiveDefinition, out QuestTarget target)`
（`QuestSceneBinder.cs:91`）取当前目标的世界坐标，`QuestTarget` 给两个点：测距用 `Position`、标记与投影用 `Anchor`。
现有规则按顺序尝试：`LocationKey` 非空查 `QuestLocation`；否则 `ReachLocation` 用自己的 `Key`（即地点键）查
`QuestLocation`；否则 `TalkTo` 按 `Key`（对话编号）在 `DialogueSceneBinder.Bound` 里找 NPC。锚点规则：地点 = 位置 +
`QuestConfig.LocationMarkerHeight`；NPC = 根物体 `Collider.bounds.max.y + QuestConfig.MarkerLift`（x/z 取包围盒中心），
没有 `Collider` 时退回地点规则。要支持新的目标来源（比如指向某个动态生成的物体），在这个方法里加一个分支，
自己决定 `Position`/`Anchor` 怎么给，返回 `true` 才会被后续投影与测距使用；解析不到时保持 `false`，
`QuestHudPresenter` 会 `HideGuidance` 而不是显示错误位置。**不要**把这段逻辑挪到 `QuestHudPresenter` 里：
场景坐标解析只该在 `QuestSceneBinder` 一处。

## 换 HUD / 面板 / 标记美术

同名替换子物体的 Sprite / 字体不用改预制体或代码；改了层级结构要保证下列**字段名对应的子物体名**不变，
否则 `OnOpenAsync` 里的 `Validate()` 会逐个点名抛出。

`QuestHudView`（`Prefabs/UI/QuestHudView.prefab`，地址 `QuestHudView`）：

| 字段 | 用途 |
| --- | --- |
| `root` | 整条任务栏（对白进行中整体隐藏） |
| `button` | 任务栏按钮，点击打开面板 |
| `title` | 任务标题 / 未追踪占位文字 |
| `objective` | 当前目标文本 |
| `guidanceRoot` | 指引标识根（`RectTransform`，锚点须在 HUD 根中心） |
| `guidanceArrow` | 屏外箭头（可空：不显示箭头） |
| `distanceLabel` | 距离文字 |

`QuestPanelView`（`Prefabs/UI/QuestPanelView.prefab`，地址 `QuestPanelView`）：

| 字段 | 用途 |
| --- | --- |
| `listRoot` | 任务列表容器 |
| `itemTemplate` | 列表项模板（根挂 `Button`；子物体 `Title`(TMP)、`Kind`(TMP)、`Highlight`），运行时隐藏、按需复用 |
| `emptyLabel` | 无进行中任务时的提示 |
| `detailRoot` | 详情区根节点 |
| `detailTitle` / `detailKind` / `detailDescription` | 详情标题 / 类型 / 描述 |
| `objectiveRoot` | 目标清单容器 |
| `objectiveTemplate` | 目标行模板（子物体 `Mark`(TMP)、`Text`(TMP)），运行时隐藏、按需复用 |
| `trackButton` / `trackLabel` | 追踪 / 取消追踪按钮及文案 |
| `closeButton` | 关闭按钮 |

列表项与目标行都是复用池（按需 `Instantiate`，多余的隐藏），不要改成每次销毁重建。

`QuestTargetMarker`（`Prefabs/World/QuestTargetMarker.prefab`，Addressables 地址 `QuestTargetMarker`）：
子物体 `Icon` 的 Sprite / 颜色 / `sortingOrder` 可换；**物体名与 `icon` 字段的接线不要动**，`Show/Hide` 靠这个
引用摆世界坐标。朝向相机由 `Icon` 上的 `CameraBillboard` 负责，换美术不用碰。

## 调表现参数（`QuestConfig`）

| 字段（默认） | 含义 |
| --- | --- |
| `edgeMargin`（48） | 屏外指引贴边留白（像素） |
| `hoverOffset`（80） | 屏内指引相对目标向上偏移（像素） |
| `distanceRefreshInterval`（0.2） | 距离文字刷新间隔（秒），避免每帧改文本 |
| `markerLift`（0.3） | NPC 目标头顶标记在其 Collider 顶部之上再抬高多少（米） |
| `locationMarkerHeight`（1.5） | 地点目标（或无 Collider 的 NPC）头顶标记离地高度（米） |
| `targetMarkerAddress`（"QuestTargetMarker"） | 世界空间目标标记预制体的 Addressables 地址，要与预制体登记的地址一致 |
| `untrackedLabel` / `mainKindLabel` / `sideKindLabel` | HUD / 面板固定文案 |

## 不该从哪扩

- **不改 `QuestService` 直接持有场景坐标或 UI 引用**：它是纯门面，场景解析在 `QuestSceneBinder`，
  表现在 `QuestHudPresenter` / `QuestPanelController`。
- **不在 `QuestRules` 里加表现相关逻辑**（指引、UI 文案）：它只管内容推进与存档语义，纯 C# 可测。
- **不在 `Game.Core` 里加任务名词**；`GameplayInstaller.InstallEvents` 本身不含任务概念，新模块要用同一套扩展点，
  不要另起一套事件注册方式。
