# 钩子（hooks）

Claude Code 在固定时机调用的小程序：编辑前查、编辑后记、收尾扫、压缩前存。
注册在 `.claude/settings.json` 的 `hooks` 段，命令里用 `$CLAUDE_PROJECT_DIR` 指路，
不写绝对路径。**改本目录任何文件之前要先读这份 README**（`required_reads.json` 里配了闸）。

## 一览

| 文件 | 事件 | matcher | 做什么 | 退出语义 |
| --- | --- | --- | --- | --- |
| `guard.js` | PreToolUse | `Edit\|Write\|MultiEdit\|NotebookEdit\|Bash\|Agent` | 拦 Unity 生成物写入（`Library/` `Temp/` `*.meta` `*.csproj` `packages-lock.json`、Luban 生成的 `Core/Config/Generated/` 与 `Data/Config/`）；`ProjectSettings/`、`Packages/manifest.json` 改为弹确认；`git commit/push` 弹确认，提交信息带 AI 署名直接拒；会丢工作区的 git 操作直接拒；Bash 里出现 `cd` / `pushd` 直接拒（带 cd 的相对路径过不了 `.env` Read deny 的静态检查，自动模式也弹确认；拒掉让调用方改绝对路径重发）；`Agent` 派单漏传 `model` 或派成 `fable` 直接拒，`fork` 弹确认（frontmatter 已声明 `model:` 的自定义 agent 免传） | 永远 exit 0，deny / ask 走 JSON `permissionDecision` |
| `required-reads.py` | PostToolUse | `Read\|Bash` | 记账：`Read` 记 `file_path`；`Bash` 从 `cat` / `head` / `tail` / `sed -n` / `less` / `type` 里解析出被读的文件（不在管道里、没有重定向、文件真实存在才算；`2>/dev/null` 不影响）。都写进 `.claude/.cache/reads/<会话>.jsonl` | 永远 exit 0，零输出 |
| `required-reads.py` | PreToolUse | `Edit\|Write\|MultiEdit` | 按 `required_reads.json` 查必读项读过没有，缺了就拒（拒绝理由里写明两条解锁路径） | 永远 exit 0，deny 走 JSON `permissionDecision` |
| `knowledge-routing.py` | PreToolUse | `Edit\|Write\|MultiEdit` | 提示该文件适用的 `.claude/rules/` 规则与模块 guide，走 `additionalContext` 注入；**同一条提示文本**每会话只注入一次 | 永远 exit 0，**从不阻断** |
| `doom-loop-detect.py` | PostToolUse | `Edit\|Write\|MultiEdit` | **连续**编辑同一文件计数（中间改过别的就清零），第 5 次首警、之后每 +3 次；`.md` 放宽到连续 8 次 | 触发时 exit 2（stderr 提醒 Agent，不撤销编辑），其余 exit 0 |
| `stop-check.py` | Stop | —（无 matcher） | 收尾扫：`.cs` 残留调试痕迹 / 新增资产缺 `.meta` / 本会话累计编辑最多的文件；同一份提醒每会话只说一次 | 永远 exit 0，**不阻断停止** |
| `precompact-save.py` | PreCompact | —（无 matcher） | 存 `git status --short` + `git diff --stat` 到 `.claude/.cache/precompact-state.txt`，并告知压缩后的 Agent 去哪读 | 永远 exit 0 |

不直接注册、但被钩子引用的两样：

| 文件 | 干什么 |
| --- | --- |
| `_hook_common.py` | 同会话提示去重 `should_emit(会话, 文本, 标签)`。`guard.js` 里有等价的 `shouldEmitOnce()`，**共用同一份账本**，格式必须一致 |
| `tests/` | 钩子自测，入口 `python .claude/hooks/tests/run.py`（L1 纯函数 + L2 子进程端到端）。`/gc` 会跑它 |

同一事件上注册的多个钩子并行跑，互相不保证顺序，所以**钩子之间不共享内存、只共享缓存文件**。

## 铁律

1. **成功静默、失败冗余。** 无事发生零输出；拦截或提醒时必须说清：哪个文件、为什么、下一步怎么做。
   只说「被拦了」的提示等于没提示——人和 Agent 都会去拆护栏而不是照做。
2. **fail-open。** 每个钩子自身异常（stdin 空、JSON 坏、配置写错、git 没装）一律 exit 0 放行。
   钩子的 bug 绝不能卡死会话。唯一例外：`required-reads.py` 在**明确判定缺必读**时才拒。
