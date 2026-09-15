---
name: instrument-module
description: 给一个玩法模块补齐埋点：scan.py 按 docs/telemetry.md 2.2 的四类尺子扫出候选点 → 逐条判断该不该埋 → 用 Edit 补代码 → 跑 lint 与 EditMode 测试 → 列清单待审不提交。脚本只定位，代码由模型改。
disable-model-invocation: true
---

# /instrument-module <模块>

模块名 PascalCase（`Sample`、`Player`），对应 `Assets/_Project/Scripts/Runtime/<模块>/`。

**尺子在 [`docs/telemetry.md`](../../../docs/telemetry.md) 第 2.2 节，本文件不另立标准**，只管「怎么把该埋的补上」。

## 分工（这条决定了整个流程的形状）

    scan.py  定位候选点 + 报告      ← 机械的部分
    模型     判断该不该埋、埋什么值  ← 需要读懂上下文的部分，用 Edit 改代码

**不存在自动改写 C# 的脚本，也不要去写一个。** 正则改源码必踩嵌套、泛型、字符串字面量、`#if` 四个坑；
更要命的是埋点的价值全在「属性里放的是不是根因分析用得上的那几个数」——那是语义判断。
一个会把埋点插错地方的脚本比没有脚本更坏：它让人以为这件事已经自动化了。

## 载体锚定（`.claude/rules/harness-authoring.md` 要求）

- **执行载体**：`/instrument-module <模块>`（人主动敲）+ `/new-feature` 的埋点步（验证之前）显式调用。
  另有 project-lint 规则 `module-missing-telemetry`（**WARN，不拦**）在保存 `*State.cs` / `*Intent.cs` 时
  提醒「这个模块一条埋点都没有」。**不挂钩子自动改代码、不定时跑、不生成任何要人维护的清单文件。**
- **状态锚点**：`python .claude/skills/instrument-module/scan.py --selftest`（20 条断言，几秒出结论，不需要 Unity）。
- **退场条件**：selftest 红了没人修，或者连着几个模块扫出来全是误报、人人跳过它 —— 那就删掉这一层，
  四类尺子留在 `docs/telemetry.md` 里靠人工照做，别加提醒。

---

## 1. 扫

```bash
python .claude/skills/instrument-module/scan.py <模块>
```

输出四块：**应埋未埋** / 已被现有埋点覆盖的候选点 / 现有埋点调用 / 跳过（每帧）。

清单是**线索不是判决**。脚本靠命名约定与结构特征认，不做语义分析，会漏也会错，
所以每条都带「依据」——三秒钟自己判一次，别照单全补。

## 2. 逐条判断该不该埋

**宁可少埋也不要埋成流水账。** 对每个候选点问一句：

> 线上出了事故，这条事件能帮我回答什么问题？答不上来就别埋。

硬的几条：

| 情况 | 结论 |
| --- | --- |
| 每帧 / 高频触发（移动、动画、`Update` 里的判定） | **一律不埋**（契约 2.2「不该埋」）。要每帧数据用 `core.perf` 采样 |
| 参数守卫（`ArgumentNullException`、`NotImplementedException`） | 不埋。那是程序员错误，不是玩法失败分支（scan.py 已经替你滤掉） |
| 框架已经埋过的（状态进出、资源加载、面板开关、存档、Unity 报错） | 不重复埋，见契约 2.1 那张表 |
| 成功路径 | 一个流程**只埋入口一条**，中间步骤不埋 |
| 失败分支 | **每个分支都埋**，并把判定用到的数值写进 `p`——失败比成功值钱 |
| 同一方法里既是状态迁移又是长耗时 | 一条 `BeginSpan` 就够，`Dispose` 时自动带 `ms` |
| 属性凑到 5 个以上 | 说明这条事件混了两件事，**拆成两条**（`TelemetryProps.Capacity` = 4） |

事件名：`snake_case`，描述**已经发生的事实**（`buy_item` / `load_failed`，不是 `do_buy`）。
属性键：优先复用 `TelemetryKeys.Props` 里已有的（`ms` `key` `id` `from` `to` `n` `ok` `reason` …）——
同一个语义全工程一个键名，分析脚本按键名做跨模块关联。失败分支有多种原因时用 `reason` 分开，别把原因写进事件名。

## 3. 拿到 `ITelemetryScope` 的标准写法

模块名 = **模块目录名的小写**（`Sample` → `"sample"`），只允许小写字母、数字、下划线和点。

```csharp
// MonoBehaviour / 状态类这类本来就在 Unity 侧的：构造函数里领一个门面
public SampleState(/* … */, ITelemetryService telemetry)
{
    this.telemetry = telemetry.Scope("sample");   // 同名 scope 由服务缓存，不会每次 new
}
```

**禁止** `builder.Register<ITelemetryScope>(…)` 注册进根作用域：所有 `GameplayInstaller` 注册的都是
**同一个**根作用域，两个模块各注册一个 `ITelemetryScope`，谁拿到谁的全看注册顺序。
模块名这个字面量只出现在模块自己的 Installer / 构造函数里一处。

