#!/usr/bin/env python3
"""L1 纯函数测试：判据本身对不对，不起子进程。

覆盖四样：
  · required-reads 的 Bash 读取解析 / 路径归一化 / `${seg:N}` 占位符展开
  · doom-loop 的连续性计数状态机
  · _hook_common 的同会话去重
  · guard.js 的 git 调用识别（经 node require 调纯函数，没装 node 时跳过）
"""
from __future__ import annotations

import os
import shutil
import tempfile
import unittest
from pathlib import Path

import hookenv
from hookenv import ROOT

rr = hookenv.load_hook("required-reads")
dl = hookenv.load_hook("doom-loop-detect")
common = hookenv.load_hook("_hook_common")

# 用真实存在的文件做样例：解析的最后一关是「文件必须存在」
README = ".claude/hooks/README.md"
RULE = ".claude/rules/csharp-code.md"
CONF = ".claude/hooks/required_reads.json"


def tearDownModule() -> None:
    hookenv.cleanup()


class BashReadParsing(unittest.TestCase):
    """required-reads：从 Bash 命令里解析出「确实被读出来」的文件"""

    def test_cat_records(self):
        self.assertEqual(rr._bash_read_targets("cat " + README), [README])

    def test_head_tail_sed_record(self):
        self.assertEqual(rr._bash_read_targets("head -50 " + RULE), [RULE])
        self.assertEqual(rr._bash_read_targets("tail -5 " + CONF), [CONF])
        self.assertEqual(rr._bash_read_targets("sed -n '1,40p' " + RULE), [RULE])

    def test_head_n_value_is_not_a_file(self):
        """`head -n 20 x` 里的 20 不能被当成文件名记账"""
        self.assertEqual(rr._bash_read_targets("head -n 20 " + RULE), [RULE])

    def test_quoted_path(self):
        self.assertEqual(rr._bash_read_targets('cat "%s"' % README), [README])
        self.assertEqual(rr._bash_read_targets("cat '%s'" % README), [README])

    def test_multiple_statements(self):
        cmd = "cat %s; echo ---; cat %s" % (README, RULE)
        self.assertEqual(rr._bash_read_targets(cmd), [README, RULE])
        self.assertEqual(rr._bash_read_targets("cat %s && cat %s" % (README, RULE)), [README, RULE])

    def test_windows_absolute_path(self):
        """负载里常见的 `X:\\dir\\file` 形态要能归一回相对路径"""
        abs_win = str((ROOT / README.replace("/", "\\")))
        self.assertEqual(rr._bash_read_targets("cat " + abs_win), [README])
        self.assertEqual(rr._bash_read_targets('cat "%s"' % abs_win), [README])

    def test_redirect_is_not_a_read(self):
        self.assertEqual(rr._bash_read_targets("cat %s > /tmp/x" % README), [])
        self.assertEqual(rr._bash_read_targets("cat %s >> /tmp/x" % README), [])

    def test_stderr_redirect_still_counts(self):
        """`2>/dev/null` 只丢 stderr，内容照样摆在眼前 —— 这是最常见的读法之一"""
        self.assertEqual(rr._bash_read_targets("cat %s 2>/dev/null" % README), [README])
        self.assertEqual(rr._bash_read_targets("cat %s 2>&1" % README), [README])

    def test_pipe_is_not_a_read(self):
        """进了管道就只看到过滤后的结果，不算读过整份"""
        self.assertEqual(rr._bash_read_targets("cat %s | grep 铁律" % README), [])
        self.assertEqual(rr._bash_read_targets("cat %s | head -5" % README), [])

    def test_tee_and_substitution_are_skipped(self):
        self.assertEqual(rr._bash_read_targets("cat %s | tee copy.md" % README), [])
        self.assertEqual(rr._bash_read_targets("cat $(ls *.md)"), [])

    def test_non_read_commands(self):
        self.assertEqual(rr._bash_read_targets("grep -n 铁律 " + README), [])
        self.assertEqual(rr._bash_read_targets("python x.py"), [])
        self.assertEqual(rr._bash_read_targets(""), [])

    def test_sed_without_n_is_not_a_read(self):
        """`sed 's/a/b/' f` 是在改不是在读"""
        self.assertEqual(rr._bash_read_targets("sed 's/a/b/' " + RULE), [])

    def test_missing_file_not_recorded(self):
        self.assertEqual(rr._bash_read_targets("cat .claude/hooks/不存在.md"), [])

    def test_glob_not_recorded(self):
        self.assertEqual(rr._bash_read_targets("cat .claude/rules/*.md"), [])


