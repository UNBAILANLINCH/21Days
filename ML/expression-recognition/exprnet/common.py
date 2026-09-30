"""公共工具：路径解析、YAML/JSON 读写、类别表、随机种子。"""

from __future__ import annotations

import argparse
import hashlib
import json
import random
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

import numpy as np
import yaml

# 本子项目根目录（ML/expression-recognition/）。配置里的相对路径一律相对它解析。
PROJECT_ROOT = Path(__file__).resolve().parents[1]
CONFIG_DIR = PROJECT_ROOT / "configs"
ARTIFACTS_DIR = PROJECT_ROOT / "artifacts"
CACHE_DIR = PROJECT_ROOT / ".cache"
LABEL_SET_DIR = CONFIG_DIR / "label_sets"

# 「认不出」：不是 softmax 的一类，是判定结果 / 金标里的期望值（规格 §7.2）。
UNKNOWN_LABEL = "unknown"
# 金标里 unknown 的别名：标注协议的「不明确 / 混合」（规格 §6.2），复核后按 unknown 处理。
UNKNOWN_ALIASES = ("ambiguous",)


def resolve_path(p: str | Path | None, base: Path = PROJECT_ROOT) -> Path | None:
    """把配置里的路径解析成绝对路径：绝对路径原样返回，相对路径相对 base（默认子项目根）。"""
    if p is None:
        return None
    p = Path(p)
    return p if p.is_absolute() else (base / p).resolve()


def resolve_cli_path(p: str | Path | None) -> Path | None:
    """命令行传入的路径：先按当前工作目录找，找不到再按子项目根找（方便在仓库根或子项目根下运行）。"""
    if p is None:
        return None
    p = Path(p)
    if p.is_absolute() or p.exists():
        return p.resolve()
    alt = PROJECT_ROOT / p
    return alt.resolve() if alt.exists() else p.resolve()


def rel_to_project(p: str | Path) -> str:
    """给报告与元数据用的相对路径（不暴露本机绝对路径）。"""
    p = Path(p).resolve()
    try:
        return p.relative_to(PROJECT_ROOT).as_posix()
    except ValueError:
        return p.name


def load_yaml(path: str | Path) -> Any:
    with open(path, "r", encoding="utf-8") as f:
        return yaml.safe_load(f)


def save_json(obj: Any, path: str | Path) -> None:
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(obj, f, ensure_ascii=False, indent=2, default=_json_default)
        f.write("\n")


def load_json(path: str | Path) -> Any:
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def _json_default(o: Any) -> Any:
    if isinstance(o, np.integer):
        return int(o)
    if isinstance(o, np.floating):
        return float(o)
    if isinstance(o, np.ndarray):
        return o.tolist()
    if isinstance(o, Path):
        return o.as_posix()
    raise TypeError(f"无法序列化为 JSON：{type(o)}")


def stable_hash(obj: Any) -> str:
    """对可 JSON 化的对象求稳定的 SHA-256（键排序、不转义中文）。"""
    s = json.dumps(obj, sort_keys=True, ensure_ascii=False, separators=(",", ":"), default=_json_default)
    return hashlib.sha256(s.encode("utf-8")).hexdigest()


def array_hash(*arrays: np.ndarray) -> str:
    h = hashlib.sha256()
    for a in arrays:
        a = np.ascontiguousarray(a)
        h.update(str(a.dtype).encode())
        h.update(str(a.shape).encode())
        h.update(a.tobytes())
    return h.hexdigest()


def set_seed(seed: int) -> None:
    random.seed(seed)
    np.random.seed(seed)
    try:
        import torch

        torch.manual_seed(seed)
    except ImportError:  # 抽特征环境可以不装 torch
        pass


# ---------------------------------------------------------------- 类别表


