# Narrative 后续接线指南（2026-09-29）

本指南取代 2026-09-25 以前的对白 View/GameSessionController 接线步骤。
范围及验收以 [prp.md](prp.md) 第 1–5 节与 [tasks.md](tasks.md) 为准。

## 1. 先复用已存在的入口

| 需求 | 入口 | 禁止重复建设 |
| --- | --- | --- |
| 对白 | DialogueService.PlayAsync(int id, Transform anchor, CancellationToken ct) | 不自行 rules.Start/PresentAsync，不新建三槽 View/HistoryView |
| 条件 | IDialogueConditionSource.Snapshot(string targetId) | 安装 NarrativeInstaller 后使用真实源；兼容占位不代表世界事实，不能直接把 dialogue:id 当世界目标 |
| 规则 | NarrativeRules、EncounterRules、NarrativeCondition | 不再写第二套阶段机/表达式系统 |
| 任务事实 | QuestService.Report、QuestCompletedEvent、TryGet | 不在对白按钮里改 QuestProgress |
| 槽位 | GameSession、SessionStartedEvent、SaveTriggerBridge | 不建 GameSessionController/槽位 UI |
| 已读 | DialogueReadStore → dialogue-read 独立档案 | 不放到 Narrative/Session 槽位，不在新游戏清已读 |

源码事实：DialogueService 会持 IWorldPauseService 令牌、管理输入/HUD 和重入，正常返回 Outcome；
取消抛 OperationCanceledException，失败也会发 OnEnded。事件的 Completed 明确区分完成与清理；
不能仅按 Outcome 是否为空判成功。候选选择后捕获阶段身份，再 await；结果回传必须仍使用捕获的身份。

## 2. 数据与执行顺序

1. 先读既有 Tables/Defines/dialogue.xml、JSON 与生成脚本，复用 Luban 管线建最小 Narrative 内容。
2. 内容目录构造既有 NarrativeContent 与 EncounterRules.Rule；补跨目录引用验证。
3. 根注册纯规则/内容、控制器、事实来源；保持规则层纯 C#，只在协调层调用现有 Dialogue。
4. 场景主动交互/已存在事实入口生成 Candidate：稳定 TargetId、TriggerId、EntryEpoch 与当时快照。
   一次提交有效候选批次，沿用优先级和稳定目标 ID 仲裁；忙碌后重新查询，不囤旧事件。
5. Condition 用真实快照求值；Dialogue await 现有服务；WaitAction 等真实完成状态；End 结束或续接父等待。
   Battle 不自动开始；C5/G5 未定义前内容校验拒绝。
6. 所有接受的状态变化同步写 Narrative 分区，再发布变化/请求保存；取消/未知 Outcome 不写成功标记。
7. 任务完成只按配置映射调用 SetFlag；读档时核对 Quest 的当前持久完成状态，不能依赖旧 Completed 事件重发。

## 3. 条件源限制

Snapshot 在进入选项、unscaled 每 0.25 秒刷新、提交选项时被调用，必须廉价、无副作用。
读取当前槽位标记及 PlayerModel 的存活/潜行/伪装快照；尚未接入的目标敌意/感知不得配置成真实内容条件。
目标失效保守拒绝目标条件，不能沿用上一目标。

旧模块文档禁止任何 Narrative → Dialogue 引用的文字不能直接用来禁止控制器调用：
二者同在 Game.Runtime，合理边界是纯规则不认识表现、条件适配器不依赖控制器形成构造循环。
实际依赖与注册契约见 Narrative 模块三件套；Boot 挂接和实测状态以 tasks.md 为准。

## 4. 存档与恢复

- NarrativeSaveData 进槽位；DialogueReadStore 的独立档案跨局保留。
- 每次写回/重载重新 Get 分区，避免 LoadAsync/ResetAll 后缓存旧对象。
- SessionStartedEvent 在内存分区就位、进入玩法前发布；回调同步恢复规则、撤销旧代次，
  不在回调中 await。需要对白 UI/世界目标的续接等场景完成绑定后执行。
- 普通状态变化走 Session RequestSave 合并闸门；不直接 SaveAsync。
- Session 自动保存不会发生在对白/面板中；退出/离场 SaveNowAsync 是例外，必须验证其快照完整。
  本批先支持稳定等待/结束状态的完整快照，不保存异步栈或 Unity 对象。
  对白/切换中等无法完整恢复的状态必须阻止或延后保存并明确返回未保存，保留已有槽文件；
  不用整段重播替代全状态恢复，不宣称原 A10 所要求的节点/打字状态恢复已完成。
- ContinueAsync 在 Session 元数据检查后调用 Narrative 候选校验，再 Commit 同一份候选；
  内容未知或处于不支持恢复的阶段必须明确拒绝，不默默重置剧情。候选校验及稳定保存/继续已经本批测试覆盖，证据见 tasks.md。
- 现有 Session 没有完整场景回滚事务；Encounter 快照非法时会退回出生点。
  本批不能把此行为描述为“整个旧现场无损回滚”。

## 5. 内容检查与样例

必须校验重复 ID、空入口、失效出口、Condition 自动环、等待/对白无出口、非法/未接入事实、
对白 ID/任务 ID 不存在、遭遇 Story/Entry 不存在及最高优先级冲突。
优先复用 NarrativeContent、EncounterRules 与 DialogueCatalog 校验，不额外造校验框架。

最小样例只演示：交互对白 → 明确出口 → 等待一个现有任务完成 → 写标记 →
条件对白可用 → 保存/重载保持结果。用验证前缀区分样例与真实章节，
不决定第一章、对抗、画皮、收押或三结局的规则。

## 6. 验证与交付

先等第一批两个协作聊天完成且释放 Unity；读取结果并自己核对文件/报告。
再按项目规则实际编译、定向 Narrative/受影响模块测试、运行 Narrative Showcase，读取最终产物。
测试“已启动”不是通过；静态 gc/lint 也不是 Unity 验证。

Showcase 在共享 SampleScene 运行时搭建，清理自己创建的运行时对象；
不修改他人的场景/字体/URP/Laila，不动 Mirror 和另一批两个 Showcase。
Boot 若确需持久接线，先核对已有差异，再经 Unity 修改明确必要对象。
新资产 meta 由 Unity 生成。同步 Narrative 三件套，运行 gc_scan，检查 diff；
最终报告未验证部分及 HANDOVER/roadmap 建议更新，不自行修改这些共享文件。
