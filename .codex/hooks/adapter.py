"""Codex hooks 适配：复用 Claude 检查逻辑，隔离缓存并转换工具负载。

载体：hooks.json 的工具事件；锚点：完整独立读取后检查 reads/session.jsonl。
读取失败返回并保存最近一次诊断摘要；客户端原生提供等价读取检查后移除此适配。
"""
import contextlib
import hashlib
import importlib.util
import io
import json
import re
import subprocess
import sys
from pathlib import Path

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[2]
CACHE = ROOT / ".codex" / ".cache"


def module(name, payload, state):
    source = ROOT / ".claude" / "hooks" / (name + ".py")
    spec = importlib.util.spec_from_file_location(name, source)
    obj = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(obj)
    obj._payload = lambda: payload
    obj._utf8_stdio = lambda: None
    obj.CACHE_DIR = state
    obj.READS_DIR = state / "reads"
    obj.SNAP = state / "precompact-state.txt"
    obj.SNAP_REL = obj.SNAP.relative_to(ROOT).as_posix()
    return obj


def invoke(name, payload, state):
    obj = module(name, payload, state)
    out, err = io.StringIO(), io.StringIO()
    with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
        obj.main()
    raw = out.getvalue().strip()
    try:
        result = json.loads(raw) if raw else {}
    except json.JSONDecodeError:
        result = {"systemMessage": raw}
    return result, err.getvalue().strip()


def relative(path, cwd):
    resolved = (Path(cwd) / path).resolve()
    try:
        return resolved.relative_to(ROOT).as_posix()
    except ValueError:
        raise ValueError("补丁目标超出项目，需单独处理：" + str(path))


def patch_paths(command, cwd):
    if not command.startswith("*** Begin Patch\n") or not command.rstrip().endswith("*** End Patch"):
        raise ValueError("无法识别补丁格式，未执行项目检查。")
    paths = re.findall(r"^\*\*\* (?:Add File|Update File|Delete File|Move to): (.+)$", command, re.M)
    if not paths:
        raise ValueError("补丁没有可识别的目标文件。")
    return list(dict.fromkeys(relative(p, cwd) for p in paths))


def guard(payload):
    proc = subprocess.run(
        ["node", str(ROOT / ".claude/hooks/guard.js")],
        input=json.dumps(payload), text=True, encoding="utf-8",
        capture_output=True, timeout=10, cwd=ROOT,
    )
    if proc.returncode:
        raise RuntimeError("guard.js 执行失败：" + proc.stderr)
    result = json.loads(proc.stdout) if proc.stdout.strip() else {}
    output = result.get("hookSpecificOutput", {})
    if output.get("permissionDecision") == "ask":
        output["permissionDecision"] = "deny"
        output["permissionDecisionReason"] += (
            "。Codex 当前不支持前置 hook 弹确认；请用户在终端或编辑器手动执行该操作，不关闭护栏重试。"
        )
    return result


def strings(value):
    if isinstance(value, str):
        return value
    if isinstance(value, dict):
        return "\n".join(strings(v) for v in value.values())
    if isinstance(value, list):
        return "\n".join(strings(v) for v in value)
    return ""


def successful(response, body=""):
    if isinstance(response, dict):
        if response.get("isError") or response.get("exit_code", 0) not in (0, None):
            return False
    text = strings(response)
    if body:
        # 文档本身可以讨论错误/截断；只检查正文以外的工具状态。
        text = text.replace("\r\n", "\n").replace(body, "")
    return not re.search(r"(?i)(?:exit code|exited with code):?\s*[1-9]|output truncated|tokens truncated", text)


def read_path(command, cwd, response):
    # ponytail: 只记独立、完整读取；复杂 shell 不猜，未记账时按提示单独重读。
    match = re.fullmatch(
        r"Get-Content -Raw -Encoding UTF8 -LiteralPath '([^'\r\n]+)'",
        command.strip(), re.I,
    )
    if not match:
        return None
    path = relative(match[1], cwd)
    body = (ROOT / path).read_text(encoding="utf-8").strip().replace("\r\n", "\n")
    # 只有工具结果包含完整文件才记账，截断和失败读取不算。
    if body and body in strings(response).replace("\r\n", "\n") and successful(response, body):
        return path
    return None


