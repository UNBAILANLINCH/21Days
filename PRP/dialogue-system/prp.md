# PRP: 对话系统（交互拉起 · 表现 · 世界时停）

> 状态：设计定稿，供执行；2026-09-25。PRD 见 [`prd.md`](prd.md)，任务见 [`tasks.md`](tasks.md)。
> 前置：[`PRP/narrative-dialogue/prp.md`](../narrative-dialogue/prp.md) 与 [`follow-up-integration.md`](../narrative-dialogue/follow-up-integration.md)。本文只改 Dialogue 半边的表现语义并把它接进 Unity；Narrative 规则层不动。
> 本文的类名、方法签名是**并行派单的对齐契约**，实现时形状不变、细节可微调；改契约先改本文。

## 1. 现状与差异

已有（`Assets/_Project/Scripts/Runtime/Dialogue/`，纯规则有 EditMode 测试）：`DialogueContent`（Line/Choice/End，三槽立绘，选项条件）、`DialogueRules`（Preparing→Typing→AwaitAdvance/AwaitChoice→Completed，历史、已读、Capture/Restore）、`DialogueIntent`、`DialogueSaveData`、`DialogueReadData`、`DialogueCharacter`、`DialogueConfig`、`DialogueController`、`DialogueView`、`DialogueHistoryView`。

与本次需求的差异（都在表现层，规则层只动两处）：

| 需求 | 现状 | 决定 |
| --- | --- | --- |
| 立绘在对话框左上 / 右上角，说话者一侧亮 | 左 / 中 / 右三槽 | 收成两槽：`0 = Left`、`1 = Right`；说话者高亮逻辑沿用 |
| 三连点补全 | Typing 时单点即补全 | 规则不变（Advance 在 Typing 仍是补全）；**何时把点击转成 Advance** 由新的纯 C# 策略类决定 |
| 倍速 x1/x2/x4 循环、自动播放 | 无 | 策略类持有挡位与自动开关；Controller 按策略推进 |
| 跳过整段对话 | 「已读快进」Toggle（逐句定时推进） | 规则新增 `Skip`（同步快进到下一个 Choice / End）；删除已读快进的表现路径，已读记录保留 |
| 世界时停 | `BlocksWorld` 属性，由外部接 `SimulationRunner.SetPaused` | 新增 Core 通用 `IWorldPauseService`（timeScale + 逻辑 tick，多持有者），对话服务持有一次 |
| 交互拉起 / 编号 | 无入口、无内容表 | `DialogueService.PlayAsync(int id)`；Luban 表 `TbDialogue` / `TbDialogueCharacter`；`DialogueInteractable` 组件 |

## 2. 架构

```text
场景：DialogueInteractable（点击 / Interact()）
        │ 由 DialogueSceneBinder 在场景加载时注入 DialogueService
        ▼
DialogueService.PlayAsync(id)      ← 其他模块也可直接调
  ├─ DialogueCatalog：IConfigService.Tables → DialogueContent / DialogueCharacter
  ├─ IWorldPauseService.Acquire(this)   （Core/Timing，timeScale=0 + SimulationRunner 暂停）
  ├─ IInputService.DisableMap("Gameplay")（UI 图照旧，EventSystem 靠它）
  ├─ DialogueRules.Start(content) → DialogueController.PresentAsync(conditions, policy, ct)
  │       Controller：TMP 打字、立绘加载、DialoguePlaybackPolicy（三连点 / 倍速 / 自动 / 跳过）、历史面板
  │       View：只显示与抛事件
  └─ 结束：释放暂停、恢复 Gameplay 图、发布 DialogueEndedEvent，返回 DialogueResult
```

依赖方向：`Game.Dialogue → Game.Core`、`Game.Dialogue → Game.Narrative`（只用 `EncounterContext` / `NarrativeCondition`）。Core 新增的暂停服务不含任何对话名词。

## 3. 契约（并行对齐点）

