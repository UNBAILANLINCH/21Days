---
type: module-guide
module: session
layer: runtime
maturity: seed
---

# Session 模块指南

## 独立试玩路由开关（2026-10-12）

SessionInstaller.registerTitleRouter默认true；仅ShopPlaytest复制的启动场景设false，避免正式存档标题入口与独立试玩入口争用。
其余Session服务和broker继续注册，CurrentSlot保持0，试玩不调用NewGame/Continue，不覆盖主线存档槽。正式Boot不改此配置。

> 改 `Assets/_Project/Scripts/Runtime/Session/` 之前读这份。对外怎么调看
> [`session-external-api.md`](session-external-api.md)，要加东西看 [`session-extension-guide.md`](session-extension-guide.md)。
> 设计定稿见 [`PRP/save-session/prp.md`](../../../../PRP/save-session/prp.md)；与源码不符时以源码为准。

## 职责边界

**做**：当前用哪个槽（`GameSession.CurrentSlot`）；关键节点自动保存的触发、合并与稳定边界闸门；槽位元数据
（保存时间、游玩时长、进度描述、场景键）；新游戏 / 继续 / 删除 / 读槽摘要；标题「开始 / 继续 / 选择存档」的路由；
选槽面板；**开局操作说明的展示闸门**；游玩时长累计；退出前的最后一次保存。

**不存什么**：不持有任何玩法分区的实例（`QuestSaveData` / `LootSaveData` / `EncounterSaveData` 都是每次
`saves.Get<T>()` 现取现写）；不持久化对白已读记录（独立档案 `dialogue-read`，见下）；不持久化对白进行中 / 剧情
逐节点状态（DialogueSaveData 尚未接入）。NarrativeSaveData 已接稳定等待/结束边界，完整保存主/父阶段与标记；对白中途禁止写盘。

**谁负责**：各分区所有者自己 `Capture` / `Restore` 自己的数据，Session 只在稳定点统一喊「存」，不知道分区内部
字段；读档后各所有者自己订阅 [`SessionStartedEvent`](../../../../Assets/_Project/Scripts/Runtime/Session/SessionStartedEvent.cs) 重载运行时状态，Session 不枚举模块。

## 内部结构

| 类 | 是什么 | 谁持有 / 谁调 |
| --- | --- | --- |
| `GameSession` | 门面：槽位状态、新游戏 / 继续 / 删除 / 读槽摘要、`RequestSave` 待处理合并、`ITickable.Tick` 评估闸门并落盘、游玩时长累计 | 根作用域单例 + `IGameService` + `ITickable`（`GameSession.cs:36`） |
| `SaveGateRules` | 纯静态：`CanSave` 四条件、`Request`/`Take` 合并待处理请求 | `GameSession.Tick` 调（`SaveGateRules.cs:10`） |
| `ISessionStateSource` / `SessionStateAdapter` | 把 Quest / Dialogue / Monster / UIService 的具体依赖包成一个接口，供 `GameSession` 在 EditMode 里假实现测试 | `SessionInstaller` 注册适配器为该接口（`ISessionStateSource.cs`、`SessionStateAdapter.cs:18`） |
| `SaveTriggerBridge` | 入口点：把任务三事件 / 开箱 / 对白结束 / 场景切换完成 接成 `RequestSave`；离开玩法状态与退出游戏接成 `SaveNowAsync` | 根作用域入口点（`SaveTriggerBridge.cs:23`） |
| `SessionTitleRouter` | 入口点：标题「开始 / 继续 / 选择存档」三事件路由；回标题时按 `LatestSlot` 显示 / 隐藏「继续」 | 根作用域入口点（`SessionTitleRouter.cs:25`） |
| `SessionTitleRules` | 纯静态：`PickNewGameSlot`、`ShouldShowContinue` | `SessionTitleRouter` 调（`SessionTitleRules.cs:10`） |
| `SaveSlotsController` | 选槽面板的会话控制：开面板、点击分派、覆盖 / 删除二次确认、离开标题时收掉面板 | 根作用域单例，被 `SessionTitleRouter` 按具体类型注入（`SaveSlotsController.cs:24`） |
| `NewGameTutorial` | 开局操作说明的展示闸门：开 `TutorialView` → 等满 `UIConfig.TutorialSeconds` → 等任意键 → 收面板 | 根作用域单例，被 `SessionTitleRouter` 与 `SaveSlotsController` 按具体类型注入（`NewGameTutorial.cs:28`） |
| `SaveSlotsView` | `UIView`（Panel）：三行槽位 + 删除 + 返回，只显示与抛事件 | `IUIService` 实例化（`SaveSlotsView.cs:26`） |
| `SessionConfig` | SO：槽数、保存提示文案与秒数、读档失败文案、进度占位文案、退出钩子超时 | `Data/Session/SessionConfig.asset`（`SessionConfig.cs:10`） |
| `SessionSaveData` | 槽位元数据分区（纯 DTO，Version 1） | `GameSession.SaveNowAsync` 落盘前更新（`SessionSaveData.cs:15`） |
| `SlotInfo` / `SlotState` / `SlotsMode` | 只读摘要 / 三态枚举 / 面板两种打开模式 | `GameSession.ReadSlotInfosAsync` 产出；`SaveSlotsView` / `SaveSlotsController` 按值消费 |
| `SessionStartedEvent` / `SaveCompletedEvent` | 事实事件（`readonly struct`） | `GameSession` 发布，各分区所有者订阅前者重载 |
| `SessionInstaller` | `GameplayInstaller`：注册以上全部（不 Resolve） | Boot 场景 `GameBootstrap` 物体，排在 `ExplorationInstaller` 之后（`SessionInstaller.cs:30`） |

