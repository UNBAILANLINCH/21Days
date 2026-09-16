---
description: 进化层 — 把本次会话的纠错 / 反馈 / 新约定沉淀成可复用资产
argument-hint: [要沉淀的教训，可留空让我从上下文提炼]
---

# /learn — 经验沉淀

待沉淀：**$ARGUMENTS**（留空则从近期对话与纠错中自行提炼）

先读 `.claude/skills/evolution/SKILL.md`。然后判断教训属于哪类，落到对应位置。

**两个入口，一份判据**：用户主动敲 `/learn`；或 `/review-change` 第 3 步沉淀门判出「本次有结论」后转到这里落笔。
沉淀门只负责「该不该停下来写」，**落到哪、怎么写只有下面这一份表**；门那一步已经写进去的不再重写一遍，收尾直接进它的清单待审。

## 沉淀路由表

| 教训类型 | 落到哪 | 例子 |
| --- | --- | --- |
| 项目层面的坑（现象 / 根因 / 正确做法） | `ai-docs/pitfalls.md` 追加一条 | 编辑器开着时 batchmode 跑测试必失败 |
| 高频、**可正则检测**的代码违规 | `.claude/skills/project-lint/rules.json` 加一条规则 | `Update` 里 `FindObjectsOfType`、`public` 序列化字段 |
| 用户偏好 / 工作方式纠正 | 全局记忆 `~/.claude/projects/<本项目>/memory/`（feedback 类） | 「提交前必须先列清单等审」 |
| 项目动态 / 决策背景 | 全局记忆（project 类） | 「玩法方向定为 X，放弃 Y 的原因」 |
| 框架契约 / 架构决策（接口约定、分层取舍、平台边界） | 对应的 `docs/*.md` 章节（多数是 `docs/architecture.md`） | 「UI 面板的 Addressables 地址等于类名」 |
| 某模块的内部知识更新 | `ai-docs/docs/modules/<模块>/` 或 `/generate-doc sync <模块>` | 模块新增了扩展点 |
| 通用 Unity / C# 规范的补充 | `.claude/rules/` 对应规则（csharp-code / unity-assets / unity-tests） | 新增一条命名约定 |

## 步骤

1. **提炼**：一句话现象 + 根因 + 正确做法。含糊的先想清楚再写，写不清就说明还没想明白。
2. **选位置写入**：
   - pitfalls 用既有格式（现象 → 根因 → 正确做法 → 关联），追加在文件末尾。
   - lint 规则给全 `id` / `rule` / `pattern` / `exclude_patterns?` / `confirm_patterns?` / `path_contains?` / `violation_tpl` / `fix` / `ref`，只改 JSON 不动引擎。
   - 规则文件改动要控制篇幅（glob 规则 < 150 行，常驻规则 < 120 行），写不下就下沉到 `ai-docs/`。
3. **加了 lint 规则就验证一次**：造一个正例（应报错）和一个反例（不应误报），跑
   `python .claude/skills/project-lint/lint.py <样例.cs>`，确认正例 exit 2、反例 exit 0。不验证不算沉淀完。
4. **改了 `.claude/` 或 `ai-docs/` 结构**（新建文件、重命名、调链接）→ 顺手跑一次
   `python .claude/skills/evolution/gc_scan.py`，别留失效引用。
5. **改了 `.claude/rules/` 或 `project-lint/rules.json` → 跑一次 `/run-evals`**，
   看这次沉淀有没有真的改变行为。`/gc` 只能证明规则文件还在、链接没断，
   证明不了它被读进去了——后者只有行为 eval 能答。
   改了 `rule_ref` 指向它的那几条用例就够，不必全量。
   **不跑也行，但要在汇报里写明为什么不用跑**（例：这次只改了 pitfalls，没动规则与 lint）。
   没写理由的「跳过」等于这道载体没接上，下次就没人跑了。
6. **报告**：沉淀到了哪几个文件、是否新增了 lint 规则、验证结果、eval 跑没跑（没跑写理由）。

## 原则

- **优先级：程序化（lint 规则）> 文档（pitfalls）> 记忆**。越靠前越「机制大于自觉」——能让钩子自动拦下的，就别指望下次记得。
- **只记非显然、会再踩的**。代码里读得到的、git log 里查得到的事实不重复记录。
- 一条教训只落一个地方。同时写 pitfalls 和 lint 规则时，pitfalls 里用 `关联:` 指向 lint 规则 id，不要两边各写一遍全文。
- 沉淀本身也是改动，仍然遵守「改完列清单等审，不擅自提交」。