@dataclass
class Labels:
    """类别表：启用的类别（顺序即输出顺序）+ 标签别名 + 各数据集官方编号顺序。

    用了类别集（configs/label_sets/*.yaml）时，keys / zh 是类别集里的类，class_map 把标注类映射到类别集的类，
    label_set 记录类别集原文（名字、各类由哪些标注类组成、集外类）。不用类别集时 class_map 为空、label_set 为 None。
    """

    keys: list[str]
    zh: list[str]
    aliases: dict[str, str] = field(default_factory=dict)  # 小写别名 → 类别 key（可能是未启用的 key）
    datasets: dict[str, dict] = field(default_factory=dict)
    class_map: dict[str, str] = field(default_factory=dict)  # 标注类 key → 类别集的类 key；空 = 不用类别集
    label_set: dict | None = None

    @property
    def num_classes(self) -> int:
        return len(self.keys)

    @property
    def annotation_keys(self) -> list[str]:
        """标注类：人工标注 / 公开集标签归一后的类别（不用类别集时就是 keys）。"""
        return list(self.label_set["annotation_keys"]) if self.label_set else list(self.keys)

    @property
    def dropped(self) -> list[str]:
        """类别集的集外类（标注类 key）：不进正样本、不计入指标。"""
        return list(self.label_set.get("drop") or []) if self.label_set else []

    def members(self) -> dict[str, list[str]]:
        """每个输出类由哪些标注类组成（合成器按它出样本）；不用类别集时每类就是它自己。"""
        if not self.label_set:
            return {k: [k] for k in self.keys}
        return {c["key"]: list(c["from"]) for c in self.label_set["classes"]}

    def index(self, key: str) -> int:
        return self.keys.index(key)

    def normalize(self, raw: Any) -> str | None:
        """把数据集里的标签（名字）归一到类别 key；不认识返回 None。结果可能是未启用的 key。"""
        s = str(raw).strip().lower()
        if s in self.aliases:
            return self.aliases[s]
        return s if s in self.keys else None

    def to_class_index(self, raw: Any) -> int | None:
        """标签 → 启用类别的下标；不认识、未启用或是类别集的集外类返回 None（样本应丢弃）。"""
        key = self.normalize(raw)
        if key is not None and self.class_map:
            key = self.class_map.get(key)
        if key is None or key not in self.keys:
            return None
        return self.keys.index(key)

    def map_annotation(self, key: str) -> str | None:
        """标注类 key（或 unknown）→ 输出类 key（或 unknown）；集外类、不认识的返回 None。"""
        if key == UNKNOWN_LABEL:
            return UNKNOWN_LABEL
        if self.class_map:
            return self.class_map.get(key)
        return key if key in self.keys else None

    def dataset_label(self, dataset: str, code: int) -> str | None:
        """按某数据集的官方编号取归一后的类别 key（可能未启用）。"""
        spec = self.datasets[dataset]
        i = int(code) - int(spec.get("index_base", 0))
        order = spec["order"]
        if not 0 <= i < len(order):
            return None
        return self.normalize(order[i])

    def to_meta(self) -> list[dict]:
        return [{"key": k, "zh": z} for k, z in zip(self.keys, self.zh)]


def load_labels(path: str | Path | None = None) -> Labels:
    path = resolve_path(path or CONFIG_DIR / "labels.yaml")
    cfg = load_yaml(path)
    classes = cfg["classes"]
    keys = [c["key"] for c in classes]
    zh = [c.get("zh", c["key"]) for c in classes]
    if len(set(keys)) != len(keys):
        raise ValueError("labels.yaml 里类别 key 有重复")
    aliases: dict[str, str] = {}
    for key, names in (cfg.get("aliases") or {}).items():
        aliases[str(key).lower()] = key
        for n in names or []:
            aliases[str(n).strip().lower()] = key
    return Labels(keys=keys, zh=zh, aliases=aliases, datasets=cfg.get("datasets") or {})


# ---------------------------------------------------------------- 类别集（label set）


def label_set_path(spec: str | Path) -> Path:
    """类别集参数 → 文件路径：只写名字（如 laila_5class）时找 configs/label_sets/<名字>.yaml，否则按路径解析。"""
    p = Path(spec)
    if p.suffix.lower() not in (".yaml", ".yml") and len(p.parts) == 1:
        return LABEL_SET_DIR / f"{p.name}.yaml"
    return resolve_cli_path(p)


def _class_map_of(label_set: dict) -> dict[str, str]:
    m: dict[str, str] = {}
    for c in label_set["classes"]:
        for a in c["from"]:
            m[a] = c["key"]
        m.setdefault(c["key"], c["key"])
    return m


