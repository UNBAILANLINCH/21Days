"""金标集护栏（规格 §6.2、§6.3、§10）：评估前与划分后都要过的检查。

逐个文件检查：
    - schema_version 为 1，entries 非空；
    - rig 等于绑定名；文件带 rig_hash 时必须等于绑定哈希（Rig.config_hash()，完整 64 位）；
    - 每条 id 非空且不重复；
    - expect 是合法标注类：标注类别表里的类（默认七类）、unknown，或 unknown 的别名 ambiguous（标注协议的「不明确 / 混合」）；
    - sliders 的 key 都在绑定里，值是有限数（拒绝 NaN / Infinity / 字符串 / 布尔），且在滑杆范围内；
    - status 为 human-labeled 时标「人工盲标」，否则标「占位，不能证明泛化」。
开发集与测试集一起查时再加：同一 group 不许同时出现在两个集里（规格 §6.2 按组划分），id 也不许跨集重复。

evaluate 复用这里的检查：绑定名 / 哈希不符时默认报错退出，只有显式传 --allow-golden-skip 才降级为跳过；
其余数据错误（值、标签、id）一律报错。

用法示例（在 ML/expression-recognition 目录下）：
    python -m exprnet.golden_check --dev golden/laila_dev.json --test golden/laila_test.json
    python -m exprnet.golden_check --dev golden/laila_dev.json --test golden/laila_test.json --rig configs/rigs/laila_rig.yaml
    python -m exprnet.golden_check --dev golden/sample_rig.json --run artifacts/runs/mlp_synth
不给 --rig / --run 时，按金标文件的 rig 字段在 configs/rigs/*.yaml 里找同名绑定。
"""

from __future__ import annotations

import json
import math
import sys
from collections import Counter
from pathlib import Path

from .common import (CONFIG_DIR, UNKNOWN_ALIASES, UNKNOWN_LABEL, load_labels, load_yaml, make_parser, rel_to_project,
                     resolve_cli_path)

SCHEMA_VERSION = 1
HUMAN_STATUS = "human-labeled"
STATUS_HUMAN_ZH = "人工盲标"
STATUS_PLACEHOLDER_ZH = "占位，不能证明泛化"
RANGE_TOL = 1e-9
MAX_LISTED = 20  # 报错信息里最多逐条列出几个问题


class GoldenError(ValueError):
    """金标集不能用于评估：绑定不符、数据不合法、文件缺失或格式错误。"""


def status_zh(status) -> str:
    return STATUS_HUMAN_ZH if status == HUMAN_STATUS else STATUS_PLACEHOLDER_ZH


def normalize_expect(expect) -> str:
    """unknown 的别名（ambiguous）归一成 unknown；其余原样返回。"""
    return UNKNOWN_LABEL if expect in UNKNOWN_ALIASES else expect


def load_golden(path: str | Path) -> dict:
    path = Path(path)
    if not path.exists():
        raise GoldenError(f"金标文件不存在：{rel_to_project(path)}")
    try:
        with open(path, "r", encoding="utf-8") as f:
            g = json.load(f)
    except (json.JSONDecodeError, UnicodeDecodeError) as e:
        raise GoldenError(f"金标文件 {rel_to_project(path)} 不是合法 JSON：{e}") from e
    if not isinstance(g, dict) or not isinstance(g.get("entries"), list):
        raise GoldenError(f"金标文件 {rel_to_project(path)} 缺少 entries 列表")
    return g


def rig_mismatch(g: dict, rig, file: str = "") -> str | None:
    """绑定名 / 绑定哈希与 rig 不符时返回中文说明，相符返回 None。"""
    where = f"金标 {file}" if file else "金标"
    if g.get("rig") != rig.name:
        return f"{where} 的绑定是「{g.get('rig')}」，而模型训练绑定是「{rig.name}」，两者不符"
    h = g.get("rig_hash")
    if h is not None and str(h) != rig.config_hash():
        return (f"{where} 的 rig_hash 为 {str(h)[:16]}…，而绑定「{rig.name}」的哈希是 {rig.config_hash()[:16]}…："
                "同名绑定的系数或范围已经变了，这份金标对应的是另一版绑定")
    return None


def _is_number(v) -> bool:
    return isinstance(v, (int, float)) and not isinstance(v, bool)


