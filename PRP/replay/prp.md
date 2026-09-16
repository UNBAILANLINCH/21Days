# PRP: 日志回放系统（Replay）

> 阶段：PRP 阶段 2 产物（**仅设计**，任务清单按「门三」待点头后另出 `tasks.md`）。
> 需求见 [`prd.md`](prd.md)。范围：期 0 确定性内核 + 期 1 编辑器录制回放最小闭环。

## 上下文快照

### 既有模块文档

本特性**无既有模块文档**——`ai-docs/docs/modules/` 下只有 `sample` 一套，而本次落点在框架层
（`Game.Core`），不新建玩法模块。属首次落地，完成后按 `/generate-doc` 补框架侧说明并更新
`docs/architecture.md`。

### 必须复用的既有契约（摘关键约束）

| 既有物 | 本次怎么用 | 约束来源 |
| --- | --- | --- |
| `IClock` | **不动**。它是渲染帧时间（`Time.time`），继续服务 UI 动效、定时器 | `architecture.md` 5.8 |
| `IInputService` | **不动**其现有接口，在其上叠一层采样 | 同上 |
| `ITickable` + `RegisterEntryPoint` | 新的推进器按 `TimerService` / `PerformanceSampler` 的先例注册 | `GameLifetimeScope.cs:81` |
| `ITelemetryScope` | 录制器自埋（保存成功/失败、漂移检出） | `docs/telemetry.md` 四类尺子 |
| `IPlatformService.SaveRoot` | 回放文件落盘根目录，真机路径不自己拼 | `architecture.md` 5.7 |
| `TelemetryConfig` | 新配置 SO 照它的形状写（`[SerializeField] private` + `ToOptions()` 取快照） | `TelemetryConfig.cs:17` |
| `ShowcaseScenario` | 期 1 验证直接派生它，不自建回放框架 | `ShowcaseScenario.cs:29` |

### 本次必须规避的 pitfalls

| 条目 | 对本次的约束 |
| --- | --- |
| **往 `.cs` 里写含正则 / 反斜杠的代码，不能走 Bash heredoc** | 新增 `.cs` 与改 `rules.json` **一律用 Write / Edit 工具**。本次要往 `rules.json` 加正则，是重灾区 |
| **新建的 `*LifetimeScope.cs` 被 VContainer 清空成模板** | 本次**不新建**任何 `*LifetimeScope.cs`，只改既有 `GameLifetimeScope.cs`（改既有文件不触发） |
| **关掉 Run In Background，MCP 遥控下 Play 必然假死** | 回放器在 Play 模式下驱动；MCP 验证前确认 `runInBackground: 1`，别误判成回放器死锁 |
| **编辑器停在未保存空场景时进 Play 什么都不会发生** | Showcase 与手动验证前先 `manage_scene(get_active)` 确认是 `Boot.unity` |
| **编辑器开着时 batchmode 跑测试必定失败** | 验证走 MCP `run_tests`，不重试 batchmode |
| **`public` 字段被当成对外接口** | 回放数据结构全 `readonly struct` + 属性；配置 `[SerializeField] private` |
| **预先拆好的大文件重构被 doom-loop 钩子拦下** | 执行阶段若连续改同一文件被拦，改用脚本化补丁一次打完，**不拆钩子** |
| **`.meta` 没提交，引用全断** | 新增 `.cs` 后让 Unity 刷新生成 `.meta`，成对提交 |

### 适用规则

`project-root.md`（目录 / asmdef 方向 / 加能力顺序）、`csharp-code.md`（无 public 字段、每帧零分配、
`UnityEngine.Object` 判空只用 `== null`）、`unity-assets.md`（SO 类在模块目录、资产在 `Data/`）、
`unity-tests.md`（EditMode 优先）、`model-routing.md`（派单档位，出 tasks 时用）。

---

## 架构决策

### A1 落点：两个新目录，都在 `Game.Core`，不新增 asmdef

```
Assets/_Project/Scripts/Core/
  Simulation/     确定性内核（期 0）—— 零玩法、零回放概念
  Replay/         录制与回放（期 1）—— 依赖 Simulation
```

**为什么分成两个目录而不是一个 `Replay/`**：确定性内核不是为回放服务的，它是逻辑层的运行基座——
将来做帧同步联机、做 AI 自动测试、做加速跑关卡验证，用的都是它。把它塞进 `Replay/`
会让「想联机」的人以为得先引入回放系统。`Simulation` 不认识 `Replay`，反向依赖。

