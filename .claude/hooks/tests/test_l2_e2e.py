#!/usr/bin/env python3
"""L2 端到端测试：子进程跑每个钩子，喂真实 stdin JSON，断言退出码与输出。

L1 保证判据算得对，L2 保证「钩子被真的调起来时」行为对 —— 两者都对不上的故障
（UTF-8、路径、退出码语义、JSON 形状）只有这一层抓得到。
"""
from __future__ import annotations

import json
import unittest

import hookenv
from hookenv import bash_payload, edit_payload, run_js_hook, run_py_hook

PY_HOOKS = ("required-reads", "knowledge-routing", "doom-loop-detect",
            "stop-check", "precompact-save")
README = ".claude/hooks/README.md"
#: 受 required_reads.json 的 `.claude/hooks/**` 规则管的编辑目标（不在豁免名单里）
GATED = ".claude/hooks/doom-loop-detect.py"


def tearDownModule() -> None:
    hookenv.cleanup()


def _decision(stdout: str) -> dict:
    """把钩子 stdout 解析成 hookSpecificOutput；没输出返回空 dict。"""
    if not stdout.strip():
        return {}
    return json.loads(stdout).get("hookSpecificOutput") or {}


class EmptyStdin(unittest.TestCase):
    """空 stdin / 坏 JSON：每个钩子都 exit 0 放行（fail-open）"""

    #: 这三个没有 tool_name 就无事可做，必须零输出。
    #: stop-check / precompact-save 不看 tool_name、直接扫工作区，输出取决于当时改了什么，
    #: 断言零输出会变成随工作区飘的假故障，所以只断言退出码。
    SILENT = ("required-reads", "knowledge-routing", "doom-loop-detect")

    def test_empty(self):
        for stem in PY_HOOKS:
            with self.subTest(hook=stem):
                code, out, err = hookenv.run_py_raw(stem, "")
                self.assertEqual(code, 0)
                self.assertEqual(err.strip(), "")
                if stem in self.SILENT:
                    self.assertEqual(out.strip(), "")

    def test_broken_json(self):
        for stem in PY_HOOKS:
            with self.subTest(hook=stem):
                code, _, err = hookenv.run_py_raw(stem, "{不是 JSON")
                self.assertEqual(code, 0)
                self.assertEqual(err.strip(), "", "钩子自己的异常不该冒到 stderr")


class RequiredReadsGate(unittest.TestCase):
    """required-reads：拦得住，也解得开"""

    def test_denies_without_read(self):
        sid = hookenv.new_sid()
        code, out, _ = run_py_hook("required-reads", edit_payload(sid, GATED))
        self.assertEqual(code, 0, "deny 靠 JSON 表达，退出码永远 0")
        d = _decision(out)
        self.assertEqual(d.get("permissionDecision"), "deny")
        reason = d.get("permissionDecisionReason", "")
        self.assertIn(README, reason)
        # 拒绝理由必须写清解锁办法，两条路都要提
        self.assertIn("Read 工具", reason)
        self.assertIn("cat", reason)
        # 第三人称陈述，不写祈使
        for bad in ("你必须", "请先读", "禁止"):
            self.assertNotIn(bad, reason)

    def test_read_tool_unlocks(self):
        sid = hookenv.new_sid()
        code, out, _ = run_py_hook(
            "required-reads", edit_payload(sid, README, tool="Read", event="PostToolUse"))
        self.assertEqual((code, out.strip()), (0, ""))
        code, out, _ = run_py_hook("required-reads", edit_payload(sid, GATED))
        self.assertEqual((code, out.strip()), (0, ""), "记过账之后应静默放行")

    def test_bash_cat_unlocks(self):
        """本次改动的核心：cat 读过 guide 再编辑，闸要放行"""
        sid = hookenv.new_sid()
        code, out, _ = run_py_hook("required-reads", bash_payload(sid, "cat " + README))
        self.assertEqual((code, out.strip()), (0, ""))
        code, out, _ = run_py_hook("required-reads", edit_payload(sid, GATED))
        self.assertEqual((code, out.strip()), (0, ""), "cat 读过就该放行")

    def test_bash_head_and_sed_unlock(self):
        for cmd in ("head -80 " + README, "sed -n '1,60p' " + README):
            with self.subTest(cmd=cmd):
                sid = hookenv.new_sid()
                run_py_hook("required-reads", bash_payload(sid, cmd))
                _, out, _ = run_py_hook("required-reads", edit_payload(sid, GATED))
                self.assertEqual(out.strip(), "")

    def test_piped_read_does_not_unlock(self):
        """管道里的 cat 只看到过滤后的结果，不该解锁"""
        sid = hookenv.new_sid()
        run_py_hook("required-reads", bash_payload(sid, "cat %s | head -5" % README))
        _, out, _ = run_py_hook("required-reads", edit_payload(sid, GATED))
        self.assertEqual(_decision(out).get("permissionDecision"), "deny")

    def test_exempt_target_not_gated(self):
        """写 README / SKILL / module-guide 不要求先读它自己"""
        sid = hookenv.new_sid()
        _, out, _ = run_py_hook("required-reads", edit_payload(sid, README))
        self.assertEqual(out.strip(), "")

    def test_unrelated_file_passes(self):
        sid = hookenv.new_sid()
        _, out, _ = run_py_hook("required-reads", edit_payload(sid, "docs/ci-setup.md"))
        self.assertEqual(out.strip(), "")


