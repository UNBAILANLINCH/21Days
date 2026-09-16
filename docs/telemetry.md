# 埋点与根因分析

埋点解决的是「**出问题时，能从日志里倒推出当时发生了什么**」。
本工程的埋点**不另建文件通道**：埋点就是一条格式固定的 Unity 日志，由 Unity 自己落盘
（编辑器 → `Editor.log`，Windows 包 → `Player.log`，Android → logcat）。
分析侧（`/analyze-telemetry`）去解析这些文件，工程内不维护第二份日志。

这么定的三个理由：写入点只有一个，不会出现「文件里有、Console 里没有」；
崩溃时 Unity 已经 flush 过，最后几条不丢；真机路径由 Unity 管，不用自己处理平台差异。

---

## 怎么用

- **写模块时什么都不用管**：框架层 `boot` / `flow` / `asset` / `ui` / `save` / `audio` 六处已经埋好，模块接进框架就自动有；模块自己的业务点由 `/new-feature` 第 6 步调 `/instrument-module` 补。
- **出问题时跑 `/analyze-telemetry`**：它自己找日志（优先工程内镜像目录）、机器聚合出摘要、提假设、定向回读验证，最后出诊断报告。只诊断不改代码。
- **嫌日志吵就调 `Assets/_Project/Data/Telemetry/TelemetryConfig.asset`**：总开关、最低级别、模块过滤、采样间隔、尖峰阈值都在那儿。

---

## 1. 日志行格式（唯一契约，两侧都照它写）

```
[Game][T] <级别> <模块>/<事件> | <JSON>
```

一条真实的样子：

```
[Game][T] I core.flow/state_enter | {"t":1234,"s":42,"f":1024,"p":{"from":"BootState","to":"TitleState","ms":312}}
[Game][T] E core.asset/load_failed | {"t":5678,"s":99,"f":2048,"p":{"key":"SampleScene_Game"},"err":"InvalidKeyException: ...","st":"at Game.Core.Assets..."}
```

前半段给人看（Console 里一眼看出是哪个模块的什么事），后半段给脚本吃。
解析正则（分析脚本用这一条，别再造第二条）：

```
^\[Game\]\[T\] ([DIWE]) ([a-z0-9_.]+)/([a-z0-9_]+) \| (\{.*\})$
```

### 字段

| 位置 | 字段 | 类型 | 含义 |
| --- | --- | --- | --- |
| 级别 | `D` / `I` / `W` / `E` | — | Debug / Info / Warn / Error。`D` 与 `Log.Debug` 一样，正式包里整句剔除 |
| 模块 | — | string | 框架层用 `core.<服务>`（`core.boot` `core.flow` `core.ui` `core.asset` `core.save` `core.audio` `core.sim` `core.perf`）；玩法模块用模块名小写（`sample` `player` `inventory`） |
| 事件 | — | string | `snake_case`，描述**已经发生的事实**（`state_enter`、`load_failed`），不用祈使式 |
| JSON `t` | int | 必有 | 自本次会话开始的毫秒数 |
| JSON `s` | int | 必有 | 会话内自增序号。日志被别的线程插行、或被工具重排时靠它恢复真实顺序 |
| JSON `f` | int | 必有 | `Time.frameCount`。定位「同一帧内连续发生」用 |
| JSON `p` | object | 可选 | 事件属性。**扁平一层**，值只能是 number / string / bool。不放数组、不放嵌套对象——脚本要做列式聚合 |
| JSON `err` | string | 仅 `E` | 异常消息 |
| JSON `st` | string | 仅 `E` | 堆栈，换行替换成 ` ⏎ ` 压成一行（日志行不能跨行，跨行就没法按行解析） |

`p` 里约定俗成的键：`ms`（耗时毫秒）、`key`（资源/配置地址）、`id`（业务 id）、`to` / `from`（转移两端）、
`n`（数量）、`ok`（bool 结果）、`name`（被处理对象的名字）、`reason`（失败/被拒的原因，蛇形短词如 `not_initialized`）、
`panel` / `depth`（UI）、`slot` / `bytes`（存档）。

**同一个事件名下的多条失败分支，用级别（`W` / `E`）+ `reason` 区分，不要为每条分支发明新事件名**——
事件名是聚合的维度，发明得越多，分析时越聚不起来。
新键随便加，但同一个语义在全工程用同一个键名——脚本按键名做跨模块关联。

