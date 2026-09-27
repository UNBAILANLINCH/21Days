# PRP: 演出管线（performance-pipeline）

> 日期：2026-09-26。PRD 见 [`prd.md`](prd.md)。本文是设计定稿与各波次的契约；执行中发现设计有误，**先改这里再改代码**。
> 设计评审门（PRP 三道门之三）在本会话未经用户点头即通过——会话自主运行，用户不在线；第 9 节列出需用户事后拍板的决定。

## 1. 上下文快照

### 1.1 框架能力（docs/architecture.md，只摘本次要用的）

| 能力 | 接口 | 本次怎么用 |
| --- | --- | --- |
| 世界时停 | `IWorldPauseService.Acquire(owner)` → `IDisposable`，按 owner 计数、可嵌套 | 演出期间持令牌；对白里插播时两把令牌并存 |
| 输入图 | `IInputService.EnableMap / DisableMap(string)`，`Actions`（生成的 `GameInput`） | 关 Gameplay 图、开 Dialogue 图读 `Advance` / `Skip`；**只恢复进来前的状态**（对白里插播时 Dialogue 图本来就开着） |
| UI | `IUIService.OpenAsync<T>(arg)` / `CloseAsync` / `SetLayerVisible(UILayer, bool)`；`UIView.IsFullScreen`（Panel 全屏隐藏其下 Panel）、`CloseOnCancel`（Esc 是否可关） | `PerformanceView` 是 **Panel 层全屏、`CloseOnCancel = false`**：Esc 被 `UICancelRouter` 挡住（不会开暂停菜单）。**对白框 `DialogueView` 在 Popup 层**（不是 Panel），盖不住，所以 `HideHud` 时服务同时整层关掉 Hud 与 Popup（工程里没有别的 `SetLayerVisible` 调用方），播完两层恢复 |
| 资源 | `IAssetService.InstantiateAsync(key, parent)` / `ReleaseInstance` | 演出预制体按 Addressables 地址实例化，播完归还 |
| 存档 | `ISaveService.Get<T>()`，分区实现 `ISaveData`（`Version` / `Migrate`） | 新分区记「已播过的演出 id」 |
| 事件 | MessagePipe，`readonly struct XxxEvent`，broker 在 `GameplayInstaller.InstallEvents` 注册 | `PerformanceStartedEvent` / `PerformanceEndedEvent` |
| 埋点 | `ITelemetryService.Scope(module)` → `ITelemetryScope`，规则类只注入接口 | 模块名 `performance` |
| 注册 | 继承 `GameplayInstaller` 挂到 Boot 场景 `GameBootstrap`；`Install` 只 Register 不 Resolve | `PerformanceInstaller` |
| 时间 | 世界暂停 = `timeScale 0`，表现层用 unscaled 时间 | `PlayableDirector.timeUpdateMode = UnscaledGameTime`，LitMotion `UpdateIgnoreTimeScale` |

### 1.2 相关模块约束

- **Dialogue**（stable，`ai-docs/docs/modules/dialogue/`）：`DialogueController.PresentAsync` 每帧循环，`Visit` 变化时 `PrepareAsync` 摆台词与立绘；
  时停与输入图只在 `DialogueService`；`DialogueContent.Node` 由 `DialogueCatalog` 从 Luban 生成物翻译；JSON **不允许缺字段**；
  `Tables/Defines/dialogue.xml` 是 schema，生成命令 `powershell -ExecutionPolicy Bypass -File scripts/gen-tables.ps1`；
  Dialogue 已依赖 Narrative，**Performance 不得反向依赖 Dialogue**（本次是 Dialogue → Performance）。
- **Loot**（最新落地的模块，`Runtime/Loot/`）：`LootInstaller` 工厂式注册 + `AsSelf().As<IGameService>()`、`LootSaveData` 分区、
  `LootSceneBinder` 场景绑定——新模块照这个形状写。
- **IsometricExploration**：探索 HUD 走 `IHudVisibility` 沉浸；本次用 `IUIService.SetLayerVisible(UILayer.Hud, false)` 整层关，
  不碰沉浸状态（沉浸是玩家意图，演出是系统行为，两者语义不同）。
- **Core 不认识玩法名词**：演出相关类型全在 `Game.Performance`；Core 只提供暂停 / UI / 资源 / 存档。

