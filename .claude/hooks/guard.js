// PreToolUse 钩子：拦截对 Unity 生成物的直接写入；git 提交/推送与工程级配置改动改为弹确认；
// Bash 里出现 cd / pushd 直接拒（会触发 .env Read deny 的静态检查弹窗）；
// Agent 派单漏传 model 或派成 fable 直接拒（规则出处 .claude/rules/model-routing.md）；
// Unity MCP run_tests 跑 PlayMode 回放没限定到单个模块直接拒（同一规则文件「测试范围」那条）。
// 规则出处：CLAUDE.md「硬规则」。
//
// run_tests 分支 —— 执行载体：settings.json PreToolUse matcher 里的 mcp__UnityMCP__run_tests（子代理的调用也过这里）；
// 状态锚点：`python .claude/hooks/tests/run.py` 里 GuardRunTestsScope 全绿；
// 退场条件：回放不再占用共用编辑器（例如挪到独立 CI 机跑）时，连同用例一起删掉这个分支。
//
// Bash 里的 git 判据（提交授权 / 署名 / 丢弃改动）按子命令判，先跳过 `-C <路径>`、`-c <键=值>` 等全局选项
// （gitSubcommands）：本工程 Bash 不写 cd、改写 `git -C`，整串正则认不出这种写法。
// `commit -F <文件>` 的信息文件也读前 64 KB 查署名（signedMessageFile），读不到就落回 ask。
// 状态锚点：run.py 里 GuardGitGlobalOptions / GuardCommitMessageFile / GitSubcommandParse / GitMessageFile 全绿。
//
// 本钩子的输出**几乎全是决策**（deny / ask），决策一律不去重：去重掉的那一次
// 就是护栏漏掉的那一次。只有挂在 ask 上的背景说明走 shouldEmitOnce()，每会话说一次。
'use strict';

const fs = require('fs');
const os = require('os');
const path = require('path');
const crypto = require('crypto');

