# 执行载体：部署前 python3 -m unittest discover；状态锚点：测试退出码；监测退场后随工具删除。
import importlib.util
import json
import pathlib
import tempfile
import unittest
from unittest.mock import patch
spec = importlib.util.spec_from_file_location("monitor", str(pathlib.Path(__file__).with_name("monitor.py")))
monitor = importlib.util.module_from_spec(spec)
spec.loader.exec_module(monitor)

class MonitorTests(unittest.TestCase):
    def test_document_sync_failure_is_retried_without_new_source_changes(self):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "state.json"
            doc = {"document": {"kind": "document", "label": "正文", "value": "正文"}}
            monitor.save(path, {"initialized": True, "snapshots": doc, "document_hashes": {"old": "hash"}})
            config = {"document_url": "url", "document_sync": {"repository": "url"}}
            with patch.object(monitor, "collect", return_value=(doc, [], 2, ["document"])), patch.object(monitor, "send"), patch.object(monitor.documents, "publish", side_effect=RuntimeError("推送失败")):
                self.assertTrue(monitor.run(config, path))
            self.assertEqual({"old": "hash"}, json.loads(path.read_text(encoding="utf-8"))["document_hashes"])
            with patch.object(monitor, "collect", return_value=(doc, [], 2, ["document"])), patch.object(monitor, "send") as send, patch.object(monitor.documents, "publish", return_value=({"new": "hash"}, "commit")):
                self.assertFalse(monitor.run(config, path))
            self.assertIn("新增策划附件已推送", send.call_args[0][1])
            self.assertEqual("commit", json.loads(path.read_text(encoding="utf-8"))["document_commit"])

    def test_incomplete_source_does_not_publish_documents(self):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "state.json"
            doc = {"document": {"kind": "document", "label": "正文", "value": "正文"}}
            with patch.object(monitor, "collect", return_value=(doc, ["表: 读取失败"], 2, ["document"])), patch.object(monitor, "send"), patch.object(monitor.documents, "publish") as publish:
                self.assertTrue(monitor.run({"document_url": "url", "document_sync": {"repository": "url"}}, path))
            publish.assert_not_called()

    def test_asset_failure_keeps_push_baseline_but_still_notifies(self):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "state.json"
            doc = {"document": {"kind": "document", "label": "正文", "value": "正文"}}
            monitor.save(path, {"initialized": True, "snapshots": doc, "asset_hashes": {"asset": "old"}})
            with patch.object(monitor, "collect", return_value=(doc, [], 2, ["document"])), patch.object(monitor.assets, "publish", side_effect=RuntimeError("推送失败")), patch.object(monitor, "send") as send:
                self.assertTrue(monitor.run({"document_url": "url", "asset_sync": {"repository": "url"}}, path))
            self.assertEqual({"asset": "old"}, json.loads(path.read_text(encoding="utf-8"))["asset_hashes"])
            self.assertIn("推送失败", send.call_args[0][1])

    def test_board_order_does_not_notify(self):
        left = monitor.board_value({"nodes": [{"id": "a", "text": "甲"}, {"id": "b", "text": "乙"}]})
        right = monitor.board_value({"nodes": [{"id": "b", "text": "乙"}, {"id": "a", "text": "甲"}]})
        self.assertEqual(left, right)
        self.assertNotEqual(left, monitor.board_value({"nodes": [{"id": "a", "text": "丙"}, {"id": "b", "text": "乙"}]}))

    def test_truncated_sheet_is_rejected(self):
        with self.assertRaises(RuntimeError):
            monitor.sheet_value({"has_more": True, "ranges": []})

    def test_failed_notification_keeps_previous_state(self):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "state.json"
            old = {"initialized": True, "snapshots": {"document": {"kind": "document", "label": "正文", "value": "旧"}}}
            monitor.save(path, old)
            before = path.read_bytes()
            new = {"document": {"kind": "document", "label": "正文", "value": "新"}}
            with patch.object(monitor, "collect", return_value=(new, [], 2, ["document"])), patch.object(monitor, "send", side_effect=RuntimeError("发送失败")):
                with self.assertRaises(RuntimeError):
                    monitor.run({"document_url": "https://example.test/wiki/test"}, path)
            self.assertEqual(before, path.read_bytes())

    def test_resource_recovered_first_read_is_baseline(self):
        resource = {"media:a": {"kind": "media", "label": "图", "value": "hash"}}
        self.assertEqual([], monitor.change_lines({}, resource, ["media:a"]))
        self.assertEqual(["新增：图"], monitor.change_lines({}, resource))

    def test_sheet_permission_failure_does_not_lose_document(self):
        doc = {"document": {"document_id": "doc1", "revision_id": 3,
                           "content": '<sheet sheet-id="s1" token="token1"></sheet>'}}
        with patch.object(monitor, "invoke", side_effect=[doc, RuntimeError("authorization")]):
            snapshots, errors, revision, known = monitor.collect({"document_url": "url", "document_id": "doc1"})
        self.assertIn("document", snapshots)
        self.assertEqual(3, revision)
        self.assertIn("sheet:token1:s1", known)
        self.assertTrue(errors)

if __name__ == "__main__":
    unittest.main()
