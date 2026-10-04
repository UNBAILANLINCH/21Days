# 乐师与音游外部契约核对

本目录只提供契约说明及可合入文档片段；不改业务代码、共享 ai-docs/HANDOVER、Unity、Git 或用户存档。目录日期沿用派单命名，实际源码核对时间为 **2026-10-04 17:24:50 UTC**。运行时代码仍由主窗口修改，合入前必须复核 [快照](source-snapshot.md) 的 hash 与行号。

## 供主实现立即使用的事实

- `TamingRules.IsTargetTamed(id)`、`CurrentControlId`、`CanControl(id)` 与 `OnControlChanged` 可复用。`IsTamed` 只查首个目标，`IsControllingEnemy` 只判断非玩家；两者均不能证明乐师资格。
- `TamingActor.StableId` 是已保存的实例身份，不是职业 ID。没有公开的乐师类型、乐师职业字段、职业映射或驯服变化事件。`CharacterPuppet` 仅表现，不承担控制或身份权限。
- `IRhythmEntryPermission.Capture(contextId)` 返回三个 bool 的 `RhythmEntryAccess`；运行时未发现其实现，只有测试 fixture。真实权限适配器和乐师 ID 映射仍未接入。
- `RhythmExternalSession` 已做入口/结果再次验权，按 runId/contextId/songId/mode 匹配，身份匹配后先封口，再执行策略及回调。Combat 只在 Completed 时调用 `IRhythmCombatPolicy.IsSuccess`。
- **当前 CanPerform 仅限制 Combat**。如果外部 FreePlay 也表示世界中的乐师演奏，调用方必须另外强制当前控制乐师，或主实现将该门禁扩到对应入口；不能写“所有模式已强制控制乐师”。独立曲库 FreePlay 的个人练习权限应与场景演奏区分。
- 当前结果消费不核对 chart/revision/ruleset/scoringVersion 与请求，因为请求没有这些字段。战斗策略必须按当前上下文允许的曲谱版本核对，再评估成功；不能把合法统计直接视为合法战斗结果。
- 失败 hook 保持 `null`，不指定掉血、战败、资源消耗或敌人反击。成功 hook 也没有确定伤害数值；目前不能宣称伤害已完成。

## 最小调用顺序与责任

1. 调用方从正式 `EncounterStep.Taming` 取得角色身份与状态。作者或玩法调用方明确指定乐师 stableId；音游不猜测显示名、数组首位或小人外观。
2. 调用方建立 contextId → 当前场景代次/角色 stableId/可选战斗身份的只读映射。`EncounterId`、`ActivationId` 是可复用的 long 战斗身份；不是现成 string contextId。探索无战斗时可能都是零，须另有场景激活代次。重载、退出、换档、新游戏使旧 context 永久失效，不能仅按场景名构造。
3. 实现 `Capture`：`IsContextValid` 查当前映射有效期；`CanAccessLibrary` 查指定乐师独立驯服状态；`CanPerform` 查其仍可控制且 `CurrentControlId == musicianId`。这些组合是待接线契约，不是当前已存在的实现。长期跨场景保留曲库解锁需读取现有存档归属或已有规则状态，不能持有卸载场景的 Transform。
4. 查询 `CanAccessLibrary` 后展示入口。角色控制变更订阅 `OnControlChanged`；退出退订。它是刷新提示，不替代 Capture；驯服可能只调用 TryTame 而不改变控制，没有对应事件，须在入口刷新/开始/结果消费时重读。
5. 为每次演奏生成唯一 runId；构造 `RhythmPlayRequest(runId, contextId, songId, mode)`。Combat 提供真实 `IRhythmCombatPolicy`，失败 hook 留空；将 request 与 consumer 交给 `RhythmState.ConfigureExternal`，然后按框架进入状态。不要预先调用 `TryBegin`：当前 State 在实际启动音频前负责调用它，提前调用会导致 `active_run` 拒绝。
6. State 选定请求曲目、完成准备，在开始时重新验权。ConfigureExternal 只检查曲库权限，既不占用 runId，也不代表已经开始；实际 StartRound 才占用。重试必须配置新的 request/runId，当前外部请求本身不能重复使用。
7. 自然曲末待全部音符和尾窗结算后发布 Completed。State 已调用 consumer.Consume；`OnRunFinished` 是观察入口，订阅方不得再独立结算同一份战斗效果。Aborted 与 TechnicalError 不进入成功标准或失败惩罚。
8. 调用方在卸载/取消/角色资格丧失时让 context 失效并 `Invalidate()`。结果迟到、身份不符、重复消费都拒绝。切换 away 再切回如果要使旧轮永久过期，应提升 context 代次或显式 Invalidate；只在曲末检查当前 bool 无法证明整轮持续控制。