### 1.3 必须规避的 pitfalls（`ai-docs/pitfalls.md`）

- 两个会话共用一个工作区：**提交按文件挑，不整份暂存**；工作区现有 111 处他人未提交改动，一律不碰。
- subagent 在 Play 里冒烟可能卡死被看门狗杀：Play 验证限时、超时先 `stop` 再汇报。
- `UnityEngine.Object` 判空只用 `== null` / `!= null`。
- Showcase 里跨帧等待用 `Check(timeout)` / `WaitUntil`，不靠 `Step` 的 hold；多条用例加载 Boot 要在 TearDown 销毁 `GameLifetimeScope`。
- 新建 `.cs` 后要让 Unity 刷新生成 `.meta`（编辑器已开：`refresh_unity`）。
- Luban JSON 不允许缺字段：给 `Node` 加字段后**所有**现有对话树 JSON 都要补上该字段。

### 1.4 适用规则

`project-root.md`（目录、asmdef 方向、复用→扩展→新建）、`csharp-code.md`（无 public 字段、每帧路径无 Find / Log、协程句柄）、
`unity-assets.md`（场景 / 预制体走 MCP、SO 只读、asmdef 引用方向）、`unity-tests.md`（EditMode 优先）、`module-verify.md`（Showcase 写法）、
`model-routing.md`（派单档位）。

## 2. 架构决策

### 2.1 模块归属与程序集

| 东西 | 位置 | 命名空间 / asmdef |
| --- | --- | --- |
| 运行时管线 | `Assets/_Project/Scripts/Runtime/Performance/` | `Game.Performance`，`Game.Runtime` |
| 自定义时间轴轨道 / 片段 / 标记 | `Assets/_Project/Scripts/Runtime/Performance/Timeline/` | `Game.Performance.Timeline`，`Game.Runtime` |
| Live2D 演员适配 | `Assets/_Project/Scripts/Runtime/Live2D/` | `Game.Live2D`，**独立 asmdef `Game.Live2D`**（见 2.7） |
| 编辑器工具 | `Assets/_Project/Scripts/Editor/Performance/` | `Game.Editor.Performance`，`Game.Editor` |
| 配置资产 | `Assets/_Project/Data/Performance/PerformanceConfig.asset`；时间轴 `Data/Performance/Timelines/<id>.playable` | — |
| 演出预制体 | `Assets/_Project/Prefabs/Performance/<id>.prefab` | Addressables 组 **`Performance`**（新建），地址 = 预制体名 = 演出 id |
| 面板预制体 | `Assets/_Project/Prefabs/UI/PerformanceView.prefab` | UI 组，地址 `PerformanceView` |
| 验证 | `Scripts/Tests/Showcase/Performance/PerformanceShowcase.cs`、`Scenes/Verify/Performance.unity` | `Game.Tests.Showcase` |
| 测试 | `Scripts/Tests/EditMode/Performance/`、`Scripts/Tests/EditMode/Editor/Performance/`（编辑器工具的测试） | `Game.Tests.EditMode` |

asmdef 引用新增：`Game.Runtime` + `Unity.Timeline`、`Unity.RenderPipelines.Universal.Runtime`；`Game.Editor` + `Unity.Timeline`、`Unity.Timeline.Editor`；
`Game.Tests.EditMode` + `Unity.Timeline`。依赖方向不变（Runtime 不引用 Editor / Tests）。

为什么到「新建」这一步：工程里没有任何「按时间编排表现」的能力（LitMotion 是单值补间，Animator 是单对象状态机），
Dialogue 是内容驱动的推进机不是时间轴；扩展 Dialogue 做「全屏演出节点」（roadmap D3）只能覆盖对白内的场景，覆盖不了场景触发与将来的剧情控制器。

### 2.2 内容形态：一段演出 = 一个预制体（舞台）+ 一个时间轴资产

- 预制体根挂 `PerformanceStage`（策略字段 + `PlayableDirector` + 舞台相机 + 演员子物体）。时间轴的轨道绑定指向**同一预制体内**的对象，
  所以绑定在实例化后天然成立，不需要运行时再解析。