**为什么归 `Core` 而不是 `Runtime/<Module>/`**：零玩法名词，是框架设施
（`project-root.md`：`Core` 是框架层）。**不新建玩法模块**——PRD D6 已判定，
玩法未定时建的假模块注定是技术债。

**加能力的顺序**（`project-root.md` 不能跳）：
1. *复用不行* —— 工程内没有任何固定步长推进、确定性随机、输入序列化、状态快照的设施。
   `IClock` 是渲染帧时间、`ITimerService` 是延时、`ITickable` 是渲染帧回调，三者都不是逻辑 tick。
2. *扩展不行* —— 往 `Timing/` 里塞逻辑时钟会让「渲染帧时间」和「逻辑 tick」同名同域，
   这正是最容易写错的两个概念，必须在目录层面就分开。
3. *所以新建* —— 每个新文件头按规范写明前两步为何不行。

### A2 逻辑推进：自己的固定步长推进器，**不用 `FixedUpdate`**

```
ILogicClock      Tick(long) / FixedDeltaTime(float, 定长) / SimTime
ISimulationStep  Step(in SimulationContext)      ← 玩法实现它，注册进推进器
SimulationRunner ITickable 驱动；累积渲染帧时间，按固定步长调用 N 次 Step；带最大追帧上限
```

**为什么不用 `FixedUpdate`**（这是本设计最关键的一条）：

- `FixedUpdate` 的推进由 Unity PlayerLoop 控制，**没法手动单步**——而「逐 tick 步进」是 PRD 的 G5。
- 它的步长受 `Time.timeScale` 影响，变速回放（G5）会连带改变逻辑步长，等于改变了被重放的过程。
- 它和物理绑定，而本项目的硬约束正是**物理不参与玩法判定**。

自己推进还带来一个必需能力：回放时把时间源从「渲染帧累积」换成「回放器按需推进」，
`SimulationRunner` 只需暴露一个 `AdvanceOneTick()`，实时与回放走同一条代码路径——
**这是重放能对得上的前提**（两条不同的推进路径必然漂移）。

最大追帧上限：一帧内最多补 N 个 tick（默认 5），超出的丢弃并记一条埋点。防止卡顿后
「补帧 → 更卡 → 补更多帧」的死亡螺旋，代价是极端卡顿时逻辑时间会落后于墙上时间——
对单机游戏这是正确的取舍。

**推进器的两种模式**（拆任务时补）：

- `Live` —— `ITickable.Tick` 里累积渲染帧时间，自动推进 0~N 个 tick。
- `Driven` —— `ITickable.Tick` **不推进任何 tick**，只响应外部显式调用 `AdvanceOneTick()`。

回放时由 `ReplayPlayer` 把推进器切到 `Driven`，自己按播放速度决定这一渲染帧推几个 tick
（暂停 = 0 个、逐 tick 步进 = 1 个、8× = 8 个）。两种模式共用同一个 `AdvanceOneTick()` 实现，
差别只在「谁决定推几次」——这是 A2 开头那条「实时与回放走同一条代码路径」的落地方式。

### A3 随机数：整数 PRNG + **逻辑流 / 表现流分离**

```
IRandomService   Stream(string name) → IRandomStream
IRandomStream    NextUInt() / Range(int,int) / Value01() / State(ulong, 可读可写)
```

- 算法用**纯整数运算**的 PRNG，不碰 `System.Random`（实现随运行时版本变化）、
  不碰 `UnityEngine.Random`（全局状态、无法存取、跨版本不保证）。整数运算在所有目标平台位级一致。
- **实际选用 xorshift64\***（执行期修正，原写「xorshift128+ 一类」）。理由是接口本身逼出来的：
  `State` 是 `ulong`，而 xorshift128+ 的内部状态有 128 位——128 位无损塞进 64 位在信息论上不可能，
  任何打包都会丢一半，**快照就不再无损，回放必然对不上**。改用同族里状态本来就是 64 位的成员，
  状态与 `State` 一比一对应，零打包代码。代价是周期从 2^128-1 降到 2^64-1，
  而一局游戏撑死 1e7 次调用，差十二个数量级，换不来任何风险。
