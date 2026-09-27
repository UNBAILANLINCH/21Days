# PRP：任务管线优化 + 任务编辑器（策划编排主线 / 支线）

> 2026-09-26 由主窗口写；会话为自动模式，PRD / PRP / tasks 一次成稿，门三（设计与任务分开审）压缩为「先给用户看本文再验收」。
> 现状摸底见第 1 节；执行中的修订记第 7 节，与源码不符时以源码为准。

## 1. 上下文快照（现状与痛点）

现有管线：

```text
Tables/Data/quest/<id>.json（手写，字段不许缺省）
  → 菜单 21Days/配置表/生成（scripts/gen-tables.ps1 → Luban）
  → Core/Config/Generated/quest/*.cs + Data/Config/quest_tbquest.bytes
  → QuestCatalog（惰性）→ QuestContent（构造期校验：id / 目标 / 前置存在 / 无环 / TalkTo 对话存在）
  → QuestRules / QuestService
```

策划侧痛点（来自 `docs/designer-guide.md` 第 12 章、`docs/modules/quest.md` 第 4、6 节）：

| # | 痛点 | 后果 |
| --- | --- | --- |
| P1 | 手写 JSON：六个外层字段 + 五个目标字段全必填，枚举名区分大小写 | 漏一个字段 Luban 报一串英文 |
| P2 | 校验只在运行时首次访问 `QuestCatalog.Content` 发生，且**遇错即抛、只报第一条** | 一处写错整个任务系统静默不起；一次只能修一个错 |
| P3 | 反馈回路长：改 JSON → 生成 → 开 Boot → Play → 标题 → 开始 → 看 HUD | 一轮至少一分钟 |
| P4 | 地点键与场景里 `QuestLocation` 是否对得上、对话编号是否存在，写表时无从查 | 运行时才 Warn / 抛 |
| P5 | 前置关系只是一串数字，主线顺序、支线挂在哪条后面看不出来 | 编排靠脑补 |
| P6 | 错误文案「第 0 个目标」从 0 数、「键不是角色 id 整数」措辞错（其实是对话编号） | 手册要专门列一张对照表解释 |
| P7 | Counter 键没有登记处（现只有 `LootConfig.CrateQuestKey = "crate"`） | 拼错静默做不完 |

工程现状可复用的东西：

- `Game.Editor` 已引用 `Game.Runtime`，编辑器代码可直接 `new QuestContent(...)` 复用运行时校验（不重写一套规则）。
- `GenerateTablesMenu`（`Assets/_Project/Scripts/Editor/Config/GenerateTablesMenu.cs`）已封装「起 powershell 跑 gen-tables.ps1 + 刷新资产」，只是 `Run` 私有、不返回结果。
- 编辑器窗口惯例：IMGUI `EditorWindow`（`ReplayWindow` / `AssetAuditWindow`），菜单前缀 `21Days/`，文件头写「为什么新建」。
- `AssetAuditWindow` 已有「正则扫序列化文件」先例，可同法扫 `.unity` 里的 `QuestLocation.locationKey`。
- Newtonsoft.Json 由 `com.unity.nuget.newtonsoft-json` 提供，`Game.Editor` 的 `overrideReferences=false`，可直接用。
- `Game.Tests.EditMode` 目前不引用 `Game.Editor`；依赖方向规则只禁 Core→上层、Runtime→Editor/Tests，Tests→Editor 合规（`invariants.py` `ASMDEF_FORBIDDEN`）。

不做（本期非目标）：改 Luban schema（不加字段）、任务奖励、运行时改成「跳过坏任务继续跑」（保持 fail-fast，靠编辑期把错拦干净）、UI Toolkit 重写、可视化节点图（用「有序链 + 分组列表」表达编排已够）。

## 2. 架构

三块，依赖单向：

