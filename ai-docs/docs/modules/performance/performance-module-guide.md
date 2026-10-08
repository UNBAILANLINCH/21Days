---
type: module-guide
module: performance
layer: runtime
maturity: stable
---

# Performance 模块指南

> 改 `Assets/_Project/Scripts/Runtime/Performance/`、`Editor/Performance/` 之前读这份。
> 对外怎么调看 [`performance-external-api.md`](performance-external-api.md)，要加东西看
> [`performance-extension-guide.md`](performance-extension-guide.md)。
> 设计定稿见 [`PRP/performance-pipeline/prp.md`](../../../../PRP/performance-pipeline/prp.md) 第 2 节；与源码不符时以源码为准。

## 职责边界

**做**：按 Addressables 地址（演出 id）拉起一段「预制体 + 时间轴」编排的**世界舞台**演出（演员是站在场景里的序列帧小人）——
实例摆到调用方给的世界位姿、暂停世界、关 Gameplay 输入图、开对白面板（进场黑场淡出 + 字幕）、让舞台相机接管画面、
时间轴走到「等待输入」标记就停下等确认、长按跳过、
播完 / 跳过 / 取消 / 失败都按「进来前的状态」逐项恢复并广播事件；「只播一次」记存档。
三类挂载点：场景触发区（进入即播）、场景加载即播、对白节点前插播。
编辑器给动画师：新建演出（一步建齐世界舞台壳：预制体 + 时间轴 + 登记地址）、打开时间轴拖动预览、校验缺项、Play 模式试播。

**不做**（明确留给人做或后续）：

| 不做 | 归属 |
| --- | --- |
| 全屏立绘叠加舞台（含上下黑边）、Live2D 模型 | 2026-09-28 用户定：整条路线下架 / 废弃，代码已删；演出只保留世界舞台 + 序列帧小人 |
| 相机运镜系统、镜头语言预设 | 用时间轴自带 Animation 轨即可满足首版 |
| 配音、口型、语音 | 设计支柱明确不做配音 |
| 演出中的分支选项、多段演出并行、演出内嵌对白 | 未来若需要另设计 |
| 演出中途存档 / 从中途恢复 | 演出期间不存档，重进从头播 |
| 手机端触屏专属操作 | PC 优先，移植阶段再补（`pc-first` 决定） |

## 运行时类分工

| 类 | 是什么 | 谁持有 / 谁调 |
| --- | --- | --- |
| `PerformanceRules` | **纯 C#** 阶段机：`Idle → Playing ⇄ Holding → Finished`；停顿确认、长按跳过计时、结果归类、埋点（`PerformanceRules.cs:19`）；`AutoPlay` + `ToggleAuto()`（只在 Playing/Holding 生效，切换即清零计时，每段 `Start` 复位为关，埋 `auto_toggled`）+ `TickAuto(dt, typing)`（只在 Holding、自动开、不在打字时累计，满 `PerformancePolicy.AutoAdvanceSeconds` 返回 true 并清零） | `PerformanceService` 持有单例，`Start/Tick/EnterHold/Confirm/TickSkip/ToggleAuto/TickAuto/Complete/Skip/Cancel/Fail` |
| `PerformancePolicy` | `readonly struct`：校验后的播放策略快照（可跳过 / 长按秒数 / 时停 / 藏 HUD / `AutoAdvanceSeconds` 自动继续间隔，默认常量 `DefaultAutoAdvanceSeconds = 1.5f`），构造即校验（`autoAdvanceSeconds` 须为不小于 0 的有限数） | 由 `PerformanceStage.BuildPolicy(config)` 产出 |
| `PerformanceSaveData` | 存档分区：`PlayedIds`，版本 1 | `ISaveService.Get<PerformanceSaveData>()` 产出，`HasPlayed`/`MarkPlayed` |
| `PerformanceService` | **对外门面** + `IGameService`：见「数据流」；持时停令牌、输入图、舞台相机接管、面板、埋点、事件（`PerformanceService.cs:52`）；播放循环每帧 `view.TickTyping(dt)`；`HandlePlayerAdvance`（面板点击与 Advance 键共用）打字中登记连点（Core 通用 `TapRevealCounter`，满 `PerformanceConfig.RevealTapCount` 次且相邻间隔 ≤ `TapWindowSeconds` 才 `view.CompleteTyping()`，与对白三连点同一规则）、否则等同 `Confirm()`；台词记录（LOG，History 键 / 「LOG」按钮）开关 `Core.TranscriptView`（Top 层），开着时冻结推进 / 自动 / 跳过、`Confirm()` 请求被丢弃，见下「LOG 与自动」；「自动」（Auto 键 / 「自动」按钮）切换 `rules.AutoPlay` | 根作用域单例；其余模块注入 `IPerformanceService` |
| `PerformanceStage` | 预制体根组件：持 `PlayableDirector` 与舞台相机（透视 URP Base）、演员名单 `cast`（说话者 → 头像 + 站位，`TryGetAvatar(speaker, out avatar, out side)`）；`Play/Resume/Pause/Stop`（`Pause` 是服务开 LOG 台词记录时调的新方法：只停导演，不发 `OnHold`、不改规则阶段、不出 ▼，与 `HoldMarker` 停顿的自动暂停是两回事）；收 `HoldMarker` 通知 → 暂停并 `OnHold`；`director.stopped` → `OnFinished` | 演出预制体根；服务播放时调用 |
| `PerformanceCastEntry` / `PerformancePlacement` | 演员名单一行（`speaker`、`avatar`、`side`，3 参构造 `(speaker, avatar, side)`）/ 世界摆放值（`None` = 不摆） | 舞台字段 / `PlayAsync` 摆放重载 |
| `PerformanceAvatarSide` | 枚举 `{ Left, Right }`：决定该说话者的头像出现在对白面板左槽还是右槽 | `PerformanceCastEntry.Side`（Inspector 按演员站位配，默认 `Left`）；`TryGetAvatar` 未命中时 `side = Left` |
| `PerformanceView` | `UIView`（**Panel 层全屏**，`CloseOnCancel = false`），实现 `IPerformanceSubtitleSink`：字幕逐字揭示 / ▼（打完才出现）/ 跳过进度环 / 进场黑场，全 LitMotion unscaled；全屏透明 `tapArea` 被点抛 `OnTap`（`PerformanceView.cs:28`）；`IsTyping`（当前句是否还在逐字揭示）、`TickTyping(unscaledDelta)`（服务每帧推进）、`CompleteTyping()`（整句补全）三个成员配合服务驱动打字，见下「字幕逐字显示」；左上「LOG」（`historyButton`）、右上「自动」（`autoButton` + `autoLabel`/`autoLabelShadow`，`SetAuto(bool)` 刷新「自动」/「自动中」）与跳过按住检测（`skipHold: UIPointerHold`，`SkipPointerHeld` 供服务读）三个控件，点击只抛 `OnAuto` / `OnHistory` | `IUIService` 按地址 `PerformanceView` 实例化；服务订阅 `OnTap`/`OnAuto`/`OnHistory`/`OnSubtitleShown`，每帧调 `TickTyping` |
| `PerformanceViewArgs` | 打开面板的参数快照（策略、跳过提示文案、进场黑场时长、停顿提示符、字幕逐字三参数 `CharactersPerSecond`/`PunctuationPauseSeconds`/`PunctuationChars`、可选 `AutoHint`/`HistoryHint` 键位小字） | 服务组装后传给 `ui.OpenAsync<PerformanceView>` |
| `PerformanceTrigger` | 场景挂载点：`performanceId`（`[PerformanceId]`）、`mode`（`OnEnter`/`OnSceneStart`）、`once`、`anchor`（演出摆放锚点，空 = 不摆）、`hideActorVisual`（默认关；演出期间藏触发者的 Renderer 与 Canvas）、`hiddenDuringPlay`（演出期间要藏的场景物体根：NPC、巡逻怪、任务 / 物资标记）；`OnTriggerEnter(2D)` 只认带 `PerformanceTriggerActor` 的对象 | 场景物体；服务由 `PerformanceSceneBinder` 注入 |
| `PerformanceTriggerActor` | 空标记，挂玩家根（同 `InteractionActor` 的做法，但不依赖 Interaction） | 场景玩家物体 |
| `PerformanceTriggerRules` | `HideSceneCharacters()` 为服务统一收集角色；静态判定：`ShouldFire(once, hasPlayed, serviceRunning, out reason)`，已播过优先于忙碌；`HideVisuals(roots)` / `RestoreVisuals(snapshot)` 成对隐藏 / 恢复一批根下的 Renderer 与 Canvas（组件去重；只切 enabled 不 SetActive）；底层 `HideRenderers` / `HideBehaviours` 与对应 Restore | `PerformanceService.PlayAsync` 对全部入口（直接播放、对白插播、场景触发）统一调用；`PerformanceTrigger.TryFire` 也经同一入口 |
| `PerformanceSceneBinder` | 入口点（`IStartable`）：启动与 `sceneLoaded` 扫描 `PerformanceTrigger`（含未激活）并 `Bind`；`BootCompletedEvent` 后触发 `OnSceneStart` 的（`PerformanceSceneBinder.cs:21`） | 根作用域入口点 |
| `PerformanceInstaller` | `GameplayInstaller`：事件 broker、配置、规则（工厂式注入 `ITelemetryScope`）、服务、场景绑定（`PerformanceInstaller.cs:26`） | Boot 场景 `GameBootstrap` 物体 |
| `PerformanceConfig` | SO：长按跳过秒数、进场黑场时长、停顿 / 跳过提示文案、字幕逐字三参数（`SubtitleCharactersPerSecond` 默认 35，0 = 整句直出；`SubtitlePunctuationPauseSeconds` 默认 0.12；`SubtitlePunctuationChars` 默认「，。！？…；：、,.!?」）、打字中连点补全两参数（`RevealTapCount` 默认 `DefaultRevealTapCount` = 3、`[Min(1)]`；`TapWindowSeconds` 默认 `DefaultTapWindowSeconds` = 0.5 秒、`[Min(0.05)]`，与 `DialogueConfig` 同值同下限；资产里次数 < 1 或窗口非正 / NaN 时按默认兜底）、`AutoAdvanceSeconds`（「自动」继续间隔，默认 `PerformancePolicy.DefaultAutoAdvanceSeconds` = 1.5 秒，语义同对白；资产里为负数 / NaN / 无穷时按默认兜底）、默认策略两开关 `defaultPauseWorld` / `defaultHideHud`（只进 `DefaultPolicy`，模板工厂目前没读） | `Data/Performance/PerformanceConfig.asset` |
| Timeline 子命名空间 `Game.Performance.Timeline` | 见下「时间轴轨道」 | — |