### 会话头（每次启动的第一条，序号固定 0）

```
[Game][T] I core/session_start | {"t":0,"s":0,"f":0,"p":{"sid":"7f3a9c21","at":"2026-09-16T01:42:08+08:00","prod":"21Days","ver":"0.1.0","plat":"WindowsPlayer","unity":"2022.3.62f2","dev":"...","scr":"1920x1080","mem":16384}}
```

`sid` 是本次运行的随机 id（8 位十六进制）。**一个日志文件里可能有多段会话**
（编辑器 `Editor.log` 会连着记很多次 Play），分析脚本必须按 `session_start` 切段，
再按 `sid` 区分，绝不能把两次运行的事件混在一起找因果。

### 日志到底在哪：指针文件（不能靠猜路径）

`Editor.log` 的路径（`%LOCALAPPDATA%\Unity\Editor\Editor.log`）是**本机全局的**，不带工程名。
本机同时开着两个 Unity（本工程 + 参考工程）时，后启动的那个会把先前那份挤成 `Editor-prev.log`，
分析脚本按固定路径去读，读到的很可能是**另一个工程**的日志——实测已经发生过一次。

所以 `TelemetryService` 初始化时把 `Application.consoleLogPath`（Unity 给出的**当前实例真正在写的**日志
完整路径，编辑器下是 Editor.log、播放器下是 Player.log）写进工程内的指针文件：

```
Logs/telemetry-source.txt          # Logs/ 已 gitignore
<Application.consoleLogPath 绝对路径>
<productName>
<sid>
<写入时刻 ISO8601>
```

指针文件本身没有任何埋点数据，只有一行「本工程的日志在哪」。
猜到的路径要额外校验：文件里第一条 `session_start` 的 `p.prod` 与本工程 `productName` 对不上就直接报错，
不要拿着别人的日志做分析。为此 `session_start` 的属性里必须有 `prod`（productName）。

### 编辑器镜像：`Logs/telemetry/<sid>.log`（只在编辑器写）

指针文件解决了「日志在哪」，但解决不了**编辑器下 `Editor.log` 根本不是本工程独占**这件事。实测（2026-09-16，三次）：

- 参考工程的 Unity 同时开着，它的每帧日志把本工程刚写的埋点推出尾部——写完 30 秒，尾部 256 MB 里一条不剩；
- 全量 `grep` 还在的 62 行埋点，20 分钟后整份 `Editor.log` 被换掉，全量扫变成 0 条；
- 文件末尾全是另一个工程的堆栈。

所以**编辑器下**额外镜像一份到工程内，一段会话一个文件：

```
Logs/telemetry/<sid>.log           # Logs/ 已 gitignore
```

四条约束：

1. **只在编辑器写**（`#if UNITY_EDITOR`）。真机那条路一个字不变——`Player.log` / logcat 是进程独占的，不存在这个问题，也不该往玩家设备上多写文件。
2. **内容与 Unity log 里那一行逐字节相同**。同一次格式化的结果分别送给两个 sink，不是两套格式——否则契约就有两个版本了。Console 照常输出，镜像只是多存一份。
3. **编辑器下指针文件指向镜像**，因为镜像才是本工程独占、干净、可靠的那一份。
4. **保留最近 20 个会话文件**，启动时清理更老的。没有清理的日志目录迟早变成几 GB 的坟场。

镜像目录的额外好处：一个文件一段会话，天然分好段，不用从几百 MB 里切；多次运行留成多个文件，
「成功会话 vs 失败会话序列 diff」这类跨会话分析才真的用得上。

**镜像与 Unity log 怎么选**：镜像是带缓冲写的（攒够 32 条 / 距上次超过 1 秒 / `E` 级立刻刷 / 退出时刷），
所以崩溃时镜像可能缺最后几条；Unity log 由 Unity 自己 flush，通常更完整。
一句话记法：**镜像干净但可能缺尾，Unity log 吵但完整——查崩溃现场两份对照着看。**

### 分析脚本的定位顺序

