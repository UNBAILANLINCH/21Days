"""将文策通俗表编成既有 Dialogue/Narrative 源数据补丁；不修改源工作簿。

现有 Luban 只读运行时 JSON，没有通俗表入口，因此只补一层离线转换，
不新增运行时解释器。输出交给 apply_patch，再使用 gen-tables.ps1 生成。
"""
import argparse
import json
import sys
from pathlib import Path

import openpyxl

# 已接入/保留兼容的场景边界；地点文本变化本身不构成玩家任务。
# 83、129 是旧档叙事衔接 ID，运行时自动续播；171 才等待玩家按键入镜。
ENTRY_ROWS = frozenset((26, 83, 129, 171, 188, 198, 215, 293, 339, 384, 397, 427, 435, 469))


def compile_rows(rows, occupied_ids):
    trees, stages, records, pending = [], [], [], []
    scene = None

    def stage(key, kind, payload="", outcome=""):
        stages.append(dict(id=key, kind=kind, payload=payload, allowEncounter=False,
                           issueRequest=False, requiredParts=[], battleResults=[],
                           anyOf=[], exits=[], setFlags=[], outcome=outcome))

    def flush():
        if not pending:
            return
        tree_id = 21000 + len(trees)
        if tree_id in occupied_ids:
            raise ValueError(f"对白编号已被占用：{tree_id}")
        nodes = []
        for index, row in enumerate(pending):
            if row[3] == "文书" and index > 0 and pending[index - 1][3] == "文书":
                nodes[-1]["text"] += "\n" + row[5]
                nodes[-1]["revision"] = 2
                continue
            speaker = "lunhuitang_guard" if row[3] == "值守" and "轮回堂" in row[1] else "wang_luye" if row[3] == "王录业" else "storyteller" if row[3] == "说书人" and row[1].startswith("序章") else ""
            expression = ("serious" if row[0] in (164, 166, 169) else "talk") if speaker == "wang_luye" else "talk" if speaker == "lunhuitang_guard" else ""
            nodes.append(dict(id=f"r{row[0]:04d}", revision=1, kind="Line", speaker=speaker,
                              speakerName=row[3] if row[3] != "文书" else "", side="Left",
                              expression=expression, clearOther=False, text=row[5], next="", outcome="",
                              blocking=True, performance="", choices=[]))
        for i, node in enumerate(nodes):
            node["next"] = nodes[i + 1]["id"] if i + 1 < len(nodes) else "end"
        nodes.append(dict(id="end", revision=1, kind="End", speaker="", speakerName="",
                          side="Left", expression="", clearOther=False, text="", next="",
                          outcome="Done", blocking=True, performance="", choices=[]))
        trees.append(dict(id=tree_id, entry=nodes[0]["id"], nodes=nodes))
        stage(f"talk_{tree_id}", "Dialogue", str(tree_id))
        pending.clear()

    for row in rows:
        number, location, time, who, sound, text = row[:6]
        if not isinstance(number, int) or not location or not who or not text:
            raise ValueError(f"不完整的剧情行：{number}")
        if row[8] != "否" or row[10] != "否":
            raise ValueError(f"第 {number} 行包含选项/分支，需先明确选项结构")
        if scene is not None and location != scene:
            flush()
            if number in ENTRY_ROWS:
                stage(f"enter_r{number:04d}", "WaitAction", location)
        scene = location
        note = who in ("演出说明（非字幕）", "策划说明（非字幕）")
        barrier = note and any(k in text for k in (
            "战斗教学", "五店场景", "翻页查看", "找合成道具", "需先将其击败", "抄录完成后"))
        if barrier:
            flush()
            stage(f"action_r{number:04d}", "WaitAction", f"writer_row_{number}")
        elif not note:
            pending.append(row)
        records.append(dict(row=number, scene=location, time=time, speaker=who, text=text,
                            sound=sound, disposition="wait" if barrier else "direction" if note else "dialogue",
                            portrait=row[6:8], task=row[12:17], item=row[17]))
    flush()
    stage("end", "End", outcome="CurrentContentEnd")
    for i, current in enumerate(stages[:-1]):
        current["exits"] = [dict(result="Done" if current["kind"] == "Dialogue" else "Success",
                                 next=stages[i + 1]["id"])]
    assert len(records) == len(rows)
    assert "\n".join(n["text"] for t in trees for n in t["nodes"] if n["kind"] == "Line") == "\n".join(
        r["text"] for r in records if r["disposition"] == "dialogue")
    assert len({s["id"] for s in stages}) == len(stages)
    return trees, dict(id="spotlight_current", entry=stages[0]["id"], stages=stages), records