### 时间轴轨道（`Runtime/Performance/Timeline/`）

| 类 | 做什么 |
| --- | --- |
| `SubtitleTrack` / `SubtitleClip` / `SubtitleBehaviour` / `SubtitleMixerBehaviour` | 字幕轨。**无绑定**：输出端从 `PlayableDirector` 所在物体上的 `PerformanceStage.SubtitleSink` 取（所以 Director 必须和 `PerformanceStage` 挂同一物体）；混合器只在权重最大片段变化时调 `ShowSubtitle(speaker, text, avatar, side)`/`HideSubtitle`，头像与站位经 `stage.TryGetAvatar(speaker, out avatar, out side)` 取（未命中 `side = Left`） |
| `HoldMarker` | 「等待输入」标记：`Marker, INotification`，`NotificationFlags.TriggerOnce`，不设 `TriggerInEditMode`（拖时间线预览不触发） |
| `IPerformanceSubtitleSink` | 字幕轨道到 UI 的契约接口，运行时由 `PerformanceView` 实现 |

动作轨不走自定义类型：`AnimationTrack` 直接绑演员（序列帧小人）的 `Animator`。

## 数据流

```text
PerformanceService.PlayAsync(id, ct)：
  IsRunning → 抛 InvalidOperationException（同 DialogueService），埋 play_rejected(busy)
  hiddenCharacters = PerformanceTriggerRules.HideSceneCharacters()（含未激活角色，排除舞台演员）
  instance = assets.InstantiateAsync(id, DontDestroyOnLoad 根)     失败 → 埋 load_failed，抛出
  stage = instance.GetComponent<PerformanceStage>()                缺 → 埋 stage_missing，抛出
  placement.HasValue → stage.transform.SetPositionAndRotation(placement)（None = 保持预制体位姿）
  policy = stage.BuildPolicy(config)；rules.Start(id, policy)      埋 started
  记录 Gameplay / Dialogue 图进来前的状态；DisableMap(Gameplay)；EnableMap(Dialogue)
  policy.PauseWorld → worldPause.Acquire(this)
  view = ui.OpenAsync<PerformanceView>(args)；policy.HideHud → SetLayerVisible(Hud/Popup, false)
  AttachCamera（舞台相机当 Base 接管画面 / 缺主相机退路，见「世界舞台」）；stage.SetSubtitleSink(view)；订阅 OnHold / OnFinished
  发布 PerformanceStartedEvent；stage.Play()
  每帧（unscaled）：
    rules.Tick(dt) → view.TickTyping(dt)（字幕逐字与规则同一个 dt；停顿期间也推进，打到一半停下时继续打完；LOG 开着时不打字）
    finishedPending（时间轴自然停）→ rules.Complete()，跳出循环
    pendingSkip（代码 Skip()）→ rules.Skip() → 停时间轴、清进度环
    History 键 / 面板「LOG」按钮 → 开：置 logOpen、Playing 时 stage.Pause()（只停导演，不发 OnHold）→ 打开 Core `TranscriptView`（Top 层）显示本段已过字幕；关：先退订 OnDismiss 再关面板，仍是 Playing 才 stage.Resume()；Esc（UI/Cancel）与面板「关闭」同样能关
    logOpen 为真 → 本帧推进 / 自动请求清零（代码 Confirm() 被丢弃）、跳过按「未按住」计时（进度清零，不结束）；新到的 holdPending 仍记一次（避免关 LOG 时按 Playing 恢复把这个停顿跳过去）；之后 continue 到下一帧，不再走下面几步
    （每帧 TickTyping 之后：view 不在打字 → revealTaps.Reset()；换句由 OnSubtitleShown 清零，开 LOG 时也清零）
    Dialogue/Advance 按下 或 面板 TapArea 点击 → OnTap → HandlePlayerAdvance（打字中 = revealTaps.RegisterTypingTap(clock.UnscaledTime)，满次才 view.CompleteTyping()，不满次什么都不做，两种情况本次输入都消费；否则走 Confirm()）
    confirmRequested（代码 Confirm() 或 HandlePlayerAdvance 走到 Confirm()）且 Holding → rules.Confirm() → 收 ▼、Resume()
    holdPending（收到 HoldMarker 通知）→ rules.EnterHold() → 请求显示 ▼（实际显隐 = 请求显示 且 不在打字中，打完才真正出现）
    Auto 键 / 面板「自动」按钮 → rules.ToggleAuto() → view.SetAuto(rules.AutoPlay)（手动确认不关自动）；rules.TickAuto(dt, view.IsTyping) 满秒数 → 按确认处理（同 HandleConfirm，收 ▼、Resume）
    policy.Skippable → rules.TickSkip(Dialogue/Skip 按住 或 面板「跳过」被鼠标按住（view.SkipPointerHeld）, dt) → view.SetSkipProgress；满 → 结束
  直到 rules.Phase == Finished
  收尾（finally）：摘回调 → stage.Stop() → 还相机（DetachCamera）→ 关面板 → 恢复 Hud/Popup 层 → 输入图恢复到进来前 → pause.Dispose()
  Outcome ∈ {Completed, Skipped} → save.Get<PerformanceSaveData>().MarkPlayed(id)
  RestoreVisuals(hiddenCharacters)；归还实例；发布 PerformanceEndedEvent；埋 ended(outcome, duration)

挂载点 1（场景触发）：PerformanceTrigger.OnTriggerEnter(2D) 认到 PerformanceTriggerActor → TryFire()
  → PerformanceTriggerRules.ShouldFire(once, hasPlayed, service.IsRunning) → 埋 trigger_fired / trigger_skipped(reason)
  → service.PlayAsync(id).Forget()（不传本物体的销毁令牌：演出挂在 DontDestroyOnLoad 根上，触发器所在场景卸载不打断演出）

挂载点 2（场景开始）：PerformanceSceneBinder 扫描 sceneLoaded 时登记的 OnSceneStart 触发器，
  在 BootCompletedEvent 之后统一触发一次（之前已加载场景）或场景加载时触发（启动完成之后才加载的场景）

挂载点 3（对白节点前插播）：DialogueController.PrepareAsync 前，Visit 变化时先调 PerformBeforeNodeAsync（DialogueController.cs:183、367）
  → node.PerformanceId 非空 且 Phase == Preparing 且非跳过快进 → Performing = true（HandleKey 与主循环全部让位）
  → performance.PlayAsync(id, PerformancePlacement.FromTransform(锚点), ct)（DialogueController.cs:388；IPerformanceService 经 DialogueInstaller.TryResolve 注入，可为 null）
     锚点来自 DialogueService.PlayAsync(dialogueId, performanceAnchor, ct)：NPC 交互（DialogueInteractable）传 NPC 自身 Transform，
     代码拉起可自己传说话的 NPC；不传 / 传 null = None，不摆放（世界舞台演出会落在世界原点、地面以下）
  → 场景角色的隐藏 / 恢复由 PerformanceService.PlayAsync 自己统一处理（见上，本挂载点不重复藏），不含舞台替身
  → 服务缺席记 Warn + 埋 dialogue 模块的 performance_unavailable；演出抛非取消异常记 Error + 埋 performance_failed，都不阻断对白
  → 同一个 finally（完成 / 跳过 / 取消 / 异常都走）：Performing = false；比对 Generation/Visit 后继续 PrepareAsync 摆台词
  → 服务缺席 / 跳过快进中不插播，也就不经过 PlayAsync、不藏
```

