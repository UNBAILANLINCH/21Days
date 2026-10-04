# 可合入模块文档片段

以下仅供主窗口增量合入；以 source-snapshot.md 为源码基线，合入前重新核对行号。当前未改 ai-docs。

## rhythm-external-api.md：追加“外部演奏与结果”

所有类型位于 `Game.Rhythm`。这些是独立音游状态的扩展契约，尚未证明正式 Boot 或世界乐师入口已接入。

| 接口 | 调用契约与来源 |
| --- | --- |
| `RhythmPlayRequest(string runId, string contextId, string songId, RhythmPlayMode mode)` | 三个非空字符串与合法枚举；一轮一 ID；`Assets/_Project/Scripts/Runtime/Rhythm/RhythmPlayRequest.cs:8` |
| `IRhythmEntryPermission.Capture(string contextId)` | 同步只读捕获，返回 `RhythmEntryAccess(IsContextValid, CanAccessLibrary, CanPerform)`；真实职业与场景有效期由调用方定义；`Assets/_Project/Scripts/Runtime/Rhythm/IRhythmEntryPermission.cs:6` |
| `RhythmExternalSession(permission, combatPolicy = null, onSuccess = null, onFailure = null, onAborted = null, onTechnicalError = null)` | Combat 必须提供成功标准；未定的失败效果保持 null；`Assets/_Project/Scripts/Runtime/Rhythm/RhythmExternalSession.cs:18` |
| `CanAccessLibrary(contextId)` / `TryBegin(request, out reason)` | 入口及实际开始重新验权，开始成功占用 runId；当前 CanPerform 只限制 Combat；`Assets/_Project/Scripts/Runtime/Rhythm/RhythmExternalSession.cs:32`、`:39` |
| `Invalidate()` / `Consume(result)` | 撤销 active；身份匹配才消费，先封口再验权/回调。过期、重复、run/context/song/mode 不符拒绝；`Assets/_Project/Scripts/Runtime/Rhythm/RhythmExternalSession.cs:60`、`:62` |
| `RhythmState.ConfigureExternal(request, consumer)` | 非播放/准备时调用；拒绝 Practice，检查曲库入口。State 实际开始时自行 TryBegin，调用方不要提前占用；`Assets/_Project/Scripts/Runtime/Rhythm/RhythmState.cs:78` |
| `LastRunResult` / `OnRunFinished` | 上轮快照与观察事件；State 已调用 consumer，观察事件不得成为第二条战斗副作用入口；`Assets/_Project/Scripts/Runtime/Rhythm/RhythmState.cs:76` |
| `IRhythmCombatPolicy.IsSuccess(RhythmRunResult)` | 仅 Combat + Completed 进入策略。策略须核对上下文允许的曲谱及规则版本；接口未定义伤害数值；`Assets/_Project/Scripts/Runtime/Rhythm/IRhythmCombatPolicy.cs:6` |

`RhythmRunResult` 为不可变快照：RunId/ContextId/Mode/Completion/EndReason、SongId/ChartId/Revision/RulesetId/ScoringVersion、NoteCount/Perfect/Good/Miss/CompletedHolds/Score/MaxCombo/OffsetMs；Resolved 是三种判定之和。Completed 必须全音符结算；Aborted/TechnicalError 不执行战斗成功标准（`Assets/_Project/Scripts/Runtime/Rhythm/RhythmRunResult.cs:12`、`:44`）。Completed 不等于战斗成功，也不等于 FreePlay 达标。

`RhythmProgressRules.RecordRun` 只写 FreePlay + Completed，五段版本键核对后保存整局最佳/最近纪录并按 runId 去重；Combat 与 Practice 不混入个人档案（`Assets/_Project/Scripts/Runtime/Rhythm/RhythmProgressRules.cs:65`）。外部 consumer 的已用 ID 集合是实例内存，不提供跨进程战斗结算去重。

## rhythm-module-guide.md：追加“未来世界接线边界”

独立音游保留世界乐师入口所需的权限捕获、外部请求及一次性结果消费接口。真实适配器、职业映射、场景代次和返回调用方路径尚未接入。驯服乐师开放曲库；世界场景演奏还须当前控制该乐师。现有 CanPerform 只限制 Combat，外部 FreePlay 如用于世界演奏须补足调用方门禁。

`RhythmState.EnterAsync` 持有世界暂停令牌并禁用 Gameplay，退出释放自身令牌（`Assets/_Project/Scripts/Runtime/Rhythm/RhythmState.cs:155`、`:759`）。令牌冻结逻辑 tick，当前不能据此支持演奏期间敌人继续行动。非正面战斗是否继续运行世界留待正式玩法确定，不更改当前音频/输入时间域。Boot 接线、预制体迁移与战斗伤害/失败效果仍后置。

## taming-external-api.md：追加“乐师调用方边界”

`TamingRules.IsTargetTamed(id)`、`CanControl(id)`、`CurrentControlId` 可供调用方组合乐师权限；`OnControlChanged` 通知新的 stableId，调用方负责退订（`Assets/_Project/Scripts/Runtime/Taming/TamingRules.cs:35`、`:40`、`:69`）。IsTamed 仅表示首个目标，IsControllingEnemy 仅表示非玩家，均不识别乐师。

`TamingActor.StableId` 是实例身份；`OnAvailabilityChanged(Action<TamingActor,bool>)` 表示组件启用/禁用，不是驯服或职业变化（`Assets/_Project/Scripts/Runtime/Taming/TamingActor.cs:20`、`:24`）。当前没有乐师职业字段或驯服变化事件；入口、开始和结果消费应重读真实状态。界面选择仍走 EncounterSceneView.RequestControl 排队，不用 TryControl 绕过逻辑输入路径。调用方不得因为某角色被驯服就自动赋予乐师职业。
