# 音游 UI 调整与验收（2026-10-06）

## 实施结果

曲库保留滚动歌曲列表，右侧突出选中曲详情、成绩与开始按钮。选曲加载保留页面，不再经过隐藏曲库的 Starting；成功后提交选中项。只有开始演奏才进入游戏。旧 HUD 在曲库中停用，页面背景完全不透明，避免透出旧按钮。

设置统一自动固定拍校准、教学曲手动试听、输入及视觉延迟；内部战斗测试与诊断默认折叠。设置值作为草稿，显式保存先写独立 Profile 快照，落盘成功才改内存；取消保留原值，失败可重试。沿用原候选 A/B 和可靠性算法。

手动试听使用完整教学曲音频与谱面，身份为 Practice，即使全部命中也不记录成绩或解锁。普通教学 FreePlay 的 60% 通关持久解锁不变，与自动校准成功无关。

演奏仅必要 HUD 与暂停；暂停冻结同局 Rules 和音频位置，恢复重建 DSP/Input 起点、清理旧输入并核对 Hold。暂停封存旧诊断为 AudioPaused，恢复后不拼接不兼容的时钟原点。结算隐藏旧设置/标题返回、游戏 HUD、判定线及按键标签，提供重试与返回选曲。

## 正确实例与真实验证

只读确认运行中的 PID 145320：路径 D:/software/unity/anzhuang/22.3.62f2/Editor/Unity.exe，实际版本 2022.3.62f2_7670c08855a9，projectPath 为 21Days。用户停止 Play 后，通过项目既有 PRP/rhythm-final-regression-fix-20261005/bridge.py 使用该实例。没有启动 Unity/服务、换版本/项目、停止用户 Play、修改 Packages/ProjectVersion/全局配置或删除缓存。

原生 Unity 编译完成。refresh 域重载时曾关闭 bridge 连接，原实例随后恢复；没有启动替代编辑器。此前报告仅离线验证、未找到工具的结论属于较早阶段，现已补上真实 Unity 证据。

| 运行 | 实际结果 | 证据 |
| --- | --- | --- |
| 最终曲库完整组 | **13/13**，80.63 秒 | job 331bd8fbe11c43aab6991a09b8dafad2；Logs/verify/rhythm/20261006-104306/report.md |
| 演奏完整组首轮 | **10/11** | job 1c0920885c434e4e9efa0bb14eb9bb4c；Logs/verify/rhythm/20261006-102726/report.md，旧全宽断言失败 |
| 演奏完整组第二轮 | **10/11**，旧 v1 fixture 不适配 v2 记录 | job 9401ca4e0b7c42b8ac05126df02ad324；Logs/verify/rhythm/20261006-104536/report.md，保留失败证据 |
| 演奏完整组第三轮 | **8/11**；三项受窗口失焦中止影响，保留失败证据 | job f4b0f6c39e48446c9ad3f617f1050300；Logs/verify/rhythm/20261006-110039/report.md，截图明确失焦中止 |
| 最终 Attention 定向复验 | **1/1**，229 命中、15 Hold、零 Miss，重试/切歌/存档恢复通过 | job 78702fa84f254645afef170906f92cfd；Logs/verify/rhythm/20261006-113506/report.md |
| 页面/选曲加载/四轨定向复验 | **3/3**；同批旧 Attention fixture 失败保留 | job 5cd4d8d8f251403fa911e48248b2cb5b；Logs/verify/rhythm/20261006-111423/report.md |
| EditMode 全 Rhythm | **195/195**，0.64 秒 | job d9b7f85220fd4eeb90e3098741fe6514；当前源码原生执行，不采用历史结果 |

最终曲库组包括：直接开始、草稿取消、满分 Practice 不计成绩/不解锁、真实音频暂停超过原预约终点、20 次暂停继续、Hold 保持及暂停释放、保存失败/慢写/重复点击、连续选曲与重入、20 首滚动曲库、持久记录、候选采用及退出等待。现场断言只有一个活动 RhythmView、没有嵌套 Canvas。

已查看实际 Showcase Game 帧及 Logs/verify/rhythm-ui-20261006/ 页面 PNG。后者由运行时 Canvas 按目标尺寸布局、Unity Camera 渲染，随后恢复 Canvas，不是缩放图片，不声称改变物理 GameView。覆盖 1227×710、1920×1080 和 1280×800。结算修后截图确认判定线和 D/F/J/K 消失。外部全局开发 Console 浮窗不属于 Rhythm 内部诊断面板，未修改其全局配置。

## 正式谱面来源核对

没有编辑正式谱面或音频，相关目录 diff 为空。工作区与 HEAD 的 blob 一致：

- HybeboyGuitar：5a8e9c47829fb856b13fc7990a687619c6a5c0a5。
- AttentionInstrumental：42882da36d57a175d0e0ed68fc5b6c072dbb45cd。

两份文本也与首次提交 503d601 完全一致。只计正式 notes：吉他 **164 枚/7 Hold**，Attention **229 枚/15 Hold**，运行时结算截图一致。本轮曾将另列四枚 practiceNotes 混入，误报 168/233，已纠正；旧谱面数量没有失效，生产资产没有变化。测试读取正式 CreateRules 数量和 Hold 数，仍要求完整命中、零 Miss、所有 Hold 成功、歌曲进度隔离、重试清零、存档重读；布局保留 TMP 不溢出及文字区域不越出行宽。

