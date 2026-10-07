# 源码快照与真实 API 表

核对时间：2026-10-04 17:24:50 UTC。SHA256 为当时读取文件，不是 Git 提交身份，不代表 Unity 编译状态。主代码并发变化后须重算；本目录未复制业务源码。

交付复核（17:29:07 UTC）发现 `RhythmState.cs` 已变化，当前 SHA256 为 `E612A4AA3B17D8444BBD538A184C242A6DC270AC8725ABD54A755EC52634062D`；ExternalSession hash 保持。最新 State 的 Completed 构造在 :399、RecordRun 在 :404、CompleteRun 在 :452、PublishPendingResult 在 :466；ConfigureExternal :78、暂停 Acquire :157、Release :759 保持。下面表格保留首次快照行号，不能当作原子一致的最终主代码版本。

以下路径前缀为 `Assets/_Project/Scripts/Runtime/`。

| 文件 | SHA256 |
| --- | --- |
| Taming/TamingRules.cs | CEB2105EB6005F5123C2D5F856F27ECB56FE0D693DB04B5F5AC0112375BDC782 |
| Taming/TamingActor.cs | 0DD9351F82DAF8C10DF1955F86AF6D6A4E0E7E662E7D32C81A8C2AE23BD8293E |
| Monster/EncounterStep.cs | FE631F9D1A6D757C4DEB33C019011DF5AF057FC5B563C8792CA0FEC97E28E7E0 |
| Monster/EncounterSceneView.cs | 9A08333921F6F50137E09F03A356C8E43981161BD78962B1A4B722BDA2E06883 |
| Rhythm/RhythmState.cs | CB0F8173D97109AA6D28C58B6C4359BB38CBF16DB17EA2C42C8B045CD1C46903 |
| Rhythm/RhythmExternalSession.cs | 38742F45B4E7AB3693901FF4C1F105F003C89DAB041F24EDE4CDB21AF1221F7A |
| Rhythm/RhythmPlayRequest.cs | 7FDDDDED4D7A5090703EC5F3C77BA5DDC3BABA6F1F881A02D1130DA4DEBC440D |
| Rhythm/RhythmRunResult.cs | 52DC764520BF28C7D279B917F88BBEF0B74AB7C39F29FB445678EE8D6A549DE6 |
| Rhythm/IRhythmEntryPermission.cs | 884903E59CAEFA63CB4336C9EF3CD2CD4C3C885B4A0245331523FD3005922C8E |
| Rhythm/RhythmEntryAccess.cs | DC0E67D7A3E48E485240879537E9F369507FF83BD6D311CBB80E54CD8BAB39D7 |
| Rhythm/IRhythmCombatPolicy.cs | BF425B5F7DF2E4ECBC2D56B541402BB306676D65CC0434F4187B752CA02809C8 |
| Rhythm/RhythmProgressRules.cs | 70DA053AE3A10CDD00CE231605F42158E2D33551E78433F5F9BEF13B9FEF2253 |

## 外部模块可复用 API