class RelNormalize(unittest.TestCase):
    """required-reads：路径归一化"""

    def test_dot_slash_stripped(self):
        self.assertEqual(rr._rel("./CLAUDE.md"), "CLAUDE.md")

    def test_backslash_absolute(self):
        self.assertEqual(rr._rel(str(ROOT / "Assets" / "x.cs")), "Assets/x.cs")

    def test_relative_kept(self):
        self.assertEqual(rr._rel("Assets/_Project/Scripts/Runtime/Player/Move.cs"),
                         "Assets/_Project/Scripts/Runtime/Player/Move.cs")

    def test_empty(self):
        self.assertEqual(rr._rel(""), "")
        self.assertEqual(rr._rel(None), "")


class SegExpand(unittest.TestCase):
    """required-reads：`${seg:N}` / `${seg:N:lower}` 占位符展开"""

    REL = "Assets/_Project/Scripts/Runtime/Player/Move.cs"

    def test_lower(self):
        tpl = "ai-docs/docs/modules/${seg:4:lower}/${seg:4:lower}-module-guide.md"
        self.assertEqual(rr._expand(tpl, self.REL),
                         "ai-docs/docs/modules/player/player-module-guide.md")

    def test_plain_and_upper(self):
        self.assertEqual(rr._expand("${seg:0}", self.REL), "Assets")
        self.assertEqual(rr._expand("${seg:4:upper}", self.REL), "PLAYER")

    def test_out_of_range_stays_literal(self):
        """段数不够时原样保留 → 展开后的路径不存在 → 按「必读文件不存在」跳过"""
        self.assertEqual(rr._expand("${seg:99}", self.REL), "${seg:99}")


class DoomLoopStreak(unittest.TestCase):
    """doom-loop：连续性计数状态机"""

    A = "Assets/_Project/Scripts/Runtime/Player/Move.cs"
    B = "Assets/_Project/Scripts/Runtime/Player/Jump.cs"
    DOC = "ai-docs/docs/architecture.md"

    def _run(self, keys):
        """按顺序喂一串编辑，返回每一步的（连续次数, 该不该报）"""
        state: dict = {}
        out = []
        for k in keys:
            state, n = dl.bump(state, k)
            out.append((n, dl.should_warn(k, n)))
        return state, out

    def test_five_in_a_row_warns_once(self):
        _, steps = self._run([self.A] * 5)
        self.assertEqual([n for n, _ in steps], [1, 2, 3, 4, 5])
        self.assertEqual([w for _, w in steps], [False, False, False, False, True])

    def test_interleaved_never_warns(self):
        """交替编辑两个文件 —— 正常开发形态，一次都不该报"""
        _, steps = self._run([self.A, self.B] * 6)
        self.assertTrue(all(n == 1 for n, _ in steps))
        self.assertFalse(any(w for _, w in steps))

    def test_other_file_resets_streak(self):
        """跨波次回来改同一个注册入口：中间改过别的，计数清零"""
        _, steps = self._run([self.A] * 4 + [self.B] + [self.A] * 4)
        self.assertFalse(any(w for _, w in steps))
        self.assertEqual([n for n, _ in steps], [1, 2, 3, 4, 1, 1, 2, 3, 4])

    def test_repeat_warns_every_three(self):
        _, steps = self._run([self.A] * 12)
        warned = [n for n, w in steps if w]
        self.assertEqual(warned, [5, 8, 11])

    def test_doc_threshold_is_looser(self):
        """文档是边做边补的，连续 8 次才提"""
        _, steps = self._run([self.DOC] * 8)
        warned = [n for n, w in steps if w]
        self.assertEqual(warned, [8])
        self.assertTrue(dl.is_doc(self.DOC))
        self.assertFalse(dl.is_doc(self.A))

    def test_cumulative_counts_kept_for_stop_check(self):
        """累计次数照旧存着（stop-check 读同一个文件），连续状态另放一个键"""
        state, _ = self._run([self.A, self.B, self.A])
        self.assertEqual(state[self.A], 2)
        self.assertEqual(state[self.B], 1)
        self.assertEqual(state[dl.STREAK_KEY], {"file": self.A, "n": 1})
        # stop-check 用 isinstance(v, int) 过滤，连续状态这个 dict 天然被跳过
        self.assertFalse(isinstance(state[dl.STREAK_KEY], int))

    def test_bump_does_not_mutate_input(self):
        base: dict = {}
        state, _ = dl.bump(base, self.A)
        self.assertEqual(base, {})
        self.assertEqual(state[self.A], 1)

    def test_broken_state_recovers(self):
        """缓存文件坏了（不是 dict / 值不是数）也要能接着数，不能抛"""
        state, n = dl.bump({self.A: "坏了"}, self.A)  # type: ignore[dict-item]
        self.assertEqual(n, 1)
        self.assertEqual(state[self.A], 1)

    def test_warn_text_is_third_person(self):
        """文案是事实陈述，不出现祈使口吻"""
        text = dl.warn_text(self.A, 5)
        self.assertIn("连续 5 次", text)
        self.assertIn("Move.cs", text)
        for bad in ("你必须", "请立即", "停下来！"):
            self.assertNotIn(bad, text)


