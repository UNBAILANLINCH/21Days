# Tasks：任务管线优化 + 任务编辑器

> 设计见 [`prp.md`](prp.md)。每项：**文件 → 做什么 → 产出 → model**。
> `.cs` / JSON 用 Write / Edit 写（不走 Bash heredoc）；本轮无场景 / 预制体改动。
> 主窗口对每波产物独立复核（`read_console` 零编译错误、`run_tests` 用例总数、`stat` 文件、`git diff`），subagent 自报不算数。

## 波次

| 波 | 任务 | model | 覆盖 PRP 验收 |
| --- | --- | --- | --- |
| W1 | T1 文件层 + 校验器 + 主线链（含 EditMode）· T2 Runtime 文案修正 | opus · sonnet | A1 A2 A5 A6 · A7 |
| W2 | T3 编辑器窗口 + 生成菜单扩展 + 校验菜单 | opus | A3 A4 A5 A6 |
| W3 | T4 文档同步 · T5 主窗口 MCP 验收 | sonnet · 主窗口 | A8 · A1–A6 |

---

## W1（T1 / T2 并行；文件不重叠）

- [x] **T1**（已交付 8 源 + 3 测试 55 条，EditMode 全量 555/555；出入见 prp.md 第 7 节）`Assets/_Project/Scripts/Editor/Quest/QuestTableFile.cs`、`QuestTableValidator.cs`、`QuestMainChain.cs`（命名空间 `Game.Editor.Quest`，按 PRP 3.1 / 3.2 契约；`QuestMainChain` 静态：`TryBuildChain(IReadOnlyList<QuestDraft>, List<QuestDraft> chain, out string reason)` 线性时返回 true 并按顺序填 chain，否则 false + 原因；`Move(List<QuestDraft> chain, int index, int delta)` 交换并**重写链上每条主线的前置** = 非主线前置 + 上一条主线 id；`SideUnlockLabel(QuestDraft, drafts)` 给左栏分组用）。`QuestTableValidator.CollectContext()`：对话编号 = `Tables/Data/dialogue/*.json` 文件名能解析成 int 的；地点键 = 扫 `Assets/**/*.unity` 文本，先用 `AssetDatabase.FindAssets("t:MonoScript QuestLocation")` 找到 `QuestLocation` 脚本 GUID，正则匹配 `m_Script: {fileID: 11500000, guid: <那个 guid>` 所在 MonoBehaviour 块里的 `locationKey: xxx`（照 `AssetAuditWindow.ScriptReferencePattern` 的写法），值 → 场景文件名（不含扩展名）；计数键 = `AssetDatabase.FindAssets("t:LootConfig")` 逐个 `LoadAssetAtPath<Game.Loot.LootConfig>` 读 `CrateQuestKey`（该类在工作区未提交的 `Runtime/Loot/LootConfig.cs`，已存在可引用）。`Assets/_Project/Scripts/Tests/EditMode/Game.Tests.EditMode.asmdef` 的 `references` 加 `"Game.Editor"`。测试 `Tests/EditMode/Quest/QuestTableFileTests.cs`、`QuestTableValidatorTests.cs`、`QuestMainChainTests.cs`（按 PRP 3.6；往返测试直接读 `Tables/Data/quest/*.json` 原文比对，路径由 `QuestTableFile.DataDirectory` 给）。每个新文件头写职责 + 「为什么新建」（复用 / 扩展为何不行）。完成后 `python .claude/skills/project-lint/lint.py <每个 .cs>` 零违规；MCP `refresh_unity(force, compile=request)` → `read_console` 零错误 → `run_tests(EditMode, test_names 含 Quest)` 全绿并报告用例总数。产出：3 源 + 3 测试 + asmdef 改动。（model: opus）
- [x] **T2**（5 处文案改动，Quest 测试仍绿）`Assets/_Project/Scripts/Runtime/Quest/QuestContent.cs`、`QuestCatalog.cs` — 按 PRP 3.5：目标序号文案改 1 起（`{i + 1}`），「不是角色 id 整数」改「对话编号「{key}」不是整数」；不改任何行为与异常类型。跑 `project-lint`；MCP 编译零错误；`run_tests` Quest 相关 EditMode 仍全绿（`QuestContentTests` / `QuestCatalogTests` 只断言含任务 id，应不需改）。**不碰文档**（文档归 T4）。产出：2 文件改动。（model: sonnet）

## W2（依赖 T1 的契约与编译通过）

- [x] **T3**（QuestEditorWindow + QuestKeyOptions；TryGenerate + 生成前校验 + 校验菜单；出入见 prp.md 第 7 节）`Assets/_Project/Scripts/Editor/Quest/QuestEditorWindow.cs`（IMGUI，PRP 3.3 全部行为；对话下拉的「第一句台词」读 `Tables/Data/dialogue/<id>.json` 的 `nodes[0]`，speaker 经 `dialogue_character.json` 映射 displayName，解析失败只显示编号；下拉的 Popup 用 `EditorGUILayout.Popup`，最后一项「手动输入…」；主线链用 `QuestMainChain`；底部消息列表单击选中任务；`OnDestroy` dirty 时弹保存 / 放弃）。`Assets/_Project/Scripts/Editor/Config/GenerateTablesMenu.cs` — 按 PRP 3.4：`Run` → `public static bool TryGenerate(bool forceDownload)`，开头跑任务表校验（Error 阻断、Warning 放行），新增菜单 `21Days/策划/校验任务表`（放在 `QuestEditorWindow.cs` 里或 `GenerateTablesMenu` 里都行，选职责更贴的那个并在文件头说明）。文件头写「为什么新建 / 扩展」。完成后 `project-lint` 零违规；MCP 编译零错误；`execute_menu_item("21Days/策划/任务编辑器")` 开窗一次 `read_console` 零异常；`execute_menu_item("21Days/策划/校验任务表")` Console 出现「任务表校验通过（4 条任务）」。产出：1 新文件 + 1 改动。（model: opus）