def apply_label_set(base: Labels, cfg: dict, file: str | None = None) -> Labels:
    """在标注类别表 base（labels.yaml）上套用类别集，返回输出类为类别集的 Labels。

    校验：每类有 key、zh、非空 from；from 与 drop 只能写 base 里的标注类；每个标注类恰好出现一次
    （要么进某个类的 from，要么进 drop），不许漏也不许重；key 不重复、不叫 unknown；
    key 与某个标注类同名时，它的 from 必须含这个标注类（避免「fear 类其实是惊讶」这种误读）。
    """
    name = str(cfg.get("name") or "").strip()
    where = f"类别集 {file or name or '（未命名）'}"
    if not name:
        raise ValueError(f"{where} 缺少 name")
    classes = cfg.get("classes") or []
    if not classes:
        raise ValueError(f"{where} 没有任何类（classes 为空）")
    ann = list(base.keys)
    seen: dict[str, str] = {}
    keys, zh, out_classes = [], [], []
    for c in classes:
        key = str(c.get("key") or "").strip()
        if not key:
            raise ValueError(f"{where} 有类缺少 key：{c}")
        if key == UNKNOWN_LABEL or key in UNKNOWN_ALIASES:
            raise ValueError(f"{where} 的类不能叫 {key}：unknown 是「认不出」的判定结果，不是一类")
        if key in keys:
            raise ValueError(f"{where} 的类 key 重复：{key}")
        members = c.get("from")
        if not isinstance(members, list) or not members:
            raise ValueError(f"{where} 的类 {key} 缺少 from（由哪些标注类组成）")
        for m in members:
            if m not in ann:
                raise ValueError(f"{where} 的类 {key} 的 from 写了不存在的标注类 {m}（可选：{ann}）")
            if m in seen:
                raise ValueError(f"{where} 的标注类 {m} 同时出现在 {seen[m]} 与 {key}")
            seen[m] = key
        if key in ann and key not in members:
            raise ValueError(f"{where} 的类 {key} 与标注类同名，但 from 里没有它自己：{members}")
        keys.append(key)
        zh.append(str(c.get("zh") or key))
        out_classes.append({"key": key, "zh": zh[-1], "from": [str(m) for m in members]})
    drop = [str(d) for d in (cfg.get("drop") or [])]
    for d in drop:
        if d not in ann:
            raise ValueError(f"{where} 的 drop 写了不存在的标注类 {d}（可选：{ann}）")
        if d in seen:
            raise ValueError(f"{where} 的标注类 {d} 同时出现在 {seen[d]} 与 drop")
        seen[d] = "drop"
    missing = [a for a in ann if a not in seen]
    if missing:
        raise ValueError(f"{where} 没有交代标注类 {missing}：每个标注类要么进某个类的 from，要么列入 drop")
    ls = {"name": name, "file": file, "classes": out_classes, "drop": drop, "annotation_keys": ann}
    if cfg.get("description"):
        ls["description"] = str(cfg["description"])
    return Labels(keys=keys, zh=zh, aliases=dict(base.aliases), datasets=dict(base.datasets),
                  class_map=_class_map_of(ls), label_set=ls)


def load_label_set(path: str | Path, base: Labels | None = None) -> Labels:
    """读类别集 yaml 并套到标注类别表 base 上（默认 configs/labels.yaml）。"""
    path = resolve_path(path)
    if not path.exists():
        raise FileNotFoundError(f"找不到类别集文件：{rel_to_project(path)}")
    return apply_label_set(base or load_labels(), load_yaml(path) or {}, file=rel_to_project(path))


def labels_of_ckpt(ckpt: dict, base: Labels | None = None) -> Labels:
    """按训练产物还原类别表：输出类取 ckpt 记录的类别，别名取当前 labels.yaml，类别集映射取 ckpt 记录。"""
    base = base or load_labels()
    ls = ckpt.get("label_set")
    return Labels(keys=list(ckpt["class_keys"]), zh=list(ckpt["class_zh"]), aliases=dict(base.aliases),
                  datasets=dict(base.datasets), class_map=_class_map_of(ls) if ls else {}, label_set=ls)


def portable_path(p: str | Path) -> str:
    """写进配置快照的路径：在子项目内的写成相对路径（resolve_path 可还原），否则保留原样。"""
    p = Path(p).resolve()
    try:
        return p.relative_to(PROJECT_ROOT).as_posix()
    except ValueError:
        return p.as_posix()


class _ZhHelpFormatter(argparse.RawDescriptionHelpFormatter):
    def add_usage(self, usage, actions, groups, prefix=None):
        return super().add_usage(usage, actions, groups, prefix="用法：")


def make_parser(prog: str, description: str, epilog: str | None = None) -> argparse.ArgumentParser:
    """命令行解析器：帮助信息里的固定字样也用中文。"""
    ap = argparse.ArgumentParser(prog=prog, description=description, epilog=epilog, add_help=False,
                                 formatter_class=_ZhHelpFormatter)
    ap._optionals.title = "参数"
    ap.add_argument("-h", "--help", action="help", help="显示本帮助并退出")
    return ap
