# 模块开发规范

> 一个玩法模块从「想做」到「可以提交」要走哪些步骤、每步的产物在哪、什么时候算做完。
> 面向人（含其他开发者）也面向 AI。约束细则在 [`.claude/rules/module-verify.md`](../.claude/rules/module-verify.md)，
> 命令流程在 [`.claude/skills/verify-module/SKILL.md`](../.claude/skills/verify-module/SKILL.md)，
> 框架层设计在 [`architecture.md`](architecture.md)。

## 1. 一个模块的生命周期

| # | 做什么 | 用哪个命令 | 产物在哪 |
| --- | --- | --- | --- |
| 1 | **定范围**：做什么、不做什么、要建改哪些文件 | `/new-feature <模块>` 第 1 步 | 对话里的文件清单 |
| 2 | **设计要点**：数据进不进 SO、类的职责、依赖方向、对外接口 | `/new-feature` 第 2 步 | 对话里的 3～6 条决定 |
| 3 | **实现**：先数据与接口，再 MonoBehaviour | `/new-feature` 第 3 步 / `/dev` | `Assets/_Project/Scripts/Runtime/<Module>/` |
| 4 | **单测**：纯逻辑规则、边界、分支 | `/unity-test EditMode` | `Scripts/Tests/EditMode/<Module>/` |
| 5 | **Showcase**：把「用户看得见的行为」写成回放，在 SampleScene 上跑 | 手写 + 按需 MCP 补 demo 物体 | `Scripts/Tests/Showcase/<Module>/<Module>Showcase.cs` |
| 6 | **跑回放**：编译门 → 快门 → Game 视图回放 → 报告 | `/verify-module <模块>` | `Logs/verify/<模块小写>/…`（已 gitignore） |
| 7 | **开发者看回放反馈**：哪一步表现不对 | 人看，对话里说 | 对话记录 |
| 8 | **迭代**：改玩法代码或改回放步骤，重跑 | `/dev` → `/verify-module` | 同上 |
| 9 | **文档三件套** | `/generate-doc <模块>` | `ai-docs/docs/modules/<模块小写>/` |
| 10 | **待审提交** | `/review-change` | 改动清单，停下等授权 |

第 6～8 步是个环，转到开发者点头为止。第 9 步的 guide 里要写上 Showcase 路径与回放舞台说明
（见 [`ai-docs/docs/modules/README.md`](../ai-docs/docs/modules/README.md)）。

**回放舞台 = 实现模板**：Showcase 在 SampleScene 上演示的接法（挂哪些组件、引用哪些配置资产、触发怎么接）与表现，
就是该模块在正式场景里的标准做法；正式场景接同一功能要与之对齐，只改内容不改接法。所以第 5 步要用的物体优先作为
demo 内容放进 SampleScene，而不是回放里临时生成——生成得出来的东西，正式场景往往也得自己接一遍，等于接法没有模板可抄。

## 2. 模块完成定义（DoD）

六条全成立才算「做完」：

1. 代码在 `Scripts/Runtime/<Module>/`，命名空间 `Game.<Module>`，project-lint 零违规，`code-reviewer` 无 BLOCK。
2. 核心规则有 EditMode 测试（`Scripts/Tests/EditMode/<Module>/`），全绿。
3. 有 `Scripts/Tests/Showcase/<Module>/<Module>Showcase.cs`（≥1 条场景），每条 3～10 步，覆盖「用户能看见的主要行为」；
   优先走 Boot 真实流程进场（`ScenePath` 返回 `null` + `EnterWorldFromTitle`），只叠加加载 SampleScene（`DemoScenePath`）留给
   验框架机制、不验玩法的回放；所需物体放进 SampleScene 或由回放运行时生成并 `Track()`。
4. `/verify-module <Module>` PASS，**且开发者看过回放并点头**（对话里有记录）。
5. `ai-docs/docs/modules/<模块小写>/` guide 存在，`modules.json` 已登记，guide 里写了 Showcase 路径与回放舞台说明。
6. `/review-change` 清单已列，停下等授权。

第 4 条是这套规范的重点：**自动化能证明「数值对、没报错」，证明不了「看起来对」**。
AI 不做视觉验收，只负责把回放跑出来、把报告和截图摆到你面前。

## 3. 「回放」是什么，为什么不录原始输入

**回放 = 确定性的脚本化场景**：按固定顺序、固定节奏调用模块的公开接口 / 动作，中间插可观察的检查点，
每步停 1～3 秒，让你在 Game 视图里看清发生了什么。

不做「录制原始输入再重播」，理由：

- 原始输入回放脆：帧率、分辨率、随机种子、异步加载时序任一变化就对不上，维护成本比重写脚本高。
- 录制出来的东西**读不懂**——出问题时只能反复看，无法定位是哪一步的哪个期望没满足。脚本化回放的每一步都有标题和期望。

已落地的能力：

