---
name: project-lint
description: 数据驱动的 Unity/C# 项目语义 linter，抓编译器与 IDE 查不到的项目反模式（public 字段、每帧 Find、tag 字符串比较、Runtime 反向依赖）。保存 .cs 时由 PostToolUse 钩子自动跑，也可手动跑做回归。
---

# project-lint

## 它解决什么

编译器和 IDE 管的是 C# 语法与通用风格；**Unity 的项目反模式它们一概不管**：

- `public float speed;` 能编译、能跑、Inspector 里也能调 —— 但任何人都能从外部改它，重构时改不动。
- `Update` 里 `GetComponent<Rigidbody2D>()` 能编译、能跑 —— 只是每帧多一次组件查找。
- `gameObject.tag == "Player"` 能编译、能跑 —— 只是每次比较都产生一次字符串分配，tag 写错还静默为 false。

这些都写在 `.claude/rules/csharp-code.md` 的反模式表里了，但**写进规则文档拦不住「看了没往心里去」**。
能翻译成正则的坑就别指望人记得 —— 这个 skill 就是那一层。

## 架构：逻辑与数据分离

```
rules.json   ← 规则定义（数据）。加规则只改这里。
   ↓
lint.py      ← 通用引擎（五层过滤 + 方法体跟踪），不随规则变化
   ↓
.claude/settings.json 的 PostToolUse(Edit|Write|MultiEdit) 钩子 ← 自动触发
```

违规时 exit 2，stderr 的内容会喂回给 Agent，它据此自我纠正；无违规零输出，不打扰。

## 两级严重度

| `severity` | 退出码 | 效果 | 什么时候用 |
| --- | --- | --- | --- |
| `block`（默认，可省略） | 2 | stderr 喂回给 Agent，它据此自我纠正 | 真违规：写了就是错的 |
| `warn` | 1 | 非阻断，stderr 给人看，方括号里多个「提醒·」前缀 | 提醒：该做但**不该拦住人干活**（如埋点缺失） |

一次运行里只要有一条 block，退出码就是 2。**别把 warn 当垃圾桶**：判不准的规则不该收进来，
而不是降成 warn（原则见下面「设计原则」第 2 条）。

## 规则字段

| 字段 | 作用 |
| --- | --- |
| `id` | 唯一标识 |
| `rule` | 中文分类名，显示在报错行的方括号里 |
| `severity` | `block`（默认）/ `warn`，见上表 |
| `files` | 适用后缀列表，省略则默认 `[".cs"]` |
| `path_contains` | 路径含任一片段才生效（写正斜杠，引擎已把反斜杠归一） |
| `file_context` | 文件级正则前置：整份文件匹配得上才检查 |
| `file_context_absent` | 反向前置：整份文件匹配得上就跳过 |
| `dir_context_absent` | **目录级**反向前置：`{"root_after": "/Scripts/Runtime/", "pattern": "…"}`，取路径里 `root_after` 之后的第一段目录（= 模块根）递归扫同后缀文件，**有任一文件命中就跳过**。用于「整个模块一处都没有 X」——按单文件判会误报 |
| `pattern` | 行级主匹配正则（必填） |
| `exclude_patterns` | 命中任一就跳过（排合法写法） |
| `confirm_patterns` | 须再命中任一才算违规（二次确认） |
| `in_methods` | 方法名列表，命中行须落在这些方法体内才报 |
| `violation_tpl` | 违规描述，`{line}` 是命中行占位符 |
| `fix` / `ref` | 怎么改 / 依据出处 |

## 五层过滤（逐层收窄，为的是把误报压到几乎没有）

| 层 | 字段 | 作用 |
| --- | --- | --- |
| 1 文件级 | `files` / `path_contains` / `file_context` / `file_context_absent` / `dir_context_absent` | 不满足整条规则跳过 |
| 2 行主匹配 | `pattern` | 当前行是否命中主正则 |
| 3 排除 | `exclude_patterns` | 命中任一则跳过 |
| 4 确认 | `confirm_patterns` | 须再命中任一才报 |
| 5 方法域 | `in_methods` | 命中行须落在指定方法体内 |

举例：`public-field-exposed` 先要求整份文件里有 `MonoBehaviour` 或 `ScriptableObject`（第 1 层），
再匹配 `public 类型 名字;`（第 2 层），最后排掉含 `=>`、`(`、`{` 的行（第 3 层）——
表达式体属性、方法签名、属性块都是合法写法，不能报。