class KnowledgeRouting(unittest.TestCase):
    """knowledge-routing：首次注入规则清单，同一条提示不重复注入"""

    def test_injects_then_dedupes(self):
        sid = hookenv.new_sid()
        code, out, _ = run_py_hook("knowledge-routing", edit_payload(sid, ".claude/hooks/guard.js"))
        self.assertEqual(code, 0)
        ctx = _decision(out).get("additionalContext", "")
        self.assertIn("hook-injection-style.md", ctx)
        self.assertIn("知识路由", ctx)
        code, out, _ = run_py_hook("knowledge-routing", edit_payload(sid, ".claude/hooks/guard.js"))
        self.assertEqual((code, out.strip()), (0, ""), "同一条提示第二次应静默")

    def test_no_rule_no_output(self):
        sid = hookenv.new_sid()
        _, out, _ = run_py_hook("knowledge-routing", edit_payload(sid, "不存在的目录/x.txt"))
        self.assertEqual(out.strip(), "")


class DoomLoop(unittest.TestCase):
    """doom-loop：连续 5 次才提，交替编辑不提"""

    A = "Assets/_Project/Scripts/Runtime/Player/Move.cs"
    B = "Assets/_Project/Scripts/Runtime/Player/Jump.cs"

    def _edit(self, sid, path):
        return run_py_hook("doom-loop-detect", edit_payload(sid, path, event="PostToolUse"))

    def test_five_in_a_row_warns(self):
        sid = hookenv.new_sid()
        for i in range(4):
            code, _, err = self._edit(sid, self.A)
            self.assertEqual((code, err.strip()), (0, ""), "第 %d 次不该报" % (i + 1))
        code, _, err = self._edit(sid, self.A)
        self.assertEqual(code, 2, "第 5 次 exit 2 提醒 Agent")
        self.assertIn("连续 5 次", err)

    def test_interleaved_never_warns(self):
        """正常开发：改完 A 改 B 再回 A —— 一次都不该报"""
        sid = hookenv.new_sid()
        for _ in range(6):
            for path in (self.A, self.B):
                code, _, err = self._edit(sid, path)
                self.assertEqual((code, err.strip()), (0, ""))

    def test_counts_file_shape(self):
        """计数文件里连续状态不是 int，stop-check 的 isinstance 过滤天然跳过它"""
        sid = hookenv.new_sid()
        self._edit(sid, self.A)
        self._edit(sid, self.B)
        data = json.loads((hookenv.CACHE / ("edit-counts-%s.json" % sid)).read_text(encoding="utf-8"))
        self.assertEqual(data[self.A], 1)
        self.assertTrue(isinstance(data["__streak__"], dict))