class EmitDedupe(unittest.TestCase):
    """_hook_common：同会话同提示只说一次，key 是提示文本"""

    def test_same_text_once(self):
        sid = hookenv.new_sid()
        self.assertTrue(common.should_emit(sid, "同一句话", "t"))
        self.assertFalse(common.should_emit(sid, "同一句话", "t"))

    def test_different_text_emits(self):
        sid = hookenv.new_sid()
        self.assertTrue(common.should_emit(sid, "第一句", "t"))
        self.assertTrue(common.should_emit(sid, "第二句", "t"))

    def test_other_session_emits(self):
        text = "跨会话的同一句话"
        self.assertTrue(common.should_emit(hookenv.new_sid(), text, "t"))
        self.assertTrue(common.should_emit(hookenv.new_sid(), text, "t"))

    def test_tag_separates(self):
        sid = hookenv.new_sid()
        self.assertTrue(common.should_emit(sid, "同文本不同来源", "a"))
        self.assertTrue(common.should_emit(sid, "同文本不同来源", "b"))

    def test_key_is_text_not_filename(self):
        """同一个文件的另一条提示照样要说 —— 按文件名去重会把它吃掉"""
        sid = hookenv.new_sid()
        self.assertTrue(common.should_emit(sid, "【知识路由】编辑 a.cs：规则 X", "kr"))
        self.assertTrue(common.should_emit(sid, "【知识路由】编辑 a.cs：规则 X · 模块 guide", "kr"))

    def test_fingerprint_stable(self):
        self.assertEqual(common.fingerprint("abc", "t"), common.fingerprint("abc", "t"))
        self.assertNotEqual(common.fingerprint("abc", "t"), common.fingerprint("abd", "t"))