## 玩家操作

- **继续 / 补全**：鼠标点击画面任意处（`PerformanceView.tapArea`）/ 空格 / 回车 / 小键盘回车 / 手柄 A（South），即 Dialogue 图 `Advance`，两者共用 `PerformanceService.HandlePlayerAdvance`——**打字中连点三下 = 整句补全**（与对白同一规则：相邻两下间隔 ≤ 0.5 秒才累计，超窗从 1 重计；次数与窗口取 `PerformanceConfig.RevealTapCount` / `TapWindowSeconds`；满次调 `view.CompleteTyping()`；不满次什么都不做；**打字中的每一下都就此消费，不当成确认、不同时继续**）；**停顿（▼）时点击 = 继续**（等同 `Confirm()`）；其余（非打字、非停顿）点击无事，不触发跳过。
- **跳过**：长按左 / 右 Ctrl 或手柄 RB（Dialogue 图 `Skip`），**或鼠标按住面板右上「跳过」文字**（`skipHold: Core.UIPointerHold`，`view.SkipPointerHeld`），两者效果等价，都要满 `SkipHoldSeconds` 才触发。
- **LOG（台词记录）**：H 键 / 手柄 LB（Dialogue 图 `History`）或点左上「LOG」按钮开关 Core 通用记录面板 `TranscriptView`（Top 层，压在演出面板之上），内容是本段演出已显示过的全部字幕；开着时也能按 Esc（UI/Cancel）或点记录面板「关闭」关掉。**开着期间**：时间轴暂停（`PerformanceStage.Pause()`，仅停导演，不触发 `OnHold`）、字幕不打字、不处理推进 / 自动键、跳过进度清零且不判定结束、**代码 `Confirm()` 请求被丢弃**（`Skip()` 不受影响）；新到的 `HoldMarker` 停顿仍会记下，关 LOG 时按当时的阶段决定是否恢复时间轴。
- **自动**：A 键 / 手柄 Y（Dialogue 图 `Auto`）或点右上「自动」按钮切换，标签在「自动」/「自动中」间刷新（`SetAuto`，打开面板时复位为「自动」）；开着时停顿处字幕打完再等 `PerformanceConfig.autoAdvanceSeconds`（默认 1.5 秒）自动继续，语义与手动确认相同（**手动确认不会关闭自动**），每段演出开始复位为关。
- 停顿提示符取 `PerformanceConfig.holdPromptText`（默认「▼」），显示在对白面板右下角；**当前句字幕逐字打完才会出现**（`SetHoldPromptVisible` 只记请求，`PerformanceView` 内部按「请求显示 且 不在打字中」门控实际显隐）；左侧「点击或按空格继续」（`HoldHint`）随 ▼ 一起显隐。
- 跳过提示：右上「跳过 ▶」是预制体固定文案，下方键位小字取 `PerformanceConfig.skipHintFormat`（默认「长按 {0}」）。

## 依赖方向

| 依赖 | 用来做什么 |
| --- | --- |
| `Game.Core.UI` / `Assets` / `Input` / `Timing` / `Save` / `Telemetry` / `Events` | 面板、实例化、输入图、世界时停、存档、埋点、`BootCompletedEvent` |
| `Game.CharacterPuppet`（叶子表现模块） | 扫描场景小人，读取 `ChibiPuppetMotion.TrackedRoot` 作为角色根，不调用角色玩法逻辑 |
| `Unity.Timeline`、`Unity.RenderPipelines.Universal.Runtime`（`Game.Runtime` 新增引用） | 时间轴播放、URP 舞台相机接管（`UniversalAdditionalCameraData`） |

**`Game.Dialogue → Game.Performance`，反向禁止**：Dialogue 经 `resolver.TryResolve<IPerformanceService>()` 拿服务并调 `PlayAsync`；
场景角色的隐藏 / 恢复由 `PerformanceService.PlayAsync` 内部统一处理（对全部入口一视同仁），Dialogue 侧不再重复实现，
Performance 不认识任何对白名词，输入图常量 `"Dialogue"` 写死在 `PerformanceService.cs:59` 而不是引用 `DialogueService.InputMap`。
`Game.Core` 不认识演出名词。`Game.Editor.Performance` 只引用 `Game.Performance` / `Game.Performance.Timeline`，不反向。

## 世界舞台（演出唯一的渲染方式）

截图对标：一排 2D 小人站在 3D 灰盒场景里、近景微俯，底部白色对白面板。2026-09-28 起演出只有这一种舞台（旧的全屏立绘舞台与
`PerformanceStageMode` 枚举已删，见「职责边界」），`PerformanceService.AttachCamera`（`PerformanceService.cs:621`）/ `DetachCamera`（`PerformanceService.cs:684`）：

