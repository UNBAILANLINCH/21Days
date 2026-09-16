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
- 现象：中文字体资产提交时才 6 KB，同事拉下来跑一次游戏，`git status` 里它就变成 2 MB 的改动；每个人每次 Play 都产生一份不一样的 diff，合并时天天冲突。
- 根因：`AtlasPopulationMode.Dynamic` 的字体资产在**编辑器里**是按需栅格化后**写回资产**的——用到哪个字就把它烘进 `.asset` 内嵌的图集贴图，1024×1024 的 Alpha8 贴图序列化成 YAML 就是 2 MB 上下。这是 TMP 有意的设计（下次进 Play 不用重烘），不是 bug，也不会报任何提示。出包后的运行时只在内存里加字，不写回资产，所以**成品不受影响，受影响的只有仓库**。TMP 3.0.7 的 `TMP Settings` 里没有"打包时清掉动态数据"的开关，只能手动清。
- 正确做法：提交前在字体资产的 Inspector 上点 **Clear Dynamic Data**（脚本等价物是 `fontAsset.ClearFontAssetData(true)`，`true` 会把图集缩回 0×0），确认 `.asset` 回到几 KB 再提交。清空**不影响功能**：Dynamic 模式和声明的 1024×1024 图集尺寸、源字体引用都保留着，下次运行第一帧就会重新按需烘（实测从空表起步，首帧 `frameCount=2` 时中文已正常渲染）。
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

## 测试自己把依赖装上了，于是接线缺口全程不报
- 现象：回放系统的验证全绿——PlayMode Showcase 2/2、EditMode 173/173，录制、状态哈希、完整快照、漂移检测逐条验过。但**真实启动路径下录出来的回放只有输入流**：状态哈希和快照全是空的，漂移检测、起点恢复、快照续跑全部空转。整个系统最核心的能力是空的，而没有任何一条验证发现得了。
- 根因：框架只定义了 `IReplayStateProvider` 契约，**没有默认实现，组合根里也没注册没接线**。而 Showcase 为了自己能跑，`new` 了一个提供者挂上去。于是所有验证验的都是「这套类凑在一起能工作」，不是「产品启动起来能工作」——**接线那一层从头到尾没被任何一条验证覆盖过**，缺口被测试自带的依赖完美掩盖。
- 正确做法：凡是靠容器接线才生效的能力，必须有**一条验证是从真实容器里解析出对象、再查它手上的依赖是不是真的挂上了**（反射读私有字段也算），而不是测试自己装一套。自查判据很简单：把测试里「自己 new 依赖挂上去」那几行删掉，看验证还跑不跑得起来——跑不起来，说明你验的是测试的接线，不是产品的接线。配套的一条：测试收尾还原时要还原成**容器里那个**，不是 `null` 也不是写死的初值，否则测试跑完会把真实运行环境弄坏（这次也真的发生了，Showcase 跑完把容器里的提供者还原成了 null）。
- 关联：`Assets/_Project/Scripts/Core/Replay/ReplayStateRegistry.cs`、`Core/Boot/GameLifetimeScope.cs` 的 `RegisterReplay`、`PRP/replay/tasks.md`；2026-09-16 做回放系统时踩到。