- **为什么不用 ScriptableObject 目录表**：时间轴绑定必须落在场景对象上，SO 存不了；预制体把「编排 + 演员 + 绑定」装在一起，
  正好是动画师心中「一段演出」的粒度。Addressables 组就是目录，编辑器窗口扫组列出全部。
- 演出 id 即 Addressables 地址（小写下划线，如 `perf_sample_greeting`），玩法侧只认这个字符串；
  `[PerformanceId]` 特性 + 编辑器下拉让 Inspector 里选而不是手打。

### 2.3 渲染：舞台相机以 URP Overlay 叠在当前主相机上

- 舞台内全部对象在新图层 **`Performance`**（MCP `add_layer`），舞台相机 `renderType = Overlay`、只剔除到该层、正交（Live2D 是 2D 模型）。
- 播放时服务把舞台相机加进 `Camera.main` 的 `UniversalAdditionalCameraData.cameraStack`，结束时移除；
  主相机的剔除遮罩要**排除** `Performance` 层（验证场景与 SampleScene 相机各改一次，编辑器校验会提示）。
- 取不到主相机的 URP 数据时退路：舞台相机改 Base、`depth` 高于主相机、背景纯黑，记 Warn + 埋 `camera_stack_unavailable`。
- 为什么不把舞台放主相机前面：探索场景是 3D 透视 + 雾 + SSAO，2D 模型摆进去会被雾和光照污染；也不做 RenderTexture（多一层拷贝、Live2D 半透明边缘会出问题）。
- **实测要求**：叠加在验证场景与 SampleScene 各跑一次；2D Renderer 与 Forward Renderer 都要能叠。

### 2.4 运行时类分工

| 类 | 类型 | 职责 |
| --- | --- | --- |
| `PerformanceRules` | **纯 C#** | 阶段机 `Idle → Playing ⇄ Holding → Finished`；`Start(id, policy)`、`EnterHold()`、`Confirm()`、`TickSkip(held, unscaledDelta)`（长按计时，返回是否触发）、`Complete()`、`Skip()`、`Cancel()`；`Outcome`、`SkipProgress`；重入抛 `InvalidOperationException` |
| `PerformancePolicy` | `readonly struct` | 校验后的策略快照：`Skippable`、`SkipHoldSeconds`、`PauseWorld`、`HideHud`、`Letterbox`；由 `PerformanceStage` 的序列化字段 + `PerformanceConfig` 默认值构造 |
| `PerformanceSaveData` | `ISaveData` | `PlayedIds`（`List<string>`），版本 1 |
| `PerformanceService` | 单例，`IPerformanceService` + `IGameService` | 见 2.5 流程；持时停令牌、输入图、相机叠加、面板开关、实例生命周期、埋点、事件广播 |
| `PerformanceStage` | MonoBehaviour（预制体根），`INotificationReceiver` | 持 `PlayableDirector` 与舞台相机；`Play / Resume / Stop`；收到 `HoldMarker` 通知 → `director.Pause()` + `OnHold`；`director.stopped` → `OnFinished`；`SetSubtitleSink(IPerformanceSubtitleSink)` 供字幕轨道找到面板 |
| `PerformanceActor` | 抽象 MonoBehaviour | `ExpressionNames`（校验用）、`SetExpression(string)`、`SetVisible(bool)` |
| `SpritePerformanceActor` | `PerformanceActor` | 占位演员：`SpriteRenderer` + 「表情名 → Sprite」列表 |
| `PerformanceView` | `UIView`（Panel 全屏，`CloseOnCancel = false`），实现 `IPerformanceSubtitleSink` | 黑边、字幕（说话者 + 正文）、「▼」停顿提示、跳过提示 + 长按进度环、进出场黑场；不注入服务，全部由服务调方法 |
| `PerformanceTrigger` | MonoBehaviour | 场景挂载点：`performanceId`、`mode ∈ { OnEnter, OnSceneStart }`、`once`；`OnTriggerEnter` / `OnTriggerEnter2D` 只认根上有 `PerformanceTriggerActor` 的对象 |
| `PerformanceTriggerActor` | MonoBehaviour（空标记） | 挂在玩家根上，一个场景一个（同 `DialogueInteractionActor` 的做法，但不依赖 Dialogue） |
| `PerformanceSceneBinder` | 入口点 `IStartable` | 启动与 `sceneLoaded` 时扫描 `PerformanceTrigger`（含未激活）并 `Bind(service)`；`OnSceneStart` 的在 Boot 完成后触发 |
| `PerformanceInstaller` | `GameplayInstaller` | 事件 broker、配置、服务（工厂式注入 `ITelemetryScope`）、Binder |
| `PerformanceConfig` | ScriptableObject | `skipHoldSeconds`（1）、`letterboxHeight`（90 px）、`fadeSeconds`（0.25）、`holdPromptText`（▼）、`skipHintFormat`（「按住 {0} 跳过」）、默认策略三开关 |