- **Input System 动作驱动**：`ShowcaseScenario.Input`（虚拟手柄 + 键盘）与 `WalkTo` / `Walk` 已经把「喂 Action 而不是按键」这层做出来了，
  走的是 Input System 真实路径（虚拟设备 → 动作图 → `LiveInputSource` → 玩家规则），**是默认做法**，不是将来的扩展点。
- **UniTask**：模块的异步接口用 `yield return task.ToCoroutine()` 接进回放。
- **Boot 就绪信号**：`EnterWorldFromTitle` 已经等到标题就绪、点「开始」、等进世界、等相机跟随，Boot 就绪等待已由它承担；
  模块特有的就绪条件（HUD、存档槽……）在它之后自己 `WaitUntil`。

## 4. 怎么写一条 Showcase

### 从模板复制起步

1. 复制 `Assets/_Project/Scripts/Tests/Showcase/SelfTest/ShowcaseSelfTest.cs` 到 `Showcase/<Module>/<Module>Showcase.cs`。
2. 改命名空间为 `Game.Tests.Showcase.<Module>`，类名为 `<Module>Showcase`。
3. 改 `Module` 返回模块名（PascalCase）；`ScenePath` 默认返回 `null`，用例开头 `yield return EnterWorldFromTitle();` 走 Boot 真实流程进世界；
   不进世界、只叠加加载 SampleScene 验框架机制时才返回 `ShowcaseOptions.DemoScenePath`。
4. 把自检的步骤换成模块行为：一条 `[UnityTest]` 讲一个用户可见行为，3～10 步。
5. 需要 demo 物体就用 MCP 把它加进 `Assets/Scenes/SampleScene.unity`（预制体实例化，不堆 override），存盘；或在回放里运行时生成并 `Track()` 清理。
6. `Game.Core` / `Game.Runtime` 已存在时，确认它们在 `Game.Tests.Showcase.asmdef` 的 `references` 里，否则引用不到模块类型。
7. 跑 `/verify-module <模块>`。

### 示例

```csharp
namespace Game.Tests.Showcase.Player
{
    [Category("Showcase")]
    public class PlayerShowcase : ShowcaseScenario
    {
        protected override string Module => "Player";
        protected override string ScenePath => null; // 世界由 Boot 流程加载，不叠加一份 SampleScene

        [UnityTest]
        public IEnumerator Sneak_LowersMoveSpeed()
        {
            yield return EnterWorldFromTitle();
            var input = ResolveService<IInputService>();
            var player = ResolveService<PlayerModel>();

            yield return WalkTo(new Vector2(7.5f, 4.9f));
            yield return Snapshot("走到目标点");

            yield return Step("按住潜行键（Gameplay/Sneak）", null, 0f);
            yield return Input.Hold(input.Actions.Gameplay.Sneak);
            yield return Check("玩家进入潜行状态", () => player.IsSneaking, 2f);
            yield return Snapshot("潜行中");

            yield return Input.Release(input.Actions.Gameplay.Sneak);
            yield return Check("松开后退出潜行", () => !player.IsSneaking, 2f);
        }
    }
}
```

逐行：

| 行 | 在干什么 |
| --- | --- |
| `[Category("Showcase")]` | 打上分类，`/verify-module` 与 Test Runner 靠它把回放与快测试分开 |
| `: ShowcaseScenario` | 继承基类，拿到进场 / 收尾 / 虚拟输入 / 叠加层 / 截图 / 报告 |
| `Module => "Player"` | 模块名，PascalCase；报告落到 `Logs/verify/player/` |
| `ScenePath => null` | 世界由 Boot 流程加载（标题「开始」→ 进 SampleScene）；只叠加加载 SampleScene 才返回 `ShowcaseOptions.DemoScenePath` |
| `EnterWorldFromTitle()` | 走 Boot 真实流程进世界：等标题就绪 → 点「开始」→ 等进世界 → 等相机跟随 |
| `ResolveService<IInputService>()` / `ResolveService<PlayerModel>()` | 从容器取输入服务与玩家模型（只读状态），不摸私有实现 |
| `WalkTo(...)` | 推虚拟摇杆走到目标点，到位松杆等停稳——真走，不是瞬移 |
| `Input.Hold(...)` / `Input.Release(...)` | 按住 / 松开某个动作（按动作查绑定，不按键位） |
| `Step("按住潜行键…", null, 0f)` | 记一步、叠加层显示标题；`hold: 0f` 因为紧跟着带 timeout 的 `Check` 自己等 |
| `Check(...)` | 检查点：过了绿、没过红并多停一会儿；**失败不中断**，整条跑完才统一报 |
| `Snapshot(...)` | 帧末截图存进本次 run 目录，报告里带路径 |

约束（完整版见 [`module-verify.md`](../.claude/rules/module-verify.md)）：默认走 `EnterWorldFromTitle` + `Input`/`WalkTo` 真实输入路径，
直达模块公开接口 / 事件是退路；不摸私有实现、不改 SO；「不读 `Input.*`」指旧版 `UnityEngine.Input` 静态 API；断言少而准，细粒度规则留给 EditMode。

