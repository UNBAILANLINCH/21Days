#!/usr/bin/env python3
"""L2 端到端测试：子进程跑每个钩子，喂真实 stdin JSON，断言退出码与输出。

L1 保证判据算得对，L2 保证「钩子被真的调起来时」行为对 —— 两者都对不上的故障
（UTF-8、路径、退出码语义、JSON 形状）只有这一层抓得到。
"""
from __future__ import annotations

import json
import shutil
import tempfile
import unittest
from pathlib import Path

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


@unittest.skipIf(hookenv.node_exe() is None, "没装 node")
class GuardGitGlobalOptions(unittest.TestCase):
    """guard.js：git 子命令前带全局选项（本工程推荐的 `git -C <路径>`）时，三道 git 判据照样生效。

    2026-09-30 两次带 AI 署名的提交就是 `git -C … commit` 绕过了整串正则。
    """

    SIG = "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"

    def _decide(self, cmd):
        _, out, _ = run_js_hook("guard", {"session_id": hookenv.new_sid(), "tool_name": "Bash",
                                          "tool_input": {"command": cmd}})
        return _decision(out).get("permissionDecision"), out

    def _assert(self, cmd, want):
        got, out = self._decide(cmd)
        self.assertEqual(got, want, "%r → %s" % (cmd, out.strip() or "零输出"))

    def test_c_path_commit_asks(self):
        self._assert('git -C D:/x commit -m "修 guard"', "ask")

    def test_c_quoted_path_heredoc_signature_denied(self):
        self._assert("git -C \"D:/a b\" commit -F - <<'EOF'\nfix(harness): x\n\n%s\nEOF" % self.SIG, "deny")

    def test_c_m_signature_denied(self):
        self._assert('git -C D:/x commit -m "x\n\n%s"' % self.SIG, "deny")

    def test_config_option_commit_asks(self):
        self._assert("git -c user.name=x commit", "ask")

    def test_stacked_options_commit_asks(self):
        self._assert('git --no-pager -c "user.name=A B" -C "D:/a b" --git-dir=D:/x/.git -P commit -m y', "ask")

    def test_c_push_asks(self):
        self._assert("git -C D:/x push origin main", "ask")

    def test_c_reset_hard_denied(self):
        self._assert("git -C D:/x reset --hard", "deny")

    def test_c_checkout_dashdash_denied(self):
        self._assert("git -C D:/x checkout -- file", "deny")

    def test_c_readonly_passes(self):
        for cmd in ("git -C D:/x status", "git -C D:/x log", "git -C D:/x diff",
                    "git -C D:/x reset --soft HEAD~1", "git -C D:/x checkout -b feat"):
            with self.subTest(cmd=cmd):
                self._assert(cmd, None)

    def test_plain_forms_unchanged(self):
        """不带全局选项的原有写法，结论与改前一致"""
        for cmd, want in (("git commit -m x", "ask"), ("git push", "ask"),
                          ("git commit -m 'x\n\n%s'" % self.SIG, "deny"),
                          ("git reset --hard", "deny"), ("git checkout -- a.cs", "deny"),
                          ("git clean -fd", "deny"), ("git restore a.cs", "deny"),
                          ("git status", None), ("git log --oneline -5", None)):
            with self.subTest(cmd=cmd):
                self._assert(cmd, want)

    def test_compound_commands(self):
        self._assert("git -C D:/x add a.cs && git -C D:/x commit -m y", "ask")
        self._assert("git -C D:/x status; git -C D:/x reset --hard", "deny")
        self._assert("git -C D:/x status\ngit -C D:/x checkout -- a.cs", "deny")

    def test_commit_message_mentioning_discard_asks(self):
        """提交信息里提到 reset --hard：含 commit 先落到 ask，不误拒（顺序同旧判据）"""
        self._assert("git -C D:/x commit -m 'fix: 拦 git -C x reset --hard'", "ask")

    def test_mention_in_echo_asks(self):
        """口径沿用旧判据：引号里提到 git commit 也问一次（bash -c "git …" 同理被认出）"""
        self._assert('echo "git commit"', "ask")
        self._assert('bash -c "git -C D:/x commit -m y"', "ask")

    def test_unterminated_quote_fails_open(self):
        self._assert('git -C "D:/x commit', None)


