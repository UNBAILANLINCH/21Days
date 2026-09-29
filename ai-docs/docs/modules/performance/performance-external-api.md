---
type: external-api
module: performance
layer: runtime
maturity: stable
---

# Performance 外部接口

> 别的模块要拉起一段演出、查播放状态时查这份。内部结构见 [`performance-module-guide.md`](performance-module-guide.md)。

## `Game.Performance.IPerformanceService`（根作用域单例 + `IGameService`，构造注入即可）

| 成员 | 签名 | 说明 |
| --- | --- | --- |
| `IsRunning` | `bool IsRunning { get; }` | 是否有演出正在播放（同一时刻只允许一段） |
| `CurrentId` | `string CurrentId { get; }` | 正在播放的演出 id；空闲时为 `null` |
| `HasPlayed` | `bool HasPlayed(string id)` | 该 id 是否已完整播过或被跳过（读存档分区，用于「只播一次」判断） |
| `PlayAsync` | `UniTask<PerformanceResult> PlayAsync(string id, CancellationToken ct = default)` | 按 Addressables 地址拉起一段演出并等它结束（成功 / 跳过 / 取消 / 失败见下）；等价于传 `PerformancePlacement.None` |
| `PlayAsync`（摆放重载） | `UniTask<PerformanceResult> PlayAsync(string id, PerformancePlacement placement, CancellationToken ct = default)` | 同上，演出实例化后 `SetPositionAndRotation` 到 `placement`；`placement.HasValue` 为假（`None`）时保持预制体自身位姿 |
| `Confirm` | `void Confirm()` | 代码确认继续：正在停顿（Holding）时等价于玩家按确认，否则无事；**不会补全逐字显示中的字幕，也不计连点**（补全只发生在玩家打字中连点 / 连按 Advance 键满次时——默认三下、相邻间隔 ≤ 0.5 秒，与对白同一规则——走 `HandlePlayerAdvance`）；**下一帧播放循环才生效**；**台词记录（LOG）开着时请求被丢弃**（开着期间一切推进都冻结），`Skip()` 不受 LOG 影响 |
| `Skip` | `void Skip()` | 代码跳过：正在播放（Playing/Holding）时等价于长按满，结果记为 Skipped；**不看舞台的 `skippable` 开关**（那只约束玩家长按输入）；**下一帧播放循环才生效** |

### `PlayAsync` 的语义

- **场景角色隐藏**：加载舞台前收集场景中带 `ChibiPuppet` 的角色（含未激活），优先取 `ChibiPuppetMotion.TrackedRoot`，没有则取顶层根，关闭根下 Renderer 与 Canvas；排除 `PerformanceStage` 内的演员。所有入口共用，所有收尾路径按原 enabled 状态恢复。公共容器下的角色应配置自己的 `TrackedRoot`，避免兜底隐藏整个容器；开始后新生成的角色不在本次快照内。
- **世界时停**：舞台 `pauseWorld` 开启时持有全局暂停令牌，暂停模拟 Tick 与缩放时间；时间轴、字幕使用 unscaled 时间继续播放，收尾释放令牌。外层页签 / 对白仍持有令牌时不会提前恢复世界。
- 舞台相机接管时继承主相机分层剔除距离与球形/平面模式，结束恢复舞台作者值；缺主相机的退路保留作者值、不复制。

- **重入**：`IsRunning` 为真时再调，抛 `InvalidOperationException`，埋 `play_rejected(reason=busy)`，不修改当前演出。
- **参数校验**：`id` 为空或 `null` 抛 `ArgumentException`，埋 `play_rejected(reason=empty_id)`。
- **取消**：`ct` 取消时，`PerformanceRules` 置 `Cancelled`，照常收尾（关面板、恢复 HUD/输入图、释放时停令牌、归还实例），
  然后向上抛 `OperationCanceledException`（UniTask 约定，同 `DialogueService`）。
