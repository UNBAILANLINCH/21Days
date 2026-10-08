# Pitfalls — 持久化错误记忆

> 踩过的坑写在这里，避免跨会话反复犯。由 `/learn` 沉淀；高频且**可正则检测**的，升级进 `.claude/skills/project-lint/rules.json` 让钩子自动拦。
>
> 只记**非显然、会再踩**的。代码里读得到、`git log` 里查得到的事实不重复记录。

格式：

```
## <简短标题>
- 现象：发生了什么（可观察到的）
- 根因：为什么会这样
- 正确做法：怎么做对
- 关联：相关规则 / 文档 / lint 规则 id
```

下面几条是种子（Unity 通用坑，不是本工程实际踩过的），随项目推进用 `/learn` 追加真实条目。

---

## `.meta` 没提交，别人打开工程引用全断
- 现象：把 `.cs`、预制体、图片提交了但漏了同名 `.meta`；同事或另一台机器拉下来打开工程，脚本从组件上掉了、预制体里的 Sprite 变成 None、场景里一片 `Missing (Mono Script)`。
- 根因：Unity 用 `.meta` 里的 GUID 做资产引用，不认路径。`.meta` 缺失时 Unity 会重新生成一个**新 GUID**，所有指向旧 GUID 的引用全部失效，而且是静默失效。
- 正确做法：资产与 `.meta` **永远成对提交**；移动 / 重命名 / 删除用 `git mv` / `git rm` 连 `.meta` 一起；新建脚本后切回 Unity 让它刷新生成 `.meta` 再提交（`/review-change` 第 2 步会查这个）。`.gitignore` 里那条 `!/[Aa]ssets/**/*.meta` 不要动。
- 关联：`.claude/rules/unity-assets.md #.meta 与版本控制`、`.claude/rules/project-root.md #生成物边界`、`/review-change`。

## `Library/` 误入库，仓库瞬间几个 G
- 现象：`git status` 里冒出成千上万个文件，提交体积几百 MB 到几个 G；clone 极慢，且换台机器还是要重新导入。
- 根因：`Library/`、`Temp/`、`Logs/`、`obj/`、`UserSettings/` 是 Unity 本地生成的缓存与导入结果，完全可由 `Assets/` + `ProjectSettings/` + `Packages/` 重建，但它们默认不被忽略。一旦提交进历史，光改 `.gitignore` 也清不掉体积。
- 正确做法：先确认 `.gitignore` 里的 Unity 生成物段落生效（`git status` 应看不到这些目录），再做第一次提交。已经误入库的用 `git rm -r --cached <目录>` 从索引移除后重新提交；进了历史的要 filter-repo 重写，**这件事必须先问用户**。日常：钩子 `guard.js` 会直接拒绝对生成物的写入，被拒就换做法，别绕。
- 关联：`.claude/rules/project-root.md #生成物边界`、`CLAUDE.md` 硬规则 1、`.claude/hooks/README.md`。

## 编辑器开着时 batchmode 跑测试，必定失败
- 现象：`Unity.exe -batchmode -runTests` 直接报工程被占用 / 拿不到锁而退出，日志里没有任何测试结果；重试几次还是一样。
- 根因：Unity 同一工程目录只允许一个实例持有 `Temp/UnityLockfile`。编辑器开着时批处理实例拿不到锁，这是设计如此，**不是偶发**，重试没有意义。
- 正确做法：先判断编辑器状态再选路径——编辑器开着且 MCP 已连 → 用 MCP 的 `run_tests`；编辑器没开 → batchmode；编辑器开着但 MCP 没连 → 停下，让用户在 Test Runner 里跑或先关编辑器。`/unity-test` 已经按这个分支写好，照着走。失败了不要重试、不要为了跑通去关用户的编辑器。
- 关联：`.claude/rules/unity-tests.md #怎么跑`、`.claude/skills/unity-test/SKILL.md`。

## 大场景合并冲突，只能靠手动重做
- 现象：两个分支（或两次会话）都改了 `SampleScene.unity`，合并时几百行 YAML 冲突；随手解出来的场景要么丢对象，要么引用指向错的 `fileID`，打开后一堆报错。
- 根因：场景是一整份 YAML，对象的 `fileID` 互相引用，文本层面的三方合并无法理解这种引用关系；场景越大冲突面越大。
- 正确做法：结构上**拆小场景**——主场景 + 功能子场景 Additive 加载；可复用的对象做成**预制体**，场景里只放实例，改动发生在预制体文件里而不是场景里。真冲突了用 Unity 自带的 YAML merge 工具（`Editor/Data/Tools/UnityYAMLMerge`，在 `.git/config` 里配成 `.unity`/`.prefab` 的 merge driver），不要手改文本；拿不准就保留一边再把另一边的改动用 MCP 重做一遍。
- 关联：`.claude/rules/unity-assets.md #场景与预制体`、`.gitattributes`。

## `public` 字段被当成对外接口，后面改不动
- 现象：图省事写了 `public float speed;`，Inspector 上确实能调；几周后要加校验 / 改名 / 改成从 SO 读，发现场景和预制体里已经序列化了这个字段名，改名即丢值，而且不知道有多少别的脚本在外面直接写它。
- 根因：Unity 里 `public` 字段同时承担了两个角色——序列化入口和对外 API。把它当「让 Inspector 能调」用，实际上顺手把内部状态公开成了契约，且序列化按**字段名**存值，改名就断。
- 正确做法：Inspector 可调字段一律 `[SerializeField] private`，对外只读用属性 `public float Speed => speed;`，需要写入就给明确的方法（带校验）。可调数值尽量进 ScriptableObject（`Assets/_Project/Data/<Module>/`），Inspector 上只拖一个配置资产。真要改已序列化的字段名，加 `[FormerlySerializedAs("旧名")]` 过渡。
- 关联：`.claude/rules/csharp-code.md #序列化与暴露面`、lint 规则 `public-field-exposed`。

## Windows 下钩子输出中文乱码 / JSON 解析失败
- 现象：钩子提示在对话里显示成一堆问号或 `锟斤拷`；或者钩子直接崩在 `json.loads`，报 `UnicodeDecodeError`，而同样的脚本在别的机器上好好的。
- 根因：Windows 的 Python 默认用系统 ANSI 代码页（中文环境是 GBK）而不是 UTF-8 处理 stdin / stdout。Claude Code 传给钩子的是 UTF-8 编码的 JSON，按 GBK 解就炸；中文提示按 GBK 输出到 UTF-8 终端就是乱码。
- 正确做法：钩子脚本里**显式指定 UTF-8**——读 stdin 用 `sys.stdin.buffer.read().decode("utf-8")` 而不是 `input()` / `sys.stdin.read()`；输出前 `sys.stdout.reconfigure(encoding="utf-8")`、`sys.stderr.reconfigure(encoding="utf-8")`；读写文件一律带 `encoding="utf-8"`。另外 JSON 里的 Windows 路径含反斜杠，解析出来后 `replace("\\", "/")` 归一化再做匹配。
- 关联：`.claude/hooks/README.md`、`.gitattributes`（行尾统一 LF）。

## Showcase 真实按键用例红：Game 视图没焦点，键盘事件被丢
- 现象：Taming / Disguise 这类用真实 Input System 按键（而非 Simulate）的回放用例第一轮跑红，检查点显示按键没生效；同样的回放重跑一次、或手动在编辑器里按同一个键却是好的。
- 根因（旧配置）：Game 视图的输入路由限制与编辑器整体失焦时的 `ResetAndDisableNonBackgroundDevices` 会使虚拟键盘事件失效；仅聚焦 Game 视图不能覆盖切到其它应用的情况。
- 当前做法：`ShowcaseScenario.ShowcaseSetUp` 保存原 `InputSystem.settings` 与 `Application.runInBackground`，创建 `HideAndDontSave` 临时 InputSettings 副本，设为 `IgnoreFocus` / `AllDeviceInputAlwaysGoesToGameView`，并临时开启后台运行。TearDown 的 `finally` 与退出 Play 的兜底回调均调用 `RestoreBackgroundInput`：恢复原设置对象与后台运行值、销毁副本，不写回项目输入资产。`FocusGameView` 仍用于展示回放，但 Unity 系统前台焦点已不是虚拟输入的前提。
- 若按键检查点仍红，先跑 `ShowcaseSelfTest.Keyboard_WhenGameViewUnfocused_RecolorsSquare`；同 fixture 的 `InputSettings_AfterScenarios_Restored` 检查恢复。`editor_is_focused=false` 仅说明编辑器失焦，不能单独证明当前失败由焦点造成，不靠反复聚焦或永久改项目设置掩盖问题。
- 关联：`.claude/skills/verify-module/SKILL.md`、`.claude/rules/module-verify.md`、`ShowcaseScenario.cs #ConfigureBackgroundInput / RestoreBackgroundInput`。日期：2026-09-29。

## 回放迁到 SampleScene 后，距离都是真实距离，别假设物体在身边
- 现象：照旧验证场景时代的写法「向右走 1 秒」之类硬编码位移，回放对象走不到目标附近，交互 / 触发类检查点判失败。
- 根因：Showcase 从独立验证场景迁到 `Assets/Scenes/SampleScene.unity` 后，用的是这张场景里真实摆放的出生点与间距：出生点离长者 3、离巡逻怪约 18，NPC 交互半径 2 且只认 `player`。这些距离比独立验证场景里凑近的占位摆法大得多，靠感觉给的位移量走不到。
- 正确做法：回放先用 `WalkTo` / `GoToPatrolLookout` 这类按逻辑位置走到目标附近，再触发交互，不要臆造一个位移时长；坐标优先从场景里的锚点物体或 `ShowcaseScenario.DemoScene.cs` 的坐标常量读，不写死数字。
- 关联：`.claude/rules/module-verify.md`、`docs/module-dev-spec.md`。日期：2026-09-28。

## Showcase 虚拟设备中途加入，会把已按住的动作复位
- 现象：回放里先按住潜行键（这时才建虚拟键盘），再推摇杆走动（这时才建虚拟手柄），潜行状态在推摇杆那一刻莫名掉线；单独测潜行或单独测移动都是好的，凑在一起才复现。
- 根因：Input System 每次 `InputSystem.AddDevice` 建新设备都会重新解析全部动作的绑定并复位动作状态；先建键盘、按住潜行，再建手柄时这次重新解析把潜行的按住状态冲掉了。
- 正确做法：`ShowcaseInputDriver` 第一次用任一设备就把手柄和键盘一起建出来（`EnsureDevices`），不要等到某个动作真正要用某只设备才建；`EnterWorldFromTitle` 点「开始」之前也会先调一次 `Input.Prime()`，回放作者不用自己操心建设备的时机。
- 关联：`Assets/_Project/Scripts/Tests/Showcase/Framework/ShowcaseInputDriver.cs`（文件头坑①、`Prime`/`EnsureDevices`）、`.claude/rules/module-verify.md`。日期：2026-09-28。

## Showcase 按键短于一个逻辑 tick 会被吞
- 现象：`Input.Press` 只按住一两帧就松开时，伪装 / 攻击等动作有时按下没反应，同一条回放重跑又是好的，看起来像间歇性 bug。
- 根因：`LiveInputSource` 只在 60 Hz 逻辑 tick 上读一次 `IsPressed`，没有按下沿锁存；编辑器渲染帧率明显高于 60 Hz 时，按住一两帧（几毫秒）的按键完全可能整段落在两次逻辑 tick 采样之间，被直接漏掉。这是运行时的**现状**而不是回放的 bug——真人手指按键的时长远长于一个逻辑 tick（约 16.7 ms），不影响实际游玩。
- 正确做法：`Input.Press` 保证按住至少 `MinPressFrames`（2 帧）且至少 `MinPressSeconds`（25 ms），跨过一次逻辑 tick 采样；要验「按到某个状态生效为止」用 `Input.PressUntil`，不要自己拼一个更短的按住时长。这条现状记进报告，不在回放里想办法掩盖过去。
- 关联：`Assets/_Project/Scripts/Tests/Showcase/Framework/ShowcaseInputDriver.cs`（文件头坑②、`Press`/`PressUntil`）、`Assets/_Project/Scripts/Tests/Showcase/Disguise/DisguiseShowcase.cs`（`ShortKeyboardPress_ReachesDisguiseAndAttack` 的文件头说明）。日期：2026-09-28。

## Showcase 界面刚打开就点按钮，会被控制器吞掉
- 现象：跳过对白弹出的确认弹窗，回放紧接着点「确认」按钮却一直不关，`WaitUntil` 超时判失败；界面截图看着弹窗明明已经打开了。
- 根因：界面一登记进 `IUIService` 就能被 `ResolveService` / `FindDeep` 取到按钮组件，但对应的控制器要等打开流程（`OpenAsync` 之后的初始化）走完才订阅 `onClick`；这中间有一小段窗口，太早调 `onClick.Invoke()` 点了个还没人听的按钮，点击就静默丢了。
- 正确做法：用 `ClickWhenReady(what, () => button, () => effective)` 代替一次性 `onClick.Invoke()`：它按固定间隔（默认 0.3 秒）重新取按钮再点一次，直到 `effective()` 判定生效，不用回放作者自己猜控制器什么时候订阅完。
- 关联：`Assets/_Project/Scripts/Tests/Showcase/Framework/ShowcaseScenario.PlayerDrive.cs`（`ClickWhenReady`）、`Assets/_Project/Scripts/Tests/Showcase/CharacterPuppet/CharacterPuppetShowcase.cs`（跳过确认弹窗用例）。日期：2026-09-28。

## MCP for Unity 两侧传输方式不一致，服务端永远报 0 个实例
- 现象：`/mcp` 里 `UnityMCP` 是 connected，读 `mcpforunity://instances` 却返回 `instance_count: 0`；任何工具调用都报 `No Unity Editor instances found`。而 Unity 的 `Window → MCP for Unity` 窗口里明明显示绿灯 `Session Active (project1)`。
- 根因：Unity 侧窗口的 `Transport` 被设成了 `HTTPLocal`（它自己在 127.0.0.1:8080 起了个本地服务），而本工程 `.mcp.json` 用的是 `--transport stdio`。stdio 模式的服务端靠 Unity 桥接写在 `~/.unity-mcp/unity-mcp-status-<hash>.json` 的状态文件发现实例；HTTP 模式的桥接不写这个文件，所以两边各自「运行中」却互相看不见。首次装包、或有人点过窗口里的 `Configure All Detected Clients`，都可能把传输方式改掉。
- 正确做法：在 **本工程** 的编辑器里打开 `Window → MCP for Unity`，`Transport` 改成 `Stdio`（若 HTTP 本地服务在跑先点 `Stop Server`），状态变成绿灯即可；**不要点** `Configure All Detected Clients`，它往用户级配置写东西，本工程只认项目级 `.mcp.json`。快速自检：`~/.unity-mcp/` 目录不存在 → 桥接没以 stdio 启动过。本机同时开着多个 Unity 时，先读 `mcpforunity://instances`，只 `set_active_instance` 到名字以本工程名开头的那个，不靠默认路由。
- 关联：`.claude/skills/unity-mcp/SKILL.md #故障排查`、`docs/ai-setup.md`；首次踩到 2026-09-15。