## 触发点与稳定边界

| 触发点 | 事件 | 走法 |
| --- | --- | --- |
| 任务激活 / 完成 / 追踪变化 | `QuestActivatedEvent` / `QuestCompletedEvent` / `QuestTrackingChangedEvent` | `RequestSave`（合并，等闸门） |
| 开箱 | `CrateCollectedEvent` | `RequestSave` |
| 对白结束 | `DialogueService.OnEnded` | `RequestSave` |
| 叙事分区改变 | `NarrativeChangedEvent`（模块已安装时） | `RequestSave`，仍受稳定边界约束 |
| 场景切换完成（含新游戏首次落盘） | `GameStateChangedEvent.To == MonsterEncounterState` | `RequestSave` |
| 离开玩法状态 | `GameStateChangingEvent.From == MonsterEncounterState` | `SaveNowAsync` 直接存，不等闸门（捕获在 Exit 之前同步完成） |
| 退出游戏 | `GameQuit.RegisterBeforeQuit` 钩子 | `SaveNowAsync`，按 `SessionConfig.QuitHookTimeoutSeconds` 单独限时 |

闸门 `SaveGateRules.CanSave`：必须处于玩法状态，且没有对白（`DialogueService.IsRunning`）、没有面板 / 弹窗
（`UIService.TopView != null`）、没有待消费的战斗终局（`EncounterStep.PendingResult != None && !ResultConsumed`）。
四条件任一不满足就不落盘，请求继续挂着，多次 `RequestSave` 合并成一次（原因取最新一次）。所有触发都只在
`GameSession.CurrentSlot != 0` 时生效，标题页阶段不存。

Narrative 已安装时还必须满足 NarrativeStable。SaveNowAsync（包括退出与离场）也不能绕过对白/Narrative 的
完整恢复边界：不稳定时返回 false 并保留待保存请求，不改旧槽、不发成功通知。允许稳定面板内的退出保存，
但对白逐节点恢复仍未实现；不能把中途剧情只保存阶段 ID 后从头播放当作恢复支持。

## 新游戏 / 继续 / 选槽

