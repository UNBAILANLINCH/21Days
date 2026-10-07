"""手动CLI的有界单人dev微调实验；每20轮打印进度，完成／失败即退出。
复用已验证适配器、ResMLP和ExprLoss；通用train会重新切分且只支持51D，
不能保持整组留出或加载59D，故独立研究脚本，不改训练框架／Unity／部署。
"""
from __future__ import annotations

import argparse
import copy
import json
import shutil
import subprocess
import sys
import time
from pathlib import Path

import numpy as np
import torch

from analysis.laila_v2_candidate.annotation_adapter import CLASS_KEYS, prepare, reference, replay
from analysis.laila_v2_candidate.capture_diagnostics import ROOT, MODEL_PATH, RUN_PATH, RIG_PATH, digest, read_json
from exprnet.calibrate import energy_np, softmax_np, decide
from exprnet.common import set_seed
from exprnet.losses import ExprLoss
from exprnet.models import load_checkpoint


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False), encoding='utf-8')


def expanded_model(baseline, dimension):
    if dimension not in (51, 59) or baseline.inp.in_features != 51:
        raise ValueError('仅支持当前51D基线和59D成对候选')
    model = copy.deepcopy(baseline)
    if dimension == 59:
        layer = torch.nn.Linear(59, baseline.inp.out_features)
        with torch.no_grad():
            layer.weight.zero_()
            layer.weight[:, :51].copy_(baseline.inp.weight)
            layer.bias.copy_(baseline.inp.bias)
        model.inp = layer
    return model


def train_mask(arrays, train_group=None, heldout_group=None):
    known = arrays['supervised5_mask'].copy()
    if not np.array_equal(known, arrays['y5'] >= 0):
        raise ValueError('五类mask和标签不符')
    if train_group is None:
        return known
    if train_group == heldout_group or train_group not in arrays['group'] or heldout_group not in arrays['group']:
        raise ValueError('整组划分无效／泄漏')
    mask = known & (arrays['group'] == train_group)
    if set(arrays['group'][mask]) & {heldout_group}:
        raise ValueError('训练评估近邻组泄漏')
    for raw in arrays['raw17'][arrays['group'] == heldout_group]:
        if np.any(np.all(arrays['raw17'][mask] == raw, axis=1)):
            raise ValueError('跨组相同raw17，不能视作留出')
    return mask


def logits(model, x):
    model.eval()
    with torch.no_grad():
        values = model(torch.from_numpy(x.astype(np.float32))).numpy()
    if not np.isfinite(values).all() or values.shape != (len(x), 5):
        raise ValueError('候选输出非法')
    return values


def fit(baseline, x, y, mask, seed=42, epochs=80, lr=3e-4, logit_adjust_tau=1.):
    if not 1 <= epochs <= 100 or not mask.any() or np.any(y[mask] < 0):
        raise ValueError('训练预算／监督mask无效')
    counts = np.bincount(y[mask], minlength=5)
    if np.any(counts == 0):
        raise ValueError('训练组缺五类，不靠补标签完成')
    set_seed(seed)
    # 既有51D→59D实验仍走零追加列；已授权的59D反馈修复须保留当前全部权重。
    if x.shape[1] not in (51, 59):
        raise ValueError('仅支持51D／59D候选')
    model = copy.deepcopy(baseline) if baseline.inp.in_features == x.shape[1] else expanded_model(baseline, x.shape[1])
    optimizer = torch.optim.AdamW(model.parameters(), lr=lr, weight_decay=.05)
    if not np.isfinite(logit_adjust_tau) or not 0 <= logit_adjust_tau <= 1:
        raise ValueError('类别先验调整须在0–1之间')
    loss_fn = ExprLoss({'label_smoothing': .1, 'logit_adjust_tau': logit_adjust_tau, 'oe_weight': 0.},
                       torch.tensor(counts / counts.sum(), dtype=torch.float32))
    xt = torch.from_numpy(x[mask].astype(np.float32))
    yt = torch.nn.functional.one_hot(torch.from_numpy(y[mask].astype(np.int64)), 5).float()
    generator = torch.Generator().manual_seed(seed)
    history = []
    for epoch in range(1, epochs+1):
        model.train()
        order = torch.randperm(len(xt), generator=generator)
        total = 0.
        for start in range(0, len(xt), 64):
            ix = order[start:start+64]
            loss, _ = loss_fn(model(xt[ix]), yt[ix], None)
            optimizer.zero_grad(set_to_none=True)
            loss.backward()
            torch.nn.utils.clip_grad_norm_(model.parameters(), 1.)
            optimizer.step()
            total += float(loss.detach()) * len(ix)
        history.append({'epoch': epoch, 'loss': total / len(xt)})
        if epoch % 20 == 0 or epoch == epochs:
            print(json.dumps({'epoch': epoch, 'dimension': x.shape[1], 'train_n': len(xt),
                              'loss': history[-1]['loss']}, ensure_ascii=False), flush=True)
    # 固定最后一轮；不看留出组选择epoch或重新校准温度／阈值。
    return model.eval(), history