### 3.1 内容模型（`DialogueContent.cs`，改）

- `public const int SlotCount = 2;`、`public enum PortraitSlot { Left = 0, Right = 1 }`；校验 `Slot` 只允许 0 / 1；`DialogueRules.Restore` 的槽位校验同步改为两槽。
- 节点字段不变（`SpeakerId / SpeakerName / Text / Next / Outcome / Blocking / Portraits / Choices`）。「说话者在哪一侧」不进模型：内容适配层把 `side` 翻译成 `Portraits = [{Slot=side, Action=Show, CharacterId=speaker, ExpressionId=expr}]`，`clearOther` 翻译成另一槽 `Clear`。

### 3.2 规则（`DialogueRules.cs`，改）

```csharp
// 同步快进：Preparing/Typing/AwaitAdvance 的 Line 逐句「补全 + 记历史 + 标已读 + 进下一句」，
// 停在 AwaitChoice / Completed / Closed；返回跳过的句数；超过 4096 句抛 InvalidOperationException（内容环路）。
public int Skip(long generation, Func<DialogueContent.Node, string> resolveSpeaker);
public event Action<DialogueContent.Choice> OnChoiceSelected;   // Apply 接受 Choose 后触发
```

- Preparing 阶段的句子不需要 TMP 字数即可被 Skip：历史记 `node.Text` 与解析后的说话者名。
- 删除 `CanSkip`（已读快进被跳过取代）；`wasReadOnEntry` / 已读键照旧写入，`DialogueReadData` 保留。
- 现有三条测试保留语义；`Skip_WhenFirstReadCompletes_...` 改为断言已读键写入。

### 3.3 表现策略（新，纯 C#，`DialoguePlaybackSettings.cs` + `DialoguePlaybackPolicy.cs`）

```csharp
public readonly struct DialoguePlaybackSettings
{
    public DialoguePlaybackSettings(float charactersPerSecond, float[] speedSteps, int revealTapCount, float tapWindowSeconds, float autoAdvanceSeconds);
    // 校验：cps > 0；steps 非空且全 > 0；tapCount >= 1；window > 0；auto >= 0
}
public sealed class DialoguePlaybackPolicy
{
    public enum TapOutcome { None, Reveal, Advance }
    public DialoguePlaybackPolicy(in DialoguePlaybackSettings settings);
    public int SpeedIndex { get; }  public float Speed { get; }          // steps[SpeedIndex]
    public bool AutoPlay { get; }   public bool Skipping { get; }
    public float CharactersPerSecond { get; }   // cps * Speed
    public float AutoAdvanceDelay { get; }      // autoAdvanceSeconds / Speed
    public void ResetForDialogue();             // 速度回 0 档、自动关、跳过关、点击计数清零
    public void CycleSpeed();                   // 0→1→2→0
    public void ToggleAuto();
    public void BeginSkip();                    // 置 Skipping=true，直到 ResetForDialogue
    public void OnNodeChanged();                // 清点击计数与自动计时
    public TapOutcome RegisterTap(float unscaledNow, DialogueSaveData.Phase phase);
    // Typing：窗口内累计到 revealTapCount 次 → Reveal（计数清零）；不足 → None；超窗从 1 重新计
    // AwaitAdvance → Advance（计数清零）；其它阶段 → None
    public bool TickAuto(float unscaledDelta, DialogueSaveData.Phase phase);
    // AutoPlay 且 AwaitAdvance 时累计，达到 AutoAdvanceDelay 返回 true 并清零；其它阶段清零返回 false
}
```

`DialogueConfig`（SO，改）：新增 `speedSteps = {1,2,4}`、`revealTapCount = 3`、`tapWindowSeconds = 0.5`、`autoAdvanceSeconds = 1.5`；`ToPlaybackSettings()`；删除 `skipInterval`；其余字段不动。

### 3.4 结果与事件（新，各自一个文件，`readonly struct`）