| 项 | 行为 |
| --- | --- |
| 舞台相机 | **Base**、透视；深度 = 主相机 + 1；遮罩 = 主相机遮罩 \| Performance 层；清屏 / 背景色 / `volumeLayerMask` / `renderPostProcessing` / 渲染器索引从主相机拷贝；FOV、裁剪面、位姿保留预制体作者值 |
| 主相机 | **保持 enabled**（`Camera.main` 不能变空），`cullingMask` 置 0 省一遍场景渲染；收尾（完成 / 跳过 / 取消 / 异常，同一个 finally）恢复 |
| 实例位姿 | `PlayAsync(id, placement)` 实例化后 `SetPositionAndRotation`；`None` 保持预制体位姿；触发区的 `PerformanceTrigger.anchor`、对白插播时说话 NPC 的 Transform（见「数据流」挂载点 3）就是给它的 |
| 演员图层 | 任意主相机可见的层（校验器不查） |
| UI | 不受影响：UI 根画布是 Screen Space Overlay（`UIService.CreateLayer`），与相机深度无关 |

- 主相机缺失（或取到的就是舞台相机）→ 退路：舞台相机按作者参数独立渲染（遮罩补 Performance 层、深度 +10），Warn + 埋 `world_camera_fallback(id, reason)`。
- 接管时同时复制主相机的 `layerCullDistances` 与 `layerCullSpherical`，所有收尾路径恢复舞台作者值；
  0 仍表示舞台自身 Far Clip，不复制主相机裁剪面。主相机缺失的退路保留作者的分层距离。
  `PerformanceServiceWorldTests` 核对继承、跳过/取消恢复及缺主相机路径。
- **舞台相机不要打 MainCamera 标签**，否则演出期间 `Camera.main` 可能取到它（校验器 `camera_tagged_main`）。
- 服务构造函数末尾有可选参数 `Func<Camera> mainCameraProvider`（默认 `Camera.main`），测试靠它注入主相机；
  其后还有可选 `IClock clock`（默认 `LocalClock`，`PerformanceInstaller` 传容器里的 `IClock`），只给连点窗口取 `UnscaledTime`，测试注入假时钟。
- 模板工厂 `PerformanceTemplateFactory` 建的就是世界舞台壳（舞台相机与默认构图和下面的示例共用 `CreateWorldStageCamera`）；
  示例本身另有 builder（见下「示例 `perf_sample_scene_talk`」）。
- 所有入口（触发区、代码 / 编辑器试播、对白插播）由 `PerformanceService` 在加载舞台前统一隐藏场景小人，包含未激活角色，排除舞台演员。
  `PerformanceTriggerRules.HideSceneCharacters` 优先取 `ChibiPuppetMotion.TrackedRoot`，没有则取 `transform.root`，复用 `HideVisuals` / `RestoreVisuals` 处理 Renderer 与 Canvas；完成 / 跳过 / 取消 / 异常均恢复原 enabled 值。
- `hideActorVisual` 与 `hiddenDuringPlay` 保留，用于非小人触发者和角色根以外的世界物体（如物资箱标记）。现有触发器 / 对白包装层的快照可以嵌套，按各自进入前的状态恢复。

### 示例 `perf_sample_scene_talk`（村口·场景对白）

- 生成：菜单 `21Days/演出/生成示例·场景对白（世界舞台）`（`Editor/Performance/Samples/SceneTalkSampleBuilder.cs`，`Build()` 可重跑，
  预制体与时间轴原地覆盖、GUID 不变；登记 Addressables 复用 `PerformanceTemplateFactory.RegisterAddressable`；末尾跑校验器并打日志）。
- 内容：五个方舟小人（`Actors/Actor_<名字>/PuppetVisual`（`CameraBillboard` 指舞台相机）/ 嵌套 `Chibi_<名字>`，舞台上停用
  `ChibiPuppetMotion`，陈 / 斯卡蒂根 `localScale.x = -1` 朝左）；字幕轨 6 句，每句 4 s、间隔 0.3 s，片段末 0.1 s 处一个 `HoldMarker`。
- 构图：舞台相机用模板工厂的默认构图（`PerformanceTemplateFactory.DefaultCamera*`，就是在本示例上调出来的）——局部 (0, 3.9, −10.6)、
  俯角 17°、垂直 FOV 29、裁剪面 0.3 / 100；站位（builder 顶部 `Actors`）局部 x = −1.75 / −0.4 / 0.5 / 1.4 / 2.3
  （阿米娅与德克萨斯之间留空给前景锥筒 `Cone_3`）。
- 演员名单 `cast` 按站位配 `side`：阿米娅 (x=−1.75) `Left`、德克萨斯 (x=−0.4) `Left`、能天使 (x=0.5) `Right`、陈 (x=1.4) `Right`、斯卡蒂 (x=2.3) `Right`；`SceneTalkSampleBuilder.cs` 生成 cast 时按 `x < 0 → Left` 写。
- 场景接线：`SampleScene` 的 `Trigger_VillageEntrance`：`performanceId = perf_sample_scene_talk`、`once` 关、`hideActorVisual` 开、
  子物体 `StageAnchor`（世界 (10.5, 4.888, 7.0)，旋转 0）接 `anchor`、`hiddenDuringPlay = Npc_Elder / Npc_Traveler / Npc_Villager /
  enerme / Crates/Crate_A/Marker`。
- 回放：`Tests/Showcase/Performance/ScenePerformanceShowcase.cs`（`VillageEntrance_WorldStageTalkAndRestore`、
  `VillageEntrance_HoldSkipRestores`），从 Boot 标题「开始」进 SampleScene，`/verify-module Performance` 一并跑。
- 已知：进场通知卡片在 UI `Top` 层，演出只藏 Hud / Popup，进场即触发时通知会压在演出画面上（回放里先等通知放完再进触发区）。

## 对白面板（`Prefabs/UI/PerformanceView.prefab`）

参考分辨率 1920×1080、按高度匹配。节点名是 Showcase / 测试的查找键，**不要改名**：

| 节点 | 父 | 锚点 / 位置 / 尺寸 | 说明 |
| --- | --- | --- | --- |
| `SubtitleRoot` | 根 | min(0,0) max(1,0) pivot(0.5,0)；左 80 右 70 底 65 高 280 | 自身 Image 已停用，只做容器 |
| `PanelBackground` | SubtitleRoot（最底） | 全拉伸 | 9 切圆角白底 `Art/Sprites/UI/Performance/ui_perf_panel_round.png`，#FFFFFF α0.86 |
| `AvatarFrame` / `Avatar` | SubtitleRoot | 锚 (0,0.5) pivot(0,0.5)；(74,0) 232² / (80,0) 220² | 左头像组，默认隐藏；`Avatar` 保持比例 |
| `AvatarFrameRight` / `AvatarRight` | SubtitleRoot | 锚 (1,0.5) pivot(1,0.5)；(-74,0) 232² / (-80,0) 220² | 右头像组，默认隐藏；`AvatarRight` 保持比例。只显示说话者那一侧的头像 + 框，另一侧隐藏，旁白两侧都隐藏 |
| `Speaker` | SubtitleRoot | 锚 (0,1) pivot(0,1)；(340,-40) 1090×48 | 40 号粗体 #2B2B2B |
| `Body` | SubtitleRoot | 全拉伸；left 340 right 340 top 100 bottom 30 | 34 号 #333333、行距 +12、左上、换行；左右对称留边，避免说话者切换时正文跟着跳动（代价：说话者在左侧时右边留白、在右侧时左边留白） |
| `HoldPrompt` / `HoldHint` | SubtitleRoot / HoldPrompt | 锚 (0.5,0) pivot(1,0) (184,16) 40² / 在 ▼ 左侧 | 「▼」32 号 #555555（文案取 `PerformanceConfig.holdPromptText`）；「点击或按空格继续」22 号灰字固定在预制体；从右下角挪到**底部居中**，给两侧头像让位 |
| `SkipRoot` | 根 | 锚 (1,1) pivot(1,1) (-60,-50) **160×90**（原 360×90，2026-09-28 改窄；右上角不动，子节点与 `SkipFill` 屏幕位置不变） | 含 `SkipLabelShadow`（2px 深色投影）、`SkipLabel`「跳过 ▶」38 号白、`SkipHint`（键位，取 `skipHintFormat`「长按 {0}」）22 号、`SkipFill` 进度环；新挂透明 `Image`（开 raycast）与 `Core.UI.UIPointerHold`（字段 `skipHold`）：鼠标按住等同长按跳过键 |
| `AutoButton`（新增） | 根 | 锚 / pivot (1,1)，(-428,-50)，160×90 | 从 `DialogueView` 同名按钮克隆（`LabelShadow`/`Label`/`Hint`）；标签「自动」/「自动中」（`autoLabel`/`autoLabelShadow`）+ 键位小字 `autoHint` |
| `HistoryButton`（新增） | 根 | 锚 / pivot (0,1)，(60,-50)，160×90 | 从 `DialogueView` 同名按钮克隆；固定文案「LOG」+ 键位小字 `historyHint` |
| `Fade`、`TapArea` | 根 | 未改 | **`TapArea` 不再是根的最后一个子物体**：根子节点顺序现为 `Fade → SubtitleRoot → TapArea → SkipRoot → AutoButton → HistoryButton`，三个控件排在 `TapArea` 之下（否则会挡住控件点击）；2026-09-28 删掉了原来夹在 `Fade` 与 `SubtitleRoot` 之间的两条全宽黑条节点 |