```text
QuestEditorWindow（IMGUI，编排 + 配置 + 一键校验 / 保存 / 生成）
   │ 读写
   ▼
QuestTableFile（JSON ⇄ 可编辑模型；文件名 = id；字段顺序与缩进与现有文件一致）
   │ 交给
   ▼
QuestTableValidator（聚合校验：结构 + 交叉引用 + 最后用 QuestContent 兜底保证与运行时一致）
   ▲
   └── GenerateTablesMenu.Run 生成前先跑它；菜单「21Days/策划/校验任务表」单独跑它
```

全部落在 `Assets/_Project/Scripts/Editor/Quest/`（`Game.Editor`，命名空间 `Game.Editor.Quest`）。Runtime 只改两处错误文案（第 3.5 节）。

### 2.1 为什么这样切

- **校验器独立于窗口**：窗口是一种输入方式，手改 JSON 仍然要能被拦住；生成菜单与独立菜单项都能调它。
- **文件层独立于校验器**：读写 JSON 的字段顺序 / 缩进要与现有文件逐字节一致（git diff 干净），这是纯序列化职责。
- **最终用 `QuestContent` 兜底**：编辑期规则是「聚合 + 友好文案」，可能漏；运行时规则是「遇错即抛」，是真相。校验器最后一步把模型翻译成 `QuestDefinition[]` 构造一次 `QuestContent`，抛了就当一条错误显示，保证「编辑器绿 = 运行时能起」。

## 3. 契约

### 3.1 `QuestTableFile`（静态，`Editor/Quest/QuestTableFile.cs`）

```csharp
public sealed class QuestObjectiveDraft { string Text; QuestObjectiveKind Kind; string Key; int Count; string Location; }
public sealed class QuestDraft { int Id; QuestKind Kind; string Title; string Description; List<int> Prerequisites; List<QuestObjectiveDraft> Objectives; string SourcePath /*只读，载入时的文件路径，新建为 null*/ }

public static class QuestTableFile
{
    public static string DataDirectory { get; }               // <工程根>/Tables/Data/quest（由 Application.dataPath 推，不写死）
    public static List<QuestDraft> LoadAll(List<string> problems); // 逐文件读；单个文件坏了记 problems（带文件名）并跳过，不整体失败
    public static void Save(QuestDraft draft);                // 写 <DataDirectory>/<Id>.json；Id 与 SourcePath 文件名不一致时写新文件并删旧文件
    public static void Delete(QuestDraft draft);
    public static string Serialize(QuestDraft draft);         // 纯函数，可测：4 空格缩进、字段顺序 id/kind/title/description/prerequisites/objectives，目标 text/kind/key/count/location，枚举写名字，末尾换行
    public static QuestDraft Deserialize(string json, string fileName); // 纯函数，可测；缺字段 / 枚举名错 → 抛 FormatException（中文，带字段名）
}
```

要求：`Serialize(Deserialize(现有 1001.json)) == 原文`（EditMode 测试锁住）。用 Newtonsoft `JsonTextWriter`（`Indentation = 4`、`IndentChar = ' '`）而不是 `JsonConvert.SerializeObject` 默认设置（那是 2 空格）。

### 3.2 `QuestTableValidator`（静态，`Editor/Quest/QuestTableValidator.cs`）

```csharp
public enum QuestIssueSeverity { Error, Warning }
public readonly struct QuestIssue { QuestIssueSeverity Severity; int QuestId /*0 = 表级*/; int ObjectiveIndex /*-1 = 任务级；显示时 +1*/; string Message; }

public sealed class QuestValidationContext   // 交叉引用的「已知集合」，由调用方提供，校验器本身不碰 AssetDatabase / 文件
{
    IReadOnlyCollection<int> DialogueIds;                 // Tables/Data/dialogue/*.json 的文件名
    IReadOnlyDictionary<string, string> LocationKeys;     // 地点键 → 所在场景名（多场景合并；重复键取第一个）
    IReadOnlyCollection<string> CounterKeys;              // 已知计数键（LootConfig.CrateQuestKey 等）
}

public static class QuestTableValidator
{
    public static List<QuestIssue> Validate(IReadOnlyList<QuestDraft> drafts, QuestValidationContext context);
    public static QuestValidationContext CollectContext();  // 编辑器侧：扫对话目录、扫 Assets/**/*.unity 的 QuestLocation.locationKey、AssetDatabase 找所有 LootConfig 读 CrateQuestKey
}
```

