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
    def test_sheet_attachment_change_shows_role_file_and_only_changed_fields(self):
        border = {"bottom": {"color": "rgb(191, 191, 191)", "style": "solid", "weight": "thin"}}
        before = {"A3": {"value": "妻子"}, "D1": {"value": "请在这一列插附件"},
                  "D3": {"border_styles": border}}
        after = dict(before, D3={"border_styles": border, "value": "妻子-何素.zip",
                                "rich_text": [{"type": "attachment", "text": "妻子-何素.zip",
                                               "attachment_token": "newtoken", "file_size": 228946}]})
        old = {"sheet": {"kind": "sheet", "label": "主线角色", "value": before}}
        new = {"sheet": {"kind": "sheet", "label": "主线角色", "value": after}}
        message = "\n".join(monitor.change_lines(old, new))
        self.assertIn("妻子 / 附件（D3）", message)
        self.assertIn("（空） → 妻子-何素.zip", message)
        self.assertIn("228946 字节", message)
        self.assertNotIn("border_styles", message)
        self.assertNotIn("newtoken", message)

    def test_same_named_attachment_replacement_is_visible(self):
        before = [{"type": "attachment", "text": "角色.zip", "attachment_token": "old"}]
        after = [{"type": "attachment", "text": "角色.zip", "attachment_token": "new"}]
        left, right = monitor.change_text(before, after)
        self.assertIn("角色.zip", left)
        self.assertIn("引用或属性已变化", right)
        self.assertNotEqual(left, right)

    def test_changed_colours_and_formula_remain_visible(self):
        changes = list(monitor.changed_fields(
            {"cell_background_color": "white", "formula": "=A1", "value": 1},
            {"cell_background_color": "red", "formula": "=B1", "value": 1}))
        self.assertEqual([("formula", "=A1", "=B1"), ("cell_background_color", "white", "red")], changes)
        self.assertEqual("底色", monitor.field_label("cell_background_color"))
        self.assertEqual("0", monitor.compact(0))
        self.assertEqual("false", monitor.compact(False))

    def test_long_text_excerpt_centres_on_the_actual_change(self):
        left, right = monitor.change_text("相同前文" * 120 + "旧要求", "相同前文" * 120 + "新要求")
        self.assertIn("旧要求", left)
        self.assertIn("新要求", right)
        self.assertLess(len(left), 325)
        self.assertTrue(left.startswith("…"))

    def test_duplicate_blank_lines_do_not_notify_but_baseline_advances(self):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "state.json"
            old = {"document": {"kind": "document", "label": "正文", "value": "# 美术需求\n\n拆件"}}
            new = {"document": {"kind": "document", "label": "正文", "value": "# 美术需求\n\n\n\n拆件"}}
            monitor.save(path, {"initialized": True, "snapshots": old})
            with patch.object(monitor, "collect", return_value=(new, [], 730, ["document"])), patch.object(monitor, "send") as send:
                self.assertFalse(monitor.run({"document_url": "url"}, path))
            send.assert_not_called()
            self.assertEqual(new, json.loads(path.read_text(encoding="utf-8"))["snapshots"])
            self.assertEqual(730, json.loads(path.read_text(encoding="utf-8"))["revision"])

    def test_document_change_shows_heading_and_before_after(self):
        old = {"document": {"kind": "document", "label": "正文", "value": "# 美术需求\n\n512px"}}
        new = {"document": {"kind": "document", "label": "正文", "value": "# 美术需求\n\n1080px"}}
        message = "\n".join(monitor.change_lines(old, new))
        self.assertIn("• 美术需求", message)
        self.assertIn("之前：512px", message)
        self.assertIn("现在：1080px", message)
        self.assertNotIn("@@", message)
        self.assertNotEqual(monitor.document_lines("段落\n\n下一段"), monitor.document_lines("段落\n下一段"))
        self.assertEqual("```\na\n\n\nb\n```".splitlines(),
                         monitor.document_lines("```\na\n\n\nb\n```"))

    def test_notification_explains_why_no_pr_was_triggered(self):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "state.json"
            old = {"document": {"kind": "document", "label": "正文", "value": "旧需求"}}
            new = {"document": {"kind": "document", "label": "正文", "value": "新需求"}}
            monitor.save(path, {"initialized": True, "snapshots": old})
            with patch.object(monitor, "collect", return_value=(new, [], 730, ["document"])), patch.object(monitor, "send") as send:
                self.assertFalse(monitor.run({"document_url": "url"}, path))
            self.assertIn("没有文件推送，不触发新 PR", send.call_args[0][1])

    def test_board_message_omits_unchanged_node_properties(self):
        old = {"board": {"kind": "board", "label": "序章画板", "value": {
            "node": {"id": "node", "border_styles": "冗长的边框数据", "text": "旧剧情"}}}}
        new = {"board": {"kind": "board", "label": "序章画板", "value": {
            "node": {"id": "node", "border_styles": "冗长的边框数据", "text": "新剧情"}}}}
        message = "\n".join(monitor.change_lines(old, new))
        self.assertIn("文字：旧剧情 → 新剧情", message)
        self.assertNotIn("border_styles", message)

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
            self.assertNotIn("没有文件推送", send.call_args[0][1])
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
