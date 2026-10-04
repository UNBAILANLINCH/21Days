# Rhythm 两项回归修复交付（2026-10-05）

## 已应用的范围

本轮仅修改现有 `RhythmState.cs`、`RhythmStateTests.cs`、`RhythmShowcase.cs`，并增量同步 HANDOVER / 模块指南。开始前已有 dirty 内容完整保留，Laila、asmdef、Boot、prefab、三份正式谱均未编辑。未新建 Assets 文件，无需新增 meta；现有三份 meta 已检查。未提交、操作 index、推送、构建或创建分支/worktree。

`applied.patch` 是相对于本轮进入时 dirty 内容的最小变更；`Scripts/` 保存最终候选源码。不是相对于 HEAD 的整个曲库增量。

## A：非法时钟不会先触发诊断异常

`CanContinue` 采样后进入私有 `ObserveClockSample`，先执行原 ClockGuard；拒绝时走原 `Interrupt` / `StopRound`，保留 InvalidClock 等明确 Reason，随后才允许记录有效 Bridge。没有 catch 全部异常，也未改变时钟容差。

离线控制组保留原记录顺序，只增加同名调用缝：NaN/∞ DSP、负采样 span 均在 SampleBridge 抛异常；修复候选不抛异常，取消准备、结束诊断并释放守卫。正常 0.25 秒记录节奏保持。实际 Showcase 另验证活跃音频句柄 disposed、输入动作禁用、末条 InvalidClock、正常重试。

## B：真实 InputSystem 丢事件机制已复现

旧 fixture 的 press 使用预定时间，release 使用墙钟；InputSystem 1.11.2 的 `InputManager.cs:3489` 按整台设备水位丢弃过旧事件。独立虚拟键盘/动作经过真实 InputSystem，使用同样的两次 1300/1900 ms 卡顿：旧序列收到 **26/32** 个 press，offset≈95、MAD≈40、分块差≈45，分类 Drift；仅把 release 改为 press+0.001 秒，收到 **32/32**，offset≈80、MAD≈45、分块差0，分类 Suggested。

完整 Runtime 主轮也实际收到 press=32、release=32、matched=32，invalid/outside/duplicate 全0，beat 索引0–31，分类 Suggested。报告保存每个实际 CallbackContext.time、原始歌曲时间、匹配 beat、计数；未放宽 MAD/drift 门槛，未修改 Estimator。release 与 press 单调；会话提前结束立即失败，finally 退订本轮观察回调。

该对照证明旧 fixture 的具体故障机制；历史 014637 报告未记录逐事件时间，不能反推当次被丢弃的确切拍号。

## 实际验证与保留失败

| 验证 | 实际结果 / 证据 |
| --- | --- |
| 离线 C# 候选及两份测试编译 | 0 warning / 0 error；`verify-pure.ps1`，36断言/0失败 |
| 离线原顺序控制组 | 4失败：NaN DSP、∞ DSP、负span诊断抛异常，失配样本先写入诊断；保留 `-Baseline` 可重跑 |
| 手动项目 lint | 三份实际 Assets C# exit=0；后续 Showcase fixture 修正也 exit=0 |
| 定向 EditMode | 35/35，0失败、0跳过；job `730201891cc94ac988be5f72d43c8c64` |
| 首轮六项 Showcase | 4通过/2失败；job `2915260eca3640a19c26493ffe5f170c`，保留 `Logs/verify/rhythm/20261005-030112/report.md` FAIL |
| 首轮通过项目 | 换曲保存等待取消/恢复、结果回调重入、非法采样音频/输入清理和重试、旧 Hold/校准生命周期 |
| 两项定向补验 | 2/2通过；job `6900df71745e47139ca1a87fac53e3c0`，137.084 秒；`Logs/verify/rhythm/20261005-031538/report.md` PASS，检查点失败0、运行时异常0 |

本轮六项均已有通过证据（4+2）；没有宣称一次六项全绿，也未重跑全仓或全部171项 EditMode。原失败 `Logs/verify/rhythm/20261005-014637/report.md` 完整保留；legacy 独立21/21属于历史证据，不混入当前统计。

首轮新增复现 fixture 在计划卡顿前被真正时钟守卫停止：telemetry `45d37fa6/s54` 为 clock_discontinuity；未中止发键导致逐拍超时并污染下一用例就绪检查。最终将设备水位对照独立于音频句柄，完整 Runtime 流程仍单独通过。未扩大时钟容差或隐藏中断。此处没有逐样本原始双时钟证据，不推断该瞬态硬件原因。

补验覆盖默认8+32、无输入固定结束、一次8拍补测、建议不自动保存、取消、明确应用保存、AB试听/失焦保留、漂移拒绝及重入恢复输入80/视觉125。已查看03建议截图与07重入截图，文字和数值符合断言；新增逐拍证据在回放顶部叠加层较长，不属于正式 UI。

## Unity 交还及剩余边界

完成后实际 `get_editor_state` 与只读 CodeDOM 查询：LailaRecognitionPlaytest、scene dirty=false、idle、非Play/非Pause、isCompiling=false、tests.is_running=false、timeScale=1、HoldScale=1；Console error=[]，无 InitTestScene 残留。锁已归还，可由父线程继续使用。原生 UnityMCP 未注册（实测 unknown MCP server），经本轮 `bridge.py` 调用既有本地 bridge 的相同工具/资源命令；未改连接配置。该客户端只手动运行，一次一个命令，不驻留或自动重试。

`gc_scan.py` 仍非零：既有 Gameplay 三处 LailaFace 命名空间不匹配、共享 Dynamic 字体缓存，共4处；本轮未处理。hooks 信任未确认，未宣称自动检查启用，已手动补 lint/gc。

物理声卡/显示器/键盘延迟、真人跟拍质量及三曲人工音乐贴合度尚未验收；未完成 Player/OS挂起/设备实际切换验收。这些不由确定性回放替代。