- **新游戏**：`GameSession.NewGameAsync(slot)`（`GameSession.cs:106`）—— `saves.ResetAll()` → 槽位元数据置初值 →
  `state.PrepareRestore(false)`（清遭遇恢复准备）→ 发 `SessionStartedEvent(slot, true)` → `GoToAsync<MonsterEncounterState>`。
  首次落盘由「场景切换完成」触发。**操作说明不在这里**：新游戏的两条入口（标题「开始」、选槽面板的新游戏模式）
  各自先 `NewGameTutorial.ShowAsync(ct)`，等到玩家按键关掉教程才调 `NewGameAsync`——所以「进游戏」与「看说明」
  的先后由调用方决定，`GameSession` 不认识教程。新增新游戏入口时照做，否则那条路径会静默跳过教程。
- **继续**：`ContinueAsync(slot)`（`GameSession.cs:141`）—— 先 `ReadCandidateAsync` 只读候选校验（缺
  `SessionSaveData` 分区也算失败），失败则通知「存档不可用」、内存与 `CurrentSlot` 都不动、返回 false；成功才
  由 ISessionStateSource.ValidateCandidate 校验 Narrative 后，对刚读出的同一候选 Commit、PrepareRestore(true)、发事件、进场景。
  未知叙事内容/不支持恢复的阶段拒绝提交；旧档缺 Narrative 分区可视为未开始。这不提供全场景失败回滚。
- **选槽**：`SessionTitleRouter` 接管标题三事件——「开始」用第一个空槽直接开局，没有空槽开选槽面板
  `SlotsMode.NewGame`；「继续」读 `LatestSlot`，没有可用存档（`LatestSlot == 0`）时「继续」**隐藏**（不是置灰，
  `TitleView.SetContinueVisible(SessionTitleRules.ShouldShowContinue(...))`；回标题与选槽面板删掉最后一个存档后走同一判定）；「选择存档」开面板 `SlotsMode.Load`。`SaveSlotsController`
  负责面板会话（覆盖 / 删除走 `ConfirmView.WaitAsync`）；流程一旦离开标题（继续 / 新游戏成功），控制器订阅的
  `GameStateChangingEvent(From == TitleState)` 会自己把面板收掉，不会残留盖在玩法画面上（`SaveSlotsController.cs:150`）。
- **面板行的可选 / 可删规则**（`SaveSlotsView.IsRowSelectable` / `IsDeleteVisible`）：读档模式下只有
  `SlotState.Available` 的行能点，空槽 / 坏槽点不动；新游戏模式三态都能点（非空槽由控制器先弹确认覆盖）；
  「删除」按钮只在可读槽（`Available`）上显示，空槽没东西可删、坏槽走新游戏覆盖流程处理。
- 面板与确认弹窗的文案（「取消」「覆盖」「删除」「无法开始新游戏」「删除存档失败」等）是
  `SaveSlotsController` / `SaveSlotsView` 里的私有常量，**没有**进 `SessionConfig`；改文案直接改这两个类。

## 标题阳 / 阴主题（2026-10-12）

两版共用 `TitleView` 的按钮与 `Canvas_Panel`，正式切图分别在 `Art/Sprites/UI/Title/Yang/`、`Yin/`。
`TitleView.SetAlternateAppearance(bool)` 替换背景、按钮底图与选中图，false 恢复阳版；不额外叠加预览乘色。
未接正式素材的旧预制体返回 false，路由继续使用 `SetAppearanceTint` 兼容。没有新增 Canvas、材质实例或每帧剧情查询。

- `SessionConfig.TitleYinStoryFlag` 是**未绑定的占位入口**，默认空值；文策确定节点后，在正式阶段 `SetFlags` 设置该键并赋到配置。
  当前未新增真实剧情节点，也不把妖界或场景编号自动等同于阴版。
- `TitleYinTint` 只作为未接正式素材时的兼容预览色。正式素材已收到并原样复制，20 张 PNG 的字节与原文件一致。
  背景自带铜镜、云纹和标题；按钮自带文字。旧 Logo 与文字叠层由预制体关闭，保留既有点击路由。
- 回标题优先取当前会话的剧情分区；冷启动只读最近可用槽的候选，不 Commit、不改变当前内存进度。
  无档、旧档缺 Narrative 分区、未绑定或无标记时用阳版。剧情变化和选槽面板删除存档后刷新；迟到读盘不覆盖较新的刷新。
