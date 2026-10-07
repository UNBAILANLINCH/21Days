"""手动CLI：最新意见复评及限定五类开发实验，完成／失败即退出。
复用适配器、旧微调和校准函数；旧脚本没有逐样本训练来源核查、跨修订复评、
简单基线及校准角色分离，故在此编排，不改主训练、标注器或Unity部署。
"""
from __future__ import annotations

import argparse
import json
import shutil
import sys
from pathlib import Path

import numpy as np
import torch

from analysis.laila_v2_candidate.annotation_adapter import CLASS_KEYS, reference, replay
from analysis.laila_v2_candidate.capture_diagnostics import ROOT, MODEL_PATH, RUN_PATH, digest, read_json
from analysis.laila_v2_candidate.retrain_candidate import expanded_model, fit, logits, score, train_mask, write_json
from exprnet.calibrate import energy_np, threshold_id_pass
from exprnet.models import load_checkpoint

OLD = ROOT/'artifacts/laila_v2_candidate/retrain-r696-s42-20261004-v1'
SENSITIVE = {'879d75c1bd2547cbad1b18210a0e2735', '34dd7013f89b4ac29efbace05eac0f0d',
             '0f35a239c482424296dd55201716f436', '64f270a804c04b119d51d6f4ba38fa78'}


def verify_bundle(bundle):
    manifest = read_json(bundle/'manifest.json')
    for name, expected in manifest['files'].items():
        path = (bundle/name).resolve()
        if not path.is_relative_to(bundle.resolve()) or digest(path) != expected:
            raise ValueError('冻结包文件hash不符／路径越界')
    with np.load(bundle/'features.npz', allow_pickle=False) as saved:
        arrays = {name: saved[name].copy() for name in saved.files}
    if not np.array_equal(arrays['id'], [r['id'] for r in manifest['rows']]):
        raise ValueError('冻结包ID顺序不符')
    return manifest, arrays


def audit_rows(result, arrays, train_ids, old_labels=None):
    train = np.isin(arrays['id'], list(train_ids))
    for i, row in enumerate(result['rows']):
        row['in_candidate_training'] = bool(train[i])
        row['source_group_seen_in_training'] = bool(np.any(train & (arrays['group'] == arrays['group'][i])))
        row['training_label_original'] = (old_labels or {}).get(row['id'])
        row['label_changed_since_training'] = (bool(train[i]) and row['id'] in (old_labels or {}) and
                                              old_labels[row['id']] != row['label_original'])
        if train.any():
            row['nearest_train_raw17_distance'] = float(np.linalg.norm(arrays['raw17'][train]-arrays['raw17'][i], axis=1).min())
            row['same_raw17_as_train'] = bool(np.any(np.all(arrays['raw17'][train] == arrays['raw17'][i], axis=1)))
        row['explicit_label_with_confused_note'] = row['id'] in SENSITIVE
    for j, key in enumerate(CLASS_KEYS):
        rows = [r for r in result['rows'] if r['y5'] == j]
        entry = result['per_class'][key]
        entry['correct_but_rejected'] = sum(r['argmax'] == key and r['final'] == 'unknown' for r in rows)
        entry['wrong_accepted'] = sum(r['final'] not in (key, 'unknown') for r in rows)
        entry['argmax_recall'] = entry['argmax_agree']/len(rows) if rows else None
        entry['final_recall'] = entry['final_agree']/len(rows) if rows else None
    result['correct_but_rejected'] = sum(v['correct_but_rejected'] for v in result['per_class'].values())
    result['training_overlap_known'] = int(np.sum(train & arrays['supervised5_mask']))
    result['sensitive_four_rows'] = [r for r in result['rows'] if r['id'] in SENSITIVE]
    return result


