"""手动CLI一次执行；5秒可证伪锚点：终端结束状态及本地产物mtime。
退出：进程完成即退出，不常驻。只读既有候选/合成验证与标定缓存，不调人工锁测。
"""
import json
from pathlib import Path
import numpy as np
from exprnet.models import load_checkpoint
from exprnet.evaluate import logits_of, rig_of_ckpt
from exprnet.calibrate import energy_np, softmax_np


def quantiles(x):
    return dict(zip(['min', 'p05', 'p50', 'p95', 'max'], np.quantile(x, [0, .05, .5, .95, 1]).tolist())) if len(x) else None


def main():
    runs = {'locked12': 'laila_cand_locked12_s42_20261003', 'full17': 'laila_cand_v2_s42_20261003'}
    result = {'status': 'synthetic-development-diagnostic-only', 'deployment_changed': False, 'models': {}}
    cached = {}
    for name, folder in runs.items():
        path = Path('artifacts/runs') / folder
        model, ckpt, canonical = load_checkpoint(path)
        d = np.load(path / 'eval_data.npz')
        logits = logits_of(model, d['xp_val'], canonical.mask_vector)
        p = softmax_np(logits / ckpt['temperature']); e = energy_np(logits, ckpt['temperature'])
        cal_logits = logits_of(model, d['negp_calib'], canonical.mask_vector)
        ec = energy_np(cal_logits, ckpt['temperature']); pc = softmax_np(cal_logits / ckpt['temperature'])
        y = d['y_val']; top = p.argmax(1); conf = p.max(1)
        threshold = ckpt['energy_threshold']; low = conf < ckpt['min_confidence']; high = e > threshold; reject = low | high
        def rates(t, e=e, low=low, top=top, y=y, ec=ec, pc=pc, confidence=ckpt['min_confidence']):
            r = (e > t) | low
            return {'threshold': float(t), 'known_reject': float(r.mean()), 'known_final_accuracy': float(((top == y) & ~r).mean()),
                    'calibration_negative_reject': float(((ec > t) | (pc.max(1) < confidence)).mean())}
        rig = rig_of_ckpt(ckpt, canonical)
        s, xp = rig.project(d['x_val'], canonical.mask_vector)
        raw = np.maximum(s, 0) @ rig.W_pos + np.maximum(-s, 0) @ rig.W_neg
        residual = np.linalg.norm((d['x_val'] - xp) * canonical.mask_vector, axis=1)
        row = {'n': len(y), 'temperature': ckpt['temperature'], 'energy_threshold': threshold, 'min_confidence': ckpt['min_confidence'],
               'argmax_correct': int((top == y).sum()), 'final_correct': int(((top == y) & ~reject).sum()),
               'rejected': int(reject.sum()), 'rejected_argmax_correct': int((reject & (top == y)).sum()),
               'rejected_argmax_incorrect': int((reject & (top != y)).sum()), 'energy_only_rejected': int((high & ~low).sum()),
               'confidence_only_rejected': int((low & ~high).sum()), 'both_rejected': int((high & low).sum()),
               'score_distribution_rejected': {'energy': quantiles(e[reject]), 'max_probability': quantiles(conf[reject])},
               'rejected_samples': [{'validation_index': int(i), 'true_class': ckpt['class_keys'][y[i]], 'argmax': ckpt['class_keys'][top[i]],
                                     'argmax_correct': bool(top[i] == y[i]), 'energy': float(e[i]), 'max_probability': float(conf[i]),
                                     'confidence_triggered': bool(low[i])} for i in np.flatnonzero(reject)],
               'per_class': [], 'threshold_curve': [],
               'projection': {'cached_max_error': float(abs(xp - d['xp_val']).max()), 'residual_all': quantiles(residual),
                              'residual_rejected': quantiles(residual[reject]), 'samples_with_preclip_saturation': int((raw > 1 + 1e-6).any(1).sum()),
                              'axes_at_bound_fraction': ((abs(s - rig.s_min) < 1e-6) | (abs(s - rig.s_max) < 1e-6)).mean(0).tolist(),
                              'axes_at_maximum_fraction': (abs(s - rig.s_max) < 1e-6).mean(0).tolist(),
                              'max_positive_sum': float(rig.pos_basis.sum(0).max()), 'max_negative_sum': float(rig.neg_basis.sum(0).max()),
                              'keys': rig.keys}, 'current_rates': rates(threshold)}
        for i, key in enumerate(ckpt['class_keys']):
            mask = y == i; r = mask & reject
            row['per_class'].append({'key': key, 'n': int(mask.sum()), 'rejected': int(r.sum()), 'energy': quantiles(e[r]),
                                     'max_probability': quantiles(conf[r]), 'rejected_correct': int((r & (top == y)).sum())})
        for target in [.90, .925, .95, .975, .99]:
            q = rates(np.quantile(ec, 1 - target)); q['target_negative_tnr'] = target; row['threshold_curve'].append(q)
        for recall in [.95, .97, .99]:
            q = rates(np.quantile(e, recall)); q['target_known_energy_pass'] = recall; row['threshold_curve'].append(q)
        result['models'][name] = row
        cached[name] = (rates, d['x_val'].copy(), y.copy())
    for name, (rates, _, _) in cached.items():
        result['models'][name]['same_absolute_threshold'] = {other: rates(row['energy_threshold']) for other, row in result['models'].items()}
    result['identical_raw_validation'] = bool(np.array_equal(cached['full17'][1], cached['locked12'][1]))
    result['identical_labels'] = bool(np.array_equal(cached['full17'][2], cached['locked12'][2]))
    result['limitations'] = ['Single seed, synthetic only; not human accuracy.', 'Absolute energy thresholds are not portable between trained models/temperatures.',
                            'Calibration curves are development diagnostics, not a threshold selection on heldout/human locked tests.',
                            'Binding coefficients remain candidate approximations; no geometry changed.']
    out = Path('artifacts/laila_v2_candidate/rejection-diagnosis.json')
    out.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({k: {f: v[f] for f in ['n','energy_threshold','rejected','rejected_argmax_correct','rejected_argmax_incorrect','energy_only_rejected','confidence_only_rejected','both_rejected','current_rates','threshold_curve','projection']} for k,v in result['models'].items()}, ensure_ascii=False, indent=2))


if __name__ == '__main__': main()