第二轮暴露旧用例使用 v1 BestScores fixture 和落盘断言，当前 v2 只在 Records 保存完整成绩。已仅修用例为 RecordRun/ReadCurrentRecord，检查落盘完整音符和 Hold，不修改生产资格规则。第三轮前置 fixture 恢复通过，但 Attention 于播放中窗口失焦，正确中止，不保存未完成成绩；不是 v2 保存失败，不关闭失焦保护。随后仅复验受影响项：页面、中断、加载选择和四轨通过；Attention 完成但旧 DSP 调度 fixture 出现四个 Hold Miss（225 命中、11/15 Hold）。改为生产输入域 PositionAtInputTime(realtime) 调度后单项 1/1，通过 229 命中、15/15 Hold、零 Miss及落盘恢复（job ea1f8194c9b44317a3209eb80924fc30，Logs/verify/rhythm/20261006-112426/report.md）。原偏移、判定窗口、谱面和断言均未放宽。释放记录显示 DSP 曾领先输入域达 35ms，超过旧 fixture 的 20ms 尾部余量；旧 DSP 一次对照也通过（job 4b402f297e3442b7a429bd35637e9821，Logs/verify/rhythm/20261006-112845/report.md），当次 DSP 落后输入域。原失败未记录逐音符 ID，不能追溯证明四个 Miss 的具体原因；保留这一限制并新增逐 Hold 释放/失败诊断。

## 审查与手动检查

### 用户报告日志浮层 1 error 的实际归因

在正确实例通过 read_console(action=get, types=error) 读到唯一条目：`rhythm/diagnostic_save_failed`，IOException，无法创建隔离路径 `showcase-saves/rhythm-DiagnosticExportButton_WritesUniqueFilesAndReportsFailure/blocked-export-root`，因为同名文件已存在。上下文 t=2593 ms、s=52、f=45471；日志没有 runId 字段，不伪造 runId。对应测试 job f4b0f6c39e48446c9ad3f617f1050300。

堆栈为 System.IO.FileSystem.CreateDirectory → System.IO.Directory.CreateDirectory → RhythmState.ExportDiagnostic（当时源码 764 行）。Showcase 在 330–334 行故意创建同名哨兵文件、注册 ExpectErrorLogs 后点击导出，随后恢复 SaveRootOverride。本轮 report 中导出用例的保留旧文件/内存、重试写入第四份文件、所有 RHD 回读均通过。因此该 1 error 是已核实的测试预期注入，不是未经查看就归为历史，也不是新增生产错误。没有清空 Console、删除 FAIL 或修改红色计数。

Unity 代码审查技能要求的独立只读审查 PASS，无 BLOCK；确认 Core 依赖边界、订阅释放、事务先落盘、暂停输入边界及新资产 meta。输入域 fixture 调度也经只读审查确认合理，无 BLOCK。

涉及 15 个 C# 文件的手动 project-lint 退出 0；目标 diff whitespace 检查退出 0。Codex hooks 未取得信任/运行证据，不能声称已启用。telemetry 扫描已有 33 点，新增选曲/开始、暂停/恢复及保存路径有对应事件，无新的关键缺失结论。

gc_scan 最终退出 1：仅三处已有 Gameplay/LailaFace 命名空间问题及动态字体 39138 KB 警告；未修改或清除协作者内容。目标 23 文件 diff whitespace 检查退出 0；全仓检查另有协作场景/字体 whitespace，不纳入本次提交。早期 Roslyn 编译和 PauseChecks 的 8 场景/16 断言仅为补充，不替代原生 Unity。

首次离线 .NET SDK 初始化曾输出开发 HTTPS 证书安装提示。只读核查当前 localhost ASP.NET 证书 NotBefore 为 2026-07-24，未发现于用户/机器受信 Root；没有会话前基线，不能断言本轮新增或未新增。没有信任、清除、导出或私钥操作；后续 SDK 禁用生成证书并隔离 CLI 目录。

## 提交边界

| 文件范围 | 位置 | 改动 |
| --- | --- | --- |
| Core/Audio/AudioPlayback.cs | Pause / Resume / ScheduleEnd | 支持冻结与继续音频时间轴 |
| Runtime/Rhythm/RhythmView.cs | 曲库 / 设置 / Finish | 整理导航、草稿、暂停及结算页面 |
| Runtime/Rhythm/RhythmState.cs | 设置保存 / 手动试听 / 暂停恢复 | 事务与同局路由，Practice 资格隔离 |
| Runtime/Rhythm/RhythmInputQueue.cs、RhythmDiagnosticSession.cs | 暂停 / 恢复 | 清旧输入，封存单起点诊断 |
| Runtime/Rhythm 校准及 ExternalSession / FixedChartCombatPolicy | 既有依赖 | 保留未提交的置信度分级和固定战斗谱资格实现 |
| Tests/EditMode/Rhythm 与 Tests/Showcase/Rhythm | 受影响用例 | 当前谱面、v2 存档、真实音频和 UI 回归 |
| ai-docs/docs/modules/rhythm 与本报告 | 接口 / 导航 / 验证 | 同步当前结果与限制 |

本地提交信息：`feat(rhythm): 整理曲库校准导航并支持同局暂停`。
新增可复用结论已写入模块文档和本报告：只统计正式 notes，不混入 practiceNotes；跨阶段验证必须按当前 v2 完整成绩身份构造 fixture，不从 legacy 分数字段读取当前纪录。没有新增未覆盖的关键埋点。

只纳入 Rhythm UI、必要 Core 音频支持、既有置信度/固定 Combat 依赖、相关测试和模块文档。保留所有其他协作修改，Laila 原暂存 blob 为 3a8b23f83ec672693975d3080a19ed043a8bd4b5。按后续用户授权追加本地提交，不 amend、不推送；若 hooks 拒绝则保留保护并报告手动操作。