## 无 BOM 的 `.ps1` 在 Windows PowerShell 5.1 里中文被截断，报莫名其妙的语法错误
- 现象：新写的 PowerShell 脚本本机一跑就报 `字符串缺少终止符` / `表达式或语句中包含意外的标记`，报错位置落在一行中文字符串里，而脚本内容看起来完全正常；同一脚本用 `pwsh`（PowerShell 7）跑又没事。
- 根因：Windows PowerShell 5.1 读取**没有 BOM** 的脚本时按系统 ANSI 代码页（中文环境是 936/GBK）解码源码，UTF-8 的中文标点（引号、破折号等）被拆成错误字节，恰好撞上引号或括号就把字符串提前截断。PowerShell 7 默认按 UTF-8 读，所以不复现。
- 正确做法：仓库里所有 `.ps1` 一律存成 **UTF-8 带 BOM**（与 `scripts/build.ps1` 一致）；脚本开头再加 `[Console]::OutputEncoding = [System.Text.Encoding]::UTF8` 避免输出乱码。用 `Write` 工具新建脚本后记得补 BOM（`head -c 3 <file> | xxd` 应为 `ef bb bf`）。其它文本文件（`.cs`、`.md`、`.py`、`.json`）保持无 BOM 不变，这条只针对 `.ps1`。
- 关联：`.claude/skills/onboard/install_env.ps1`、`scripts/build.ps1`、`ai-docs/pitfalls.md #Windows 下钩子输出中文乱码`；首次踩到 2026-09-15。

## 新建的 `*LifetimeScope.cs` 被 VContainer 清空成模板
- 现象：用编辑器外的工具（Claude、IDE、脚本）新建一个文件名以 `LifetimeScope.cs` 结尾的脚本，切回 Unity 刷新后文件内容变成 VContainer 的空模板（只剩 `public class Xxx : LifetimeScope { protected override void Configure(...) {} }`），自己写的注册代码全没了，且没有任何提示。
- 根因：VContainer 包自带 `Editor/ScriptTemplateModifier.cs`，它注册了 `AssetModificationProcessor.OnWillCreateAsset`：Unity 第一次发现新文件、给它生成 `.meta` 时会触发这个回调，回调对所有路径以 `LifetimeScope.cs` 结尾的脚本无条件 `File.WriteAllText` 写入模板。它本意是给「Assets → Create → C# Script」的新建流程填模板，但分不清文件是编辑器里建的还是外部拷进来的。
- 正确做法：新建 `*LifetimeScope.cs` 时**先建一个空文件让 Unity 生成 `.meta`**（或先起别的名字再改名），确认 `.meta` 存在后再写入真正内容；已经被清空的重写一遍即可，第二次不会再触发。修改既有的 LifetimeScope 文件不受影响。
- 关联：`Assets/_Project/Scripts/Core/Boot/GameLifetimeScope.cs`、`docs/developer-guide.md #15 常见问题`；首次踩到 2026-09-15。

## `.gitattributes` 里的 `*.{png,jpg}` 花括号规则从来没生效
- 现象：`.gitattributes` 写了 `*.{png,jpg,psd} binary`，`git check-attr -a foo.png` 却返回 `text: auto`；二进制资产全靠 `* text=auto` 的自动探测兜底，某些含大段 ASCII 的二进制（如部分 `.bytes`、`.fbx` 文本格式）有被当文本做行尾转换的风险。
- 根因：`.gitattributes` 的模式匹配用的是 git 自带的 wildmatch，它不做 shell 那种 `{a,b}` 花括号展开，`*.{png,jpg}` 被当成字面量去匹配一个真的叫 `x.{png,jpg}` 的文件名。这条规则静默失效，没有任何警告。
- 正确做法：每个扩展名单写一行（`*.png binary`、`*.jpg binary`……）。改完用 `git check-attr binary text eol -- foo.png foo.unity` 实测，`binary: set` 才算生效。写完 `.gitattributes` 就顺手测一次，别相信肉眼。
- 关联：`.gitattributes`、`.claude/rules/unity-assets.md #.meta 与版本控制`；发现于 2026-09-15 波 2 验收。

## MCP `read_console` 读不到日志，其实是 Console 窗口的等级按钮被关了
- 现象：`read_console(action="get")` 稳定返回 0 条，连刚打的 `Debug.Log` / 警告都读不到，但 `execute_code` 能正常执行；编辑器 Console 窗口里看起来也「很干净」。
- 根因：MCP for Unity 的 `read_console` 走的是编辑器 Console 的内部 `LogEntries` 接口，它尊重 Console 窗口右上角 **Log / Warning / Error 三个等级按钮**的开关状态。有人为了清净把 Log 和 Warning 关掉后，这两类条目在窗口里不显示、经 MCP 也读不到，只剩 Error。这个开关存在本机 `UserSettings/`，不进 git，所以每台机器状态不同，别人复现不了。
- 正确做法：读不到日志先看 Console 窗口右上角三个等级按钮是否都亮着，点亮再读；`/onboard` 的人工步骤里提醒新人别关。真要过滤用 `read_console` 的 `types` / `filter_text` 参数，不要关窗口按钮。
- 关联：`.claude/skills/unity-mcp/SKILL.md #故障排查`、`docs/developer-guide.md #15.2`；波 1 踩到、波 3 定位，2026-09-15。

## 关掉 Run In Background，MCP 遥控下的 Play 模式必然「假死」
- 现象：用 MCP 进 Play 模式后，`await` 永远不返回、面板动画停在第一帧、连查两次 `Time.frameCount` 数值一样；代码不报错，像死锁。手动点回编辑器窗口后又突然全部跑完。
- 根因：Player Settings 的 **Run In Background** 关掉时（`ProjectSettings.asset` 里 `runInBackground: 0`），编辑器窗口失焦 Unity 就不推进 PlayerLoop。MCP 遥控时编辑器一直是失焦的，所以必现。这是**工程设置**，会连累每个用 MCP 的人。
- 正确做法：本工程保持 `runInBackground: 1`，不要在 Player Settings 里取消勾选。真要让玩家切出去时暂停，用 `OnApplicationFocus` 写玩法层的暂停逻辑，不要关这个开关。临时绕过可在运行时设 `Application.runInBackground = true`，但那只在当次 Play 有效，治标。Android 上应用切后台由系统挂起，这个开关基本不起作用，所以关它对成品包也没什么收益。
- 关联：`docs/developer-guide.md #15.6`、`ProjectSettings/ProjectSettings.asset`；2026-09-15 波 3 踩到、当天有人误关一次。

## `Editor.log` 是本机全局的，按默认路径读会读到别的工程
- 现象：分析埋点日志时摘要里的会话对不上——版本号、场景名、报错内容都不是本工程的；或者本工程明明刚跑过，日志里却一条埋点都没有。
- 根因：`%LOCALAPPDATA%\Unity\Editor\Editor.log` 这个路径**不带工程名，是整台机器共用的**。本机同时开着两个 Unity（本工程 + 参考工程）时，后启动的那个会把先前那份挤成 `Editor-prev.log`；分析脚本按固定路径去读，读到的就是另一个工程正在写的日志。实测发生过一次，而且非常难察觉：日志是真的、格式是对的、只是**不是你要查的那个工程**。
- 正确做法：`TelemetryService` 初始化时把 `Application.consoleLogPath`（Unity 给出的**当前实例真正在写的**日志完整路径）写进工程内的指针文件 `Logs/telemetry-source.txt`（`Logs/` 已 gitignore，里面没有任何埋点数据，只有一行「本工程的日志在哪」）。定位顺序固定为**指针文件 → 用户显式给的路径 → 猜默认路径**，猜出来的必须校验第一条 `session_start` 的 `p.prod` 与本工程 `productName` 对得上，对不上直接报错退出，不拿着别人的日志做分析。`/analyze-telemetry` 默认 `--source auto` 走的就是这条链；先跑 `analyze.py sources` 看这次用的是哪条。
- 关联：`docs/telemetry.md #日志到底在哪：指针文件`、`.claude/skills/telemetry/SKILL.md #0 定位日志源`；2026-09-16 波 4 实测。

## 往 `.cs` 里写含正则 / 反斜杠的代码，不能走 Bash heredoc
- 现象：用 Bash 的 heredoc（`cat > Foo.cs <<'EOF'` 一类）生成脚本或测试文件，写进去的 `\\.` 变成 `\.`、`\\s` 变成 `\s`、`\"` 丢了反斜杠；Unity 一编译就是一串莫名其妙的语法错误，而对话里看到的内容是对的。波 1 为此返工过一次。
- 根因：Bash 工具这一层对命令字符串还会做一次转义处理，heredoc 里的反斜杠被吃掉一层。正则字面量（`@"^\[Game\]\[T\] ..."`）、Windows 路径、JSON 里的 `\\.` 全是重灾区——**它们恰恰是「一个字符错了就整体失效、但不会报错」的东西**。
- 正确做法：写文件一律用 **Write / Edit 工具**，不用 shell 重定向拼内容。Bash 只用来跑命令、读文件、做检索。同理：往 `rules.json` 这类 JSON 里加正则、往 Python 里写 `re.compile(...)`，也走 Write / Edit。
- 关联：`CLAUDE.md #验证与工具`、`.claude/skills/project-lint/rules.json`、`.claude/skills/instrument-module/scan.py`；波 1 踩到，2026-09-16 波 4 复述。

## 行为 eval 全绿，不代表知识注入三层都验过了
- 现象：`/run-evals` 报 3/3 通过，于是认为「规则能送达、AI 能照做」这件事已经有回归保护。实际上**第三层（模块 guide 强制闸）一次都没被测到**，它坏了 eval 也照样全绿。
- 根因：知识是三层加载的——常驻的 `CLAUDE.md`、按文件类型 glob 注入的 `.claude/rules/`、以及编辑模块代码前由 `required-reads` 钩子强制先读的模块 guide。第三层靠钩子里的路径匹配触发，规则写死在 `required_reads.json`：`Assets/_Project/Scripts/Runtime/*/**`。而行为 eval 让 subagent 在 **scratchpad 的临时目录**里落盘（故意的，不能往仓库里写），临时目录不在那个路径下，匹配不命中，钩子根本不会触发。所以 eval 测得到前两层，唯独测不到第三层。
- 正确做法：看 eval 结果时把结论限定成「常驻规则与按类型注入的规则有效」，不要外推成「知识层没问题」。模块 guide 那道闸单独验：`.claude/hooks/tests/` 里的端到端用例覆盖了它（构造真实仓库路径喂给钩子，断言拦与放行），跑 `/gc` 就会带着跑。真要在 eval 里连它一起验，只能让 subagent 在仓库内真写再回滚，风险和成本都高一截——**当前选择是不做，并把这个边界写明，而不是让人以为覆盖全了**。
- 关联：`.claude/skills/run-evals/SKILL.md #覆盖边界`、`.claude/hooks/required_reads.json`、`.claude/hooks/tests/`；2026-09-16 建 eval 载体时识别。

## 中文显示成 `□`，改 TMP Settings 的 Default Font Asset 修不好
- 现象：做好中文 TMP 字体资产，设成 `TMP Settings` 的 **Default Font Asset**，以为全局生效；结果已有界面（`TitleView` / `SampleView`）的中文还是方框，新建的 TMP 组件倒是好的。
- 根因：预制体上的 TMP 组件把字体资产**显式序列化在 `m_fontAsset` 字段里**（这两个预制体指着 `LiberationSans SDF` 的 guid `8f586378...`），不是"留空用默认值"。TMP 的字符查找顺序是：组件自己的字体 → 该字体的局部 fallback → 局部 sprite asset → **TMP Settings 全局 fallback** → Default Font Asset → 默认 sprite asset（`TMP_Text.cs:6198` 一带）。Default Font Asset 排在倒数第二，确实会被查到，但它同时也决定**新建**文本组件默认挂哪个字体——用它兜中文，等于让以后每个新组件的主字体都变成中文字体，副作用比收益大。
- 正确做法：中文字体挂进 `TMP Settings` 的 **Fallback Font Assets**（全局 fallback），Default Font Asset 保持 `LiberationSans SDF`。这样英文数字仍走 Liberation 的字形，缺字才回落；且对**所有** TMP 组件生效，不管它们各自挂的是哪个字体资产，一个预制体都不用改。不要去改 `Assets/TextMesh Pro/` 里 `LiberationSans SDF.asset` 自己的局部 fallback——那是模板自带资产，原位不动。
- 关联：`docs/developer-guide.md #11.5`、`Assets/_Project/Art/Fonts/README.md`、`Assets/TextMesh Pro/Resources/TMP Settings.asset`；2026-09-16 上中文字体时踩到。

## TMP Dynamic 字体资产进一次 Play 就胖 2 MB，污染 git
- 现象：中文字体资产提交时才 6 KB，同事拉下来跑一次游戏，`git status` 里它就变成 2 MB 的改动；每个人每次 Play 都产生一份不一样的 diff，合并时天天冲突。**不只 Play**：跑带真实 View 的 EditMode 用例同样会烘字——2026-09-29 实测，三次全量 EditMode 之后 `Font_NotoSansSC_Regular SDF.asset` 从 6,404 B 涨到 2,131,629 B（53 个字，字幕里的中文被栅格化写回）。所以「跑完测试顺手提交」时先看这个文件，别把它带进提交。
- 根因：`AtlasPopulationMode.Dynamic` 的字体资产在**编辑器里**是按需栅格化后**写回资产**的——用到哪个字就把它烘进 `.asset` 内嵌的图集贴图，1024×1024 的 Alpha8 贴图序列化成 YAML 就是 2 MB 上下。这是 TMP 有意的设计（下次进 Play 不用重烘），不是 bug，也不会报任何提示。出包后的运行时只在内存里加字，不写回资产，所以**成品不受影响，受影响的只有仓库**。TMP 3.0.7 的 `TMP Settings` 里没有"打包时清掉动态数据"的开关，只能手动清。
- 正确做法：提交前在字体资产的 Inspector 上点 **Clear Dynamic Data**（脚本等价物是 `fontAsset.ClearFontAssetData(true)`，`true` 会把图集缩回 0×0），确认 `.asset` 回到几 KB 再提交。清空**不影响功能**：Dynamic 模式和声明的 1024×1024 图集尺寸、源字体引用都保留着，下次运行第一帧就会重新按需烘（实测从空表起步，首帧 `frameCount=2` 时中文已正常渲染）。 **Clear Dynamic Data 只清当前引用的那张图集**：Play 期动态扩容时新建的旧图集会以孤立子资产（`… SDF Atlas N`）留在 `.asset` 里，清完文件仍有几 MB；用 `AssetDatabase.RemoveObjectFromAsset` + `DestroyImmediate` 摘掉再 `SaveAssets`（2026-09-28 从 17 MB 清到 6 KB 时发现，HEAD 里 8.5 MB 就是这么来的）。（2026-09-29 复核：HEAD 里那份 22.2 MiB 是 `2095933` 有意入库的「动态字形增量」，含 **7 张没人引用的同名 `Atlas 2`** 孤立子资产；按本节口径已清回 6,404 B。口径维持「增量不入库」——缺字由运行时重烘兜住，落盘只调 `AssetDatabase.SaveAssetIfDirty(字体资产)`，不用 `SaveAssets`，理由见本文件「MCP 里调 `AssetDatabase.SaveAssets()`…」一条。）
- 关联：`Assets/_Project/Art/Fonts/README.md`、`docs/developer-guide.md #11.5`；2026-09-16 上中文字体时踩到。