事件：`PerformanceStartedEvent(Id)`、`PerformanceEndedEvent(Id, Outcome)`；结果 `PerformanceResult(Id, Outcome, DurationSeconds)`；
`PerformanceOutcome { Completed, Skipped, Cancelled, Failed }`。

### 2.5 `PerformanceService.PlayAsync(id, ct)` 流程

```text
IsRunning → 抛 InvalidOperationException（同 DialogueService），埋 play_rejected(busy)
rules.Start(id) → 埋 play_requested
instance = assets.InstantiateAsync(id, root)        失败 → Failed，埋 load_failed，抛出
stage = instance.GetComponent<PerformanceStage>()  缺 → Failed
policy = stage.BuildPolicy(config)
try
  记录 Gameplay / Dialogue 图进来前的状态；DisableMap(Gameplay)；EnableMap(Dialogue)
  policy.PauseWorld → pause = worldPause.Acquire(this)
  view = ui.OpenAsync<PerformanceView>(policy)；policy.HideHud → ui.SetLayerVisible(Hud, false) + ui.SetLayerVisible(Popup, false)（对白框在 Popup 层）
  相机叠加（2.3）；stage.SetSubtitleSink(view)；订阅 stage.OnHold / OnFinished
  发布 PerformanceStartedEvent；埋 started；stage.Play()
  每帧（unscaled）：
    Holding 且 Dialogue.Advance 本帧按下 → rules.Confirm() → stage.Resume()，view.SetHoldPromptVisible(false)
    policy.Skippable → rules.TickSkip(Dialogue.Skip.IsPressed(), dt) → view.SetSkipProgress；触发 → rules.Skip() → stage.Stop()
    OnHold → rules.EnterHold() → view.SetHoldPromptVisible(true)
    OnFinished（自然结束）→ rules.Complete()
  直到 rules.Phase == Finished
finally
  退订；关面板；Hud / Popup 层恢复；相机移除；输入图**恢复到进来前**；pause?.Dispose()；assets.ReleaseInstance(instance)
  Outcome ∈ {Completed, Skipped} → save.Get<PerformanceSaveData>().PlayedIds 追加（去重）
  发布 PerformanceEndedEvent；埋 ended(outcome, duration)
```

- 读动作的代码加 `// lint-ok: 演出表现层读动作，不进确定性模拟`（同 `DialogueKeyboardInput`）。
- 输入图常量写在本模块（`private const string InputMap = "Dialogue"`），不引用 `DialogueService.InputMap`——避免 Performance → Dialogue。
  复用 Dialogue 图而不是新开 `Performance` 图：`GameInput.inputactions` 正被另一会话改动，且两图的键位语义一致（确认 / 跳过）；将来要分再分。
- 对白里插播时 `DialogueController` 处于「演出中」状态：不推进、不收 `Advance` / `Skip`（见 2.6）。

### 2.6 挂载点

1. **场景触发器**（`PerformanceTrigger`）：Collider `isTrigger`；`OnEnter` 只认 `PerformanceTriggerActor`；`once` 默认 true（查 `HasPlayed`）；
   服务 `IsRunning` 时跳过并埋 `trigger_skipped(busy)`。`OnSceneStart` 由 Binder 在 Boot 完成 + 场景加载后调用。
2. **对白节点前插播**：`Tables/Defines/dialogue.xml` 的 `Node` 加 `performance`（string，空 = 无，JSON 必须显式写 `""`）；
   `DialogueContent.Node.PerformanceId`；`DialogueController.PrepareAsync` 在摆台词之前：`PerformanceId` 非空且服务可用 →
   置 `Performing = true`（输入处理与自动 / 打字全部跳过，语义同 `Overlaid`）→ `await performance.PlayAsync(id, ct)` → 复位 → 继续摆台词。
   `IPerformanceService` 经 `DialogueInstaller` 用 `resolver.TryResolve` 注入（Boot 没挂 `PerformanceInstaller` 时为 null：记 Warn + 埋 `performance_unavailable`，跳过不阻塞）。
   世界时停与输入图不用额外处理：两把令牌并存；Dialogue 图本来就开着，服务只恢复进来前状态。
