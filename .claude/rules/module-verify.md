---
description: 模块回放验证（Showcase）规范：目录命名、ShowcaseScenario 作者 API、编写约束、与快测试的分工、模块完成定义。编辑 Scripts/Tests/Showcase/ 下文件时适用。
paths: ["Assets/_Project/Scripts/Tests/Showcase/**"]
globs: ["Assets/_Project/Scripts/Tests/Showcase/**"]
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
| 回放舞台 | `Assets/Scenes/SampleScene.unity`（即 `ShowcaseOptions.DemoScenePath`），不建独立验证场景 |
| 报告 | `Logs/verify/<模块小写>/<yyyyMMdd-HHmmss>/report.md` + 截图 `NN-<名字>.png`，另复制一份 `latest.md` |

框架代码在 `Showcase/Framework/`，模板在 `Showcase/SelfTest/ShowcaseSelfTest.cs`。
写新模块就**复制 `ShowcaseSelfTest.cs` 到 `Showcase/<Module>/`**，改命名空间、类名、`Module`、`ScenePath`，再把步骤换成模块行为。

## 作者 API 速查（`ShowcaseScenario`，签名固定）

| 成员 | 干什么 |
| --- | --- |
| `protected abstract string Module { get; }` | 模块名，PascalCase 必填；报告目录取它的小写 |
| `protected virtual string ScenePath => null;` | 默认：走 Boot 真实流程进世界的回放返回 `null`，配 `EnterWorldFromTitle`；只叠加加载 SampleScene（不进世界，验框架机制用）才返回 `ShowcaseOptions.DemoScenePath` |
| `protected virtual bool LoadBootScene => true;` | 为真且 Boot 场景文件存在 → 先加载 Boot；`TearDown` 时自动退回标题并销毁 `GameLifetimeScope`（幂等） |
| `protected virtual IEnumerator WaitForBootReady()` | 默认等一帧；覆写为等到标题就绪，供 `EnterWorldFromTitle` 之前的阶段用 |
| `EnterWorldFromTitle(bootTimeout=20f, enterTimeout=20f, startStepTitle=null)` | 默认进场入口：等标题就绪 → 点「开始」→ 等进世界（场景已加载、`PlayerModel` 可解析）→ 等相机跟随 |
| `RequireTitleButton(string name)` | 取标题界面下的按钮（`StartButton`/`ContinueButton`/`LoadButton`…），找不到抛异常 |
| `BeginLeaveToTitle(IGameFlow flow, Action done)` | 用例中途经流程切回标题（验「继续」一类），完成时回调 `done` |
| `BootShutdownSettleSeconds` | `virtual float`，默认 0；收尾销毁根作用域后再等的真实秒数，离场有异步写盘时覆写 |
| `FindDeep<T>(Transform root, string name)` | 在 `root` 下递归按名字找组件（含未激活），找不到返回 `null` |
| `Input` | 虚拟手柄 + 键盘（懒建，第一次用任一设备就一起建）：`HoldStick`/`SetStick`/`ReleaseStick` 推摇杆，`Press`/`Hold`/`Release(action)` 按动作查绑定，不按键位 |
| `Input.Prime()` | 提前把手柄和键盘一起建出来；设备中途加入会复位已按住的动作，`EnterWorldFromTitle` 已经调用 |
| `Input.Press(action)` | 按下 → 保持 ≥2 帧且 ≥25 ms（跨过一次 60 Hz 逻辑 tick 采样，否则会被吞）→ 松开 |
| `Input.PressUntil(action, changed, maxSeconds=0.5f)` | 按住直到 `changed()` 成立再松开；Gameplay 开关键（走跑/伪装/攻击）首选 |
| `ClickWhenReady(what, button, effective, timeout=3f, retryInterval=0.3f)` | 界面刚打开点击会被控制器吞掉，按间隔重试点击直到 `effective()` 成立 |
| `PlayerPosition` | 玩家逻辑坐标（场景 XZ 的 `PlayerModel.Position`）；取不到返回 `(0,0)` 并警告 |
| `WalkTo(Vector2 target, stopDistance=1f, timeout=10f)` | 推摇杆走到目标附近；超时按 `WaitUntil` 口径记失败并继续，结束松杆等停稳 |
| `Walk(Vector2 direction, float seconds)` | 朝方向推摇杆走一段时间，结束松杆等停稳 |
| `EnterDemoWorld(readyWhat, resolveServices, missingMessage)` | Demo 专用进场：`EnterWorldFromTitle` 之后等遭遇逻辑激活且服务可解析，再建齐虚拟设备、摇杆回中 |
| `WalkRoute(waypoints, stopDistance=1f, timeoutPerLeg=10f)` / `GoToPatrolLookout()` | 按路线逐点走 / 切跑绕开村口触发区走到巡逻线观察点；坐标常量见 `ShowcaseScenario.DemoScene.cs` 注释 |
| `StrikeMonster(timeout=5f)` / `StrikeSwings` / `FaceMonster()` | 追上巡逻怪对准朝向并按攻击键；`StrikeSwings` 累计出手次数，区分「没按上」与「按上了没打中」 |
| `Step(string title, Action act = null, float hold = -1)` | 记一步 + 叠加层显示 + 执行 `act` + 停顿（默认 1.5 s × 倍率） |
| `Check(string expect, Func<bool> cond, float timeout = 0)` | 检查点；`timeout > 0` 则逐帧轮询。失败只记录不中断，继续往下 |
| `Snapshot(string name)` | 帧末截图进本次 run 目录，报告里带路径；批处理无图形时跳过 |
| `Wait(float seconds)` | 按倍率停顿 |
| `WaitUntil(string what, Func<bool> cond, float timeout)` | 等条件，超时记失败并继续 |
| `FindRequired<T>(string name)` | 按名字取组件，找不到直接 `Assert.Fail`（前置条件不满足，中断合理） |
| `EnsureCamera()` | 场上没相机就建一个正交相机；只给验框架机制、不验玩法的回放用（Replay/SelfTest 这类代码搭场景） |