**镜像目录 → 指针文件 → 用户显式给的路径 → 猜默认路径**，并在摘要头部注明用了哪条。
镜像目录存在时优先吃整个目录（多段会话一起分析），而不是只挑一个文件。

---

## 2. 谁来埋

### 2.1 框架层：零侵入，玩法不写一行

下面这些由 `Core` 自己埋，任何模块接进框架就自动有：

| 模块 | 事件 | 关键属性 |
| --- | --- | --- |
| `core` | `session_start` / `session_end` | 见上 |
| `core.boot` | `step` / `ready` / `failed` | `name` `ms` |
| `core.flow` | `state_enter` / `state_exit` / `state_failed` | `from` `to` `ms` |

> `core.flow` 三条事件里 **`from` 恒指转移的来源状态、`to` 恒指目标状态**，不随事件名换意思：
> `state_exit` 是「从 `from` 离开、要去 `to`」，`state_enter` 是「从 `from` 来、进了 `to`」。
> 首次进入没有来源、最后一次退出没有目标，那一侧留空字符串。`ms` 是本条对应的 `ExitAsync` / `EnterAsync` 耗时。

| `core.asset` | `load` / `load_failed` / `scene_load` / `release` | `key` `ms` |
| `core.ui` | `open` / `close` | `panel` `ms` `depth` |
| `core.save` | `write` / `load` / `corrupt` / `migrate` | `slot` `ms` `bytes` |
| `core.audio` | `bgm` / `sfx_denied` | `key` |
| `core.sim` | `tick_dropped` | `n` |
| `core.perf` | `sample` / `spike` | `fps` `frame_ms` `gc_mb` `mem_mb` |
| `core.log` | `unity_error` | 由 `Application.logMessageReceived` 自动转，`err` `st` |

`core.sim` 埋的是确定性内核（固定步长推进器）的推进异常，目前只有 `tick_dropped` 一条：
`n` 是单帧追帧超上限时丢弃掉的 tick 数，逻辑时间就此落后于墙上时间。
查「卡了一下之后画面像跳过去一段」这类手感问题时先看它。

`core.log/unity_error` 是根因分析的主线索：**任何** `Debug.LogError` 和未捕获异常都会变成一条带序号的埋点，
于是「报错前 30 条发生了什么」这个问题永远答得出来。

**组合根注册契约**：`TelemetryService` 的构造参数是 `params ITelemetrySink[] sinks`，组合根要注册的是
**`ITelemetrySink[]`（数组）**，不是单个 `ITelemetrySink`——VContainer 按类型解析，注册单个接口会在解析时失败（实测踩过）。
工程里已按此写的两处：`GameLifetimeScope`、`GameFlowTests`。

### 2.2 玩法模块：一行一个点

模块从 DI 拿一个已经绑好模块名的 `ITelemetryScope`，埋点就是一行：

```csharp
telemetry.Track("buy_item", ("id", intent.ItemId), ("n", intent.Count), ("ms", elapsed));
```

**该埋哪些点**（`/instrument-module` 按这四类自动补，人工写也照这个尺子）：

1. **意图入口** —— 每个 `XxxIntent` 被处理的地方。玩家做了什么，这是唯一的事实来源。
2. **状态迁移** —— 状态类里改变对外可见状态的公开方法。
3. **失败分支** —— 规则类里 `return false` / 抛异常之前，`catch` 块里（用 `TrackError`）。
   **失败比成功值钱**：成功路径只埋入口，失败路径每个分支都埋，并且把判定用到的数值写进 `p`。
4. **长耗时操作** —— 可能超过一帧的异步流程，用 `BeginSpan` 自动记 `ms`。

**不该埋**：每帧都会触发的东西（移动、动画帧、Update 里的判定）。
高频事件会把日志淹掉，让真正的线索沉底——需要每帧数据时用 `core.perf` 的采样，别自己埋一个每帧事件。

---

## 3. 开关与开销

埋点行为由 `Assets/_Project/Data/Telemetry/TelemetryConfig.asset` 控制：