class StopCheck(unittest.TestCase):
    """stop-check：永不阻断；同一份收尾提醒每会话只说一次"""

    def test_dedupes_same_notes(self):
        sid = hookenv.new_sid()
        # 造一个「高频编辑」状态，逼出确定的提醒内容
        fp = hookenv.CACHE / ("edit-counts-%s.json" % sid)
        fp.parent.mkdir(parents=True, exist_ok=True)
        fp.write_text(json.dumps({"Assets/_Project/Scripts/Runtime/Player/Move.cs": 12}),
                      encoding="utf-8")
        code, first, _ = run_py_hook("stop-check", {"session_id": sid, "hook_event_name": "Stop"})
        self.assertEqual(code, 0, "Stop 钩子永不阻断")
        self.assertIn("Move.cs（12 次）", first)
        code, second, _ = run_py_hook("stop-check", {"session_id": sid, "hook_event_name": "Stop"})
        self.assertEqual((code, second.strip()), (0, ""), "同一份提醒第二次应静默")


class PreCompact(unittest.TestCase):
    """precompact-save：存快照并告知去哪读；永不阻断压缩"""

    def test_outputs_valid_json_or_nothing(self):
        sid = hookenv.new_sid()
        code, out, _ = run_py_hook("precompact-save",
                                   {"session_id": sid, "hook_event_name": "PreCompact"})
        self.assertEqual(code, 0)
        if out.strip():  # 工作区干净时没有输出，也是对的
            d = _decision(out)
            self.assertIn(".claude/.cache/precompact-state.txt", d.get("additionalContext", ""))


@unittest.skipIf(hookenv.node_exe() is None, "没装 node")
class Guard(unittest.TestCase):
    """guard.js：生成物 / cd / 派单校验；ask 每次都问，附带说明只说一次"""

    def test_denies_meta(self):
        _, out, _ = run_js_hook("guard", {"tool_name": "Edit",
                                          "tool_input": {"file_path": "Assets/X.unity.meta"}})
        self.assertEqual(_decision(out).get("permissionDecision"), "deny")

    def test_denies_cd(self):
        _, out, _ = run_js_hook("guard", {"tool_name": "Bash",
                                          "tool_input": {"command": "cd X && cat a.md"}})
        self.assertEqual(_decision(out).get("permissionDecision"), "deny")

    def test_plain_bash_passes(self):
        _, out, _ = run_js_hook("guard", {"tool_name": "Bash",
                                          "tool_input": {"command": "cat a.md"}})
        self.assertEqual(out.strip(), "")

    def test_agent_model_rules(self):
        base = {"description": "x", "prompt": "y"}
        _, out, _ = run_js_hook("guard", {"tool_name": "Agent", "tool_input": dict(base)})
        self.assertEqual(_decision(out).get("permissionDecision"), "deny", "漏传 model 要拒")
        _, out, _ = run_js_hook("guard", {"tool_name": "Agent",
                                          "tool_input": dict(base, model="fable")})
        self.assertEqual(_decision(out).get("permissionDecision"), "deny")
        _, out, _ = run_js_hook("guard", {"tool_name": "Agent",
                                          "tool_input": dict(base, model="opus")})
        self.assertEqual(out.strip(), "")

    def test_commit_asks_every_time_note_once(self):
        sid = hookenv.new_sid()
        payload = {"session_id": sid, "tool_name": "Bash",
                   "tool_input": {"command": "git commit -m x"}}
        _, out, _ = run_js_hook("guard", payload)
        first = _decision(out)
        self.assertEqual(first.get("permissionDecision"), "ask")
        self.assertIn("提交约定", first.get("additionalContext", ""))
        _, out, _ = run_js_hook("guard", payload)
        second = _decision(out)
        self.assertEqual(second.get("permissionDecision"), "ask", "要人点头的事每次都要问")
        self.assertNotIn("additionalContext", second, "附带说明每会话只说一次")

    def test_ai_signature_denied(self):
        _, out, _ = run_js_hook("guard", {
            "tool_name": "Bash",
            "tool_input": {"command": "git commit -m 'x\n\nCo-Authored-By: Claude <a@b>'"}})
        self.assertEqual(_decision(out).get("permissionDecision"), "deny")


if __name__ == "__main__":
    unittest.main()
