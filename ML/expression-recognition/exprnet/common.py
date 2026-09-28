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
    """类别表：启用的类别（顺序即输出顺序）+ 标签别名 + 各数据集官方编号顺序。"""

    keys: list[str]
    zh: list[str]
    aliases: dict[str, str] = field(default_factory=dict)  # 小写别名 → 类别 key（可能是未启用的 key）
    datasets: dict[str, dict] = field(default_factory=dict)

    @property
    def num_classes(self) -> int:
        return len(self.keys)

    def index(self, key: str) -> int:
        return self.keys.index(key)

    def normalize(self, raw: Any) -> str | None:
        """把数据集里的标签（名字）归一到类别 key；不认识返回 None。结果可能是未启用的 key。"""
        s = str(raw).strip().lower()
        if s in self.aliases:
            return self.aliases[s]
        return s if s in self.keys else None

    def to_class_index(self, raw: Any) -> int | None:
        """标签 → 启用类别的下标；不认识或未启用返回 None（样本应丢弃）。"""
        key = self.normalize(raw)
        if key is None or key not in self.keys:
            return None
        return self.keys.index(key)

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