## 批处理打包里临时改工程设置，恢复写在「报告结果」之后 = 永远不会恢复
- 现象：给 `BuildScript` 加 `-releaseBuild`（临时切 IL2CPP + ARM64 出 64 位包）后，出完包工作区里 `ProjectSettings/ProjectSettings.asset` 死死留着改动；代码里明明写了 `try/finally`，日志里那条「已恢复为……」却一次都没打出来过，像是 `finally` 被吃了。
- 根因：批处理模式下汇报结果的最后一步是 `EditorApplication.Exit(code)`，它**直接终止进程，不抛异常、不返回、不展开调用栈**——所以包着它的 `finally` 永远轮不到执行。把 `BuildPipeline.BuildPlayer` 和 `ReportResult` 一起放进 `try` 是最自然的写法，恰恰是错的。`Fail()` 同理，它内部也调 `Quit`，改完设置之后再插任何会 `Fail()` 的前置检查，同样会漏掉恢复。
- 正确做法：`try` 里**只放 `BuildPlayer`**，`finally` 里恢复设置，`ReportResult`（以及任何会调 `EditorApplication.Exit` 的收尾）**放在 `finally` 之后**；所有可能 `Fail()` 的前置检查全部提到「改设置」之前做，让「改设置 → BuildPlayer」之间不夹任何退出路径。恢复失败要 `Debug.LogError` 喊出来并提示 `git checkout -- ProjectSettings/ProjectSettings.asset`，别让人以为干净。
- 关联：`Assets/_Project/Scripts/Editor/Build/BuildScript.cs`（`Build` 的 try/finally、`RestoreAndroidSettings`）、`docs/developer-guide.md #14.2`；2026-09-16 加 `-Release` 开关时识别。

## `SetScriptingBackend` 写回默认值不会删条目：`{}` 变成 `Android: 0`，git diff 照样脏
- 现象：`finally` 里老老实实把脚本后端设回 `Mono2x`、架构设回 `ARMv7`，`AssetDatabase.SaveAssets()` 也调了，日志里「已恢复」也打了；`git diff ProjectSettings/ProjectSettings.asset` 却还是有一条改动——`scriptingBackend: {}` 变成了两行的 `scriptingBackend:` + `  Android: 0`。**实测确实会发生**，不是理论担心。
- 根因：`scriptingBackend` / `platformArchitecture` 这类字段序列化成的是**按平台键的字典**。工程从没显式设过 Android，字典就是空的 `{}`，读出来是该平台的默认值（Android 默认 Mono2x）。`SetScriptingBackend(Android, Mono2x)` 是**写入一条值为 0 的记录**，不是「删掉记录、回到默认」——Unity 没有公开的删除 API。语义完全一样，文本多一行，diff 照样脏。
- 正确做法：改设置之前把 `ProjectSettings/ProjectSettings.asset` 的**原始字节**一起 `File.ReadAllBytes` 存下来；`finally` 里三步走：① 按 API 恢复内存里的值 → ② `AssetDatabase.SaveAssets()` 刷盘 → ③ 拿磁盘上的字节和原始字节比，不一致就整体 `File.WriteAllBytes` 回写。三步缺一不可，顺序也不能换：只回写字节不恢复内存值，Unity 之后再刷一次盘就会把改动写回来；先比对再刷盘，等于把 Unity 的写入当成「已经恢复好了」。本工程实测两次 Android 出包前后 `ProjectSettings.asset` 的 md5 完全一致，就是靠这三步。
- 关联：`Assets/_Project/Scripts/Editor/Build/BuildScript.cs`（`RestoreAndroidSettings` / `RestoreProjectSettingsBytes`）、`docs/developer-guide.md #14.2`；2026-09-16 加 `-Release` 开关时实测踩到。

## `BuildSummary.totalSize` 不是 APK 体积，Android 上能差 20 倍
- 现象：打包日志里 `[Build] 体积 906.9 MB`，磁盘上的 `21Days.apk` 只有 41.6 MB；同一次构建两个数字差了 20 多倍。IL2CPP 的包尤其夸张（Mono 那次是 109.6 MB vs 33.4 MB，也差 3 倍）。照着日志汇报会把人吓一跳，以为包体爆了。
- 根因：`BuildReport.summary.totalSize` 统计的是**构建产物的未压缩总字节**（所有中间原生库、符号、未压缩资源都算进去），而 APK 是个 zip，`libil2cpp.so` 这类几十 MB 的原生库压缩率很高。这个字段在 Windows 那种「输出是一个目录」的平台上大致对得上，在 Android 上根本不是一回事。
- 正确做法：Android 的体积以**磁盘上 `.apk` 文件的大小**为准（`scripts/build.ps1` 报的就是这个，对的）；`BuildSummary.totalSize` 只当「未压缩产物有多大」的参考，别写进汇报。要拆体积构成用 Build Report 或直接 `zipfile` 列 APK，不要用这个字段。
- 关联：`Assets/_Project/Scripts/Editor/Build/BuildScript.cs`（`ReportResult`，目前仍在打这个数）、`.claude/skills/build/SKILL.md #3 汇报`；2026-09-16 加 `-Release` 开关实测时发现，**尚未修**。

## 两个会话共用一个工作区，整份暂存会把对方没审过的改动一起提交
- 现象：两个 Claude Code 会话同时开在同一个仓库上，各改各的。轮到自己提交时 `git add CLAUDE.md`，结果把对方正在写、还没给用户看过的内容一起提交了。对方那边的 `/review-change` 清单从此对不上，用户也没机会审那部分。
- 根因：`git add` 是**文件级**的，不是行级。而 `CLAUDE.md`、`ai-docs/docs/catalog.md`、`ai-docs/pitfalls.md`、`.claude/skills/new-feature/SKILL.md` 这类 harness 共享文件，两个会话都会改。只要同一个文件里有两家的改动，整份暂存必然越界。更麻烦的是**看文件名判断不出归属**：对方给某个服务加埋点会改到 `UIService.cs`、`developer-guide.md`、`pitfalls.md`，这些名字里都没有「埋点」二字。
- 正确做法：提交前先**按内容判归属**（`git diff -- <file> | grep -c` 数各自的特征词，别只看文件名），混合文件走这三步——
  1. `git show HEAD:<path>` 取基线，在基线上**只重放自己的改动**（字符串替换或按 `## ` 分块挑），生成一份临时文件；
  2. `git hash-object -w --path <path> <临时文件>` 写进对象库，再 `git update-index --cacheinfo 100644,<hash>,<path>` 只把这一版放进索引；
  3. 提交前 `git diff --cached | grep -i <对方特征词>` 兜底确认零命中，提交后再确认对方的改动仍留在工作区。
  纯属自己的文件照常 `git add`。**别用 `git add -p`**：交互式在本环境跑不了。
- 关联：`CLAUDE.md #硬规则 4`、`.claude/skills/review-change/SKILL.md #并发会话`；2026-09-15 起连续七次提交都这么做，2026-09-16 沉淀。
- **并行改同一批文件（2026-09-26）**：这次不是提交期撞车，是**开工期**就撞了——两个会话各自派 subagent 改 Player / Input / UIService 等同一批文件，而且两边设计还不一样（字段命名、迁移路径都不同）。发现得晚一步就会互相覆盖；这次是其中一个 subagent 中途 `git status`/`git diff` 发现对方的未提交改动跟自己要改的文件重叠，主动停手，另一边才没被覆盖。正确做法：开工前先 `git status` 看工作区有没有别人的未提交改动；有就先跨会话发消息划清各自改哪些文件、谁的底层设计为准，不要各写各的等提交时再对；改共用文件前**重新 `Read`**（别信自己上一轮读到的内容，对方可能已经改过），只做最小插入，不顺手重排或重构无关部分；改完等到「编译零错误 + EditMode 全绿」这个稳定点再通知对方开工，不要在半成品状态上招呼别人接手。
- **共享索引与临时索引提交（2026-09-26）**：`git commit` 不带路径会把别的会话 `git add` 进共享索引的文件一起带走（今天两个会话各踩一次）；混合文件即使按路径提交（`git commit -- <路径…>`），只要该路径在共享索引里已经是对方 `add` 过的混合版本，拿到的仍是连着对方未完成追加的那一份，不是自己单独的改动。稳妥做法是以 `git show HEAD:<文件>` 为基线，只重放自己这一轮的改动生成一份临时文件，再走一条完全不碰共享索引的临时索引提交：`GIT_INDEX_FILE=<临时索引路径> git read-tree HEAD` 建一份独立索引 → `git hash-object -w --path <path> <临时文件>` 把重放结果写进对象库，`GIT_INDEX_FILE=<临时索引> git update-index --cacheinfo 100644,<hash>,<path>` 只把这一条换成新版本 → `GIT_INDEX_FILE=<临时索引> git write-tree` 出树 → `git commit-tree <树> -p HEAD -m <信息>` 出提交对象 → `git update-ref refs/heads/main <新提交> <提交前的旧 HEAD>` 带旧值校验推进分支。用这条路径出完提交后，**共享索引里那些被提交的路径仍指向提交前的旧 blob**，相对新 HEAD 会显示成「反向改动」（看起来像被撤销了一样）；必须紧接着 `git reset -- <这些路径>` 把共享索引对齐新 HEAD，否则下一个用整份索引提交（`git commit` 不带路径，或 `git add -A`）的人会把这次刚提交的内容撤掉。

- **对方的合并 / 清理流程会把你的在建文件整个扫走（2026-10-07）**：这次不是提交期撞车，是**对方开工**撞的——另一边「临时分支合并前的全量暂存」跑了 `git reset --hard` + `git stash -u`（收未跟踪文件）切到合并分支，A 会话新建的 4 个 `.cs`、2 张 PNG、1 个脚本连同 Unity 刚生成的 `.meta` 一起从工作区消失，已跟踪文件上的改动也被回退。A 看到的现象是「文件一会儿在、一会儿不在」，同时 Unity 打 `Cannot open file '...meta' for write` 与 `'...png' does not exist`——**极像文件系统或沙箱故障**，顺着那条线查会白烧很久（本次就是这么烧的）。
- 正确做法：**工作区的文件莫名消失时，第一件事是 `git stash list` + `git reflog`，不是查文件系统**。查到 `stash@{0}` 后别急着 pop（会把对方的 WIP 一起倒进树里、并在对方的文件上产生冲突，本次对方 pop 时确实撞了 4 个文件）：先用 `git ls-tree -r --name-only 'stash@{0}^3'` 列未跟踪树、`git show 'stash@{0}:<path>'` 读已跟踪文件，确认自己的产物都在，再等对方收尾。反过来，**要 stash / 合并 / `reset --hard` / `clean` 之前先 `git status --short -uall` 看清有没有别人正在写的未跟踪文件**，有就先打招呼——共用工作区里「未提交」的另一半就是对方的在建成果。
- 关联：`CLAUDE.md #硬规则 4`、`.claude/skills/review-change/SKILL.md #并发会话`；2026-10-07 整份开局教程改动被扫掉时踩到（产物最终从 `stash@{0}` 全数取回，未丢）。

## 打包期间编辑器是关的，MCP 全部不可用，验证得提前想好命令行退路
- 现象：`/build` 要求关闭编辑器（工程锁只允许一个实例），于是打包这段时间里 `read_console`、`run_tests`、`execute_code` 全部连不上——而人往往是打完包才想起「我要怎么确认它对不对」，这时只剩一个退出码可看。
- 根因：MCP 是**遥控编辑器**的通道，编辑器进程没了通道自然断。这和「编辑器开着 batchmode 打不了包」是同一枚硬币的两面：两者互斥，不可能同时拥有。
- 正确做法：派打包类任务时**在派单里就写死命令行验证路径**，不要留给事后。可用的有——读 `Logs/build-*.log`（`grep -c "error CS\|BuildFailedException"`）；用 Python `zipfile` 列 APK 内容验架构与 bundle（别猜，要列）；`du -sh` / `stat` 量真实产物；`git status --short ProjectSettings/` 验临时改的工程设置是否恢复；`git show HEAD:<file>` 比对基线。全部不需要编辑器。
- 关联：`.claude/skills/build/SKILL.md`、`ai-docs/pitfalls.md #编辑器开着时 batchmode 跑测试`；2026-09-16 出 Windows / Android / Release 三种包时踩到。

## 编辑器停在未保存的空场景时，进 Play 什么都不会发生
- 现象：用 MCP `manage_editor(action="play")` 验证功能，等了 10 秒，该写的文件没写、Console 里一条相关日志都没有，看起来像功能坏了。实际是活动场景是 Unity 默认的未保存空场景——`manage_scene(action="get_active")` 返回的 `name` 是空串、`buildIndex: -1`、`rootCount: 1`，`GameBootstrap` 压根不在场景里，进 Play 只是跑了个空场景。
- 根因：Unity 打开工程时不保证恢复上次的场景（上次异常退出、别的进程动过工程、刚跑完 batchmode 出包，都可能停在 Untitled）。空场景照样能进 Play，不报任何错。
- 正确做法：任何需要**跑起框架**的验证（埋点、模块回放、状态流），进 Play 前先 `manage_scene(action="get_active")` 确认活动场景是 `Assets/_Project/Scenes/Boot.unity`，不是就先 `load` 它。判别特征：`buildIndex: -1` 或 `name` 为空 = 未保存的空场景。
- 关联：`.claude/skills/unity-mcp/SKILL.md`、`.claude/skills/verify-module/SKILL.md`；2026-09-16 验证埋点端到端时踩到。

