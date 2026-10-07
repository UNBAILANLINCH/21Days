"""手动CLI；5秒锚点为终端状态及round输出mtime，完成即退出。
只在独占Unity窗口下由另一个临时MCP采集器生成manifest，本文件不连接Unity。
复用capture_diagnostics，不把配方名当人工标签，不训练或改阈值。
"""
import argparse
import json
from pathlib import Path

import numpy as np

from analysis.laila_v2_candidate.capture_diagnostics import (
    ROOT, REPO, RIG_PATH, RUN_PATH, MODEL_PATH, FBX_PATH,
    digest, diagnose, read_json, validate_capture, write_json,
)
from exprnet.canonical import load_canonical
from exprnet.rig import load_rig

ROUND = ROOT / 'artifacts/laila_v2_candidate/unity-round-20261003'
PROTECTED = [
    'Assets/_Project/Scenes/Boot.unity', 'Assets/Scenes/SampleScene.unity',
    'Assets/_Project/Scenes/LailaRecognitionPlaytest.unity', 'Assets/Settings/UniversalRP.asset',
    'Assets/Settings/UniversalRenderer.asset', 'Packages/manifest.json',
    'Packages/packages-lock.json', 'ProjectSettings/ProjectVersion.txt',
    'Assets/_Project/Art/Fonts/Font_NotoSansSC_Regular SDF.asset',
    FBX_PATH.relative_to(REPO).as_posix(), MODEL_PATH.relative_to(REPO).as_posix(),
    MODEL_PATH.with_suffix('.json').relative_to(REPO).as_posix(),
]


def hashes():
    return {p: digest(REPO / p) for p in PROTECTED if (REPO / p).is_file()}


def main():
    ap = argparse.ArgumentParser(); ap.add_argument('action', choices=['baseline', 'analyze', 'final'])
    args = ap.parse_args()
    if args.action == 'baseline':
        if (ROUND / 'before-hashes.json').exists():
            raise ValueError('本轮基线已存在，禁止覆盖')
        write_json(ROUND / 'before-hashes.json', hashes())
        print('baseline recorded'); return
    if args.action == 'final':
        before = read_json(ROUND / 'before-hashes.json'); after = hashes()
        changed = [p for p in before if before[p] != after.get(p)]
        write_json(ROUND / 'after-hashes.json', {'hashes': after, 'changed': changed})
        print(json.dumps({'protected_changed': changed}, ensure_ascii=False)); return
    rig = load_rig(RIG_PATH, load_canonical())
    import torch
    torch.set_num_threads(1)
    manifest = read_json(ROUND / 'unity-manifest.json')
    if not manifest.get('completed') or manifest.get('error'):
        raise ValueError('Unity采集未完整完成，不能写通过报告')
    rows, inputs, features, reports = [], [], [], {}
    for entry in manifest['entries']:
        record_path = ROOT / 'data/laila-captures-v2/dev' / (entry['id'] + '.json')
        record = read_json(record_path)
        s = validate_capture(record, rig, digest(FBX_PATH))
        x = rig.forward(s[None])[0] * rig.canonical.mask_vector
        # 重放每个实采点；diagnose验证checkpoint、部署ONNX与元数据契约。
        report = diagnose(record, rig, RUN_PATH, MODEL_PATH)
        report['capture_sha256'] = digest(record_path)
        report['image_sha256'] = digest(record_path.with_suffix('.png'))
        report['live_sentis_prob_error'] = float(np.max(np.abs(np.array(list(report['probabilities'].values())) - entry['live_probabilities'])))
        report['live_sentis_energy_error'] = abs(report['energy'] - entry['live_energy'])
        write_json(ROUND / 'diagnostics' / (entry['id'] + '.json'), report)
        reports[entry['recipe']] = report
        rows.append({'id': entry['id'], 'recipe': entry['recipe'], 'group': record['group'],
                     'human_label': None, 'decision': report['decision'], 'energy': report['energy'],
                     'geometry': entry['geometry'], 'live_prob_error': report['live_sentis_prob_error'],
                     'live_energy_error': report['live_sentis_energy_error']})
        inputs.append(s); features.append(x)
    raw, mapped = np.array(inputs), np.array(features)
    collisions = []
    for i in range(len(rows)):
        for j in range(i + 1, len(rows)):
            if np.linalg.norm(raw[i] - raw[j]) > 1e-5 and np.max(np.abs(mapped[i] - mapped[j])) < 1e-7:
                collisions.append({'a': rows[i]['recipe'], 'b': rows[j]['recipe'],
                                   'raw_17_l2': float(np.linalg.norm(raw[i] - raw[j])),
                                   'mapped_51_max_difference': float(np.max(np.abs(mapped[i] - mapped[j])))})
    geometry_flags = [r for r in rows if r['geometry']['finite'] is False
                      or r['geometry']['orientation_reversals'] > 0 or r['geometry']['area_below_15pct'] > 0]
    result = {'status': 'real-unity-development-captures-unlabeled', 'samples': len(rows),
              'human_labels': 0, 'deployment_changed': False, 'rows': rows,
              'exact_mapping_collisions': collisions, 'geometry_flags': geometry_flags,
              'max_live_sentis_prob_error': max(r['live_prob_error'] for r in rows),
              'max_live_sentis_energy_error': max(r['live_energy_error'] for r in rows),
              'limitations': ['配方A/B仅为待盲判候选，不等于惊讶/恐惧真实标签。',
                             '用户失败截图无参数，未声称精确复现。',
                             '局部面积/法向检查不能证明没有自交、眼/牙穿插或完整口腔验收。',
                             '没有改变几何或AU系数；本轮是校准取证，不是完成语义校准。']}
    write_json(ROUND / 'round-analysis.json', result)
    print(json.dumps({k: result[k] for k in ['samples', 'human_labels', 'max_live_sentis_prob_error', 'max_live_sentis_energy_error']}
                     | {'exact_mapping_collisions': len(collisions), 'geometry_flags': len(geometry_flags)}, ensure_ascii=False))


if __name__ == '__main__':
    main()