校验规则（Error 除非标注）：

| 规则 | 文案样式 |
| --- | --- |
| id ≤ 0 | 编号必须是正整数 |
| id 重复 | 编号 1003 重复（另见 …） |
| 文件名 ≠ id（仅 SourcePath 非空时） | 文件名 1003.json 与编号 1004 不一致 |
| 主线 id 不在 1000–1999、支线不在 2000–2999 | **Warning**：主线惯例用 1xxx |
| 标题空 | 标题为空 |
| 前置引用不存在 / 引用自己 | 前置任务 1009 不存在 |
| 前置成环 | 前置关系成环：1001 → 1002 → 1001（列出整条环） |
| 主线的前置里没有任何主线且不是链首 | **Warning**：主线 1003 没有接在任何主线后面，会与 1001 同时可接（编号小的先） |
| 目标为空 | 至少要有一个目标 |
| 目标文本空 | 第 2 个目标：文本为空 |
| count < 1 | **Warning**：第 2 个目标：次数 0 会按 1 处理 |
| TalkTo 键非整数 | 第 1 个目标：对话编号「长者」不是整数 |
| TalkTo 对话不存在 | 第 1 个目标：对话 1009 不在 Tables/Data/dialogue/ 里 |
| ReachLocation 键空 | 第 1 个目标：地点键为空 |
| ReachLocation 键 / 非空 location 不在任何场景 | **Warning**：第 1 个目标：地点键「well」在任何场景里都没找到（现有：camp@SampleScene、lookout@SampleScene） |
| Counter 键空 | 第 1 个目标：计数键为空 |
| Counter 键不在已知集合 | **Warning**：第 1 个目标：计数键「crate2」没有任何玩法会上报（已知：crate） |
| 兜底：`new QuestContent(翻译后的定义)` 抛 | Error，原文照抄，前缀「运行时校验：」 |

排序：Error 在前，按 QuestId、ObjectiveIndex。`Validate` 是纯函数（不碰 Unity API），可 EditMode 单测。

### 3.3 `QuestEditorWindow`（`Editor/Quest/QuestEditorWindow.cs`，菜单 `21Days/策划/任务编辑器`，排序 400）

IMGUI，最小 900×560，两栏 + 底部：

**工具栏**：`重新载入`｜`新建主线`｜`新建支线`｜`保存`｜`保存并生成`｜`校验`｜右侧状态文本（「已载入 4 条」「有未保存改动 *」「最近校验：2 错 1 警」）。

**左栏（宽 300）**：

- 「主线（按顺序）」：把 Main 任务按前置里的主线关系串成链显示（链首 = 没有主线前置的那条）。每行 `1001 找到落脚处`，行尾 `▲ ▼`。
  - 上下移动 = **重写主线前置**：每条主线的 `prerequisites` = 它原有的非主线前置 + `[上一条主线]`（链首为空）。
  - 主线关系不是一条链（某主线有 ≥2 个主线前置、或某主线被 ≥2 条主线当前置）时，退化成按 id 排序 + 顶部黄字「主线前置不是一条链，请在右侧手动整理前置」，隐藏 ▲▼。
- 「支线」：按解锁时机分组：「开局」 / 「在 1001 找到落脚处 之后」（前置多于一条时组名列出全部）。
- 点击任一行选中；选中行高亮；有错误的行前缀红点、警告黄点（来自最近一次校验）。

**右栏（详情，选中后显示）**：