## 预先拆好的大文件重构，会被 doom-loop 钩子当成打转拦下
- 现象：一次「单文件解析链路 → 支持多文件」的重构，连续编辑同一个 `.py` 到第 8 次时被 `doom-loop-detect.py` 拦下，提示可能在错误方向上打转。但那是一次事先拆解清楚、按计划分步推进的重构，不是反复试错。
- 根因：钩子按「同一文件连续编辑次数」判定，这个信号区分不了「反复试错」和「一次计划内的多步重构」——后者本来就会连着改同一个文件很多次。
- 正确做法：被拦时**不要拆钩子**（`CLAUDE.md` 硬规则 5）。改用**脚本化补丁一次性打完**——把多处编辑写进一个 Python 脚本跑一遍，既绕开连续编辑计数，改动也更好复核。如果某类重构反复被拦，把现象报给用户去评估判据要不要加例外，**不擅自改钩子**（钩子归策略层，改它要单独授权）。
- 关联：`.claude/hooks/doom-loop-detect.py`、`CLAUDE.md` 硬规则 5；2026-09-16 重构 `.claude/skills/telemetry/analyze.py` 时踩到。

## 只给 InputSystemUIInputModule 赋 actionsAsset，UI 一个点击都收不到
- 现象：Canvas、GraphicRaycaster、EventSystem 全都在，按钮 `interactable=True`，在按钮位置 `RaycastAll` 也只命中它自己——但点下去毫无反应，**控制台零报错零警告**。逐段查链路（按钮监听、事件转发、状态机、Addressables）每一环单看都正常。
- 根因：`InputSystemUIInputModule.actionsAsset` 的 setter 不会凭空按名字去新资产里找动作，它是拿模块**已有**的动作引用当模板去找同名的（`UpdateReferenceForNewAsset`：旧引用为 null 就直接 `return null`）。模块 `OnEnable` 时会自动塞一份 `DefaultInputActions` 当模板——如果为了"省一圈"把 GameObject 建成 inactive 再挂组件来阻止它，模板就没了，赋 `actionsAsset` 变成空转，`point`/`leftClick`/`submit` 等十个引用**全是 null**。模块没有任何输入源，所以既不响应也不报错。
- 正确做法：赋完 `actionsAsset` **必须逐个显式绑定**（`UIService.BindUIActions`：`module.point = InputActionReference.Create(asset.FindAction("UI/Point"))`，Click / Navigate / Submit / Cancel / ScrollWheel / MiddleClick / RightClick 同理）。动作名改了就绑不上，而且同样是静默失灵，所以找不到动作要报 Warn。排查这类"UI 没反应又不报错"的问题，第一刀砍在 `EventSystem.current.currentInputModule` 的 `point`/`leftClick` 是不是 null，比顺着业务链路一段段查快得多。
- 关联：`Assets/_Project/Scripts/Core/UI/UIService.cs` 的 `CreateEventSystem` / `BindUIActions`、`Assets/_Project/Data/Input/GameInput.inputactions` 的 UI 动作图；2026-09-16 点标题「开始」没反应时踩到。

## EditMode 测试用例总数不涨也不报错，其实是域没重载
- 现象：新测试文件已经编译进 `Library/ScriptAssemblies/Game.Tests.EditMode.dll`（反射查得到类型），但 MCP `run_tests` 跑出来的用例总数没变——该有 173 条只跑了 169 条，少跑的 4 条**既不算失败也不算跳过**，结果照样全绿。
- 根因：编辑器当前 AppDomain 里加载的还是旧程序集，跟磁盘上的 dll MVID 对不上——编译完了但没做域重载，测试框架枚举的是内存里那份旧的。触发条件是编辑器处于**未聚焦**状态（MCP 遥控时一直如此）；`refresh_unity(force, compile="request")`、菜单里的「立即编译」、`EditorUtility.RequestScriptReload()` 都压不住它。
- 正确做法：用例总数不涨又不报错，先怀疑域没重载，别怀疑测试没写对。判据 5 秒可证伪：`execute_code` 里反射 `AppDomain.CurrentDomain.GetAssemblies()` 查目标类型在不在、比对程序集 MVID 与磁盘 dll 是否一致；确认没重载就用 `CompilationPipeline.RequestScriptCompilation(RequestScriptCompilationOptions.CleanBuildCache)` 强制重编重载。日常预防：跑测试前先 `refresh_unity(force, compile="request")`，并核对用例总数是否符合预期——总数是这类静默失败唯一的观测点。
- 关联：`.claude/rules/unity-tests.md #怎么跑`、`.claude/skills/unity-test/SKILL.md`；2026-09-16 做回放系统时实测踩到。

## 一条用例留下的未观察 UniTask 异常，会随机砸中另一条无关用例
- 现象：EditMode 测试偶发失败，报 `Unhandled log message: '[Exception] InvalidOperationException: ThrowingView 打不开'`，而且每次挂的用例都不一样（`TelemetryServiceTests` 的限流用例、`RandomStreamTests` 的用例都中过），失败用例本身跟 UI、UniTask 毫无关系；同一份代码重跑一次又可能全绿。
- 根因：`UIServiceTests` 里"`OnOpenAsync` 抛异常"那条用例留下一个未被观察的 UniTask 异常，由 `UniTaskScheduler` 延迟发布到 Unity 日志系统，落在哪条用例的边界里取决于 GC 时机，于是随机砸中当时正在跑的某条——测试框架把它当成本次运行的未预期日志判失败。跑过 `execute_code` 之后尤其容易触发，动态程序集会改变 GC 时机。
- 正确做法：清空控制台挡不住它（只是清掉已有日志，异常还没发布）；跑测试前先 `refresh_unity(force, compile="request")` 触发一次域重载，把上一轮遗留的待发布异常一起带走。看到「失败用例与报错内容风马牛不相及」这种现象先按这条排查，不要去改那条无辜用例的断言。根治要在产生异常的那条用例里把 UniTask 异常观察掉（`.Forget()` 带异常处理，或接 `UniTaskScheduler.UnobservedTaskException`），这属于 UI 测试自己的范围。
- 关联：`Assets/_Project/Scripts/Tests/EditMode/Core/UIServiceTests.cs`、UniTask 的 `UniTaskScheduler`、`.claude/rules/unity-tests.md`；2026-09-16 做回放系统时连续踩到两次。
- **根治（2026-09-26）**：根因是 `Core/UI/UIService.cs` 打开面板失败路径对给并发等待者准备的 `UniTaskCompletionSource` 调了 `TrySetException`——单次打开（没有别的调用方在排队等同一个 `type`）时没有等待者去读这个 completion 的结果，异常就成了「未观察」，由 GC 终结器经 `UniTaskScheduler` 延迟再发布一次，砸中当时随便哪条正在跑的用例。修法是在 `TrySetException` 之后**立刻读一次它自己的结果**（`completion.Task.GetAwaiter().GetResult()`，包一层 try/catch 吞掉同一个异常）把它标记为已观察，本次调用方仍然拿到原始异常（下面照常 `throw`），互不冲突。配了回归用例 `UIServiceTests.OpenAsync_WhenOnOpenAsyncThrows_LeavesNoUnobservedTaskException` 断言不再有未观察异常。跑测试前强制刷新域重载的做法仍然推荐（挡的是其它遗留场景），但对这一条已经不再是必需。

## 测试自己把依赖装上了，于是接线缺口全程不报
- 现象：回放系统的验证全绿——PlayMode Showcase 2/2、EditMode 173/173，录制、状态哈希、完整快照、漂移检测逐条验过。但**真实启动路径下录出来的回放只有输入流**：状态哈希和快照全是空的，漂移检测、起点恢复、快照续跑全部空转。整个系统最核心的能力是空的，而没有任何一条验证发现得了。
- 根因：框架只定义了 `IReplayStateProvider` 契约，**没有默认实现，组合根里也没注册没接线**。而 Showcase 为了自己能跑，`new` 了一个提供者挂上去。于是所有验证验的都是「这套类凑在一起能工作」，不是「产品启动起来能工作」——**接线那一层从头到尾没被任何一条验证覆盖过**，缺口被测试自带的依赖完美掩盖。
- 正确做法：凡是靠容器接线才生效的能力，必须有**一条验证是从真实容器里解析出对象、再查它手上的依赖是不是真的挂上了**（反射读私有字段也算），而不是测试自己装一套。自查判据很简单：把测试里「自己 new 依赖挂上去」那几行删掉，看验证还跑不跑得起来——跑不起来，说明你验的是测试的接线，不是产品的接线。配套的一条：测试收尾还原时要还原成**容器里那个**，不是 `null` 也不是写死的初值，否则测试跑完会把真实运行环境弄坏（这次也真的发生了，Showcase 跑完把容器里的提供者还原成了 null）。
- 关联：`Assets/_Project/Scripts/Core/Replay/ReplayStateRegistry.cs`、`Core/Boot/GameLifetimeScope.cs` 的 `RegisterReplay`、`PRP/replay/tasks.md`；2026-09-16 做回放系统时踩到。

## `gen-tables.ps1` 首次下载 Luban 后解压失败：本机没有 7-Zip
- 现象：脚本报「解压 Luban.7z 失败：自带的 tar 解不了，也没找到 7-Zip」，退出码 2；`Tools/Luban/Luban.7z` 留在原地。
- 根因：Windows 自带 `tar.exe`（libarchive）解不了这份 7z；脚本只回退到 `C:\Program Files\7-Zip\7z.exe`。
- 正确做法：装 7-Zip（`winget install 7zip.7zip`）后重跑；或不装系统软件，用 Python：`pip install --user py7zr` 后 `py7zr.SevenZipFile('Tools/Luban/Luban.7z').extractall('Tools/Luban')`，再删包、确认 `Tools/Luban/Luban.dll` 在，重跑脚本会跳过下载直接生成。`Tools/` 已 gitignore。
- 关联：`scripts/gen-tables.ps1`、`docs/developer-guide.md` 配置表一节；2026-09-25 落地对话表时踩到。

## Luban JSON 数据源：字段不能缺省，一文件多记录要写 `*@`
- 现象：JSON 里省掉 `revision`、`blocking` 这类有「默认值」的字段，生成报「结构:'Node' 字段:'revision' 缺失」；把多条记录放进一个数组文件、`input` 写文件名，报「requires an element of type 'Object', but the target element has type 'Array'」；写 `*文件名` 又报「input 文件不存在」。
- 根因：Luban 5.1 的 JSON 读取器按 bean 定义逐字段取值，schema 里 `default=`、`type="int#default=1"` 都不被识别；目录输入是「一文件一记录（JSON 对象）」，数组文件必须用 `*@文件名` 语法声明。
- 正确做法：JSON 每个字段显式写（空串、空数组、`revision: 1`、`blocking: true` 都写）；一棵树 / 一条记录一个文件放目录里，`input="目录名"`；确实要一个文件装多条就 `input="*@文件名.json"`。「缺省值」在代码适配层做（例如 `DialogueCatalog` 把 `revision < 1` 按 1 处理）。
- 关联：`Tables/Defines/dialogue.xml`、`ai-docs/docs/modules/dialogue/dialogue-module-guide.md` 内容表一节；2026-09-25 踩到。

## 给共享 MonoBehaviour 加新序列化字段，默认值会静默改掉别的场景
- 现象：给 `EncounterSceneView` 加 `flipByMoveDirection` 时默认 `true`，本场景勾了看着没问题；但 `Scenes/Verify/Disguise.unity`、`Taming.unity` 也挂着同一个组件，它们的 YAML 里没有这个新字段，Unity 按脚本默认值加载，两个无关模块的验证场景就多出了翻转纸片的行为，没有任何测试或人知道。
- 根因：序列化字段缺省走脚本默认值；共享组件被多个场景 / 预制体引用时，默认值等于对所有旧场景做了一次静默改动。
- 正确做法：新字段默认值取「旧行为不变」的那个（bool 默认 `false`、数值默认「不生效」的 0），只在需要的场景里显式打开；提交前 `grep` 一下该组件的 `m_Script` guid 出现在哪些 `.unity` / `.prefab` 里，逐个确认。
- 关联：`.claude/rules/csharp-code.md` 序列化与暴露面；code-reviewer 在 2026-09-25 的探索场景审查里抓到。

## 用 MCP 在活动场景里搭 UI 预制体，散件会随场景一起保存
- 现象：用 MCP 在 `SampleScene`（活动场景）里现搭一个 UI 预制体的层级（建 GameObject、挂组件、调 RectTransform），
  搭完再另存为 `.prefab`；`SampleScene` 里却多出一个同名的根物体——那些散件本来就是场景里的真实 GameObject，
  另存为预制体只是**复制**了一份，原实例仍留在场景根节点上。2026-09-26 波 3 在 `SampleScene` 里发现并删除了一个
  遗留的 `ExplorationHudView` 根物体。
- 根因：MCP 的 `manage_gameobject` 是对**当前打开的场景**操作，没有「预览场景」概念；在活动场景里搭好再拖成
  Prefab（或用 `manage_prefabs` 从场景对象生成）不会自动清场景里的源实例，这一步需要额外手动删除，容易漏。
- 正确做法：优先用 `PrefabUtility.LoadPrefabContents` / `SaveAsPrefabAsset` 这类走**预览场景**的 API 建预制体
  （不经过任何已打开的真实场景）；确实要在活动场景里现搭再转存的，转存完立刻把场景里的源实例删掉，保存场景前
  跑 `git diff -U0 -- <场景文件> | grep m_Name` 复核有没有多出不该在的根物体。
- 关联：`.claude/rules/unity-assets.md #场景与预制体`、`PRP/exploration-whitebox/tasks.md` T6；2026-09-26 波 3 发现。

## Showcase 回放中途别人保存 .cs，Play 内域重载把测试协程吞掉，进度卡住不报失败
- 现象：`run_tests(PlayMode, Game.Tests.Showcase)` 跑到某条用例后进度不再前进（2026-09-26 Dialogue 回放停在 2/23 达 4 分钟），`get_test_job` 一直 running；编辑器仍在 Play、帧数在涨、`timeScale = 0`、对白面板开着，控制台刷第三方 `IngameDebugConsole.DebugLogManager.LateUpdate` 空引用；既不超时也不判失败。回放框架的 `Check` / `WaitUntil` 都用 `realtimeSinceStartup` 计超时，与时停无关，别往那查。
- 根因：并行会话保存了 `.cs`（当时是 `Core/Save/*`），编辑器偏好「Script Changes While Playing」默认是「Recompile And Continue Playing」，于是在 Play 中重编译并做域重载；UTF 的 `[UnityTest]` 协程随旧域被丢掉，没人再推进它。`Editor.log`（本工程那份，见上文「`Editor.log` 是本机全局的」）里紧跟在最后一条 `[VERIFY]` 之后能看到 `Requested script compilation because: Assetdatabase observed changes` → `initialDomainReloadingComplete`。
- 正确做法：`ShowcaseScenario` 的 SetUp 调 `EditorApplication.LockReloadAssemblies()`、TearDown 在 `finally` 里对称 `UnlockReloadAssemblies()`（静态计数防重复解锁，退出 Play 时兜底全部释放），回放期间的改动只排队、结束后再编译。兜底：本机 Preferences → General → Script Changes While Playing 设为「Recompile After Finished Playing」；并行派单时约定回放期间不保存 `.cs`。已经卡住的：`run_tests(clear_stuck=true)` + `manage_editor(action="stop")`，再重跑。
- 关联：`Assets/_Project/Scripts/Tests/Showcase/Framework/ShowcaseScenario.cs`（`AcquireReloadLock` / `ReleaseReloadLock`）、`.claude/skills/verify-module/SKILL.md`、`.claude/rules/module-verify.md`；2026-09-26 H1 / H6 并行时踩到。