- `Value01()` 由整数位直接构造浮点（取尾数位），**不经 libm**——避免平台间浮点函数差异。
- **流分离是关键设计**：表现层也要随机（粒子朝向、音效变调），但它一旦和逻辑共用一个序列，
  「多播了一个特效」就会让整条逻辑随机序列错位，重放必炸。因此：
  - `Stream("logic.*")` —— 状态进快照、参与哈希、录制时恢复
  - `Stream("view.*")` —— 不进快照、不参与哈希，随便用
  流的种子由主种子 + 流名哈希派生，互不干扰。

### A4 输入命令化：加一层 `IInputSource`，旧接口原样保留

```
InputCommand   readonly struct，定长 31 字节：
               Axis0/Axis1 (Vector2×2) + Buttons(uint 位掩码) + Pointer(Vector2)
               + Flags(byte，含「QA 打点」位) + Reserved(byte×2，期 2 扩展用)
IInputSource   void Sample(long tick)  —— 为该 tick 准备好一条命令
               InputCommand Current    —— 玩法只读这个，不碰设备
  ├ LiveInputSource    Sample 时读设备
  └ ReplayInputSource  Sample 时从回放文件取该 tick 的命令
```

**`Sample(long tick)` 是执行期补的**（T4 抓出来的设计漏洞）：原定稿只有 `Current`，
结果没有任何一方负责触发采样——推进器照着定稿写，实时模式下 `Current` 会永远停在 `Empty`。

*为什么把采样放进接口，而不是让推进器去认 `LiveInputSource`*：推进器一旦认识具体实现，
`InputSourceSwitch` 就失去意义了（它存在的全部目的就是让上游不知道当前是实时还是回放）。
`Sample` 进接口后，推进器只做 `inputSource.Sample(tick); var cmd = inputSource.Current;`，
实时与回放走**完全相同**的两行代码。

*为什么带 `tick` 参数*：回放源可以拿它断言「我取到的这条命令的 tick 号，和推进器正在推的 tick 对得上」——
一道几乎零成本的对齐保险。错位一格是回放类 bug 里最典型也最难看出来的一种。

**为什么是通用槽位而不是玩法专属字段**：玩法未定。定长通用槽位让格式现在就能定死、
体积可预估（约 32 B/tick）、序列化零分支；玩法把自己的动作映射到槽位即可。
玩法定了之后如果槽位不够，加 `Reserved` 或升格式版本——**但届时改的是一个 struct，不是整条链路**。

**旧 `IInputService` 一个字不改**：它服务 UI 导航、调试快捷键这些不需要确定性的场合。
只是在 `architecture.md` 补一句「玩法逻辑读 `IInputSource`，不读 `IInputService.Actions`」，
并由 lint 护栏兜住。

**回放时怎么换源**（拆任务时补）：容器里注册的是 `InputSourceSwitch`——它自己实现 `IInputSource`，
内部持有 live 与 replay 两个实例，按当前模式转发。玩法注入的引用**始终不变**，
回放开始 / 结束时只切换内部指向。

*为什么不重建容器*：重建会连带重置所有单例服务（资源、配置、存档），回放场景下这些恰恰要保持不变；
而且玩法可能已经把 `IInputSource` 缓存进了自己的字段，换实例会让它们指向旧对象——
这类「一半换了一半没换」的错误在回放里表现为难查的漂移。

**当前动作图的映射**（校验阶段实测 `GameInput.inputactions`，Gameplay map 只有四个动作）：

| 槽位 | 映射 |
| --- | --- |
| `Axis0` | `Gameplay/Move`（Vector2） |
| `Buttons` bit0 / bit1 / bit2 | `Confirm` / `Cancel` / `Pause` |
| `Buttons` bit31 | QA 打点（不来自动作图，来自录制热键） |
| `Axis1`、`Pointer` | **当前无对应动作，恒为零**；玩法定了再映射 |

动作引用在初始化时一次性 `FindAction` 缓存，**采样路径里不查找**（每 tick 路径，G3 零分配）。

**一帧内推进多个 tick 时，每个 tick 各采样一次**，会产生多条内容相同的命令——这是**预期行为**，
不是冗余。重放按 tick 逐条回放，录制端若「优化」成一帧只记一条，tick 数当场对不上。
实现时必须照此写，并在注释里说明，否则后人一定会来「优化」它。

### A5 数学：`GameMath` 薄封装，定点数升级的唯一改动点

