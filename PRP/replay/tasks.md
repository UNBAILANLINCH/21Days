# Tasks: 日志回放系统（Replay）

> PRP 阶段 2 产物之二，阶段 3 校验后修订（v2，2026-09-16）。
> 设计见 [`prp.md`](prp.md)，需求见 [`prd.md`](prd.md)。
> 五波 18 项，每波收口时 lint + EditMode 测试 + `code-reviewer` 审查，改动攒在工作区，
> 全部收敛后一次性 `/review-change` 列清单待授权——**中途不提交**。

## 执行纪律（每一项都适用）

- 写 `.cs` / 改 `rules.json` **一律用 Write / Edit 工具**，不用 Bash heredoc（pitfall：反斜杠被吃一层，
  正则与转义字符静默损坏，编译报一堆看不懂的语法错）。
- 每个新文件头按 `project-root.md`「加能力的顺序」写明**为什么不能复用 / 不能扩展**。
- Inspector 字段一律 `[SerializeField] private` + 只读属性，**无 public 字段**；
  每 tick 路径零分配、零 `Find` / `GetComponent` / `Log`。
- 新增 `.cs` 后让 Unity 刷新生成 `.meta`，成对纳入改动清单。
- 连续改同一文件被 doom-loop 钩子拦下时，改用脚本化补丁一次打完，**不拆钩子**。
- 派单一律显式传 `model`，subagent 永不派成 `fable`。

> **本期零场景、零预制体改动**（Showcase 在代码里搭世界）。唯二的资产操作是 T14 建两个 SO。

---

## 波 A — 确定性内核骨架（期 0）

- [ ] **T1** 目录落位与双基线　`Assets/_Project/Scripts/Core/{Simulation,Replay}/`
      — 建两个目录（随首个文件产生，不预建空目录）；**确认无需新增或修改任何 asmdef**
      （校验已实测：`Game.Tests.Showcase` 与 `Game.Editor` 均已引用 `Game.Core`）；
      跑 `python .claude/skills/evolution/invariants.py` 记依赖基线；
      **另记一份 `git status` 基线**——工作区已有 `UIService.cs`、`Game.Editor.asmdef`、
      `Editor/Tools/` 5 个新文件等**不属于本特性**的改动，T18 对账与最终提交要按内容排除它们。
      产出：两份基线贴进本文件末尾。（model: **sonnet**）

- [ ] **T2** 逻辑时钟与推进器　`Core/Simulation/` 下 `ILogicClock.cs`、`LogicClock.cs`、
      `ISimulationStep.cs`、`SimulationContext.cs`、`SimulationRunner.cs`、`SimulationConfig.cs`
      — 固定步长推进；`ITickable` 驱动（按 `TimerService` 先例 `RegisterEntryPoint`）；
      **双模式** `Live`（累积渲染帧自动推进）/ `Driven`（`Tick` 空操作，只响应 `AdvanceOneTick()`）；
      最大追帧上限，超出丢弃并埋一条 `core.sim/tick_dropped`（带丢弃数）；
      `SimulationConfig` 为 SO（tickRate 60、追帧 5、主种子 0=随机）。
      **不用 `FixedUpdate`**（理由见 prp.md A2）。（model: **opus**）

- [ ] **T3** 确定性随机数　`Core/Simulation/` 下 `IRandomService.cs`、`IRandomStream.cs`、
      `RandomService.cs`、`XorShiftRandomStream.cs`
      — 纯整数 PRNG（**不用** `System.Random` / `UnityEngine.Random`）；`State` 可读可写；
      `Value01()` 由尾数位构造、**不经 libm**；`Stream(name)` 按主种子 + 流名哈希派生；
      `logic.*` 与 `view.*` 两类流的语义写进 XML 注释（前者进快照参与哈希，后者不进）；
      配置里主种子为 0 时取一次性随机种子，用 `Guid.NewGuid()` 的哈希（`TelemetryService` 生成 sid 的先例），
      **不要用 `UnityEngine.Random` 取种子**——那正是本任务要禁的东西。种子一经确定即写入回放头部。（model: **opus**）