## MCP 改完场景没当场保存，别的会话一跑 PlayMode 测试改动就没了
- 现象：用 `execute_code` / `manage_gameobject` 在 Additive 打开的 `SampleScene` 里建了一批物体，还没保存，并行会话启动了 PlayMode 测试；退出 Play 后编辑器只剩 `Boot.unity`，`SampleScene` 连同未保存改动一起消失（2026-09-26 探索白盒波 9 踩到，灰盒 `MultiLevel` 重建了一遍）。
- 根因：UTF 跑 PlayMode 前会记下场景布局、跑完按**它开始时的磁盘版本**恢复；它开始时 `SampleScene` 的改动还没落盘，或它根本不恢复 Additive 打开的场景。多会话共用一个编辑器时，场景的「脏状态」不是你独占的。
- 正确做法：场景改动**在同一次 MCP 调用里建完就 `EditorSceneManager.SaveScene`**，调用开头先判 `EditorApplication.isPlayingOrWillChangePlaymode`，是 Play 就退出等待，不要改；改前把场景文件复制一份到 scratchpad，保存后 `diff` 复核只增不删。
- 关联：`.claude/skills/unity-mcp/SKILL.md` 改场景纪律、`PRP/exploration-whitebox/tasks.md` 波 9。
- **补充（2026-09-28 换箱子标记那轮）**：两个会话的子代理在同一个编辑器里、同一个打开的 `SampleScene` 实例上各改各的物体，最后由一次保存一起写进磁盘（两组改动都在，没有覆盖）；但期间另一会话的 `.cs` 触发域重载，MCP 桥重启后 `batch_execute` 返回「0 成功、load 拒绝：当前场景有未保存改动」，磁盘却已带着全部改动落盘——**返回值失败不等于没保存，判定以磁盘 diff 为准**。`manage_scene load` 遇到脏场景会拒绝，但编辑态直接 `EditorSceneManager.OpenScene(Boot, Single)`（`execute_code` / 菜单）不会，会把别人内存里的改动悄悄丢掉：切场景前先 `manage_scene get_active` 看 `isDirty`，脏了就等对方保存，不要硬切。

## 等距相机下「人在桥下」不等于「桥挡住人」
- 现象：遮挡半透明回放把玩家放在桥正中下方 (17.25, 10.25)，桥始终不淡出；以为射线或层写错了。
- 根因：相机偏移 (0, 11.8, −14)，相机→玩家胸口的视线俯角约 40°；离地 2.6 m、南北宽 2.5 m 的桥，在视线方向上挡住的是它**北侧** 2～3 m 的人（桥投影往后落），正下方的人相机从桥南沿下面看得见。
- 正确做法：遮挡用例先用 `Physics.RaycastAll(相机, 胸口)` 在编辑器里算一遍被挡的站位再写；挡人的位置 ≈ 遮挡物北沿 + (离地高 − 0.8) / tan(俯角)。宽大的甲板（10 m）人站在下方中部确实会被挡。
- 关联：`Runtime/IsometricExploration/OccluderFadePresenter.cs`、`ExplorationShowcase.Occluder_FadesBridgeWhenPlayerBeneath`。

## 可选第三方 SDK 的适配层：asmdef 引用不存在的程序集不会报错，但程序集名猜错会静默失效
- 现象：给 Live2D 做适配层时担心「SDK 缺席、asmdef 引用了不存在的 `Live2D.Cubism.*` 会编译报错」，准备绕远路（`~` 目录 + 复制安装）。实测（2026-09-26）：asmdef 的 `defineConstraints` 未满足时 Unity **跳过整个程序集**，连引用解析都不做，控制台零错误零警告，`CompilationPipeline.GetAssemblies()` 里也没有它。反过来真正的坑是：PRP 里按印象写的引用名 `Live2D.Cubism.Core` / `Live2D.Cubism.Framework` 都不存在——官方 CubismUnityComponents 运行时只有一个 `Live2D.Cubism.asmdef`（另有 `Live2D.Cubism.Editor`）。名字错了不会报错，导入 SDK 后适配层照样不编译、符号也不会被检测脚本加上，表现成「装了 SDK 什么都没发生」。
- 根因：约束未满足的程序集对编译管线是不可见的，错误只会在符号真的被定义之后才暴露；而符号又靠检测那个（写错的）asmdef 名来加，两头互相掩护。
- 正确做法：可选 SDK 一律「独立 asmdef + `defineConstraints` + 编辑器脚本按 SDK 的 asmdef **文件名**检测后写符号」；引用名与检测名必须去官方仓库的文件树核对（不要凭记忆写），并在适配层文件头列出用到的 API 与「未本地编译验证」字样；导入 SDK 后第一件事是编译一次适配层。
- 关联：`Assets/_Project/Scripts/Runtime/Live2D/Game.Live2D.asmdef`、`Scripts/Editor/Performance/Live2DDefineSync.cs`（菜单 `21Days/演出/检查 Live2D 符号` 是状态锚点）、`PRP/performance-pipeline/prp.md` 2.7。（叠加模式与 Live2D 已于 2026-09-28 下架）

## 编辑器代码用 Timeline API 建好的时间轴，会被别的测试运行器收尾时回滚 Undo 打成空壳
- 现象：`PerformanceTemplateFactory` 刚建好 8 秒 / 5 轨的 `.playable`，回放一进 Play 就秒结束；磁盘上的资产变成 `m_Tracks: []`、`m_FixedDuration: 0`（`CreateAsset` 时的初始状态），只多一个没人引用的 Markers 子资产（2026-09-26 演出回放第 1 轮）。
- 根因（推断，未复现）：`TimelineAsset.CreateTrack` / `CreateClip` / `CreateMarkerTrack` 在编辑器下会往 Undo 栈推快照；随后并行会话的 EditMode 运行器或别的工具回滚了当前 Undo 组，资产被打回初始状态并在下一次保存时写盘。
- 正确做法：编辑器工具用代码建完时间轴 / 预制体后，对资产本体与每条轨道 `Undo.ClearUndo(obj)` 再 `SaveAssetIfDirty`；建完就保存，别让「刚建好、还没落盘」的状态跨越任何测试运行。另外 Animation 轨默认 `ApplyTransformOffsets`，片段按**首帧相对**叠加到轨道偏移上：代码建的入场动画要把轨道 `m_Position` 设成片段首帧值，否则演员会整体漂移（同一轮踩到）。
- 关联：`Assets/_Project/Scripts/Editor/Performance/PerformanceTemplateFactory.cs`、`PRP/performance-pipeline/tasks.md` T26。

## 并行会话整目录恢复 `ProjectSettings/`，会把别人刚加的图层 / 符号一起抹掉
- 现象：本会话用 MCP `add_layer Performance` 加进 `TagManager.asset` 后，同一小时内该文件两次被改写回没有这个图层的版本（一次只是编辑器内存丢了、一次文件也回去了），场景与预制体里的第 9 层引用随之悬空（2026-09-26）。
- 根因：`/verify-module` 的副作用清单建议恢复 `EditorSettings.asset`，有会话顺手用 git 把 `ProjectSettings/` **整个目录**恢复到 HEAD；`TagManager.asset` / `ProjectSettings.asset`（脚本符号）都在里面。
- 正确做法：恢复 ProjectSettings 只恢复**点名的那一个文件**；加了图层 / 标签 / 编译符号的会话立刻给并行会话发一条；回放或提交前 `git diff ProjectSettings/TagManager.asset` 复核图层还在。
- 关联：`ai-docs/pitfalls.md #两个会话共用一个工作区`、`.claude/skills/verify-module/SKILL.md` 第 7 步。

## 多会话共用一个工作区：别人 `git add` 过的文件会随你的 `git commit` 一起进库
- 现象：任务编辑器那轮首笔提交混进六个别人暂存的文件（并非本轮改动）。
- 根因：`git commit` 提交的是整个索引，索引是共享的，不会自动区分「谁 `add` 的」。
- 正确做法：`git commit -F <信息文件> -- <路径…>` 按路径提交，提交后 `git show --stat` 核对只有自己的文件；已混入且未推送时用 `git reset --soft <基底>` 再按路径重建，别人的文件会回到已暂存状态，不会丢。
- 关联：`docs/commit-convention.md #多会话共用工作区`、`ai-docs/project-guide.md` 硬规则第 4 条；2026-09-26 任务编辑器那轮。

## Write 工具整份重写 Markdown 会把换行变成 CRLF
- 现象：`git diff` 警告 `CRLF will be replaced`，整份文件每一行都带 `\r`。
- 根因：Write 工具整份重写已有文件时按平台行为写入换行符，与仓库既有 LF 不一致。
- 正确做法：已有文件一律用 Edit 局部改，不用 Write 整份重写；写完 `tr -cd '\r' < <文件> | wc -c` 应为 0；不小心变了用 Python `data.replace(b"\r\n", b"\n")` 转回再存盘。
- 关联：`docs/designer-guide.md` 曾被整份重写变成 CRLF；2026-09-26 任务编辑器那轮。

## MCP `execute_menu_item` 会把菜单项执行两遍
- 现象：调一次 `execute_menu_item` 执行某菜单项，Console 里出现两份完全相同的输出。
- 根因：MCP 工具本身的行为（未定位到具体原因），与被调用菜单项的代码无关。
- 正确做法：验证菜单行为时按「结果会出现两遍」去看，不要当成代码 bug 去加去重；要精确验证一次调用的效果就用 `execute_code` 直接调那段静态方法。
- 关联：`.claude/skills/unity-mcp/SKILL.md`；2026-09-26 任务编辑器那轮验证「校验任务表」菜单时发现。

## Luban 生成物内容相同时不重写文件，`.bytes` 时间戳不变
- 现象：`TryGenerate` 返回成功，Console 也有「生成完成，资产已刷新」，但 `quest_tbquest.bytes` 的 mtime 没变。
- 根因：Luban 生成时内容与磁盘上已有文件相同就不重写，是生成器的正常行为，不是生成失败。
- 正确做法：判断「这次生成跑过」看 Console 的「生成完成，资产已刷新」，不要看文件时间戳；要验证内容确实变了，就先改一处表数据再生成对比。
- 关联：`Assets/_Project/Scripts/Editor/Config/GenerateTablesMenu.cs`；2026-09-26 任务编辑器那轮 T5 实测。

## MCP 预制体舞台改动可能不落盘，agent 报告「已改」不能当数
- 现象：波 8（`ResetButton` 挪左下角）agent 用 MCP 预制体舞台（`open_prefab_stage` → `set_property` →
  `save_prefab_stage`）改完并做了 YAML 复核，报告里也写了复核结果，但用户后续截图发现按钮还压在
  左上角原位；实际 `git diff` 一看，提交进库的 `ExplorationHudView.prefab` 里 `ResetButton` 的
  `anchoredPosition` 仍是改前的 `(16, -130)`——舞台里的修改没有真正写回磁盘上的预制体资产。
- 根因：预制体舞台（Prefab Stage）是编辑器里的一份内存副本，`save_prefab_stage` 依赖舞台仍处于
  预期状态才会落盘；并发会话频繁切场景 / 跑 PlayMode 测试触发的域重载或场景切换会打断舞台，
  agent 拿到的「保存成功」返回值不能保证这次保存真的写到了资产文件，YAML 复核如果读的是内存态
  或读的时机在真正落盘之前，同样会得出「已经改对」的假阳性。
- 正确做法：改预制体用 `execute_code` 走 `PrefabUtility.LoadPrefabContents(path)` 加载一份独立的
  离屏副本 → 直接改 `RectTransform` 等组件字段 → `PrefabUtility.SaveAsPrefabAsset(root, path, out ok)`
  → `PrefabUtility.UnloadPrefabContents(root)` → `AssetDatabase.SaveAssets()`；这条路径不经过任何
  可能被打断的编辑器舞台。改完用 Bash 直接 `grep`/`sed` 读磁盘上的 YAML 核对字段值，并跑
  `git diff --stat -- <预制体路径>` 确认真的有改动落盘；再 `refresh_unity` 一次后**重新读一遍 YAML**，
  确认没有被后续的资产刷新或别的会话覆盖 / 回滚。全程不要在任何场景里留下该预制体的实例。
- 关联：`ai-docs/pitfalls.md #用 MCP 在活动场景里搭 UI 预制体，散件会随场景一起保存`；
  `PRP/exploration-whitebox/tasks.md` 波 8 T16、波 12（本次用新方法改 `ImmersiveButton`/`ResetButton`
  右上角位置并复核成功）。

## 走 Boot 的回放会写玩家真实存档槽，第 4 次新游戏就进不了场景
- 现象：`ExplorationShowcase` 8 条用例前 3 条 PASS，第 4 条起全部卡在 `EnterExploration()` 的
  「等进入探索场景」超时；跑完一看，真实存档目录 `<persistentDataPath>/saves/` 下多出了
  `slot1.json`、`slot2.json`、`slot3.json`。
- 根因：标题「开始」现在走真实存档链路（`SessionTitleRouter` → `GameSession.NewGameAsync` →
  `SessionTitleRules.PickNewGameSlot` 选第一个空槽 → `MonsterEncounterState` 进场景后自动写
  `<IPlatformService.SaveRoot>/slot{N}.json`）；`SessionConfig.slotCount = 3`。回放走的是真 Boot，
  没有覆盖 `SaveRoot`，每条用例点一次「开始」就真占用一个槽。3 个槽在第 3 条用例后全部写满，
  第 4 条起 `PickNewGameSlot` 找不到空槽返回 0，路由改成打开 `SaveSlotsController.OpenAsync
  (SlotsMode.NewGame)` 选槽面板而不是直接进场景，回放却仍在等「进入探索场景」，于是必超时。