@unittest.skipIf(hookenv.node_exe() is None, "没装 node")
class GuardCommitMessageFile(unittest.TestCase):
    """guard.js：`git commit -F <文件>`（commit-convention 推荐写法）的信息文件也查署名；读不到落回 ask"""

    SIG = "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"

    @classmethod
    def setUpClass(cls):
        cls.tmp = Path(tempfile.mkdtemp(prefix="guard-msgfile-e2e-"))
        (cls.tmp / "sub").mkdir()
        (cls.tmp / "signed.txt").write_text("fix(harness): x\n\n%s\n" % cls.SIG, encoding="utf-8")
        (cls.tmp / "clean.txt").write_text("fix(harness): x\n", encoding="utf-8")
        (cls.tmp / "sub" / "msg.txt").write_text("fix: x\n\n%s\n" % cls.SIG, encoding="utf-8")

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(cls.tmp, ignore_errors=True)

    def _decide(self, cmd, cwd=None):
        payload = {"session_id": hookenv.new_sid(), "tool_name": "Bash", "tool_input": {"command": cmd}}
        if cwd:
            payload["cwd"] = cwd
        _, out, _ = run_js_hook("guard", payload)
        return _decision(out)

    def test_signed_file_denied(self):
        d = self._decide('git -C D:/x commit -F "%s" -- a.cs' % (self.tmp / "signed.txt").as_posix())
        self.assertEqual(d.get("permissionDecision"), "deny")
        self.assertIn("signed.txt", d.get("permissionDecisionReason", ""), "拒绝理由要点名是哪个文件")

    def test_clean_file_asks(self):
        d = self._decide('git commit --file="%s"' % (self.tmp / "clean.txt").as_posix())
        self.assertEqual(d.get("permissionDecision"), "ask")

    def test_missing_file_asks(self):
        d = self._decide('git commit -F "%s"' % (self.tmp / "不存在.txt").as_posix())
        self.assertEqual(d.get("permissionDecision"), "ask", "读不到文件 fail-open，落回原来的 ask")

    def test_relative_path_with_c_resolved(self):
        d = self._decide('git -C "%s" -C sub commit -F msg.txt' % self.tmp.as_posix())
        self.assertEqual(d.get("permissionDecision"), "deny")

    def test_relative_path_with_payload_cwd_resolved(self):
        d = self._decide("git commit -Fsigned.txt", cwd=str(self.tmp))
        self.assertEqual(d.get("permissionDecision"), "deny")


@unittest.skipIf(hookenv.node_exe() is None, "没装 node")
class GuardRunTestsScope(unittest.TestCase):
    """guard.js：PlayMode 回放要限定范围（model-routing 硬规则 6）——全跑十几分钟，还占着共用编辑器"""

    TOOL = "mcp__UnityMCP__run_tests"
    ONE_MODULE = "^Game\\.Tests\\.Showcase\\.Performance\\."

    def _run(self, **tool_input):
        _, out, _ = run_js_hook("guard", {"tool_name": self.TOOL, "tool_input": tool_input})
        return out

    def _deny_reason(self, out):
        d = _decision(out)
        self.assertEqual(d.get("permissionDecision"), "deny")
        return d.get("permissionDecisionReason", "")

    def test_editmode_unfiltered_passes(self):
        """EditMode 快，全量合理"""
        self.assertEqual(self._run(mode="EditMode").strip(), "")

    def test_playmode_unfiltered_denied(self):
        self.assertIn("十几分钟", self._deny_reason(self._run(mode="PlayMode")))

    def test_playmode_single_module_allowed(self):
        self.assertEqual(self._run(mode="PlayMode", group_names=[self.ONE_MODULE]).strip(), "")

    def test_playmode_two_modules_denied(self):
        reason = self._deny_reason(self._run(
            mode="PlayMode",
            group_names=["^Game\\.Tests\\.Showcase\\.(Performance|Dialogue)\\."]))
        self.assertIn("2 个回放模块", reason)

    def test_playmode_two_single_module_entries_allowed(self):
        """一条正则只命中一个模块就算限定住了，逐条列模块是允许的"""
        self.assertEqual(self._run(
            mode="PlayMode",
            group_names=[self.ONE_MODULE, "^Game\\.Tests\\.Showcase\\.Dialogue\\."]).strip(), "")

    def test_playmode_whole_showcase_denied(self):
        self.assertIn("回放模块", self._deny_reason(
            self._run(mode="PlayMode", group_names=["^Game\\.Tests\\.Showcase\\."])))

    def test_playmode_explicit_cases_allowed(self):
        self.assertEqual(self._run(
            mode="PlayMode",
            test_names=["Game.Tests.Showcase.Performance.PerformanceShowcase.Play_StartsPerformance"],
        ).strip(), "")

    def test_playmode_non_showcase_assembly_allowed(self):
        """程序集过滤与分组过滤是「与」关系：不含回放程序集就跑不到回放"""
        self.assertEqual(self._run(mode="PlayMode", assembly_names=["Game.Tests.PlayMode"]).strip(), "")

    def test_playmode_group_without_showcase_namespace_allowed(self):
        self.assertEqual(self._run(mode="PlayMode", group_names=["^Game\\.Tests\\.EditMode\\.Core\\."]).strip(), "")

    def test_playmode_clear_stuck_allowed(self):
        """只清孤儿任务，不开跑"""
        self.assertEqual(self._run(mode="PlayMode", clear_stuck=True).strip(), "")

    def test_playmode_json_string_group_allowed(self):
        """group_names 传 JSON 字符串形式的数组（MCP 客户端的常见形态）也要认"""
        self.assertEqual(self._run(mode="PlayMode", group_names='["%s"]' % self.ONE_MODULE).strip(), "")


if __name__ == "__main__":
    unittest.main()