实测屏幕矩形（参考分辨率 1920×1080，左下角为原点，容差 0.5 px）：LOG（`HistoryButton`）x[60,220]、自动（`AutoButton`）x[1332,1492]、倍速位空着 x[1516,1676]（演出面板没有倍速按钮）、跳过（`SkipRoot`）x[1700,1860]，y 都是 [940,1030]——与 `DialogueView.prefab` 的同名 / 同位控件完全重合，`Tests/EditMode/Dialogue/TalkPanelConsistencyTests.cs` 的 `Controls_SitAtSameScreenRect` 逐一比对。

子节点顺序：`PanelBackground` → `AvatarFrame` → `AvatarFrameRight` → 左头像（`Avatar`）→ 右头像（`AvatarRight`）→ `Speaker` → `Body` → `HoldPrompt`。

`PerformanceView` 可选字段现为 5 个：`avatar` / `avatarFrame`（左）、`avatarRight` / `avatarFrameRight`（右）、`skipHint`（本预制体全接；不接也能跑：无对应头像槽、键位写进 `skipLabel`）。显隐规则：只显示说话者那一侧的头像 + 框，另一侧隐藏，旁白两侧都隐藏；右侧字段没接线（老预制体）退回左侧。样式全在预制体，代码只填文字与显隐。

这份节点 / 锚点 / 颜色表同时是 `Prefabs/UI/DialogueView.prefab`（NPC 交互对白）的基准：两边共用节点名（`SubtitleRoot`/`PanelBackground`/`AvatarFrame`/`Speaker`/`Body`/`HoldPrompt`/`HoldHint` 等）与样式值，2026-09-28 起对白面板照本节重做。`Tests/EditMode/Dialogue/TalkPanelConsistencyTests.cs` 读两份真实预制体逐节点比对 Rect、Image、TMP 属性；改这里的样式必须同步改 `DialogueView.prefab`，否则那组测试会挂。

## 字幕逐字显示

参数在 `PerformanceConfig`：`SubtitleCharactersPerSecond`（默认 35 字/秒，unscaled；**0 = 不逐字，整句直出**）、`SubtitlePunctuationPauseSeconds`（默认 0.12 秒，标点后短停）、`SubtitlePunctuationChars`（默认「，。！？…；：、,.!?」）。三者随 `PerformanceViewArgs` 传给 `PerformanceView`，`OnOpenAsync` 里按 `charactersPerSecond > 0f` 决定建不建 `Game.Core.UI.TypingCadence`（`PerformanceView.cs:175`）——速度 ≤ 0 时 `cadence` 为 null，`ShowSubtitle` 直接整句可见。

语义：

- `ShowSubtitle` 起句：解析 TMP 富文本后的可见字符序列，`maxVisibleCharacters` 从 0 开始（`BeginTyping`，`PerformanceView.cs:282`）。
- `PerformanceService` 播放循环每帧调 `view.TickTyping(dt)`（与 `rules.Tick` 同一个 unscaled dt，**Holding 期间也 tick**——已经进入停顿但字还没打完时继续打完，不会被停顿截断）。
- 玩家点击 / 按 Advance：打字中 → 登记一次连点（`Game.Core.UI.TapRevealCounter`，对白 `DialoguePlaybackPolicy` 用的同一个类），满次（默认三下、相邻间隔 ≤ 0.5 秒）才 `CompleteTyping()` 整句补全；每一下都就此消费，不同时触发继续；非打字中 → 等同 `Confirm()`，只在 Holding 时生效（见「玩家操作」）。计数在换句（`OnSubtitleShown`）、不在打字（播放循环每帧 `TickTyping` 之后检查）、开 LOG 时清零；LOG 开着时点击不计数。
- ▼ 显隐门控：`SetHoldPromptVisible(bool)` 只记「服务要求显示」，实际显隐 = 请求显示 且 当前句已打完（`ApplyHoldPrompt`，`PerformanceView.cs:385`）；`HoldHint` 固定小字随 ▼ 一起显隐。
- 示例演出核对：`perf_sample_scene_talk` 每句字幕结尾都落了 `HoldMarker`，停顿期间即使字还没打完也会继续打完不被截断（旧叠加示例 greeting 已下架）。新增 / 改字幕节奏时留意这条（片段太短、又没有停顿会让字幕被切断）。

## LOG 与自动

`PerformanceService` 每次 `PlayAsync` 开始清空内部台词列表 `transcriptLines`；`PerformanceView.OnSubtitleShown`（每显示一句字幕就抛一次，`ShowSubtitle` 末尾 `PerformanceView.cs:254`）触发时追加一条 `TranscriptLine`。两个控件都复用 Dialogue 的 `"Dialogue"` 动作图，不新开键位：

- **LOG**：History 键（H / LB）或左上「LOG」按钮开关 Core 通用记录面板 `TranscriptView`（Top 层，见 architecture.md「UI」一节）。打开：先置 `logOpen = true`，若规则仍是 `Playing` 就先 `PerformanceStage.Pause()`（只停导演，不发 `OnHold`、不改阶段、不出 ▼，与 `HoldMarker` 停顿是两回事），再 `await ui.OpenAsync<TranscriptView>()` 并 `Show(transcriptLines, false)`。开着期间：不打字（`view.TickTyping` 跳过）、不处理 `Advance` 推进与 `Auto` 切换（本帧请求直接清零）、跳过按「未按住」计时（进度清零、不判定结束）、**代码 `Confirm()` 的请求被丢弃**；新到的 `HoldMarker` 停顿仍会 `EnterHold()` 记下（不记的话关 LOG 时按 `Playing` 恢复时间轴会把这个停顿跳过去）。关闭（再按 History / 面板「LOG」/ Esc(`UI.Cancel`) / 记录面板「关闭」都可以）：先退订 `TranscriptView.OnDismiss` 再 `CloseAsync`，仍是 `Playing` 才 `stage.Resume()`。播放循环收尾（`finally`，含正常结束 / 跳过 / 取消 / 异常）会先关掉还开着的 LOG 再关演出面板，因为它在 Top 层，不关会压在回到探索的画面上；打开 / 关闭失败只埋 `transcript_open_failed` / `transcript_close_failed` 并记 Error，演出照常收尾。
- **自动**：Auto 键（A / Y）或右上「自动」按钮切换 `PerformanceRules.ToggleAuto()`（只在 Playing/Holding 生效，切换即清零计时，埋 `auto_toggled`），按钮标签在 `SetAuto(bool)` 驱动下于「自动」/「自动中」间切换（打开面板时强制复位为「自动」）。开着时 `TickAuto(dt, view.IsTyping)` 只在 Holding、不在打字时累计，满 `PerformancePolicy.AutoAdvanceSeconds`（默认 1.5 秒，配置字段 `PerformanceConfig.autoAdvanceSeconds`）返回一次 true，服务按与手动确认相同的 `HandleConfirm` 处理（收 ▼、`stage.Resume()`）。**手动确认不会关闭自动**（与 `DialoguePlaybackPolicy` 语义一致），每段演出 `PerformanceRules.Start` 时复位为关。
- **鼠标按住跳过**：`PerformanceView.skipHold`（`Core.UI.UIPointerHold`，挂在 `SkipRoot` 上的透明 `Image` + 按住检测组件）让鼠标按住面板「跳过」与长按跳过键等价，服务读 `view.SkipPointerHeld` 与 `actions.Dialogue.Skip.IsPressed()` 取或。

