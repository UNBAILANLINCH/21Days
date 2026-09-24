---
description: 模块回放验证（Showcase）规范：目录命名、ShowcaseScenario 作者 API、编写约束、与快测试的分工、模块完成定义。编辑 Scripts/Tests/Showcase/ 或 Scenes/Verify/ 下文件时适用。
paths: ["Assets/_Project/Scripts/Tests/Showcase/**", "Assets/_Project/Scenes/Verify/**"]
globs: ["Assets/_Project/Scripts/Tests/Showcase/**", "Assets/_Project/Scenes/Verify/**"]
alwaysApply: false
---

# 模块回放验证（Showcase）规则

Showcase 是**给人看的回放**：一条 `[UnityTest]` 按固定顺序调模块的公开接口，每步停 1～3 秒让开发者在 Game 视图里
亲眼看到表现，中间插检查点断言并截图，跑完出一份 markdown 报告。它复用 Unity Test Framework 当回放引擎，
住在独立测试程序集 `Game.Tests.Showcase` 里（带 `[Category("Showcase")]`），由 `/verify-module <模块>` 驱动。
不是原始输入录制（脆、跨分辨率不稳），也不是单元测试的替代品。

## 目录与命名

| 项 | 固定写法 |
| --- | --- |
| 文件位置 | `Assets/_Project/Scripts/Tests/Showcase/<Module>/<Module>Showcase.cs` |
| 命名空间 | `Game.Tests.Showcase.<Module>` |
| 类名 | `<Module>Showcase : ShowcaseScenario`，带 `[Category("Showcase")]` |
| 测试方法 | `[UnityTest] public IEnumerator <行为>_<期望>()`，如 `TakeDamage_ShowsHealthDrop` |
| 验证场景 | `Assets/_Project/Scenes/Verify/<Module>.unity`（用 MCP 建，不进 Build Settings） |
| 报告 | `Logs/verify/<模块小写>/<yyyyMMdd-HHmmss>/report.md` + 截图 `NN-<名字>.png`，另复制一份 `latest.md` |

框架代码在 `Showcase/Framework/`，模板在 `Showcase/SelfTest/ShowcaseSelfTest.cs`。
写新模块就**复制 `ShowcaseSelfTest.cs` 到 `Showcase/<Module>/`**，改命名空间、类名、`Module`、`ScenePath`，再把步骤换成模块行为。

## 作者 API 速查（`ShowcaseScenario`，签名固定）

| 成员 | 干什么 |
| --- | --- |
| `protected abstract string Module { get; }` | 模块名，PascalCase 必填；报告目录取它的小写 |
| `protected virtual string ScenePath => null;` | 非空则 SetUp 里加载它；返回 `null` = 场景在代码里搭 |
| `protected virtual bool LoadBootScene => true;` | 为真且 Boot 场景文件存在 → 先加载 Boot 再加载验证场景 |
| `protected virtual IEnumerator WaitForBootReady()` | 默认等一帧；框架 Boot 就绪信号落地后覆写它 |
| `Step(string title, Action act = null, float hold = -1)` | 记一步 + 叠加层显示 + 执行 `act` + 停顿（默认 1.5 s × 倍率） |
| `Check(string expect, Func<bool> cond, float timeout = 0)` | 检查点；`timeout > 0` 则逐帧轮询。失败只记录不中断，继续往下 |
| `Snapshot(string name)` | 帧末截图进本次 run 目录，报告里带路径；批处理无图形时跳过 |
| `Wait(float seconds)` | 按倍率停顿 |
| `WaitUntil(string what, Func<bool> cond, float timeout)` | 等条件，超时记失败并继续 |
| `FindRequired<T>(string name)` | 按名字取组件，找不到直接 `Assert.Fail`（前置条件不满足，中断合理） |
| `EnsureCamera()` | 场上没相机就建一个正交相机，给「代码搭场景」的作者用 |

节奏倍率由菜单 `21Days/验证/回放节奏/…` 写 EditorPrefs 控制（慢速 x2 / 标准 x1 / 快速 x0.25 / 不停顿 x0），
批处理下自动归零；`21Days/验证/打开最近报告目录` 直接打开报告根目录。所有回放日志带前缀 `[VERIFY]`。

## 编写规范

- **一条 `[UnityTest]` = 一个用户可见行为**，每条 3～10 步。步骤标题写「做了什么」，检查点写「应该看到什么」，
  中文，能直接念给策划听。