- 表现接口支持恢复阳版；**同一局剧情是否切回尚未定案**，没有添加复归标记、优先级或永久锁定规则。

验证入口：`SessionTitleRulesTests` 的标记判定、`Tests/EditMode/Core/TitleViewTests.cs` 的恢复原色与隐藏图片测试、
`Tests/PlayMode/Session/SessionTitleRouterTests.cs` 的真实帧冷启动候选只读与新游戏恢复，
以及 `Tests/Showcase/Title/TitleShowcase.cs` 的 Boot 标题阳 → 阴 → 阳预览。
2026-10-12 实测：编译控制台无错误；标题 EditMode 13/13、路由 PlayMode 1/1、Title 回放 1/1 通过，
回放两个表现检查点通过且无运行时异常，报告在 `Logs/verify/title/20261012-173355/report.md`。
Unity 资产体检四项通过，新 C# 与目录 `.meta` 已由 Unity 刷新生成；最终视觉验收仍待开发者确认。
未做真机 Profiler 测量；迟到读盘与删除最近槽仍未单独覆盖自动化测试。

埋点：`session/title_appearance` 记录显示的版本；`session/title_appearance_failed` 记录读取失败；
`session/title_appearance_read` 记录冷启动候选读取耗时。
本批没有新增帧循环埋点，历史候选点不纳入本批修补。

### 正式素材映射（2026-10-13）

`Yang/` 与 `Yin/` 各含以下 11 张图片，名称一致；只有目录区分版本。

| 文件名 | 用途 |
| --- | --- |
| `ui_title_bg.png` | 无按钮背景，已经包含游戏标题 |
| `ui_title_start.png` | 开始按钮，两版各一张；保留既有按压缩放反馈 |
| `ui_title_start_selected.png` | 开始按钮的悬停 / 选中 / 按压高亮，两版分别使用原始素材 |
| `ui_title_settings_normal.png`、`ui_title_settings_selected.png` | 设置的普通 / 选中图 |
| `ui_title_load_normal.png`、`ui_title_load_selected.png` | 选择存档的普通 / 选中图 |
| `ui_title_continue_normal.png`、`ui_title_continue_selected.png` | 继续的普通 / 选中图 |
| `ui_title_quit_normal.png`、`ui_title_quit_selected.png` | 退出游戏的普通 / 选中图 |

导入为 Single Sprite，保留原尺寸与透明通道，Bilinear、无 mipmap、无压缩；Unity 生成并维护 `.meta`。
菜单使用原生 Sprite Swap：悬停、键盘 / 手柄选中与按压共用对应选中图。
放置位置参考图与 PSB 源文件不进入运行时资产目录。
正式素材已通过 Unity MCP 接入 `TitleView.prefab`；Vector2 与内部按钮引用由编辑器 API 接线，未手改 YAML。
本批实测：标题 EditMode 16/16、正式素材回放 1/1 通过；回放验证阴版替换、阳版恢复和菜单选中图，控制台无错误。
报告在 `Logs/verify/title/20261013-000115/report.md`，包含阳版、阴版、恢复与选中图四张截图；最终视觉验收仍由开发者确认。
回放临时显示完整四个菜单用于构图检查；正常游玩仍按有无存档控制「继续」，触屏平台仍隐藏「退出游戏」。

### 阳版拆件入场（2026-10-13）

正式 `TitleView.prefab` 已接 `Core/UI/TitleEntranceMotion`，复用 LitMotion 2.0.2，不引入新的 Tween 库或 Canvas。
50 张运行时 PNG 在 `Art/Sprites/UI/Title/Yang/Entrance/`，`parts.json` 保留原画坐标；
45 条轨道的时间、偏移、旋转、缩放、运动曲线、淡入时长与呼吸幅度在 `Data/UI/TitleEntranceConfig.asset` 调整。