## 世界时停与输入图：责任归属

复用 Dialogue 的思路（`DialogueService.cs` 同款收尾），资源全在 `PerformanceService` 一处：

| 资源 | 归属 | 为什么 |
| --- | --- | --- |
| 世界暂停令牌 | `PerformanceService.RunAsync`（`policy.PauseWorld` 时才 `Acquire`） | 会话级资源，可单段关掉（策略开关） |
| 动作图 | 复用 **Dialogue** 图（常量 `InputMap = "Dialogue"`），不新开 Performance 图 | `GameInput.inputactions` 正被另一会话改动，且确认 / 跳过键位语义与对白一致；只恢复进来前的状态，对白里插播时 Dialogue 图本来就开着 |
| Gameplay 图 | 只在进来前是开的才恢复 | 调用方本来关着（如对白插播、过场）就不擅自打开 |
| HUD / Popup 层 | `HideHud` 时两层一起关（对白框 `DialogueView` 在 Popup 层，Panel 层的演出面板盖不住它） | 开面板**之前**读 `IsLayerVisible(Hud)` / `IsLayerVisible(Popup)` 记进来前的值，`OpenAsync<PerformanceView>` 完再各自 `SetLayerVisible(false)`；收尾按读到的值恢复，不再一律恢复为可见。嵌套（对白里插播演出）：对白先藏 Hud → 演出读到 Hud=false、Popup=true，藏 Popup（对白框随之隐）→ 演出结束 Hud 仍隐、Popup 恢复、对白框重现 → 对白结束 Hud 才恢复 |
| 重入保护 | `running` 标志 + `rules` 的 `IsActive` | 同一时刻只允许一段演出（同 DialogueService） |

`PerformanceRules` / `PerformanceStage` / `PerformanceView` 一律不碰 `Time.timeScale` 或输入图，只在 `PerformanceService`。
读动作的地方都带 `// lint-ok`：演出表现层读动作不进确定性模拟，同 `DialogueKeyboardInput` 的例外。现在 `PerformanceService.cs` 共 6 处：取 `input.Actions` 引用 1 处、LOG 开关（`History` 键 / `UI.Cancel` 键）2 处、`Advance` 推进 1 处、`Auto` 切换 1 处、跳过按住判定（`Skip` 键）1 处。

## 图层 `Performance`

- 图层 **`Performance`**（第 9 槽，`TagManager.asset`）：模板工厂把新建舞台整棵树设到这一层（层不存在时在第一个空用户层建它）；
  舞台相机接管时遮罩 = 主相机遮罩 | 这一层，所以舞台物体一定拍得到。示例 `perf_sample_scene_talk` 的根在 Default 层、小人保持自身图层，也没问题。
- 主相机剔除遮罩要**排除** `Performance` 层：演出实例生成到舞台相机接管之间（面板打开的那几帧），主相机不会顺带拍到放在这一层的舞台物体。

## 编辑器工具（`Assets/_Project/Scripts/Editor/Performance/`，`Game.Editor.Performance`）

`Game.Editor.asmdef` 未加 URP Runtime 引用（避免多加一个仅编辑器用的重依赖）：涉及相机类型判断的地方
（`PerformanceTemplateFactory.CreateWorldStageCamera`、`PerformanceValidator.IsBaseCamera`）按类型名反射取
`UniversalAdditionalCameraData`，用 `SerializedObject` 读写其序列化字段 `m_CameraType`（0 = Base，`PerformanceValidator.UrpBaseValue`）。

| 类 | 做什么 |
| --- | --- |
| `PerformanceEditorWindow`（菜单 `21Days/演出/演出编辑器`） | 左栏列出 `Prefabs/Performance/*.prefab` 里带 `PerformanceStage` 的（id / 时长 / 字幕与停顿计数 / 校验状态灯）；右栏「打开时间轴」「定位资产」「校验」+ 新建演出（「创建」）+ Play 模式下「试播」（从 `GameLifetimeScope` 解析 `IPerformanceService`，同 `ReplayWindow` 的分工） |
| `PerformanceTemplateFactory`（静态） | `Create(id, options)` 一步建齐**世界舞台壳**：时间轴（字幕 / 动作 / 音效三轨，固定 8 秒；不建表情轨，动作轨不预绑）+ 预制体（`PerformanceStage`+`PlayableDirector`+`StageCamera`（`CreateWorldStageCamera`：透视、URP Base、Untagged、无 AudioListener、默认构图 `DefaultCamera*`）+ 空站位根 `Actors`，演员名单为空）+ 全树设 `Performance` 层（层不存在时在 TagManager 第一个空槽建）+ 登记 Addressables `Performance` 组（组不存在时照抄 `UI` 组 schema 新建）。id 只能小写字母/数字/下划线。`CreateWorldStageCamera` 与 `RegisterAddressable` 是 `internal`，村口示例 builder 共用 |
| `PerformanceValidator`（静态） | 纯校验：输入舞台根物体，输出 `List<PerformanceIssue>`——缺 `PerformanceStage`/`PlayableDirector`/时间轴、时长为 0、舞台相机缺失（`camera_missing`，Error）/ 不是 Base（`camera_world_not_base`）/ 正交（`camera_world_orthographic`）/ 打了 MainCamera 标签（`camera_tagged_main`）（后三条 Warning，问题码沿用旧名）、演员名单（`cast_speaker_empty`/`cast_speaker_duplicate`/`cast_avatar_missing`，Warning）、字幕正文为空、字幕说话者未登记（`subtitle_speaker_not_in_cast`，Info，旁白除外）、`HoldMarker` 落在 0 秒或末尾外、Addressables 地址未登记。不查图层与剔除遮罩（运行时从主相机拷贝） |
| `PerformanceIssue` | 一条问题：`Severity`（Error/Warning/Info，Info 不影响状态灯）、`Code`（机器可读）、`Message`（中文）、`Context`（可定位对象） |
| `PerformanceStageEditor`（`PerformanceStage` 自定义 Inspector） | 默认字段 + 「打开时间轴」「校验」按钮 + 校验结果 |
| `PerformanceIdDrawer`（`[PerformanceId]` 的 PropertyDrawer） | 从 Addressables `Performance` 组的已登记地址画下拉（含「手动输入…」）；组不存在或当前值未登记时退回文本框 + 红字「未登记」 |
| `SubtitleClipEditor`（`ClipEditor`） | 片段上直接显示「说话者：正文前 12 字」；正文为空时标红 |
| `HoldMarkerEditor`（`MarkerEditor`） | 悬停提示「等待玩家确认」，标记旁画「▼」；落在开头/末尾之外时标红 |

