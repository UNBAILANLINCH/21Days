# 执行载体：部署前 unittest discover；状态锚点：测试退出码；资产同步退场后删除。
import hashlib
import pathlib
import tempfile
import unittest
import zipfile
from unittest.mock import patch
import assets

TARGET = "Assets/_Project/Art/Sprites/UI/Title/test.png"
META = b"fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\n"
PNG = b"\x89PNG\r\n\x1a\nfixture"

def fixture(directory):
    path = directory / TARGET
    path.parent.mkdir(parents=True)
    path.write_bytes(b"old")
    path.with_name(path.name + ".meta").write_bytes(META)
    for parent in path.parents:
        if parent == directory / "Assets" or parent == directory:
            break
        parent.with_name(parent.name + ".meta").write_bytes(META)

class AssetTests(unittest.TestCase):
    def test_documents_scripts_and_traversal_are_rejected(self):
        for name in ["docs/design/spotlight/doc.md", "Assets/_Project/Art/doc.docx",
                     "Assets/_Project/Art/table.xlsx", "Assets/_Project/Art/script.py",
                     "Assets/_Project/Art/../secret.png", "Assets/_Project/Art/c:\\secret.png"]:
            with self.subTest(name=name), self.assertRaises(RuntimeError):
                assets.checked_target(name)

    def test_zip_reads_only_explicitly_bound_member(self):
        def invoke(args, cwd):
            path = pathlib.Path(cwd) / "asset.zip"
            with zipfile.ZipFile(str(path), "w") as archive:
                archive.writestr("parts/head.png", b"png")
                archive.writestr("design.md", b"private document")
            return {"saved_path": str(path)}
        self.assertEqual(b"png", assets.download({"token": "FileToken", "member": "parts/head.png"}, invoke))

    def test_missing_meta_or_changed_guid_blocks_before_writing(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = pathlib.Path(temporary)
            fixture(directory)
            with self.assertRaisesRegex(RuntimeError, "缺少 Unity 生成的 meta"):
                assets.validate_files(directory, {TARGET.replace("test.png", "new.png"): PNG})
            with self.assertRaisesRegex(RuntimeError, "禁止替换已有 meta"):
                assets.validate_files(directory, {TARGET + ".meta": META.replace(b"012345", b"123456")})
            self.assertEqual(b"old", (directory / TARGET).read_bytes())

    def test_document_renamed_as_png_is_not_uploaded(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = pathlib.Path(temporary)
            fixture(directory)
            with self.assertRaises(RuntimeError):
                assets.validate_files(directory, {TARGET: b"# Private design document"})

    def test_unchanged_asset_does_not_commit_or_push(self):
        hashes = {TARGET: hashlib.sha256(b"new").hexdigest()}
        with patch.object(assets, "download", return_value=b"new"), patch.object(assets, "prepare") as prepare:
            self.assertEqual((hashes, None), assets.publish({"bindings": [{"target": TARGET}]}, pathlib.Path("."), hashes, None))
            prepare.assert_not_called()

    def test_no_delivery_bindings_does_not_prepare_or_publish(self):
        with patch.object(assets, "prepare") as prepare:
            self.assertEqual(({}, None), assets.publish({"bindings": []}, pathlib.Path("."), {}, None))
            prepare.assert_not_called()

    def test_only_bound_assets_are_staged_and_only_sync_branch_is_pushed(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = pathlib.Path(temporary)
            fixture(directory)
            def git(root, *args):
                if "--name-only" in args:
                    return TARGET
                if args[0] == "rev-parse":
                    return "abc"
                if args[0] == "ls-remote":
                    return "abc\trefs/heads/" + assets.BRANCH
                return ""
            with patch.object(assets, "download", return_value=PNG), patch.object(assets, "prepare", return_value=directory), patch.object(assets, "git", side_effect=git) as command:
                hashes, sha = assets.publish({"bindings": [{"target": TARGET}]}, directory, {}, None)
            self.assertEqual("abc", sha)
            self.assertEqual(hashlib.sha256(PNG).hexdigest(), hashes[TARGET])
            command.assert_any_call(directory, "add", "--", TARGET)
            command.assert_any_call(directory, "push", "origin", "HEAD:refs/heads/codex/feishu-sync")
            self.assertEqual(META, (directory / (TARGET + ".meta")).read_bytes())

if __name__ == "__main__":
    unittest.main()