- **`Cancelled` / `Failed` 不记「已播」**：只有 `Completed` / `Skipped` 会写入 `PerformanceSaveData`，`HasPlayed` 才会返回 `true`。
- **`Confirm()` / `Skip()` 的用途**：给回放（Showcase）、编辑器试播、将来的触屏按钮用，不需要真的按键；效果与玩家输入完全等价，
  在 `PerformanceService` 的播放循环里走同一条处理路径（`HandleConfirm` / `HandleSkipped`）。

### `PerformanceResult` / `PerformanceOutcome`

| 类型 | 成员 | 说明 |
| --- | --- | --- |
| `PerformanceResult`（`readonly struct`） | `Id`、`Outcome`、`DurationSeconds` | `PlayAsync` 的返回值；`DurationSeconds` 取 `PerformanceRules.ElapsedSeconds`（含停顿等待时间） |
| `PerformanceOutcome`（枚举） | `Completed` / `Skipped` / `Cancelled` / `Failed` | `Completed`=自然播完；`Skipped`=玩家长按满或代码 `Skip()`；`Cancelled`=外部 `ct` 取消；`Failed`=加载失败 / 预制体缺舞台 / 播放异常 |

### `PerformancePlacement`（`readonly struct`）

| 成员 | 说明 |
| --- | --- |
| `PerformancePlacement(Vector3 position, Quaternion rotation)` | 指定摆放（摆到原点也算指定，`HasValue = true`） |
| `static None` / `default` | 不摆放，实例保持预制体自身位姿 |
| `Position` / `Rotation` / `HasValue` | 世界位置 / 朝向 / 是否指定 |
| `static FromTransform(Transform anchor)` | 取锚点世界位姿；`null` 返回 `None` |

## 事件（MessagePipe，`IPublisher<T>`/`ISubscriber<T>` 注入，一文件一个 `readonly struct`）

| 事件 | 字段 | 何时发布 |
| --- | --- | --- |
| `PerformanceStartedEvent` | `string Id` | `stage.Play()` 之前（面板已打开、舞台相机已接管） |
| `PerformanceEndedEvent` | `string Id`、`PerformanceOutcome Outcome` | `PlayAsync` 的 `finally` 里，**无论正常结束、取消还是异常都会发布**（`Failed` / `Cancelled` 也会） |

订阅按 `EventConventions.cs` 第 5 条：`ISubscriber<T>.Subscribe(...).AddTo(bag)`，句柄进 `DisposableBag` 自行释放。

## `Game.Performance.PerformanceTrigger`（场景组件）

| 成员 | 签名 | 说明 |
| --- | --- | --- |
| `PerformanceId` | `string PerformanceId { get; }` | 要播放的演出 id |
| `Mode` | `PerformanceTriggerMode Mode { get; }` | `OnEnter`（进入触发区）/ `OnSceneStart`（场景开始，由 `PerformanceSceneBinder` 调用） |
| `Once` | `bool Once { get; }` | 是否只播一次（存档已播过就不再触发） |
| `TryFire` | `void TryFire()` | 按判定规则尝试触发；未绑定服务 / id 为空 / 已播过 / 服务忙时不触发（各记一次 Warn 或埋 `trigger_skipped`） |
| `TryFire`（指定触发者） | `void TryFire(PerformanceTriggerActor actor)` | 同上；`actor` 是要在演出期间隐藏的触发者，传 `null` 时按场景里的 `PerformanceTriggerActor` 查一次 |
| `Anchor` | `Transform Anchor { get; }` | 演出摆放锚点；非空时触发用它的世界位姿构造 `PerformancePlacement` 传给摆放重载，空 = 不传摆放 |
| `HideActorVisual` | `bool HideActorVisual { get; }` | 默认 `false`；为真时演出期间关掉触发者根下全部 `Renderer`，结束（含取消 / 异常）按原 `enabled` 值恢复 |

`Bind(IPerformanceService, ITelemetryScope)` 由 `PerformanceSceneBinder` 在场景加载时调用；运行时 `Instantiate` 出的触发器
要手动 `Bind`，否则 `TryFire` 只记 Warn 不生效，也不会被 `OnSceneStart` 扫到。