```csharp
public readonly struct DialogueResult { int DialogueId; string Outcome; bool Skipped; }
public readonly struct DialogueStartedEvent { int DialogueId; }
public readonly struct DialogueChoiceSelectedEvent { int DialogueId; string NodeId; string ChoiceId; }
public readonly struct DialogueEndedEvent { int DialogueId; string Outcome; bool Skipped; }
```

MessagePipe 的 broker 注册需要根作用域的 `MessagePipeOptions`；`GameplayInstaller.Install` 里拿不到时，`DialogueService` 改用 `event Action<T>` 暴露同名事件（`OnStarted / OnChoiceSelected / OnEnded`），并在文件头写明原因。二选一，不要两套。

### 3.5 内容表（Luban，`Tables/Defines/dialogue.xml` + `Tables/Data/dialogue/*.json` + `Tables/Data/dialogue_character.json`）

- schema 用 XML 定义（`luban.conf` 的 `Defines` 目录已被扫描），**不改** `__tables__.xlsx` / `__beans__.xlsx`。
- `TbDialogue`：主键 `id:int`；bean `Dialogue { id, entry:string, nodes:list,Node }`；`input` 指向 `Data/dialogue` 目录，一棵树一个 JSON 文件（文件名 = id）。
- `Node { id, revision:int(默认 1), kind:NodeKind, speaker:string(角色 id，可空=旁白), speakerName:string(可空，覆盖显示名), side:PortraitSide, expression:string(可空=角色默认), clearOther:bool, text, next, outcome, blocking:bool(默认 true), choices:list,Choice }`
- `Choice { id, text, next, outcome, hideWhenUnavailable:bool, unavailableReason, anyOf:list,ConditionGroup }`；`ConditionGroup { all:list,Condition }`；`Condition { fact:ConditionFact, key, expected:bool }`；`ConditionFact` 与 `EncounterContext.Fact` 同名同序。
- `TbDialogueCharacter`：主键 `id:string`；`Character { id, displayName, defaultExpression, expressions:list,Expression }`；`Expression { id, sprite:string }`（`sprite` = Addressables 地址）。
- 生成：`powershell -ExecutionPolicy Bypass -File scripts/gen-tables.ps1`（Luban 已就位于 `Tools/Luban/`）。生成物进 git；行尾由 `.gitattributes` 归一化。

验证内容（约定好的 ID，Showcase 与资产按此接）：

| 项 | 值 |
| --- | --- |
| 对话树 | `1001`（老者 ↔ 旅人，3 句 → 选项 3 个（第 3 个需剧情标记 `knows_elder`、不满足隐藏）→ 各 1 句 → End，Outcome `Accepted` / `Refused`）；`1002`（2 句独白 → End，Outcome `Done`） |
| 角色 | `elder`（显示名「老者」，表情 `default` / `angry`）、`traveler`（「旅人」，`default` / `smile`） |
| 立绘地址 | `Dialogue/Portrait_elder_default`、`Dialogue/Portrait_elder_angry`、`Dialogue/Portrait_traveler_default`、`Dialogue/Portrait_traveler_smile`；文件 `Assets/_Project/Art/Sprites/Dialogue/<同名>.png` |

`DialogueCatalog`（新，Runtime）：构造注入 `IConfigService`；首次访问把表翻译成 `DialogueContent` / `DialogueCharacter` 并缓存；`bool TryGet(int id, out DialogueContent content)`、`IReadOnlyList<DialogueCharacter> Characters`。翻译规则见 3.1。EditMode 内容检查测试直接读 `Assets/_Project/Data/Config/*.bytes` 构造生成的表类，断言每棵树可构造、每个角色每个表情地址非空。

### 3.6 世界暂停（Core，新，`Assets/_Project/Scripts/Core/Timing/`）

```csharp
namespace Game.Core.Timing
public interface IWorldPauseService { bool IsPaused { get; } IDisposable Acquire(object owner); }
public sealed class WorldPauseService : IWorldPauseService, IDisposable   // 构造注入 SimulationRunner
```