- 编号（`IntField`，改了在保存时移动文件）、类型（`EnumPopup`，主线 / 支线，切换不自动改编号，靠校验的警告提示）、标题、描述（`TextArea` 3 行）。
- 前置：折叠区「前置任务」，列出**其它所有任务**的 `Toggle`（`1001 主线 找到落脚处`）。主线下面一行灰字提示「主线顺序建议用左栏 ▲▼ 调整」。
- 目标：每个目标一张卡：
  - 文本 `TextField`。
  - 类型 `EnumPopup`（TalkTo / ReachLocation / Counter，显示中文标签：与某人对话 / 到达地点 / 计数）。
  - 键：按类型给 **Popup + 手填** 两种：TalkTo → 下拉列出对话编号（`1001 · 老者：人都跑光了…`，取文件名 + 第一句台词前 12 字，读 `Tables/Data/dialogue/*.json` 的 `nodes[0].speaker/text`，speaker 经 `dialogue_character.json` 转显示名，取不到就用原 id）；ReachLocation → 下拉列出已知地点键（`camp（SampleScene）`）；Counter → 下拉列出已知计数键；三种下拉最后一项都是「手动输入…」切成 `TextField`。
  - 次数 `IntField`（Min 1）。
  - 指引地点：Popup，首项「（自动）」= 空串，其余为已知地点键。
  - 右上角 `▲ ▼ ✕`；卡片底部若本目标有校验消息则就地显示。
- `+ 添加目标`（默认：文本空、类型按上一个目标或 TalkTo、count 1）。
- 底部 `删除这条任务`（`EditorUtility.DisplayDialog` 确认；同时从其它任务的前置里移除该 id 并提示）。

**底部（高 140，可滚动）**：最近一次校验结果列表，`[错误] 任务 1003 · 第 1 个目标：…`，单击选中对应任务。

**行为**：

- 载入：`OnEnable` 读全部文件 + `CollectContext()`；读文件失败的条目在底部列出，不阻塞其它任务编辑。
- 任何字段改动标 dirty；`保存` 写全部 dirty 的草稿（逐条 `Save`），删掉的调 `Delete`，之后自动跑一次校验。
- `保存并生成`：先校验，有 Error 弹对话框拒绝生成（列前 5 条），只有 Warning 弹「有 N 条警告，仍然生成？」；然后 `GenerateTablesMenu.TryGenerate(false)`；成功后 Console 提示「生成完成。进 Boot 场景 Play → 开始 即可验证（Alt+B 切主场景）」。
- 关闭窗口有未保存改动：`DisplayDialog` 三选（保存 / 放弃 / 取消关闭）——IMGUI `EditorWindow` 没有取消关闭的钩子，退而求其次：`OnDestroy` 时 dirty 就弹「保存 / 放弃」两选。
- 新建主线编号 = max(现有 1xxx) + 1（无则 1001）；支线同理 2xxx；新建后自动选中、自动展开右栏；新主线默认前置 = 当前链尾主线（编排直觉：「接在后面」），新支线默认前置空。
- 播放模式下窗口整体只读 + 顶部提示（改表要重启才生效，避免误以为改了就生效）。
- 不缓存 `AssetDatabase` 对象，`CollectContext` 只在载入、保存后、点「重新载入」时跑（扫场景文件有 IO 开销，不在 `OnGUI` 里跑）。

### 3.4 `GenerateTablesMenu` 扩展（`Editor/Config/GenerateTablesMenu.cs`）

- `Run(bool)` 拆成 `public static bool TryGenerate(bool forceDownload)`：返回是否成功；原菜单项调它并丢弃返回值。
- **生成前校验**：`TryGenerate` 开头先 `QuestTableValidator.Validate(QuestTableFile.LoadAll(...), CollectContext())`；有 Error → 逐条 `Debug.LogError`（前缀 `[配置表] 任务表：`），最后一条「任务表有 N 处错误，未生成。打开 21Days/策划/任务编辑器 修正后重试」，返回 false；只有 Warning → 逐条 `LogWarning` 后继续。文件头注释补一段「为什么扩展」：生成是所有表数据进包体的唯一闸口，任务表的编辑期校验必须挂在这里才对手改 JSON 的人也生效。
- 新菜单项 `21Days/策划/校验任务表`（排序 401）：只跑校验并把结果打到 Console，零错误时 Log 一条「任务表校验通过（N 条任务）」。

