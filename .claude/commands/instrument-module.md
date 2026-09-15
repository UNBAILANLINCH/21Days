---
description: 给玩法模块补齐埋点 — 扫候选点、逐条判断、用 Edit 补、跑测试、列清单待审
argument-hint: <模块名>
---

# /instrument-module — 把该埋的点补上

模块：**$ARGUMENTS**（PascalCase，对应 `Assets/_Project/Scripts/Runtime/<模块>/`）

按 `.claude/skills/instrument-module/SKILL.md` 执行，**先读它再动手**。
该埋哪些点的尺子在 `docs/telemetry.md` 第 2.2 节（意图入口 / 状态迁移 / 失败分支 / 长耗时），
日志格式与属性键的契约在同一份文档第 1 节。

## 流程（五步）

1. `python .claude/skills/instrument-module/scan.py <模块>` —— 拿候选点清单（**线索，不是判决**）。
2. 逐条判断该不该埋。判据一句话：**出了事故，这条事件能帮我回答什么问题？答不上来就别埋。**
3. 用 **Edit** 补埋点代码（脚本只定位，不改 `.cs`）。保存时 project-lint 自动跑，零违规才算过。
4. 再扫一遍确认覆盖到了；跑 `/unity-test EditMode`（构造函数加了参数，测试一定要跟着改）。
5. 按 SKILL 第 7 节的格式列清单，**停下等审，绝不提交**。

## 三条硬的

- **宁可少埋也不要埋成流水账**。每帧触发的东西一律不埋（`docs/telemetry.md` 2.2「不该埋」），
  需要每帧数据用 `core.perf` 的采样。框架已经埋过的（状态进出、资源、UI、存档、Unity 报错）不重复埋。
- **失败比成功值钱**：成功路径只埋入口一条，失败分支每个都埋，并把**判定用到的数值**写进 `p`。
- **规则类不能因为埋点变得不纯**：注入 `ITelemetryScope` 接口、由 Installer 工厂式喂进去，
  不碰 `UnityEngine.Time`、不用静态门面、不吞异常。写法见 SKILL 第 4 节，**这条是硬架构约束**。