## in_methods：只管每帧路径

`Awake` 里 `GetComponent` 是**推荐做法**，`Update` 里才是坑。同一个正则，位置不同结论相反，
所以引擎要知道「这一行在哪个方法里」：

1. 先剥掉注释与字符串字面量（`Debug.Log($"{a}")` 里的 `{}` 会把大括号计数带偏）。
2. 用方法头正则认出 `修饰符* 返回类型 方法名(参数)`，等它后面第一个 `{` 压栈，深度退回就出栈。

**已知取舍**（写在 `lint.py` 的 docstring 里，改之前先看）：

- 跨行的逐字字符串（`@"` 开头、下一行才闭合）不跟踪 —— 撞上了是漏报，不会误报。
- 构造函数不被识别成方法（正则要求方法名前有返回类型）—— 现有规则不关心。
- `else if (x) {` 会被方法头正则误认成「方法 if」，靠关键字黑名单挡掉。

## 豁免写法

命中行末尾写 `// lint-ok: <理由>`，这一行就放行。**理由必须写** —— 绕了也留痕。
这是 `CLAUDE.md` 硬规则 5「护栏挡住时不拆护栏」的唯一出口：确属误报就写理由放行，不去改规则、不去关钩子。

```csharp
private void Update()
{
    // 编辑器专用的调试面板，只在 Editor 下编译进来
    Debug.Log(state); // lint-ok: #if UNITY_EDITOR 调试面板，不进构建
}
```

## 当前规则

| id | 管什么 |
| --- | --- |
| `public-field-exposed` | MonoBehaviour / ScriptableObject 里的 public 字段 |
| `find-in-update` | 每帧回调里 `Find` 系 / `GetComponent<>` |
| `debug-log-in-update` | 每帧回调里 `Debug.Log` |
| `tag-string-compare` | `.tag == "X"` 字符串比较 |
| `send-message` | `SendMessage` / `BroadcastMessage` 反射调用 |
| `empty-unity-message` | 空的 `Update` / `FixedUpdate` / `LateUpdate` |
| `local-absolute-path` | `.cs/.md/.json/.asmdef/.txt` 里写进本机绝对路径 |
| `using-unityeditor-in-runtime` | `Scripts/Runtime/` 下裸 `using UnityEditor` |
| `module-missing-telemetry` | **提醒（warn）**：模块有 `XxxState` / `XxxIntent` 却整个模块零埋点 → 跑 `/instrument-module <模块>` |

## 新增一条规则

在 `rules.json` 的 `rules` 数组里加一个对象即可，不用动 `lint.py`：

```json
{
  "id": "coroutine-no-handle",
  "rule": "生命周期",
  "files": [".cs"],
  "pattern": "^\\s*StartCoroutine\\s*\\(",
  "exclude_patterns": ["="],
  "violation_tpl": "裸 StartCoroutine 丢了句柄：{line}",
  "fix": "存成 Coroutine 字段，OnDisable 里 StopCoroutine",
  "ref": "csharp-code.md #生命周期"
}
```

规则来源建议：`CLAUDE.md` 硬规则的程序化版本、`.claude/rules/` 里的反模式表、`ai-docs/pitfalls.md` 里反复出现的坑。
`/learn` 沉淀经验时，**凡是能机械判定的一律优先落到这里**，落不了才写文档。

## 手动跑

```bash
# CLI 模式：lint 指定文件（可以一次给多个）
python .claude/skills/project-lint/lint.py Assets/_Project/Scripts/Runtime/Player/PlayerMovement.cs

# hook 模式：不带参数时从 stdin 读工具负载 JSON
echo '{"tool_input":{"file_path":"Assets/_Project/Scripts/Runtime/Player/PlayerMovement.cs"}}' | python .claude/skills/project-lint/lint.py
```

有 block 违规 exit 2、只有 warn 提醒 exit 1，详情都打到 stderr；干净 exit 0 且零输出。

## 设计原则

- **成功静默，失败冗余**：通过时不输出一个字；违规时给到行号 + 原因 + 改法 + 依据，不只说「错了」。
- **只收机械可判的**：要理解语义才能判的（这个方法该不该拆、这个数字算不算配置）别塞进来。
  正则做不了，假阳性会逼人绕路，而绕的路子一旦形成，真该拦那次也拦不住。
- **fail-open**：引擎自己出岔子一律 exit 0。护栏不该把会话卡死。