- 持有者集合从 0 → 1：记下当前 `Time.timeScale`，置 0，`runner.SetPaused(this, true)`；1 → 0：恢复 timeScale 与 `SetPaused(this, false)`。
- 同一 owner 重复 Acquire 返回同一令牌；令牌 Dispose 幂等；服务 Dispose 释放全部。
- 在 `GameLifetimeScope` 注册为 `IWorldPauseService`；`docs/architecture.md` 5.8 补一行契约。
- EditMode 测试：引用计数、timeScale 保存恢复、幂等；TearDown 还原 timeScale。

### 3.7 对话服务与控制器（Runtime）

```csharp
public interface IDialogueConditionSource { EncounterContext Snapshot(string targetId); }
public sealed class DefaultDialogueConditionSource : IDialogueConditionSource   // 全 true、无标记；Narrative 接线后替换
public sealed class DialogueService
{
    public bool IsRunning { get; }
    public UniTask<DialogueResult> PlayAsync(int dialogueId, CancellationToken ct = default);
    // 未知 id → ArgumentException；进行中再调 → InvalidOperationException；ct 取消 → rules.Cancel() 并抛 OperationCanceledException
}
```

`DialogueController.PresentAsync(IDialogueConditionSource, DialoguePlaybackPolicy, CancellationToken)`：

- 每帧：Typing 按 `policy.CharactersPerSecond * clock.UnscaledDeltaTime` 推进；`policy.TickAuto` 为 true 时提交 Advance；`policy.Skipping` 且处于 Line 阶段时调 `rules.Skip` 后继续循环；`view.SetControls(policy.AutoPlay, policy.Speed, policy.Skipping)`。
- `view.OnTap` → `policy.RegisterTap(clock.UnscaledTime, rules.Phase)`，Reveal / Advance 都提交 Advance 意图（规则自己区分补全与推进）。
- `OnAuto / OnSpeed / OnSkip` 分别调策略；历史面板逻辑不变；删除已读快进路径。
- Visit 变化时 `policy.OnNodeChanged()`；`PrepareAsync` 沿用（两槽）。
- 输入图切换、暂停持有都在 `DialogueService`，Controller 不碰。

### 3.8 View（`DialogueView.cs`，改）

Inspector 引用：`speaker`、`body`（TMP，富文本）、`portraits`（长度 2：左、右）、`tapArea`（全屏透明 Button）、`history`（LOG）、`auto`、`speed`、`skip`（三个 Button）、`autoLabel`、`speedLabel`（TMP）、`choiceRoot`、`choiceTemplate`。
事件：`OnIntent`（选项）、`OnTap`、`OnHistory`、`OnAuto`、`OnSpeed`、`OnSkip`。
方法：`SetLine`、`SetVisible`、`SetPortrait(slot, sprite, speaking)`、`SetControls(bool auto, float speed, bool skipping)`（标签「自动」/「自动中」、`x1`…）、`SetInput(enabled)`、`ClearChoices / AddChoice`。
层：`UILayer.Popup`，`IsFullScreen = false`。

### 3.9 可交互对象（新）

```csharp
public sealed class DialogueInteractable : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] int dialogueId;  [SerializeField] float interactRadius;  [SerializeField] Transform actor;
    public int DialogueId { get; }   public bool IsBound { get; }   public bool InRange { get; }   // actor 为空或半径 <= 0 即恒 true
    public event Action<DialogueResult> OnCompleted;
    public void Bind(DialogueService service);
    public void Interact();   // 未绑定 / 进行中 / 不在范围 → 忽略并 Log.Warn；否则 PlayAsync(...).Forget，异常 Log.Error
}
public sealed class DialogueSceneBinder : IStartable, IDisposable   // 已加载场景 + SceneManager.sceneLoaded → 对该场景内所有 DialogueInteractable 调 Bind；运行时实例化的自行 Bind
```

