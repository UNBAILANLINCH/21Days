"""真实采集的非法输入、版本和近邻分组护栏；fixture不是人工样本。"""
import json

import numpy as np
import pytest

from analysis.laila_v2_candidate.capture_diagnostics import (
    RIG_PATH, POSITIVE, NEGATIVE, audit, decision, diagnose, main, prepare_blind, validate_capture,
)
from exprnet.canonical import load_canonical
from exprnet.rig import load_rig


@pytest.fixture
def rig():
    return load_rig(RIG_PATH, load_canonical())


def capture(rig, sample_id='a' * 32, split='dev', group='g001'):
    return {'schema_version': 1, 'status': 'unlabeled', 'source': 'unity-editor-play-capture',
            'id': sample_id, 'split': split, 'group': group, 'rig': rig.name,
            'rig_hash': rig.config_hash(), 'fbx_sha256': 'f' * 64,
            'sliders': dict(zip(rig.keys, [0.] * 17)), 'weights': dict.fromkeys(POSITIVE + NEGATIVE, 0.)}


@pytest.mark.parametrize('fault', ['missing-axis', 'missing-weight', 'conflict', 'nan', 'boolean',
                                    'range', 'mismatch', 'rig-hash', 'fbx-hash'])
def test_invalid_capture_rejected(rig, fault):
    item = capture(rig)
    if fault == 'missing-axis': del item['sliders'][rig.keys[0]]
    if fault == 'missing-weight': del item['weights'][POSITIVE[0]]
    if fault == 'conflict':
        item['weights'][POSITIVE[0]] = 50.; item['weights'][NEGATIVE[0]] = 50.
    if fault == 'nan': item['sliders'][rig.keys[0]] = float('nan')
    if fault == 'boolean': item['sliders'][rig.keys[0]] = True
    if fault == 'range': item['sliders'][rig.keys[0]] = 1.01
    if fault == 'mismatch': item['sliders'][rig.keys[0]] = .5
    if fault == 'rig-hash': item['rig_hash'] = '0' * 64
    if fault == 'fbx-hash': item['fbx_sha256'] = '0' * 64
    with pytest.raises(ValueError): validate_capture(item, rig, 'f' * 64)


def test_real_weight_reconstruction(rig):
    item = capture(rig); item['weights'][NEGATIVE[0]] = 75.; item['sliders'][rig.keys[0]] = -.75
    assert validate_capture(item, rig, 'f' * 64)[0] == -.75


def test_audit_exposes_information_loss(rig):
    result = audit(rig)
    erased = [axis['key'] for axis in result['axes'] if axis.get('negative', {}).get('erased')]
    assert set(erased) == {'eye_L_lower_y', 'eye_R_lower_y', 'mouth_corner_L_x', 'mouth_corner_R_x'}
    assert 'jawOpen' in result['unreachable_channels']
    s1 = rig.defaults.copy(); s1[0] = .5
    s2 = rig.defaults.copy(); s2[3] = .5
    np.testing.assert_array_equal(rig.forward(s1[None]), rig.forward(s2[None]))


@pytest.mark.parametrize('energy,p,reasons', [
    (-1., [.6, .4], []),
    (-.9, [.6, .4], ['energy_above_threshold']),
    (-1.1, [.5, .5], ['confidence_below_minimum']),
    (-.9, [.5, .5], ['energy_above_threshold', 'confidence_below_minimum']),
])
def test_decision_matches_deployed_strict_boundaries(energy, p, reasons):
    result = decision(p, energy, -1., .6)
    assert result['reasons'] == reasons
    assert result['rejected'] == bool(reasons)


def store(root, item):
    folder = root / item['split']; folder.mkdir(exist_ok=True, parents=True)
    path = folder / (item['id'] + '.json')
    path.write_text(json.dumps(item), encoding='utf-8')
    # 非真实PNG，测试不声称采集或人工标注；工具只搬运图像字节。
    path.with_suffix('.png').write_bytes(b'test-image-placeholder')


def test_cross_split_group_rejected_before_output(rig, tmp_path):
    root = tmp_path / 'captures'
    store(root, capture(rig)); store(root, capture(rig, 'b' * 32, 'test'))
    out = tmp_path / 'blind'
    with pytest.raises(ValueError, match='跨划分'): prepare_blind(root, out, rig, 'f' * 64, 42)
    assert not out.exists()


def test_blind_packet_has_no_labels_or_recipe_leakage(rig, tmp_path):
    root = tmp_path / 'captures'; store(root, capture(rig))
    out = tmp_path / 'blind'; summary = prepare_blind(root, out, rig, 'f' * 64, 42)
    assert summary == {'samples': 1, 'independent_groups': 1, 'human_labels_created': 0}
    for csv in (out / 'annotators').glob('*.csv'):
        text = csv.read_text(encoding='utf-8-sig')
        assert 'g001' not in text and 'dev' not in text and 'surprise' not in text
        assert ',,,\n' in text
    with pytest.raises(ValueError, match='覆盖'): prepare_blind(root, out, rig, 'f' * 64, 42)


def test_empty_collection_does_not_create_fake_samples(rig, tmp_path):
    out = tmp_path / 'blind'
    with pytest.raises(ValueError, match='没有真实采集'): prepare_blind(tmp_path / 'missing', out, rig, 'f' * 64, 42)
    assert not out.exists()


def test_locked_test_cannot_be_development_diagnosed(rig):
    with pytest.raises(ValueError, match='锁定test'):
        diagnose(capture(rig, split='test'), rig, None, None)


def test_cli_cannot_overwrite_source_or_capture(tmp_path):
    target = tmp_path / 'source.json'
    assert main(['audit', '--out', str(target)]) == 1
    assert not target.exists()