def patch_file(path, content):
    if Path(path).exists():
        old = Path(path).read_text(encoding="utf-8")
        return f"*** Update File: {path}\n@@\n" + "\n".join("-" + x for x in old.splitlines()) + "\n" + "\n".join("+" + x for x in content.splitlines()) + "\n"
    return f"*** Add File: {path}\n" + "\n".join("+" + x for x in content.splitlines()) + "\n"


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path, nargs="?")
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--only")
    parser.add_argument("--list", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        def row(i, who, text):
            return [i, "序章上半｜测试", "夜", who, "无", text, "待确认", "待确认",
                    "否", "不适用", "否", "顺序播放", "否", "不适用", "不适用", "不适用", "不适用", "无"]
        sample = [row(1, "说书人", "开场"), row(2, "演出说明（非字幕）", "转身"),
                  row(3, "策划说明（非字幕）", "战斗教学"), row(4, "崔皓", "战后")]
        trees, story, records = compile_rows(sample, set())
        assert trees[0]["nodes"][0]["speaker"] == "storyteller"
        assert trees[1]["nodes"][0]["speaker"] == ""
        assert [n["text"] for t in trees for n in t["nodes"] if n["kind"] == "Line"] == ["开场", "战后"]
        assert [s["kind"] for s in story["stages"]] == ["Dialogue", "WaitAction", "Dialogue", "End"]
        changed_scene = [row(1, "说书人", "前情"), row(2, "崔皓", "线索")]
        changed_scene[1][1] = "人间｜新的叙事地点"
        _, changed_story, _ = compile_rows(changed_scene, set())
        assert all(s["kind"] != "WaitAction" for s in changed_story["stages"])
        documents = [row(144, "文书", "案卷标题"), row(145, "文书", "正文"),
                     row(146, "文书", "已结"), row(147, "文书", "已结"), row(148, "王录业", "结过两次")]
        document_trees, _, document_records = compile_rows(documents, set())
        assert len(document_records) == 5
        assert document_trees[0]["nodes"][0]["text"] == "案卷标题\n正文\n已结\n已结"
        assert document_trees[0]["nodes"][0]["next"] == "r0148"
        assert document_trees[0]["nodes"][1]["speaker"] == "wang_luye"
        assert document_trees[0]["nodes"][1]["expression"] == "talk"
        stern, _, _ = compile_rows([row(166, "王录业", "这是大忌。")], set())
        assert stern[0]["nodes"][0]["expression"] == "serious"
        guard = row(175, "值守", "稀客啊。")
        guard[1] = "镜中长安-轮回堂"
        guard_trees, _, _ = compile_rows([guard], set())
        assert guard_trees[0]["nodes"][0]["speaker"] == "lunhuitang_guard"
        assert guard_trees[0]["nodes"][0]["expression"] == "talk"
        guard[1] = "另一处关卡"
        unrelated, _, _ = compile_rows([guard], set())
        assert unrelated[0]["nodes"][0]["speaker"] == ""
        for occupied, option in [({21000}, "否"), (set(), "是")]:
            sample[0][8] = option
            try:
                compile_rows(sample, occupied)
            except ValueError:
                pass
            else:
                raise AssertionError("应拒绝编号冲突和未定义选项")
        print("导入自检通过：对白保真、说明分流、玩法等待、编号冲突和选项拒绝。")
        return
    if args.source is None:
        parser.error("需要提供源工作簿")
    workbook = openpyxl.load_workbook(args.source, read_only=True, data_only=False)
    sheet = workbook["通俗用法"]
    matrix = list(sheet.iter_rows(values_only=True))
    assert matrix[0][3] == "说话角色（人物/旁白）" and matrix[0][5] == "说啥"
    rows = [list(r[:18]) for r in matrix[1:] if r[0] is not None]
    assert [r[0] for r in rows] == list(range(1, len(rows) + 1))
    story_path = Path("Tables/Data/narrative/spotlight_current.json")
    owned = set()
    if story_path.exists():
        owned = {int(s["payload"]) for s in json.loads(story_path.read_text(encoding="utf-8"))["stages"] if s["kind"] == "Dialogue"}
    occupied = {json.loads(p.read_text(encoding="utf-8-sig"))["id"] for p in Path("Tables/Data/dialogue").glob("*.json")} - owned
    trees, story, records = compile_rows(rows, occupied)
    files = {f"Tables/Data/dialogue/{t['id']}.json": t for t in trees}
    files["Tables/Data/narrative/spotlight_current.json"] = story
    files["docs/design/spotlight/writer-import.json"] = dict(source=args.source.name, rows=len(rows),
        dialogueCount=len(trees), records=records, waits=[s for s in story["stages"] if s["kind"] == "WaitAction"])
    if args.list:
        print(json.dumps(list(files)))
        return
    print("*** Begin Patch")
    for path, data in files.items():
        if args.only and path != args.only:
            continue
        print(patch_file(path, json.dumps(data, ensure_ascii=False, indent=2) + "\n"), end="")
    print("*** End Patch")


if __name__ == "__main__":
    main()