def transcript_response(payload, command, cwd):
    """只接受当前真实调用的完成事件，不从历史相似命令或磁盘正文补输出。"""
    transcript = payload.get("transcript_path")
    call_id = payload.get("tool_use_id")
    if not transcript or not call_id:
        return None
    parent_id, result = None, None
    with Path(transcript).open(encoding="utf-8") as stream:
        for line in stream:
            try:
                record = json.loads(line)
            except json.JSONDecodeError:
                continue
            event = record.get("payload", {})
            if record.get("type") == "response_item":
                if event.get("type") == "custom_tool_call" and result is None:
                    parent_id = event.get("call_id")
                elif (event.get("type") == "custom_tool_call_output" and result is not None
                      and event.get("call_id") == parent_id):
                    # 原始 stdout 不能证明模型拿到了全文，还要核对实际交付的工具结果。
                    delivered = []
                    for block in event.get("output", []):
                        text = block.get("text", "")
                        try:
                            delivered.append(strings(json.loads(text)))
                        except (ValueError, TypeError):
                            delivered.append(text)
                    result["output"] = "\n".join(delivered)
                    return result
            item = event.get("item", {})
            if record.get("type") != "event_msg" or event.get("type") != "item_completed":
                continue
            if item.get("id") != call_id or item.get("type") != "CommandExecution":
                continue
            if (event.get("thread_id") != payload.get("session_id")
                    or event.get("turn_id") != payload.get("turn_id")
                    or item.get("cwd", "").lower() != Path(cwd).resolve().as_uri().lower()
                    or item.get("command", [None])[-1] != command
                    or item.get("status") != "completed" or item.get("exit_code") != 0):
                return None
            original = {"exit_code": item["exit_code"], "output": item.get("stdout", "")}
            if not read_path(command, cwd, original):
                return None
            result = original
    return None


