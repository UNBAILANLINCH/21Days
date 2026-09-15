// PreToolUse 钩子：拦截对 Unity 生成物的直接写入；git 提交/推送与工程级配置改动改为弹确认；
// Bash 里出现 cd / pushd 直接拒（会触发 .env Read deny 的静态检查弹窗）；
// Agent 派单漏传 model 或派成 fable 直接拒（规则出处 .claude/rules/model-routing.md）。
// 规则出处：CLAUDE.md「硬规则」。
//
// 本钩子的输出**几乎全是决策**（deny / ask），决策一律不去重：去重掉的那一次
// 就是护栏漏掉的那一次。只有挂在 ask 上的背景说明走 shouldEmitOnce()，每会话说一次。
'use strict';

const fs = require('fs');
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

let raw = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', (c) => (raw += c));
process.stdin.on('end', main);

function main() {
  let input;
  try { input = JSON.parse(raw); } catch { return; }
  const tool = input.tool_name || '';
  const ti = input.tool_input || {};
  const root = norm(input.cwd || process.env.CLAUDE_PROJECT_DIR || process.cwd());

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
    if (/\bgit\s+commit\b/.test(cmd) && /Co-Authored-By:\s*Claude|Claude-Session:|Generated with.{0,4}Claude Code|🤖/i.test(cmd)) {
      return decide('deny', '提交信息带 AI 署名或会话链接，去掉后重试（docs/commit-convention.md）');
    }
    if (/\bgit\s+(commit|push)\b/.test(cmd)) {
      // ask 本身**每次都要问**——要人点头的事去重掉一次，就是护栏漏掉一次。
      // 只有附带的那句背景说明按会话去重：它每次都一样，说第二遍起只是在烧上下文。
      const note = '本工程的提交约定：改动攒在工作区，收敛后列清单给用户逐次授权；'
        + '提交信息按 docs/commit-convention.md，不带任何 AI 署名尾注。';
      const ctx = shouldEmitOnce(sessionId(input), note, 'guard-commit') ? note : '';
      return decide('ask', 'git commit / push 需要用户逐次授权', ctx);
    }
    if (/\bgit\s+(reset\s+--hard|clean\b|checkout\s+--\s|restore\b)/.test(cmd)) return decide('deny', '会丢弃工作区改动的 git 操作，请用户手动执行');
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