- 只通过模块的**公开接口 / 事件**驱动：不 `GetComponent` 到私有实现，不改 ScriptableObject 字段，不读 `Input.*`。
- 每步 hold 1～3 秒；**至少在末尾 `Snapshot` 一次**；`Check` 必须对应肉眼可见或数值可见的变化——
  检查一个屏幕上看不出区别的内部标志位，属于写错了地方，该去 EditMode。
- 不依赖 `Assets/Scenes/SampleScene.unity`；验证场景只放该模块需要的最少对象，可复用的做成预制体实例，不堆 override。
- 检查点失败**不用** `Debug.LogError` / `Assert`：Test Framework 会把未预期的 `LogError` 当测试失败并打断报告流程。
  失败走 `Debug.LogWarning` + 记录，收尾在 `ShowcaseTearDown` 里统一 `Assert.Fail`。
- `UnityEditor` 相关代码一律 `#if UNITY_EDITOR` 包住；`UnityEngine.Object` 判空用 `== null` / `!= null`。
- 框架落地后：异步接口用 `yield return task.ToCoroutine()`（UniTask）；Boot 就绪信号有了就覆写 `WaitForBootReady()`。
- 跨帧的等待一律用 `Check(..., timeout:)` 或 `WaitUntil(...)`，**不能靠 `Step` 的 hold 等表现自然发生**：批处理（CI）下节奏倍率为 0，hold 只剩一帧，靠它等的回放会在 CI 里误判失败。
- 多条用例都加载 Boot 的 Showcase：`GameBootstrap` 只有 `DontDestroyOnLoad`、没有重复实例保护，第二条用例起会叠出多套容器 / UIRoot / EventSystem。加 `[UnityTearDown]` 按类型名找到所有 `GameLifetimeScope` 并 `Destroy` 其 GameObject、等一帧，参考 `Assets/_Project/Scripts/Tests/Showcase/Dialogue/DialogueShowcase.cs` 的 `DestroyBootScope`。
- 要验证**瞬态**（正在打字、动画进行中、淡入未完成）时，触发它的那一步写 `Step(..., hold: 0f)` 并紧跟带 timeout 的 `Check`；默认 1.5 s 停顿足够让十几个字打完，检查点会红在「状态已经过去了」而不是功能坏了。

## 与 EditMode / PlayMode 快测试的分工

| | 管什么 | 跑法 |
| --- | --- | --- |
| EditMode（`Game.Tests.EditMode`） | 纯逻辑规则、边界值、分支穷举——**断言多而细** | `/unity-test EditMode` |
| PlayMode（`Game.Tests.PlayMode`） | 必须过 Unity 生命周期的快测试（物理、协程、场景加载），无停顿 | `/unity-test PlayMode` |
| Showcase（`Game.Tests.Showcase`） | 用户**看得见**的主要行为，带停顿与截图给人看 | `/verify-module <模块>` |

**回放不是单测**：断言少而准，一条回放里 3～5 个检查点足够；细粒度规则一律留给 EditMode。
Showcase 慢，`/unity-test PlayMode` 想只跑快测试就用 `assembly_names` 把它排除掉。

## 模块完成定义（DoD，六条全成立才算做完）

1. 代码在 `Scripts/Runtime/<Module>/`，命名空间 `Game.<Module>`，project-lint 零违规，`code-reviewer` 无 BLOCK。
2. 核心规则有 EditMode 测试（`Scripts/Tests/EditMode/<Module>/`），全绿。
3. 有 `Scripts/Tests/Showcase/<Module>/<Module>Showcase.cs`（≥1 条场景），每条 3～10 步，覆盖该模块「用户能看见的主要行为」；
   需要场景的有 `Scenes/Verify/<Module>.unity`。
4. `/verify-module <Module>` PASS，**且开发者看过回放并点头**（对话里有记录）。
5. `ai-docs/docs/modules/<模块小写>/` guide 存在（`/generate-doc`），`modules.json` 已登记，guide 里写了 Showcase 与验证场景路径。
6. `/review-change` 清单已列，停下等授权。

## 检查清单

- [ ] 文件路径、命名空间、类名、`Module` 四者对得上。
- [ ] 每条 `[UnityTest]` 只讲一个用户可见行为，3～10 步，末尾有 `Snapshot`。
- [ ] 只走公开接口 / 事件；没有 `GetComponent` 私有实现、没有改 SO、没有读 `Input.*`。
- [ ] 检查点对应看得见的变化；失败路径没有 `Debug.LogError` / 就地 `Assert`。
- [ ] 验证场景不依赖 SampleScene，只放最少对象，`.meta` 已由 Unity 生成。
- [ ] 不细粒度断言——那些在 EditMode 里。
- [ ] 没有靠 Step 的 hold 等跨帧结果；跨帧等待都走 Check(timeout) / WaitUntil。