## 5. 报告、截图与节奏

报告落在 `Logs/verify/<模块小写>/<yyyyMMdd-HHmmss>/report.md`，截图同目录 `NN-<名字>.png`；
最近一次另复制成 `Logs/verify/<模块小写>/latest.md`。`Logs/` 已在 `.gitignore`，不进仓库，AI 只读不写。

```markdown
# Player 模块验证回放报告

- 生成时间：2026-09-15 14:22:33
- 回放批次：20260915-142233
- 节奏倍率：x1
- 结论：**FAIL** —— 检查点失败 1 个，运行时异常 0 条

## TakeDamage_ShowsHealthDrop

| # | 类型 | 内容 | 结果 | 秒 |
| ---: | --- | --- | :---: | ---: |
| 1 | 步骤 | 第1步 · 玩家受击 20 点 | · | 0.0 |
| 2 | 检查 | 血量应为 80 | ✓ | 1.5 |
| 3 | 截图 | 受击后（`20260915-142233/01-受击后.png`） | · | 1.6 |
| 4 | 步骤 | 第2步 · 等待回血 | · | 1.7 |
| 5 | 检查 | 血量回到 100 | ✗ | 7.7 |

## 运行时异常

无
```

节奏用编辑器菜单调，写进 EditorPrefs，下次沿用：

| 菜单 | 效果 |
| --- | --- |
| `21Days/验证/回放节奏/慢速 (x2)` | 停顿翻倍，看细节 |
| `21Days/验证/回放节奏/标准 (x1)` | 默认 |
| `21Days/验证/回放节奏/快速 (x0.25)` | 只想确认没红 |
| `21Days/验证/回放节奏/不停顿 (x0)` | 当回归测试跑（批处理下自动是这个） |
| `21Days/验证/打开最近报告目录` | 打开 `Logs/verify/` |

## 6. `/verify-module` 一次跑下来你会看到什么

```
桥与编辑器判定  →  退出 Play  →  编译门        →  EditMode 快门
（没连就停）        （在 Play 就停止）  （有错列文件:行停）   （红了默认停）
        ↓
「请把 Game 视图切到前台」→  Game 视图里逐步回放（顶部叠加层显示第几步 / ✓✗ / 倍率）
        ↓
汇报：结论行 + 步骤表 + 异常列表 + 报告路径  →  **你来点头或指出哪一步不对**
```

前四道是门，哪一道不过就停在那里并说清原因，不会硬往下跑。最后一步永远是把判断权交给你。

## 7. 与框架脚手架的衔接

框架层（[`architecture.md`](architecture.md)）落地时有三件事要跟着做：

1. `Game.Core` / `Game.Runtime` 建出来后，把 `"Game.Core"`、`"Game.Runtime"` 加进 `Game.Tests.Showcase.asmdef` 的 `references`
   （建之前故意不引用：引用不存在的程序集会让整个程序集不编译）。
2. Boot 场景的就绪信号定下来后，覆写 `WaitForBootReady()`，让回放等框架服务初始化完再开始。
3. `docs/developer-guide.md` 定稿时把本文链进去，作为「写一个模块」的入口。

## 8. 常见问题

| 现象 | 多半是 | 怎么办 |
| --- | --- | --- |
| `No Unity Editor instances` | 编辑器没开，或 bridge 没启动 | 打开 Unity，`Window → MCP for Unity` 确认 bridge 运行中再重跑；不重试、不改 `.mcp.json` |
| 回放没开始就报错 / 场景是空的 | 编辑器还停在 Play 模式，或所需 demo 物体没放进 SampleScene | 先停止 Play；确认 `Assets/Scenes/SampleScene.unity` 里有回放需要的对象，或改成运行时生成并 `Track()` |
| 截图全黑 | `Snapshot` 撞上帧边界，或 Game 视图被别的窗口完全挡住 | 保证 Game 视图可见；`Snapshot` 前加一步 `Wait(0.2f)`；批处理无图形时截图本来就跳过 |
| 某个检查点一直红 | 期望写错了（比如查了看不见的内部状态），或模块行为确实不对 | 先看报告里那一步的截图对不对；只在数值上说不通的检查点，该挪去 EditMode |
| 回放太慢等不住 | 节奏倍率是标准或慢速 | 菜单切 `快速 (x0.25)`；确认没红之后再切回标准细看 |
| 回放卡住不动，切回 Unity 窗口才继续 | Player 设置的 Run In Background 被关了，编辑器失焦时 Play 停止更新 | 保持 `ProjectSettings.asset` 里 `runInBackground: 1`（仓库默认如此）；或回放期间让 Unity 窗口保持前台 |
| `/unity-test PlayMode` 变得很慢 | 把 Showcase 也跑进去了 | 传 `assembly_names` 排除 `Game.Tests.Showcase`；回放只走 `/verify-module` |
