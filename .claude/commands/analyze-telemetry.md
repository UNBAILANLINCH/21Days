---
description: 埋点日志根因定位 — 机器聚合出摘要，提假设，定向回读验证，出诊断报告
argument-hint: [--source editor|player|<路径>] [--module <模块>] [--last N]
---

# /analyze-telemetry — 从日志里倒推出当时发生了什么

参数：**$ARGUMENTS**（都可省略，默认 `--source editor --last 3`）

按 `.claude/skills/telemetry/SKILL.md` 执行，那份是分析纪律，**先读它再动手**。
日志格式与字段的契约在 `docs/telemetry.md`。

## 流程（四步，别跳）

1. `analyze.py sources` —— 看这台机器上有哪些日志、是不是本工程的。
2. `analyze.py summarize --source <源> [--module ...] [--last N]` —— 拿结构化摘要（200 行以内）。
3. 基于摘要提 **2～3 个根因假设**，每个都写清「什么证据能证伪它」。
4. `analyze.py query --session <sid> --around <序号> --window 30` —— 定向回读验证 / 证伪，
   然后出报告落 `Logs/telemetry/<时间戳>.md`：现象 / 证据（带 sid + 序号）/ 根因（含置信度）/ 验证方法 / 建议改法。

## 三条硬的

- **不许把原始日志整份读进上下文**（Read / cat / grep 打开 `Editor.log` 一律不行，本机实测 632 MB）。要细节只能用 `query`。
- **相关不等于因果**：错误前序只是嫌疑人，结论要带置信度和验证方法。查不出来就明说「缺什么数据、该补哪个埋点、补在哪个文件」，别硬猜一个根因。
- **只诊断，不改代码**。改动走 `/dev`，埋点补全走 `/instrument-module <模块>`。只写 `Logs/telemetry/`，不碰工程别处。