def partial_calibration(z, arrays, train, calibration, temperature):
    if np.any(train & calibration) or set(arrays['group'][train]) & set(arrays['group'][calibration]):
        raise ValueError('训练／校准组泄漏')
    if not calibration.any() or not np.all(arrays['supervised5_mask'][calibration]):
        raise ValueError('校准只接受非空已知人工标签')
    for raw in arrays['raw17'][calibration]:
        if np.any(np.all(arrays['raw17'][train] == raw, axis=1)):
            raise ValueError('训练／校准相同姿态泄漏')
    threshold = threshold_id_pass(energy_np(z[calibration], temperature), tpr=.95)
    return {'energy_threshold': threshold, 'temperature': temperature, 'min_confidence': .4,
            'rule': 'fixed-ID95-known-only-development-probe', 'target_known_pass': .95,
            'calibration_ids': arrays['id'][calibration].tolist(), 'test_ids': [],
            'calibration_class_support': sorted(set(arrays['y5'][calibration].tolist())),
            'full_rejection_calibrated': False,
            'limitation': '仅6张惊讶/恐惧开发校准，无独立测试、其余四类校准或人工unknown；不用于部署。'}


def centroid_logits(x, y, train):
    if not train.any() or set(y[train].tolist()) != set(range(5)):
        raise ValueError('质心基线训练须覆盖五类')
    scale = x[train].std(axis=0)
    scale[scale < 1e-6] = 1.
    centers = np.stack([x[train & (y == j)].mean(axis=0) for j in range(5)])
    return -np.square((x[:,None,:]-centers[None,:,:])/scale).mean(axis=2)


def feature_conflicts(arrays):
    known = np.flatnonzero(arrays['supervised5_mask'])
    results = {}
    for feature in ('raw17', 'x51', 'x59'):
        conflicts = []
        for position, i in enumerate(known):
            for j in known[position+1:]:
                if arrays['y5'][i] != arrays['y5'][j] and np.array_equal(arrays[feature][i],arrays[feature][j]):
                    conflicts.append({'ids':[str(arrays['id'][i]),str(arrays['id'][j])],
                                      'pages':[int(i+1),int(j+1)],
                                      'labels':[str(arrays['label_original'][i]),str(arrays['label_original'][j])]})
        results[feature] = conflicts
    return results


