---
type: module-guide
module: session
layer: runtime
maturity: seed
---

# Session 模块指南

## 五槽存档卷轴界面（2026-10-14）

SessionConfig 的默认值和正式配置均为五槽，原槽 1～3 的文件编号与数据格式保留，新增槽 4、5 复用同一读写、覆盖与删除流程。
SaveSlotsView 使用 1920×1080 参考坐标，五条卷轴从左至右按槽号排列；普通、高亮、空槽与不可用图片位于 `Assets/_Project/Art/Sprites/UI/SaveSlots/`。
原始 PNG 保留透明通道与尺寸，Single Sprite、Bilinear、无 mipmap、无压缩；布局与字体参考图位于 `docs/design/references/save-slots/`。
进度使用思源宋体 CN Bold 44.81，日期与时长为 19.33；普通态白色，高亮态 `#27A0BF`，日期与游玩时长分两行显示。
空槽保留原新游戏/读档判定并隐藏进度文字；不可用槽使用普通卷轴与「不可用」文字，禁止直接读档；删除仍走既有二次确认。
Session EditMode 实测 51/51 通过，新增测试覆盖槽 4、5 保存、重启读取、删除以及原槽文件不变；编辑器渲染检查覆盖五槽绑定、第五槽点击与高亮文字颜色。
预览在 `outputs/save-ui/preview.png`。未进行手机真机验证或整局回放，正式存档文件未用于测试。

### 卷轴下拉删除（2026-10-14）

存档条下部（参考坐标 y=650～981）接 `UIDragReveal`：鼠标悬停显示 `ui_cursor_grab.png`，按住左键显示 `ui_cursor_held.png`。
两张光标分别原样复制美术提供的「抓手.png」「抓手2.png」，保留 70×80、61×68 尺寸与透明通道，Cursor 类型、可读、无压缩及 mipmap。
下部按压由手柄消费，不能触发父按钮读档；下拉距离按 Canvas 坐标计算，最多 110 单位，松手达到一半时落到展开位，否则恢复。
默认删除渐变与叉号的 CanvasGroup 为零透明且不接射线；下拉到位并松手时直接进入既有删除二次确认，不再要求点击叉号。取消确认后卷轴恢复原位，确认后才删除存档。
上推手柄可收起，重新绑定、停用或失去窗口焦点会取消抓取、隐藏删除区并恢复系统光标；空槽和不可用槽不提供删除拖拽。
参考素材中带叉号的整条图片不再用作未下拉状态，防止默认出现删除标记。
EditMode 55/55、停用复位 PlayMode 1/1 通过；编辑器射线检查验证下部命中手柄、下拉不读档、第五槽删除回调，前后预览在 `outputs/save-ui/before-pull.png` 与 `after-pull.png`。

点击路由修正：Input System 会独立查找 `IPointerClickHandler`，手柄必须接收并消费点击，避免上抛给父级读档按钮。
卷轴展开后整条按钮点击进入删除确认，抓取中忽略按钮回调；收起后恢复正常读档。
此前直接调用删除按钮回调的验证未覆盖真实叉号落点，不能作为鼠标删除操作通过的证据。
修正后 EditMode 56/56 通过，预览检查从叉号位置做 GraphicRaycaster 命中与 PointerClick，确认命中删除按钮；展开后点击卷轴只发删除事件，读档事件计数不增加。

## 五店接线与切槽捕获边界（2026-10-13）

SessionInstaller.registerTitleRouter默认true，正常Boot保持标题路由。旧ShopPlaytest启动场景已归档为Legacy入口，五店现在走正常开始/继续和当前槽。

NewGame/Continue从重置/读取候选到场景切换结束持有sessionTransitioning守卫。期间SaveNow不捕获旧场景，Tick不自动写盘；切换完成事件的RequestSave保留到新现场稳定后处理。这样旧MonsterEncounterState离场不会覆盖刚载入的目标槽EncounterSaveData。切槽重入或写盘在途拒绝新局/继续；finally在失败/取消后释放守卫。普通离场保存规则保持不变。

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
| `SaveSlotsView` | `UIView`（Panel）：五条悬挂卷轴 + 删除 + 返回，只显示与抛事件 | `IUIService` 实例化（`SaveSlotsView.cs:26`） |
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
（`UIService.TopView != null`）、没有待消费的战斗终局（`SessionStateAdapter.BattleResultPending`）。
四条件任一不满足就不落盘，请求继续挂着，多次 `RequestSave` 合并成一次（原因取最新一次）。所有触发都只在
`GameSession.CurrentSlot != 0` 时生效，标题页阶段不存。