点击路径：场景相机挂 `Physics2DRaycaster`，对象带 `Collider2D`；EventSystem 由 `UIService` 创建（UI 动作图已显式绑定，见 pitfalls）。

### 3.10 注册（`DialogueInstaller : GameplayInstaller`，新，挂 Boot 的 GameBootstrap）

注册 `DialogueConfig` 实例（缺失时 Error + 默认顶上，照 `SampleInstaller`）、`DialogueReadData`（本期内存实例）、`DialogueRules`（工厂：read、config.HistoryLimit、`ITelemetryService.Scope("dialogue")`）、`DialogueCatalog`、`DialogueController`（角色列表来自 Catalog）、`DefaultDialogueConditionSource as IDialogueConditionSource`、`DialogueService`、`RegisterEntryPoint<DialogueSceneBinder>`。

## 4. 资产与场景（全部经 Unity MCP 创建）

- `Prefabs/UI/DialogueView.prefab`（地址 `DialogueView`，UI 组）：根 `CanvasGroup`；`TapArea` 全屏透明 Image(raycast) + Button；底部 `Box`（高约 28% 屏、底部拉伸）内 `SpeakerName`、`Body`；`PortraitLeft` 锚在 Box 左上角外沿、`PortraitRight` 右上角（约 200×200）；右上角 `Controls` 横排 `AutoButton`(自动)、`SpeedButton`(x1)、`SkipButton`(跳过 ▶)；左上角 `HistoryButton`(LOG)；`ChoiceRoot`（VerticalLayoutGroup，居中在 Box 上方）+ 隐藏的 `ChoiceTemplate`。控件层级在 TapArea 之上。
- `Prefabs/UI/DialogueHistoryView.prefab`（地址 `DialogueHistoryView`）：半透明全屏 + ScrollRect + TMP + 关闭按钮。
- `Data/Dialogue/DialogueConfig.asset`。
- `Art/Sprites/Dialogue/Portrait_*.png` 4 张占位（Unity `execute_code` 画 256×256 纯色 + 简单图形并 `EncodeToPNG`），登记 Addressables 地址（UI 组）。
- `Scenes/Verify/Dialogue.unity`：`Main Camera`（正交、`Physics2DRaycaster`）、`Elder`（SpriteRenderer + BoxCollider2D + `DialogueInteractable` 1001）、`Traveler`（1002）。
- `Scenes/Boot.unity`：GameBootstrap 上加 `DialogueInstaller` 并拖 Config。改前确认 Boot.unity 在 git 里干净。

## 5. 验证清单

1. 控制台零编译错误（MCP `read_console`）。
2. EditMode：`Game.Tests.EditMode` 全量通过，用例总数比改前增加（防域未重载）。
3. `/verify-module Dialogue`：Showcase 场景 ≥ 2 条：① 交互 → 打字 → 三连点补全 → 推进 → 倍速循环 → 自动到选项 → 隐藏条件选项 → 选择 → 结束 Outcome=Accepted，全程时停检查点；② 跳过 → 停在选项 → 选择 → 结束 Skipped=true → 世界恢复。
4. `project-lint` 零违规；`code-reviewer`（sonnet）无 BLOCK。
5. `/generate-doc dialogue` 三件套；`catalog.md`、`modules.json` 登记；`gc_scan.py` 无失效引用。
6. `/review-change` 列清单，停下等审；不提交。

## 6. 风险与默认决策

- Luban JSON 目录输入的记录格式（一文件一记录 vs 列表）以实际生成结果为准；生成失败时先修 schema，不手写生成物。
- `Time.timeScale = 0` 会冻结 scaled 的协程 / 动画；Showcase 基类的等待全部是 realtime，对话表现全部用 unscaled，已核对。
- Boot 后停在 TitleState，TitleView（Panel 层）会盖住场景点击；Showcase 第 0 步用 `IUIService` 关闭它，不改 Title。
- 工作区里已有他人未提交改动（`Assets/Settings/`、`ProjectSettings/QualitySettings.asset`、字体资产），本次不碰、不暂存。