## 去重与存档边界

`RhythmExternalSession.usedRunIds` 在 TryBegin 成功时登记，并仅在该 consumer 实例内保留；Invalidate 不清除已用 ID，Consume 无 active 时拒绝。权限/策略/回调抛错后也不能重放这轮。新建 consumer 后该内存集合不会跨实例或跨进程去重，未来不可逆战斗副作用需由调用方按 runId + context 身份保存结算凭据；本轮不新增持久战斗账本。

FreePlay 的 `RhythmProgressRules.RecordRun` 仅接受 Completed，校验五段版本键、音符数量与 Hold 数量，按 ProcessedRunIds 去重。保留整轮最佳结果，同分保留先前局，最近完成局单独保存。Combat/Practice、中断/技术失败均不成为个人纪录。旧 BestScores 是兼容显示数据，不能补造完整判定统计或作为战斗完成凭据。

## 暂停与将来非正面战斗

当前 `RhythmState.EnterAsync` 禁用 Gameplay Action Map 并获取 `IWorldPauseService` 令牌；它冻结 `Time.timeScale` 和 `SimulationRunner`，退出 finally 释放自己的令牌。只停止音频一轮或显示曲库不会自动解除世界暂停。其他持有者仍可让世界继续暂停。

如果未来设计要求非正面演奏期间敌人继续行动，现有整段状态持有暂停令牌的方式会阻止该行为；非正面战斗本身并不必然要求实时世界，两者不能提前划等号。本轮保留权限/结果契约，不决定世界运行方式、不新增世界模式枚举、不绕过暂停服务、不改 DSP/输入时间域。正式迁移时再决定是暂停世界的状态切换，还是保留世界的叠加演奏，并对应验收敌人逻辑、输入与结算时序。

## 迁移到 Boot 前待办与审核点

- 实现真实乐师 stableId 映射、权限适配器、context 代次和生命周期退订；禁止用测试 fixture 代替真实接线。
- 决定外部 FreePlay 场景演奏的控制门禁，以及控制中途丧失是否立即中断并永久失效旧请求。
- 核对状态切换是否调用 `EncounterStep.End` 或卸载原世界：若 Capture 直接要求 `IsActive`，离开世界进入 Rhythm 会令其失效。保留场景、快照还是桥接身份必须由正式接线解决，不能简单删除有效期检查。
- 确定外部结束返回调用方的路径。当前 Rhythm 的 Back 走 TitleState；没有证明可返回原战斗。预制体迁移、Boot Installer/Addressables 接线仍后置。
- Combat 策略核对允许的 song/chart/revision/ruleset/scoringVersion，再按战斗标准评估；不自动复用 FreePlay 的通过比例。
- 当前 `EncounterStep.Step` 在首个目标已驯服时会 AbortBattle；不能直接把现有巡逻遭遇战斗当作乐师战斗流程。使用现有 ID 不等于复用现有胜负状态机。
- 未来确定伤害与失败表现后再接回调。回调重入/异常、过期结果、换档、重试、控制改变、正常 Completed、Aborted/TechnicalError、Combat 不写个人纪录均需隔离存档验证。
- 重新读取主代码并复核本目录所有行号；合入 [文档片段](merge-fragments.md) 时不覆盖现有历史验证记录。没有本轮 Unity 编译、测试或 Boot 验收结论。

本会话未取得 hooks 已信任且运行的证据；不宣称自动检查启用。仅新增 PRP Markdown，无新 Unity 资产或 C#，不需要生成 .meta。

## 实际交付检查

三份产物已落盘回读；相对链接检查没有失效项，未发现本机绝对路径或 TODO/FIXME。没有操作 Git，未获取全工作区 diff；本任务写入仅为本目录三份 Markdown，apply_patch 内容可直接审查。

`gc_scan.py` 实际运行，exit 1：工程静态不变量报告四项——Gameplay 下 FaceBlendShapeController.cs、FaceDragHandle.cs、MuralFaceController.cs 的 Game.LailaFace 命名空间与目录不符，以及 Dynamic 字体 `Assets/_Project/Art/Fonts/Font_NotoSansSC_Regular SDF.asset` 35005 KB。这些文件未由本任务修改，不扩大范围修复。扫描没有报告 Markdown 链接、模块文档目录或钩子脚本引用错误。默认 uv 查找和项目 venv 受解释器路径限制失败后，以已安装解释器运行成功，不把首次失败当成扫描通过。