## `Game.Performance.PerformanceStage`（演出预制体根，给编写演出 / 编辑器工具的人看）

| 成员 | 签名 | 说明 |
| --- | --- | --- |
| `StageCamera` | `Camera StageCamera { get; }` | 舞台相机（透视 URP Base，不打 MainCamera 标签）；播放时接管画面，语义见 module-guide「世界舞台」 |
| `Cast` | `IReadOnlyList<PerformanceCastEntry> Cast { get; }` | 演员名单：`Speaker`（与字幕片段说话者严格相等）→ `Avatar`（Sprite）+ `Side`（`PerformanceAvatarSide`，头像在对白面板左槽还是右槽，按演员站位配，默认 `Left`） |
| `TryGetAvatar` | `bool TryGetAvatar(string speaker, out Sprite avatar, out PerformanceAvatarSide side)` | 严格相等匹配、重名取第一条；空串 / 未登记 / 该条头像为空返回 `false`（此时 `side = Left`） |
| `PerformanceAvatarSide` | 枚举 `{ Left, Right }` | 决定该说话者头像出现在对白面板左槽还是右槽 |

字幕输出端契约 `IPerformanceSubtitleSink.ShowSubtitle(string speaker, string text, Sprite avatar, PerformanceAvatarSide side)`（`avatar` 可为 `null`），
由字幕轨道混合器在片段切换边沿调用；实现者目前只有 `PerformanceView`，只显示说话者那一侧的头像 + 框，另一侧隐藏。

## `[PerformanceId]`（`Game.Performance.PerformanceIdAttribute`）

标在 `string` 字段上，声明该字段的值是一个演出 id（Addressables `Performance` 组地址）。运行时无行为，只影响
`Game.Editor.Performance.PerformanceIdDrawer` 在 Inspector 里把它画成下拉框而不是文本框。`PerformanceTrigger.performanceId` 已用它。

## 对白 JSON 的 `performance` 字段（跨模块协议，属于 Dialogue 的表结构，这里只说 Performance 这一侧的契约）

`Tables/Defines/dialogue.xml` 的 `Node.performance`（string，空串 = 不插播，**JSON 必须显式写 `""`**）经
`DialogueCatalog` 翻译成 `DialogueContent.Node.PerformanceId`；`DialogueController` 在摆台词前会
`await IPerformanceService.PlayAsync(id, ct)`。Performance 侧对这个字段没有额外校验——地址不存在时
`PlayAsync` 抛异常，由 `DialogueController` 捕获记 `performance_failed` 并继续显示台词，不影响对白流程。

## 调用时机与前置条件

- 需要 Boot 根作用域已建好（`PerformanceInstaller` 已注册）——直接 Play 玩法场景没有 `IPerformanceService`。
- `HasPlayed` / `IsRunning` / `CurrentId` 随时可查，不需要等某个事件。
- `Confirm()` / `Skip()` 在没有演出播放（`!IsRunning`）或阶段不匹配时静默无效，不抛异常。

## 禁止事项

- **不要绕过 `PlayAsync` 直接实例化演出预制体或调 `PerformanceStage.Play()`**：会跳过重入保护、时停、输入图切换、
  摆放与舞台相机接管、HUD 隐藏、存档记录与事件广播。
- **不要缓存 `PerformanceResult` 之外的内部状态**：`PerformanceRules` / `PerformanceStage` 不对外暴露，查进度只能通过
  `IsRunning` / `CurrentId`；没有「查询当前阶段 / 长按进度」的公开接口给外部用（这些是表现细节，只在服务内部消费）。
- **不要在别的模块里假设 `PlayAsync` 会在某一帧内返回**：它会挂起到演出真正结束（可能几秒到十几秒），调用方必须能容忍
  这段时间被挂起（Dialogue 的做法是 `Performing = true` 让位，不阻塞整个游戏）。
- **不要跨模块引用 `Game.Performance.Timeline` 里的轨道 / 片段类型**：那些只在 Timeline 图内部运行，外部消费点只有
  `IPerformanceService` 与 `PerformanceTrigger`。