**`BattleResultPending` 收两个来源**（`SessionStateAdapter.cs:88-89`）：待消费的遭遇终局
（`EncounterStep.PendingResult != None && !ResultConsumed`）**与实时遭遇升级回合制的「在途」**（`step.IsEscalating`）。
后者 2026-10-14 并入（PRP/combat-iteration D5，用户拍板「战中不可存档」）：升级那一路**不经**剧情的 `TryBeginBattle`，
所以 `NarrativeStable` 挡不住它——两条路各自堵一个口，合起来才是「战斗中不可存档」。

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
  `GameStateChangingEvent(From == TitleState)` 会自己把面板收掉，不会残留盖在玩法画面上（`SaveSlotsController.cs` 的 `HandleStateChanging`）。
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
- `TitleYinTint` 只作为未接正式素材时的兼容预览色。正式素材初次接入时原样复制；阳版四张卷轴选中图在 2026-10-14 调整了原有渐变的可见范围，见下文。
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
阳版四张卷轴选中图使用 PSB 原有浅青蓝到黄绿色标，将渐变范围对齐到屏幕内可见纸面：原卷轴的上半截在画面外，较短的设置/退出原 PNG 顶部已偏绿。2026-10-14 在 PSB 副本中调整渐变缩放与偏移后原生导出，未改色标、源 PSB、文字、尺寸或透明轮廓；沿用原文件名和 GUID。
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
| 0.52～2.87 | 中景石头保持原尺寸，从左右外侧向镜子收拢；外围两块晚一拍从画面左右外侧滑入，均持续 1.25 秒并淡入、减速落定 |
| 1.2～5.65 | 云雾保留镜后展开；镜旁白色云纹从 1.55/1.72 秒起线性淡入，到 5.65 秒才完全显现；底部雾气仍随自身运动淡入 |
| 2.5～4.09 | 丝带用两个 RectMask2D 从左右外缘向内揭露，不拉伸图像 |
| 3.5～4.65 | 中间的选择存档/继续先展开（3.5/3.57），外侧设置/退出随后展开（3.78/3.85）；0.8 秒展开中轻轻回拉一次，再落定 |
| 4.65～5.65 | 开始按钮最后缓缓浮现；落定后保留细微的云、水面、铜镜与按钮呼吸 |

运动与淡入分开驱动；入场采用 unscaled 时间。`OnOpenAsync` 同步准备起始态后立即返回，
不等完整 5.65 秒，避免阻塞 `SessionTitleRouter` 的存档主题刷新。入场期间根 CanvasGroup 禁止交互，
落定后恢复。设置面板覆盖、主题切换与销毁均取消时间轴并复位；返回被覆盖的标题不重复入场。
普通菜单入场结束后交还原完整图片的 Sprite Swap；开始按钮入场时使用拆件，落定后隐藏六个拆件子图，
由同一呼吸节点上的完整 Image 接管 Sprite Swap。阳版与阴版高亮跟随主题切换，按压缩放仍留在原按钮父节点。
没有存档时继续隐藏，触屏退出隐藏的原规则不变，不由动画重新激活按钮。

卷轴底轨道的 `UnrollVertical` 改变 RectTransform 高度，顶部 pivot 固定，九宫格保留上下横轴与边线厚度。
源图上半段在画面外，初始长度包含该隐藏段，只有可见纸面从约 6% 展开；曲线在可见长度略过后回拉约 7%，再落定。
白色底部渐变位于纸面内部、横轴上方，随下沿移动和回拉。卷轴底按 PSB 合成纸面与渐变，九宫格的下边界保留整段渐变高度（按 parts.json 中纸面下沿减渐变上沿计算），展开只拉伸上方纸面。
渐变从展开时就显现，花纹和文字按原时间出现；完成后完整按钮图接管。不能把九宫格下边界只设为横轴的 18 像素，否则渐变随中段拉伸，在展开前段会变弱。
文字上层还需一层淡白覆盖：原 `底部渐变.png` 放在各 Button 的最后一个子节点，位于文字、花纹及完整按钮图之上，使用原尺寸和 PNG 透明度。它跟随纸面相同的展开曲线、位移与淡入，落定后保留在阳版普通态；阴版及阳版悬停、选中、按下时隐藏，避免冲淡原始高亮配色。`TitleEntranceMotion` 监听按钮 Image 的脏顶点回调，以实际 Sprite Swap 图片判断覆盖层显隐，启停时配对订阅/注销，无逐帧轮询。此前误加到横轴外的 `ScrollFooter` 已移除。
卷轴底必须用 Photoshop 在原画布烘焙图层样式，保留整张浅色渐变。直接导出的纯深蓝填充与完整按钮不同，会在入场结束交还完整图时跳色。
四张 `卷轴底.png` 已按原 PSB 烘焙纸面与内部渐变；保持原宽高与 GUID，画面外上半段用透明占位保留坐标，源 PSB 不写回。
花纹/文字留在纸面内并受 RectMask2D 裁切；花纹在展开后 0.34 秒开始、用 0.28 秒淡入，文字在 0.48 秒开始、用 0.32 秒淡入。
中景石头左/右在 0.52/0.6 秒开始，从外侧以小幅斜向位移靠近下落的镜子；运动与线性淡入均持续 1.25 秒，末段减速而不反弹。
外围左/右石头在 1.5/1.62 秒开始，以横向偏移 -280/+220 从画面外向内滑入，运动与淡入均为 1.25 秒，沿用中景的减速曲线；保持原高度、大小及落定位置。
`LinearFade` 用于四块石头及四条云雾轨道；白色 `云_左/右` 淡入持续 4.1/3.93 秒，移动停止后继续淡入；`雾气_左/右` 保留 2.3/2.4 秒，其他淡入保留原曲线。
主题切换和停用时同时恢复高度，防止返回菜单留下缩短的纸面。最终尺寸/位置保持不变。