@unittest.skipIf(hookenv.node_exe() is None, "没装 node")
class GitSubcommandParse(unittest.TestCase):
    """guard.js gitSubcommands：跳过全局选项取子命令；复合命令逐个识别"""

    def _parse(self, cmd):
        return hookenv.call_js("guard", "gitSubcommands", cmd)

    def _subs(self, cmd):
        return [c["sub"] for c in self._parse(cmd)]

    def test_skips_global_options(self):
        for cmd in ("git -C D:/x commit", "git -c user.name=x commit", "git --no-pager commit",
                    "git -P commit", "git --literal-pathspecs commit", "git --git-dir=D:/x/.git commit",
                    "git --work-tree D:/x commit", "git --namespace n --bare commit"):
            with self.subTest(cmd=cmd):
                self.assertEqual(self._subs(cmd), ["commit"])

    def test_stacked_options_and_quoted_values(self):
        """-C 路径带引号含空格、-c 值里带引号，多个选项叠着写"""
        cmd = 'git -C "D:/a b" -c "user.name=A B" --no-pager -C sub -c core.x="1 2" commit -m "x y"'
        self.assertEqual(self._parse(cmd),
                         [{"sub": "commit", "args": ["-m", "x y"], "dirs": ["D:/a b", "sub"]}])

    def test_compound_each_call(self):
        cmd = "git -C D:/x add a && git -C D:/x commit -m y; git -C D:/x push | cat\ngit -C D:/x status"
        self.assertEqual(self._subs(cmd), ["add", "commit", "push", "status"])

    def test_heredoc_message_is_not_args(self):
        """heredoc 正文不算 commit 的参数（署名检查另按整条命令查）"""
        cmd = "git -C D:/x commit -F - <<'EOF'\nfix: x\nEOF"
        self.assertEqual(self._parse(cmd), [{"sub": "commit", "args": ["-F", "-"], "dirs": ["D:/x"]}])

    def test_alias_defined_by_c(self):
        self.assertEqual(self._subs("git -c alias.ci=commit ci -m y"), ["commit"])
        self.assertIn("commit", self._subs("git -c 'alias.x=!git commit -m y' x"))

    def test_wrapped_and_substituted(self):
        """包在 bash -c 引号里、路径用 $( ) 算出来的，照样认"""
        self.assertEqual(self._subs('bash -c "git -C \'D:/a b\' commit -m y"'), ["commit"])
        self.assertEqual(self._subs("git -C $(pwd) commit -m y"), ["commit"])
        self.assertEqual(self._subs('git -C "$(pwd)" push'), ["push"])

    def test_mention_in_quotes_counts(self):
        """口径沿用旧整串正则：引号里提到的 git commit 也算（宁可多问一次）"""
        self.assertEqual(self._subs('echo "git commit"'), ["commit"])

    def test_not_git(self):
        for cmd in ("", "gitk --all", "echo commit", "ls .gitignore", "python x.py commit"):
            with self.subTest(cmd=cmd):
                self.assertEqual(self._parse(cmd), [])

    def test_unterminated_quote_does_not_throw(self):
        """引号没闭合：不抛、识别不出子命令（bash 自己也会报语法错，不构成绕过）"""
        self.assertEqual(self._parse('git -C "D:/x commit'), [])