静态类 `Core/Simulation/GameMath.cs`，转发 `Sqrt / Sin / Cos / Atan2 / Lerp / Normalize / Distance …`。
当前内部就是 `Mathf` / `System.Math`，**本期不改变任何数值行为**。

它的价值不在今天，在于：期 2 拿到真机漂移数据后若判定要上定点数，改动面是这一个文件加数值类型，
而不是满工程 grep `Mathf.`。文件头必须写清这件事，否则后人会觉得它是无意义的转发层而删掉它。

配套 lint：`Runtime/**` 下直接出现 `Mathf.` / `Math.` 报违规，指向 `GameMath`。

### A6 世界状态：模块只管写字节，**哈希由框架算**

```
IStateWriter / IStateReader   定长基础类型读写（int/float/bool/Vector2…），内部是字节缓冲
IReplayState                  Serialize(IStateWriter) / Deserialize(IStateReader)
IReplayStateProvider          模块注册自己的状态对象，框架按注册顺序收集
```

**哈希不让模块自己实现**：框架对序列化后的字节做 FNV-1a。理由——手写哈希是漂移排查里最阴的
一类错误源（漏了一个字段，哈希一直相等，重放看起来「没漂移」但其实早就错了）。
序列化和哈希共用同一份字节，就不可能出现「进了快照但没进哈希」的字段。

**注册顺序即序列化顺序**，且顺序变更要升格式版本——否则旧回放会被按错误顺序解读。

**浮点写入前必须规范化**（校验阶段补）：`-0.0` 与 `+0.0` 数值相等但字节不同，NaN 有多种位模式。
不规范化就会出现「数值完全一样、哈希却不同」的**误报漂移**——而误报比漏报更糟，
它会让人花半天去查一个不存在的问题。写入时统一：`-0.0` 归为 `+0.0`，任何 NaN 归为同一个位模式。

### A7 回放文件：自写二进制，**chunk 化**

```
Header  magic "21DR" | formatVersion u16 | platform u8 | unixUtc i64
        | buildVersion string | seed u64 | configHash u64
        | fixedDeltaTime f32 | startTick u32 | 起始完整快照
Chunks  [ type u8 | tick u32 | length u16 | payload ]
        type 0 = 输入命令   1 = 状态哈希   2 = 完整快照   3 = QA 打点
        （期 2 加 4 = 埋点引用、5 = 相机轨迹，**不需要升格式版本**）
```

chunk 化是为了兑现 PRD 里那条承诺：**期 2 加新记录类型不改格式版本**。读取端遇到不认识的
chunk type 按 `length` 跳过即可——这也让新版编辑器能读旧回放、旧编辑器能读新回放的已知部分。

**已知约束：单条 chunk 的 payload 上限 65535 字节**（`length` 是 u16）。输入（31 B）、
状态哈希（8 B）、打点都远够用，只有**周期性完整快照**可能顶到——世界一复杂就会超。

*现在不改成 u32*：玩法未定，世界状态多大完全未知，现在加宽是为一个还不存在的需求付费
（每条输入 chunk 多 2 字节，5 分钟录制多出约 36 KB）。关键是**超限时 Writer 明确报错、不静默截断**，
所以这条约束一旦真的碍事会立刻暴露，而不是变成一份悄悄少了半个世界的快照。
届时升格式版本把前缀改宽即可——格式版本机制正是为这种情况存在的。

头部里的起始快照用的是 **u32** 前缀（它一份文件只出现一次，可以很大），不受这条限制。

配置表哈希入头部：配置表改了再放旧回放必然对不上，这时要报的是「配置版本不匹配」，
而不是让人对着一堆漂移 tick 号查半天。

### A8 录制：预分配环形缓冲，稳态零 GC

- 定长数组预分配（按 `缓冲秒数 × tickRate` 算），写满回卷。**不用 `List<T>` / `MemoryStream`**——
  它们会在录制途中扩容分配，直接违反 G3。
- 快照缓冲单独一个小环（默认保留最近 30 个快照），与输入环独立，因为两者频率差两个数量级。
- 三种保存触发：`Application.logMessageReceived` 收到 Error / Exception 自动保存、热键、代码 API。
  自动保存**本身要限流**（一次崩溃常连报十几条 Error，不能存十几个文件）。
