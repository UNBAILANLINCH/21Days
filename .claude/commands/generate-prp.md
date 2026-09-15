---
description: PRP 阶段 2 — 由 PRD 生成可执行的 PRP（强制读模块三件套 / 规则 / pitfalls）
argument-hint: <feature-name>
---

# /generate-prp — PRP 阶段 2：生成 PRP

特性名：**$1**

把 `PRP/$1/prd.md` 转成可执行、可验证、可追溯的 PRP。产物 `PRP/$1/prp.md` + `PRP/$1/tasks.md`。

## 必做的上下文加载（模板强制，不可跳过）

1. 读 `PRP/$1/prd.md`。
2. 读 PRD「涉及模块」对应的 `ai-docs/docs/modules/<模块>/` **三件套**：
   - `<模块>-module-guide.md` — 内部结构与生命周期（改这个模块必读）
   - `<模块>-external-api.md` — 对外接口与调用约束（跨模块调用必读）
   - `<模块>-extension-guide.md` — 扩展点（要加新功能必读）
   模块文档尚不存在（模块还没落地）→ 在 PRP 里写明「无既有文档，本次为首次落地，完成后 `/generate-doc <模块>`」。
3. 读 `.claude/rules/` 中本次相关的规则：
   - `project-root.md`（目录、asmdef 依赖方向、加能力的顺序）— 必读
   - `csharp-code.md`（命名、序列化暴露面、生命周期、性能反模式）— 写 `.cs` 必读
   - `unity-assets.md`（场景 / 预制体 / SO / asmdef / `.meta`）— 动资产必读
   - `unity-tests.md`（EditMode 优先、命名、怎么跑）— 必读
   - `model-routing.md`（派单档位）— 写 tasks.md 前必读
4. 读 `ai-docs/pitfalls.md`，挑出本次必须规避的条目，写进「上下文快照」。
5. 看相关既有代码（`Assets/_Project/Scripts/Runtime/`），确认复用点与命名风格。**加能力的顺序是复用 → 扩展 → 新建**，PRP 里要写明为什么到了「新建」这一步。

## 产出 `PRP/$1/prp.md`

```markdown
# PRP: <特性名>

## 上下文快照
- 相关模块 guide / external-api 摘要（只摘关键约束，不复制全文）
- 本次必须规避的 pitfalls（逐条列 id / 标题）
- 适用规则（project-root / csharp-code / unity-assets / unity-tests）

## 架构决策
- 模块归属与命名空间 `Game.<Module>`，落在哪个目录、哪个 asmdef
- asmdef 依赖方向：谁引用谁（Runtime 不引用 Editor / Tests）
- 数据形态：哪些数值进 ScriptableObject（`Assets/_Project/Data/<Module>/`），哪些是运行时状态
- 序列化暴露面：Inspector 上要调什么，用 `[SerializeField] private` + 只读属性
- 场景 / 预制体改动：具体建哪些对象、挂哪些组件，走 Unity MCP 执行
- 关键取舍及理由（为什么不复用 X、为什么不扩展 Y）

## 任务清单（文件级，有序）
见 tasks.md

## 验证清单
- [ ] Unity 控制台零编译错误
- [ ] `project-lint` 零违规（保存时自动跑）
- [ ] EditMode 测试覆盖 <核心规则>，`/unity-test` 全绿
- [ ] 满足 PRD 全部验收标准（逐条对应）
- [ ] asmdef 依赖方向合规，无 Runtime → Editor / Tests 反向引用
- [ ] 无 public 字段；无每帧 Find / GetComponent / Log / 分配

## 风险 / 回滚
- 风险：...
- 回滚：改动仅在工作区，`git checkout -- <路径>`；已建的场景对象用 MCP 删回
```

## 产出 `PRP/$1/tasks.md`

有序、文件级、可逐项打勾。每项写清：**动哪个文件路径 → 做什么 → 产出什么 → 派给哪档模型**。

```markdown
# Tasks: <特性名>

- [ ] T1 `Assets/_Project/Scripts/Runtime/<Module>/<Module>Config.cs` — 建 ScriptableObject 配置类，字段 ...（model: sonnet）
- [ ] T2 `Assets/_Project/Scripts/Runtime/<Module>/<Xxx>.cs` — 实现核心逻辑 ...（model: opus）
- [ ] T3 场景接线 — 用 MCP 在 <场景> 建 <对象>，挂 <组件>，赋引用（model: sonnet）
- [ ] T4 `Assets/_Project/Scripts/Tests/EditMode/<Module>/<Xxx>Tests.cs` — 覆盖 <行为>（model: opus）
```

新建目录 / asmdef 也要作为独立任务列出，不要藏在别的任务里顺手做。

## 原则

- PRP 是「写下来、可审、可验」的决策记录；上下文要**精准而非堆砌**——摘关键约束，不复制整份 guide。
- tasks.md 不留「魔法步骤」：每项都要具体到文件和动作，否则执行阶段会现场发挥。
- 完成后交 `/validate-prp $1` 校验，通过再执行。