def check_entries(g: dict, rig, annotation_keys: list[str], file: str = "") -> list[str]:
    """逐条检查 schema、id、expect、sliders，返回问题列表（空列表 = 通过）。不查绑定名 / 哈希（见 rig_mismatch）。"""
    where = f"{file}：" if file else ""
    problems: list[str] = []
    if g.get("schema_version") != SCHEMA_VERSION:
        problems.append(f"{where}schema_version 应为 {SCHEMA_VERSION}，实际 {g.get('schema_version')!r}")
    entries = g.get("entries") or []
    if not entries:
        problems.append(f"{where}entries 为空")
    legal = list(annotation_keys) + [UNKNOWN_LABEL] + list(UNKNOWN_ALIASES)
    lo = dict(zip(rig.keys, rig.s_min.tolist()))
    hi = dict(zip(rig.keys, rig.s_max.tolist()))
    seen: set[str] = set()
    for i, e in enumerate(entries):
        if not isinstance(e, dict):
            problems.append(f"{where}第 {i + 1} 条不是对象")
            continue
        eid = e.get("id")
        tag = f"{where}{eid if isinstance(eid, str) and eid else f'第 {i + 1} 条'}"
        if not isinstance(eid, str) or not eid.strip():
            problems.append(f"{tag}：id 缺失或为空")
        elif eid in seen:
            problems.append(f"{tag}：id 重复")
        else:
            seen.add(eid)
        if e.get("expect") not in legal:
            problems.append(f"{tag}：expect「{e.get('expect')}」不是合法标注类（可选：{legal}）")
        sl = e.get("sliders")
        if not isinstance(sl, dict):
            problems.append(f"{tag}：sliders 应为对象（省略的滑杆按默认值 0）")
            continue
        for k, v in sl.items():
            if k not in lo:
                problems.append(f"{tag}：用了绑定「{rig.name}」里没有的滑杆 {k}")
            elif not _is_number(v) or not math.isfinite(v):
                problems.append(f"{tag}：滑杆 {k} 的值 {v!r} 不是有限数")
            elif not lo[k] - RANGE_TOL <= v <= hi[k] + RANGE_TOL:
                problems.append(f"{tag}：滑杆 {k} 的值 {v} 超出范围 [{lo[k]:g}, {hi[k]:g}]")
        if "group" in e and (e["group"] is None or str(e["group"]).strip() == ""):
            problems.append(f"{tag}：group 字段为空")
    return problems


def format_problems(problems: list[str]) -> str:
    shown = problems[:MAX_LISTED]
    more = f"\n  ……另有 {len(problems) - MAX_LISTED} 条" if len(problems) > MAX_LISTED else ""
    return "\n".join(f"  - {p}" for p in shown) + more


def check_golden_file(path: str | Path, rig, annotation_keys: list[str]) -> dict:
    """单个文件的完整检查结果：errors（不能用）/ info（状态、条数、各标签条数、group 情况）。"""
    path = Path(path)
    file = rel_to_project(path)
    out = {"file": file, "errors": [], "ids": set(), "groups": {}}
    try:
        g = load_golden(path)
    except GoldenError as e:
        out["errors"].append(str(e))
        return out
    mm = rig_mismatch(g, rig, file)
    if mm:
        out["errors"].append(mm)
    if g.get("rig") == rig.name:  # 绑定名都不对时逐条查滑杆没有意义（全是「绑定里没有的滑杆」）
        out["errors"] += check_entries(g, rig, annotation_keys, file)
    entries = [e for e in g["entries"] if isinstance(e, dict)]
    out["status"] = g.get("status")
    out["status_zh"] = status_zh(g.get("status"))
    out["rig"] = g.get("rig")
    out["has_rig_hash"] = g.get("rig_hash") is not None
    out["n"] = len(entries)
    out["expect_counts"] = dict(Counter(normalize_expect(e.get("expect")) for e in entries))
    out["ids"] = {e["id"] for e in entries if isinstance(e.get("id"), str)}
    out["groups"] = {}
    for e in entries:
        if "group" in e and e["group"] is not None:
            out["groups"].setdefault(str(e["group"]), []).append(e.get("id"))
    out["n_without_group"] = sum("group" not in e for e in entries)
    return out


def check_sets(dev: Path | None, test: Path | None, rig, annotation_keys: list[str]) -> dict:
    """开发集 / 测试集一起查：各自过文件检查，再查 group 与 id 不跨集。"""
    res = {"rig": rig.name, "rig_hash": rig.config_hash(), "annotation_keys": list(annotation_keys), "files": {}, "cross_errors": []}
    for name, p in (("dev", dev), ("test", test)):
        if p is not None:
            res["files"][name] = check_golden_file(p, rig, annotation_keys)
    if "dev" in res["files"] and "test" in res["files"]:
        d, t = res["files"]["dev"], res["files"]["test"]
        for gid in sorted(set(d["groups"]) & set(t["groups"])):
            res["cross_errors"].append(f"group「{gid}」同时出现在开发集（{', '.join(map(str, d['groups'][gid]))}）"
                                       f"与测试集（{', '.join(map(str, t['groups'][gid]))}）：近邻样本不许跨集（规格 §6.2）")
        for eid in sorted(d["ids"] & t["ids"]):
            res["cross_errors"].append(f"id「{eid}」同时出现在开发集与测试集")
    res["ok"] = not res["cross_errors"] and all(not f["errors"] for f in res["files"].values())
    return res


