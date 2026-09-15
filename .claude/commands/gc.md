---
description: 进化层 — 扫描 harness 健康度，报告失效引用 / 缺失路径
argument-hint: (无参数)
---

# /gc — Harness 健康度扫描

找出 harness 内部的失效引用：markdown 相对链接断裂、`modules.json` 登记的文档目录缺失、`settings.json` 里注册的钩子脚本缺失，外加跑一遍钩子自测（钩子坏了不报错，只会悄悄不生效）与一遍工程静态不变量扫描（asmdef 依赖方向、平台宏、命名空间、`.meta` 配对、UI 面板地址——这些破了也不报错，只在运行时静默失效）。

## 步骤

1. 运行：

   ```bash
   python .claude/skills/evolution/gc_scan.py
   ```

2. 解读退出码：
   - **exit 0** —— 打印「健康度扫描通过」，无失效引用，到此为止。
   - **exit 1** —— 打印失效项列表，逐项修复：
     - 链接断裂 → 补上目标文件，或改链接指向真实路径（别删链接了事）。
     - `modules.json` 里的文档目录不存在 → 跑 `/generate-doc <模块>` 生成，或把该模块状态改回 `todo`。
     - `settings.json` 引用的钩子脚本不存在 → 补脚本，或从 `settings.json` 注销该钩子。
     - 钩子自测失败 → 单跑 `python .claude/hooks/tests/run.py` 看完整报错；是钩子坏了就修钩子，
       是判据有意改了就同步改用例（别直接删用例了事，删掉的那条正是下次没人发现的故障）。
     - 工程静态不变量违规 → 单跑 `python .claude/skills/evolution/invariants.py` 看完整清单，
       每条自带「现象 / 改法 / 依据」，按改法修。**若判定是误报**（判据够不着真实结构），
       改 `invariants.py` 里那条的判据或直接删掉它，不要加白名单——白名单攒起来以后没人敢动。
   - 修完**重跑一次**确认 exit 0。
3. 失效项很多时，别逐个糊：结合 `.claude/skills/evolution/SKILL.md` 的「走歪信号」排查是不是最近某次 harness 改动引入的退化——先看 `git status` / `git log` 定位改动，必要时 `git revert` 回退那一次，而不是在坏结构上打补丁。

## 何时跑

- 改动了 `.claude/rules/`、`.claude/commands/`、`.claude/hooks/`、`ai-docs/` 的**结构或文件名**之后（重命名、移动、新建、删除）。
- 跑完 `/learn`、`/generate-doc` 之后（这两个会新增文档和交叉引用）。
- 定期体检；或感觉 harness「不对劲」时（钩子不提示了、文档链接点不开、lint 频繁误报）。
- `/review-change` 前，只要本次改过 `.claude/` 或 `ai-docs/`。

## 注意

- 第 5 项（`invariants.py`）只查**跨文件、跨资产**的约束——单文件正则判得了的归 `project-lint`，两边不重复。它纯读文本，**不需要 Unity 编辑器开着**。
- 扫描是只读的，不会自动改文件；修复动作由你按上面的路由自己做。
- 「行为有没有退化」不归这里：那是 `/run-evals` 的事（见 `.claude/skills/run-evals/SKILL.md`）。`/gc` 只看当前仓库的静态自洽，不派 agent、不跑任务。