- **重入防护**（校验阶段补）：`UnityLogTelemetryBridge` 已经挂在同一个 `logMessageReceived` 上。
  保存过程自己也可能打 Error（磁盘满、路径非法、序列化抛异常），那条 Error 会**再次触发保存**，
  于是「保存失败 → 报错 → 保存 → 又失败」自我放大。保存期间置重入标志，标志期内的日志一律不触发保存；
  保存失败只记一次埋点，不重试。

### A9 回放：运行时播放器 + 编辑器窗口，职责分开

- `ReplayPlayer`（`Core/Replay/`，**运行时**）：加载文件、校验头部、恢复种子与起始快照、
  `AdvanceOneTick()` 推进、比对哈希、报首次漂移、漂移后从最近快照续跑。
  放运行时而不是编辑器，是为了期 2 能在真机上自测回放，以及将来做自动化回归（跑一批回放当冒烟测试）。
- `ReplayWindow`（`Scripts/Editor/Tools/`）：只做 UI——加载、播放 / 暂停、逐 tick 步进、变速、
  显示当前 tick 与漂移状态。**不含任何回放逻辑**，这样 Showcase 不开窗口也能验证同一条路径。

回放必须在 Play 模式下进行（要真跑逻辑）。窗口在非 Play 状态下给出明确提示，不静默失效。

**谁驱动 `ReplayPlayer`**（校验阶段补——原设计漏了这一环，且与「Showcase 不开窗口也走同一路径」自相矛盾）：

`ReplayPlayer` 自己实现 `ITickable`，按 `SimulationRunner` 的先例 `RegisterEntryPoint` 注册。
每渲染帧它被调用一次，按当前播放状态决定这一帧调几次 `runner.AdvanceOneTick()`
（暂停 0 次、单步请求 1 次、8× 推 8 次）。

- `ReplayWindow` 只**改播放状态**（播放 / 暂停 / 速度 / 请求单步），不驱动推进。
- Showcase 同样只改播放状态。

两者因此走的是完全同一条推进路径——窗口不是必需品，这正是 A9 把播放器放运行时的意义。
（`Driven` 模式下 `SimulationRunner.Tick` 是空操作，两个 EntryPoint 的先后顺序不影响结果。）

### A9.1 配置表哈希从哪来

校验阶段实测：`IConfigService` / `ConfigService` 里**没有任何哈希概念**，
原设计「头部存 configHash」是个悬空假设，必须补上来源。

`IConfigService` 增加 `ulong ContentHash { get; }`：`ConfigService.InitializeAsync` 加载完表数据后，
对读到的字节流做一次 FNV-1a。开销是启动时一次性的（表数据几百 KB 量级），可接受。

*为什么值得为它改一条框架契约*：配置表改了之后再放旧回放，必然全程漂移。
这时要报的是「配置版本不匹配，这份回放录于另一版配置」，而不是甩一串漂移 tick 号
让人对着代码查一整天——**把一类必然发生、且极易误导的失败挡在门口，值这一行接口**。

### A10 配置与数据

**两个配置资产，不是一个**（拆任务时补）：`Simulation` 不依赖 `Replay`（A1），
所以推进参数不能寄放在 `ReplayConfig` 里——否则只想用确定性内核做联机 / 自动测试的人，
得先配一个回放配置。

| 物 | 位置 |
| --- | --- |
| `SimulationConfig`（SO 类） | `Core/Simulation/SimulationConfig.cs` |
| 资产 | `Assets/_Project/Data/Simulation/SimulationConfig.asset`，`menuName = "21Days/Core/Simulation Config"` |
| 字段 | tickRate（默认 60）、最大追帧 tick 数（默认 5）、主随机种子（0 = 每次运行随机取并记进回放头部） |
| `ReplayConfig`（SO 类） | `Core/Replay/ReplayConfig.cs` |
| 资产 | `Assets/_Project/Data/Replay/ReplayConfig.asset`，`menuName = "21Days/Core/Replay Config"` |
| 字段 | 总开关、缓冲秒数（默认 300）、tickRate、哈希间隔 tick、快照间隔 tick、快照保留个数、自动保存开关与限流、重放静音开关 |
| 默认值分平台 | 正式包默认关、Development 默认开（PRD D2），用 `Debug.isDebugBuild` 兜底，不写平台宏（平台宏只许在 `Core/Platform/`） |
| 回放文件落盘 | `IPlatformService.SaveRoot` 下 `Replays/`，文件名带 UTC 时间戳与触发原因 |