## 7. 执行中的修订（以此为准）

- 3.3 连点窗口：按**相邻两次点击间隔** ≤ `tapWindowSeconds` 计（比从首次点击起算更宽容）；非 Typing / AwaitAdvance 阶段的点击返回 None 并清零计数。
- 3.4 事件：`GameplayInstaller.Install` 拿不到根作用域的 `MessagePipeOptions`，采用 `DialogueService` 上的 C# 事件 `OnStarted / OnChoiceSelected / OnEnded`，载荷仍是三个 `readonly struct`。
- 3.5 Luban 事实（5.1.0 实测）：目录输入一文件一记录（JSON 对象）；一个文件放数组要写 `input="*@文件名.json"`；**JSON 不允许缺字段**，`revision / blocking / 空数组 / 空串` 都要显式写；schema 的 module 名为 `dialogue`，生成物在 `cfg.dialogue.*`，代码里写 `global::cfg.dialogue.` 避免与 `DialogueContent.Node` 撞名。
- 3.5 数据约定：旁白节点（speaker 为空）忽略 `clearOther`；选项节点有 speaker 时同样生成立绘指令；不同结局用不同的 End 节点。
- 3.6 注册位置在 `RegisterSimulationDriver` 之后；令牌是 `WorldPauseService.Token` 私有嵌套类（「一个文件一个类型」不含嵌套类型，与 `SimulationRunner.Mode` 惯例一致）。

## 8. 二期：场景交互表现与对话内表现（参考明日方舟 SideStory「直到大地变成一颗酸橙」）

用户给出四张参考：NPC 头顶气泡与名字、进范围后右下角「对话」按钮、跳过确认弹窗、右侧竖排胶囊选项。规则层不动，只加表现与一个焦点系统。

### 8.1 范围交互与 NPC 标记

| 组件 | 职责 |
| --- | --- |
| `DialogueInteractionActor`（MonoBehaviour 标记，挂玩家根 `player`） | 告诉焦点系统「谁是玩家」；不含逻辑 |
| `DialogueInteractionFocus`（`ITickable` 入口点，Installer 注册） | 每帧在 `DialogueSceneBinder.Bound` 里选距离 Actor 最近且 `InRange` 的一个为焦点（无分配）；对话进行中无焦点；`Current`、`event Action<DialogueInteractable> OnFocusChanged`；读 `IInputService.Actions.Gameplay.Confirm.WasPressedThisFrame()` 触发 `Current.Interact()`（触发对话不属于确定性模拟，允许读 Actions，注释写明）；打开常驻 `DialogueInteractHudView` 并按焦点显隐 |
| `DialogueInteractable` | 增 `[SerializeField] string displayName`、只读 `Focused`（由 Focus 设置）；`InRange` 用 3D 距离；保留点击路径 |
| `DialogueInteractableMarker`（MonoBehaviour，挂 NPC；取代一期临时的 `DialogueInteractableHint`） | 引用 `bubbleIdle`（灰「…」）、`bubbleFocused`（白「!」）、`nameLabel`（TMP 3D）；按 `Focused` 切换，名字只在焦点时显示；朝向相机 |
| `DialogueInteractHudView`（`UILayer.Hud`，地址同名） | 右下角方形卡片「对话」+ 图标位；`Show(name)` / `Hide()`；`OnInteract` → `Focus.Current.Interact()` |

占位资源：`Art/Sprites/Dialogue/Marker_Idle.png`、`Marker_Focus.png`（128×128 圆气泡，灰「…」/ 白「!」）；HUD 按钮用纯色 Image。`interactRadius` 建议 3.5；`actor` 字段保留但不再必填（Focus 用 Actor 标记）。

### 8.2 跳过确认