- 正确做法（**已根治，2026-09-26**）：不用再各自模块备份/还原槽文件了——`PlatformServiceBase.SaveRootOverride`
  已落地，`ShowcaseScenario.ShowcaseSetUp/TearDown` 统一在加载 Boot 之前把 `SaveRoot` 重定向到临时隔离目录、
  收尾时清理，细节见下一条「从『开始』进场景的回放把玩家真实存档写满了」。`ExplorationShowcase` 若仍在用
  `IsolateSaveSlots` / `RestoreSaveSlots` 这套自备份，应当改为直接依赖基类的覆盖目录并删掉这段自建逻辑
  （同 `SessionShowcase` 2026-09-26 的改法）；新写的回放不要再照抄这条里的临时备份方案。
- 关联：`Assets/_Project/Scripts/Tests/Showcase/Exploration/ExplorationShowcase.cs`；
  `Assets/_Project/Scripts/Core/Save/JsonSaveService.cs`（槽文件命名 `GetSlotPath`/`ProfilePath`）；
  `Assets/_Project/Scripts/Runtime/Session/SessionTitleRules.cs`；
  `ai-docs/pitfalls.md #从『开始』进场景的回放把玩家真实存档写满了`；2026-09-26 探索白盒波 13，同日由存档会话根治。

## 编辑器 `isCompiling` 长期 true、控制台错误与磁盘不符、反射看到旧签名 —— 程序集重载锁泄漏
- 现象（2026-09-26 实测）：`QuestInstaller` 的 CS7036 在磁盘早已修好后仍报了十几分钟；`EditorApplication.isCompiling`
  一直是 `true`；反射看 `QuestService` 的构造仍是旧的 7 参数版本；同时 `editor/state` 资源里 `is_compiling` 却报
  `false`，两个信息源互相矛盾。
- 根因：某次测试运行被域重载打断时，`LockReloadAssemblies` 的计数没能归零（大概率是回放框架的锁在异常路径下没走到
  对称的 Unlock），编辑器认为「还有人要求不许重载」，于是磁盘上已经修好的代码永远编译不进来，控制台报的错、反射看到的
  签名都停在锁死那一刻，看起来像是「怎么改都没用」。
- 正确做法：判定依据用**反射看程序集里的实际签名**，不要看控制台报错（控制台这时候是旧状态的回声）。解法是
  `execute_code` 里连续调几次 `UnityEditor.EditorApplication.UnlockReloadAssemblies()`（锁是计数式的，一次不一定够）
  + `CompilationPipeline.RequestScriptCompilation()` + `AssetDatabase.Refresh()`，再 `refresh_unity` 一次。预防：回放
  框架（`ShowcaseScenario`）的锁已经在 TearDown / 退出 Play 时对称释放，但别的会话正在跑测试期间不要保存 `.cs`，
  被打断的正是这条路径。
- 关联：`Assets/_Project/Scripts/Tests/Showcase/Framework/ShowcaseScenario.cs`（`AcquireReloadLock` / `ReleaseReloadLock`）、
  `ai-docs/pitfalls.md #Showcase 回放中途别人保存 .cs`；`PRP/save-session/tasks.md`。

## 从「开始」进场景的回放把玩家真实存档写满了
- 现象：`SessionShowcase` / `ExplorationShowcase` 这类从标题「开始」进场景的回放，会经真实的存档服务把 `slot1..N.json` 写进 `IPlatformService.SaveRoot`——也就是玩家本机真实存档目录（Windows 上 `%LOCALAPPDATA%Low/DefaultCompany/<产品名>/saves/`）。跑上三条这样的用例后三个槽全被占满，`Session/Exploration` 回放里再点「开始」就不再直落新游戏，而是弹出选槽面板等玩家二选一，回放的 `WaitUntil` 等不到预期状态，直接超时挂住。
- 根因：`PlatformServiceBase.SaveRoot` 在 2026-09-26 之前只有一种算法——拼 `Application.persistentDataPath`，不区分「真实运行」与「回放测试」。Showcase 复用的是**真实**存档服务（不是 mock），这是它「验证真实启动路径」这个设计初衷决定的，副作用是槽文件必然落进真实目录。最早发现这问题的 `SessionShowcase` 曾经用「SetUp 备份本机槽文件到临时目录、TearDown 还原」自保，但这只解决了「不污染开发者本机存档」，没解决「三条用例之间互相占槽、槽用满后『开始』行为改变」这个根本问题；且每个新写的 Showcase 都要抄一遍这段备份/还原逻辑，抄漏一步就会把真实存档删掉或还原不回去。
- 正确做法：不再各自模块自保，改成框架统一在**根目录**上做覆盖——`PlatformServiceBase.SaveRootOverride`（静态，默认 null，只给编辑器内测试/回放用）。`SaveRoot` 属性每次读取都先看这个覆盖，非空就直接返回，不走 persistentDataPath 那条老路径。`ShowcaseScenario.ShowcaseSetUp` 在**加载 Boot 场景之前**把它设成 `Application.temporaryCachePath/showcase-saves/<模块小写>-<用例名>`（保证目录存在且为空），`ShowcaseTearDown` 的 `finally` 里无条件置回 `null` 并删除该目录（删失败只 Warn，不影响用例判定）。因为设置发生在 Boot 加载、容器建出 `PlatformServiceFactory.Create()` 之前，且属性每次调用都现读覆盖值（不缓存旧值），所以时机上必然生效。模块作者的 Showcase 不再需要（也不应该）自己碰 `SaveRoot`、自己备份/还原槽文件——直接读 `platform.SaveRoot` 拿到的就是这个隔离目录，坏档用例往这个目录里写坏文件即可，清理交给基类。
- 关联：`Assets/_Project/Scripts/Core/Platform/PlatformServiceBase.cs`（`SaveRootOverride`）、`Assets/_Project/Scripts/Tests/Showcase/Framework/ShowcaseScenario.cs`（`ShowcaseSetUp`/`ShowcaseTearDown`）、`Assets/_Project/Scripts/Tests/Showcase/Session/SessionShowcase.cs`、`.claude/rules/module-verify.md #编写规范`；2026-09-26 由 `SessionShowcase` 的手工备份/还原自保方案收敛为框架统一方案。

## `run_tests(clear_stuck=true)` 清掉了别的会话真实在跑的 PlayMode 任务
- 现象：另一会话的模块回放跑到第 7 条用例被清掉，任务状态直接变成 `Failed`，而那条回放本身没有卡死，只是还没跑完。
- 根因：`clear_stuck=true` 把「当前有一个 `tests_running` 的任务」当成孤儿状态直接清掉，但共用编辑器时这个任务可能是
  别的会话正常在跑、只是还没到自己的用例。
- 正确做法：清之前先读 `mcpforunity://editor/state` 的 `tests.current_job_id` 与 `started_unix_ms`，只清确认是**自己
  起的**、且**已经超过 5 分钟没有任何进展**的任务；共用编辑器时优先用 `get_test_job(wait_timeout=60)` 之类的轮询等待，
  不要一遇到「暂时没结果」就 `clear_stuck`。
- 关联：`.claude/skills/unity-mcp/SKILL.md`、`ai-docs/pitfalls.md #Showcase 回放中途别人保存 .cs`；`PRP/save-session/tasks.md`。

## URP 相机栈只接受渲染器类型一致的相机：舞台 Overlay 相机在 3D 场景里整段不画
- 现象：演出舞台相机（Overlay）叠进主相机的 `cameraStack` 后，2D 验证场景正常，SampleScene（3D）里黑边、字幕都在、舞台内容一片空白；服务没走退路、没埋点，只有游戏内调试面板的 Warning 计数每帧涨 1（2026-09-26 冒烟发现）。
- 根因：URP 资产里有多个渲染器（0 号 Renderer2D、1 号 UniversalRenderer），舞台相机 `rendererIndex = -1` 落到默认的 0 号，主相机用 1 号；`UniversalRenderPipeline` 对渲染器类型不同的叠加相机直接跳过并每帧告警 `Only cameras with compatible renderer types can be stacked`，MCP 的 `read_console` 读不到这条原文。
- 正确做法：叠加前把 Overlay 相机的渲染器对齐到主相机（URP 14 没有公开的索引 getter，反射读 `UniversalAdditionalCameraData.m_RendererIndex` 再 `SetRenderer`，收尾还原），叠加后再比一次 `scriptableRenderer.GetType()`，不一致就走 Base 退路并埋点；验证场景与正式场景用的渲染器不同时，两边都要冒烟一次。
- 关联：`Assets/_Project/Scripts/Runtime/Performance/PerformanceService.cs`（`AttachCamera` / `ReadRendererIndex`）、`PRP/performance-pipeline/tasks.md` T31b。（叠加模式与 Live2D 已于 2026-09-28 下架）

## 序列帧画布宽不是 4 的倍数，出包时块压缩退回不压缩；编辑器里看不出来
- 现象：方舟小人第一次渲出的画布宽 242 / 318 / 330 px，导入后 Inspector 显示正常；但这台机器编辑器里所有贴图（连现有纸片）`Texture2D.format` 都是 `RGBA32`，从编辑器里根本判断不了出包是否压缩。
- 根因：DXT / ETC2 / ASTC 都是 4×4 块压缩，宽高不是 4 的倍数时 Unity 出包会退回不压缩（体积 ×4～×8）；本机编辑器不在导入时压缩，所以没有任何提示。
- 正确做法：序列帧画布宽高一律取 4 的倍数（`scripts/ark-spine-frames/render_frames.py` 已按此补边：宽两侧对称、高只补顶，脚底原点不变）；美术交付规范写进了 `docs/artist-guide.md` 3.4。验证压缩结果看 `TextureImporter.GetAutomaticFormat("Standalone")` 或出包报告，不看编辑器里的 `format`。
- 关联：`Assets/_Project/Scripts/Editor/Importers/SpriteImportProcessor.cs`（首次导入默认压缩）；2026-09-28 序列帧小人那轮。

## 纯纸片 NPC 的 `Visual` 在半身高，小人挂它下面脚底会随相机俯仰偏移
- 现象：想照玩家的接法把小人实例挂到 NPC 的 `Visual` 下，位置、缩放怎么抵消都不贴地。
- 根因：NPC 纸片 pivot 在中心，`Visual` 摆在 y 0.8、缩放 0.625；`CameraBillboard` 把整个 `rotation` 设成相机旋转（含 38° 俯仰），子物体绕半身高的点转，脚底被甩离地面。玩家 / 巡逻者的 `Visual` 原点就在脚底，所以没这个问题。
- 正确做法：NPC 根下另建 `PuppetVisual`（原点 + `CameraBillboard`），小人挂其下；纸片停用当朝向源（`flipX`），`trackedRoot` 指 NPC 根。步骤见 `docs/developer-guide.md` 6.15。
- 关联：`Assets/_Project/Scripts/Runtime/IsometricExploration/CameraBillboard.cs`、`ai-docs/docs/modules/characterpuppet/characterpuppet-module-guide.md`；2026-09-28 序列帧小人那轮。

## 逻辑 tick 与渲染帧脱节导致拖影 / 抖动
- 现象：遭遇场景里角色纸片看起来顿一下再跳一下，转向和贴墙滑动时最明显；直接把逻辑 `Position` 抄给 Transform 时数值本身没错，但同一逻辑值会连续渲染好几帧，下一次 tick 到达时又整段跳过去。
- 根因：逻辑位置只在固定 tick（`SimulationRunner` 的固定步长）推进，渲染却是每帧都刷新；视图如果直接读当前 tick 的 `Position`，帧率与 tick 率脱节的部分就会表现成拖影 / 抖动。
- 正确做法：视图按 `SimulationRunner.Accumulator / FixedDeltaTime` 算出的 alpha，在 `PreviousPosition` 与 `Position` 之间插值（纯函数 `EncounterProjection.InterpolationAlpha` / `InterpolatePosition`），不要直接抄 `Position`。凡是整体改写 `Position` 的新入口（传送、剧情挪人）都要同步 `PreviousPosition`（调 `SyncPreviousPosition` 或走 `Rules.Reset`），否则插值会把角色从旧位置「拉」过来一次。表现层把碰撞修正写回逻辑位置时只改被挡的那一根轴，另一根轴保留逻辑值——两根轴都改会把逻辑位置往回拉，插值点因此变慢，贴墙滑动会跟着变慢。
- 关联：`Assets/_Project/Scripts/Runtime/Monster/EncounterSceneView.cs`（`Bind` / `Interpolate`）、`Assets/_Project/Scripts/Runtime/Monster/EncounterProjection.cs`（`InterpolationAlpha` / `InterpolatePosition`）、`ai-docs/docs/modules/isometricexploration/isometricexploration-module-guide.md #修改时检查`；2026-09-28。

## 序列帧走路播放速率不能用「速度 × 常数」
- 现象：给方舟序列帧小人接走路速率时套用旧的「速度 × 0.53」，跑步状态下动画播放成了约 2.65 倍快放，脚步和位移完全对不上。
- 根因：0.53 是按旧循环（0.6 秒一圈）反算出来的系数；套到时长不同的新剪辑（方舟 Move 循环 1.13 秒）上就失效——常数背后隐含的是「剪辑本身按什么速度做的」，剪辑变了常数就要跟着变，不是一个能通用的系数。
- 正确做法：播放速率 = 角色实际速度 ÷ 剪辑制作时的地速（`ChibiPuppet.walkClipSpeed` / `runClipSpeed`，来自序列帧 `meta.json` 的 `groundSpeed`），算出来的比值再夹到 `[0.8, 1.6]`；需要角色跑得更快就出专门的 run 帧，不要靠调高上限去让 walk 剪辑硬撑。
- 关联：`Assets/_Project/Scripts/Runtime/CharacterPuppet/ChibiPuppetMotionRules.cs`（`PlaybackRate`）、`Assets/_Project/Scripts/Runtime/CharacterPuppet/ChibiPuppet.cs`（`walkClipSpeed` / `runClipSpeed`）；2026-09-28。

## MCP `execute_code` 改预制体可能被重复执行，非幂等脚本会把新节点加出好几份
- 现象：用 `execute_code` 走 `LoadPrefabContents → 加子物体 → SaveAsPrefabAsset` 给 `PerformanceView.prefab` 加面板节点，只调了一次、返回 `saved=True`；磁盘 YAML 里 `PanelBackground`、`Avatar`、`SkipHint` 等每个新节点都有 4 份，`get_history` 里同一段代码记了 2 次执行。
- 根因：同 `execute_menu_item` 执行两遍那条，MCP 这一侧会重放请求（未定位到具体原因）；「加子物体」类脚本不幂等，每多跑一次就多一份。
- 正确做法：改资产的 `execute_code` 脚本开头先查「是否已改过」（如 `if (find("PanelBackground") != null) return "already-applied";`），做成幂等；改完照「预制体舞台改动可能不落盘」那条用 `grep "m_Name:" | sort | uniq -c` 核对节点没有重复。已经加重了就用 `git show HEAD:<路径> > <路径>` 只恢复这一个文件再重跑（不要整目录 checkout，共用工作区）。
- 关联：`ai-docs/pitfalls.md #MCP execute_menu_item 会把菜单项执行两遍`、`#MCP 预制体舞台改动可能不落盘`；2026-09-28 演出世界模式 / 对白面板那轮。