3. 代码触发：任何模块注入 `IPerformanceService` 直接 `await PlayAsync`。

### 2.7 Live2D 适配层（SDK 缺席也要零错误）

- `Assets/_Project/Scripts/Runtime/Live2D/Game.Live2D.asmdef`：`references` = `Game.Core`、`Game.Runtime`、**`Live2D.Cubism`**（官方 CubismUnityComponents 运行时只有这一个 asmdef，Core / Framework / Rendering 都在里面；另有 `Live2D.Cubism.Editor`）；
  `defineConstraints` = `["LIVE2D_CUBISM"]`。符号不存在 → 整个程序集不编译（已实测：未满足约束时 Unity 跳过整个 asmdef，缺失引用零错误零警告）。
- `Game.Editor` 里 `Live2DDefineSync`（`[InitializeOnLoad]`）：`AssetDatabase.FindAssets("t:AssemblyDefinitionAsset")` 里存在名为 `Live2D.Cubism` 的 asmdef →
  给当前 BuildTargetGroup 加 `LIVE2D_CUBISM`；不存在则移除。写 PlayerSettings 属于改 `ProjectSettings/`，
  只在 SDK 出现 / 消失时各写一次，文件头写明这一点（对应 project-guide 硬规则 3）。
- **实测要求**：先建 asmdef 与一个空类，`refresh_unity` + `read_console` 确认 SDK 缺席时**零错误零警告**；
  若 Unity 对未解析引用报错，退路是把目录改成 `Live2D.Optional~/`（Unity 忽略）+ 编辑器「安装适配层」按钮复制进来。
- `Live2DPerformanceActor : PerformanceActor`：表情名 → `CubismExpressionController.ExpressionsList.CubismExpressionObjects[i].name`（去掉 `.exp3` 后缀）→ `CurrentExpressionIndex`；
  `SetVisible` 切 `CubismRenderController` 的 `Opacity`。动作不经适配层：Cubism 导入器把 `.motion3.json` 变成 `AnimationClip`，
  时间轴的 **Animation 轨道**绑定模型的 `Animator` 直接播；混合要平滑再给模型挂 `CubismFadeController`。这些写进模块 guide 与开发者手册。

### 2.8 编辑器（给动画师）

| 类 | 做什么 |
| --- | --- |
| `PerformanceEditorWindow`（菜单 `21Days/演出/演出编辑器`，order 400） | 左列表：扫 `Prefabs/Performance/*.prefab` 里带 `PerformanceStage` 的，显示 id / 时长 / 轨道摘要 / 校验状态；右侧：新建（输入 id）、打开时间轴（进预制体模式并选中 Director，Timeline 窗口随之显示，拖时间线即预览）、校验全部、定位资产、试播（仅 Play 模式：像 `ReplayWindow` 那样从 `GameLifetimeScope` 解析服务） |
| `PerformanceTemplateFactory` | 一步建齐：`Data/Performance/Timelines/<id>.playable`（字幕、表情、Animation、Audio 四条轨 + 演员绑定）、`Prefabs/Performance/<id>.prefab`（`PerformanceStage` + `PlayableDirector`（`playOnAwake` 关、`UnscaledGameTime`、`extrapolationMode None`）+ `StageCamera`（Overlay、只剔 `Performance` 层）+ `Actor`（`SpritePerformanceActor`））、全部对象设 `Performance` 层、Addressables `Performance` 组登记地址（组不存在则建） |
| `PerformanceValidator` + `PerformanceIssue` | 纯校验逻辑（输入是预制体 / 时间轴，输出问题列表）：地址未登记、无时间轴、时长 0、字幕正文为空、表情轨绑定为空或表情名不在演员的 `ExpressionNames`、`HoldMarker` 在 0 秒或超出末尾、缺舞台相机 / 不是 Overlay、舞台对象不在 `Performance` 层 |
| `SubtitleClipEditor` / `ExpressionClipEditor`（`ClipEditor`）、`HoldMarkerEditor`（`MarkerEditor`） | 片段上直接画出字幕文本 / 表情名、标记画「等待输入」 |
| `PerformanceStageEditor`（自定义 Inspector） | 「打开时间轴」「校验」两个按钮 + 校验结果 |
| `PerformanceIdDrawer`（`[PerformanceId]` 的 PropertyDrawer） | 从 `Performance` 组列地址做下拉，找不到的显示红字 |
| `Live2DDefineSync` | 2.7 |