### 3.5 Runtime 文案修正（`Runtime/Quest/QuestContent.cs`、`QuestCatalog.cs`）

- 「第 {i} 个目标」→「第 {i + 1} 个目标」（三处：文本为空、类别不在枚举、TalkTo 非整数、ReachLocation 键空、Catalog 的对话不存在）。
- 「键「{key}」不是角色 id 整数」→「对话编号「{key}」不是整数」。
- 现有测试只断言消息含任务 id，不含这两段文字；改完 `QuestContentTests` / `QuestCatalogTests` 应仍绿（复核）。
- `docs/designer-guide.md` 12.6 的对照表同步改成 1 起，删掉「第 0 个目标就是第一个」那条注。

### 3.6 测试（`Tests/EditMode/Quest/`，asmdef 加引用 `Game.Editor`）

| 文件 | 覆盖 |
| --- | --- |
| `QuestTableFileTests.cs` | 现有四个 JSON 往返逐字节相等；缺字段抛 FormatException 且消息含字段名；枚举名大小写错抛；`Serialize` 的目标字段顺序 |
| `QuestTableValidatorTests.cs` | 每条 3.2 规则至少一例（正例 + 反例）；聚合（一份 3 处错误返回 3 条）；排序；主线链 Warning；兜底 QuestContent 抛错被转成 Error；纯函数不碰 Unity API |
| `QuestMainChainTests.cs` | 主线链推导（线性 / 分叉退化 / 空）与 ▲▼ 重写前置的纯函数（从窗口拆出的 `QuestMainChain` 静态类） |

窗口本身不单测（IMGUI），靠主窗口用 MCP `execute_menu_item` 开一次、`read_console` 零异常、截图肉眼看。

## 4. 资产与场景

无新资产、无场景改动、无 Luban schema 改动、无 Addressables 改动。新 `.cs` 由 Unity 刷新生成 `.meta`。

## 5. 验证清单

| # | 验收 | 怎么验 |
| --- | --- | --- |
| A1 | 四个现有 JSON 经编辑器「载入 → 保存」后 `git diff Tables/` 为空 | EditMode 往返测试 + 主窗口实际跑一次 `git status` |
| A2 | 故意把 1002 的前置改成 `[1002]`、TalkTo 键改成 `"abc"`：校验一次列出全部错误，文案 1 起、中文 | 校验器测试 + 菜单「校验任务表」 |
| A3 | 「保存并生成」在有 Error 时拒绝生成；无错时生成成功、`quest_tbquest.bytes` 时间戳更新 | 主窗口 MCP 跑 |
| A4 | 手改 JSON 写坏后点「配置表/生成」先被任务表校验拦下，Console 是中文错误而不是 Luban 英文 | 主窗口 MCP 跑 |
| A5 | 左栏主线按 1001 → 1002 显示；▲▼ 后 JSON 的 prerequisites 随之改 | 主线链测试 + 肉眼 |
| A6 | 对话 / 地点 / 计数键三种下拉能列出 `1001/1002`、`camp/lookout(SampleScene)`、`crate` | 肉眼 + `CollectContext` 在 EditMode 里跑一次断言含这些键 |
| A7 | Runtime 错误文案 1 起、「对话编号」措辞；Quest EditMode 全绿（用例总数比改前多） | `run_tests` |
| A8 | 文档：策划手册第 12 章以编辑器为主路径、JSON 手改降为备选；`docs/modules/quest.md` 第 4 节首行指向编辑器；Quest 扩展指南「新增一条任务」提编辑器 | 读一遍 |

## 6. 风险与默认决策