3. **UTF-8 两头都要管。** 读：`sys.stdin.buffer.read().decode("utf-8", "replace")`；
   写：`sys.stdout.reconfigure(encoding="utf-8")`、stderr 同理。
   Windows 默认 cp936，漏掉任一头，中文路径会变乱码——症状极具迷惑性：
   读过的文件照样被拦，因为账本里存的是乱码，跟配置里的中文名永远对不上。
4. **路径归一化。** 负载里 Windows 路径可能是 `X:\dir\file.cs` 形态，一律
   `replace("\\", "/")` 后转成**相对工程根**的正斜杠路径；比较时大小写不敏感。
   不要 `Path(...).resolve()` 相对路径——钩子的 cwd 不保证是工程根。
5. **工程根靠 `Path(__file__).resolve().parents[2]` 推**（本文件在 `<root>/.claude/hooks/`）。
   钩子源码里不出现绝对路径、本机用户名、邮箱。
6. **缓存统一放 `.claude/.cache/`**（已 gitignore），目录不存在就 `mkdir(parents=True)`。
   写缓存失败只能吞掉，不能因此拦人。
7. **会话隔离。** 会话标识取负载 `session_id` → 环境变量 `CLAUDE_SESSION_ID` → 当天日期（逐级退化）。
   按天是兜底，不是常态：同一天多个会话共享记账的话，「上个会话读过这个会话就不用读了」——
   而新会话上下文是空的，正是最该重读的时候。
8. **注入文案与阻断纪律另有一份规则**：`.claude/rules/hook-injection-style.md`
   （第三人称陈述、只在写操作注入、同会话同文本去重、解锁链路不可靠就不许 deny）。
   编辑本目录时它会自动注入，不用手找。
9. **判据写成纯函数并补测试。** 改完跑 `python .claude/hooks/tests/run.py`；
   验不了的钩子最后没人敢动，只能整个删掉。

### 缓存文件

| 路径 | 谁写 | 内容 |
| --- | --- | --- |
| `.claude/.cache/reads/<会话>.jsonl` | `required-reads.py` | 每行一个 JSON 数组，本会话读过的相对路径。只追加，不读改写（并发安全） |
| `.claude/.cache/emitted-<会话>.txt` | 所有注入型钩子 + `guard.js` | 每行 `<标签>:<提示文本 sha1 前 16 位>`，同会话同文本只注入一次 |
| `.claude/.cache/edit-counts-<会话>.json` | `doom-loop-detect.py` | `{相对路径: 累计编辑次数}`，外加 `__streak__: {file, n}` 存连续状态。`stop-check.py` 读同一个文件，按 `isinstance(v, int)` 过滤，天然跳过 `__streak__` |
| `.claude/.cache/precompact-state.txt` | `precompact-save.py` | 最近一次压缩前的 git 工作态快照 |

行为不对时先删整个 `.claude/.cache/` 再复现——缓存脏是最常见的假故障。

## 自测

```bash
python .claude/hooks/tests/run.py     # L1 纯函数 + L2 端到端，一条命令全跑
```

- **L1**（`tests/test_l1_units.py`）：判据算得对不对 —— Bash 读取解析、路径归一化、
  `${seg:N}` 展开、连续性计数状态机、去重。
- **L2**（`tests/test_l2_e2e.py`）：钩子被真的调起来时行为对不对 —— 子进程喂真实 stdin JSON，
  断言退出码与输出里该出现 / 不该出现什么。`guard.js` 也在里面（没装 node 时自动跳过）。
- `/gc` 会跑这个入口。加了新判据或新提示**就补一条用例**，没有用例的判据等于没加。

## 手工冒烟

自测覆盖不到、或想看**实际输出长什么样**时用。在工程根下跑（Git Bash）。
`session_id` 随便取个 `smoke`，测完删 `.claude/.cache/*smoke*` 即可。

```bash
# 0) 空 stdin：exit 0 放行。前三个应零输出；stop-check / precompact-save 不看 tool_name、
#    直接扫工作区，工作区脏的时候它们有输出是对的
for h in required-reads knowledge-routing doom-loop-detect stop-check precompact-save; do
  printf '' | python .claude/hooks/$h.py; echo "$h exit=$?"
done
```