`DialogueSkipConfirmView`（Popup，地址同名）：「是否跳过剧情？」+ 确认 / 取消，事件 `OnConfirm / OnCancel`。Controller：点跳过 → 打开确认，期间与历史面板同样「覆盖中」（不打字、不自动、主面板输入关）；确认 → `policy.BeginSkip()`；取消 → 关闭恢复。Showcase 里点跳过后要再点确认。

### 8.3 选项样式与图标

- 预制体：`ChoiceRoot` 锚在右侧中部（anchor (1, 0.5)，pivot (1, 0.5)，右边距 40，宽 720），竖排、右对齐；`ChoiceTemplate` 深色胶囊（高 72，圆角占位用深色 Image，alpha 0.85），文字白色右对齐，左侧 48×48 `Icon` Image（无图标时隐藏）。
- 内容：`Choice.icon`（string，Addressables 地址，可空；JSON 必须显式写 `"icon": ""`）→ `DialogueContent.Choice.IconKey` → Catalog 透传 → Controller 用 `IAssetService` 加载，`ClearChoices` 时释放句柄 → `View.AddChoice(choice, available, Sprite icon)`。
- 占位图标：`Art/Sprites/Dialogue/ChoiceIcon_Go.png`、`ChoiceIcon_Leave.png`；1001 的 accept / refuse 各配一个，remember 留空。

### 8.4 不做

对话框手绘边框、`Dialog` 贴纸、星星装饰、背景模糊等美术资源本期仍占位，等美术给图后换 Sprite 即可；轮廓 / 手绘线动效不做。

### 8.0 前置修复：Boot 兜底相机让位（Core）

现象：Boot 常驻场景的 `Main Camera`（depth 0、纯色清屏）在附加加载的玩法场景相机（SampleScene depth -1）之上重画，玩家看到灰底，点击射线却走场景相机。仓库 HEAD 即如此，不是本轮引入。
修法：新建 `Core/Boot/FallbackCamera.cs`（MonoBehaviour，挂 Boot 的 `Main Camera`）：订阅 `SceneManager.sceneLoaded / sceneUnloaded`，任一事件后检查除自己外是否还有启用的 `Camera`（`Camera.allCameras`），有则 `camera.enabled = false`，没有则恢复。事件驱动，不每帧。禁用后 `Camera.main` 也不再返回它。Boot.unity 用 MCP 挂组件并保存。验证：Boot → 开始 → 截图能看到等距场景与 NPC。

### 8.5 无对话树 NPC 的常驻台词气泡

- `DialogueInteractable`：`dialogueId == 0` 表示没有对话树；新增 `[SerializeField] string[] bubbleLines`（常驻台词，Inspector 配置；本地化 / 进表留后续）。`CanInteract` 在「有树且已绑定」或「无树但有台词」时为真。`Interact()`：无树 → 触发 `event Action<string> OnBubbleRequested`，每次交互按顺序取下一句（循环），**不暂停世界、不切输入图、不开对话面板**；有树 → 原逻辑。
- `DialogueSpeechBubble`（MonoBehaviour + 预制体 `Prefabs/World/DialogueSpeechBubble.prefab`，世界空间 Canvas：背景 Image（占位 9 宫格白框、底部小三角）、`Name` TMP（「💬 名字」用文字「◌ 名字」占位）、`Body` TMP（自动换行，宽约 3 个单位）、`Arrow` ▼）：挂在 NPC 上，订阅所在 `DialogueInteractable.OnBubbleRequested`；显示时逐字打出（unscaled），打完显示 ▼，`holdSeconds`（默认 4）后淡出；再次交互立即换下一句；朝向相机（`CameraBillboard` 或 `LookAt`）。位置在头顶标记之上，标记显示时气泡与标记不重叠（气泡显示期间隐藏「!」标记）。
- 验证：SampleScene 加第三个 NPC `Npc_Villager`（`dialogueId=0`，两句台词），Showcase 加一条：交互 → 气泡出现且 `Time.timeScale == 1` → 再交互换句 → 超时淡出。
