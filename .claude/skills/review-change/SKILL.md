---
name: review-change
description: 列出工作区改动清单等用户审核，授权后按规范提交
disable-model-invocation: true
---

# /review-change

1. **收集**：`git status --short`、`git diff`、`git diff --cached`、未跟踪文件。逐个判断是否在本次任务范围内，范围外的单独标出，不混进清单。
2. **自查**：改动里出现用户名、绝对路径、邮箱等本地标识就先改掉。新建的 `.cs` 还没有 `.meta`，说明 Unity 未刷新，提醒用户切回编辑器一下再提交。改过 `.claude/` 或 `ai-docs/` 时先跑 `python .claude/skills/evolution/gc_scan.py`，失效引用修完再列清单。
3. **列清单**：一张表，三列：文件路径 | 位置（类 / 方法或行范围）| 改了什么（一句话）。表下给拟用的完整提交信息（标题 + 正文），按 `docs/commit-convention.md` 写；清单里混了多类改动（feat 与 fix）就拆成多次提交，分别给信息。
4. **停下等授权**。「看过了」「流程没问题」都不是授权，只有用户明确说提交才进入下一步。
5. **提交**：`git add` 清单里的文件，用第 3 步给出的提交信息提交。不带任何 AI 署名或会话链接。不 push，除非用户明说。

## 并发会话

同一个仓库上同时开着另一个会话时，工作区里混着两家的改动，**整份 `git add` 会把对方没审过的内容一起提交**。
归属**按内容判，不按文件名判**（对方给服务加埋点会改到 `UIService.cs`、`developer-guide.md`、`pitfalls.md`，名字里都看不出来）。
混合文件的拆分办法（`git show HEAD:` 取基线 → 只重放自己的改动 → `hash-object` + `update-index` 单独入索引）
与提交前后的兜底检查，见 [`ai-docs/pitfalls.md`](../../../ai-docs/pitfalls.md) 里「两个会话共用一个工作区」一条。

完成标准：清单已列出并停下；或授权后提交完成并回报 commit hash。
