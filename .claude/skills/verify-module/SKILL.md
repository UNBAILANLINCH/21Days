---
name: verify-module
description: 在开着的 Unity 编辑器里跑某模块的回放场景（Showcase）：编译门 → EditMode 快门 → Game 视图逐步回放 → 检查点与截图 → 出报告，最后把视觉验收交回开发者
disable-model-invocation: true
---

# /verify-module <模块> [--manual]

模块名 PascalCase（`Player`、`Inventory`）。`/verify-module SelfTest` 跑**框架自检**——
`Showcase/SelfTest/ShowcaseSelfTest.cs` 不依赖任何玩法模块，用来确认回放框架本身是通的（新装环境、回放整体报错时先跑它）。

「回放」= 确定性的脚本化场景：按固定顺序调模块公开接口、中间插可观察的检查点，不是原始输入录制。
编写规范见 `.claude/rules/module-verify.md`，全流程规范见 `docs/module-dev-spec.md`。

> **Claude 不能替开发者做视觉验收。** 断言与截图只能证明「数值对、没报错」；「看起来对不对」只有开发者说了算。
> 汇报的最后一定要把判断权交回去，不许自己下「表现正常 / 手感没问题」的结论。

## 0. 判定编辑器与桥（不能跳）

`manage_scene(action="get_active")`。报 `No Unity Editor instances` → **停下**，给两条路让开发者选：

- 打开 Unity，`Window → MCP for Unity` 确认 bridge 运行中，然后重跑 `/verify-module <模块>`；
- 或者自己在 Test Runner 里跑 PlayMode + 程序集 `Game.Tests.Showcase`，跑完把
  `Logs/verify/<模块小写>/latest.md` 贴回来，Claude 只做第 8 步的汇报。

**不重试、不改 `.mcp.json`、绝不 `set_active_instance` 切到别的 Unity 实例**——那是别人的工程，会在别人的编辑器里进 Play。

## 1. 退出 Play

读资源 `mcpforunity://editor/state`；已在 Play 就 `manage_editor(action="stop")`（unity-mcp 纪律 4：Play 模式下不碰资产）。

## 2. 编译门

`refresh_unity(scope="scripts", compile="request", wait_for_ready=true)` → `read_console(action="get", types=["error"])`。
有编译错误 → 列 `文件:行` + 一句话中文说明，**停**。编译不过回放跑不起来，先修。

## 3. 静态门（只报告，缺 Showcase 或缺验证场景才停）

| 查什么 | 缺了怎么办 |
| --- | --- |
| `Assets/_Project/Scripts/Runtime/<Module>/` 存在 | 只提醒（模块名可能拼错了，顺手确认一下） |
| `Assets/_Project/Scripts/Tests/EditMode/<Module>/` 有测试 | 只提醒（DoD 第 2 条没达成） |
| `Assets/_Project/Scripts/Tests/Showcase/<Module>/<Module>Showcase.cs` 存在 | **停**：按 `.claude/rules/module-verify.md` 先写一条，从 `ShowcaseSelfTest.cs` 复制起步 |
| Showcase 的 `ScenePath` 非空（grep 该文件）时 `Assets/_Project/Scenes/Verify/<Module>.unity` 存在 | **停**：场景加载不到回放必失败，先用 MCP 建场景存盘（`manage_scene` + `manage_gameobject`） |
| `ai-docs/docs/modules/<模块小写>/` guide 存在且 `modules.json` 已登记 | 只提醒（DoD 第 5 条，收尾时 `/generate-doc`） |

`SelfTest` 跳过上表的 Runtime / EditMode / 场景 / 文档四项，只查 `ShowcaseSelfTest.cs` 在不在。

## 4. 快门（EditMode 先绿）

`manage_tools(action="activate", group="testing")` →
`run_tests(mode="EditMode", group_names=["^Game\\.Tests\\.EditMode\\.<Module>\\."], include_failed_tests=true)`
（工具参数是 JSON 字符串，正则里的点要写成 `\\.`） →
`get_test_job(job_id, wait_timeout=60)` 轮询到完成。

红了 → 列失败用例（名字 + 断言信息 + `文件:行`），**默认停**。细粒度规则先修好再看表现；
开发者说「照样跑」再往下走，并在最终汇报里标明快门是红的。