## 服务里懒建 `DontDestroyOnLoad` 根，EditMode 测试里一调就抛
- 现象：给 `PerformanceService` 写 EditMode 服务级测试，`PlayAsync` 一调就同步结束，断言「应在播放」失败；主相机、摆放都没被动过。
- 根因：`EnsureRoot` 里 `Object.DontDestroyOnLoad(go)` 在非 Play 模式直接抛 `InvalidOperationException`（「can only be used in play mode」，已实测），异常被 `PlayAsync` 包进任务里，测试不 `GetResult` 就看不到。
- 正确做法：运行时代码里的 `DontDestroyOnLoad` 用 `if (Application.isPlaying)` 包住（EditMode 下根物体留在当前场景，测试 `TearDown` 按名字删掉）；测试断言「在播放」时顺手把已完成任务的异常带进失败信息，别只报 `Expected: True`。
- 关联：`Assets/_Project/Scripts/Runtime/Performance/PerformanceService.cs`（`EnsureRoot`）、`Tests/EditMode/Performance/PerformanceServiceWorldTests.cs`；2026-09-28 同一轮。

## 世界空间头顶标记跨模块复用同一张图，不同含义撞脸
- 现象：SampleScene 里物资箱头顶标记和任务目标标记长得一模一样，玩家分不清「这里能开箱」还是「这里是任务目标」。
- 根因：箱子 `Marker` 直接借了对白模块的 `Art/Sprites/Dialogue/Marker_Focus.png`（白「!」气泡）并染黄 (1, 0.85, 0.3)，恰好与 `Prefabs/World/QuestTargetMarker.prefab` 的图和颜色完全相同；各模块各自「顺手借图」时没人看全局。
- 正确做法：新模块的头顶标记用自己目录下的专属图（如 `Art/Sprites/Loot/marker_crate.png`），**形状与颜色都要区分**，不靠同一张图换染色；加新标记前把现有三种（NPC 可对话、任务目标、可拾取箱子）放一起比一眼。
- 关联：`docs/artist-guide.md` 6.8 节、`ai-docs/docs/modules/loot/loot-module-guide.md #接线要求`；2026-09-28。

## 同一类面板在不同会话里各自对标参考图，视觉漂移
- 现象：NPC 交互对白框（`DialogueView.prefab`）做成深色底 + 左右两侧立绘压暗，同期时间轴演出对白框（`PerformanceView.prefab`）做成白色圆角 + 头像位与样式；玩家在同一场景里连续碰到两种对白框，风格明显不一致。
- 根因：两处面板分别在不同时间、由不同会话对标不同参考图搭出来，各自都符合自己那份需求，但没人比对过「这两个面板在玩家看来是不是同一套视觉语言」；样式散落在预制体与 `DialogueConfig` 的压暗 / 缩放参数里，代码侧也没有任何东西能提醒「另一份面板已经不一样了」。
- 正确做法：**后建的面板照先有的那份规范来**，不各自另起参考图；确定「同一视觉语言」的一组面板要有**一致性测试**锁住共用节点名与样式值（本例是 `Tests/EditMode/Dialogue/TalkPanelConsistencyTests.cs`，逐节点比对 `DialogueView.prefab` 与 `PerformanceView.prefab` 的 Rect / Image / TMP 属性），改任一份不同步另一份就挂测试；样式全放预制体，代码只填文字与显隐，不要把「压暗」「缩放」这类视觉差异编码进表现层参数（`DialogueMotionSettings` 一度带了三个压暗 float，后来发现这本身就是两套视觉语言各自演化出的产物，直接删掉）。
- 关联：`ai-docs/docs/modules/dialogue/dialogue-module-guide.md #对白面板视觉与演出面板共用规范`、`ai-docs/docs/modules/performance/performance-module-guide.md #对白面板`、`Assets/_Project/Scripts/Tests/EditMode/Dialogue/TalkPanelConsistencyTests.cs`；2026-09-28。

## 正式场景引用了测试程序集脚本
- 现象：`Assets/Scenes/SampleScene.unity`（在 Build Settings 与 Addressables 里，会进包）的 `player` 挂着 `IsometricPlayerController3D`，脚本却在 `Assets/_Project/Scripts/Tests/Showcase/IsometricExploration/`（asmdef `Game.Tests.Showcase`）。编辑器里一切正常，出包后测试程序集不进包，组件变 missing script。
- 根因：asmdef 依赖方向只在代码层有 `invariants.py` 拦（Runtime 不引用 Tests），资产层（场景 / 预制体挂了哪个脚本）没人查；回放用的临时控制器顺手挂进了正式场景。
- 正确做法：正式场景、预制体只挂 `Scripts/Core/`、`Scripts/Runtime/` 下的脚本；回放需要的控制器由 Showcase 运行时 `AddComponent` / 实例化并 `Track()`。`invariants.py` 新增「正式场景 / 预制体不引用测试程序集脚本」检查，`/gc` 会报。
- 关联：`.claude/rules/project-root.md #目录与 asmdef 依赖方向`、`.claude/skills/evolution/invariants.py`；2026-09-28。

## InitTestScene 残留堆积
- 现象：Project 窗口 `Assets/` 根下出现一排 `InitTestScene6392…unity`，2026-09-28 一次清掉 41 个。
- 根因：Unity Test Framework 跑 PlayMode 测试时在 `Assets/` 根建临时启动场景，正常结束会自删；手动停 Play、跑测试时触发重编译、进程被杀、多会话共用编辑器互相打断，都会残留。`.gitignore` 已忽略，所以 git 看不见，只在编辑器里堆。
- 正确做法：编辑器不在 Play 时直接 `rm Assets/InitTestScene*.unity Assets/InitTestScene*.unity.meta`，零风险；`invariants.py` 新增残留检查，`/gc` 会报数量。少残留的办法：PlayMode 测试跑的时候别停 Play、别改代码触发编译。
- 关联：`.gitignore` 里 InitTestScene 注释、`.claude/skills/evolution/invariants.py`；2026-09-28。

## 回放静置期玩家自走提前触发演出，恢复检查伪装成代码故障
- 现象：2026-09-28 两次演出回放（`ScenePerformanceShowcase`）「结束后恢复」的三项检查连续失败；反复复跑同一份代码却能通过，看起来像间歇性 bug。
- 根因：回放拍 before 快照前有一段静置等待，这段时间里玩家自己走动了（疑似卡键 / 外部输入落到了 Game 视图），提前走进了触发区，before 快照拍在演出已经开始运行之后，之后的所有「恢复」断言自然和预期对不上；代码本身没有问题。
- 正确做法：排查回放失败先看 Editor.log 里的埋点时间线（`performance/trigger_fired`、`interaction/focus_changed`（2026-10-08 前为 `dialogue/focus_changed`）等），比对时间戳能不能对上用例预期的顺序，别一上来就改代码；回放在拍 before 快照前加「环境干净」守卫（演出尚未运行、玩家存活），环境不干净就直接报环境问题而不是走进断言失败。`ScenePerformanceShowcase.WalkIntoTrigger()` 已加上这道守卫（`Check("回放环境干净：演出尚未运行、玩家存活", …)`）。
- 关联：`Assets/_Project/Scripts/Tests/Showcase/Performance/ScenePerformanceShowcase.cs:223`–`228`（`WalkIntoTrigger`）；2026-09-28 字幕逐字 / HUD 恢复那轮。

## 共用一台编辑器的并发会话互相干扰
- 现象：一个会话在跑对白回放、开着 MonsterEncounter 场景；另一个会话的 agent 为了改 SampleScene 把当前场景切走、又把回放节奏（EditorPrefs，全局）切成「快速」，对方的回放节奏和打开的场景都变了。
- 根因：Unity 编辑器状态（当前场景、EditorPrefs、Play 状态、选中物体）是单例全局的，MCP 谁都能改，没有隔离；agent 只顾自己的任务，不知道别人在用。
- 正确做法：用 MCP 改场景前先 `manage_scene get_hierarchy` / 读 `mcpforunity://editor/state` 记下当前打开的场景，改完存盘后切回去；改 EditorPrefs 这类全局设置（回放节奏）先读旧值，跑完还原；派单 prompt 里明确写「共用编辑器，跑完还原场景与节奏」。看到别人的场景在 Play 就等，不抢。
- 关联：`.claude/skills/unity-mcp/SKILL.md #改场景 / 预制体的纪律`、`.claude/skills/verify-module/SKILL.md`、记忆 `shared-worktree-commit-discipline`；2026-09-28。

## EditMode 测试里 `Time.unscaledDeltaTime` 不是每次循环的真实墙钟间隔，不能拿它断言「过了多少秒」
- 现象：给演出「自动继续」（`PerformanceRules.TickAuto`）写 EditMode 测试，想用循环调用 `Tick` 的次数乘 `Time.unscaledDeltaTime` 来断言「过了 N 秒该怎样」，结果同一段 14 ms 墙钟内单次循环量到的 `unscaledDeltaTime` 有时会累计超过 1 秒，断言时灵时不灵。
- 根因：EditMode 下播放循环不是固定帧率驱动，编辑器繁忙（导入、编译、别的会话在操作）时会把好几帧的间隔压缩成一次很大的 `unscaledDeltaTime`；它反映的是「距上次编辑器循环过了多久」，不是稳定的渲染帧间隔，靠它反推「真实经过了多少秒」并不可靠。
- 正确做法：「满多少秒触发」这类语义放纯 C# 规则测试里，直接构造固定的 `dt` 值喂给 `TickAuto` / `Tick` 钉住边界（累计到秒数前不触发、到了触发一次、之后按新一轮重新计），不依赖真实循环次数反推秒数；只有「服务这条线接没接上」这种接线测试才允许用真实循环等待，且要按墙钟时间设超时（见下一条）。
- 关联：`Assets/_Project/Scripts/Tests/EditMode/Performance/PerformanceRulesTests.cs`（`TickAuto_*` 系列，纯 C# 钉边界）、`Assets/_Project/Scripts/Tests/EditMode/Performance/PerformanceServiceWorldTests.cs:289`–`290`；2026-09-28 演出 LOG / 自动那轮。

## UniTask 的编辑器 PlayerLoop 在 `EditorApplication.isUpdating` 时停摆，EditMode 用例按帧数等待会误报超时
- 现象：`PerformanceServiceWorldTests` 里等演出收尾的用例，全量跑测试套件时偶发超时，单独跑这条用例却总是通过。（2026-09-29 复跑：同文件的 `PlayAsync_AutoOn_ContinuesAtHoldAfterSecondsWithoutConfirm` 也出现过「全量跑偶发失败、单跑与另一次全量都过」——它已按墙钟等 10 秒，仍可能在编辑器停摆期间耗光预算。见到这条失败先单跑复现一次，别当成改动引入的回归。）
- 根因：UniTask 的编辑器循环在 `EditorApplication.isPlayingOrWillChangePlaymode || isCompiling || isUpdating` 时整帧跳过（`PlayerLoopHelper.cs:339`）；共用编辑器时别的会话在导入资产 / 编译，`isUpdating` 会为 true 一段时间，这段时间里 `await UniTask.Yield` 根本不推进，「等 N 帧」的循环会把预算的帧数在停摆期间耗光，还没等到条件成立就报超时。
- 正确做法：EditMode 里等异步任务收尾一律按墙钟时间等待（`Time.realtimeSinceStartup` 起点 + 固定秒数上限，如 10 秒），不要按帧数上限；帧数上限只在能保证编辑器不会被别的会话打断时才可靠。
- 关联：`Library/PackageCache/com.cysharp.unitask@2e993ff18f/Runtime/PlayerLoopHelper.cs:339`、`Assets/_Project/Scripts/Tests/EditMode/Performance/PerformanceServiceWorldTests.cs:691`–`696`（`WaitCompletedRealtime`）；本文件「共用一台编辑器的并发会话互相干扰」一条；2026-09-28 演出 LOG / 自动那轮。

## MCP 里调 `AssetDatabase.SaveAssets()` 会把别的会话改脏的资产一起写上磁盘
- 现象：2026-09-28 下架示例 greeting 那一单（`4da29a3`）在 MCP 里改完目标资产后调了 `AssetDatabase.SaveAssets()`，工作区随之多出本单根本没碰的改动——字体 SDF 资产（`Art/Fonts/Font_NotoSansSC_Regular SDF.asset`）与 `ProjectSettings/EditorSettings.asset`，提交前得逐个挑出去。
- 根因：`SaveAssets()` 保存的是**编辑器内存里全部标脏的资产**，不是「刚才改的那个」；共用一台编辑器时，别的会话进 Play 撑大的 TMP 动态字体图集、测试运行器改过的 Enter Play Mode Options 等都挂在同一个脏列表里，一并被写盘。
- 正确做法：改单个资产后只调 `AssetDatabase.SaveAssetIfDirty(asset)`（预制体走 `PrefabUtility.SaveAsPrefabAsset`，它自己落盘），**禁止在 MCP 脚本里调 `AssetDatabase.SaveAssets()`**；改完 `git status --short` 核对只多了目标文件，多出来的别人的改动不提交、也不擅自还原（可能是对方还没存完的工作）。本文件「MCP 预制体舞台改动可能不落盘」一条里的 `AssetDatabase.SaveAssets()` 那一步以本条为准。
- 关联：`4da29a3`；本文件「TMP Dynamic 字体资产进一次 Play 就胖 2 MB」「MCP 预制体舞台改动可能不落盘」「共用一台编辑器的并发会话互相干扰」；`.claude/skills/unity-mcp/SKILL.md`；2026-09-28。