## 4. 规则类（纯 C#）怎么埋才不破坏约束

规则类的硬约束是「不继承 MonoBehaviour、不碰 `UnityEngine.Time`、不读单例，
**能写 EditMode 测试、将来能整体搬服务端**」（architecture.md 第 7 节）。埋点不能把这条毁掉。

**结论：注入 `ITelemetryScope` 接口，由 Installer 工厂式喂进去。** 五条，缺一条就破。

1. **注入接口，不注入实现、不用静态门面**。`ITelemetryScope` / `PropValue` / `TelemetryProps` / `TelemetrySpan`
   这几个文件只 `using System`，一行 Unity 都没有；Unity 依赖全在实现侧（`UnityDebugTelemetrySink` 写 Console、
   `UnityTelemetryClock` 读 `Time.frameCount`），规则类看不见它们。
   **不要**在规则类里写 `Log.Error` / `Debug.Log`——那才是真把 Unity 带进来了。
2. **构造函数收 `ITelemetryScope`（小接口），不收 `ITelemetryService`**：后者大一圈，测试替身要多写十几个成员。
   由模块 Installer 显式喂：

   ```csharp
   builder.Register(c => new SampleRules(
           c.Resolve<IConfigService>(),
           c.Resolve<ITelemetryService>().Scope("sample")),
       Lifetime.Singleton);
   ```

3. **要量耗时用 `telemetry.BeginSpan("…")`，不要自己读时间**。时钟（`ITelemetryClock`）在服务那侧，
   `BeginSpan` 不引入任何 Unity 依赖；而 `Time.realtimeSinceStartup` 一行就把规则类焊死在 Unity 上。
4. **埋点是旁路，不改判定**：`TrackError` 之后照样 `throw` / `return false`，不吞异常、不改返回值、
   不因为埋点失败而影响玩法。属性值只放 number / string / bool，不塞 `UnityEngine.Object`。
5. **EditMode 测试三行就能造一个真服务**（`Game.Tests.EditMode` 里已有这两个假件，
   `using Game.Tests.EditMode.Telemetry;`）：

   ```csharp
   var telemetry = new TelemetryService(
       TelemetryOptions.Default, new FakeTelemetryClock(), new RecordingTelemetrySink());
   var rules = new SampleRules(config, telemetry.Scope("sample"));
   ```

   `TelemetryScope` 的构造函数是 `internal`，外面 `new` 不出来 —— 要么走 `service.Scope(…)`，
   要么自己实现 `ITelemetryScope`（十个成员，能断言埋了哪些事件时才值得写）。

## 5. 改代码

用 **Edit** 逐处改，一次改一个文件。保存 `.cs` 时 project-lint 钩子自动跑，**零违规**才算这一处完成。
改动只限埋点：不顺手重构、不改判定逻辑、不动签名（除非是为了注入 scope，那属于本次改动，要在清单里写明）。

## 6. 验证

1. `python .claude/skills/instrument-module/scan.py <模块>` 再扫一遍 —— 该埋的应该都变成「已埋」，
   剩下的「未埋」每一条都要能说出为什么不埋。
2. `/unity-test EditMode`（或 `run_tests` 过滤本模块）—— **构造函数加了参数，测试一定会红**，把测试一起改掉。
3. 编辑器开着就 `read_console` 确认零编译错误；没开就让开发者看控制台。
4. 想看埋点真的出来了没有：跑一次 `/verify-module <模块>`，再 `/analyze-telemetry --module <模块小写> --last 1`。

## 7. 列清单，停下等审

固定格式，**不提交**（`CLAUDE.md` 硬规则 4）：

```
/instrument-module Sample 完成，待审。

补了 5 条埋点（模块名 sample）：
| 文件:行 | 事件 | 类别 | 属性 | 为什么 |
| --- | --- | --- | --- | --- |
| SampleRules.cs:91 | buy_item | 意图入口 | id, n, ms | 唯一能回答「玩家买了什么」的事实来源 |
| SampleRules.cs:87 | order_rejected | 失败分支 | n, reason | 数量非法，把收到的 n 写进去才知道是谁传的 |

没埋的候选点与原因：
- SampleInstaller.cs:60 Log.Error —— 配置没拖是编辑期问题，core.log/unity_error 已经会转一条。

顺带改动：SampleRules 构造函数加了 ITelemetryScope 参数，SampleInstaller 改成工厂式注册，
SampleRulesTests 跟着改了 4 处 new。
EditMode 109/109 绿，project-lint 零违规。
```

## 完成标准

清单里每条候选点都有结论（埋了 / 没埋 + 一句话原因）；lint 零违规；EditMode 绿；
改动攒在工作区、清单已列出、**没有提交**。或者明确停在某一道门（模块不存在 / 测试红 / 编译错误）并说清下一步。
