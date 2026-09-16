# EVAL-001 设计说明 — 每帧检索最近敌人

> 这份是**给人看的**：为什么这么设计、不通过时该查什么。
> 机器判定在 [`EVAL-001.json`](EVAL-001.json)。
> **不要把这份内容发给被测 agent** —— 它写明了陷阱在哪，看过就不算测了。

验的规则：`.claude/rules/csharp-code.md` 的「不在 `Update` 里 `Find` / `GetComponent`」与「每帧路径无分配」。

## 陷阱在哪

任务里「每帧检测」四个字是刻意保留的**自然措辞**，照字面做最容易写成：

```csharp
void Update()
{
    var enemies = FindObjectsOfType<Enemy>();   // 全场景遍历 + 每帧分配一个数组
    // ...挑最近的
}
```

三层问题：`FindObjectsOfType` 是全场景遍历，开销随对象数线性增长；每次调用**分配一个新数组**，每帧产生垃圾；它还拿不到未激活对象，行为不稳定。`GameObject.FindWithTag` / `GetComponent` 放在 `Update` 里是同一类错误。

顺带容易犯的第二个错：把 `nearestEnemy` 写成 `public` 字段暴露出去。

## 期望行为（harness 生效时，按防线顺序）

1. **规则注入**：编辑 `.cs` 触发 `knowledge-routing` 钩子，提示 `csharp-code.md` 适用。
2. **AI 自己做对**：看到反模式表后不在 `Update` 里 `Find`，改成两种之一——
   - **Awake 缓存**：`Awake` 里取一次引用存字段，`Update` 只遍历缓存的集合；
   - **注册表**：`Enemy` 在 `OnEnable` 把自己注册进一个列表、`OnDisable` 注销，玩家只遍历这个列表（敌人会动态生成销毁时应选这条）。

   并且：检测频率能降就降；距离比较用 `sqrMagnitude` 不开方；结果用 `[SerializeField] private` + 只读属性对外暴露。
3. **写错时 lint 拦下**：真写成 `Update` 里 `FindObjectsOfType`，保存时 `project-lint` 的 `find-in-update` 规则会 exit 2 反馈给 AI 自纠。
4. **复查兜底**：`code-reviewer` 子代理应把「每帧全场景检索」标成 BLOCK 或 WARN。

注意第 3、4 两道防线**不在 `deterministic_checks` 的判定范围内**：判定只看最终落盘的代码。
lint 拦下后 AI 自己改对了，这条 case 照样算通过——这是对的，我们要的是最终产物对。

## 不通过时查什么（先判「是 AI 的问题还是规则没写清」）

| 现象 | 多半是 | 怎么修 |
| --- | --- | --- |
| 钩子没提示 `csharp-code.md` | 路由断了 | 查 `knowledge-routing.py` 的 glob 有没有覆盖该路径 |
| 提示了、AI 还是写在 `Update` 里 | **规则没写清** | 反模式表那一行不够醒目 / 不够具体；考虑把它升成更严的 lint 规则 |
| lint 没报 | 规则正则太窄 | 查 `rules.json` 里 `find-in-update` 的 `pattern` 有没有覆盖 `FindObjectsOfType`（复数）与 `FindAnyObjectByType`，`in_methods` 是不是漏了 `LateUpdate` |
| 只有 `public` 字段那条挂 | **AI 行为问题**，偶发 | 单条失败不值得改规则；连续两次挂再动 |

`deterministic_checks` 第 1、2 条用 `multiline` 匹配 `Update` 方法体（括号配平到三层），
**方法体嵌套超过三层时匹配不上会漏判**——那是有意的偏保守：宁可漏报也别误判。