## Codex 必读文档反复重读仍未记账：输出截断与会话记录格式不兼容
- **现象**：独立 `Get-Content -Raw` 已执行，修改代码仍被必读闸拦住；提高命令输出预算、把文档分成多次读取也未解决。2026-09-29 排查缓存：Performance 指南正文 31931 字符，hook 摘要仅 25707 字符、`full_body_present=false`；另有 adapter.py 正文完整（9369 字符，结果 9371 字符）但 `successful=false` 的旧诊断。缓存只保留各会话最近一次失败，不能据此统计总发生次数。
- **根因**：三处适配问题叠加。① 命令 stdout、交付给模型的工具结果、PostToolUse 摘要是不同层；增加命令的 `max_output_tokens` 不会解除后两层的截断。本次环境的 hook 摘要约 10000 token 即截断，这不是所有客户端的固定规格。② 旧回退逻辑要求会话文件含 `CommandExecution` 完成事件，但本次会话文件没有该事件；又只检查一条父调用输出，不能累计同一调用的多批交付。③ 旧成功判据在整段文本搜索 `output truncated` / `exit code: 1`，把正文讨论这些字样误判成工具失败；现已先排除完整正文再判工具状态。
- **正确做法**：保持独立完整读取；长文从同一次成功读取结果按换行分批 `notify({exit_code: result.exit_code, output: chunk})`，全部在同一个父 `functions.exec` 调用内交付，示例见 [Codex hooks 说明](../.codex/hooks/README.md#长文读取示例)。适配器绑定真实 PostToolUse 的父调用，下一次前置事件核验同一调用的交付全文后记账；仍拒绝非零退出、缺段、截断和混入别的调用。不要跨调用拼历史、手填账本、缩减必读清单或关闭 hook。再次失败先查 `.codex/.cache/<会话散列>/last-read-failure.json` 的全文 / 状态 / 截断字段及 `pending-read.json`，不要盲目重复读取。**压缩或清除上下文后账本主动重置是现行设计，需重新读取，不属于本故障。**
- **关联**：[adapter.py](../.codex/hooks/adapter.py) 的 `successful` / `transcript_response` / `handle`；[test_adapter.py](../.codex/hooks/test_adapter.py) 覆盖正文含错误字样、完整与缺段输出、父调用不匹配、非零退出、延迟记账与压缩重置，2026-09-29 重跑 PASS。此前真实 Performance 长文分批交付后，运行时代码补丁已通过必读闸；这证明本次客户端链路可用，不代表其他客户端格式均已验证。

## MCP 测试任务被中断后，`TestRunStatus.IsRunning` 会一直挡着 `refresh_unity`
- 现象：编辑器里明明没有测试在跑（`EditorApplication.isPlaying=False`、控制台几十秒没有新日志、`Time.frameCount` 不涨），`refresh_unity` 却一直返回 `{"code":"tests_running"}`；`run_tests` 带 `clear_stuck=true` 也回「No running job to clear」。
- 根因：`RefreshUnity` 的闸门读 `MCPForUnity.Editor.Services.TestRunStatus.IsRunning`（静态标志，开跑 `MarkStarted`、收尾 `MarkFinished`）。发起测试的 MCP 客户端中途断开 / 会话被中止时收尾没走到，标志就停在 true；而 `clear_stuck` 清的是 `TestJobManager._currentJobId`，跟它不是同一个东西。
- 正确做法：先用 `execute_code` 读 `isPlaying` / 控制台确认没有真在跑；确系孤儿再反射调 `TestRunStatus.MarkFinished()`（`internal static`，程序集 `MCPForUnity.Editor`）。2026-09-30 实测：上一会话中断留下 PlayMode 标志、`staleMinutes=32.5`，清掉后 `refresh_unity` 立刻可用。
- 关联：`Library/PackageCache/com.coplaydev.unity-mcp@*/Editor/Services/TestRunStatus.cs`、`Editor/Tools/RefreshUnity.cs:28`、`Editor/Tools/RunTests.cs`（`clear_stuck`）；`.claude/skills/unity-mcp/SKILL.md` 故障排查表；2026-09-30。

## 编辑器没焦点时 Unity 不自动重编译，跑测试用的是旧程序集
- 现象：改完回放脚本直接 `run_tests` 重跑，报告里的坐标与失败项和改之前一模一样，看着像「改的代码没生效」；`Library/ScriptAssemblies/Game.Tests.Showcase.dll` 的时间戳停在改文件之前。
- 根因：Unity 只在编辑器窗口有焦点时自动刷新资产（Auto Refresh 的行为），编辑器在后台时改 `.cs` **不会**触发编译；MCP 的测试任务用当前已加载的程序集跑，不会替你编译一次。共用编辑器 / 无人值守时最容易踩。
- 正确做法：改完代码再跑测试或回放，先过编译门——`refresh_unity(mode="force", scope="scripts", compile="request")`，然后核对 `Library/ScriptAssemblies/<目标程序集>.dll` 的时间戳晚于改动时间，再 `read_console` 看有没有编译错误。2026-09-30 验 Dialogue 回放时白跑了一轮（3.5 分钟）才发现。
- 关联：`.claude/skills/verify-module/SKILL.md` 第 2 步（编译门）、`.claude/skills/unity-mcp/SKILL.md` 纪律 3；`ai-docs/pitfalls.md`「MCP 测试任务被中断后…」；2026-09-30。

## 回放截图帧在编辑器里超过 100 毫秒，会触发 `core.perf/spike` 警告，警告计数跟着涨
- 现象：Battle 回放里每做一个动作、截一张图，控制台的 Warn 计数就涨一格（2026-10-07 W2a 验收时 23 → 26），看着像战斗流程在报警。
- 根因：不是功能问题。截图那一帧在编辑器里超过 100 毫秒，`PerformanceSampler` 对单帧超过阈值的帧立刻补一条 W 级 `core.perf/spike`（阈值 `spikeThresholdMs = 100`，`TelemetryConfig.cs:36`；发出点 `PerformanceSampler.cs:107`），同一段卡顿只报一次（`inSpike` 防抖）。
- 正确做法：回放里要断言「警告数没涨」或排查「为什么多了几条 Warn」时，先看埋点时间线里新增的是不是 `core.perf/spike` 且发生在 `Snapshot` 那一步附近，是就不用查功能代码；要断言警告数时把 `core.perf/spike` 排除在外，或不要在同一个断言窗口里截图。
- 关联：`Assets/_Project/Scripts/Core/Telemetry/PerformanceSampler.cs:107`、`TelemetryConfig.cs:36`；`PRP/turnbased-battle/prp.md` §9 W2a 验收与 W2b 结论；2026-10-07。

## 叠加加载场景时，相机不能打 `MainCamera` 标签
- 现象：叠加加载一张带相机的场景（战斗场景 `BattleArena`）后再卸载，若那台相机打了 `MainCamera`，缓存 `Camera.main` 的模块手里就是一台随场景销毁的相机。
- 根因：`QuestSceneBinder` 在每次场景加载与卸载回调里都把 `Camera.main` 缓存进 `SceneCamera`（`QuestSceneBinder.cs:57,187,199`）。叠加场景里的相机一旦打了 `MainCamera`，加载回调缓存的就是它，卸载时它随场景一起销毁。
- 正确做法：叠加场景里的相机**不打 `MainCamera` 标签、不挂 `AudioListener`**，由流程在开场时关掉世界相机的 `Camera` 组件（不关物体）、打开叠加相机，收场先交还再卸载场景（`BattleCameraHandoff.cs:8-9,48,61-79`；`BattleScenePresenter.cs:183-184`）。新增一个叠加加载的场景前，先搜一遍 `Camera.main` 的缓存点（`grep -rn "Camera.main" Assets/_Project/Scripts/Runtime`）。
- 关联：`ai-docs/docs/modules/battle/battle-module-guide.md`「相机接管规则」「SceneBinder 审计结论」、`BattleStage.cs:27`；2026-10-07。

## 共用一台编辑器进 Play 跑回放，要先锁程序集重载、用完对称解锁
- 现象：多个会话共用一个 Unity 编辑器时，一个会话进 Play 跑回放，另一个会话保存了 `.cs`，编辑器在 Play 中重编译并做域重载，本次运行作废：协程被丢掉、进度卡住不报失败。
- 根因：编辑器偏好「Script Changes While Playing」默认是边玩边重编译；谁保存代码谁触发，跟当前在跑什么无关。细节与现象见本文「Showcase 回放中途别人保存 .cs，Play 内域重载把测试协程吞掉」一条。
- 正确做法：走 `ShowcaseScenario` 的回放，框架已在 SetUp 里 `LockReloadAssemblies()`、TearDown 里对称 `UnlockReloadAssemblies()`（`ShowcaseScenario.cs:220,247`，退出 Play 时兜底全部释放，`:317`）。**不走框架、自己用 MCP 进 Play 做验证时，同样要先 `EditorApplication.LockReloadAssemblies()`，结束后对称 `UnlockReloadAssemblies()`**，别忘了解锁：计数没归零，编辑器会一直认为「还有人要求不许重载」，磁盘上改好的代码编不进来（见本文关于 `LockReloadAssemblies` 计数没归零的那一条）。并行派单时约定回放期间不保存 `.cs`；跑之前先过一遍编译门。
- 关联：`Assets/_Project/Scripts/Tests/Showcase/Framework/ShowcaseScenario.cs:220,247,317`；本文「Showcase 回放中途别人保存 .cs…」「共用一台编辑器的并发会话互相干扰」；2026-10-07 回放验收。

## 回放的 `group_names` 按子串匹配，只写模块名会带出别的模块
- 现象：`run_tests(PlayMode, group_names=["World"])` 想只跑 World 回放，结果连演出回放 `PlayById_ShowsSubtitlesAndRestoresWorld`、`VillageEntrance_WorldStageTalkAndRestore` 一起跑了；后者超时没收尾，把紧接着的 World 回放也弄脏（画面里叠着标题界面，12 个检查点误报失败）。
- 根因：组名是正则，对测试全名做子串匹配；任何测试名里含 `World` 都会命中。
- 正确做法：一律写锚定正则 `^Game\.Tests\.Showcase\.<模块>\.`（EditMode 同理 `^Game\.Tests\.EditMode\.<模块>\.`），跑前看一眼返回的用例总数是否符合预期。
- 关联：`ai-docs/docs/modules/world/world-module-guide.md`「验证入口」、`PRP/interaction/prp.md` §8 第二波第 9 条；2026-10-08。

## TMP 文字在未激活的父物体下量首选宽度不准
- 现象：交互提示 HUD 按文字算胶囊宽度，第一次显示时长提示被截成「前往 · 镜中妖界长安…」，之后再显示就正常；EditMode 里直接量同一预制体却是对的。
- 根因：提示的 `Root` 在预制体里默认未激活，`Show` 先改文字、先量宽、后激活——Play 里文字组件还没初始化，`GetPreferredValues` 量出的宽度退回最小宽度。
- 正确做法：要按文字量尺寸的 UI，先 `SetActive(true)` 再量（`InteractPromptHudView.Show`）；写回放断言时检查 `TMP_Text.isTextTruncated` 与容器宽度，不只比文字内容。
- 关联：`Assets/_Project/Scripts/Runtime/Interaction/InteractPromptHudView.cs`（`Show` / `FitWidth`）、`BattleShowcase.HudPromptFitsText`；2026-10-08。

## GLTFUtility 按「字符数」读 GLB 的 JSON 块，重打包的 `.glb` 必须保持纯 ASCII
- 现象：工程里放入重打包过的 `.glb`，Unity 报 `Asset import failed ... JsonReaderException: Additional text encountered after finished reading JSON content`，位置恰好等于 JSON 应有的字符数；同一份文件在 Blender、glTF 校验器里都正常，`bufferView` 边界、对齐、网格字节数与原件逐字节一致。
- 根因：`com.siccity.gltfutility` 的 `Importer.GetGLBJson` 先从 GLB 头读出 chunk 的**字节长度**，却用 `new char[chunkLength]` + `reader.Read(jsonChars, 0, chunkLength)` 按**字符数**读（`Importer.cs:122-127`）。JSON 块里只要有一个非 ASCII 字符（哪怕只是中文材质名），字符数就小于字节数，于是它接着往 BIN 块里多读二进制，Newtonsoft 随即判定「JSON 结束后还有内容」。原始文件没事是因为 Blender 导出时把非 ASCII 全写成 `\uXXXX` 转义，字节数恰好等于字符数——这个前提是隐式的，重打包时最容易丢。
- 正确做法：要进这个工程的 `.glb`，JSON 块必须序列化成纯 ASCII（`json.dumps(..., ensure_ascii=True)`，或让导出器转义非 ASCII），并断言 `len(json_bytes) == len(json_text)`；顺带确认 `buffers[0].byteLength` 等于 BIN chunk 长度、每个 `bufferView` 起点 4 字节对齐。别拿「文件在别的工具里能打开」当通过标准，判定要落在 Unity 的导入日志上。
- 关联：`Library/PackageCache/com.siccity.gltfutility@*/Scripts/Importer.cs:96-135`、`Scripts/Editor/GLBImporter.cs`；`Assets/_Project/Art/scene/env_well/README.md`；2026-10-08 水井素材入库。

## GLTFUtility 解析了 `texCoord` 却从不使用，多套 UV 的模型只会按 UV0 采样
- 现象：同一份 `.glb`，Blender 里井壁有石砖、桶有木箍，进 Unity 后这两件各是一坨**光滑的棕色**，其余物件贴图正常；模型顶点数、贴图、材质引用都对。
- 根因：这两个网格有**两套 UV**——`UVMap` 全是 `(0,0)`（退化），`UVMap.001` 才是真 UV。Blender 的材质用 `UV Map` 节点**显式指定 `UVMap.001`**，所以正常；glTF 只能靠材质的 `texCoord` 序号选 UV 集，Blender 导出时也确实写对了 `"texCoord": 1`。但 GLTFUtility 只把 `texCoord` **解析进字段就完事**（`GLTFMaterial.cs:259` 定义后全文无引用，`KHR_texture_transform.cs:37` 是 `// TODO texCoord`），导入时永远按 UV0 采样——而在 Unity 里 `mesh.uv = TEXCOORD_0`、`mesh.uv2 = TEXCOORD_1`（`GLTFMesh.cs:287-288`），于是它采的是那套退化的 UV，整个网格只命中了贴图上的**一个像素**。
- 正确做法：**把真 UV 换到 TEXCOORD_0 的位置**——对这些 primitive 令 `attributes["TEXCOORD_0"] = attributes.pop("TEXCOORD_1")`，再把该材质所有贴图引用（baseColor / metallicRoughness / normal / occlusion / emissive）的 `texCoord` 归 0，让规范读取器和 GLTFUtility 一致。**只改 JSON 索引，不动 BIN、不动几何。** 排查时先按材质列出「贴图指向第几套 UV」与「各套 UV 的实际范围」，退化的那套特征是 u、v 分别是常数。
- 关联：`Library/PackageCache/com.siccity.gltfutility@*/Scripts/Spec/GLTFMaterial.cs:259`、`Scripts/Spec/GLTFMesh.cs:287-290`、`Scripts/Extensions/KHR_texture_transform.cs:37`；`Assets/_Project/Art/scene/env_well/README.md`；2026-10-08 水井素材入库（只有 `pierre`、`seau` 两个材质带 `texCoord: 1`，其余 32 个都是 0，所以只影响这两件）。