const DENY = [
  [/^(Library|Temp|Logs|obj|Build|Builds|UserSettings)\//i, 'Unity 生成目录，不手改'],
  [/\.meta$/i, '.meta 由 Unity 生成，移动/删除请用 git mv / git rm'],
  [/\.(csproj|sln)$/i, 'IDE 工程文件由 Unity 生成'],
  [/^Packages\/packages-lock\.json$/i, 'packages-lock 由 Unity 解析生成'],
  [/^Assets\/_Project\/Scripts\/Core\/Config\/Generated\//i, 'Luban 生成的配置表代码，改 Tables/ 下的 Excel 再跑 scripts/gen-tables.ps1'],
  [/^Assets\/_Project\/Data\/Config\//i, 'Luban 生成的配置表数据，改 Tables/ 下的 Excel 再跑 scripts/gen-tables.ps1'],
];
const ASK = [
  [/^ProjectSettings\//i, '工程设置改动，需确认'],
  [/^Packages\/manifest\.json$/i, '依赖包改动，需确认'],
];

// 回放程序集与模块目录：模块清单每次现扫，不维护名单（harness-authoring.md「不建伴生跟踪文件」）
const SHOWCASE_NS = 'Game.Tests.Showcase';
const SHOWCASE_DIR = ['Assets', '_Project', 'Scripts', 'Tests', 'Showcase'];
const RUN_TESTS_TOOL = /^mcp__UnityMCP__run_tests$/;

// 被 require 时（钩子自测的 L1）只导出纯函数，不挂 stdin
if (require.main === module) {
  let raw = '';
  process.stdin.setEncoding('utf8');
  process.stdin.on('data', (c) => (raw += c));
  process.stdin.on('end', () => main(raw));
}
module.exports = { listArg, showcaseModules, modulesMatchedBy, runTestsVerdict,
  gitSubcommands, gitVerdict, commitMessageFiles, resolveGitPath };

function main(raw) {
  let input;
  try { input = JSON.parse(raw); } catch { return; }
  const tool = input.tool_name || '';
  const ti = input.tool_input || {};
  const root = norm(input.cwd || process.env.CLAUDE_PROJECT_DIR || process.cwd());

  if (RUN_TESTS_TOOL.test(tool)) {
    let why = null;
    try { why = runTestsVerdict(ti, showcaseModules()); } catch { return; }   // 判据自身出错 → fail-open
    if (why) return decide('deny', why);
    return;
  }

  if (['Edit', 'Write', 'MultiEdit', 'NotebookEdit'].includes(tool)) {
    const rel = toRel(ti.file_path || ti.notebook_path || '', root);
    for (const [re, why] of DENY) if (re.test(rel)) return decide('deny', `${why}：${rel}`);
    for (const [re, why] of ASK) if (re.test(rel)) return decide('ask', `${why}：${rel}`);
    return;
  }

  if (tool === 'Bash') {
    const cmd = String(ti.command || '');
    // 带 cd 的复合命令里若有相对路径读取，权限检查算不出它落在哪、无法排除 .env 的 Read deny，
    // 自动模式也会弹确认（CLAUDE.md「Bash 不写 cd」）。这里直接拒，让调用方改成绝对路径重发，弹窗就不会出现。
    if (/(^|[;&|(\n]\s*)(cd|pushd)(\s|$)/.test(cmd)) {
      return decide('deny', 'Bash 不写 cd / pushd：路径写绝对路径或相对工程根（CLAUDE.md「验证与工具」）。带 cd 的相对路径过不了 .env Read deny 的静态检查，会弹确认');
    }
    const g = gitVerdict(cmd, input.cwd);   // 自带 try/catch：解析或读文件出错都返回 null / 落回 ask，走 fail-open
    if (g && g.commitNote) {
      // ask 本身**每次都要问**——要人点头的事去重掉一次，就是护栏漏掉一次。
      // 只有附带的那句背景说明按会话去重：它每次都一样，说第二遍起只是在烧上下文。
      const note = '本工程的提交约定：改动攒在工作区，收敛后列清单给用户逐次授权；'
        + '提交信息按 docs/commit-convention.md，不带任何 AI 署名尾注。';
      const ctx = shouldEmitOnce(sessionId(input), note, 'guard-commit') ? note : '';
      return decide(g.decision, g.reason, ctx);
    }
    if (g) return decide(g.decision, g.reason);
    if (/(^|[\s;&|])(rm|rmdir|del|Remove-Item)\b[^\n]*\b(Assets|ProjectSettings|Packages|\.git)\b/i.test(cmd)) return decide('ask', '删除工程目录内容，需确认');
  }

  if (tool === 'Agent') {
    const st = String(ti.subagent_type || '').trim();
    const model = String(ti.model || '').trim().toLowerCase();

    // a) fork 永远跑在主窗口模型上（fable），model 参数会被忽略 → ask
    if (st === 'fork') return decide('ask', 'fork 子代理继承主窗口模型（fable），违反 .claude/rules/model-routing.md「subagent 永不派成 fable」；确属有意再放行，否则改派 general-purpose 并显式传 model: opus / sonnet');

    // b) 显式派成 fable → deny
    if (model === 'fable') return decide('deny', 'subagent 不得派成 fable（.claude/rules/model-routing.md）：工程任务改 model: opus，机械活改 model: sonnet；需要更高层判断时回主窗口验收，不升级 subagent');

    // c) 没传 model：若 subagent_type 对应 .claude/agents/<st>.md 且其 frontmatter 声明了 model: → 放行；否则 deny
    if (!model) {
      const fm = agentFrontmatterModel(st);   // 读不到文件 / 没有 frontmatter / 没有 model 行 → 返回 ''
      if (fm === 'fable') return decide('deny', `.claude/agents/${st}.md 的 frontmatter 把 model 定成了 fable，违反 model-routing.md，改成 sonnet 或 opus`);
      if (fm) return;   // frontmatter 已固定，放行
      return decide('deny', `派单未显式传 model（.claude/rules/model-routing.md 硬规则 1）：工程任务（跨文件实现、改接口、根因不明的调试）传 model: opus，机械活（单点执行、批量修改、检索摘要）传 model: sonnet${st ? `；或在 .claude/agents/${st}.md 的 frontmatter 里声明 model:` : ''}`);
    }
    // 其余（opus / sonnet / haiku 等）静默放行
  }
}

function norm(p) { return String(p).replace(/\\/g, '/').replace(/\/{2,}/g, '/').replace(/\/+$/, ''); }
function toRel(p, root) {
  p = norm(p);
  if (p.toLowerCase().startsWith(root.toLowerCase() + '/')) p = p.slice(root.length + 1);
  return p.replace(/^\.\//, '');
}
function decide(permissionDecision, permissionDecisionReason, additionalContext) {
  // 不调用 process.exit：Windows 管道下 stdout 写入是异步的，提前退出会截断输出。
  const out = { hookEventName: 'PreToolUse', permissionDecision, permissionDecisionReason };
  if (additionalContext) out.additionalContext = additionalContext;
  process.stdout.write(JSON.stringify({ hookSpecificOutput: out }) + '\n');
}

function sessionId(input) {
  let sid = String((input && input.session_id) || process.env.CLAUDE_SESSION_ID || '').trim();
  if (!sid) sid = new Date().toISOString().slice(0, 10).replace(/-/g, '');  // 兜底：按天
  return sid.replace(/[^\w.\-]/g, '_').slice(0, 100) || 'unknown';
}

// 同会话提示去重，`_hook_common.py` 的 JS 版：**共用同一份账本**
// `.claude/.cache/emitted-<会话>.txt`，行格式 `<标签>:<sha1 前 16 位>`，两边算法必须一致。
// key 是提示文本本身而不是文件名 —— 同一个文件的另一条提示该说，同一条提示换个文件不该重说。
// 只给建议型文案用；deny / ask 的决策本身永远不去重。
function shouldEmitOnce(sid, text, tag) {
  const hash = crypto.createHash('sha1').update(String(text), 'utf8').digest('hex').slice(0, 16);
  const key = `${tag || '-'}:${hash}`;
  const fp = path.resolve(__dirname, '..', '.cache', `emitted-${sid}.txt`);
  try {
    if (fs.existsSync(fp) && fs.readFileSync(fp, 'utf8').split(/\r?\n/).includes(key)) return false;
  } catch { /* 读不了就当没说过：宁可多说一遍，也不能让提示悄悄消失 */ }
  try {
    fs.mkdirSync(path.dirname(fp), { recursive: true });
    fs.appendFileSync(fp, key + '\n', 'utf8');
  } catch { /* 记不下就下次再说一遍，不该因此吞掉这一次 */ }
  return true;
}

// ── Bash 里的 git 调用（规则出处 CLAUDE.md「硬规则」、docs/commit-convention.md）──────────
// 旧判据是 `\bgit\s+commit\b` 这类整串正则，子命令前一带全局选项（`git -C <路径> commit`）就全部失配，
// 提交授权、署名拦截、丢弃改动三道检查一起失效（2026-09-30 有两次带署名的提交因此没拦下）。
//
// 口径沿用旧正则：命令文本**任何位置**出现的 git 都算，引号、$( )、反引号、heredoc 正文里的
// 也当一段命令文本再识别一遍。这样 `bash -c "git …"`、`powershell -Command "git …"` 不用逐个列举；
// 代价是 `echo "git commit"` 这类「提到」也算调用 —— 与旧判据一致，commit / push 多问一次。

// 带值的全局选项（值可能带引号、含空格；`--git-dir=…` 这种等号写法走下面的通用分支）
const GIT_VALUE_OPTS = new Set(['-C', '-c', '--git-dir', '--work-tree', '--namespace',
  '--config-env', '--super-prefix', '--attr-source']);
const GIT_MAX_DEPTH = 4;     // 引号 / $( ) / heredoc 的递归层数上限
const GIT_MAX_TEXTS = 200;   // 一条命令最多识别多少段文本，防病态输入拖慢钩子
const AI_SIGNATURE = /Co-Authored-By:\s*Claude|Claude-Session:|Generated with.{0,4}Claude Code|🤖/i;

// 返回命令里每个 git 调用：[{ sub: 小写子命令, args: 其后的参数词（已去引号）, dirs: 依次出现的 -C 值 }]。
// 跳过的全局选项：GIT_VALUE_OPTS 里的连同其值；其余以 - 开头的词（--no-pager / -P / -p / --bare /
// --literal-pathspecs / --git-dir=… 等）当无值开关。`-c alias.<名>=<值>` 定义的别名解析回真实子命令。
function gitSubcommands(cmd) {
  const calls = [];
  const queue = [[String(cmd == null ? '' : cmd), 0]];
  for (let n = 0; queue.length && n < GIT_MAX_TEXTS; n++) {
    const [text, depth] = queue.shift();
    if (!/git/i.test(text)) continue;
    const nested = [];
    for (const words of shellCommands(text, nested)) collectGitCalls(words, calls, nested);
    if (depth < GIT_MAX_DEPTH) for (const t of nested) queue.push([t, depth + 1]);
  }
  return calls;
}

function isGitWord(w) {
  return /^git(\.exe)?$/i.test(String(w).replace(/\\/g, '/').split('/').pop());
}

function collectGitCalls(words, calls, nested) {
  for (let k = 0; k < words.length; k++) {
    if (!isGitWord(words[k])) continue;
    const aliases = {};
    const dirs = [];
    let j = k + 1;
    while (j < words.length && words[j].startsWith('-')) {
      if (!GIT_VALUE_OPTS.has(words[j])) { j += 1; continue; }
      if (words[j] === '-c' && j + 1 < words.length) noteAlias(words[j + 1], aliases, nested);
      if (words[j] === '-C' && j + 1 < words.length) dirs.push(words[j + 1]);
      j += 2;
    }
    if (j >= words.length) continue;
    let sub = words[j].toLowerCase();
    if (Object.prototype.hasOwnProperty.call(aliases, sub)) sub = aliases[sub];
    if (!sub) continue;
    calls.push({ sub, args: words.slice(j + 1), dirs });
    k = j;   // 选项值与子命令不再当 git 词重扫；参数里若还有 git 照样识别
  }
}

// `-c alias.ci=commit` → ci 视作 commit；`-c 'alias.x=!git commit …'` 是外壳别名，值当一段命令文本再识别
function noteAlias(kv, aliases, nested) {
  const m = /^alias\.([^=]+)=([\s\S]*)$/i.exec(kv);
  if (!m) return;
  const body = m[2].trim();
  if (body.startsWith('!')) nested.push(body.slice(1));
  else if (body) aliases[m[1].toLowerCase()] = body.split(/\s+/)[0].toLowerCase();
}

// 把一段 shell 文本切成简单命令的词表（词已去引号），遇 ; & | ( ) 换行断开。
// 引号内容、$( ) 与反引号内部、heredoc 正文另存进 nested，由 gitSubcommands 当独立文本再识别。
// 只求够用且不抛：不认识的语法按普通字符处理，引号 / 括号没闭合就吃到结尾。
function shellCommands(s, nested) {
  const cmds = [];
  const heredocs = [];   // 本行登记的 heredoc，换行后按顺序读正文
  let words = [];
  let cur = null;        // 正在拼的词；null 表示处在词间空白
  const add = (t) => { cur = (cur === null ? '' : cur) + t; };
  const endWord = () => { if (cur !== null) { words.push(cur); cur = null; } };
  const endCmd = () => { endWord(); if (words.length) cmds.push(words); words = []; };
  let i = 0;
  while (i < s.length) {
    const ch = s[i];
    if (ch === '\\') {
      if (s[i + 1] === '\n') { i += 2; continue; }                          // 续行
      if (s[i + 1] === '\r' && s[i + 2] === '\n') { i += 3; continue; }
      if (i + 1 < s.length) add(s[i + 1]);
      i += 2;
      continue;
    }
    if (ch === "'") {
      let e = s.indexOf("'", i + 1);
      if (e < 0) e = s.length;
      const body = s.slice(i + 1, e);
      nested.push(body); add(body); i = e + 1;
      continue;
    }
    if (ch === '"') {
      let j = i + 1, body = '';
      while (j < s.length && s[j] !== '"') {
        if (s[j] === '\\' && j + 1 < s.length && '"\\$`\n'.includes(s[j + 1])) {
          if (s[j + 1] !== '\n') body += s[j + 1];
          j += 2;
        } else body += s[j++];
      }
      nested.push(body); add(body); i = j + 1;
      continue;
    }
    if (ch === '`') {
      let e = s.indexOf('`', i + 1);
      if (e < 0) e = s.length;
      const body = s.slice(i + 1, e);
      nested.push(body); add('`' + body + '`'); i = e + 1;
      continue;
    }
    if (ch === '$' && s[i + 1] === '(') {
      const e = closingParen(s, i + 1);
      const body = s.slice(i + 2, e);
      nested.push(body); add('$(' + body + ')'); i = e + 1;
      continue;
    }
    if (ch === '<' && s.startsWith('<<<', i)) { endWord(); i += 3; continue; }   // here-string：后面那个词照常成词
    if (ch === '<' && s[i + 1] === '<') {                                        // heredoc：登记结束符
      endWord();
      i += 2;
      let tabs = false;
      if (s[i] === '-') { tabs = true; i++; }
      while (s[i] === ' ' || s[i] === '\t') i++;
      let delim = '';
      while (i < s.length && !/[\s;&|()<>]/.test(s[i])) {
        if (s[i] === "'" || s[i] === '"') {
          let e = s.indexOf(s[i], i + 1);
          if (e < 0) e = s.length;
          delim += s.slice(i + 1, e); i = e + 1;
        } else if (s[i] === '\\') { delim += s[i + 1] || ''; i += 2; }
        else delim += s[i++];
      }
      if (delim) heredocs.push({ delim, tabs });
      continue;
    }
    if (ch === '\n') {
      endCmd();
      i++;
      while (heredocs.length) {   // 正文读到单独一行的结束符为止；找不到就吃到结尾
        const { delim, tabs } = heredocs.shift();
        const lines = [];
        while (i < s.length) {
          let nl = s.indexOf('\n', i);
          if (nl < 0) nl = s.length;
          const line = s.slice(i, nl);
          i = nl + 1;
          const bare = line.replace(/\r$/, '');
          if ((tabs ? bare.replace(/^\t+/, '') : bare) === delim) break;
          lines.push(line);
        }
        nested.push(lines.join('\n'));
      }
      continue;
    }
    if (ch === ';' || ch === '|' || ch === '(' || ch === ')') { endCmd(); i++; continue; }
    if (ch === '&') {
      if (s[i + 1] === '>') { endWord(); i += 2; if (s[i] === '>') i++; continue; }   // &> / &>> 重定向
      endCmd(); i++;
      continue;
    }
    if (ch === '<' || ch === '>') {   // 重定向符号不成词（>& >| >> 一并吞掉），目标文件照常成词
      endWord(); i++;
      while (s[i] === '>' || s[i] === '&' || s[i] === '|') i++;
      continue;
    }
    if (ch === ' ' || ch === '\t' || ch === '\r') { endWord(); i++; continue; }
    add(ch); i++;
  }
  endCmd();
  return cmds;
}

// s[open] 是 `(`，返回配对 `)` 的下标；跳过引号内的括号；没配上返回 s.length
function closingParen(s, open) {
  let depth = 0;
  for (let j = open; j < s.length; j++) {
    const c = s[j];
    if (c === '\\') { j++; continue; }
    if (c === "'") {
      const e = s.indexOf("'", j + 1);
      if (e < 0) return s.length;
      j = e;
      continue;
    }
    if (c === '"') {
      let e = j + 1;
      while (e < s.length && s[e] !== '"') e += s[e] === '\\' ? 2 : 1;
      if (e >= s.length) return s.length;
      j = e;
      continue;
    }
    if (c === '(') depth++;
    else if (c === ')' && --depth === 0) return j;
  }
  return s.length;
}

// 会丢弃工作区改动的调用（口径同旧判据：reset --hard / clean / checkout -- / restore）
function discardsWorktree(c) {
  if (c.sub === 'reset') return c.args.includes('--hard');
  if (c.sub === 'checkout') return c.args.includes('--');
  return c.sub === 'clean' || c.sub === 'restore';
}

// ── 提交信息文件（`git commit -F <文件>`，commit-convention 推荐的写法）──────────────
// 信息在文件里、不在命令文本里，整串查署名看不到它，只能读文件。
// 读不到 / 不是普通文件 / 编码坏 → 当没读到（fail-open，落回 ask）；超过 64 KB 只看前 64 KB。
const MSG_FILE_LIMIT = 64 * 1024;
// commit 里吃一个值的选项：值要跳过，免得 `-m "-F x"` 里的说明文字被当成 -F
const COMMIT_LONG_WITH_VALUE = new Set(['--message', '--reuse-message', '--reedit-message', '--template',
  '--author', '--date', '--fixup', '--squash', '--cleanup', '--trailer', '--pathspec-from-file']);

// commit 参数里 -F / --file 给出的文件：-F <路径>、-F<路径>、-aF <路径>（捆绑短选项）、--file <路径>、
// --file=<路径>（git 认唯一前缀，--fil 同理）。`-` 是标准输入，跳过；遇 `--` 停，其后是路径规格。
function commitMessageFiles(args) {
  const files = [];
  const a = Array.isArray(args) ? args.map(String) : [];
  for (let i = 0; i < a.length; i++) {
    const w = a[i];
    if (w === '--') break;
    const long = /^--fil(?:e)?(?:=([\s\S]*))?$/.exec(w);
    if (long) {
      const v = long[1] !== undefined ? long[1] : a[++i];
      if (v !== undefined) files.push(v);
      continue;
    }
    if (COMMIT_LONG_WITH_VALUE.has(w)) { i++; continue; }
    if (!/^-[^-]/.test(w)) continue;
    for (let k = 1; k < w.length; k++) {   // 短选项逐字看：F 取本词剩余或下一个词；m c C t 的值同理，要跳过
      const ch = w[k];
      if (ch === 'F') {
        const v = w.slice(k + 1) || a[++i];
        if (v !== undefined) files.push(v);
        break;
      }
      if ('mcCt'.includes(ch)) { if (k === w.length - 1) i++; break; }
      if ('Su'.includes(ch)) break;        // 可选值只能贴着写，本词剩余就是值
    }
  }
  return files.filter((f) => f && f !== '-');
}

// Git Bash 的 /d/x 形态 → D:/x；~ → 家目录。其余原样交给 path.resolve
function hostPath(p) {
  let s = String(p);
  if (s === '~' || s.startsWith('~/')) s = os.homedir() + s.slice(1);
  if (process.platform === 'win32') {
    const m = /^\/([a-zA-Z])(\/|$)/.exec(s);
    if (m) s = m[1].toUpperCase() + ':/' + s.slice(3);
  }
  return s;
}

// 按 git -C 语义解析路径：多个 -C 依次叠加（后一个相对路径接在前一个上，空串不改目录），再解析 p
function resolveGitPath(p, dirs, base) {
  let cur = path.resolve(hostPath(base));
  for (const d of dirs || []) if (d) cur = path.resolve(cur, hostPath(d));
  return path.resolve(cur, hostPath(p));
}

// 读文件前 MSG_FILE_LIMIT 字节按 UTF-8 解码；任何失败返回 null
function readMessageHead(file) {
  let fd;
  try {
    if (!fs.statSync(file).isFile()) return null;   // FIFO、设备、目录一律不读，读了可能卡住
    fd = fs.openSync(file, 'r');
    const buf = Buffer.alloc(MSG_FILE_LIMIT);
    const n = fs.readSync(fd, buf, 0, MSG_FILE_LIMIT, 0);
    // 截断处可能切在多字节字符中间：stream 模式把残尾留着不报错，真正的坏编码照样抛
    return new TextDecoder('utf-8', { fatal: true }).decode(buf.subarray(0, n), { stream: n === MSG_FILE_LIMIT });
  } catch {
    return null;
  } finally {
    if (fd !== undefined) { try { fs.closeSync(fd); } catch { /* 关不上也不影响决策 */ } }
  }
}

// 这次 commit 的 -F 文件里带署名就返回该文件名（命令里写的原样），否则 null。自身出错也返回 null（fail-open）
function signedMessageFile(call, cwd) {
  try {
    const base = cwd || path.resolve(__dirname, '..', '..');   // 钩子输入没给 cwd 就按工程根
    for (const f of commitMessageFiles(call.args)) {
      const text = readMessageHead(resolveGitPath(f, call.dirs, base));
      if (text !== null && AI_SIGNATURE.test(text)) return f;
    }
  } catch { /* 见上 */ }
  return null;
}

// Bash 命令的 git 决策。放行返回 null，否则 { decision, reason, commitNote? }。cwd 取钩子输入里的 cwd。
// 顺序沿用旧判据：署名 deny → commit / push ask → 丢弃改动 deny。含 commit 的命令先落到 ask，
// 提交信息里提到 `git reset --hard` 不会被误拒，人点头前也看得到整条命令。
// 署名按整条命令文本查（heredoc 与 -m 都在里面），再读 -F / --file 给的文件查，前提是识别出了 commit 子命令。
function gitVerdict(cmd, cwd) {
  let calls;
  try { calls = gitSubcommands(cmd); } catch { return null; }   // 解析自身出错 → fail-open
  const has = (sub) => calls.some((c) => c.sub === sub);
  if (has('commit') && AI_SIGNATURE.test(String(cmd))) {
    return { decision: 'deny', reason: '提交信息带 AI 署名或会话链接，去掉后重试（docs/commit-convention.md）' };
  }
  for (const c of calls) {
    const f = c.sub === 'commit' ? signedMessageFile(c, cwd) : null;
    if (f) return { decision: 'deny', reason: `提交信息文件 ${f} 里带 AI 署名或会话链接，从文件里删掉后重试（docs/commit-convention.md）` };
  }
  if (has('commit') || has('push')) {
    return { decision: 'ask', reason: 'git commit / push 需要用户逐次授权', commitNote: true };
  }
  const bad = calls.find(discardsWorktree);
  if (bad) {
    const shown = `git ${bad.sub} ${bad.args.join(' ')}`.trim();
    const label = shown.length > 60 ? shown.slice(0, 60) + '…' : shown;
    return { decision: 'deny', reason: `会丢弃工作区改动的 git 操作（${label}），由用户手动执行` };
  }
  return null;
}

// ── run_tests 回放范围（规则出处 .claude/rules/model-routing.md 硬规则 6）──────────────
// 以下都是纯函数（showcaseModules 只读目录），L1 用例经 require 直接调。

// MCP 参数可能是字符串、数组、或 JSON 字符串形式的数组；空串 / 空数组都当没给。
function listArg(v) {
  if (v == null) return [];
  if (Array.isArray(v)) return v.map((x) => String(x == null ? '' : x).trim()).filter(Boolean);
  if (typeof v !== 'string') return [];
  const s = v.trim();
  if (!s) return [];
  if (s.startsWith('[')) {
    try { const a = JSON.parse(s); if (Array.isArray(a)) return listArg(a); } catch { /* 见下 */ }
    // 正则里的 `\.` 不是合法 JSON 转义，`["^A\.B\."]` 这种字符串形式的数组 JSON.parse 必失败：
    // 退化成抽引号片段，只还原 `\"` 与 `\\`，其余反斜杠原样留着（正则要用）。
    const quoted = s.match(/"((?:[^"\\]|\\.)*)"/g);
    if (quoted) {
      return quoted
        .map((q) => q.slice(1, -1).replace(/\\(.)/g, (m, c) => (c === '"' || c === '\\' ? c : m)))
        .map((x) => x.trim())
        .filter(Boolean);
    }
  }
  return [s];
}

// 现扫 Showcase/ 下的子目录名当回放模块清单；读不到返回 null（调用方 fail-open）。
function showcaseModules(root) {
  try {
    const dir = path.join(root || path.resolve(__dirname, '..', '..'), ...SHOWCASE_DIR);
    return fs.readdirSync(dir, { withFileTypes: true }).filter((d) => d.isDirectory()).map((d) => d.name);
  } catch {
    return null;
  }
}

// 一条 group_names 正则会命中哪些回放模块。编译失败返回 null（fail-open）。
// 候选全名按 Unity 测试树构造：命名空间节点 / 类 / 用例。命中公共祖先节点
// （Game、Game.Tests、Game.Tests.Showcase、程序集名）等于整套都跑，算全部模块。
function modulesMatchedBy(src, modules) {
  let re;
  try {
    let s = String(src);
    let flags = '';
    if (s.startsWith('(?i)')) { s = s.slice(4); flags = 'i'; }   // .NET 内联忽略大小写，JS 不认
    re = new RegExp(s, flags);
  } catch {
    return null;
  }
  const P = SHOWCASE_NS;
  if (['Game', 'Game.Tests', P, `${P}.dll`].some((n) => re.test(n))) return modules.slice();
  return modules.filter((m) => [`${P}.${m}`, `${P}.${m}.${m}Showcase`, `${P}.${m}.${m}Showcase.X`].some((n) => re.test(n)));
}

// 放行返回 null，拒绝返回中文理由。modules 为 null / 空（目录读不到）时 fail-open。
function runTestsVerdict(ti, modules) {
  ti = ti || {};
  if (String(ti.mode || 'EditMode').trim().toLowerCase() !== 'playmode') return null;   // EditMode 快，全量合理
  if (ti.clear_stuck === true || String(ti.clear_stuck).toLowerCase() === 'true') return null;   // 只清孤儿任务，不开跑
  if (listArg(ti.test_names).length) return null;   // 点名用例
  const asm = listArg(ti.assembly_names);
  // 程序集过滤与分组过滤是「与」关系：不含回放程序集，就跑不到回放，分组写多宽都无所谓
  if (asm.length && !asm.some((a) => a.toLowerCase().includes(SHOWCASE_NS.toLowerCase()))) return null;
  const fix = '可行的改法：group_names 每条只匹配一个受本次改动影响的模块（^Game\\\\.Tests\\\\.Showcase\\\\.<Module>\\\\.），'
    + '或用 test_names 点名用例；只跑非回放的 PlayMode 测试就传 assembly_names=["Game.Tests.PlayMode"]。'
    + '确需全量回归，逐条列出模块，并在汇报里写明理由（.claude/rules/model-routing.md 硬规则 6）';
  const groups = listArg(ti.group_names);
  if (!groups.length) {
    const scope = asm.length ? 'assembly_names 含 Game.Tests.Showcase，又没给 group_names / test_names' : '没给 assembly_names / group_names / test_names';
    return `run_tests PlayMode ${scope}，会把整套回放跑完：要十几分钟，还占着共用编辑器。${fix}`;
  }
  if (!Array.isArray(modules) || !modules.length) return null;
  for (const g of groups) {
    const hit = modulesMatchedBy(g, modules);
    if (hit && hit.length >= 2) {
      const names = hit.slice(0, 5).join('、') + (hit.length > 5 ? ' 等' : '');
      return `run_tests PlayMode 的 group_names「${g}」同时匹配 ${hit.length} 个回放模块（${names}），等于全跑：要十几分钟，还占着共用编辑器。${fix}`;
    }
  }
  return null;
}

// 读 .claude/agents/<name>.md 的 frontmatter，取 model 字段（小写）。
// 读不到 / 没有 frontmatter / 没有 model 行一律返回 ''（fail-open，见 hooks/README.md 铁律 2）。
function agentFrontmatterModel(name) {
  try {
    if (!name || /[\\/]/.test(name) || name.includes('..')) return '';
    const root = path.resolve(__dirname, '..', '..');
    const file = path.join(root, '.claude', 'agents', `${name}.md`);
    if (!fs.existsSync(file)) return '';
    const text = fs.readFileSync(file, 'utf8');
    const fmMatch = /^---\r?\n([\s\S]*?)\r?\n---/.exec(text);
    if (!fmMatch) return '';
    const modelMatch = /^\s*model\s*:\s*([A-Za-z0-9_-]+)/m.exec(fmMatch[1]);
    if (!modelMatch) return '';
    return modelMatch[1].toLowerCase();
  } catch {
    return '';
  }
}