def score(z, arrays, temperature, threshold, min_confidence, subset=None):
    n = len(arrays['y5'])
    subset = np.ones(n, dtype=bool) if subset is None else subset
    probabilities = softmax_np(z / temperature)
    energy = energy_np(z, temperature)
    final = decide(probabilities, energy, threshold, min_confidence)
    argmax = probabilities.argmax(1)
    known = subset & arrays['supervised5_mask']
    confusion = np.zeros((5, 5), dtype=int)
    final_confusion = np.zeros((5, 6), dtype=int)
    for i in np.flatnonzero(known):
        confusion[arrays['y5'][i], argmax[i]] += 1
        final_confusion[arrays['y5'][i], 5 if final[i] < 0 else final[i]] += 1
    def distribution(mask):
        return {'count': int(mask.sum()), 'argmax': {k: int(np.sum(mask & (argmax == j))) for j, k in enumerate(CLASS_KEYS)},
                'final': {k: int(np.sum(mask & (final == j))) for j, k in enumerate(CLASS_KEYS)},
                'rejected': int(np.sum(mask & (final < 0)))}
    return {'known_count': int(known.sum()),
            'argmax_agree': int(np.sum(known & (argmax == arrays['y5']))),
            'final_agree': int(np.sum(known & (final == arrays['y5']))),
            'known_rejected': int(np.sum(known & (final < 0))),
            'known_wrong_accepted': int(np.sum(known & (final >= 0) & (final != arrays['y5']))),
            'argmax_confusion': confusion.tolist(), 'final_confusion_including_reject': final_confusion.tolist(),
            'per_class': {k: dict(distribution(known & (arrays['y5'] == j)),
                                 argmax_agree=int(confusion[j,j]), final_agree=int(final_confusion[j,j]))
                          for j, k in enumerate(CLASS_KEYS)},
            'excluded_output_only': {k or 'blank': distribution(subset & (arrays['label_original'] == k))
                                     for k in ('disgust', 'ambiguous', '')},
            'confused_output_only': distribution(subset & arrays['confused_candidate_mask']),
            'rows': [{'id': str(arrays['id'][i]), 'group': str(arrays['group'][i]),
                      'label_original': str(arrays['label_original'][i]), 'y5': int(arrays['y5'][i]),
                      'probabilities': probabilities[i].tolist(), 'energy': float(energy[i]),
                      'argmax': CLASS_KEYS[argmax[i]], 'final': 'unknown' if final[i] < 0 else CLASS_KEYS[final[i]]}
                     for i in np.flatnonzero(subset)]}


