// PreToolUse 钩子：拦截对 Unity 生成物的直接写入；git 提交/推送与工程级配置改动改为弹确认。
// 规则出处：CLAUDE.md「硬规则」。
'use strict';

const DENY = [
  [/^(Library|Temp|Logs|obj|Build|Builds|UserSettings)\//i, 'Unity 生成目录，不手改'],
  [/\.meta$/i, '.meta 由 Unity 生成，移动/删除请用 git mv / git rm'],
  [/\.(csproj|sln)$/i, 'IDE 工程文件由 Unity 生成'],
  [/^Packages\/packages-lock\.json$/i, 'packages-lock 由 Unity 解析生成'],
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
    if (/\bgit\s+commit\b/.test(cmd) && /Co-Authored-By:\s*Claude|Claude-Session:|Generated with.{0,4}Claude Code|🤖/i.test(cmd)) {
      return decide('deny', '提交信息带 AI 署名或会话链接，去掉后重试（docs/commit-convention.md）');
    }
    if (/\bgit\s+(commit|push)\b/.test(cmd)) return decide('ask', 'git commit / push 需要用户逐次授权');
    if (/\bgit\s+(reset\s+--hard|clean\b|checkout\s+--\s|restore\b)/.test(cmd)) return decide('deny', '会丢弃工作区改动的 git 操作，请用户手动执行');
    if (/(^|[\s;&|])(rm|rmdir|del|Remove-Item)\b[^\n]*\b(Assets|ProjectSettings|Packages|\.git)\b/i.test(cmd)) return decide('ask', '删除工程目录内容，需确认');
  }
}

function norm(p) { return String(p).replace(/\\/g, '/').replace(/\/{2,}/g, '/').replace(/\/+$/, ''); }
function toRel(p, root) {
  p = norm(p);
  if (p.toLowerCase().startsWith(root.toLowerCase() + '/')) p = p.slice(root.length + 1);
  return p.replace(/^\.\//, '');
}
function decide(permissionDecision, permissionDecisionReason) {
  // 不调用 process.exit：Windows 管道下 stdout 写入是异步的，提前退出会截断输出。
  process.stdout.write(JSON.stringify({
    hookSpecificOutput: { hookEventName: 'PreToolUse', permissionDecision, permissionDecisionReason },
  }) + '\n');
}
