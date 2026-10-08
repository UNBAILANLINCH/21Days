# 用途：合并冲突解完后，静态校验 Unity 场景 YAML 的引用完整性。
# 为什么新建：手解 scene 冲突的风险是 fileID 悬空或重复，Unity 只在打开场景时才报，代价高；
#   本脚本在提交前用纯文本规则跑一遍，能立刻定位到行号。
# 执行载体：人工运行 `python scripts/scene_ref_check.py <scene.unity>`；
#   状态锚点：末行打印 "OK" 或 "问题 N 条"；退场条件：Unity 侧打开场景无报错后即可删除。
import re
import sys
from collections import defaultdict

ANCHOR = re.compile(r"^--- !u!(\d+) &(\d+)(?: stripped)?\s*$")
REF = re.compile(r"\{fileID: (-?\d+)\}")

path = sys.argv[1] if len(sys.argv) > 1 else "Assets/Scenes/SampleScene.unity"
with open(path, "r", encoding="utf-8") as handle:
    lines = handle.read().split("\n")

if any(line.startswith(("<<<<<<<", "=======", ">>>>>>>")) for line in lines):
    print("FAIL 仍有冲突标记")
    sys.exit(1)

defined = {}
dupes = defaultdict(list)
for index, line in enumerate(lines, start=1):
    match = ANCHOR.match(line)
    if match:
        file_id = int(match.group(2))
        if file_id in defined:
            dupes[file_id].append(index)
        else:
            defined[file_id] = index

# 0 = null 引用，合法；负数与正数都必须落在已定义锚点上。
dangling = []
for index, line in enumerate(lines, start=1):
    for raw in REF.findall(line):
        file_id = int(raw)
        if file_id == 0:
            continue
        if file_id not in defined:
            dangling.append((index, file_id, line.strip()))

print(f"文件: {path}")
print(f"文档锚点: {len(defined)} 个；重复 id: {len(dupes)} 个；悬空引用: {len(dangling)} 条")

for file_id, where in dupes.items():
    print(f"  重复 id {file_id}: 首见行 {defined[file_id]}，再次出现行 {', '.join(map(str, where))}")

for index, file_id, text in dangling[:60]:
    print(f"  悬空引用 行 {index}: fileID {file_id} -> {text}")

if not dupes and not dangling:
    print("OK")
else:
    print(f"问题 {len(dupes) + len(dangling)} 条")
