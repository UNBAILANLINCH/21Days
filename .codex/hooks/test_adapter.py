"""运行：uv run --no-project --offline python .codex/hooks/test_adapter.py"""
import importlib.util
import json
import shutil
import subprocess
import tempfile
from pathlib import Path

spec = importlib.util.spec_from_file_location("adapter", Path(__file__).with_name("adapter.py"))
adapter = importlib.util.module_from_spec(spec)
spec.loader.exec_module(adapter)


def run():
    real_root = adapter.ROOT
    with tempfile.TemporaryDirectory(prefix="21days-hooks-") as directory:
        root = Path(directory)
        shutil.copytree(real_root / ".claude/hooks", root / ".claude/hooks")
        shutil.copytree(real_root / ".claude/rules", root / ".claude/rules")
        shutil.copytree(real_root / ".claude/skills/project-lint", root / ".claude/skills/project-lint")
        # 复用脚本根据自身路径取根，所以临时目录也使用相同布局。
        adapter.ROOT, adapter.CACHE = root, root / ".codex/.cache"
        subprocess.run(["git", "init", "-q", str(root)], check=True)
        base = {"session_id": "a", "transcript_path": "parent", "cwd": str(root)}

        def call(event, tool="", command="", response=None, **kwargs):
            return adapter.handle(dict(base, hook_event_name=event, tool_name=tool,
                                       tool_input={"command": command},
                                       tool_response=response or {}, **kwargs))

        def patch(path):
            return "*** Begin Patch\n*** Add File: " + path + "\n+x\n*** End Patch\n"

        def decision(result):
            return result.get("hookSpecificOutput", {}).get("permissionDecision")

        assert decision(call("PreToolUse", "apply_patch", patch("Assets/x.meta"))) == "deny"
        assert decision(call("PreToolUse", "apply_patch", patch("Assets/../Library/x"))) == "deny"
        assert decision(call("PreToolUse", "apply_patch", patch("ProjectSettings/x"))) == "deny"
        assert decision(call("PreToolUse", "Bash", "git push")) == "deny"
        assert decision(call("PreToolUse", "Bash", "git reset --hard")) == "deny"
        assert call("PreToolUse", "Bash", "git status --short") == {}
        moved = "*** Begin Patch\n*** Update File: a.cs\n*** Move to: Library/a.cs\n@@\n-x\n+y\n*** End Patch\n"
        assert decision(call("PreToolUse", "apply_patch", moved)) == "deny"

        target = ".claude/hooks/required-reads.py"
        assert decision(call("PreToolUse", "apply_patch", patch(target))) == "deny"
        doc = ".claude/hooks/README.md"
        content = (root / doc).read_text(encoding="utf-8")
        command = "Get-Content -Raw -Encoding UTF8 -LiteralPath '" + doc + "'"
        call("PostToolUse", "Bash", command, {"exit_code": 1, "output": content})
        assert decision(call("PreToolUse", "apply_patch", patch(target))) == "deny"
        call("PostToolUse", "Bash", command, {"exit_code": 0, "output": content[:30]})
        assert decision(call("PreToolUse", "apply_patch", patch(target))) == "deny"
        call("PostToolUse", "Bash", command, {"exit_code": 0, "output": content})
        assert decision(call("PreToolUse", "apply_patch", patch(target))) is None
        base["transcript_path"] = "child"
        assert decision(call("PreToolUse", "apply_patch", patch(target))) == "deny"
        base["transcript_path"] = "parent"

        # 正文讨论截断/退出码不能误判；真实非零退出和实际截断仍拒绝。
        literal = root / "literal.md"
        literal.write_text("output truncated\ntokens truncated\nexit code: 1", encoding="utf-8")
        literal_command = "Get-Content -Raw -Encoding UTF8 -LiteralPath 'literal.md'"
        literal_body = literal.read_text(encoding="utf-8")
        assert adapter.read_path(literal_command, root, literal_body) == "literal.md"
        assert adapter.read_path(literal_command, root, {"exit_code": 1, "output": literal_body}) is None
        assert adapter.read_path(literal_command, root, literal_body[:10]) is None

        transcript = root / "transcript.jsonl"
        evidence = dict(base, transcript_path=str(transcript), tool_use_id="exec-1", turn_id="turn-1")
        item = {"type": "CommandExecution", "id": "exec-1", "cwd": root.as_uri(),
                "command": ["pwsh", "-Command", command], "status": "completed", "exit_code": 0, "stdout": content}
        records = [
            {"type": "response_item", "payload": {"type": "custom_tool_call", "call_id": "parent-1"}},
            {"type": "event_msg", "payload": {"type": "item_completed", "thread_id": "a", "turn_id": "turn-1", "item": item}},
            {"type": "response_item", "payload": {"type": "custom_tool_call_output", "call_id": "parent-1",
                "output": [{"type": "input_text", "text": json.dumps({"exit_code": 0, "output": content})}]}},
        ]

        def save_records():
            transcript.write_text("\n".join(json.dumps(r) for r in records), encoding="utf-8")

        save_records()
        actual = adapter.transcript_response(evidence, command, root)
        assert adapter.read_path(command, root, actual) == doc
        whole_output = records.pop()
        split = content.index("\n", len(content) // 2)
        first = {"type": "response_item", "payload": {"type": "custom_tool_call_output", "call_id": "parent-1",
                 "output": content[:split]}}
        second = {"type": "response_item", "payload": {"type": "custom_tool_call_output", "call_id": "parent-1",
                  "output": content[split + 1:]}}
        records.extend([first, second])
        save_records()
        assert adapter.read_path(command, root, adapter.transcript_response(evidence, command, root)) == doc
        second["payload"]["call_id"] = "another-parent"
        save_records()
        assert adapter.read_path(command, root, adapter.transcript_response(evidence, command, root)) is None
        records.pop()
        save_records()
        assert adapter.read_path(command, root, adapter.transcript_response(evidence, command, root)) is None
        records[-1] = whole_output
        save_records()
        legacy_records = records[:]
        records = [records[0], first, second]
        first["payload"]["output"] = json.dumps({"exit_code": 0, "output": content[:split]})
        second["payload"]["call_id"] = "parent-1"
        second["payload"]["output"] = json.dumps({"exit_code": 0, "output": content[split + 1:]})
        bound = dict(evidence, parent_call_id="parent-1")
        save_records()
        assert adapter.read_path(command, root, adapter.transcript_response(bound, command, root)) == doc
        second["payload"]["output"] = json.dumps({"exit_code": 1, "output": content[split + 1:]})
        save_records()
        assert adapter.transcript_response(bound, command, root) is None
        second["payload"]["output"] = json.dumps({"exit_code": 0, "output": content[split + 1:]})
        second["payload"]["call_id"] = "another-parent"
        save_records()
        assert adapter.read_path(command, root, adapter.transcript_response(bound, command, root)) is None
        records = legacy_records
        save_records()
        for key, wrong in [("tool_use_id", "other"), ("session_id", "other"), ("turn_id", "other")]:
            assert adapter.transcript_response(dict(evidence, **{key: wrong}), command, root) is None
        assert adapter.transcript_response(evidence, command + " extra", root) is None
        item["exit_code"] = 1
        save_records()
        assert adapter.transcript_response(evidence, command, root) is None
        item["exit_code"] = 0
        records[-1]["payload"]["output"][0]["text"] = content[:30]
        save_records()
        assert adapter.read_path(command, root, adapter.transcript_response(evidence, command, root)) is None
        records.pop()
        save_records()
        assert adapter.transcript_response(evidence, command, root) is None
        adapter.handle(dict(evidence, hook_event_name="PostToolUse", tool_name="Bash",
                            tool_input={"command": command}, tool_response="Warning: truncated output"))
        # 同父工具尚未完成时，下一条命令不能吞掉第一条读取的待验证记录。
        adapter.handle(dict(evidence, hook_event_name="PreToolUse", tool_name="Bash",
                            tool_input={"command": "git status --short"}))
        assert any(json.loads(p.read_text()) for p in adapter.CACHE.rglob("pending-read.json"))
        records.append({"type": "response_item", "payload": {"type": "custom_tool_call_output", "call_id": "parent-1",
                       "output": [{"type": "input_text", "text": json.dumps({"exit_code": 0, "output": content})}]}})
        save_records()
        assert decision(adapter.handle(dict(evidence, hook_event_name="PreToolUse", tool_name="apply_patch",
                        tool_input={"command": patch(target)}))) is None

        cs = root / "Assets/_Project/Scripts/Runtime/Example/Test.cs"
        cs.parent.mkdir(parents=True)
        cs.write_text("class Test : MonoBehaviour {\npublic float speed;\n// TEMP\n}", encoding="utf-8")
        rel = cs.relative_to(root).as_posix()
        routing = call("PreToolUse", "apply_patch", patch(rel))
        assert "csharp-code" in json.dumps(routing)
        outputs = [call("PostToolUse", "apply_patch", patch(rel)) for _ in range(5)]
        assert "project-lint" in json.dumps(outputs[0]) and "speed" in json.dumps(outputs[0]), outputs[0]
        assert "doom-loop" in json.dumps(outputs[-1])
        stopped = call("Stop")
        assert "TEMP" in json.dumps(stopped) and ".meta" in json.dumps(stopped)
        assert "decision" not in stopped
        call("PreCompact")
        assert list(adapter.CACHE.rglob("precompact-state.txt"))
        restored = call("PostCompact")
        assert "git status" in json.dumps(restored)
        assert decision(call("PreToolUse", "apply_patch", patch(target))) == "deny"
        try:
            adapter.patch_paths(patch("../outside.cs"), str(root))
        except ValueError:
            pass
        else:
            raise AssertionError("escaped root accepted")
        print("PASS: guard, moves, required reads, failed/truncated reads, session isolation, routing, lint, loop, stop, snapshot and restore")
    adapter.ROOT = real_root


if __name__ == "__main__":
    run()