- [ ] **T4** 输入命令与来源　`Core/Simulation/` 下 `InputCommand.cs`、`IInputSource.cs`、
      `LiveInputSource.cs`、`InputSourceSwitch.cs`
      — `InputCommand` 定长 `readonly struct`（2 轴 + 按钮位掩码 + 指针 + Flags + 2 字节 Reserved）；
      **映射按实测动作图**：`Axis0`=`Gameplay/Move`，`Buttons` bit0/1/2=`Confirm`/`Cancel`/`Pause`，
      bit31=QA 打点（来自热键不来自动作图），`Axis1` 与 `Pointer` **当前恒为零**；
      动作引用初始化时缓存，**采样路径不 `FindAction`**；
      **一帧推多个 tick 时每 tick 各采样一次**，产生多条相同命令是预期行为，
      注释里写明「不要优化成一帧一条，会让 tick 数对不上」；
      `InputSourceSwitch` 注册进容器、内部切换 live/replay（**不重建容器**，见 prp.md A4）。
      `IInputService` 现有接口**一个字不改**。（model: **opus**）

- [ ] **T5** 数学薄封装　`Core/Simulation/GameMath.cs`
      — 转发 `Sqrt / Sin / Cos / Atan2 / Lerp / Normalize / Distance / Abs / Min / Max / Clamp`；
      本期**不改变任何数值行为**。文件头必须写清「这是定点数升级的唯一改动点」，
      否则后人会当无意义转发层删掉。（model: **sonnet**）

---

## 波 B — 期 0 收口

- [ ] **T6** 容器接线与契约文档　`Core/Boot/GameLifetimeScope.cs`、`docs/architecture.md`
      — 注册 `SimulationConfig` / `LogicClock` / `RandomService` / `SimulationRunner`(EntryPoint) /
      `InputSourceSwitch`，位置在 `LocalClock` 之后、`InputService` 之前，**仅追加，不动既有任何一行注册顺序**；
      `architecture.md` 改六处（5.8 输入补 `IInputSource`、5.8 时间补 `ILogicClock` 并写清与 `IClock` 分工、
      5.4 配置补 `ContentHash`、新增 5.10 确定性内核契约、第 3 节目录表补两个新目录、
      第 7 节补一行「确定性内核也是将来帧同步联机的地基」）。
      改既有 `*LifetimeScope.cs` **不触发** VContainer 模板覆盖，放心改。（model: **opus**）

- [ ] **T7** 期 0 EditMode 测试　`Tests/EditMode/Simulation/` 下 `SimulationRunnerTests.cs`、
      `RandomStreamTests.cs`、`InputCommandTests.cs`
      — 覆盖：10 FPS 与 200 FPS 下推进同样 tick 数结果一致；`Driven` 模式不自动推进；追帧上限生效；
      同种子序列可重现；`State` 存取恢复后续序列与未中断时相同；`logic`/`view` 流互不干扰；
      `InputCommand` 序列化往返逐字段相等。（model: **opus**）

- [ ] **T8** lint 护栏　`.claude/skills/project-lint/rules.json`
      — 加 5 条规则，作用域一律 `Assets/_Project/Scripts/Runtime/**`：
      `gameplay-system-random`、`gameplay-render-time`、`gameplay-raw-math`、`gameplay-raw-input`（以上 BLOCK）、
      `gameplay-physics-query`（**WARN**，纯表现用途合法会误报，误报写 `// lint-ok: 纯表现，不参与判定`）。
      **必须用 Write / Edit 工具改**（含正则）。改完全量跑一次确认不误伤 `Core/` 与 `Sample`。（model: **sonnet**）

---

## 波 C — 回放数据层（期 1）

- [ ] **T9** 配置内容哈希　`Core/Config/IConfigService.cs`、`Core/Config/ConfigService.cs`
      — 加 `ulong ContentHash { get; }`，`InitializeAsync` 加载完表数据后对字节流做一次 FNV-1a。
      **这是本期唯一修改的既有框架服务**，理由见 prp.md A9.1（没有它，「配置改过」会伪装成一堆漂移 tick）。
      补一条 EditMode 断言：同数据两次加载哈希相同、数据变则哈希变。（model: **opus**）

- [ ] **T10** 状态序列化与哈希　`Core/Replay/` 下 `IStateWriter.cs`、`IStateReader.cs`、`StateBuffer.cs`、
      `IReplayState.cs`、`IReplayStateProvider.cs`、`StateHasher.cs`
      — 定长基础类型读写，内部字节缓冲预分配；**哈希由框架对序列化字节算 FNV-1a**，模块不实现哈希；
      **浮点写入前规范化**（`-0.0`→`+0.0`、NaN 统一位模式），否则数值相等却哈希不同 = 误报漂移；
      注册顺序即序列化顺序，顺序变更要升格式版本（写进注释）。（model: **opus**）

