"""手动 CLI，一次运行后退出；不连接 Unity、不训练、不改部署。
5秒状态锚点：终端成功/失败与输出文件时间；无定时器或后台任务。
复用 Rig、checkpoint、导出配对检查；现有 rejection_diagnosis 仅分析合成缓存，
因此独立补充真实采集快照的输入护栏、映射损失与匿名盲标准备。
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import json
import re
import shutil
from pathlib import Path

import numpy as np

from exprnet.canonical import load_canonical
from exprnet.rig import load_rig

ROOT = Path(__file__).resolve().parents[2]
REPO = ROOT.parents[1]
RIG_PATH = ROOT / 'configs/rigs/laila_rig_v2_candidate.yaml'
MODEL_PATH = REPO / 'Assets/_Project/Data/LailaFaceRecognition/laila_cand_v2_s42_20261003.onnx'
FBX_PATH = REPO / 'Assets/_Project/Art/fbx/Head-topo-expression-extended-brow-regions-refined3 1.fbx'
RUN_PATH = ROOT / 'artifacts/runs/laila_cand_v2_s42_20261003'
OUT_PATH = ROOT / 'artifacts/laila_v2_candidate/capture-diagnostics'
POSITIVE = [
    'Brow_L_Inner_Up', 'Brow_L_Mid_Up', 'Brow_L_Outer_Up',
    'Brow_R_Inner_Up', 'Brow_R_Mid_Up', 'Brow_R_Outer_Up',
    'Eye_L_UpperLid_Up', 'Eye_L_LowerLid_Up', 'Eye_R_UpperLid_Up', 'Eye_R_LowerLid_Up',
    'Mouth_L_Up', 'Mouth_R_Up', 'Mouth_L_Out', 'Mouth_R_Out',
    'Mouth_UpperLipL_Up', 'Mouth_UpperLipR_Up', 'Mouth_LowerLip_Down',
]
NEGATIVE = [
    'Brow_L_Inner_Down', 'Brow_L_Mid_Down', 'Brow_L_Outer_Down',
    'Brow_R_Inner_Down', 'Brow_R_Mid_Down', 'Brow_R_Outer_Down',
    'Eye_L_UpperLid_Down', 'Eye_L_LowerLid_Down', 'Eye_R_UpperLid_Down', 'Eye_R_LowerLid_Down',
    'Mouth_L_Down', 'Mouth_R_Down', 'Mouth_L_In', 'Mouth_R_In',
]


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def read_json(path):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError('重复JSON字段：' + key)
            result[key] = value
        return result
    return json.loads(Path(path).read_text(encoding='utf-8'), object_pairs_hook=unique)


def number(value, name):
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not np.isfinite(value):
        raise ValueError('非有限数值：' + name)
    return float(value)


def validate_capture(record, rig, fbx_digest):
    if record.get('schema_version') != 1 or record.get('status') != 'unlabeled':
        raise ValueError('仅接受schema_version=1、unlabeled原始采集')
    if not re.fullmatch(r'[0-9a-f]{32}', str(record.get('id', ''))):
        raise ValueError('采集id必须为匿名32位UUID')
    if record.get('source') not in ('unity-editor-play-capture', 'unity-player-capture'):
        raise ValueError('不是Unity原始采集；不得把人工配方伪装成实采')
    if record.get('rig') != rig.name or record.get('rig_hash') != rig.config_hash():
        raise ValueError('采集绑定版本/哈希不匹配')
    if record.get('fbx_sha256') != fbx_digest:
        raise ValueError('采集FBX哈希不匹配')
    if not isinstance(record.get('group'), str) or not record['group'].strip():
        raise ValueError('缺近邻组')
    if record.get('split') not in ('unassigned', 'train', 'dev', 'test'):
        raise ValueError('未知数据划分')
    sliders, weights = record.get('sliders'), record.get('weights')
    if not isinstance(sliders, dict) or set(sliders) != set(rig.keys):
        raise ValueError('必须保留完整17轴，无缺键或额外键')
    if not isinstance(weights, dict) or set(weights) != set(POSITIVE + NEGATIVE):
        raise ValueError('必须保留当前完整31个形态权重')
    s = np.array([number(sliders[k], k) for k in rig.keys], dtype=np.float64)
    if np.any(s < rig.s_min - 1e-7) or np.any(s > rig.s_max + 1e-7):
        raise ValueError('滑杆越界；诊断不静默裁剪非法输入')
    reconstructed = []
    for i, positive in enumerate(POSITIVE):
        p = number(weights[positive], positive)
        n = number(weights[NEGATIVE[i]], NEGATIVE[i]) if i < len(NEGATIVE) else 0.0
        if min(p, n) < -.001 or max(p, n) > 100.001:
            raise ValueError('形态权重越界：' + positive)
        if i < len(NEGATIVE) and p > .001 and n > .001:
            raise ValueError('同轴正反形态冲突：' + positive)
        reconstructed.append((np.clip(p, 0, 100) - np.clip(n, 0, 100)) / 100)
    if not np.allclose(s, reconstructed, atol=2e-7, rtol=0):
        raise ValueError('17轴与31权重不一致，快照不可复现')
    return s


def audit(rig):
    c = rig.canonical
    halves, axes = [], []
    for i, slider in enumerate(rig.sliders):
        item = {'key': slider.key, 'range': [slider.min, slider.max], 'sweeps': []}
        for direction, bound in [('positive', slider.max), ('negative', slider.min)]:
            if bound == 0:
                continue
            basis = rig.pos_basis[i] if bound > 0 else rig.neg_basis[i]
            halves.append((slider.key + ':' + direction, basis * c.mask_vector))
            item[direction] = {'channels': {n: float(v) for n, v in zip(c.names, basis) if v},
                               'erased': not bool(np.any(basis * c.mask_vector))}
            for fraction in [.25, .5, .75, 1.0]:
                s = rig.defaults.copy(); s[i] = bound * fraction
                x = rig.forward(s[None])[0] * c.mask_vector
                item['sweeps'].append({'slider_value': float(s[i]),
                                       'canonical': {n: float(v) for n, v in zip(c.names, x) if v}})
        axes.append(item)
    proportional = []
    for i, (a, x) in enumerate(halves):
        for b, y in halves[i + 1:]:
            if np.linalg.norm(x) and np.linalg.norm(y):
                ratio = float(np.dot(x, y) / np.dot(y, y))
                if np.allclose(x, ratio * y, atol=1e-12, rtol=0):
                    proportional.append({'a': a, 'b': b, 'a_equals_ratio_times_b': ratio})
    return {'schema_version': 1, 'status': 'engineering-mapping-audit-not-human-calibration',
            'rig': rig.name, 'rig_hash': rig.config_hash(), 'axes': axes,
            'proportional_half_axes': proportional,
            'unreachable_channels': [n for n, yes in zip(c.names, rig.reachable()) if not yes],
            'limitations': ['名称不能证明几何方向或AU语义；需要Unity真实光影下逐轴复核。',
                           '半轴向量共线说明信息压缩，不证明惊讶/恐惧整体不可分。',
                           '原型依据configs/facs.yaml；眉中缺独立基、三段Down合并、内眉左右共享。']}


def decision(probabilities, energy, threshold, confidence):
    p = np.asarray(probabilities, dtype=np.float64)
    if p.ndim != 1 or not len(p) or not np.isfinite(p).all() or not np.isfinite(energy):
        raise ValueError('无效推理输出')
    reasons = []
    if energy > threshold:
        reasons.append('energy_above_threshold')
    if p.max() < confidence:
        reasons.append('confidence_below_minimum')
    return {'rejected': bool(reasons), 'reasons': reasons, 'argmax_index': int(p.argmax()),
            'maximum_probability': float(p.max()), 'energy_margin': float(energy - threshold),
            'confidence_margin': float(p.max() - confidence)}


def diagnose(record, rig, run_path, model_path):
    if record['split'] == 'test':
        raise ValueError('拒绝对锁定test运行开发诊断；该命令只能用于开发')
    import torch
    import onnxruntime as ort
    from exprnet.models import load_checkpoint
    from exprnet.export import verify_pair
    from exprnet.calibrate import energy_np, softmax_np
    meta_path = model_path.with_suffix('.json')
    ok, message = verify_pair(model_path, meta_path)
    if not ok:
        raise ValueError(message)
    model, ckpt, canonical = load_checkpoint(run_path)
    meta = read_json(meta_path)
    if rig.config_hash() != meta['rig']['config_hash'] or rig.config_hash() != ckpt['train_rig_hash']:
        raise ValueError('绑定与部署/训练哈希不一致')
    if canonical.names != meta['canonical_dims'] or canonical.masked_names != meta['masked_dims']:
        raise ValueError('规范空间/屏蔽维不一致')
    if ckpt['class_keys'] != [label['key'] for label in meta['labels']]:
        raise ValueError('类别顺序不一致')
    for field in ('temperature', 'energy_threshold', 'min_confidence'):
        if ckpt[field] != meta[field]:
            raise ValueError('部署与checkpoint不一致：' + field)
    s = np.array([[record['sliders'][k] for k in rig.keys]], dtype=np.float32)
    raw = np.maximum(s, 0) @ rig.W_pos + np.maximum(-s, 0) @ rig.W_neg
    mapped = rig.forward(s)
    features = mapped * canonical.mask_vector
    with torch.no_grad():
        logits = model(torch.from_numpy(features)).numpy()
    p = softmax_np(logits / ckpt['temperature'])[0]
    e = float(energy_np(logits, ckpt['temperature'])[0])
    options = ort.SessionOptions(); options.intra_op_num_threads = 1; options.inter_op_num_threads = 1
    session = ort.InferenceSession(str(model_path), sess_options=options, providers=['CPUExecutionProvider'])
    po, eo = session.run(['probs', 'energy'], {'sliders': s})
    errors = {'probs': float(np.max(np.abs(po[0] - p))), 'energy': abs(float(eo[0]) - e)}
    if max(errors.values()) >= 1e-5:
        raise ValueError('checkpoint与实际ONNX不一致：' + str(errors))
    keys = ckpt['class_keys']
    result = decision(po[0], float(eo[0]), meta['energy_threshold'], meta['min_confidence'])
    result['argmax_key'] = keys[result['argmax_index']]
    result['final_key'] = 'unknown' if result['rejected'] else result['argmax_key']
    return {'schema_version': 1, 'status': 'offline-replay-not-live-unity-or-human-label',
            'sample_id': record['id'], 'group': record['group'], 'split': record['split'],
            'capture_source_recorded': record['source'],
            'capture_environment_verified': False,
            'rig_hash': rig.config_hash(), 'fbx_sha256': record['fbx_sha256'],
            'checkpoint_sha256': digest(run_path / 'ckpt.pt'), 'onnx_sha256': digest(model_path),
            'metadata_sha256': digest(meta_path), 'sliders_17': record['sliders'],
            'weights_31': record['weights'], 'canonical_preclip_51': dict(zip(canonical.names, raw[0].tolist())),
            'canonical_mapped_51': dict(zip(canonical.names, mapped[0].tolist())),
            'classifier_input_51': dict(zip(canonical.names, features[0].tolist())),
            'logits': dict(zip(keys, logits[0].tolist())), 'probabilities': dict(zip(keys, po[0].tolist())),
            'energy': float(eo[0]), 'temperature': meta['temperature'],
            'energy_threshold': meta['energy_threshold'], 'min_confidence': meta['min_confidence'],
            'decision': result, 'onnx_checkpoint_max_error': errors,
            'saturated_channels': [n for n, v in zip(canonical.names, raw[0]) if v > 1],
            'human_label': None}


def prepare_blind(capture_root, out, rig, fbx_digest, seed):
    captures, seen, groups = [], set(), {}
    for path in sorted(capture_root.rglob('*.json')):
        record = read_json(path)
        validate_capture(record, rig, fbx_digest)
        sample_id = record['id']
        if path.stem != sample_id or not path.with_suffix('.png').is_file():
            raise ValueError('采集PNG/id不配对：' + path.name)
        if sample_id in seen:
            raise ValueError('重复采集id：' + sample_id)
        seen.add(sample_id)
        group = record['group']
        if group in groups and groups[group] != record['split']:
            raise ValueError('近邻组跨划分：' + group)
        groups[group] = record['split']
        captures.append((path, record))
    if not captures:
        raise ValueError('没有真实采集；不生成假样本或人工标签')
    if out.exists():
        raise ValueError('输出目录已存在，拒绝覆盖已有盲标')
    order = np.random.default_rng(seed).permutation(len(captures))
    # 完整验证后写输出；annotators不含分组/划分/配方/模型输出。
    images = out / 'annotators/images'; images.mkdir(parents=True)
    steward = []
    ids = []
    for index in order:
        path, record = captures[int(index)]
        sample_id = record['id']; ids.append(sample_id)
        shutil.copyfile(path.with_suffix('.png'), images / (sample_id + '.png'))
        steward.append({'id': sample_id, 'group': record['group'], 'split': record['split'],
                        'capture_json': path.relative_to(capture_root).as_posix(),
                        'capture_sha256': digest(path), 'image_sha256': digest(path.with_suffix('.png')),
                        'rig_hash': record['rig_hash'], 'fbx_sha256': record['fbx_sha256'],
                        'capture_source_recorded': record['source']})
    for annotator in range(1, 4):
        with (out / f'annotators/annotator_{annotator}.csv').open('w', encoding='utf-8-sig', newline='') as stream:
            writer = csv.writer(stream); writer.writerow(['id', 'label', 'clarity', 'note'])
            writer.writerows([[sample_id, '', '', ''] for sample_id in ids])
    write_json(out / 'steward-manifest.json', {'schema_version': 1, 'status': 'unlabeled',
                                              'shuffle_seed': seed, 'entries': steward})
    return {'samples': len(captures), 'independent_groups': len(groups), 'human_labels_created': 0}


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False) + '\n', encoding='utf-8')


def main(argv=None):
    ap = argparse.ArgumentParser(description='本地候选映射/真实快照诊断；不操作Unity、不训练或改阈值')
    sub = ap.add_subparsers(dest='action', required=True)
    a = sub.add_parser('audit'); a.add_argument('--out', type=Path, default=OUT_PATH / 'mapping-audit.json')
    d = sub.add_parser('diagnose'); d.add_argument('--capture', type=Path, required=True)
    d.add_argument('--run', type=Path, default=RUN_PATH); d.add_argument('--model', type=Path, default=MODEL_PATH)
    d.add_argument('--out', type=Path, required=True)
    b = sub.add_parser('blind'); b.add_argument('--captures', type=Path, default=ROOT / 'data/laila-captures-v2')
    b.add_argument('--out', type=Path, required=True); b.add_argument('--seed', type=int, default=42)
    args = ap.parse_args(argv)
    rig = load_rig(RIG_PATH, load_canonical())
    try:
        artifact_root = (ROOT / 'artifacts/laila_v2_candidate').resolve()
        if not args.out.resolve().is_relative_to(artifact_root):
            raise ValueError('CLI输出必须位于artifacts/laila_v2_candidate，禁止覆盖采集/部署/源配置')
        if args.action == 'audit':
            result = audit(rig); write_json(args.out, result)
            summary = {'axes': len(result['axes']), 'proportional_pairs': len(result['proportional_half_axes'])}
        elif args.action == 'diagnose':
            record = read_json(args.capture); validate_capture(record, rig, digest(FBX_PATH))
            if args.capture.stem != record['id'] or not args.capture.with_suffix('.png').is_file():
                raise ValueError('采集JSON必须与原始匿名PNG配对')
            result = diagnose(record, rig, args.run, args.model)
            result['capture_sha256'] = digest(args.capture)
            result['image_sha256'] = digest(args.capture.with_suffix('.png'))
            write_json(args.out, result); summary = result['decision']
        else:
            summary = prepare_blind(args.captures, args.out, rig, digest(FBX_PATH), args.seed)
        print(json.dumps({'success': True, 'result': summary}, ensure_ascii=False)); return 0
    except (ValueError, KeyError, OSError) as error:
        print(json.dumps({'success': False, 'error': str(error)}, ensure_ascii=False)); return 1


if __name__ == '__main__':
    raise SystemExit(main())
