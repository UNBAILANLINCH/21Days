"""将文策通俗表编成既有 Dialogue/Narrative 源数据补丁；不修改源工作簿。

现有 Luban 只读运行时 JSON，没有通俗表入口，因此只补一层离线转换，
不新增运行时解释器。输出交给 apply_patch，再使用 gen-tables.ps1 生成。
"""
import argparse
import json
import sys
from pathlib import Path

import openpyxl


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
        for row in pending:
            nodes.append(dict(id=f"r{row[0]:04d}", revision=1, kind="Line", speaker="",
                              speakerName=row[3] if row[3] != "文书" else "", side="Left",
                              expression="", clearOther=False, text=row[5], next="", outcome="",
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
            # 同一段说书梦境内的地点改变不要求玩家完成跨场景操作。
            if not (location.startswith("序章上半") and scene.startswith("序章上半")):
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
    assert sum(len(t["nodes"]) - 1 for t in trees) == sum(r["disposition"] == "dialogue" for r in records)
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
        assert [n["text"] for t in trees for n in t["nodes"] if n["kind"] == "Line"] == ["开场", "战后"]
        assert [s["kind"] for s in story["stages"]] == ["Dialogue", "WaitAction", "Dialogue", "End"]
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
