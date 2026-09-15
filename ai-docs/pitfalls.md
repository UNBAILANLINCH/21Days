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