## 接线要求

缺任一项都**不会编译报错**，只会运行时不动或报错：

| 项 | 要求 | 缺了会怎样 |
| --- | --- | --- |
| Installer | `Boot.unity` 的 `GameBootstrap` 挂 `PerformanceInstaller`，排在 `DialogueInstaller` 之后；**Config** 拖 `Data/Performance/PerformanceConfig.asset` | 没挂：解析不到 `IPerformanceService`（对白插播记 Warn 跳过）；没拖：记 Error 用默认值顶上 |
| 面板地址 | Addressables UI 组 `PerformanceView` → `Prefabs/UI/PerformanceView.prefab`，**16** 个字段全接（`fade`/`subtitleRoot`/`speaker`/`body`/`holdPrompt`/`skipRoot`/`skipLabel`/`skipFill`/`tapArea`/`autoButton`/`autoLabel`/`autoLabelShadow`/`autoHint`/`historyButton`/`historyHint`/`skipHold`，`skipFill` 的 Image Type 须为 Filled；`tapArea` 层级在黑场 / 字幕之上、三个控件之下，其余 Graphic 关 `raycastTarget`）；可选 `avatar`/`avatarFrame`/`avatarRight`/`avatarFrameRight`/`skipHint` 五个（本预制体已接） | `ui.OpenAsync<PerformanceView>()` 找不到预制体；漏接字段 `Validate()` 逐个点名抛出；可选字段漏接只是没有对应头像 / 键位写回 `skipLabel` |
| 演出资产 | Addressables **Performance** 组：地址 = 预制体名 = 演出 id；预制体 `Prefabs/Performance/<id>.prefab`；时间轴 `Data/Performance/Timelines/<id>.playable`；两者由 `PerformanceTemplateFactory` 一步建齐 | 地址查不到：`InstantiateAsync` 抛出，埋 `load_failed` |
| 图层 | `Performance`（第 9 槽，`TagManager.asset`）；主相机剔除遮罩要**排除**它 | 层不存在：舞台相机只按主相机遮罩拍，放在这一层的舞台物体（模板壳整棵树）拍不到；主相机没排除：舞台相机接管前的几帧主相机会顺带拍到舞台物体 |
| 玩家标记 | 玩家根挂 `PerformanceTriggerActor`（一个场景一个） | 场景触发器 `OnTriggerEnter(2D)` 永远不认玩家，不会触发 |
| 触发器 | 场景物体挂 `PerformanceTrigger` + `isTrigger` 的 `Collider`/`Collider2D`（`OnEnter` 模式必须） | 缺碰撞体：进入不触发（`OnSceneStart` 模式不需要碰撞体） |
| 对白插播 | 对白节点 JSON 的 `performance` 字段非空，且 `Tables/Defines/dialogue.xml` 已生成对应字段 | 空串：不插播；服务未注册：记 Warn 埋 `performance_unavailable` 直接显示台词 |
| 入口 | 从 Boot → 标题「开始」进场景才有演出服务与场景绑定 | 直接 Play 玩法场景：触发器 `TryFire` 记 Warn「未绑定演出服务」 |

示例：回放舞台 `Assets/Scenes/SampleScene.unity`（Main Camera 已排除第 9 层剔除遮罩；场景里唯一的触发区是村口
`Trigger_VillageEntrance`，挂世界舞台示例，见上「示例 `perf_sample_scene_talk`」；对白 1003 不挂在任何 NPC 上，只由回放用代码拉起）。
示例演出只剩世界舞台的 `perf_sample_scene_talk`；旧叠加示例 greeting（模板工厂建的预制体 + 时间轴 + 入场动画）已下架，
Addressables 地址一并移除。

## 埋点（模块 `performance` + dialogue 侧两条）

| 事件 | 位置 | 尺子 |
| --- | --- | --- |
| `play_requested(id)`、`play_rejected(id, reason ∈ busy/empty_id)` | `PerformanceService` | 意图入口 / 失败 |
| `started(id, skippable)`、`hold_entered(id, index, at)`、`hold_confirmed(id, index)`、`skipped(id, at, source ∈ hold/code)`、`ended(id, outcome, duration)` | `PerformanceRules` | 状态迁移 |
| `load_failed(id, error)`、`stage_missing(id)`、`play_failed(id, error)`、`view_close_failed(id, error)`、`transcript_open_failed` / `transcript_close_failed(id, error)` | `PerformanceService` | 失败分支 |
| `world_stage_attached(id)`；W 级 `world_camera_fallback(id, reason ∈ no_main_camera/stage_is_main)` | `PerformanceService` | 状态迁移 / 失败分支 |
| `trigger_fired(id, mode)`、`trigger_skipped(id, reason ∈ busy/played)`、`trigger_play_failed(id, error)` | `PerformanceTrigger` | 意图入口 / 失败 |
| `performance_unavailable(node, performance)`（Warn）、`performance_failed(node, performance, error)`（Error） | `DialogueController`，模块名 **`dialogue`** | 失败分支 |

不埋每帧事件（`Tick` / `TickSkip` 本身不埋）。契约见 [`docs/telemetry.md`](../../../../docs/telemetry.md)。

## 测试与验证