节奏倍率由菜单 `21Days/验证/回放节奏/…` 写 EditorPrefs 控制（慢速 x2 / 标准 x1 / 快速 x0.25 / 不停顿 x0），
批处理下自动归零；`21Days/验证/打开最近报告目录` 直接打开报告根目录。所有回放日志带前缀 `[VERIFY]`。

## 编写规范

- **一条 `[UnityTest]` = 一个用户可见行为**，每条 3～10 步。步骤标题写「做了什么」，检查点写「应该看到什么」，
  中文，能直接念给策划听。
- **默认走真实输入路径**：`EnterWorldFromTitle` 进世界后用 `Input`（虚拟手柄/键盘，经 Input System 真实路径）像玩家一样操作；
  直达模块公开接口 / 事件是退路——用在模块没有输入路径、或要验的正是接口本身。仍然不 `GetComponent` 到私有实现、不改 SO；
  「不读 `Input.*`」指旧版 `UnityEngine.Input` 静态 API 与 `Input.touches`，走 `Input`/`WalkTo` 驱动的动作路径不算。
- 每步 hold 1～3 秒；**至少在末尾 `Snapshot` 一次**；`Check` 必须对应肉眼可见或数值可见的变化——
  检查一个屏幕上看不出区别的内部标志位，属于写错了地方，该去 EditMode。
- 回放统一在 `Assets/Scenes/SampleScene.unity` 上跑；**优先走 Boot 真实流程进场**（`EnterWorldFromTitle`，`ScenePath` 返回 `null`），
  只叠加加载 SampleScene（`ScenePath` 返回 `DemoScenePath`）留给验框架机制、不验玩法的回放；回放需要的物体作为 demo 内容放进 SampleScene，
  或由回放运行时生成并 `Track()` 清理；**不许改变从标题「开始」进 SampleScene 的正常游玩表现**。
- **移动真走不瞬移**：用 `WalkTo` / `Walk` 经虚拟摇杆走，不 `playerRules.Reset` 瞬移；长距离挪位（十几个单位以上、走路本身不是
  演示内容）可先 `Input.Press(Gameplay.Run)` 切跑再走。