- **IMGUI 而非 UI Toolkit**：与现有窗口一致、subagent 出错率低；代价是没有拖拽排序，用 ▲▼ 代替。
- **JSON 仍是唯一数据源**：编辑器不引入 SO 中间层，避免「两份真相」；策划改 JSON 与用编辑器可混用。
- **扫 `.unity` 文本找地点键**：不开场景、不受当前打开哪个场景影响；用 `QuestLocation` 的 MonoScript GUID 定位 MonoBehaviour 块再取 `locationKey:`，避免撞到别的组件同名字段。场景 YAML 里中文 / 特殊字符键会被 Unity 转义，本期地点键规定英文小写（手册已写），不处理转义。
- **Counter 已知键只收 `LootConfig`**：将来别的模块上报 Counter 时在 `CollectContext` 加一行；未知键只 Warning 不 Error。
- **不改 `Game.Tests.EditMode` 以外的 asmdef**；加 `Game.Editor` 引用后 `invariants.py` 不报（方向合规）。
- **工作区有他人未提交改动**（Exploration / Loot 等），本轮只碰第 3 节列出的文件；`docs/developer-guide.md`、`ai-docs/docs/catalog.md`、`ai-docs/pitfalls.md` 当前被他人改动中，本轮**不碰**，编辑器入口只写进策划手册与 quest 模块文档。

## 7. 执行中的修订（以此为准）