- [ ] **T11** 回放文件格式　`Core/Replay/` 下 `ReplayFormat.cs`、`ReplayHeader.cs`、`ReplayChunk.cs`、
      `ReplayWriter.cs`、`ReplayReader.cs`
      — magic `21DR` + formatVersion + platform + unixUtc + buildVersion + seed + configHash（来自 T9）
      + fixedDeltaTime + startTick + 起始完整快照；chunk 为 `[type|tick|length|payload]`，
      **未知 type 按 length 跳过**（期 2 加类型不升版本）；版本不匹配 / 文件损坏给**可读报错**
      （说清坏在哪），不崩溃、不静默播错。（model: **opus**）

- [ ] **T12** 录制器与配置　`Core/Replay/` 下 `ReplayRecorder.cs`、`ReplayConfig.cs`
      — 预分配定长环形缓冲（按 `缓冲秒数 × tickRate`），**禁用 `List<T>` / `MemoryStream`**（扩容分配违反 G3）；
      快照单独小环（默认留 30 个）；三种保存触发（`logMessageReceived` 收 Error/Exception 自动存、热键、API）；
      **重入防护**：保存期间置标志，标志期内的日志一律不触发保存，保存失败只埋一次点、不重试
      （否则「保存失败→报错→再保存」自我放大，且 `UnityLogTelemetryBridge` 挂在同一个回调上）；
      落盘到 `IPlatformService.SaveRoot` 下 `Replays/`，文件名带 UTC 时间戳与触发原因；
      正式包默认关、Development 默认开（用 `Debug.isDebugBuild`，**不写平台宏**）。（model: **opus**）

---

## 波 D — 回放播放层（期 1）

- [ ] **T13** 播放器与回放输入源　`Core/Replay/` 下 `ReplayPlayer.cs`、`ReplayInputSource.cs`
      — **`ReplayPlayer` 自己实现 `ITickable` 并 `RegisterEntryPoint` 注册**（与 `SimulationRunner` 同例），
      每渲染帧按播放状态决定调几次 `runner.AdvanceOneTick()`（暂停 0 / 单步 1 / 8× 推 8）——
      窗口与 Showcase 都只改播放状态、都不驱动推进，两者因此走同一条路径；
      加载校验头部 → 恢复种子与起始快照 → 切 `Driven` 与 replay 输入源 → 比对哈希；
      漂移时报**首次**漂移 tick 号（不是最后一个）并从最近快照续跑、**不中断**；
      `configHash` 不匹配时报「配置版本不匹配」而不是一堆漂移 tick。
      放**运行时**而非 Editor（期 2 真机自测与自动化回归要用）。（model: **opus**）

- [ ] **T14** 配置资产　`Assets/_Project/Data/Simulation/SimulationConfig.asset`、
      `Assets/_Project/Data/Replay/ReplayConfig.asset`
      — 优先走 Unity MCP 创建；**MCP 没连或编辑器没开时，改为把创建步骤列给用户在编辑器里做**，
      不直接写 `.asset` 文本。`menuName` 前缀 `21Days/Core/`；填默认值；确认 `.meta` 生成。（model: **sonnet**）

- [ ] **T15** 编辑器回放窗口　`Assets/_Project/Scripts/Editor/Tools/ReplayWindow.cs`
      — 加载文件、播放/暂停、逐 tick 步进、变速（0.25×~8×）、显示当前 tick 与漂移状态；
      **不含任何回放逻辑**（只改 `ReplayPlayer` 的播放状态）；
      非 Play 状态下给明确提示，不静默失效。（model: **opus**）

---

## 波 E — 验证与收口

- [ ] **T16** Showcase 示范世界　`Tests/Showcase/Replay/` 下 `DemoWorld.cs`、`ReplayShowcase.cs`
      — `DemoWorld` 实现 `ISimulationStep` + `IReplayState`：若干实体随 tick 移动、用 `logic` 流决定转向、
      响应 `InputCommand`；`ReplayShowcase` 派生 `ShowcaseScenario`，**`ScenePath` 返回 `null`（代码里搭，不建场景文件）**；
      流程为录一段 → 重放 → 断言全程哈希一致 → 注入不确定源 → 断言报出首次漂移 tick → 断言从快照续跑不中断。
      定位写死为**验机制不验玩法**。进 Play 前先 `manage_scene(get_active)` 确认是 `Boot.unity`
      （pitfall：空场景进 Play 什么都不发生）；确认 `runInBackground: 1`（pitfall：否则 MCP 下必假死，
      会被误判成回放器死锁）。（model: **opus**）