```bash
# 1) required-reads —— PostToolUse 记账（零输出，账本多一行）
printf '{"session_id":"smoke","hook_event_name":"PostToolUse","tool_name":"Read","tool_input":{"file_path":".claude/rules/unity-assets.md"}}' \
  | python .claude/hooks/required-reads.py
cat .claude/.cache/reads/smoke.jsonl

# 2) required-reads —— PreToolUse 查闸：没读过 unity-assets.md 的会话改场景 → deny
printf '{"session_id":"fresh","hook_event_name":"PreToolUse","tool_name":"Edit","tool_input":{"file_path":"Assets/Scenes/SampleScene.unity"}}' \
  | python .claude/hooks/required-reads.py
# 换成上面记过账的 smoke 会话 → 静默放行
printf '{"session_id":"smoke","hook_event_name":"PreToolUse","tool_name":"Edit","tool_input":{"file_path":"Assets/Scenes/SampleScene.unity"}}' \
  | python .claude/hooks/required-reads.py

# 3) Windows 反斜杠绝对路径也认（把 X/工程根 换成实际盘符与目录名）
#    ⚠ Git Bash 的 printf 会吃掉一半反斜杠：要让 JSON 里出现 `\\`，源码里得写 8 个
printf '{"session_id":"smoke","hook_event_name":"PostToolUse","tool_name":"Read","tool_input":{"file_path":"X:\\\\\\\\工程根\\\\\\\\.claude\\\\\\\\rules\\\\\\\\csharp-code.md"}}' \
  | python .claude/hooks/required-reads.py
# 数不清反斜杠时改用文件：把负载存成 payload.json，再 `python .claude/hooks/x.py < payload.json`
```

```bash
# 3b) Bash 记账 —— cat 读过必读项，闸就该放行（解锁链路的实测）
printf '{"session_id":"smoke2","hook_event_name":"PostToolUse","tool_name":"Bash","tool_input":{"command":"cat .claude/hooks/README.md"}}' \
  | python .claude/hooks/required-reads.py
printf '{"session_id":"smoke2","hook_event_name":"PreToolUse","tool_name":"Edit","tool_input":{"file_path":".claude/hooks/guard.js"}}' \
  | python .claude/hooks/required-reads.py   # 应零输出（放行）
# 进了管道的 cat 不算读过整份 —— 换个会话试，仍应 deny
printf '{"session_id":"smoke3","hook_event_name":"PostToolUse","tool_name":"Bash","tool_input":{"command":"cat .claude/hooks/README.md | head -5"}}' \
  | python .claude/hooks/required-reads.py
printf '{"session_id":"smoke3","hook_event_name":"PreToolUse","tool_name":"Edit","tool_input":{"file_path":".claude/hooks/guard.js"}}' \
  | python .claude/hooks/required-reads.py   # 应 deny
```

```bash
# 4) knowledge-routing —— 首次给规则列表，同一条提示第二次静默
printf '{"session_id":"smoke","tool_name":"Edit","tool_input":{"file_path":"Assets/_Project/Scripts/Runtime/Player/Move.cs"}}' \
  | python .claude/hooks/knowledge-routing.py
printf '{"session_id":"smoke","tool_name":"Edit","tool_input":{"file_path":"Assets/_Project/Scripts/Runtime/Player/Move.cs"}}' \
  | python .claude/hooks/knowledge-routing.py   # 应零输出
```

```bash
# 5) doom-loop-detect —— 连打 5 次，第 5 次 exit 2 并在 stderr 给提醒
for i in 1 2 3 4 5; do
  printf '{"session_id":"smoke","tool_name":"Edit","tool_input":{"file_path":"Assets/_Project/Scripts/Runtime/Player/Move.cs"}}' \
    | python .claude/hooks/doom-loop-detect.py; echo "第 $i 次 exit=$?"
done
# 5b) 交替改两个文件 —— 连续性被打断，一次都不该报（跨波次回来改同一个注册入口就是这个形态）
for i in 1 2 3 4 5 6; do
  for f in Move.cs Jump.cs; do
    printf '{"session_id":"smoke4","tool_name":"Edit","tool_input":{"file_path":"Assets/_Project/Scripts/Runtime/Player/'"$f"'"}}' \
      | python .claude/hooks/doom-loop-detect.py; echo "$f exit=$?"
  done
done
```

```bash
# 6) stop-check / precompact-save —— 干净工作区应零输出
printf '{"session_id":"smoke","hook_event_name":"Stop"}' | python .claude/hooks/stop-check.py
printf '{"session_id":"smoke","hook_event_name":"PreCompact"}' | python .claude/hooks/precompact-save.py
```

```bash
# 7) guard.js —— 拦生成物
printf '{"tool_name":"Edit","tool_input":{"file_path":"Assets/Scenes/SampleScene.unity.meta"}}' \
  | node .claude/hooks/guard.js
```

