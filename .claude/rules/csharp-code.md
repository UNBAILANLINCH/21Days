---
description: Unity C# 编码规范：命名、序列化、生命周期、性能反模式。编辑任何 .cs 文件时适用。
paths: ["**/*.cs"]
globs: ["**/*.cs"]
alwaysApply: false
---

# Unity C# 编码规范

## 命名与结构

- 命名空间 `Game.<Module>`，与目录一致；一个文件一个类，文件名等于类名。
- 类 / 方法 / 属性 `PascalCase`；私有字段 `camelCase`，不加 `_` 前缀，不加 `m_` 前缀；常量 `PascalCase`。
- MonoBehaviour 以职责命名（`PlayerMovement`），不用 `Manager` 兜底；ScriptableObject 配置类以 `Config` / `Data` 结尾。
- 事件用 `event Action<T>` 或 `UnityEvent`，命名 `OnXxx`。

## 序列化与暴露面

- Inspector 可调字段一律 `[SerializeField] private`；对外只读用属性 `public T Xxx => xxx;`。**不用 public 字段**。
- 数值配置进 ScriptableObject（`Data/`），MonoBehaviour 里不写死魔法数字。
- 引用优先在 Inspector 里拖赋，其次 `GetComponent` 在 `Awake` 缓存；不在 `Update` 里 `Find` / `GetComponent`。

## 生命周期

- `Awake`：自身引用与初始化；`Start`：依赖其他对象的初始化；`OnEnable/OnDisable` 成对订阅/退订事件。
- 空的 `Update` / `FixedUpdate` / `LateUpdate` 直接删掉，留着也有调用开销。
- 帧内不 `new` 大对象、不 `string` 拼接日志；`Debug.Log` 不放在每帧路径上。
- 协程持有句柄（`Coroutine`），`OnDisable` 时 `StopCoroutine`；不裸 `StartCoroutine` 丢句柄。

## 反模式（project-lint 会拦）

| 反模式 | 改法 |
| --- | --- |
| `public` 字段暴露到 Inspector | `[SerializeField] private` + 属性 |
| `Update` 里 `GameObject.Find` / `FindObjectOfType` / `GetComponent` | `Awake` 缓存或 Inspector 引用 |
| `gameObject.tag == "X"` | `CompareTag("X")` |
| `SendMessage` / `BroadcastMessage` | 直接引用、接口或事件 |
| `Update` 里 `Debug.Log` | 去掉，或改事件触发时打印 |
| 代码/注释里出现本机绝对路径 | 用相对路径或 `Application.dataPath` |

误报时在该行末尾写 `// lint-ok: <理由>` 放行。

## 检查清单

- [ ] 命名空间、文件名、类名三者一致。
- [ ] 无 public 字段；配置数值在 ScriptableObject。
- [ ] 每帧路径无 Find / GetComponent / Log / 分配。
- [ ] 订阅与退订成对；协程有句柄。
