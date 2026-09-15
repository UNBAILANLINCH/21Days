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