## W3（T4 与 T5 并行）

- [x] **T4**（四份文档已改；主窗口对齐了 12.1 播放模式措辞与 12.5 读文件失败段，并把整份 CRLF 转回 LF）文档：`docs/designer-guide.md` 第 12 章改写——12.1 开头新增「最省事：菜单 21Days → 策划 → 任务编辑器」一段（工具栏按钮各做什么、左栏怎么排主线 / 挂支线、右栏三种键的下拉、保存并生成、Console 看什么），原 JSON 手改内容整体降为「12.7 不用编辑器直接改 JSON」并保留；12.6 对照表序号改 1 起、「角色 id」改「对话编号」、删「第 0 个目标就是第一个」注、新增一行「生成被拦：`[配置表] 任务表：…`」说明去编辑器修；第 1 章「我要做 X」表的任务行加「（有编辑器）」。`docs/modules/quest.md` 第 4 节表格「新增 / 修改一条任务」行的「在哪改」改为「菜单 21Days/策划/任务编辑器（或直接改 `Tables/Data/quest/<编号>.json`）」，第 6 节删掉/改写已被本轮解决的限制（无）。`ai-docs/docs/modules/quest/quest-extension-guide.md`「新增一条任务」第 1 步前加一句「编辑器：21Days/策划/任务编辑器，保存并生成一键完成 1–3 步，校验在编辑期就报」；`quest-module-guide.md`「内容表（Luban）」段末加一句编辑期校验入口（`Game.Editor.Quest.QuestTableValidator`，生成前自动跑）。**不碰** `docs/developer-guide.md`、`ai-docs/docs/catalog.md`、`ai-docs/pitfalls.md`（他人改动中）。只读 HEAD 内容与本轮新文件，不把工作区里他人未提交功能写成现状。（model: sonnet）
- [x] **T5**（A1–A6 实测通过；EditMode 全量 609/609、Quest 命名空间 116/116；code-reviewer PASS，WARN 已修；截图肉眼看过；改动清单见本文件末尾，未提交）主窗口验收：`refresh_unity` → `read_console` 零错误；`run_tests(EditMode)` 全量，Quest 用例数 ≥ 51 + 新增；开编辑器窗口截图肉眼看布局；用 MCP `execute_code` 调 `QuestTableFile.LoadAll` → 逐条 `Save` → `git status --short Tables/` 为空（A1）；临时把 `1002.json` 前置改成 `[1002]` 跑「校验任务表」看 Console（A2），改回；`TryGenerate(false)` 在坏表时返回 false（A4），好表时 `.bytes` 更新（A3）；派 `code-reviewer`（sonnet）审 `Editor/Quest/` 与 `GenerateTablesMenu.cs`；`python .claude/skills/evolution/gc_scan.py`；新文件 `.meta` 已生成。最后列改动清单待用户审，**不提交**。（主窗口）

## 验收覆盖对照

| PRP 验收 | 任务 |
| --- | --- |
| A1 往返无 diff | T1 测试 + T5 实跑 |
| A2 聚合校验、文案 1 起 | T1 测试 + T5 |
| A3 保存并生成 | T3 + T5 |
| A4 生成前拦截 | T3 + T5 |
| A5 主线链与 ▲▼ | T1 `QuestMainChainTests` + T3 + T5 肉眼 |
| A6 三种下拉 | T1 `CollectContext` + T3 + T5 肉眼 |
| A7 Runtime 文案 | T2 |
| A8 文档 | T4 |

## 改动清单（用户 2026-09-26 授权「提交」，按规范分三次提交）

新增：`Assets/_Project/Scripts/Editor/Quest/`（QuestDraft、QuestObjectiveDraft、QuestTableFile、QuestTableValidator、QuestValidationContext、QuestIssue、QuestIssueSeverity、QuestMainChain、QuestEditorWindow、QuestKeyOptions，各带 .meta）、`Tests/EditMode/Quest/QuestTableFileTests.cs`、`QuestTableValidatorTests.cs`、`QuestMainChainTests.cs`（各带 .meta）、`PRP/quest-editor/`。
修改：`Editor/Config/GenerateTablesMenu.cs`、`Tests/EditMode/Game.Tests.EditMode.asmdef`、`Runtime/Quest/QuestContent.cs`、`QuestCatalog.cs`、`docs/designer-guide.md`、`docs/modules/quest.md`、`ai-docs/docs/modules/quest/quest-extension-guide.md`、`quest-module-guide.md`。
不属于本轮（他人会话）：`Editor/Game.Editor.asmdef`（Timeline 引用）、`Editor/Performance/`、Loot / Inventory / Exploration / 表演 / xlsx / dialogue 数据等。
提交前把 `QuestTableValidator.CollectCounterKeys` 改成按类型名 `t:LootConfig` 找资产 + `SerializedObject` 读 `crateQuestKey`，不再编译期依赖 `Game.Loot`，Loot 缺席时只是没有已知计数键；测试对此做了条件断言。提交：ca9248a feat(editor)、cc3ad80 fix(quest)、docs(quest) 见本次提交自身。