def run(bundle, out, targeted=False, prior_control=False):
    bundle, out = Path(bundle).resolve(), Path(out).resolve()
    artifact_root = (ROOT/'artifacts/laila_v2_candidate').resolve()
    if not bundle.is_relative_to(artifact_root) or not out.is_relative_to(artifact_root) or out.exists():
        raise ValueError('只接受本地冻结包和未存在的独立输出目录')
    manifest, arrays = verify_bundle(bundle)
    baseline, ckpt, _ = load_checkpoint(RUN_PATH)
    old_manifest = read_json(OLD/'source-manifest.json')
    old_hashes = read_json(OLD/'artifact-hashes.json')
    if old_manifest['rig_hash'] != manifest['rig_hash'] or old_manifest['dataset_version'] != manifest['dataset_version']:
        raise ValueError('历史候选与当前绑定／数据版本不符')
    old_labels = {r['id']: r['label_original'] for r in old_manifest['rows']}
    protected = {str(p): digest(p) for p in (MODEL_PATH, MODEL_PATH.with_suffix('.json'), RUN_PATH/'ckpt.pt',
                  bundle/'annotation-original.json', ROOT/manifest['source']['annotation'])}
    out.mkdir(parents=True)
    sources = [Path(__file__), Path(__file__).with_name('retrain_candidate.py'), ROOT/'exprnet/losses.py',
               ROOT/'exprnet/calibrate.py', ROOT/'exprnet/models.py']
    for path in sources:
        shutil.copyfile(path,out/(path.stem+'-source.py'))
    torch.set_num_threads(2)
    torch.use_deterministic_algorithms(True)
    results = {}
    z = logits(baseline, arrays['x51'])
    actual = replay(manifest, arrays, MODEL_PATH)
    expected = np.array([list(r['probabilities'].values()) for r in actual['rows']])
    from exprnet.calibrate import softmax_np
    if np.max(np.abs(softmax_np(z/ckpt['temperature'])-expected)) > 1e-5:
        raise ValueError('旧checkpoint与部署ONNX不同')
    def report(name, values, train_ids=(), labels=None, policy=None):
        policy = policy or ckpt
        result = score(values, arrays, policy['temperature'], policy['energy_threshold'], policy['min_confidence'])
        results[name] = audit_rows(result, arrays, train_ids, labels)
        results[name]['policy'] = {k: policy[k] for k in ('temperature','energy_threshold','min_confidence')}
        return result
    report('deployment51', z)
    for dimension in (51, 59):
        for purpose in ('group_holdout', 'all_dev_fit'):
            name = f'candidate{dimension}_{purpose}'
            if digest(OLD/(name+'.pt')) != old_hashes[name+'.pt']:
                raise ValueError('历史候选checkpoint hash不符')
            saved = torch.load(OLD/(name+'.pt'), weights_only=False)
            if saved['source_annotation_sha256'] != old_manifest['source']['sha256'] or saved['source_revision'] != 696:
                raise ValueError('历史候选训练来源不符')
            model = expanded_model(baseline, dimension)
            model.load_state_dict(saved['state_dict'])
            report('r696_'+name, logits(model, arrays['x'+str(dimension)]), saved['train_ids'], old_labels)
    write_json(out/'initial-evaluation.json', {'revision': manifest['source']['revision'], 'models': results})
    print(json.dumps({'stage':'initial-evaluation','revision':manifest['source']['revision'],
         'counts':manifest['raw_label_counts'],'known':manifest['class_counts5'],
         'results':{k:{s:v[s] for s in ('argmax_agree','final_agree','known_rejected','correct_but_rejected','known_wrong_accepted')}
                    for k,v in results.items()}}, ensure_ascii=False), flush=True)
    training = train_mask(arrays, 'round20261003-sweep', 'round20261003-pairs')
    calibration = arrays['supervised5_mask'] & (arrays['group'] == 'round20261003-pairs')
    plan = {'train_ids':arrays['id'][training].tolist(),'calibration_ids':arrays['id'][calibration].tolist(),
            'test_ids':[], 'train_groups':['round20261003-sweep'], 'calibration_groups':['round20261003-pairs'],
            'independent_test_available':False, 'all_five_class_calibration_available':False,
            'limitation':'只有两个近邻来源组，无法形成互斥训练/校准/测试；校准组仅惊讶/恐惧，不冒拆sweep。'}
    write_json(out/'split-plan.json', plan)
    if targeted or prior_control:
        config = {'seed':42,'epochs':80,'lr':.0003,'logit_adjust_tau':1. if prior_control else 0.,'label_smoothing':.1,
                  'optimizer':'AdamW','weight_decay':.05,'batch_size':64,'grad_clip':1.,
                  'synthetic':False,'outlier_exposure':False,'augment':False,'torch_threads':2,
                  'reason':'固定类别先验对照；同一r779训练组、初始权重与预算，仅控制tau，非扫参。',
                  'selection':'fixed-final-epoch-no-calibration-or-test-model-selection', 'deployment_compatible':False}
        write_json(out/'training-config.json', config)
        variants = ((59,False),) if prior_control else ((51,False),(59,False),(59,True))
        for dimension, exclude_sensitive in variants:
            mask = training & (~np.isin(arrays['id'],list(SENSITIVE)) if exclude_sensitive else True)
            name = f'new{dimension}_tau'+('1' if prior_control else '0')+('_without_sensitive_four' if exclude_sensitive else '')
            model, history = fit(baseline, arrays['x'+str(dimension)], arrays['y5'],mask,
                                 seed=42,epochs=80,logit_adjust_tau=config['logit_adjust_tau'])
            values = logits(model, arrays['x'+str(dimension)])
            torch.save({'format':'laila-targeted-development-v1','dimension':dimension,'state_dict':model.state_dict(),
                        'train_ids':arrays['id'][mask].tolist(),'source_revision':manifest['source']['revision'],
                        'source_sha256':manifest['source']['sha256'],'config':config,'history':history},out/(name+'.pt'))
            restored = expanded_model(baseline,dimension)
            restored.load_state_dict(torch.load(out/(name+'.pt'),weights_only=False)['state_dict'])
            np.testing.assert_array_equal(logits(restored,arrays['x'+str(dimension)]),values)
            report(name+'_old_rejection',values,arrays['id'][mask],dict(zip(arrays['id'],arrays['label_original'])))
            policy = partial_calibration(values,arrays,mask,calibration,ckpt['temperature'])
            write_json(out/(name+'-partial-calibration.json'),policy)
            result = report(name+'_partial_calibration',values,arrays['id'][mask],dict(zip(arrays['id'],arrays['label_original'])),policy)
            result['calibration_scope'] = policy
            result['calibration_metrics'] = score(values,arrays,policy['temperature'],policy['energy_threshold'],.4,calibration)
        cz = centroid_logits(arrays['x59'],arrays['y5'],training)
        cp = partial_calibration(cz,arrays,training,calibration,1.)
        report('centroid59_uncalibrated_classification_partial_rejection',cz,arrays['id'][training],policy=cp)
        results['centroid59_uncalibrated_classification_partial_rejection']['calibration_scope'] = cp
    if any(digest(Path(p)) != sha for p,sha in protected.items()):
        raise ValueError('评估期间受保护标签／模型改变')
    summary = {'status':'single-user-group-development-no-independent-test','revision':manifest['source']['revision'],
       'annotation_sha256':manifest['source']['sha256'],'source_bundle':reference(bundle),'split_plan':plan,
       'feature_conflicts':feature_conflicts(arrays),
       'known_with_confused_note':int(np.sum(arrays['supervised5_mask'] & arrays['confused_candidate_mask'])),
       'implementation_sha256':{reference(p):digest(p) for p in sources},
       'protected_source_sha256':{reference(p):sha for p,sha in protected.items()},
       'historical_candidates_sha256':{k:v for k,v in old_hashes.items() if k.endswith('.pt')},
       'versions':{'python':sys.version.split()[0],'numpy':np.__version__,'torch':torch.__version__},
       'command':['python','-m','analysis.laila_v2_candidate.latest_review','--bundle',reference(bundle),
                  '--out',reference(out)]+(['--targeted'] if targeted else ['--prior-control'] if prior_control else []),
       'models':results,'protected_unchanged':True,'deployment_changed':False,
       'limitations':['旧候选大量样本已在训练中；新标注不等于新独立姿态。',
                      '预定三个微调及一个同数据先验对照固定预算；不以校准组选择维度、参数或epoch。',
                      '6张单类校准仅开发探针，不能决定五类部署阈值；无人工unknown校准。',
                      '质心概率未校准，energy尺度与ResMLP不同，只比较类别去向与各自开发拒识。',
                      '疑惑备注按原标签；敏感性实验只排除指定四样本，不改源数据。']}
    write_json(out/'evaluation.json',summary)
    write_json(out/'artifact-hashes.json',{p.relative_to(out).as_posix():digest(p) for p in out.rglob('*') if p.is_file()})
    print(json.dumps({'success':True,'out':reference(out),'targeted':targeted,'deployment_changed':False}),flush=True)
    return summary


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='最新五类标注复评与有限研究，不部署')
    parser.add_argument('--bundle',type=Path,required=True)
    parser.add_argument('--out',type=Path,required=True)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument('--targeted',action='store_true')
    mode.add_argument('--prior-control',action='store_true')
    args = parser.parse_args()
    run(args.bundle,args.out,args.targeted,args.prior_control)