当前只有阳版拆件。阴版使用已有完整正式图，切阴版时隐藏阳版拆件并恢复背景与按钮；恢复阳版不重播入场。
天空与水面的 Photoshop 渐变依赖原画布尺寸，已在原 1920×1080 画布上烘焙。
水纹 SoftLight 已合入水面，不能再叠一次原始水纹 PNG。`TitleEntranceBuilder` 只用于首次接线，已接线时拒绝覆盖；
后续直接调配置与预制体。点击开始后的转场见下节。

验证入口：`TitleEntranceMotionTests` 的 EditMode 配置/切主题检查与 PlayMode 覆盖/暂停检查；
`TitleShowcase.Entrance_RevealsPartsInRhythmAndRestoresMenuInteraction` 记录八步入场，检查向内收拢、持续淡入、中间先开、一次回拉、填充/底部渐变和交互恢复，
原主题切换回放继续保留。实测：18 条标题 EditMode、3 条 PlayMode 和 2 条视觉回放通过，控制台无错误；
最终报告 `Logs/verify/title/20261013-025352/report.md`。50 张 PNG 的尺寸与配套 meta 全部通过检查。
镜后展开调整只改变 8 条云雾与石头轨道的起始偏移和缩放，其他 37 条轨道与全部终点保持不变；
右中景、右前景石头移到铜镜后层。调整后 18 条 EditMode 与 2 条视觉回放通过，控制台无错误，
报告 `Logs/verify/title/20261013-084020/report.md`。
全局 gc 扫描仍有工作区原有的 Dynamic 字体缓存膨胀告警，未清除其他改动。最终视觉表现由开发者确认。

卷轴展开/石头淡入调整实测（2026-10-13）：20 条标题 EditMode、2 条入场 PlayMode 通过；
最终七步回放通过、0 检查点失败、0 运行时异常，控制台无错误，报告 `Logs/verify/title/20261013-164901/report.md`。
四张卷轴底 Sprite 的九宫格边界由 Unity 原生导入器设置；45 个部件最终中心、21 条其他场景/开始轨道保留。
本批未重新跑点击开始进场景的回放或手机性能测量；全局 gc 扫描仍报现有字体缓存，视觉观感尚待开发者确认。