```bash
# 7b) guard.js —— Bash 带 cd 直接拒（免 .env Read deny 弹窗）
printf '{"tool_name":"Bash","tool_input":{"command":"cd E:/x && head -20 a.cs"}}' \
  | node .claude/hooks/guard.js   # → deny
printf '{"tool_name":"Bash","tool_input":{"command":"head -20 E:/x/a.cs"}}' \
  | node .claude/hooks/guard.js   # → 零输出
```

```bash
# 8) guard.js —— Agent 派单校验
printf '{"tool_name":"Agent","tool_input":{"description":"x","prompt":"y"}}' \
  | node .claude/hooks/guard.js   # 缺 model → deny
printf '{"tool_name":"Agent","tool_input":{"description":"x","prompt":"y","model":"fable"}}' \
  | node .claude/hooks/guard.js   # model 传 fable → deny
printf '{"tool_name":"Agent","tool_input":{"description":"x","prompt":"y","model":"opus"}}' \
  | node .claude/hooks/guard.js   # model 传 opus → 零输出
printf '{"tool_name":"Agent","tool_input":{"description":"x","prompt":"y","subagent_type":"code-reviewer"}}' \
  | node .claude/hooks/guard.js   # subagent_type code-reviewer 不传 model → 零输出（frontmatter 已固定 sonnet）
printf '{"tool_name":"Agent","tool_input":{"description":"x","prompt":"y","subagent_type":"fork"}}' \
  | node .claude/hooks/guard.js   # subagent_type fork → ask
```

语法自检（改完必跑）：

```bash
for f in .claude/hooks/*.py; do
  python -c "import py_compile,sys;py_compile.compile(sys.argv[1],doraise=True)" "$f" && echo "ok $f"
done
node --check .claude/hooks/guard.js && echo "ok guard.js"
python -c "import json;json.load(open('.claude/hooks/required_reads.json',encoding='utf-8'))" && echo "ok required_reads.json"
python .claude/hooks/tests/run.py      # 语法过了不代表判据还对，这条才是
```

## 加一条必读

改 `.claude/hooks/required_reads.json`，**不用改代码**：

```json
"Assets/_Project/Scripts/Editor/**": [".claude/rules/csharp-code.md"]
```

- 键是 glob（相对工程根、正斜杠、大小写不敏感，`fnmatch` 语法：`*` 和 `**` 都跨目录）。
- 值里可用 `${seg:N}` / `${seg:N:lower}` 取被改文件路径的第 N 段（0 起），
  一条规则覆盖所有现有和将来的模块——逐模块手写必漏，而漏掉**不报错**，
  只是那个模块的闸从来没生效过，从外面完全看不出来。
- 必读文件**不存在就跳过这一项**：闸可以提前配好，文档没写出来也不会误拦；
  不跳过的话是死锁（永远拦、且读不到）。
- 目标文件本身就是必读文件，或文件名是 `README.md` / `SKILL.md` / `*-module-guide.md` 时豁免。
- 以 `_` 开头的键是注释，不参与匹配。
- **清单别堆太长**：每多一份，动手前的成本就高一分；它挡不住「读了没往心里去」，
  那要靠 `project-lint` 的机械闸。闸太紧等于没有闸，人会想办法绕。

## 加一个新钩子

1. 在本目录新建 `<名字>.py`，照抄现有钩子的骨架：
   `ROOT = Path(__file__).resolve().parents[2]` · `_utf8_stdio()` · `_payload()` ·
   `_session_id()` · `_rel()` · 最外层 `try/except → sys.exit(0)`。
2. 在 `.claude/settings.json` 的对应事件下注册，命令写
   `python "$CLAUDE_PROJECT_DIR/.claude/hooks/<名字>.py"`，给 `timeout`（秒）。
   PreToolUse / PostToolUse 要写 `matcher`（正则，匹配工具名）；Stop / PreCompact 不需要。
   会拦人或耗时的钩子加一句中文 `statusMessage`，让用户知道卡在哪。
3. 想影响工具调用就输出 JSON 到 stdout：
   - PreToolUse 拒绝/确认：`{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"deny|ask","permissionDecisionReason":"<中文理由>"}}`
   - 注入上下文：`{"hookSpecificOutput":{"hookEventName":"<事件>","additionalContext":"<中文>"}}`
     —— 文案照 `.claude/rules/hook-injection-style.md` 写，并过一遍
     `_hook_common.should_emit()` 去重（deny / ask 不去重）。
   - 只想提醒 Agent（PostToolUse）：写 stderr + exit 2。
4. 判据抽成纯函数，在 `tests/test_l1_units.py` 加用例；行为在 `tests/test_l2_e2e.py` 加一条端到端。
5. 按上面「手工冒烟」的形式补一组示例进本文件，并在「一览」表里加一行。