- [ ] **T17** 期 1 EditMode 测试　`Tests/EditMode/Replay/` 下 `ReplayFormatTests.cs`、`StateHasherTests.cs`、
      `DriftDetectionTests.cs`
      — 覆盖：chunk 读写往返、未知 chunk 跳过、头部版本不匹配给可读报错、损坏文件不崩溃、
      `-0.0`/NaN 规范化后哈希稳定、漏字段场景下哈希确实变化、哈希对不上能定位**首次**漂移 tick。（model: **opus**）

- [ ] **T18** 验收对账与实测回填　（验证命令派 **sonnet** 执行，主窗口独立复核）
      — MCP `read_console` 确认零编译错误；`/unity-test EditMode` 全绿；`/verify-module replay` 出报告；
      `gc_scan.py` 与 `invariants.py` 对比 T1 依赖基线；
      `git status` 对比 T1 工作区基线，确认**未修改 `Runtime/` 下任何既有文件**、
      且本期改动与他人在工作区的既有改动**可按内容分开**；
      Profiler 实测稳态每帧分配、实测 5 分钟录制体积，把 G3 的 0.2 ms 预算与体积数字**回填 `prd.md`**；
      PRD 13 条验收标准逐条打勾。
      **subagent 自报通过不算通过**——主窗口自己复跑 `run_tests` / `read_console` 再采信。（model: **sonnet** + 主窗口复核）

---

## 验收覆盖对账

`prd.md` 每条验收标准都必须有任务负责。逐条对上：

| # | 验收标准 | 负责任务 |
| --- | --- | --- |
| 1 | 固定步长与渲染帧率无关 | T2 → T7 |
| 2 | 同种子序列可重现、状态存取可恢复 | T3 → T7 |
| 3 | `InputCommand` 序列化往返相等 | T4 → T7 |
| 4 | 一段录制重放全程哈希逐点一致 | T10 / T12 / T13 → T16 |
| 5 | 报出**首次**漂移 tick 号 | T13 → T17 |
| 6 | 漂移后从最近快照续跑不中断 | T13 → T16 |
| 7 | 稳态录制每帧零 GC | T12 → T18 |
| 8 | 异常触发落盘且可被窗口加载 | T12 / T11 / T15 |
| 9 | 头部含格式版本、不匹配给可读报错 | T11 → T17 |
| 10 | 窗口可暂停 / 步进 / 变速、tick 可见 | T15（推进能力由 T13 提供） |
| 11 | lint 拦系统随机 / 渲染帧时间 / 物理判定 | T8 |
| 12 | `/verify-module replay` 跑通出报告 | T16 |
| 13 | 5 分钟录制体积 ≤ 2 MB | T12 → T18 |

**无遗漏**。T9（配置哈希）不直接对应某条验收标准，它是 #4 与 #8 的必要前置——
没有它，「配置改过」这类失败会伪装成漂移，让 #4 的判定失去意义。

---

## 波次收口标准

每波结束都要满足，不满足不进下一波：

- [ ] Unity 控制台零编译错误（MCP `read_console`，**编辑器没开就让用户看控制台**）
- [ ] `project-lint` 零违规
- [ ] 该波涉及的 EditMode 测试全绿（编辑器开着走 MCP `run_tests`，**不重试 batchmode**）
- [ ] `code-reviewer` 子代理审查通过（`/unity-code-review`）
- [ ] 改动留在工作区，**不提交**

全部五波收敛后统一走 `/review-change` 列清单，用户逐次明确授权才提交。

---

## 执行进度