## 5. 开跑前告知开发者

原话模板（照抄，回放是要人盯着看的，不能悄悄开始）：

> 回放马上开始，请把 Unity 的 **Game 视图切到前台**盯着看。每步会停 1～3 秒，屏幕顶部叠加层显示第几步和检查结果；
> 觉得太慢 / 太快用菜单 `21Days/验证/回放节奏` 调倍率（慢速 x2 / 标准 x1 / 快速 x0.25 / 不停顿 x0），调完重跑。

然后 `read_console(action="clear")` 清掉旧日志，事后只看本次。

## 6. 回放

```
run_tests(mode="PlayMode", assembly_names=["Game.Tests.Showcase"],
          group_names=["^Game\\.Tests\\.Showcase\\.<Module>\\."],
          init_timeout=120000, include_failed_tests=true)
```

→ `get_test_job(job_id, wait_timeout=60)` 循环到完成。**期间不做任何别的 MCP 写操作**（编辑器正在 Play，改资产会被丢弃或写坏 SO）。

## 7. 收集

1. 读报告：`Logs/verify/<模块小写>/latest.md`（截图路径是相对的 `<run>/NN-xx.png`）。
2. `read_console(action="get", types=["error"], include_stacktrace=true)` —— 运行时异常。
3. 报告文件缺失时的退路：`read_console(filter_text="[VERIFY]", types=["all"])`。注意 Test Runner 进 Play 会触发域重载，
   MCP 读到的控制台缓冲常常是空的（实测如此）——所以**报告文件才是主证据**，退路拿不到东西就让开发者看 Unity 的 Console 窗口。
4. **用 Read 工具打开关键截图 PNG 看一眼**，与对应步骤的期望对照；对不上就在汇报里点名哪一张。

`Logs/` 已在 `.gitignore`，Claude 只读它，永不写、永不提交。

5. **查副作用**：`git status --short ProjectSettings/`。实测 MCP 的测试运行器会在跑测试时把
   `EditorSettings.asset` 的 Enter Play Mode Options 打开（域重载 / 场景重载关闭），跑完未必复原；
   Test Runner 首次建新场景还会生成 `SceneTemplateSettings.json`。这两处**不是回放框架需要的**，
   在汇报里列出来，由开发者决定恢复（`git checkout -- ProjectSettings/EditorSettings.asset`，Claude 不能替他跑）还是保留。

## 8. 汇报（固定格式）

```
结论：PASS —— 6 步全过，3 个检查点绿，无运行时异常。报告 Logs/verify/player/20260915-142233/report.md

| 场景 | 步骤 / 检查点 | 结果 | 截图 |
| --- | --- | --- | --- |
| TakeDamage_ShowsHealthDrop | 第2步 玩家受击 20 点 | ✓ | — |
| TakeDamage_ShowsHealthDrop | 检查 血量应为 80 | ✓ | 01-受击后.png |

运行时异常：无（有则逐条 `文件:行` + 一句话）
```

结论行写 PASS / FAIL 与一句话原因；表里一行一步或一检查点；异常单独列。**不贴整份报告，不贴堆栈全文。**
结尾必须原话交回视觉验收：

> 视觉表现请你在 Game 视图 / 截图里确认，告诉我哪一步不对。

## 9. 迭代闭环

开发者反馈 → 判断是玩法代码错还是回放步骤本身写错了 → 改动走 `/dev`（或 `/new-feature` 的实现步） → 重跑 `/verify-module <模块>`。
**PASS 且开发者点头**（对话里有记录）才算模块完成，再进 `/review-change`。开发者没点头就不算过，不许自行结案。

## 10. `--manual`（自己动手看）

跳过第 4～8 步：`manage_scene(action="load", path="Assets/_Project/Scenes/Verify/<Module>.unity")` →
`manage_editor(action="play")` → 开发者自己操作 → 说「好了」后 `read_console(action="get", types=["error"])` 汇总异常 →
`manage_editor(action="stop")`。**Play 期间不改任何资产**。Showcase 还没写、或想手感试玩时用它。

## 完成标准

报告已按第 8 步格式汇报，且已明确把视觉表现的验收交给开发者；
或者停在某一道门（桥 / 编译 / 缺 Showcase / 快门红）并说清是哪一道、为什么、下一步做什么。