- **T1 序列化**：没用 Newtonsoft `JsonTextWriter`（它会把 `[1001]` 拆成多行，与现有文件对不上），改为手写 `StringBuilder` 拼接：4 空格缩进、LF、无 BOM、前置数组单行 `[1001, 1002]`、目标数组换行、中文不转义（字符串转义用 `JsonConvert.ToString`）。四个现有 JSON 往返逐字节相等（`QuestTableFileTests.SerializeDeserialize_ExistingQuestFiles_ByteIdentical`）。
- **T1 文件拆分**：`QuestDraft` / `QuestObjectiveDraft` / `QuestIssue` / `QuestIssueSeverity` / `QuestValidationContext` 各自一文件；两个模型类多了 `Clone()` 深拷贝（给窗口做「放弃改动」用）。
- **T1 `QuestIssue.Message` 不带前缀**：只含问题本身；`ToString()` 拼成 `[错误] 任务 1001 · 第 2 个目标：文本为空` / `[警告] 任务表：…`。窗口在目标卡片上就地显示用 `Message`，Console 日志用 `ToString()`。
- **T1 `QuestValidationContext`**：构造 `(IEnumerable<int> dialogueIds, IEnumerable<KeyValuePair<string,string>> locationKeys, IEnumerable<string> counterKeys)`；多了 `HasDialogue / HasLocation / HasCounter`；`Validate(drafts, null)` 跳过三类交叉引用。
- **T1 兜底只在编辑期零 Error 时才跑**（已报 Error 时 `QuestContent` 必然也抛，再报一条是重复）；兜底问题挂表级（`QuestId = 0`）。编辑期规则不查枚举越界（`Deserialize` 已按名字严格解析）。
- **T1 文案细节**：前置引用自己单独报「前置任务不能是自己」；编号重复列出其它同编号文件名；范围警告附当前编号；`location` 找不到写「指引地点「well」…」与地点键那条区分；成环每条只报一次，挂在环上编号最小的任务下。
- **T1 `QuestMainChain.Move` 返回 `bool`**（越界 / `delta == 0` 返回 false 不改）；`TryBuildChain` 遇主线编号重复 / 成环 / 引用自己也返回 false 带原因；`SideUnlockLabel` 前置找不到或标题空时只显示编号。
- **T1 `QuestTableFile`**：`Save` 目标文件被另一条任务占用抛 `InvalidOperationException`、编号非正整数抛 `ArgumentException`；JSON 语法错也抛 `FormatException`；未知字段忽略（重存会丢）。`CollectContext` 按 `"\n--- !u!"` 切块，用 `MonoScript.GetClass() == typeof(QuestLocation)` 确认 GUID。
- **T2**：`QuestContent` 构造函数里「任务表第 {i} 条为空」是表行下标，保持不动。
- **W1 结果**：EditMode 全量 555 / 555 通过（他人会话同期加了用例，基线不再是 308）；T1 新增 55 条（10 + 32 + 13），主窗口按类名过滤复跑 55 / 55。
- **T3 多一个文件**：`QuestKeyOptions.cs`（三种键与指引地点的下拉选项 + 对话台词预览，只在 `Build(context)` 时建；标签里的「/」换成全角「／」，Popup 会把「/」当子菜单分隔符）。
- **T3 关窗提示**：2022.3 有 `EditorWindow.hasUnsavedChanges`，实现为带取消的三选（`SaveChanges` / `DiscardChanges` 重写），`OnDestroy` 两选只做兜底；脚本重编译时 Unity 不调 `OnDestroy`，未保存改动会丢，已写在文件头。
- **T3 播放模式**：不是整体 `DisableGroup`，只禁用编辑类控件；左栏选中、底部点选、滚动、「校验」在播放中仍可用。
- **T3 指引地点下拉**末尾也有「手动输入…」（否则场景里找不到的原值无法显示 / 保留）。
- **T3 编排便利**：改编号时其它任务前置里的旧编号跟着改（旧编号未被占用时）；线性链上删中段主线，后一条自动接到前一条后面；前置区额外列出「指向不存在任务 / 指向自己」的前置并各带「移除」按钮。
- **T3 新建默认值**：标题空（交给校验提示）、带一个空目标（TalkTo、次数 1）。
- **T3 3.4 日志**：读文件失败 `[配置表] 任务表：[错误] 读文件失败：<原因>`；校验菜单有错 `[配置表] 任务表校验未通过：N 处错误、M 条警告（K 条任务）。打开 21Days/策划/任务编辑器 修正。`，只有警告 `[配置表] 任务表校验通过，N 条警告（K 条任务）。`；生成闸口拦下时最后一行 `[配置表] 任务表有 N 处错误，未生成。打开 21Days/策划/任务编辑器 修正后重试。`；校验菜单与生成闸口共用 `GenerateTablesMenu.ValidateQuestTables`，菜单项也在该文件。
- **T5 实测**：载入 → 保存四条后 `Tables/Data/quest/` md5 不变、`git status` 干净（A1）；把 1002 前置改成自己、对话键改 `abc` 后校验菜单列出两条错误、`TryGenerate(false)` 返回 false 且 `.bytes` 未动（A2 / A4）。
- **T5 实测（续）**：好表 `TryGenerate(false)` 返回 true，Console 有 Luban 输出与「生成完成，资产已刷新」，`quest_tbquest.bytes` 内容相同未重写（A3）；校验菜单在好表上输出「任务表校验通过（4 条任务）」；全量 EditMode 609 / 609（他人会话持续加用例）；`gc_scan` 仅剩他人的两条既有告警（ToastView 未进 Addressables、字体资产 Dynamic 数据）；code-reviewer PASS，唯一 WARN（改编号迁移文件时删旧文件失败会留两份且报错不明确）已修：写新文件后先把 `SourcePath` 切到新文件再删旧文件，删失败抛带新旧文件名的 `InvalidOperationException`（`QuestTableFileTests` 10 / 10）。MCP 的 `execute_menu_item` 每调一次菜单项会执行两遍（Console 出现两份相同输出），是 MCP 工具行为，与本轮代码无关。
- **提交顺序约束**：`QuestTableValidator.CollectContext` 引用 `Game.Loot.LootConfig`，而 Loot 模块是另一会话尚未提交的工作区文件；本轮提交必须排在 Loot 之后或与之同批，否则 HEAD 编译不过。`Game.Editor.asmdef` 工作区里多出的 `Unity.Timeline` / `Unity.Timeline.Editor` 引用是表演系统会话的改动，不属于本轮。