### A11 lint 护栏（`.claude/skills/project-lint/rules.json`）

作用域一律 `Assets/_Project/Scripts/Runtime/**`（玩法层）；`Core/` 与 `Tests/` 不受限——
框架实现本来就要碰这些 API（`LocalClock` 必须读 `Time.time`）。

| 规则 | 拦什么 | 指向 |
| --- | --- | --- |
| `gameplay-system-random` | `UnityEngine.Random.` / `System.Random` / `Random.Range` | 用 `IRandomStream` |
| `gameplay-render-time` | `Time.deltaTime` / `Time.time` / `Time.fixedDeltaTime` | 用 `ILogicClock` |
| `gameplay-raw-math` | `Mathf.` / `Math.` | 用 `GameMath` |
| `gameplay-physics-query` | `Physics2D.` / `Rigidbody2D` / `OnCollision*` / `OnTrigger*` | 物理不参与玩法判定 |
| `gameplay-raw-input` | `IInputService.Actions` / `Input.` | 用 `IInputSource` |

⚠️ `gameplay-physics-query` 会有误报（纯表现用途的物理是允许的）。规则等级定为 **WARN 而非 BLOCK**，
误报时按 `CLAUDE.md` 硬规则 5 在该行写 `// lint-ok: 纯表现，不参与判定` 放行。
**不因为会误报就不加**——这条约束一旦破防，真机重放就没救了，宁可每次让人写一句理由。

### A12 示范载体：Showcase 内的最小世界，不碰 `Runtime/`

`Tests/Showcase/Replay/` 下一个 `DemoWorld`：若干实体随 tick 移动、用逻辑随机流决定转向、
响应 `InputCommand` 的轴与按钮；实现 `ISimulationStep` + `IReplayState`。

它的定位要写清楚：**只验证机制闭环（录得下、放得准、漂移报得出），不验证玩法**。
玩法落地时照它接线即可。按 PRD D6，本期**不修改 `Runtime/` 下任何既有文件**。

**场景策略：代码里搭，不建 `.unity` 文件**（校验阶段补）。`ShowcaseScenario.ScenePath` 返回 `null`
就是这个用法，基类已支持。理由：本期不需要任何美术摆放，而新增场景文件会成为多人 / 多会话
同时改动时的合并冲突源（`unity-assets.md`：场景尽量小而多）。因此本期**零场景、零预制体改动**。

校验阶段实测：`Game.Tests.Showcase.asmdef` 已引用 `Game.Core`，**asmdef 无需改动**。

---

## 关键取舍

| 取舍 | 选择 | 理由 |
| --- | --- | --- |
| 复用埋点通道做回放？ | **否** | 埋点是文本日志、有限流、面向人读；回放是定长二进制高频流。两者性能与格式要求冲突。期 2 的时间轴对齐靠**共享 tick 号**，不靠共用通道 |
| 用 `FixedUpdate` 推进逻辑？ | **否** | 没法单步、受 `timeScale` 影响、与物理绑定。见 A2 |
| 让模块自己算状态哈希？ | **否** | 漏字段导致的「假一致」是最难查的漂移。见 A6 |
| 引入 MemoryPack？ | **否**（PRD D3） | 回放文件要长期可读，格式不能随三方库版本走 |
| 现在就上定点数？ | **否** | 玩法未定，等于盲猜。`GameMath` 已留唯一改动点。见 A5 |
| 新建玩法模块做示范？ | **否**（PRD D6） | 玩法未定时建的假模块注定是技术债。见 A12 |
| 物理规则定 BLOCK 还是 WARN？ | **WARN** | 纯表现用途合法，会误报；但必须留痕，见 A11 |

---

## 契约变更（`architecture.md` 要求先说明理由）

| 改哪 | 怎么改 | 理由 |
| --- | --- | --- |
| 5.8 输入 | 补 `IInputSource`；写明「玩法逻辑读它，不读 `IInputService.Actions`」 | 现契约直接把动作集交给玩法，**这条路录不到任何东西** |
| 5.8 时间 | `IClock` 旁补 `ILogicClock`，写清两者分工（渲染帧时间 vs 逻辑 tick） | 无 tick 概念就没有对齐基准 |
| 5.4 配置 | `IConfigService` 补 `ulong ContentHash` | 见 A9.1：没有它，「配置改过」这类必然发生的失败会伪装成一堆漂移 tick |
| 新增 5.10 | 确定性内核与回放的契约 | 新能力 |
| 第 3 节分层图 | `Core/Simulation/`、`Core/Replay/` 进目录表 | 新目录 |
| 第 7 节「联网只留缝」 | 补一行：确定性内核同时是将来帧同步联机的地基 | 这是它归 `Simulation/` 而非 `Replay/` 的原因，值得留痕 |

