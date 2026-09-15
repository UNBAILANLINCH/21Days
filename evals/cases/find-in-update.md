# Eval: 每帧检索最近敌人（Update 里的 Find 反模式）

验的规则：`.claude/rules/csharp-code.md` 的「不在 `Update` 里 `Find` / `GetComponent`」与「每帧路径无分配」。

## 场景

原样发给 AI（不加任何提示）：

> 给玩家加个功能：每帧检测场景里离玩家最近的敌人，把它记下来，后面别的系统要用。

## 陷阱

照字面「每帧检测」最容易写成：

```csharp
void Update()
{
    var enemies = FindObjectsOfType<Enemy>();   // 每帧全场景遍历 + 分配数组
    // ...挑最近的
}
```

问题有三层：`FindObjectsOfType` 是全场景遍历，开销随对象数线性增长；每次调用**分配一个新数组**，每帧产生垃圾；而且它拿不到未激活对象，行为还不稳定。`GameObject.FindWithTag` / `GetComponent` 放在 `Update` 里是同一类错误。

顺带还容易犯：把 `nearestEnemy` 写成 `public` 字段暴露出去（违反序列化暴露面规则）。

## 期望行为（harness 生效时，按防线顺序）

1. **规则注入**：编辑 `.cs` 触发 `knowledge-routing` 钩子，提示 `csharp-code.md` 适用；若 `Player` 模块已有 guide，`required-reads` 要求先读 `ai-docs/docs/modules/player/player-module-guide.md`。
2. **AI 自己做对**：看到「反模式表」后不在 `Update` 里 `Find`，改成下面两种之一：
   - **Awake 缓存**：`Awake` 里取一次引用存字段，`Update` 只遍历缓存的集合；
   - **注册表**：`Enemy` 在 `OnEnable` 把自己注册进一个静态 / 单例列表，`OnDisable` 注销，玩家只遍历这个列表（敌人会动态生成销毁时应选这条）。
   并且：检测频率能降就降（每 N 帧或定时器），距离比较用 `sqrMagnitude` 不开方，结果用 `[SerializeField] private` + 只读属性对外暴露，不用 `public` 字段。
3. **写错时 lint 拦下**：真写成 `Update` 里 `FindObjectsOfType`，保存时 `project-lint` 的 `find-in-update` 规则应报违规（给行号 + 原因 + 修复建议 + 规则引用），exit 2 反馈给 AI 自纠。
4. **复查兜底**：改动范围交给 `code-reviewer` 子代理（`model: sonnet`）时，它应把「每帧全场景检索」标成 BLOCK 或 WARN，并指出 `public` 字段问题。

## 判定（通过条件）

- [ ] 最终代码里 `Update` / `FixedUpdate` / `LateUpdate` 内**没有** `FindObjectsOfType` / `FindObjectOfType` / `GameObject.Find*` / `GetComponent`。
- [ ] 引用在 `Awake` 缓存，或用注册表（`OnEnable` 注册 / `OnDisable` 注销，成对）。
- [ ] 每帧路径无新分配（不 `new` 数组 / List，不拼字符串，不 `Debug.Log`）。
- [ ] 对外暴露用属性，没有 `public` 字段。
- [ ] 若 AI 写出了违规版本，`project-lint` 的 `find-in-update` 至少报了一条，**或** `code-reviewer` 给出 BLOCK/WARN——两道全没响才算 case 失败。
- [ ] 纯逻辑部分（挑最近目标）有 EditMode 测试，`/unity-test` 能跑。

## 不通过时查什么

- 钩子没提示 → `knowledge-routing.py` 的 glob 是不是没覆盖到该路径。
- lint 没报 → `.claude/skills/project-lint/rules.json` 里 `find-in-update` 的 `pattern` 有没有覆盖 `FindObjectsOfType`（复数）与 `FindAnyObjectByType` 等变体，`file_context` 是不是限制得太死（只匹配了 `void Update()` 却漏了 `LateUpdate`）。
- AI 读了规则仍写错 → 反模式表那一行是不是写得不够醒目 / 不够具体，考虑 `/learn` 把它升级成更严的 lint 规则。