| 波 | 任务 | 状态 | 实测验收 |
| --- | --- | --- | --- |
| A | T1~T5 | ✅ 完成 | 15 文件 / `.meta` 齐全 / 编译零错误零警告（主窗口复核） |
| A | 追加修正 | ✅ 完成 | `IInputSource.Sample(long tick)` —— 执行期发现原设计无人触发采样 |
| B | T6~T8 | ✅ 完成 | EditMode **141/141 passed**（主窗口复跑确认） |
| B | 追加修正 | ✅ 完成 | `accumulator`→`double`；启动期误导 Warn 消除；`core.sim` 补进两处文档 |
| B | 主窗口补丁 | ✅ 完成 | `gameplay-raw-math` 正则漏了 `Math.`（`using System` 后的常见写法），已补并探针验证 |
| C | T9~T12 | ✅ 完成 | EditMode **150/150**（主窗口复核）；四件各有 `execute_code` 实测 |
| D | T13~T15 | ✅ 完成 | **端到端闭环打通**：录 400 tick → 重放 → 世界终态逐位相同、零漂移 |
| D | 追加修正 | ✅ 完成 | 补 `SeekClockTo` 正规入口 + `RandomService.Reseed`（负对照证明其必要性） |
| E | T16 / T17 | 🔄 进行中 | — |
| E | T18 | ⏸ | — |

**波 D 的端到端实测**（`execute_code`，最像真事故的形态：环形缓冲跑满绕圈 → 起点是中途快照 tick 200、
重放侧全新一局且主种子不同、有一条 `logic.late` 流到 tick 250 才首次取用）：

```
[载入] StartTick=200 模式=Driven 载入期间步骤被调=0   ← 定位时钟没跑任何逻辑
[重放] 漂移=False 校验哈希点=10 缺输入=0 跳输入=0
[世界] 录制端 Accum=933262886428708174 / 重放端 Accum=933262886428708174 → 逐位相同
[负对照] 种子不对齐时：漂移=True FirstDriftTick=260 （late 流 250 首次取用，260 是其后第一个校验点）
```

负对照同时证明了两件事：`Reseed` 是闭环的必要条件；正向那组的「零漂移」不是因为根本没在校验。

波 C 各件的实测要点：配置指纹按表名 Ordinal 排序（用变异测试证过断言非空）；
状态哈希对 ±0.0 与整族 NaN 规范化、改 1 ulp 仍变、只换字段顺序也变、稳态零分配；
文件格式未知 chunk 跳过 + 4 种截断位置恢复 + 14 例头部损坏可读报错 + 读写各 2 万条零分配；
录制器三道重入闸、环形回卷存最近一批、快照超限接住后输入流照录、稳态 2 万 tick 零分配。

**期 0（确定性内核）已完整落地**：固定步长推进（Live/Driven 双模式）、确定性随机（logic/view 流分离）、
输入命令化（31 字节定长）、`GameMath` 升级口、5 条 lint 护栏、26 条 EditMode 测试。

### 执行期对设计的修正（已回填 prp.md）

1. `IInputSource` 补 `Sample(long tick)` —— 原设计只有 `Current`，没人负责触发采样。
2. PRNG 从 xorshift128+ 改 **xorshift64\*** —— 128 位状态无损塞不进 `ulong State`，快照就不再无损。
3. 容器**分两段注册** —— `LiveInputSource` 要拿 `IInputService.Actions`，必须排在 `InputService` 之后。
4. `ILogicClock` 绑推进器内部时钟 —— 另注册一份会是「第二个没人推的时钟」，`Tick` 永远停在 0 且不报错。
5. `accumulator` 用 `double` —— `float` 下总时长落在 tick 边界时，不同帧率会分到两侧。
6. 单条 chunk payload 上限 **65535**（`length` 是 u16）——完整快照可能顶到。现在不加宽（玩法未定，
   等于为未知需求付费），靠「超限时明确报错、不静默截断」让它一旦碍事就立刻暴露，届时升格式版本。
7. 补 `SimulationRunner.SeekClockTo(long)` —— 中途起点的回放（崩溃现场全是这种）要把时钟挪到 `StartTick`，
   原先只能把 `ILogicClock` 向下转型成实现类绕道。**不进 `ILogicClock`**：那是玩法也会注入的只读接口，
   放进去等于给玩法开改时钟的后门；推进权本来就只在推进器手里。
8. 补 `RandomService.Reseed(ulong)` —— 回放途中**新建**的随机流会按当前主种子派生，与录制时不是一套，
   静默分叉。`Reseed` 必须**原地重设已有流实例的状态、不换对象**（玩法可能缓存了 `IRandomStream` 引用，
   换实例就是「一半换了一半没换」）。另外 `ApplyHeaderSeed` 必须排在恢复快照**之前**，否则会把刚恢复好的流状态冲掉。

