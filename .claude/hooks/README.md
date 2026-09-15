# 钩子（hooks）

Claude Code 在固定时机调用的小程序：编辑前查、编辑后记、收尾扫、压缩前存。
注册在 `.claude/settings.json` 的 `hooks` 段，命令里用 `$CLAUDE_PROJECT_DIR` 指路，
不写绝对路径。**改本目录任何文件之前要先读这份 README**（`required_reads.json` 里配了闸）。

## 一览

| 文件 | 事件 | matcher | 做什么 | 退出语义 |
| --- | --- | --- | --- | --- |
| `guard.js` | PreToolUse | `Edit\|Write\|MultiEdit\|NotebookEdit\|Bash\|Agent` | 拦 Unity 生成物写入（`Library/` `Temp/` `*.meta` `*.csproj` `packages-lock.json`）；`ProjectSettings/`、`Packages/manifest.json` 改为弹确认；`git commit/push` 弹确认，提交信息带 AI 署名直接拒；会丢工作区的 git 操作直接拒；`Agent` 派单漏传 `model` 或派成 `fable` 直接拒，`fork` 弹确认（frontmatter 已声明 `model:` 的自定义 agent 免传） | 永远 exit 0，deny / ask 走 JSON `permissionDecision` |
| `required-reads.py` | PostToolUse | `Read` | 把读过的文件记进本会话已读账本 `.claude/.cache/reads/<会话>.jsonl` | 永远 exit 0，零输出 |
| `required-reads.py` | PreToolUse | `Edit\|Write\|MultiEdit` | 按 `required_reads.json` 查必读项读过没有，缺了就拒 | 永远 exit 0，deny 走 JSON `permissionDecision` |
| `knowledge-routing.py` | PreToolUse | `Edit\|Write\|MultiEdit` | 提示该文件适用的 `.claude/rules/` 规则与模块 guide，走 `additionalContext` 注入；同文件每会话只提一次 | 永远 exit 0，**从不阻断** |
| `doom-loop-detect.py` | PostToolUse | `Edit\|Write\|MultiEdit` | 同文件同会话编辑计数，第 5 次首警、之后每 +3 次再警 | 触发时 exit 2（stderr 提醒 Agent，不撤销编辑），其余 exit 0 |
| `stop-check.py` | Stop | —（无 matcher） | 收尾扫：`.cs` 残留调试痕迹 / 新增资产缺 `.meta` / 本会话高频编辑文件 | 永远 exit 0，**不阻断停止** |
| `precompact-save.py` | PreCompact | —（无 matcher） | 存 `git status --short` + `git diff --stat` 到 `.claude/.cache/precompact-state.txt`，并告知压缩后的 Agent 去哪读 | 永远 exit 0 |

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

### 缓存文件

| 路径 | 谁写 | 内容 |
| --- | --- | --- |
| `.claude/.cache/reads/<会话>.jsonl` | `required-reads.py` | 每行一个 JSON 数组，本会话读过的相对路径。只追加，不读改写（并发安全） |
| `.claude/.cache/routing-seen-<会话>.txt` | `knowledge-routing.py` | 每行一个已提示过的文件（小写），用于去重 |
| `.claude/.cache/edit-counts-<会话>.json` | `doom-loop-detect.py` | `{相对路径: 编辑次数}`，`stop-check.py` 也读它 |
| `.claude/.cache/precompact-state.txt` | `precompact-save.py` | 最近一次压缩前的 git 工作态快照 |

行为不对时先删整个 `.claude/.cache/` 再复现——缓存脏是最常见的假故障。

## 手工冒烟

在工程根下跑（Git Bash）。`session_id` 随便取个 `smoke`，测完删 `.claude/.cache/*smoke*` 即可。

```bash
# 0) 空 stdin：每个钩子都应零输出、exit 0
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
# 4) knowledge-routing —— 首次给规则列表，第二次同文件静默
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
python -c "import json;json.load(open('.claude/hooks/required_reads.json',encoding='utf-8'))" && echo "ok required_reads.json"
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
   - 只想提醒 Agent（PostToolUse）：写 stderr + exit 2。
4. 按上面「手工冒烟」的形式补一组示例进本文件，并在「一览」表里加一行。