### 2.9 序列化暴露面

一律 `[SerializeField] private` + 只读属性。`PerformanceStage`：`skippable`、`pauseWorld`、`hideHud`、`letterbox`（bool，默认按 `PerformanceConfig`）、`director`、`stageCamera`；
`PerformanceTrigger`：`performanceId`（`[PerformanceId]`）、`mode`、`once`；`SpritePerformanceActor`：`target`、`expressions`（`[Serializable] ExpressionEntry { name, sprite }`）；
`PerformanceView`：`letterboxTop` / `letterboxBottom` / `fade`（`Image`）、`subtitleRoot`、`speaker` / `body`（TMP）、`holdPrompt`、`skipRoot` / `skipLabel` / `skipFill`（`Image`，Filled）。

### 2.10 埋点（模块 `performance`，四类尺子）

| 事件 | 位置 | 尺子 |
| --- | --- | --- |
| `play_requested(id)`、`play_rejected(id, reason ∈ busy / unknown_id)` | Service | 意图入口 / 失败 |
| `started(id)`、`hold_entered(id, index)`、`hold_confirmed(id)`、`skipped(id, at)`、`ended(id, outcome, duration)` | Rules（注入 `ITelemetryScope`） | 状态迁移 |
| `load_failed(id, error)`、`stage_missing(id)`、`camera_stack_unavailable(id)` | Service | 失败分支 |
| `trigger_fired(id, mode)`、`trigger_skipped(id, reason ∈ busy / played)` | Trigger / Binder | 意图入口 |
| `performance_unavailable(node)`（模块 `dialogue`） | DialogueController | 失败分支 |

不埋每帧事件（`TickSkip` 不埋）。

## 3. 验证清单

- [ ] 控制台零编译错误；`project-lint` 零违规；`invariants.py` 通过。
- [ ] EditMode：`PerformanceRulesTests`（阶段、停顿、长按、重入、取消）、`PerformancePolicyTests`（非法值）、`PerformanceSaveDataTests`（去重）、
  `PerformanceTriggerRulesTests`（once / busy / played 判定）、`PerformanceValidatorTests`、`PerformanceTemplateFactoryTests`（临时目录建后删）、
  `DialogueCatalogTests` 补 `performance` 字段翻译。
- [ ] Showcase 4 条对应 PRD A2–A5，`/verify-module Performance` PASS。
- [ ] SDK 缺席时 `Game.Live2D` 不编译且零错误（A7）。
- [ ] code-reviewer 无 BLOCK；三件套 + 策划说明 + 手册章节 + roadmap 回填（A8）。

## 4. 风险 / 回滚

| 风险 | 应对 |
| --- | --- |
| URP 相机叠加在 2D Renderer 上不生效 | 2.3 的 Base 相机退路；实测两种渲染器 |
| 未解析 asmdef 引用在 SDK 缺席时报错 | 2.7 的 `~` 目录退路 |
| `GameInput.inputactions` 被另一会话改动 | 本次不改它（复用 Dialogue 图） |
| 对白 JSON 加字段漏改某棵树 | 生成器会报错；任务里点名 1001 / 1002 / 新 1003 |
| 舞台相机与 Boot `FallbackCamera` 叠加 | 只在玩法场景播；验证场景自带主相机 |
| 回滚 | 改动仅在工作区：`git checkout -- <路径>`；新建资产 `git clean` 前先按文件挑；`Performance` 层与 Addressables 组用 MCP 删回 |

## 5. 需用户事后拍板

见 `prd.md` 待确认问题 Q1–Q6；另加：是否把示例演出挂进 SampleScene（本次只挂验证场景，不动 SampleScene——它正被两个会话改）。