| 节拍（从入场开始计秒） | 表现 |
| --- | --- |
| 0～2.8 | 天空迅速显露，水面慢升，铜镜旋转下落并轻微落定 |
| 0.98～2.93 | 标题晚半拍紧随铜镜下落 |
| 1.25～3.8 | 莲花纹围绕铜镜中心展开；云雾与石头从镜后的小尺寸状态向左右、略向下错峰展开，远山保持原节奏 |
| 2.5～4.09 | 丝带用两个 RectMask2D 从左右外缘向内揭露，不拉伸图像 |
| 3.65～5.47 | 卷轴依次加速落下、短促回弹，再显示花纹与文字 |
| 5.05～6.3 | 开始按钮最后缓缓浮现；落定后保留细微的云、水面、铜镜与按钮呼吸 |

运动与淡入分开驱动；入场采用 unscaled 时间。`OnOpenAsync` 同步准备起始态后立即返回，
不等完整 6.3 秒，避免阻塞 `SessionTitleRouter` 的存档主题刷新。入场期间根 CanvasGroup 禁止交互，
落定后恢复。设置面板覆盖、主题切换与销毁均取消时间轴并复位；返回被覆盖的标题不重复入场。
普通菜单入场结束后交还原完整图片的 Sprite Swap；开始按钮入场时使用拆件，落定后隐藏六个拆件子图，
由同一呼吸节点上的完整 Image 接管 Sprite Swap。阳版与阴版高亮跟随主题切换，按压缩放仍留在原按钮父节点。
没有存档时继续隐藏，触屏退出隐藏的原规则不变，不由动画重新激活按钮。

当前只有阳版拆件。阴版使用已有完整正式图，切阴版时隐藏阳版拆件并恢复背景与按钮；恢复阳版不重播入场。
天空与水面的 Photoshop 渐变依赖原画布尺寸，已在原 1920×1080 画布上烘焙。
水纹 SoftLight 已合入水面，不能再叠一次原始水纹 PNG。`TitleEntranceBuilder` 只用于首次接线，已接线时拒绝覆盖；
后续直接调配置与预制体。点击开始后的转场见下节。

验证入口：`TitleEntranceMotionTests` 的 EditMode 配置/切主题检查与 PlayMode 覆盖/暂停检查；
`TitleShowcase.Entrance_RevealsPartsInRhythmAndRestoresMenuInteraction` 记录五个入场阶段并检查交互恢复，
原主题切换回放继续保留。实测：18 条标题 EditMode、3 条 PlayMode 和 2 条视觉回放通过，控制台无错误；
最终报告 `Logs/verify/title/20261013-025352/report.md`。50 张 PNG 的尺寸与配套 meta 全部通过检查。
镜后展开调整只改变 8 条云雾与石头轨道的起始偏移和缩放，其他 37 条轨道与全部终点保持不变；
右中景、右前景石头移到铜镜后层。调整后 18 条 EditMode 与 2 条视觉回放通过，控制台无错误，
报告 `Logs/verify/title/20261013-084020/report.md`。
全局 gc 扫描仍有工作区原有的 Dynamic 字体缓存膨胀告警，未清除其他改动。最终视觉表现由开发者确认。

开始高亮素材原样复制到两版 `ui_title_start_selected.png`，字节校验与源文件一致；Unity 导入与 meta 配套完成。
高亮修补后 18 条 EditMode、3 条视觉回放通过，无运行时异常，报告 `Logs/verify/title/20261013-092437/report.md`。
高亮回放覆盖两版悬停、移出还原、键盘/手柄选中与恢复阳版；同时回归原标题入场与点击开始进入场景。
默认态修正：TitleView 的 SelectDefaultOnOpen 为 false，打开或从上层面板返回时不自动选中开始，两版保持各自普通图。
DefaultSelected 仍保留开始按钮；UIService 在真实导航/确认输入到来且没有选中项时建立焦点，不影响键盘/手柄操作。
普通图与高亮图引用已核实，没有互换。38 条相关 EditMode、2 条视觉回放通过，无运行时异常，
报告 `Logs/verify/title/20261013-094256/report.md`，包括两版未操作普通态与真实手柄导航、确认键开局。

### 点击开始转场（2026-10-13）