- **虚拟输入不依赖窗口焦点**：基类临时使用 InputSettings 副本，设 `IgnoreFocus` / `AllDeviceInputAlwaysGoesToGameView` 并开启后台运行；TearDown 与退出 Play 恢复原设置。不要为修回放永久改项目输入设置；失焦回归见 `ShowcaseSelfTest.Keyboard_WhenGameViewUnfocused_RecolorsSquare`。
- SampleScene 里的距离是真的：出生点离长者 3、离巡逻怪 18，NPC 交互半径 2 且只认 `player`。回放先用 `WalkTo` / `GoToPatrolLookout` 走到位再交互，别假设物体在身边；去巡逻怪那边一律走北侧路线常量（`RouteToPatrol`），村口演出触发区（x 8..11、z 1..4）一进就冻住世界，别往那边走。
- 检查点失败**不用** `Debug.LogError` / `Assert`：Test Framework 会把未预期的 `LogError` 当测试失败并打断报告流程。
  失败走 `Debug.LogWarning` + 记录，收尾在 `ShowcaseTearDown` 里统一 `Assert.Fail`。
- `UnityEditor` 相关代码一律 `#if UNITY_EDITOR` 包住；`UnityEngine.Object` 判空用 `== null` / `!= null`。
- 框架落地后：异步接口用 `yield return task.ToCoroutine()`（UniTask）；Boot 就绪信号有了就覆写 `WaitForBootReady()`。
- 跨帧的等待一律用 `Check(..., timeout:)` 或 `WaitUntil(...)`，**不能靠 `Step` 的 hold 等表现自然发生**：批处理（CI）下节奏倍率为 0，hold 只剩一帧，靠它等的回放会在 CI 里误判失败。
- 多条用例都加载 Boot 的 Showcase：基类已自动处理——`LoadBootScene` 为真时 `ShowcaseTearDown` 会自动经流程退回标题并销毁所有
  `GameLifetimeScope`（幂等），子类**不用**再写 `[UnityTearDown]` 销毁；旧代码里手写的 `DestroyBootScope` 可以删。
- 要验证**瞬态**（正在打字、动画进行中、淡入未完成）时，触发它的那一步写 `Step(..., hold: 0f)` 并紧跟带 timeout 的 `Check`；默认 1.5 s 停顿足够让十几个字打完，检查点会红在「状态已经过去了」而不是功能坏了。
- 验证场景镜头固定时，回放各步的**累计位移要收尾回原点**（或来回对称）：小人一路向右走出画面，`Check` 靠读状态照样绿，但截图是空的，人看不到证据。改速度或时长前先算终点在不在画内。2026-09-26 拼接小人补走 / 跑两步时踩到。
- 从「开始」进场景的回放会经存档服务写 `slot1..N.json`：**不要自己碰 `IPlatformService.SaveRoot` 或自行备份 / 还原槽文件**——`ShowcaseScenario.ShowcaseSetUp/TearDown` 已经统一用 `PlatformServiceBase.SaveRootOverride` 把它重定向到临时目录并在收尾时清理，模块作者的 `[UnitySetUp]`/`[UnityTearDown]` 直接读 `platform.SaveRoot` 拿到的就是这个隔离目录。

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
   `ScenePath` 返回 `ShowcaseOptions.DemoScenePath`，回放需要的物体已放进 SampleScene 或由回放运行时生成并 `Track()`。
4. `/verify-module <Module>` PASS，**且开发者看过回放并点头**（对话里有记录）。
5. `ai-docs/docs/modules/<模块小写>/` guide 存在（`/generate-doc`），`modules.json` 已登记，guide 里写了 Showcase 路径与回放舞台说明。
6. `/review-change` 清单已列，停下等授权。

## 检查清单

- [ ] 文件路径、命名空间、类名、`Module` 四者对得上。
- [ ] 每条 `[UnityTest]` 只讲一个用户可见行为，3～10 步，末尾有 `Snapshot`。
- [ ] 优先走 `EnterWorldFromTitle` + `Input`/`WalkTo` 真实输入路径；直达公开接口 / 事件是退路。没有 `GetComponent` 私有实现、
      没有改 SO、没有读旧版 `Input.*`（`UnityEngine.Input` 静态 API）。
- [ ] 检查点对应看得见的变化；失败路径没有 `Debug.LogError` / 就地 `Assert`。
- [ ] 回放在 SampleScene 上跑，所需物体已作为 demo 内容放进 SampleScene 或由回放运行时生成并 `Track()` 清理；没有改变正常游玩表现。
- [ ] 不细粒度断言——那些在 EditMode 里。
- [ ] 没有靠 Step 的 hold 等跨帧结果；跨帧等待都走 Check(timeout) / WaitUntil。