@unittest.skipIf(hookenv.node_exe() is None, "没装 node")
class GitMessageFile(unittest.TestCase):
    """guard.js：commit -F / --file 给的提交信息文件也查署名；读不到、太大、编码坏都落回 ask"""

    SIG = "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"

    @classmethod
    def setUpClass(cls):
        cls.tmp = Path(tempfile.mkdtemp(prefix="guard-msgfile-"))
        (cls.tmp / "sub" / "inner").mkdir(parents=True)
        cls.other = cls.tmp / "other"
        cls.other.mkdir()
        files = {
            "signed.txt": "fix(harness): x\n\n%s\n" % cls.SIG,
            "clean.txt": "fix(harness): x\n",
            "sub/inner/signed.txt": "fix: x\n\n%s\n" % cls.SIG,
            # 署名在 64 KB 之后：只读前 64 KB，看不到 → ask（fail-open 的边界）
            "tail.txt": "a" * (70 * 1024) + "\n" + cls.SIG,
            # 署名在开头、64 KB 截断处切在中文字符中间：不能因为截断当成坏编码
            "cjk.txt": cls.SIG + "\n" + "中" * 30000,
        }
        for rel, text in files.items():
            (cls.tmp / rel).write_text(text, encoding="utf-8")
        (cls.tmp / "bad.txt").write_bytes(b"\xff\xfe\xfa " + cls.SIG.encode("ascii"))

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(cls.tmp, ignore_errors=True)

    def _p(self, rel=""):
        return (self.tmp / rel).as_posix() if rel else self.tmp.as_posix()

    def _decision(self, cmd, cwd=None):
        v = hookenv.call_js("guard", "gitVerdict", cmd, cwd)
        return v and v["decision"]

    def test_option_forms(self):
        files = lambda args: hookenv.call_js("guard", "commitMessageFiles", args)  # noqa: E731
        for args, want in ((["-F", "m.txt"], ["m.txt"]), (["-Fm.txt"], ["m.txt"]),
                           (["-aF", "m.txt"], ["m.txt"]), (["--file", "m.txt"], ["m.txt"]),
                           (["--file=m.txt"], ["m.txt"]), (["--fil=m.txt"], ["m.txt"]),
                           (["-F", "m.txt", "--", "a.cs"], ["m.txt"]),
                           (["-F", "-"], []), (["-m", "-F x"], []), (["-am", "-F"], []),
                           (["--", "-F", "m.txt"], [])):
            with self.subTest(args=args):
                self.assertEqual(files(args), want)

    def test_c_stacks_like_git(self):
        """多个 -C 依次叠加；绝对路径的 -C 重新起算；空串不改目录"""
        resolve = lambda p, dirs: hookenv.call_js("guard", "resolveGitPath", p, dirs, self._p())  # noqa: E731
        same = lambda a, b: self.assertEqual(os.path.normcase(os.path.normpath(a)),  # noqa: E731
                                             os.path.normcase(os.path.normpath(b)))
        same(resolve("m.txt", ["sub", "inner"]), self.tmp / "sub" / "inner" / "m.txt")
        same(resolve("m.txt", ["sub", self.other.as_posix()]), self.other / "m.txt")
        same(resolve("m.txt", ["", "sub"]), self.tmp / "sub" / "m.txt")

    def test_signed_file_denied(self):
        for cmd in ('git commit -F "%s"' % self._p("signed.txt"),
                    'git commit --file="%s" -- a.cs' % self._p("signed.txt"),
                    'git -C D:/x commit -aF "%s"' % self._p("signed.txt")):
            with self.subTest(cmd=cmd):
                self.assertEqual(self._decision(cmd), "deny")

    def test_clean_and_missing_file_ask(self):
        self.assertEqual(self._decision('git commit -F "%s"' % self._p("clean.txt")), "ask")
        self.assertEqual(self._decision('git commit -F "%s"' % self._p("不存在.txt")), "ask")

    def test_relative_path_follows_c(self):
        self.assertEqual(self._decision('git -C "%s" -C sub -C inner commit -F signed.txt' % self._p()), "deny")
        self.assertEqual(self._decision('git -C "%s" commit -F clean.txt' % self._p()), "ask")

    def test_relative_path_follows_cwd(self):
        """没有 -C 按钩子输入的 cwd 解析；没给 cwd 按工程根（那里没有这个文件 → ask）"""
        self.assertEqual(self._decision("git commit -F signed.txt", self._p()), "deny")
        self.assertEqual(self._decision("git commit -F signed.txt"), "ask")

    def test_fail_open_boundaries(self):
        self.assertEqual(self._decision('git commit -F "%s"' % self._p("bad.txt")), "ask", "编码坏")
        self.assertEqual(self._decision('git commit -F "%s"' % self._p("tail.txt")), "ask", "署名在 64 KB 之后")
        self.assertEqual(self._decision('git commit -F "%s"' % self._p("cjk.txt")), "deny", "截断切在多字节字符中间")
        self.assertEqual(self._decision('git commit -F "%s"' % self._p("sub")), "ask", "目录不是普通文件")

    @unittest.skipUnless(os.name == "nt", "Git Bash 路径形态只在 Windows 上换算")
    def test_git_bash_drive_path(self):
        posix = self._p("signed.txt")   # 形如 C:/Users/…
        msys = "/" + posix[0].lower() + posix[2:]
        self.assertEqual(self._decision('git commit -F "%s"' % msys), "deny")


if __name__ == "__main__":
    unittest.main()
