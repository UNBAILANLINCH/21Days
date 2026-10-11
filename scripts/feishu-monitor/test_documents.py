# 执行载体：python -m unittest discover；状态锚点：退出码；同步工具退场后删除。
# 新建原因：现有素材测试不覆盖策划附件与受保护的人工转写。
import hashlib
import pathlib
import tempfile
import unittest
from unittest.mock import patch
import documents

class DocumentTests(unittest.TestCase):
    def download(self, args, cwd):
        path = pathlib.Path(cwd) / "asset.docx"
        path.write_bytes(b"original")
        return {"saved_path": str(path)}

    def snapshots(self):
        return {"document": {"kind": "document", "label": "正文", "value": "最新策划"},
                "sheet:t:s": {"kind": "sheet", "label": "需求", "value": {"A1": "新需求"}},
                "board:b": {"kind": "board", "label": "流程", "value": {"a": {"text": "起点"}}},
                "media:m": {"kind": "media", "label": "重要角色与势力.docx", "value": hashlib.sha256(b"original").hexdigest()}}

    def test_only_new_planning_files_are_uploaded(self):
        snapshots = self.snapshots()
        snapshots["media:old"] = {"kind": "media", "label": "存量大纲.docx", "value": "old"}
        snapshots["media:picture"] = {"kind": "media", "label": "参考.png", "value": "picture"}
        snapshots["media:config"] = {"kind": "media", "label": "资产配置.xlsx", "value": "config"}
        files = documents.build_files(snapshots, "private-source-url", "user", self.download, ["old"])
        self.assertEqual({documents.ROOT + "media_m.docx", documents.ROOT + "README.md"}, set(files))
        index = files[documents.ROOT + "README.md"].decode("utf-8")
        self.assertNotIn("最新策划", index)
        self.assertNotIn("private-source-url", index)

    def test_no_new_planning_document_does_not_publish(self):
        with patch.object(documents.assets, "prepare") as prepare:
            self.assertEqual(({"old": "hash"}, None), documents.publish({"baseline_tokens": ["m"]}, pathlib.Path("."), {"old": "hash"}, self.snapshots(), "url", "user", self.download))
            prepare.assert_not_called()

    def test_missing_baseline_does_not_upload_existing_documents(self):
        with self.assertRaisesRegex(RuntimeError, "基线未配置"):
            documents.publish({}, pathlib.Path("."), {}, self.snapshots(), "url", "user", self.download)

    def test_publish_preserves_manual_documents_and_is_idempotent(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = pathlib.Path(temporary)
            protected = directory / "docs/design/spotlight/06_补交.md"
            protected.parent.mkdir(parents=True)
            protected.write_text("人工正文", encoding="utf-8")
            def git(repo, *args):
                if "--name-only" in args:
                    return documents.ROOT + "README.md"
                if args[0] == "rev-parse":
                    return "abc"
                if args[0] == "ls-remote":
                    return "abc\trefs/heads/codex/feishu-sync"
                return ""
            with patch.object(documents.assets, "prepare", return_value=directory) as prepare, patch.object(documents.assets, "git", side_effect=git):
                hashes, commit = documents.publish({"baseline_tokens": []}, directory, {}, self.snapshots(), "url", "bot", self.download)
                self.assertEqual("abc", commit)
                self.assertEqual((hashes, None), documents.publish({"baseline_tokens": []}, directory, hashes, self.snapshots(), "url", "bot", self.download))
                prepare.assert_called_once()
            self.assertEqual("人工正文", protected.read_text(encoding="utf-8"))
            self.assertIn("重要角色与势力.docx", (directory / documents.ROOT / "README.md").read_text(encoding="utf-8"))
            self.assertTrue(all(name.startswith(documents.ROOT) for name in hashes))

    def test_attachment_integrity_and_download_boundary(self):
        snapshots = self.snapshots()
        snapshots["media:m"] = {"kind": "media", "label": "附件.docx", "value": hashlib.sha256(b"original").hexdigest()}
        def download(args, cwd):
            path = pathlib.Path(cwd) / "asset.docx"
            path.write_bytes(b"original")
            return {"saved_path": str(path)}
        files = documents.build_files(snapshots, "url", "bot", download)
        self.assertEqual(b"original", files[documents.ROOT + "media_m.docx"])
        snapshots["media:m"]["value"] = "changed"
        with self.assertRaisesRegex(RuntimeError, "发生变化"):
            documents.build_files(snapshots, "url", "bot", download)
        with self.assertRaisesRegex(RuntimeError, "越界"):
            documents.build_files(snapshots, "url", "bot", lambda args, cwd: {"saved_path": str(pathlib.Path(cwd).parent / "outside.docx")})

    def test_push_failure_does_not_return_success(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = pathlib.Path(temporary)
            def git(repo, *args):
                if args[0] == "push":
                    raise RuntimeError("推送失败")
                return ""
            with patch.object(documents.assets, "prepare", return_value=directory), patch.object(documents.assets, "git", side_effect=git):
                with self.assertRaisesRegex(RuntimeError, "推送失败"):
                    documents.publish({"baseline_tokens": []}, directory, {}, self.snapshots(), "url", "bot", self.download)

if __name__ == "__main__":
    unittest.main()
