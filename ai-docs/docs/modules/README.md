# 模块文档三件套

每个玩法模块（`Assets/_Project/Scripts/Runtime/<Module>/`）在这里有一个文档目录，放三份文档。
由 `/generate-doc <模块>` 生成、`/generate-doc sync <模块>` 同步，注册表在 `.claude/skills/generate-doc/modules.json`。

## 目录与命名

```
ai-docs/docs/modules/<模块小写>/
├── <模块>-module-guide.md
├── <模块>-external-api.md
└── <模块>-extension-guide.md
```

目录名全小写（`player`、`inventory`），文件名前缀与目录名一致。模块名对应命名空间 `Game.<Module>` 与运行时目录 `Scripts/Runtime/<Module>/`，三者保持一一对应。

## 三份文档各管什么

| 文档 | 回答的问题 | 谁在什么时候读 |
| --- | --- | --- |
| `-module-guide.md` | 改这个模块**之前必须知道什么**？ | 要编辑本模块的人，**编辑前必读**（`required-reads` 钩子强制） |
| `-external-api.md` | 从外面**怎么调**、**不能怎么调**？ | 别的模块要调用本模块时 |
| `-extension-guide.md` | 要给它**加东西**，从哪儿接？ | 要扩展本模块功能时 |

### module-guide 写什么

- **职责边界**：做什么、明确不做什么。
- **内部结构**：有哪几个类，各自职责，谁持有谁。
- **核心数据**：ScriptableObject 配置类及其资产位置（`Assets/_Project/Data/<Module>/`）；运行时状态存在哪。
- **生命周期**：`Awake` / `Start` / `OnEnable` 各做了什么，订阅与退订在哪。
- **接线要求**：Inspector 上必须拖赋哪些引用，缺了会怎样；场景里需要哪些对象。
- **禁止事项**：这个模块里踩过的坑、不要这么改的地方。

### external-api 写什么

- 公开类型与公开成员的**签名**（`public` 方法 / 属性 / `event`）。
- 调用约束：什么时机能调、调用前提、线程 / 帧时序要求。
- 禁止事项：不要绕过哪个接口直接摸内部、不要在运行时改 SO 字段这类。
- 私有实现细节**不写**。

### extension-guide 写什么

- 现成的扩展点（接口、`event`、可继承的基类、可新增的 SO 资产类型）。
- 典型扩展步骤（加一种新的 X 要动哪几处）。
- 扩展时的依赖方向约束（不能反向依赖、不能引用 Editor / Tests）。

## frontmatter 模板

三份文档都带：

```yaml
---
type: module-guide | external-api | extension-guide
module: <模块>
layer: runtime | editor
maturity: seed | stable
---
```

- `type` —— 三选一，与文件名后缀对应。
- `module` —— 模块名，与目录、命名空间 `Game.<Module>` 一致。
- `layer` —— `runtime`（`Game.Runtime` 下）或 `editor`（`Game.Editor` 下）。
- `maturity` —— `seed`（骨架 / 刚生成 / 待补）、`stable`（结构稳定，可信）。

## 篇幅约束

**guide 只写「编辑前必须知道的」，不做代码复读机。**

- `module-guide` 目标 < 150 行；`external-api` / `extension-guide` 各 < 120 行。
- 写不下说明模块该拆，或该把细节留给代码——实现细节读代码更准、更不会过期。
- 不复制粘贴整段源码；要举例就给最小片段加 `path:line` 引用。
- 有取舍、有历史原因的设计决策要写（代码里看不出来）；显而易见的结构不写。
- `sync` 模式会保留人工补写的段落，放心写背景和取舍。

## 新建模块时

模块第一次落地（`/new-feature <模块名>` 或 PRP 执行完）后：

1. 跑 `/generate-doc <模块>` 生成三件套。
2. 在 `ai-docs/docs/catalog.md` 的模块表补一行。
3. 跑一次 `python .claude/skills/evolution/gc_scan.py` 确认没有失效链接。

后续每次改动模块接口，跑 `/generate-doc sync <模块>`。