| 项 | 默认 | 说明 |
| --- | --- | --- |
| 总开关 | 开 | 关掉后所有 `Track` 变成空调用 |
| 最低级别 | `I` | 设 `W` 只留警告与错误 |
| 模块过滤 | 空 | 填了就只埋这几个模块，查特定模块时用 |
| 性能采样间隔 | 5 秒 | 设 0 关闭周期采样 |
| 帧尖峰阈值 | 100 ms | 单帧超过就立刻打一条 `core.perf/spike` |
| 每秒最多条数 | 200 | 超出的丢弃并在下一秒补一条 `core/throttled`，防止某个循环把日志刷爆 |
| 关闭 Log 堆栈 | 开 | 启动时 `Application.SetStackTraceLogType(LogType.Log, None)`。**影响全工程的 `Debug.Log`**：普通日志不再带堆栈（Warning / Error 不受影响）。这是埋点开销的大头，关掉后一条埋点约等于一次字符串拼接 |

**编辑器下尖峰阈值偏敏感**：实测编辑器 Play 的首帧与场景加载动辄超 100 ms，一次 13 秒的会话里尖峰打了 10 条、周期采样才 11 条。
真机上 100 ms 是合理阈值，编辑器嫌吵就把阈值调高，或把采样间隔设 0 关掉周期采样。

**新加的配置字段在资产被重新序列化之前不会出现在 `.asset` 的 YAML 里**（比如 `editorMirrorKeepSessions`），
运行期取到的是字段的初始值，行为是对的；下次在 Inspector 里保存这份资产时会自动写进去。
看到 `.asset` 里没有某个字段不用慌，也**不要手改资产 YAML**。

正式包里 `D` 级埋点整句被编译器剔除（同 `Log.Debug`），`I` 及以上保留。

---

## 4. 分析：`/analyze-telemetry`

```
/analyze-telemetry [--source editor|player|<路径>] [--module <模块>] [--last <N>]
```

脚本 `.claude/skills/telemetry/analyze.py` **先做机器聚合，再交给模型判断**——
原始日志动辄几万行，整份灌进上下文既贵又会把线索冲淡。脚本负责算，模型负责推理。

脚本产出（结构化摘要，不含原始行）：

- **会话清单**：每段会话的 `sid`、时长、事件数、错误数、结束方式（正常 / 崩溃 / 截断）
- **错误聚类**：按堆栈指纹归并，每类给出次数、首次与末次时刻、涉及的会话数
- **错误前序**：每类错误发生前 N 条事件的**共同前缀序列**——这是根因的第一嫌疑人
- **成功／失败对照**：同一流程走通的会话与出错的会话，事件序列的 diff
- **状态流转移矩阵**：哪些转移发生过、哪些转移异常终止
- **慢操作排行**：按 `ms` 排的 top-N，附发生时的帧号与前后事件
- **性能尖峰**：`spike` 时刻附近的事件窗口

模型拿到摘要后：形成 2～3 个根因假设 → 用 `analyze.py query` **定向回读**原始片段验证 → 出报告。
报告落 `Logs/telemetry/<时间戳>.md`（`Logs/` 已 gitignore），格式：现象 / 证据 / 根因 / 验证方法 / 建议改法。
**分析工作流只诊断，不改代码**——改动走 `/dev`。

---

## 5. 相关

**`module-missing-telemetry` 这条 lint 是 WARN 级（`exit 1`）**：Claude Code 对非 0/2 的退出码只把 stderr 给人看、不阻断模型，
所以这条提醒人看得到，AI 不一定看得到。改成 `exit 2` 能让 AI 也被拦，但那样就成了硬阻断，
与「埋点缺失不该拦住人干活」冲突，所以没这么做。

**更要紧的是它的覆盖面**：这条规则只在「整个模块一个埋点调用都没有」时才响。模块已经埋过点之后，
**新增代码里漏埋的点它完全沉默**——别指望 lint 兜住埋点完整性。
真正保证「做完模块就埋点」的是两个显式步骤：新建模块走 `/new-feature` 第 6 步，
迭代已有模块走 `/review-change` 的收尾门（只补本次改动新增的点，不背历史债）。lint 只是给人的兜底提醒。

- 模块埋点补全：`/instrument-module <模块>`；`/new-feature` 在验证之前有一步专门调它
- 日志门面：`Assets/_Project/Scripts/Core/Logging/Log.cs`
- 事件约定（MessagePipe 事件与埋点是两回事，别混）：`Core/Events/EventConventions.cs`
- 框架分层：[`architecture.md`](architecture.md)