正式 `TitleView.prefab` 已接 `Core/UI/TitleStartTransition` 和 `Data/UI/TitleStartConfig.asset`。
空槽开局时，SessionTitleRouter 先等待转场，再沿用操作说明与 NewGameAsync；无空槽先打开选槽面板。
满槽选择并确认覆盖后，SaveSlotsController 先关闭全屏选槽面板，再播放同一段标题转场、显示操作说明并开局。
取消覆盖不播放、不初始化会话；开局失败或取消后复位标题演出。
继续、选择存档不播放这段转场；缺少组件或 Shader 不受支持时保留原开局路径。

| 节拍 | 表现 |
| --- | --- |
| 0～1.15 秒 | 停止呼吸并从落定姿态缓转铜镜 32 度，轻微放大，锁住标题操作 |
| 1.15～3.35 秒 | 帧末截取一次完整标题；同心水波、扭转与模糊渐强，速度线晚半拍显现，最后收成黑幕 |
| 转场结束 | 立即释放 RenderTexture 和临时材质，只留静态黑底；操作说明和原加载黑幕接住后续流程 |

效果使用 `Art/Shaders/TitleWaterTransition.shader` 与预制体引用的材质，不新增 URP Renderer Feature。
配置控制镜转时长/角度、水波时长以及水波/模糊/速度线强度；运行时不写 SO 或共享材质。
暂停时仍用真实时间播放；取消、标题停用或开局失败会复位镜子与交互并释放截图。
截图按图形后端的 UV 方向采样，避免 Windows RenderTexture 上下颠倒。
埋点 `session/title_start_transition` 记录等待演出的耗时。

验证：21 条标题/操作说明 EditMode、6 条标题 PlayMode 通过；原入场与主题回放仍通过。
最终开始转场回放五步全部通过、无运行时异常，报告 `Logs/verify/title/20261013-090844/report.md`。
回放实际点击开始、观察镜转与水波、确认截图释放、按键关闭说明并等待场景加载黑幕揭开。
未做手机 GPU/显存性能测量，最终视觉节奏仍由开发者确认。
满槽转场补接验证：48 条 Session EditMode 通过；真实鼠标满槽选档、覆盖确认、标题转场与进入场景五步通过，
报告 `Logs/verify/title/20261013-102142/report.md` 的 MouseStart_FullSlots_ConfirmedOverwritePlaysTransition 段无异常。
该批空槽回放未触发转场，不能将整批报告视为通过；模拟按压改为至少 50ms 后，单独复验被手动 Play 状态阻断，尚不能确认失败原因。
回放存档只写临时覆盖目录，未改正式槽文件。另通过 Unity 原生 API 补回 Noto 字体当前缺失图集页，保留 475 个字符及已有图集；
修复前副本在 `Tools/Font_NotoSansSC_before_missing_atlas.asset.backup`，没有清除字形缓存或字体样式。

### 本批待审清单（未提交）

路径均相对项目根；新增 C# 与目录的 Unity 生成 `.meta` 一并归入本批。