def find_rig_by_name(name: str, canonical):
    """在 configs/rigs/*.yaml 里找 name 等于 name 的绑定。"""
    from .rig import load_rig

    hits = []
    for p in sorted((CONFIG_DIR / "rigs").glob("*.yaml")):
        try:
            if (load_yaml(p) or {}).get("name") == name:
                hits.append(p)
        except Exception:  # noqa: BLE001  坏文件不影响找别的
            continue
    if len(hits) != 1:
        where = "没有" if not hits else f"有 {len(hits)} 个"
        raise GoldenError(f"configs/rigs/ 里{where} name 为「{name}」的绑定配置，请用 --rig 或 --run 指定")
    return load_rig(hits[0], canonical)


def render(res: dict) -> str:
    L = [f"绑定：{res['rig']}（哈希 {res['rig_hash'][:16]}…）；合法标签：{', '.join(res['annotation_keys'])}、"
         f"{UNKNOWN_LABEL}（别名 {', '.join(UNKNOWN_ALIASES)}）"]
    zh = {"dev": "开发集", "test": "测试集"}
    for name, f in res["files"].items():
        L.append(f"\n[{zh[name]}] {f['file']}")
        if "n" in f:
            L.append(f"  状态：{f['status_zh']}（文件 status：{f['status']}）；{f['n']} 条；"
                     f"rig_hash：{'有' if f['has_rig_hash'] else '无'}；"
                     f"group：{len(f['groups'])} 组，{f['n_without_group']} 条没写 group")
            L.append("  各标签条数：" + "，".join(f"{k} {v}" for k, v in sorted(f["expect_counts"].items(), key=lambda kv: str(kv[0]))))
        if f["errors"]:
            L.append(f"  不通过（{len(f['errors'])} 个问题）：\n" + format_problems(f["errors"]))
        else:
            L.append("  通过")
    if len(res["files"]) == 2:
        L.append("\n[跨集] " + ("通过：group 与 id 都不跨集" if not res["cross_errors"] else
                               f"不通过（{len(res['cross_errors'])} 个问题）：\n" + format_problems(res["cross_errors"])))
    L.append("\n结论：" + ("全部通过" if res["ok"] else "不通过，修好后再用于评估"))
    return "\n".join(L)


def main(argv=None) -> int:
    ap = make_parser("python -m exprnet.golden_check",
                     "检查金标开发集 / 测试集：绑定名与哈希、滑杆值有限且在范围内、标签合法、id 唯一、group 不跨集（规格 §6.3）。")
    ap.add_argument("--dev", default=None, help="开发集 json，如 golden/laila_dev.json")
    ap.add_argument("--test", default=None, help="测试集 json，如 golden/laila_test.json")
    ap.add_argument("--rig", default=None, help="绑定配置 yaml；不给时按金标的 rig 字段在 configs/rigs/ 里找同名绑定")
    ap.add_argument("--run", default=None, help="训练产物目录：用它的训练绑定与标注类（类别集模型用类别集的标注类）")
    args = ap.parse_args(argv)
    if not args.dev and not args.test:
        ap.error("至少给 --dev 或 --test 之一")
    if args.rig and args.run:
        ap.error("--rig 与 --run 只能给一个")
    from .canonical import load_canonical
    from .rig import load_rig

    dev = resolve_cli_path(args.dev) if args.dev else None
    test = resolve_cli_path(args.test) if args.test else None
    try:
        if args.run:
            from .common import labels_of_ckpt
            from .evaluate import rig_of_ckpt
            from .models import load_checkpoint

            _, ckpt, canonical = load_checkpoint(resolve_cli_path(args.run))
            rig = rig_of_ckpt(ckpt, canonical)
            annotation_keys = labels_of_ckpt(ckpt).annotation_keys
        else:
            canonical = load_canonical()
            annotation_keys = load_labels().keys
            if args.rig:
                rig = load_rig(resolve_cli_path(args.rig), canonical)
            else:
                first = dev or test
                rig = find_rig_by_name(load_golden(first).get("rig"), canonical)
    except GoldenError as e:
        print(f"[错误] {e}", file=sys.stderr)
        return 2
    res = check_sets(dev, test, rig, annotation_keys)
    print(render(res))
    return 0 if res["ok"] else 1


if __name__ == "__main__":
    sys.exit(main())
