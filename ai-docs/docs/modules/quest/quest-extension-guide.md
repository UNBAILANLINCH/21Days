---
type: extension-guide
module: quest
layer: runtime
maturity: stable
---

# Quest 扩展指南

> 要加任务、加目标类型、换美术时看这份。架构见 [`quest-module-guide.md`](quest-module-guide.md)，对外签名见 [`quest-external-api.md`](quest-external-api.md)。

## 扩展点一览

| 要加什么 | 扩展点 | 改代码吗 |
| --- | --- | --- |
| 一条新任务 | `Tables/Data/quest/<id>.json` | 否 |
| 一种新的目标完成条件类型 | `quest.xml` 枚举 + `QuestObjectiveKind` + `QuestRules.Report` + 上报点 | 是 |
| 新的指引目标来源（非地点 / 非对话 NPC） | `QuestSceneBinder.TryResolveTarget` | 是 |
| 换 HUD / 面板 / 标记美术 | 对应预制体的子物体，字段名不变 | 否 |
| 调表现参数（指引留白、刷新间隔、固定文案、通知文案） | `QuestConfig.asset` 字段 | 否 |

## 新增一条任务

编辑器：菜单 `21Days/策划/任务编辑器`（`Game.Editor.Quest.QuestEditorWindow`），「保存并生成」一步完成下面 1–3 步，且 4 的校验在编辑期就会报出全部问题。

1. 在 `Tables/Data/quest/` 新建 `<id>.json`（文件名 = id），照 `1001.json` 的形状写。**每个字段都要写**：`prerequisites`（空写 `[]`）、`objectives` 每项的 `count`（1 起）、`location`（空写 `""`）。
2. `kind` 主线 (`Main`) 同一时刻只有一条会被激活；要接在某条主线后面，把它的 id 填进后续主线的 `prerequisites`。
3. 跑 `powershell -ExecutionPolicy Bypass -File scripts/gen-tables.ps1`，生成物一起提交。
4. 非法内容（规则见 guide「内容表」）在首次访问 `QuestService.Content` 时抛出，不会半途生效。

## 新增一种目标完成条件类型

现有三种：`TalkTo`（对话）、`ReachLocation`（到达地点）、`Counter`（自定义计数）。加第四种要改四处：

1. `Tables/Defines/quest.xml` 的 `ObjectiveKind` 枚举加一项（同名同序，`comment` 写清楚 `key` 怎么解释）。
2. `Assets/_Project/Scripts/Runtime/Quest/QuestObjectiveKind.cs` 加同名枚举项（`QuestCatalog.TranslateObjectiveKind` 按**名字**映射，两边不同名会直接抛错，不会读错类别）。
3. 如果新类型对键格式有约束（像 TalkTo 要求 `Key` 是整数字符串），在 `QuestContent.ValidateObjectives`（`QuestContent.cs:58`）补一段校验。
4. 找到「谁能判定这个条件被满足」，在那一侧调 `QuestService.Report(kind, key, amount)`——参照
   `QuestObjectiveDriver.HandleDialogueEnded` / `Tick`：一次性事实（如对话结束）在回调里报一次；每帧判定的（如进入范围）在 `Tick` 里判、命中后 `return`（`Report` 会重建 `InProgress` 缓存，一帧只报一次）；
   `QuestRules.Report`（`QuestRules.cs:95`）本身不用改：它只按 `QuestObjectiveDefinition.Matches(kind, key)` 匹配当前目标。

## 加指引来源

在 `QuestSceneBinder.TryResolveTarget`（`QuestSceneBinder.cs:94`，现有解析顺序与锚点三级规则见 guide 类分工表）加一个分支
（比如指向某个动态生成的物体），自己决定 `QuestTarget` 的 `Position`（测距）/ `Anchor`（标记与投影）怎么给；
目标是带对话图标的 NPC 时第三个参数传其 `DialogueInteractable`，任务标记才会接管它的「…/!」图标，否则传 null。
返回 `true` 才会被投影与测距使用；解析不到保持 `false`（HUD 会 `HideGuidance` 而不是显示错误位置）。**不要**把这段逻辑挪到 `QuestHudPresenter` 里：场景坐标解析只该在 `QuestSceneBinder` 一处。

## 换 HUD / 面板 / 标记美术

同名替换子物体的 Sprite / 字体不用改预制体或代码；改了层级结构要保证下列**字段名对应的子物体名**不变，否则 `OnOpenAsync` 里的 `Validate()` 会逐个点名抛出。

`QuestHudView`（`Prefabs/UI/QuestHudView.prefab`，地址 `QuestHudView`）：