| 类型 | 位置 | 覆盖 |
| --- | --- | --- |
| EditMode | `Tests/EditMode/Performance/PerformanceRulesTests.cs` | 阶段迁移、停顿确认、长按计时与松手归零、不可跳过、重入、取消、结果分类、计时；新增 9 条自动相关：`ToggleAuto` 活动中翻转 / 非活动恒 false 且保持关、`TickAuto` 打字中不累计 / 满秒数触发一次并重新计时 / 离开 Holding 清零 / 关闭时恒不触发 / 0 秒下一 tick 即触发、手动 `Confirm` 不关自动、`Start` 后自动复位为关 |
| EditMode | `.../PerformancePolicyTests.cs` | 非法 `SkipHoldSeconds`（0/负数/NaN/无穷）抛异常、字段透传、`default` 值无效；新增 4 条：不传自动秒数取默认值 1.5（与 `PerformancePolicy.DefaultAutoAdvanceSeconds` 一致）、合法值原样保留、非法值（负数/NaN/无穷）抛异常、`PerformanceConfig.BuildPolicy` 透传自动秒数且资产非法时按默认兜底；再新增 1 条：连点补全两参数默认值与 `DialogueConfig` 一致、资产非法值按默认兜底、合法值透传 |
| EditMode | `.../PerformanceSaveDataTests.cs` | 默认空、去重记录、大小写敏感、旧存档 `PlayedIds` 为 null 时的恢复 |
| EditMode | `.../PerformanceTriggerRulesTests.cs` | once/played/busy 判定与原因优先级（已播过优先于忙碌）；渲染器隐藏 / 恢复 |
| EditMode | `.../PerformanceStageCastTests.cs`、`PerformancePlacementTests.cs` | 头像查找（命中 / 旁白 / 未登记 / 重名取第一 / 头像为空）；摆放值语义 |
| EditMode | `.../PerformanceServiceWorldTests.cs` | 直接服务入口隐藏未列名 / 未激活角色及 Canvas、排除舞台演员、取消后恢复原显隐；舞台相机接管与收尾恢复（跳过 / 取消）、摆放生效、不传摆放保持预制体位姿、主相机缺失退路；新增「读 Hud/Popup 层状态发生在开面板之前」（读的时间点早于 `OpenAsync` 调用）、进来前隐藏 → 结束仍隐藏（对白里插播不误亮）；再新增 3 条：自动开着在停顿处满秒数后不经 `Confirm()` 自行继续、LOG 打开时暂停导演且关闭后按阶段恢复、LOG 开着时收到跳过会先关 LOG 再关演出面板；再新增 5 条 `PlayerTap_*`（真实面板 `tapArea` 点击 + 注入假时钟）：打字中点一两下不补全且停顿不被确认、窗口内第三下补全且那一下不当确认（之后单点才确认）、超窗从 1 重计、换句清零、LOG 开着不计数且开 LOG 时清零 |
| EditMode（Core） | `Tests/EditMode/Core/TapRevealCounterTests.cs`（7 条） | 连点计数本身：窗口内第三下补全并清零、间隔恰等于窗口仍算、超窗重计、`Reset`、次数 1 每下都补全、非法参数、参数透传 |
| EditMode | `.../PerformanceViewTypingTests.cs`（8 条） | EditMode 下建 Canvas 实例化真实预制体：逐字揭示按 cps 推进、0 速整句直出、标点停顿、`CompleteTyping` 立即补全（面板只管补全动作，连点计数在服务）、▼ 门控（打字中不出现、打完才出现）、`HideSubtitle`/`OnCloseAsync` 重置逐字状态 |
| EditMode | `.../PerformanceViewControlsTests.cs`（7 条） | 实例化真实预制体：打开写键位小字与初始「自动」标签、`SetAuto` 在「自动」/「自动中」间切换且投影镜像主标签、重新打开复位为「自动」、按钮点击抛 `OnAuto`/`OnHistory`（关面板后退订不再抛）、`ShowSubtitle` 抛 `OnSubtitleShown`（旁白说话者为空串）、`TapArea` 层级须在 `SkipRoot`/`AutoButton`/`HistoryButton` 之下、`SkipRoot` 挂 `UIPointerHold` 与开 raycast 的透明 `Image` 且 `SkipPointerHeld` 跟随按住状态 |
| EditMode | `.../PerformanceTriggerTests.cs` | 锚点位姿传递、默认不隐藏、隐藏触发者并在结束 / 异常后恢复；`hiddenDuringPlay` 的 Renderer 与 Canvas 隐藏、空 / 重复 / 嵌套引用、取消后恢复 |
| EditMode | `Tests/EditMode/Editor/Performance/PerformanceTemplateFactoryTests.cs`、`PerformanceValidatorTests.cs` | 模板工厂建齐世界舞台壳（临时目录，`TearDown` 删干净）：三条轨且无表情轨、舞台相机透视 / URP Base / 不打 MainCamera 标签 / 无 AudioListener、有 `Actors` 站位根、过校验器无 Error、整棵树在 `Performance` 层；校验器逐条问题码 |
| EditMode（Dialogue 侧） | `Tests/EditMode/Dialogue/DialogueCatalogTests.cs` | `performance` 字段翻译（去空白、空串 = 不插播；1003 第 2 句 = `perf_sample_scene_talk`） |
| EditMode（Dialogue 侧） | `Tests/EditMode/Dialogue/DialogueControllerTests.cs` | 插播摆放：传锚点 → 演出服务收到 `HasValue == true` 且位姿等于锚点；不传 → 收到 `None`。插播期间场景角色的显隐已改由 `PerformanceService.PlayAsync` 统一处理，不再在本类测（见上 `PerformanceServiceWorldTests`） |
| Showcase | `Tests/Showcase/Performance/PerformanceShowcase.cs`（4 条，全部用世界舞台示例 `perf_sample_scene_talk`） | `PlayById_ShowsSubtitlesAndRestoresWorld`：代码拉起并摆到村口 `Trigger_VillageEntrance.Anchor`（对白面板 / 字幕 / 时停 / 舞台落在锚点 / 停顿确认 / 结束恢复，PRD A2）；`HoldSkip_EndsEarlyWithSkippedOutcome` 长按跳过（A3）；`Trigger_PlaysOnEnter_FollowsOnceFlag` 场景触发按 Once 判定（A4）；`DialogueNode_PlaysPerformanceBeforeSecondLine` 对白 1003 经 `DialogueService.PlayAsync(1003, Npc_Elder)` 在第二句前插播，查演出实例根与 `Npc_Elder` 水平距离、高度差都 < 0.5（A5）；插播期间查 玩家、长者、旅人、村民、巡逻怪与 Yao 根下 Renderer 全部 `enabled == false`（截图「插播·场景角色已隐藏」），回到对白后查全部恢复为进入插播前的值。代码试播也检查同一批角色隐藏与结束恢复 |
| Showcase | `Tests/Showcase/Performance/ScenePerformanceShowcase.cs`（2 条） | SampleScene 村口触发世界舞台示例：头像 / 说话者逐句、舞台相机接管、玩家与 NPC（含手工名单外的 Yao）/ 巡逻怪 / 标记隐藏，播放期间时间轴推进而模拟 Tick 与玩家坐标不变，结束后 Tick 恢复、五人在画内；「面板显示第一句」前新增瞬态检查「第一句逐字显示中（0 < 已显示字数 < 总字数）」；逐句走完 / 跳过后全部恢复；`WalkIntoTrigger()` 拍 before 快照前先检查「回放环境干净：演出尚未运行、玩家存活」（见 `ai-docs/pitfalls.md`）；新增 LOG 步骤：某句停顿时点左上「LOG」打开台词记录（内容含「阿米娅：」）→ 静置 0.5 秒真实时间验证导演没走、仍停在停顿 → 点「关闭」；随后在第三句验证点右上「自动」后标签变「自动中」，不再手动确认也能自行播完 |
| 回放舞台 | 回放在 `Assets/Scenes/SampleScene.unity` 上跑 | 见「接线要求」示例 |

跑 `/unity-test EditMode Performance`；视觉验收跑 `/verify-module Performance`（编辑器须打开）。

## 已知约束 / 未做

- **表情轨已移除**：当前序列帧小人无表情素材，表情轨、片段编辑器与无实现的演员抽象一并删除。
- **演出中途不能存档 / 恢复**：`PerformanceSaveData` 只记「播完 / 跳过」这个终态，没有中途快照；异常退出（应用崩溃）会导致该演出下次重新播放。
- **对白插播固定在节点之前**：先演后说（PRD Q3 默认值），换成「之后」需要改 `DialogueController.PresentAsync` 的调用顺序。
- **演出统一隐藏全部场景小人，不可逐个配置**：舞台上没有替身的角色也一起隐藏。只收集开始时存在的角色；开始后新生成的角色不在快照内。
  没有 `TrackedRoot` 时兜底取场景顶层根；公共容器下的角色应配置自己的 `TrackedRoot`，避免一起藏掉兄弟物体。
- **没有分图**：演出复用 Dialogue 的 `"Dialogue"` 输入图，将来要给演出单独定义键位需要新开一张图并同步改 `PerformanceService.InputMap`。
- 手柄键位提示与 Dialogue 同款限制：`BuildSkipHint` 只取键盘第一条绑定，不跟随手柄。

## 禁止事项

- 不要在 `PerformanceRules` / `PerformanceStage` / `PerformanceView` 里碰 `Time.timeScale` 或输入图——只在 `PerformanceService`。
- 不要绕过 `PerformanceService.PlayAsync` 直接调 `PerformanceStage.Play()`：不会走重入保护、摆放、时停、输入图、舞台相机接管、存档与埋点。
- 不要在 `Game.Performance` 里引用对白 / 探索等玩法模块；对白只经 `IPerformanceService` 反向调用。角色隐藏仅依赖 `CharacterPuppet` 公开表现组件。
- 不要给 `PerformanceTrigger` 传本物体的销毁令牌：演出实例挂在 `DontDestroyOnLoad` 根上，触发器所在场景卸载不该打断已开始的演出。
- 不要在时间轴混合器（`SubtitleMixerBehaviour`）里每帧调用字幕面板 / 演员方法：只在权重最大片段变化时调一次。