def handle(payload):
    event = payload.get("hook_event_name", "")
    sid = str(payload.get("session_id") or "")
    if not sid:
        raise ValueError("hook 缺少 session_id，无法安全隔离读取与编辑记录。")
    scope = sid + "|" + str(payload.get("transcript_path") or "")
    state = CACHE / hashlib.sha256(scope.encode()).hexdigest()[:24]
    base = dict(payload, session_id="session", cwd=str(ROOT))
    cwd = payload.get("cwd") or str(ROOT)
    tool = payload.get("tool_name")
    ti = payload.get("tool_input") or {}
    command = ti.get("command", ti.get("cmd", "")) if isinstance(ti, dict) else ti
    response = payload.get("tool_response", {})
    notes = []
    pending = state / "pending-read.json"

    if event == "PreToolUse" and pending.exists():
        waiting = json.loads(pending.read_text(encoding="utf-8"))
        for call_id, prior in list(waiting.items()):
            original = transcript_response(prior, prior["command"], prior["cwd"])
            if original is None:
                continue  # 同一父工具中的下一条命令尚不能看到父工具完成输出。
            path = read_path(prior["command"], prior["cwd"], original)
            if path:
                invoke("required-reads", dict(base, hook_event_name="PostToolUse", tool_name="Read", tool_input={"file_path": path}), state)
            del waiting[call_id]
        pending.write_text(json.dumps(waiting), encoding="utf-8")

    if event in ("SessionStart", "PostCompact"):
        # 压缩后已读信息不能代表仍在上下文里：清空本会话账本，保留编辑次数。
        if event == "PostCompact" or payload.get("source") in ("compact", "clear"):
            pending.unlink(missing_ok=True)
            log = state / "reads/session.jsonl"
            if log.exists():
                log.write_text("", encoding="utf-8")
        notes.append("项目 hooks 已运行：编辑使用 apply_patch；必读文档用独立命令 Get-Content -Raw -Encoding UTF8 -LiteralPath '相对路径' 读取。")
        snap = state / "precompact-state.txt"
        if snap.exists():
            notes.append(snap.read_text(encoding="utf-8"))
        return {"hookSpecificOutput": {"hookEventName": event, "additionalContext": "\n".join(notes)}}

    if event == "PreCompact":
        invoke("precompact-save", base, state)
        return {"systemMessage": "项目工作态快照已处理；压缩后自动恢复可用快照。"}
    if event == "Stop":
        result, err = invoke("stop-check", base, state)
        return {"systemMessage": "\n".join(filter(None, [result.get("systemMessage"), err]))}

    if tool in ("Bash", "exec_command"):
        if event == "PreToolUse":
            return guard(dict(base, tool_name="Bash", tool_input={"command": command}))
        path = read_path(command, cwd, response)
        is_read = re.fullmatch(r"Get-Content -Raw -Encoding UTF8 -LiteralPath '([^'\r\n]+)'", command.strip(), re.I)
        if not path and is_read and isinstance(response, str) and response.startswith("Warning: truncated output"):
            # Codex 的 hook 摘要固定截断；同次调用的原始 stdout 仍须通过全文检查。
            original = transcript_response(payload, command, cwd)
            if original is not None:
                path = read_path(command, cwd, original)
            else:
                state.mkdir(parents=True, exist_ok=True)
                evidence = {key: payload.get(key) for key in ("session_id", "turn_id", "transcript_path", "tool_use_id")}
                waiting = json.loads(pending.read_text(encoding="utf-8")) if pending.exists() else {}
                waiting[payload.get("tool_use_id")] = dict(evidence, command=command, cwd=cwd)
                pending.write_text(json.dumps(waiting), encoding="utf-8")
        if path:
            invoke("required-reads", dict(base, tool_name="Read", tool_input={"file_path": path}), state)
        elif is_read:
            requested = is_read.group(1)
            body = (ROOT / relative(requested, cwd)).read_text(encoding="utf-8").strip().replace("\r\n", "\n")
            output = strings(response).replace("\r\n", "\n")
            diagnostic = {
                "file": relative(requested, cwd),
                "response_type": type(response).__name__,
                "response_keys": list(response) if isinstance(response, dict) else [],
                "payload_keys": list(payload), "tool_use_id": payload.get("tool_use_id"),
                "tool_input": ti,
                "body_chars": len(body), "output_chars": len(output),
                "full_body_present": body in output, "successful": successful(response),
                "hook_summary_truncated": output.startswith("Warning: truncated output"),
            }
            state.mkdir(parents=True, exist_ok=True)
            (state / "last-read-failure.json").write_text(json.dumps(diagnostic, ensure_ascii=False), encoding="utf-8")
            return {"systemMessage": "读取未记账诊断：" + json.dumps(diagnostic, ensure_ascii=False)}
        return {}

    if tool != "apply_patch":
        return {}
    paths = patch_paths(command.replace("\r\n", "\n"), cwd)
    if event == "PostToolUse" and not successful(response):
        return {}
    for path in paths:
        current = dict(base, tool_name="Edit", tool_input={"file_path": path})
        if event == "PreToolUse":
            result = guard(current)
            if result:
                return result
            result, _ = invoke("required-reads", current, state)
            if result:
                output = result["hookSpecificOutput"]
                output["permissionDecisionReason"] += (
                    "\n请逐份使用独立命令读取：Get-Content -Raw -Encoding UTF8 -LiteralPath '文档相对路径'"
                    "\n不要用部分读取、管道或多命令合并替代；确保完整输出后重试。"
                )
                return result
            result, _ = invoke("knowledge-routing", current, state)
            context = result.get("hookSpecificOutput", {}).get("additionalContext")
            if context:
                notes.append(context)
        elif event == "PostToolUse":
            _, warning = invoke("doom-loop-detect", current, state)
            if warning:
                notes.append(warning)
            if path.lower().endswith(".cs") and (ROOT / path).is_file():
                proc = subprocess.run(
                    [sys.executable, str(ROOT / ".claude/skills/project-lint/lint.py"), path],
                    cwd=ROOT, text=True, encoding="utf-8", capture_output=True, timeout=15,
                )
                if proc.returncode or proc.stderr:
                    notes.append(proc.stderr or "project-lint 执行失败。")
    if notes:
        return {"hookSpecificOutput": {"hookEventName": event, "additionalContext": "\n".join(notes)}}
    return {}


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    try:
        payload = json.loads(sys.stdin.buffer.read().decode("utf-8"))
        result = handle(payload)
    except Exception as error:
        # 前置检查故障不伪装通过；后置/生命周期检查只提醒，不撤销已经执行的工具。
        reason = "Codex 项目 hook 检查失败：" + str(error)
        if isinstance(locals().get("payload"), dict) and payload.get("hook_event_name") == "PreToolUse":
            result = {"hookSpecificOutput": {"hookEventName": "PreToolUse", "permissionDecision": "deny", "permissionDecisionReason": reason}}
        else:
            result = {"systemMessage": reason}
    print(json.dumps(result, ensure_ascii=False))