| 字段 | 用途 |
| --- | --- |
| `root` / `button` | 整条任务栏（对白进行中整体隐藏）/ 任务栏按钮（点击打开面板） |
| `title` / `objective` | 任务标题（未追踪时显示占位文字）/ 当前目标文本 |
| `guidanceRoot` | 屏外贴边标识根（`RectTransform`，锚点须在 HUD 根中心） |
| `guidancePivot` | 标识中心的空枢轴 `ArrowPivot`，旋转它让箭头绕标识公转；箭头与距离文本都挂它下面（层级尺寸见 guide 接线要求） |
| `guidanceArrow` | 屏外箭头，只管显隐（可空：不显示箭头） |
| `distanceLabel` | 距离文字，与箭头相对，运行时反向旋转保持正立 |
| `keyHint` | 任务键键位提示（TMP，可空）；运行时由 `QuestHudPresenter` 按 `Gameplay/Journal` 第一条键盘绑定写入（如「Tab」），没有键盘绑定时隐藏 |

`QuestPanelView`（`Prefabs/UI/QuestPanelView.prefab`，地址 `QuestPanelView`）：

| 字段 | 用途 |
| --- | --- |
| `listRoot` / `emptyLabel` | 任务列表容器 / 无进行中任务时的提示 |
| `itemTemplate` | 列表项模板（根挂 `Button`；子物体 `Title`(TMP)、`Kind`(TMP)、`Highlight`），运行时隐藏、按需复用 |
| `detailRoot` / `detailTitle` / `detailKind` / `detailDescription` | 详情区根节点 / 标题 / 类型 / 描述 |
| `objectiveRoot` | 目标清单容器 |
| `objectiveTemplate` | 目标行模板（子物体 `Mark`(TMP)、`Text`(TMP)），运行时隐藏、按需复用 |
| `trackButton` / `trackLabel` / `closeButton` | 追踪 / 取消追踪按钮及文案 / 关闭按钮（已完成标签下 `trackButton` 整个物体隐藏） |
| `tabInProgressButton` / `tabCompletedButton` | 「进行中」「已完成」标签按钮（预制体里是 `TabInProgress` / `TabCompleted`，挂 `UIButtonFeedback`）；各自须有子物体 `Selected` 作选中态，`Validate` 会查；`emptyLabel` 的文字由 Controller 按标签换（暂无进行中的任务 / 还没有完成的任务） |
| `defaultSelected`（`UIView` 基类字段） | 打开后键盘 / 手柄默认选中项，当前拖的是 `trackButton`（列表项是运行时复制的，预制体里拖不到） |

列表项与目标行都是复用池（按需 `Instantiate`，多余的隐藏），不要改成每次销毁重建。

`QuestTargetMarker`（`Prefabs/World/QuestTargetMarker.prefab`，Addressables 地址 `QuestTargetMarker`）：
子物体 `Icon` 的 Sprite / 颜色 / `sortingOrder` 可换；**物体名与 `icon` 字段的接线不要动**。朝向相机由 `CameraBillboard` 负责。
`Icon` 缩放 0.45 与对话标记 `MarkerIdle` / `MarkerFocus` 对齐（它替代 NPC 的「…/!」图标），两边尺寸要同步改。

## 调表现参数（`QuestConfig`）

| 字段（默认） | 含义 |
| --- | --- |
| `edgeMargin`（72） | 屏外指引标识中心贴边留白（参考分辨率像素）；须 ≥ 箭头公转半径 48 + 箭头半宽 14 + 余量，否则箭头朝外时被裁 |
| `distanceRefreshInterval`（0.2） | 距离文字刷新间隔（秒），避免每帧改文本 |
| `markerLift`（0.3） | NPC 没有对话图标锚点时，标记在其 Collider 顶部之上再抬高多少（米） |
| `locationMarkerHeight`（1.5） | 地点目标（或无 Collider 的 NPC）头顶标记离地高度（米） |
| `targetMarkerAddress`（"QuestTargetMarker"） | 世界空间目标标记预制体的 Addressables 地址，要与预制体登记的地址一致 |
| `untrackedLabel` / `mainKindLabel` / `sideKindLabel` | HUD / 面板固定文案 |
| `activatedNotificationFormat`（"接取任务：{0}"） / `completedNotificationFormat`（"任务完成：{0}"） | 接取 / 完成通知标题格式，`{0}` 为任务标题；留空则只显示标题，写坏（如 `{1}`）退回「格式原文 + 标题」不抛 |

## 不该从哪扩

- **不改 `QuestService` 直接持有场景坐标或 UI 引用**：它是纯门面，场景解析在 `QuestSceneBinder`，表现在 `QuestHudPresenter` / `QuestPanelController`。
- **不在 `QuestRules` 里加表现相关逻辑**（指引、UI 文案）：它只管内容推进与存档语义，纯 C# 可测。
- **不在别处切 `DialogueInteractable.SetMarkerOverridden`**：接管 / 交还只在 `QuestHudPresenter.SetOverridden` 一处成对做。
- **不在 `Game.Core` 里加任务名词**；新模块注册事件走同一个 `GameplayInstaller.InstallEvents`，不另起一套。