填充/透明度/节奏再调整实测：20 条标题 EditMode、3 条入场 PlayMode 与七步回放通过，控制台无错误。
报告 `Logs/verify/title/20261013-183824/report.md` 仅验证底部渐变同步伸展，未覆盖整张卷轴填充，不能据此判定结束跳色已修复。全段从 6.3 秒收紧到 5.65 秒，45 个部件最终中心与 20 条其他场景轨道保留。
整张填充修正实测：原 PSB 原生烘焙四张底图，新增结束前后实际屏幕像素比较；
报告 `Logs/verify/title/20261013-191811/report.md` 通过，继续卷轴抽样色 `88DDE3 → 88DDE3`，0 检查点失败、0 运行时异常，控制台无错误。
渐变合并/内收/全程淡入调整实测：20 条标题 EditMode、4 条入场 PlayMode 和八步回放通过，控制台无错误。
报告 `Logs/verify/title/20261013-201510/report.md` 同时验证上部填充 `88DDE3 → 88DDE3` 和纸面内部白色渐变 `A0D8E4 → A0D8E4`；未覆盖横轴外下方的渐变，也未验证镜旁白色云纹在运动停止后继续淡入。
镜旁云纹调整实测：20 条标题 EditMode、6 条入场 PlayMode 通过；阳阴主题切换回放通过。
报告 `Logs/verify/title/20261013-215207/report.md` 的白色云纹淡入检查有效，但外部渐变检查验证了错误的位置，不能作为用户所需纸面内部渐变的验收依据；对应外部渐变测试已移除。
纸面内部渐变修正（2026-10-13）：四张卷轴底的九宫格下边界改为 180/182/187 像素，固定保留原渐变范围；未改文字时点、云纹淡入或石头运动。
20 条标题 EditMode、5 条入场 PlayMode、主题切换回放通过；纸面底色报告 `Logs/verify/title/20261013-232536/report.md` 为 PASS、0 检查点失败、0 运行时异常，控制台无错误。该报告只验证了文字后面的纸面渐变，未覆盖用户随后明确的“叠在字上面”要求。
新增文字出现前的实际像素检查：底部 `A0D8E4`、上方 `8AD2DF`；结束切图前后底部均为 `A0D8E4`。采样须避开顶部 78 像素的回放提示条。最终视觉由开发者确认，未复测点击开始转场或真机性能。
文字前景覆盖修正：20 条标题 EditMode、6 条入场 PlayMode、2 条标题回放通过；报告 `Logs/verify/title/20261013-234936/report.md`，无检查点失败或运行时异常。
四个卷轴在落定后开关覆盖层进行实际像素比较，文字阴影区域分别有 3227/4975/3650/4686 个像素被提亮，证明渐变位于文字上层；结束前后纸面底部均为 `D5ECF1`，没有换图跳色。截图 `15-四卷轴渐变覆盖文字.png` 供开发者确认覆盖强度。
选中渐变可见范围与外围石头调整（2026-10-14）：6 条入场 PlayMode、2 条标题回放通过，报告 `Logs/verify/title/20261014-135748/report.md`；四张选中 PNG 尺寸及逐像素透明通道保持不变。回放检查四按钮高亮的上下色差、普通态文字渐变，以及外围石头横向滑入、淡入、终点复位；仅修改两条外围石头轨道，另外 43 条轨道原样保留。本批未复测点击开始转场或手机性能。

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
新游戏保留这段完整转场；读档使用下节的短版。缺少组件或 Shader 不受支持时保留原流程。

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

### 读档收卷与短转场（2026-10-15）

仅 `SlotsMode.Load` 的选槽点击播放收卷：纸面由顶部固定的 RectMask2D 从下向上裁切，原图底轴与挂穗用 RawImage 的 UV 区域保持原尺寸移动，整条不缩放。
收卷时长在 `SessionConfig.LoadRollSeconds`（0.45 秒），轴与挂穗高度在存档预制体 `rollFooterHeight`（218 参考像素）。遮罩在普通显示和下拉删除时停用，不改变删除拖拽。
收卷后关闭选档面板，显示原标题并调用 `TitleView.PlayLoadTransitionAsync`：镜转 0.35 秒、水波 1.2 秒，分别由 `TitleStartConfig.LoadMirrorSeconds/LoadWaveSeconds` 控制。
标题「继续」复用同一短版；新游戏、满槽覆盖开局仍走 `PlayStartTransitionAsync` 的完整节拍与操作说明。
转场结束后调用原 `GameSession.ContinueAsync`，不改变存档格式或候选校验。失败时复位镜子与黑幕并重开选档，取消收卷则恢复裁切、底轴、Sprite Swap 和交互。
验证入口：`SaveSlotsLoadTransitionTests`（暂停完成、取消复原）、`TitleStartTransitionTests`（短版独立时长与原转场回归），以及 `TitleShowcase.MouseLoad_RollsScrollThenShortWaterWarpEntersWorld/MouseLoad_MissingFileRestoresSelection` 的真实鼠标回放。
实测：相关 EditMode 63/63、首轮 PlayMode 7/7 通过；最终真实鼠标回放 2/2 通过，0 检查点失败、0 运行时异常，报告 `Logs/verify/title/20261015-013247/report.md`。最后一次 PlayMode 复测连接未返回结果，不计为通过。回放只使用临时存档目录，未做真机性能测量；全局 gc 仍报两份已有 Dynamic 字体缓存膨胀。

选档加载寿命修正（2026-10-15）：短转场和 `ContinueAsync` 使用 `SaveSlotsController` 的寿命令牌，控制器释放时取消；标题正常关闭不再取消入场。标题阶段只需 Boot，进入玩法后 Boot 常驻、SampleScene 由 `SceneGameState` 叠加加载。根因与验证陷阱见 [经验记录](../../../pitfalls.md#标题关闭取消了选档加载画面只剩-boot)。
本次 Session EditMode 51/51、Session PlayMode 4/4（含标题先销毁的回归测试）、真实鼠标读档回放 2/2 通过，0 检查点失败、0 运行时异常；报告 `Logs/verify/title/20261015-032054/report.md` 明确检查玩法状态与两个场景均已加载。使用隔离临时存档，最终视觉由开发者确认。未新增 lint 规则或改通用规范，因此不运行规则行为 eval。

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
