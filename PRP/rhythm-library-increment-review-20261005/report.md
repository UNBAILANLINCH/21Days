# RhythmDemo 增量独立审查

结论：本次通知后的单次增量复验 PASS；原 legacy 可比性 P2 已修复，当前没有仍成立的 BLOCK/WARN。只读业务源码，未操作 Unity、Git、Assets、Packages、共享测试或玩家存档。

## 最新增量复验（父通知后执行一次）

真实源码重新编译成功，`RESULT 21 PASS`，退出码 0。原有 15 项保护断言全部保留并通过；原缺陷反例改为回归断言，并补充版本/持久化/回调检查。

- legacy 3500 仍存在并可独立查询；当前完整单局 best 为 3000，不再取 legacy 最大值。
- scoringVersion 改为 2：当前纪录为空、当前 best=0、legacy=3500；rulesetId 改变同样隔离当前 best。恢复原身份后原 3000 分局仍可读取。
- JSON 序列化/反序列化后，UnlockedSongs 保留；前置歌曲 revision 改为 3 后子曲仍开放。
- ExternalSession 成功回调抛异常后 active 已封口，重复结果与相同 runId 再启动均拒绝。
- State 回调保护为只读源码核对：自然结算在 StopRound、进度快照安排之后 PublishPendingResult；发布前清空 pendingRunResult/pendingResultConsumer，事件监听异常捕获。未宣称离线桩能替代 Unity State 生命周期实测。
- View 当前纪录使用当前 best，legacy 独立标识“无判定摘要”，旧 revision 历史单独展示。

本次快照：ProgressRules `1592B56CE81CA3C141229DAA09D8C6F580FB66883429970265BA4FFBD0049AFD`；ExternalSession `0E604620058852C6447A84ED6F3F92B9B859C0695EF922D622FEAF6C55BD2929`；State `C8589515BB6188FC4F69D61454CCEDB2F6CD8C64BB3D5ABA1DD3ABD49168CBFA`。Archive 与其余数据契约快照保持下文旧值。仅修改本审查目录，未触碰主实现 index。

以下保留首次审查历史，风险已由上述增量复验关闭。

## 可复现证据

项目根目录运行 `pwsh -NoProfile -File PRP/rhythm-library-increment-review-20261005/verify.ps1`。PowerShell 7 的 Roslyn Add-Type 编译真实 Progress/Record/RunResult/Catalog/Song/ExternalSession/Archive/ISaveData 源码；Unity 属性、SO 与四音符谱面资源为明确最小桩，不宣称 Unity 编译或真实谱面验证。Newtonsoft 使用当前 PowerShell 已加载程序集。每次输出 SHA256 快照，编译或断言失败返回非零。

首次实跑：`RESULT 16 PASS (includes confirmed comparability defect reproduction)`。其中 15 项保护行为通过，1 项是当时缺陷的行为复现。最新结果见上文。

覆盖：v1 best/clear 幂等迁移且不伪造统计、revision 保持子曲开放、完整自由局入档、重复拒绝、同分保留原 best 单局而 last 更新、Combat/Aborted 不入自由纪录、容器快照隔离、已驯服与当前控制分离、错误 runId 不封口当前局、Combat 只消费一次、自由局不调用战斗策略、Aborted/TechnicalError 不触发失败效果、上下文过期拒绝、坏 v2 DTO 在档案预检被拒绝。

## 风险排序

### 已修复 WARN / P2：未知 legacy 分数混入当前评分版本的“最佳”

`Assets/_Project/Scripts/Runtime/Rhythm/RhythmProgressRules.cs:59` 的 `GetBestScore` 在同 song/chart/revision 下把当前单局与 `BestScores` legacy 取最大值，legacy 键没有 rulesetId/scoringVersion。`RhythmState.cs:95` 与曲库条目仍调用该查询；因此改变评分身份后，没有当前可比记录仍能显示旧未知分数为最佳。

最小复现：v1 best=3500；先记录当前完整 3000 分局；把 SongData.scoringVersion 从 1 改成 2；`ReadCurrentRecord` 为 null，但 `GetBestScore` 返回 3500。脚本最后一项已经在真实规则上证实。详情页对 legacy 的“无判定摘要”标记正确，然而列表/结算聚合最佳仍会跨不可比评分身份。建议当前可比最佳与未知历史最高保持独立查询和标签；旧分数保留，不据此假造完整局。审查窗口未修改业务代码。

## 并发增量复查

早期读到 Archive 仅版本检查，曾提出坏 v2 被框架 fallback 当空档写回的疑点。当前源码已包含信封形状、DTO 反序列化及 Normalize 预检；fixture `profile-review-corrupt.json` 为 `BestScores: []`，真实 Archive 现在直接抛 JsonException，故该问题已消除，不列当前风险。

早期 CompleteRun 直接调用回调，存在结算中重入疑点。最终 State 已新增 PublishPendingResult，并在自然结算、停止与保存快照安排之后发布；不继续把早期顺序问题报告为现存缺陷。这里是源码增量核对，未在 Unity 实测回调重入。

核心最终快照：ProgressRules `70DA053AE3A10CDD00CE231605F42158E2D33551E78433F5F9BEF13B9FEF2253`；ProgressArchive `483B21D83ED1E4A6964FBB8223371BFEAEADBA7F0685E4FD33EFB4766A44BC60`；ExternalSession `38742F45B4E7AB3693901FF4C1F105F003C89DAB041F24EDE4CDB21AF1221F7A`。主实现仍在改 State，最终整合后需重跑此脚本并完成独占 Unity 验证。

## 边界与剩余验证

未验证真实框架异步读写/慢写/失败注入、UI 滚动视觉与实际输入、Unity 编译、真实谱面 Hold 数量、OS 存档恢复。这些由主实现独占 Unity 的整合验收覆盖。未定义伤害、失败惩罚或职业身份。

gc_scan 未完成：实测 `uv python find` 默认 cache 初始化失败；改到临时 cache 后，managed Python 安装目录读取被拒绝。当前 PATH 中 python/python3 不可用。没有为此绕过权限。

本窗口未审查或确认 Codex hooks 信任状态；不宣称 hooks 已启用。业务源码未修改，不以本窗口结果替代主实现的 C# lint/Unity 测试。