| 文件路径 | 位置 | 改了什么 |
| --- | --- | --- |
| `Assets/_Project/Prefabs/UI/TitleView.prefab` | 标题、菜单、开始按钮 | 按参考构图使用锚点布局，移除竖排布局，关闭非交互射线 |
| `Assets/_Project/Scripts/Core/UI/Views/TitleView.cs` | OnOpenAsync / SetAppearanceTint / OnRefresh | 缓存图片原色、切换乘色且可还原，标题文字可配置 |
| `Assets/_Project/Scripts/Runtime/Session/SessionConfig.cs`、`Assets/_Project/Scripts/Runtime/Session/SessionTitleRules.cs` | 标题主题参数与判定 | 未绑定剧情标记占位入口、预览色调、纯标记判定 |
| `Assets/_Project/Scripts/Runtime/Session/SessionTitleRouter.cs`、`Assets/_Project/Scripts/Runtime/Session/SessionInstaller.cs`、`Assets/_Project/Scripts/Runtime/Session/SaveSlotsController.cs` | 标题刷新与注册、删除通知 | 读取会话或只读候选、拒绝迟到结果、成对订阅、显示版本与失败及耗时埋点 |
| `Assets/_Project/Scripts/Tests/EditMode/Core/TitleViewTests.cs`、`Assets/_Project/Scripts/Tests/EditMode/Session/SessionTitleRulesTests.cs` | 标题快测试 | 原色恢复、隐藏图片、未绑定及跨槽标记判定 |
| `Assets/_Project/Scripts/Tests/PlayMode/Session/SessionTitleRouterTests.cs`、`Assets/_Project/Scripts/Tests/PlayMode/Game.Tests.PlayMode.asmdef` | 帧循环路由测试及已有库引用 | 验证只读候选不提交、当前进度不变、新游戏恢复阳版 |
| `Assets/_Project/Scripts/Tests/Showcase/Title/TitleShowcase.cs` | Boot 标题回放 | 阳 → 阴 → 阳，检查图片与 Canvas 数量不变并截图 |
| `ai-docs/docs/modules/session/session-module-guide.md`、`ai-docs/docs/modules/session/session-external-api.md`、`ai-docs/docs/modules/session/session-extension-guide.md` | 标题主题说明 | 同步配置、公开入口、接线、验证状态与待定事项 |

埋点门：本批主题显示、读取失败和异步读盘耗时已记录；没有新增代码对应的应埋未埋候选。
沉淀门：本次无新结论；测试的真实帧要求已写在测试文件头，不另增重复规则。
范围外：工作区原有字体、文档、工具与节奏玩法改动未纳入本批；全局 gc 扫描仍报已有 Dynamic 字体数据膨胀，未清除他人未提交的字体数据。

拟提交信息（仅提案，尚未提交）：

```text
feat(ui): 标题悬挂菜单与阳阴主题切换入口

- 标题页采用悬挂菜单、中央标题和下方开始的锚点布局
- Session 读取剧情标记决定主题，冷启动只读最近槽候选
- 补齐主题快测试、路由帧循环测试、视觉回放与模块说明
- 留：正式切图、剧情节点和同一局切回条件待美术与文策定案
```

## 开局操作说明（新手教程）

开始新游戏时先盖一张全屏操作说明图（`TutorialView`，Core/UI 的面板，Addressables 地址 `TutorialView`），
最少展示 `UIConfig.TutorialSeconds`（默认 3 秒）；到点才淡入「按任意键继续」提示并开始接受**任意键 / 手柄键 /
鼠标点击 / 触屏按下**，玩家按键后教程收掉，紧接着才 `NewGameAsync` 进游戏。读档（「继续」）不走这一步。

| 规则 | 在哪 | 说明 |
| --- | --- | --- |
| 最短展示时长 | `UIConfig.TutorialSeconds`（`Data/UI/UIConfig.asset`） | 这段内任何按键都不响应，防「点开始」那一下把教程顺手关掉 |
| 什么算「任意键」 | `TutorialView.AnyDismissPressed` | 读 Input System 设备，代码在 `Scripts/Core/`（框架表现层，非玩法层）；只认真正的按钮，摇杆 / 扳机不算；只看 `wasPressedThisFrame`，进面板之前按住的键不算 |
| 面板开不出来 | `NewGameTutorial.ShowAsync` 的 catch | 预制体 / 地址缺失、UIService 已释放 → 记错误日志后照常进游戏。**教程是提示不是闸门**，不许卡住开局 |
| 等待被取消 | 同上的 finally | 取消按原样抛给调用方；关面板用不带 ct 的重载，否则「已取消」会让教程图永久盖在屏上 |

教程图与「按任意键继续」提示条都是图片，面板里没有 TMP 文字——换美术只换 `Prefabs/UI/TutorialView.prefab` 上的
两个 Sprite 引用。**当前是占位素材**（`Art/Sprites/UI/Tutorial/ui_tutorial_controls.png`、`ui_tutorial_hint.png`，
由 `scripts/placeholder-ui/make_tutorial_art.py` 按键位表生成，改键后重跑），正式素材到位后删脚本与占位图。

