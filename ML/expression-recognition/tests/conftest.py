"""pytest 公共设置：把子项目根加进 sys.path，并提供常用夹具。测试不联网、不需要任何数据集。"""

import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from exprnet.canonical import load_canonical  # noqa: E402
from exprnet.common import CONFIG_DIR, load_labels  # noqa: E402
from exprnet.rig import load_rig  # noqa: E402


@pytest.fixture(scope="session")
def canonical():
    return load_canonical()


@pytest.fixture(scope="session")
def labels():
    return load_labels()


@pytest.fixture(scope="session")
def sample_rig(canonical):
    return load_rig(CONFIG_DIR / "rigs" / "sample_rig.yaml", canonical)
