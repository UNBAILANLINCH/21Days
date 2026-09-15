---
description: 进化层 — 扫描 harness 健康度，报告失效引用 / 缺失路径
argument-hint: (无参数)
---

# /gc — Harness 健康度扫描

找出 harness 内部的失效引用：markdown 相对链接断裂、`modules.json` 登记的文档目录缺失、`settings.json` 里注册的钩子脚本缺失。

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
   - 修完**重跑一次**确认 exit 0。
3. 失效项很多时，别逐个糊：结合 `.claude/skills/evolution/SKILL.md` 的「走歪信号」排查是不是最近某次 harness 改动引入的退化——先看 `git status` / `git log` 定位改动，必要时 `git revert` 回退那一次，而不是在坏结构上打补丁。

## 何时跑

- 改动了 `.claude/rules/`、`.claude/commands/`、`.claude/hooks/`、`ai-docs/` 的**结构或文件名**之后（重命名、移动、新建、删除）。
- 跑完 `/learn`、`/generate-doc` 之后（这两个会新增文档和交叉引用）。
- 定期体检；或感觉 harness「不对劲」时（钩子不提示了、文档链接点不开、lint 频繁误报）。
- `/review-change` 前，只要本次改过 `.claude/` 或 `ai-docs/`。

## 注意

- 这个扫描只查 harness 自身的引用完整性，**不查 Unity 工程**（`.meta` 配套、GUID 引用、asmdef 依赖由 Unity 编译和 `project-lint` 管）。
- 扫描是只读的，不会自动改文件；修复动作由你按上面的路由自己做。