## 槽位元数据

`SessionSaveData`（`SessionSaveData.cs:15`）：`SavedAtUtcTicks`、`PlaytimeSeconds`、`ProgressText`、`SceneKey`，
Version 1。`ReadSlotInfosAsync` 只读候选，`candidate.Contains<SessionSaveData>()` 才算 `Available`，否则按
`Empty`（无文件）或 `Unavailable`（有文件但读不了/缺元数据）处理，三态见 `SlotState.cs`。进度描述由
`SessionStateAdapter.BuildProgressText()` 在保存时算：追踪中的任务标题 → 进行中第一条主线标题 → `SessionConfig.ProgressPlaceholder`。

## 接线要求

缺任一项都**不会编译报错**，只会运行时不动或报错：

| 项 | 要求 | 缺了会怎样 |
| --- | --- | --- |
| Installer | `Boot.unity` 的 `GameBootstrap` 挂 `SessionInstaller`，排在 `ExplorationInstaller` 之后 | 没挂：解析不到 `GameSession`，各触发点全部失效 |
| Config | `SessionInstaller.config` 拖 `Data/Session/SessionConfig.asset` | 没拖：记 Error 并用代码建的默认值顶上 |
| 预制体地址 | Addressables（UI 组）`SaveSlotsView` → `Prefabs/UI/SaveSlotsView.prefab`；`ConfirmView` → `Prefabs/UI/ConfirmView.prefab`；`TutorialView` → `Prefabs/UI/TutorialView.prefab`。**地址等于类名** | `ui.OpenAsync<T>()` 找不到预制体（教程那条会退化成「不显示教程、直接进游戏」） |
| `TutorialView` 预制体接线 | `content`（教程图 Image）**必填**，`background` / `hintGroup` 可空 | `content` 为空时打开即抛，被 `NewGameTutorial` 吞成一条错误日志：表现是教程永远不出现 |
| `TitleView` 按钮 | 预制体 `Buttons` 组下 `ContinueButton` / `LoadButton` 拖到 `TitleView.continueButton` / `loadButton` | 两个字段可空，不接线时旧行为不变（按钮不显示回调） |

## 验证入口

- EditMode：`Tests/EditMode/Session/`——`GameSessionTests.cs`（新游戏 / 继续 / 坏文件 / 高版本槽 / 删除 / 合并请求）、
  `SaveGateRulesTests.cs`、`SessionSaveDataTests.cs`、`SessionTitleRulesTests.cs`、`SaveSlotsViewTests.cs`、
  `NewGameTutorialTests.cs`（教程面板开不出来时不挡开局、等待被取消时面板一定收掉）；
  `Tests/EditMode/Core/TutorialViewTests.cs`（最短展示时长这条纯规则）。
- Showcase：计划路径 `Tests/Showcase/Session/SessionShowcase.cs`（存 → 退 → 读一致；坏档 / 高版本槽显示「不可用」），
  由 PRP `save-session` 的 T6 产出，**本文档时点已落地**；产出后跑 `/verify-module Session`。

## 禁止事项

- **不要缓存分区实例**：`LoadAsync` / `ResetAll` / `Commit` 都整体替换分区字典，任何持有旧引用的服务会把改动
  写进一份没人读的对象。读档 / 新游戏后想拿最新分区，重新 `saves.Get<T>()`。
- 对白进行中（`DialogueService.IsRunning`）、任意面板打开中（`UIService.TopView != null`）都不保存，交给
  `SaveGateRules` 判断，不要在业务代码里绕过闸门直接调 `saves.SaveAsync`。
- 新增分区想参与「读档后重载」，订阅 `SessionStartedEvent` 自己重载，不要指望 Session 主动调用；Session 不认识
  任何玩法名词以外的重载逻辑。
- 不要把 `DialogueReadData`（已读记录）经 `saves.Get<>()` 放进槽位——它故意不实现 `ISaveData`，独立档案
  `dialogue-read` 由 `Game.Dialogue.DialogueReadStore` 自己读写，见 `dialogue-module-guide.md`。
