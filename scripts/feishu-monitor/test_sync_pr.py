# 执行载体：部署前 unittest discover；状态锚点：测试退出码；删除工作流时一起删除。
# 新建原因：现有监测测试不覆盖 GitHub 工作流，使用本地 gh 替身验证创建和复用行为。
import os
import pathlib
import shutil
import subprocess
import tempfile
import unittest


class SyncPrTests(unittest.TestCase):
    def run_workflow(self, existing="", changed="2", failure=""):
        workflow = pathlib.Path(__file__).parents[2] / ".github/workflows/feishu-sync-pr.yml"
        lines = workflow.read_text(encoding="utf-8").split("        run: |\n", 1)[1].splitlines()
        script = "\n".join(line[10:] for line in lines)
        stub = '''gh() {
  if [ "$1 $2" = "pr list" ]; then
    [ "$FAILURE" != list ] || return 1
    printf '%s' "$EXISTING"
  elif [ "$1" = api ]; then
    [ "$FAILURE" != compare ] || return 1
    printf '%s' "$CHANGED"
  elif [ "$1 $2" = "pr create" ]; then
    [ "$FAILURE" != create ] || return 1
    echo created
  else
    return 1
  fi
}
'''
        bash = shutil.which("bash")
        if os.name == "nt":
            git = pathlib.Path(shutil.which("git"))
            bash = str(git.parents[1] / "bin/bash.exe")
        if not bash:
            self.fail("需要 bash 验证工作流")
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "check.sh"
            path.write_text(stub + script, encoding="utf-8")
            env = dict(os.environ, EXISTING=existing, CHANGED=changed,
                       FAILURE=failure, GH_REPO="example/21Days")
            return subprocess.run([bash, str(path)], env=env, stdout=subprocess.PIPE,
                                  stderr=subprocess.PIPE, universal_newlines=True,
                                  encoding="utf-8", timeout=10)

    def test_creates_pr_for_file_changes(self):
        result = self.run_workflow()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("created", result.stdout)

    def test_reuses_open_pr(self):
        result = self.run_workflow(existing="https://github.com/example/21Days/pull/1")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertNotIn("created", result.stdout)

    def test_skips_when_files_match_main(self):
        result = self.run_workflow(changed="0")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertNotIn("created", result.stdout)

    def test_api_and_creation_failures_do_not_claim_success(self):
        for failure in ("list", "compare", "create"):
            result = self.run_workflow(failure=failure)
            self.assertNotEqual(0, result.returncode)
            self.assertNotIn("created", result.stdout)


if __name__ == "__main__":
    unittest.main()