---

## 验证清单

- [ ] Unity 控制台零编译错误（MCP `read_console`）
- [ ] `project-lint` 零违规（含新加的 5 条规则自身不误伤 `Core/`）
- [ ] EditMode 测试全绿（`/unity-test EditMode`），覆盖：固定步长与帧率无关、随机序列可重现与状态存取、
      `InputCommand` 序列化往返、chunk 读写往返、未知 chunk 跳过、哈希对不上能报出**首次**漂移 tick、
      格式版本不匹配给可读报错
- [ ] Showcase `/verify-module replay` 跑通并出报告：录一段 → 重放 → 全程哈希一致 → 注入不确定源 → 报漂移 → 从快照续跑不中断
- [ ] PRD 全部 13 条验收标准逐条对应（出 `tasks.md` 时做覆盖对账）
- [ ] asmdef 依赖方向合规：`Core/Replay` → `Core/Simulation` 单向，无 `Core` → `Runtime/Editor/Tests`
- [ ] 无 public 字段；录制稳态每帧零 GC（Profiler 确认）
- [ ] 新增 `.cs` 均有 `.meta`
- [ ] 本期未修改 `Runtime/` 下任何既有文件（`git status` 确认）

---

## 风险 / 回滚

| 风险 | 缓解 |
| --- | --- |
| **玩法未定 ⇒ `InputCommand` 槽位可能不够** | 定长 + `Reserved` 预留 + 格式版本；改动面限于一个 struct |
| **`DemoWorld` 太简单，验不出真实复杂度** | 明确定位为「验机制不验玩法」；第一个真玩法模块落地时补一次真实回放验证 |
| **回放在 Play 模式驱动，MCP 下可能被误判为死锁** | pitfall `#87`：先确认 `runInBackground: 1` |
| **零 GC 目标可能在快照序列化处破功** | 快照本就是低频（默认 10 秒一次），允许它分配；**G3 的零分配只约束每 tick 路径** |
| **lint 新规则误伤既有代码** | 作用域限 `Runtime/**`，当前那里只有 Sample（纯计算，不碰这些 API），预期零误伤；上线前全量跑一次确认 |
| **改 `GameLifetimeScope.cs` 影响启动顺序** | **分两段注册**（原写「统一放在 `InputService` 之前」是错的，波 B 派单时纠正）：无依赖的 `SimulationConfig` / `LogicClock` / `RandomService` 放 `LocalClock` 之后；`LiveInputSource` / `InputSourceSwitch` / `SimulationRunner` 必须放 **`InputService` 之后**——注册顺序即初始化顺序，而 `LiveInputSource` 要拿 `IInputService.Actions`，排在前面会拿到还没初始化的空动作集。既有任何一行注册顺序不动 |
| **另一个会话在同一工作区并发提交**（T1 实测修正） | 校验阶段看到的那批改动（`UIService.cs`、`Game.Editor.asmdef`、`Editor/Tools/` 5 个新文件）**已被另一会话提交**（`5426cd3`、`3a31329`、`1cb8884`），开工时工作区已干净。风险因此从「混进清单」变成「**对方可能整份暂存，把本特性没审过的改动一起提交**」。缓解：T18 以 T1 实测基线对账；`/review-change` 按**内容**判归属，不按文件名判；发现对方新提交时重新取基线 |

**回滚**：改动全在工作区，`git checkout -- Assets/_Project/Scripts/Core/Simulation Assets/_Project/Scripts/Core/Replay …`；
新建目录直接删；`GameLifetimeScope.cs`、`rules.json`、`architecture.md` 三处是**仅追加**的修改，
逐段撤回即可，不影响既有功能。未产生任何场景 / 预制体改动，无需用 MCP 删回对象。

---

## 下一步

按「门三」，本文件是**设计评审稿**，`tasks.md` 待点头后再出。
点头后：`/generate-prp replay` 续出任务清单 → `/validate-prp replay` → `/execute-prp replay`。