9. **补 `IReplayStateProvider` 的默认实现并接线**（波 E 在真实容器里实测发现）。原先框架只定义了契约，
   没有实现类，`GameLifetimeScope` 也没注册没接上——**真实启动路径下录制器与播放器手上没有状态提供者**，
   录出来的回放只有输入流，状态哈希与完整快照全缺，漂移检测 / 起点恢复 / 快照续跑**全部空转**。

   这个缺口被「测试自己提供依赖」完美掩盖了：Showcase 自带 `DemoStateProvider` 所以全绿。
   **教训**：验证一个系统时，如果测试替自己把依赖装上了，那验的是「这套类凑一起能工作」，
   不是「产品启动起来能工作」。凡是靠容器接线的能力，必须有一条验证是**从真实容器里解析出来查**的。

### 一条贯穿全程的验证纪律

多个任务自发做了**防空验证**，值得固化成规矩：一套全绿但什么都抓不住的测试，比没有测试更危险。
- 变异验证：临时把被测代码改坏（删掉 `names.Sort`），确认对应断言真的转红，再还原。
- 负对照：让种子不对齐，确认漂移点精确落在「晚出场的流首次取用之后的第一个校验点」。
- 反空断言：断言「零漂移」时必须同时断言**校验点数 > 0**；断言「序列相同」时必须同时断言**互异值数量**。

### 执行期踩到的坑（收尾要 `/learn` 进 pitfalls.md）

**EditMode 测试会随机挂在一条无关用例上**，报 `Unhandled log message: '[Exception] InvalidOperationException: ThrowingView 打不开'`。
真因不是控制台残留——是 `UIServiceTests` 里 `OnOpenAsync` 抛异常那条用例留下一个**未被观察的 UniTask 异常**，
由 `UniTaskScheduler` 延迟发布，落进哪个测试边界取决于 GC 时机。**清控制台挡不住**；
跑 `execute_code` 之后尤其容易触发（动态程序集改变了 GC 时机）。
正确做法：跑测试前先 `refresh_unity(force, compile="request")` 触发一次域重载。

---

## 基线记录（T1 填）

> 执行 T1 时把 `invariants.py` 输出与 `git status` 输出贴在这里，T18 用它对账。

**注意（2026-09-16 T1 实测）**：任务描述里说工作区有 `UIService.cs`、`Game.Editor.asmdef`、
`Editor/Tools/` 5 个新文件的未提交改动——实测这批改动**已不在工作区**，`git log` 显示已被
提交 `3a31329 feat(editor): 工具栏加主场景/编译/刷新按钮与播放速度条`（及 `5426cd3 fix(ui): ...`）
收纳（另一个并发会话所为，见 memory `concurrent-sessions.md`）。当前工作区实际改动见下方
git status 基线，T18 对账按**这份实测输出**为准，不按任务描述里的旧清单。

```
$ python .claude/skills/evolution/invariants.py
（退出码 1，发现 1 处跨文件约束违规）

[invariants] 发现 1 处跨文件约束违规：

  1) Assets/_Project/Art/Fonts/Font_NotoSansSC_Regular SDF.asset
      现象：Dynamic 字体资产 2054 KB，里面攒着 Play 期栅格化出来的字形数据，提交进去每人每次运行都会产生巨大 diff 并在分支间冲突
      改法：在 Inspector 里选中该资产 → 右键菜单 / 齿轮里点 Clear Dynamic Data，回到几 KB 的基线再提交（字形运行时会自动重建，清掉不影响显示）
      依据：Art/Fonts/README.md · pitfalls.md #Dynamic 字体资产污染 git

这些是 project-lint 的逐行正则够不着的跨文件约束；逐条按「改法」修，改完重跑。
```

```
$ git status --porcelain
 M "Assets/_Project/Art/Fonts/Font_NotoSansSC_Regular SDF.asset"
 M ProjectSettings/ProjectSettings.asset
?? PRP/replay/
```

**asmdef 核对结论**：逐行读取确认，本次功能**不需要新增或修改任何 `.asmdef`**。
- `Assets/_Project/Scripts/Tests/Showcase/Game.Tests.Showcase.asmdef` 的 `references` 已含
  `"Game.Core"`、`"Game.Runtime"`。
- `Assets/_Project/Scripts/Editor/Game.Editor.asmdef` 的 `references` 已含
  `"Game.Core"`、`"Game.Runtime"`。
两份文件均**未做任何修改**。