def run(annotation, out, epochs=80, seed=42):
    out = Path(out).resolve()
    if not out.is_relative_to((ROOT/'artifacts/laila_v2_candidate').resolve()) or out.exists():
        raise ValueError('输出须是本项目artifacts下尚不存在的独立目录')
    if not Path(annotation).resolve().is_relative_to((ROOT/'artifacts/laila_v2_candidate').resolve()):
        raise ValueError('只接受本地显式标注记录')
    if not 1 <= epochs <= 100:
        raise ValueError('固定预算1–100轮')
    t0 = time.time()
    manifest, arrays, source = prepare(annotation, require_all_classes=True)
    baseline, ckpt, canonical = load_checkpoint(RUN_PATH)
    if ckpt['class_keys'] != CLASS_KEYS or ckpt['train_rig_hash'] != manifest['rig_hash']:
        raise ValueError('旧checkpoint与标签／绑定不匹配')
    if not np.array_equal(arrays['x51'], arrays['x59'][:,:51]):
        raise ValueError('成对特征前51维不一致')
    groups = sorted(set(arrays['group'].tolist()))
    if set(groups) != {'round20261003-sweep', 'round20261003-pairs'}:
        raise ValueError('本次预算仅适用已审查r696两组；新组需要另审计划')
    heldout = arrays['group'] == 'round20261003-pairs'
    fitting = train_mask(arrays, 'round20261003-sweep', 'round20261003-pairs')
    full = train_mask(arrays)
    baseline_replay = replay(manifest, arrays, MODEL_PATH)
    for field in ('temperature', 'energy_threshold', 'min_confidence'):
        if ckpt[field] != read_json(MODEL_PATH.with_suffix('.json'))[field]:
            raise ValueError('旧checkpoint与部署判定参数不一致')
    baseline_z = logits(baseline, arrays['x51'])
    p = softmax_np(baseline_z / ckpt['temperature'])
    expected = np.asarray([list(r['probabilities'].values()) for r in baseline_replay['rows']])
    if np.max(np.abs(p-expected)) > 1e-5:
        raise ValueError('旧checkpoint与真实ONNX重放不一致')
    out.mkdir(parents=True)
    backup = out/'baseline-backup'
    backup.mkdir()
    originals = {'deployment.onnx': MODEL_PATH, 'deployment.json': MODEL_PATH.with_suffix('.json'),
                 'ckpt.pt': RUN_PATH/'ckpt.pt', 'rig.yaml': RIG_PATH,
                 'canonical.yaml': ROOT/'configs/canonical.yaml', 'annotation-original.json': Path(annotation)}
    fingerprints = {}
    for name, path in originals.items():
        original = path.read_bytes()
        (backup/name).write_bytes(original)
        fingerprints[reference(path)] = digest(path)
        if digest(backup/name) != digest(path):
            raise ValueError('回滚备份字节不一致')
    # 实际从备份加载ONNX和checkpoint，核对恢复后的概率和energy。
    import onnxruntime as ort
    options = ort.SessionOptions()
    options.intra_op_num_threads = options.inter_op_num_threads = 1
    restored = ort.InferenceSession(str(backup/'deployment.onnx'), sess_options=options, providers=['CPUExecutionProvider'])
    rp, re = restored.run(['probs', 'energy'], {'sliders': arrays['raw17']})
    restored_model, _, _ = load_checkpoint(backup)
    restore_errors = {'probabilities': float(np.max(np.abs(rp-p))),
                      'energy': float(np.max(np.abs(re-energy_np(baseline_z, ckpt['temperature'])))),
                      'checkpoint_logits': float(np.max(np.abs(logits(restored_model, arrays['x51'])-baseline_z)))}
    if max(restore_errors.values()) > 1e-5:
        raise ValueError('备份恢复推理核验失败')
    print(json.dumps({'backup_verified': True, 'restore_errors': restore_errors, 'training_starts': True}), flush=True)
    np.savez_compressed(out/'paired-inputs.npz', **arrays)
    write_json(out/'source-manifest.json', manifest)
    shutil.copyfile(Path(__file__), out/'experiment-script.py')
    config = {'seed': seed, 'epochs': epochs, 'lr': .0003, 'batch_size': 64, 'weight_decay': .05,
              'label_smoothing': .1, 'logit_adjust_tau': 1., 'model': 'warm-start-existing-ResMLP',
              'train_group': 'round20261003-sweep', 'heldout_group': 'round20261003-pairs',
              'augment': False, 'synthetic': False, 'outlier_exposure': False,
              'epoch_selection': 'fixed-final-no-holdout-selection',
              'candidate_rejection_policy': 'frozen-baseline-T-threshold-for-diagnostic-only-not-calibrated',
              'temperature': ckpt['temperature'], 'energy_threshold': ckpt['energy_threshold'],
              'min_confidence': ckpt['min_confidence'], 'class_keys': CLASS_KEYS}
    write_json(out/'config.json', config)
    summaries = {'baseline': {'all_dev': score(baseline_z, arrays, ckpt['temperature'], ckpt['energy_threshold'], ckpt['min_confidence']),
                              'heldout_pairs': score(baseline_z, arrays, ckpt['temperature'], ckpt['energy_threshold'], ckpt['min_confidence'], heldout)}}
    torch.set_num_threads(2)
    torch.use_deterministic_algorithms(True)
    for dimension in (51, 59):
        x = arrays['x'+str(dimension)]
        for purpose, mask in [('group_holdout', fitting), ('all_dev_fit', full)]:
            name = f'candidate{dimension}_{purpose}'
            model, history = fit(baseline, x, arrays['y5'], mask, seed, epochs)
            z = logits(model, x)
            # 研究checkpoint独立格式；59D绝不冒用旧51D加载器或部署metadata。
            checkpoint = {'format': 'laila-dev-finetune-research-v1', 'dimension': dimension,
                          'model_cfg': ckpt['model_cfg'], 'state_dict': model.state_dict(),
                          'feature_names': arrays['feature_names'+str(dimension)].tolist(), 'class_keys': CLASS_KEYS,
                          'config': config, 'train_ids': arrays['id'][mask].tolist(),
                          'train_groups': sorted(set(arrays['group'][mask].tolist())),
                          'source_annotation_sha256': manifest['source']['sha256'], 'source_revision': manifest['source']['revision'],
                          'history': history, 'deployment_compatible': False, 'rejection_calibrated': False}
            torch.save(checkpoint, out/(name+'.pt'))
            restored_candidate = expanded_model(baseline, dimension)
            restored_candidate.load_state_dict(torch.load(out/(name+'.pt'), weights_only=False)['state_dict'])
            np.testing.assert_array_equal(logits(restored_candidate, x), z)
            summaries[name] = {'training_n': int(mask.sum()),
                               'all_dev': score(z, arrays, ckpt['temperature'], ckpt['energy_threshold'], ckpt['min_confidence']),
                               'heldout_pairs': score(z, arrays, ckpt['temperature'], ckpt['energy_threshold'], ckpt['min_confidence'], heldout),
                               'heldout_is_training_data': purpose == 'all_dev_fit'}
    distances = np.linalg.norm(arrays['raw17'][heldout,None,:]-arrays['raw17'][fitting][None,:,:], axis=2)
    preserved = {path: digest(ROOT/path) == sha for path, sha in fingerprints.items() if not path.startswith('external-fixture/')}
    # 项目外的实际部署路径也要直接复核，不能因reference匿名降级跳过。
    for name, path in originals.items():
        if digest(path) != digest(backup/name):
            raise ValueError('训练期间原标签／部署／配置发生变化')
    result = {'status': 'development-only-bounded-training-no-independent-test', 'seed': seed,
              'revision': manifest['source']['revision'], 'config': config, 'baseline_restore_errors': restore_errors,
              'source_unchanged': all(preserved.values()), 'deployment_unchanged': True,
              'git_head': subprocess.check_output(['git','rev-parse','HEAD'], cwd=ROOT, text=True).strip(),
              'versions': {'python': sys.version.split()[0], 'numpy': np.__version__, 'torch': torch.__version__},
              'command': ['python','-m','analysis.laila_v2_candidate.retrain_candidate','--annotations',reference(annotation),
                          '--out',reference(out),'--epochs',str(epochs),'--seed',str(seed)],
              'heldout_nearest_raw17_distance': distances.min(1).tolist(), 'summaries': summaries,
              'limitations': ['Single-user dev opinions, not independent consensus or golden truth.',
                              'Only two recipe groups; pairs holdout has six surprise/fear rows and no other classes.',
                              'Existing dev observations influenced this experiment; group holdout is not locked test.',
                              'All-dev-fit metrics are resubstitution, not accuracy estimates.',
                              'Frozen baseline rejection values are diagnostic for changed logits; no calibrated candidate threshold.',
                              'Disgust acceptance is outside-five-class acceptance; ambiguous/blank/confused are output-only, not OOD truth.',
                              '59D research input is incompatible with existing 51D classifier/export metadata.'],
              'seconds': time.time()-t0}
    write_json(out/'comparison.json', result)
    checksums = {p.relative_to(out).as_posix(): digest(p) for p in out.rglob('*') if p.is_file()}
    write_json(out/'artifact-hashes.json', checksums)
    print(json.dumps({'success': True, 'out': reference(out), 'seconds': result['seconds'],
                      'results': {k:{s:{key:value for key,value in v[s].items() if key in
                                    ('known_count','argmax_agree','final_agree','known_rejected','known_wrong_accepted')}
                                     for s in ('all_dev','heldout_pairs')} for k,v in summaries.items()}}, ensure_ascii=False), flush=True)
    return result


def main():
    parser = argparse.ArgumentParser(description='现有ResMLP有界单人dev微调；不修改部署／标签，不采集或扩类')
    parser.add_argument('--annotations', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--epochs', type=int, default=80)
    parser.add_argument('--seed', type=int, default=42)
    args = parser.parse_args()
    run(args.annotations, args.out, args.epochs, args.seed)


if __name__ == '__main__':
    main()