| 真实类型/成员 | 路径与行 | 可复用范围 / 限制 |
| --- | --- | --- |
| `Game.Taming.TamingActor.StableId / DisplayName`（string） | Taming/TamingActor.cs:20 | 序列化身份；复制需新 ID；不代表职业 |
| `event Action<TamingActor,bool> OnAvailabilityChanged` | Taming/TamingActor.cs:24 | OnEnable/OnDisable 发布，可用性变化；不是驯服通知 |
| `TamingRules.PlayerId / CurrentControlId`（string） | Taming/TamingRules.cs:33、:35 | 当前控制身份；控制能力还要 CanControl |
| `IReadOnlyList<string> TargetIds` | Taming/TamingRules.cs:39 | 显式注册顺序；不能据顺序推断乐师 |
| `event Action<string> OnControlChanged` | Taming/TamingRules.cs:40 | 新 ID，重复选择不发；Reset/Restore 可能发布控制事件 |
| `MonsterRules GetTarget(string)` / `string GetDisplayName(string)` | Taming/TamingRules.cs:67、:68 | ID 查目标或空；读取 Model.Health 不写字段 |
| `bool IsTargetTamed(string)` / `IsAvailable(string)` / `CanControl(string)` | Taming/TamingRules.cs:69、:70、:71 | 独立驯服、注册且可用、存活与双方生命条件；player 特例恒可选 |
| `bool TryTame(string)` / `TryControl(string)` | Taming/TamingRules.cs:80、:88 | 同步规则 API；场景 UI 不直接用它替代命令队列 |
| `TamingTargetSaveData[] Capture()` / `Restore(saved, controlId, held)` | Taming/TamingRules.cs:140、:165 | 正式归属存档能力可复用；音游不维护第二份驯服状态 |
| `Game.Monster.EncounterStep.Taming / CurrentControlId` | Monster/EncounterStep.cs:27、:28 | 正式规则归属，不能再创建另一个 TamingRules 驱动同一角色 |
| `bool IsActive` / `long EncounterId / ActivationId` | Monster/EncounterStep.cs:50、:51、:52 | 活跃/战斗关联身份；零值不是有效战斗；没有现成 contextId |
| `StartBattle(long,long)` / `ConsumeResult(long,long)` | Monster/EncounterStep.cs:57、:67 | 既有遭遇胜负消费，不是 RhythmRunResult 接口 |
| `EncounterSceneView.PlayerIdentity / PatrolActors` | Monster/EncounterSceneView.cs:92、:93 | TamingActor / TamingActor[]；只在场景有效期读取 |
| `CurrentControlId / CurrentControlObject` | Monster/EncounterSceneView.cs:95、:96 | string / Transform；卸载后不得持有视图对象 |
| `bool RequestControl(string stableId, bool tame = false)` | Monster/EncounterSceneView.cs:108 | 校验后排队，下 tick 生效；返回 true 不等于已经切换 |

## Rhythm 新结构字段与行为

| 类型 | 成员与路径行 |
| --- | --- |
| `RhythmPlayMode` / `RhythmRunCompletion` | FreePlay/Combat/Practice 与 Completed/Aborted/TechnicalError；Rhythm/RhythmRunResult.cs:7、:8 |
| `RhythmPlayRequest` | RunId/ContextId/SongId（string）、Mode（RhythmPlayMode），Rhythm/RhythmPlayRequest.cs:20 |
| `RhythmRunResult` 身份 | RunId/ContextId/EndReason/SongId/ChartId/Revision/RulesetId/ScoringVersion（string），Mode、Completion；Rhythm/RhythmRunResult.cs:12 |
| `RhythmRunResult` 统计 | NoteCount/Perfect/Good/Miss/CompletedHolds/Score/MaxCombo（int）、OffsetMs（double）、Resolved（计算 int）；Rhythm/RhythmRunResult.cs:22 |
| `RhythmEntryAccess` | IsContextValid/CanAccessLibrary/CanPerform（只读 bool）；Rhythm/RhythmEntryAccess.cs:13 |
| `RhythmExternalSession.Active` | RhythmPlayRequest，只读；Rhythm/RhythmExternalSession.cs:30 |
| 拒绝原因 | active_run、used_run_id、missing_combat_policy、expired_context、not_tamed、not_controlling_performer；Rhythm/RhythmExternalSession.cs:39 |
| 结果匹配与封口 | run/context/song/mode 匹配后清 active，再 Capture；Rhythm/RhythmExternalSession.cs:62 |
| `RhythmState` 交付 | ConfigureExternal:78；实际 TryBegin:295；CompleteRun:451；PublishPendingResult:465；OnRunFinished:77 |
| 完成与个人存档 | Tick:399 构造 Completed；RecordRun:404（交付复核）；RhythmProgressRules.RecordRun:65 拒绝 Combat/Practice |

## 实查缺口

对 `Assets/_Project/Scripts/**/*.cs` 搜索 `: IRhythmEntryPermission` 和 `: IRhythmCombatPolicy`，只找到 `RhythmExternalSessionTests.cs` 和 `RhythmLibraryShowcase.cs` 的嵌套 fixture。没有查到真实 Runtime 适配器。职业/权限没有由 CharacterPuppet 补充：其公开 API 是表现状态、染色和朝向。

暂停公开契约在 `Assets/_Project/Scripts/Core/Timing/IWorldPauseService.cs:29`，`Acquire(object owner)` 在 :39；同 owner 复用令牌、Dispose 幂等、引用计数恢复。State Acquire 在 Rhythm/RhythmState.cs:157，Release 在 :759。它不是可选择“只禁用玩家、继续敌人”的接口。
