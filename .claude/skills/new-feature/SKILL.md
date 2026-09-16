---
name: new-feature
description: 新玩法模块的标准流程：定范围 → 设计要点 → 实现 → 接线 → 验证 → 待审清单
disable-model-invocation: true
---

# /new-feature <模块名>

**照着 `Assets/_Project/Scripts/Runtime/Sample/` 写**，文档照着 `ai-docs/docs/modules/sample/` 写。
那是端到端跑通框架每一层的样板，形状拿来即用。

第 1、2 步的三道门是 PRP 三道门的简版，完整说明与由来见 [`PRP/README.md`](../../../PRP/README.md#三道门)。

1. **定范围 + 定验收**：按 `CLAUDE.md` 目录约定，用一段话复述这个模块做什么、不做什么，列出将新建 / 修改的文件路径（全部在 `Assets/_Project/` 下），其中必须包含 `Scripts/Tests/Showcase/<Module>/<Module>Showcase.cs` 与 `Scenes/Verify/<Module>.unity`。有歧义的地方合并成一次提问，问完再动手。
   - **门一：这一步只写「什么」，不写「怎么」。** 出现类名、方法名、具体 API、「用某某模式实现」就是越界，那属于第 2 步。越界的害处是把实现方案伪装成需求，后面没人再质疑它。
   - **门二：验收标准在这一步就写死**，3 到 6 条，每条要么机器可判（测试断言、lint 规则、`invariants.py` 能扫的）、要么肉眼可验（Showcase 回放里看得见的）。**写完对一遍：后面每个任务至少覆盖其中一条，没被任何任务覆盖的验收标准，要么删掉要么补任务。**
2. **设计要点**：3 到 6 条。哪些数据进 ScriptableObject、运行时类各自的职责、依赖方向、与已有模块的接口。只写决定，不写实现。若模块已有 `ai-docs/docs/modules/<模块小写>/` guide，先读。
   - **门三：设计出完停一次，等用户点头再往下走。** 不要把设计和任务拆解一口气输出——两样一起端上来，人只会审任务不审架构，等发现架构不对时任务已经做了一半。
3. **建骨架**：菜单 **`21Days/工程/创建模块骨架…`**（输入 PascalCase 模块名）建出 `Scripts/Runtime/<Module>/`、`Scripts/Tests/EditMode/<Module>/`、`Data/<Module>/` 与占位规则类；编辑器没开就手写同样的结构。**模块不单独建 asmdef**，`Game.Runtime` 已经有了；确认 `Game.Core` / `Game.Runtime` 在 `Game.Tests.Showcase.asmdef` 的 `references` 里，否则 Showcase 引用不到模块类型。
4. **实现**：顺序是规则类 → 意图 → 配置 → 状态 → 面板 → 注册器。四条硬约束：
   - **规则类必须是纯 C#**（不继承 MonoBehaviour、不碰 `UnityEngine.Time`、不读单例），这样能写 EditMode 测试、将来能搬服务端（architecture.md 第 7 节）。MonoBehaviour 只做表现。
   - **状态改变走意图对象**（`readonly struct`），不在 UI 里直接改字段。
   - **面板预制体放 `Prefabs/UI/`，Addressables 地址等于面板类名**，加进 `UI` 组；玩法场景加进 `Scenes` 组，地址别和类名撞。面板不注入服务，事件往外抛由状态接住。
   - **模块靠 `GameplayInstaller` 注册进根作用域**（继承它，组件挂到 Boot 场景的 `GameBootstrap` 物体上）。**不要用玩法场景里的子作用域注册状态**：`GameFlow` 从根 `IObjectResolver` 解析状态类型，而且标题界面时玩法场景还没加载，必然解析失败。
5. **接线**：Unity MCP 已连接就用它建物体、挂组件、赋引用；未连接就只写脚本，把接线步骤列给用户在编辑器里做。接完跑一次 `21Days/工程/资产体检` 确认没有缺 `.meta` / 丢脚本 / 漏进 Addressables。
6. **埋点**：跑 `/instrument-module <模块>`——按 `docs/telemetry.md` 2.2 的四类尺子（意图入口 / 状态迁移 / 失败分支 / 长耗时）把该埋的补上，**每帧触发的一律不埋**。规则类只注入 `ITelemetryScope` 接口、由 Installer 工厂式喂进去，别把 Unity 依赖带进纯 C#（写法见该 skill 第 4 节）。埋完测试要跟着改（构造函数多了参数）。
7. **验证**：控制台零编译错误；规则类写 EditMode 测试并跑 `/unity-test`（假服务照 `SampleRulesTests.FakeConfigService` 写几行即可）。保存时 project-lint 自动跑，零违规；把改动范围交给 `code-reviewer` 子代理（model sonnet）做模块级审查。再跑 `/verify-module <模块>`，**开发者看过回放点头才算过**（写法见 `.claude/rules/module-verify.md`）。
8. **收尾**：`/generate-doc <模块>` 生成文档三件套，在 `ai-docs/docs/catalog.md` 补一行、在 `.claude/skills/generate-doc/modules.json` 登记一条；跑 `/review-change`，停下等审。

完成标准：第 1 步列出的每个文件都已落地，或逐个说明未做原因；控制台零编译错误；埋点已按四类尺子补过（`/instrument-module` 的清单里每条候选点都有结论，project-lint 的埋点提醒已消失）；`/verify-module <模块>` PASS 且开发者已点头；文档三件套与登记已就位；清单已列出且未提交。
