# Codex 项目 hooks

注册文件：[hooks.json](../hooks.json)。适配器：[adapter.py](adapter.py)，Windows 启动器：[run.ps1](run.ps1)。
原规则及检查脚本保留在 `.claude/`；没有复制规则库，也未修改 Claude 的注册配置。

## 启用

从仓库打开 Codex 后输入 `/hooks`，审查并信任本项目六个事件定义。未信任时不会执行。
已通过本机 `hooks/list` 检查解析：六项均 enabled，初始 trustStatus 为 untrusted。
首次信任后重新开启会话，SessionStart 应提示“项目 hooks 已运行”。定义变更后按客户端提示重新审查。

## 覆盖

| 时机 | 自动动作 |
| --- | --- |
| PreToolUse / apply_patch | 归一所有新增、修改、删除与移动目标；复用 guard、必读闸、规则提示 |
| PreToolUse / Bash | 复用现有危险 Git、提交推送与删除命令检查 |
| PostToolUse / Bash | 独立完整读取成功且结果包含整份文件时记账 |
| PostToolUse / apply_patch | 成功后计数；第 5 次及随后每 3 次提醒；对现存 C# 运行原 lint |
| Stop | 原调试残留、缺失 meta、高频编辑提醒；不阻止结束 |
| PreCompact | 保存原 git status / diff stat 工作态快照 |
| PostCompact / SessionStart | 恢复可用快照；压缩或清除上下文后重置已读账本 |

必读文档使用如下独立命令（路径相对当前命令工作目录）：

```powershell
Get-Content -Raw -Encoding UTF8 -LiteralPath '.claude/rules/unity-assets.md'
```

合并命令、管道、部分读取、失败或截断输出不记账；被拦后按提示单独重读。
文件必须完整出现在工具结果中才记账。这是读取行为检查，不能证明模型理解了内容。

Codex 0.158 的 PostToolUse 字符串摘要实测会在约 10000 token 截断，即使命令返回了完整输出。
这种情况先挂起验证，在下一次前置事件中检查当前会话记录：会话、轮次、调用 ID、工作目录、
原命令和成功退出必须一致；旧客户端核对原始 stdout 与实际交付的父工具结果。
不再落盘 CommandExecution 的客户端，在真实 PostToolUse 中绑定当前父调用 ID，
只接受该调用交付的 `{exit_code: 0, output: 正文}` 结果，全文匹配后才记账。
长文可在同一个父调用中用 notify 按换行边界分批输出上述结果；检查会按顺序合并同一调用的输出，
仍要求全文连续匹配，缺段、截断或混入别的调用都不算完整读取。
不接受历史相似命令、部分正文或从磁盘补出来的输出。压缩时同时清掉待验证记录。
最近一次失败摘要保存在当前会话缓存的 `last-read-failure.json`，用于区别执行失败、截断与全文不匹配；
正文中讨论错误或截断的文字不作为工具状态判断。

### 长文读取示例

命令只读一份全文；分批的是本次结果的交付，不是多条部分读取命令。以下代码放在同一次 `functions.exec`：

```javascript
const result = await tools.exec_command({
  cmd: "Get-Content -Raw -Encoding UTF8 -LiteralPath 'ai-docs/docs/modules/performance/performance-module-guide.md'",
  max_output_tokens: 30000
});
const body = result.output.replace(/\r\n/g, "\n");
let start = 0;
while (start < body.length) {
  let end = Math.min(start + 8000, body.length);
  if (end < body.length) {
    const newline = body.lastIndexOf("\n", end);
    if (newline < start) throw new Error("单行超过分批上限，需单独检查输出预算");
    end = newline;
  }
  notify({exit_code: result.exit_code, output: body.slice(start, end)});
  start = end + 1;
}
```

预算须容纳命令全文；若命令结果本身已截断，分批不能补回缺文。待下一次前置事件处理完，再核对该会话的 `reads/session.jsonl`；不能只看命令退出码宣称记账成功。
反复失败的根因、诊断字段与压缩重置的区别见 [pitfalls 记账条目](../../ai-docs/pitfalls.md#codex-必读文档反复重读仍未记账输出截断与会话记录格式不兼容)。

## 平台差异

- Codex 当前不支持 PreToolUse 的 `ask`。原 guard 要求确认的操作改为 deny，提示用户手动执行。没有加入自动许可或跳过检查的通道。
- Claude 的 Fable/opus/sonnet 代理规则不适用于 Codex；代理仍按当前客户端规则运行。
- 任意 shell 写文件、通过外部程序执行补丁、交互式 stdin、MCP 资产写入不被解析为文件编辑。代码修改必须使用正式 `apply_patch` 工具；其他路径按项目约定检查，不宣称 hooks 全覆盖。
- Codex 的前置适配器自身异常时阻止该次操作并报错，避免检查故障被误当作放行；后置与生命周期异常只提醒。原 Claude 代码的内部 fail-open 行为保持原样。
- 缓存按 session_id 与 transcript_path 隔离在 `.codex/.cache/`，不与 Claude 共用。并发更新同一文件的计数沿用原脚本的读改写逻辑，可能丢计数；避免多个代理同时改同一文件。
- `generate-doc/detect.py` 在原 Claude 配置也未启用，这次不额外启用。

## 验证

```powershell
$projectPython = uv python find
& $projectPython -B .codex/hooks/test_adapter.py
& $projectPython .claude/skills/evolution/gc_scan.py
```

测试使用独立临时仓库；覆盖生成物及路径归一、移动目标、敏感命令、必读拦截与读取成功/失败/截断、会话隔离、lint、规则提示、循环提醒、收尾、快照保存与恢复。
Windows 启动器已用模拟 git push 负载验证返回 deny；测试不会执行推送。
脚本与注册验证不等于真实会话已信任并运行。

依据：[官方 Codex hooks](https://learn.chatgpt.com/docs/hooks)。
