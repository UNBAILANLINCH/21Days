"""手动一次现场反馈修复；每20轮打印进度，完成／失败即退出，不操作Unity。
复用现有fit/score/59D映射；旧入口仅接受固定139图，无法显式保存新反馈来源和角色。
单59D、seed42/80轮/tau0，原105＋3怒训练；7反馈与6pairs仅开发回归，不是独立测试。
"""
import argparse
import json
import re
import shutil
import struct
from pathlib import Path

import numpy as np
import torch

from analysis.laila_v2_candidate.annotation_adapter import CLASS_KEYS, reference
from analysis.laila_v2_candidate.capture_diagnostics import (
    ROOT, REPO, MODEL_PATH, RUN_PATH, RIG_PATH, POSITIVE, NEGATIVE, digest, read_json, number)
from analysis.laila_v2_candidate.latest_review import verify_bundle
from analysis.laila_v2_candidate.retrain_candidate import expanded_model, fit, logits, score, write_json
from analysis.laila_v2_candidate.feature_probe import extended_features, decode
from analysis.laila_v2_candidate.export_playtest import preset_rows, PlaytestModel
from exprnet.export import export_onnx, verify_pair
from exprnet.models import load_checkpoint
from exprnet.rig import load_rig
from exprnet.calibrate import softmax_np, energy_np, decide
from exprnet.models import count_params

BASE = ROOT/'artifacts/laila_v2_candidate'
BUNDLE = BASE/'annotation-adapter-r779-20261004-v1'
CANDIDATE = BASE/'targeted-r779-s42-20261004-v2'
RESEARCH = REPO/'Assets/_Project/Data/LailaFaceRecognition/Research/laila_research59_r779.onnx'
SLOTS = {2:'a5ce8c20ec88410eaf65cc3d1ec25d4b', 9:'a1012d8f641f4f56a2210d922214b50b', 10:'237ff2433cbb480f9ba90e8682f61206'}

STRENGTH_SOURCE = BASE/'feedback-entry-ui-verification-20261006-v2/ui-replay.json'
STRENGTH_TRAIN = ('16-PreviousAngry-1', '17-PreviousAngry-2', '20-OldAngry-2')
CURRENT_REPAIR = BASE/'feedback-repair-s42-20261005-v1'
CURRENT_ONNX = REPO/'Assets/_Project/Data/LailaFaceRecognition/Research/FeedbackRepair20261005/laila_research59_feedback_20261005.onnx'


def strength_rows(proof):
    """只接受已保存21个精确配方；三个定向设计标签不扩散到任意扰动。"""
    if proof.get('status') != 'succeeded' or proof.get('completed') != 24 or proof.get('human_labels') is not False:
        raise ValueError('须为已完成的原UI回放，不冒充人工标签')
    expected = preset_rows()
    for branch in ('PreviousAngry', 'OldAngry'):
        for i in range(3):
            expected.append(dict(preset_rows()[12+i], branch=branch))
    rows = proof['rows'][:21]
    if len(rows) != 21 or len({r['name'] for r in rows}) != 21:
        raise ValueError('21配方重复／缺失')
    result = []
    for i, (row, template) in enumerate(zip(rows, expected)):
        values = np.asarray(template['values'], dtype=np.float32)
        if 12 <= i < 18:
            scale = (.5, .75, 1.)[(i-12)%3]
            values[0] = values[3] = -.85*scale
            values[1] = values[4] = -.25*scale
            values[2] = values[5] = .65*scale
            if i < 15: values[7] = values[9] = 0
        raw = np.asarray(row.get('raw17', []), dtype=np.float32)
        if (raw.shape != (17,) or not np.isfinite(raw).all() or
                not np.allclose(raw, values, atol=1e-7, rtol=0) or
                row.get('model') != 'feedback59' or row.get('UI_pointer_event') is not True or
                row.get('automatic_inference_count') != i+2):
            raise ValueError('保存的配方／模型／实际推理来源变化')
        category = min(i//3, 4)
        branch = 'default' if i < 15 else ('original-lower-lid' if i < 18 else 'old-brow')
        name = f'{i:02d}-'+(('Neutral','Happy','Sad','SurpriseFear','Angry')[category] if i < 15
                            else ('PreviousAngry' if i < 18 else 'OldAngry'))+f'-{i%3}'
        if row['name'] != name:
            raise ValueError('固定失败姿态ID／档位变化')
        result.append({'id':name, 'raw17':raw.tolist(), 'y5':category, 'label':CLASS_KEYS[category],
                       'branch':branch, 'strength':('light','medium','strong')[i%3],
                       'prototype_group':('designed-'+CLASS_KEYS[category]) if category < 4 else 'designed-anger-all-comparisons',
                       'role':'training' if name in STRENGTH_TRAIN else 'exposed-development-regression',
                       'source_row':i, 'source':'saved-design-preset-user-authorized-semantic-repair',
                       'human_blind_label':False, 'synthetic_neighborhood':False})
    if tuple(r['id'] for r in result if r['role']=='training') != STRENGTH_TRAIN:
        raise ValueError('固定三个修复成员改变')
    return result


def reject_duplicate_additions(existing_raw, existing_y, additions):
    keys = [tuple(np.round(r['raw17'], 6)) for r in additions]
    if len(set(keys)) != len(keys): raise ValueError('新增姿态重复')
    for row in additions:
        matching = np.all(np.isclose(existing_raw, row['raw17'], atol=1e-6, rtol=0), axis=1)
        if matching.any():
            if np.any(existing_y[matching] != row['y5']): raise ValueError('新增姿态与原标签冲突')
            raise ValueError('新增姿态已存在，不重复计数或改旧角色')


def run_strength_repair(out):
    """手动单次CLI；20轮终端锚点，80轮或任何校验失败退出，无Unity／后台服务。"""
    out = Path(out).resolve()
    if not out.is_relative_to(BASE.resolve()) or out.exists(): raise ValueError('必须是不存在的独立产物目录')
    baseline, ckpt, canonical = load_checkpoint(RUN_PATH)
    rig = load_rig(RIG_PATH, canonical)
    meta = read_json(CURRENT_ONNX.with_suffix('.json'))
    if digest(CURRENT_ONNX) != '9d0999f9164dcbb5b00a93a6d5b7082912abf93a5c07f4478966bc631f8733b2' or meta['onnx']['sha256'] != digest(CURRENT_ONNX):
        raise ValueError('当前反馈59D基线版本改变')
    for name, value in read_json(CURRENT_REPAIR/'artifact-hashes.json').items():
        path = (CURRENT_REPAIR/name).resolve()
        if not path.is_relative_to(CURRENT_REPAIR.resolve()) or digest(path) != value: raise ValueError('原训练产物不完整／hash改变')
    arrays_file = np.load(CURRENT_REPAIR/'paired-inputs.npz', allow_pickle=False)
    arrays = {k:arrays_file[k].copy() for k in arrays_file.files}
    old = expanded_model(baseline, 59)
    source_saved = torch.load(CURRENT_REPAIR/'candidate59.pt', map_location='cpu', weights_only=False)
    old.load_state_dict(source_saved['state_dict']); old.eval()
    source_config = read_json(CURRENT_REPAIR/'config.json')
    if (arrays['training_mask'].sum()!=108 or np.any(arrays['training_mask'] & arrays['calibration_mask']) or
            arrays['id'][arrays['training_mask']].tolist()!=source_config['train_ids']):
        raise ValueError('原108条训练划分改变')
    rows = strength_rows(read_json(STRENGTH_SOURCE))
    additions = [r for r in rows if r['role']=='training']
    reject_duplicate_additions(arrays['raw17'], arrays['y5'], additions)
    for row in rows:
        overlap = np.all(np.isclose(arrays['raw17'][arrays['training_mask']], row['raw17'], atol=1e-6, rtol=0),axis=1)
        row['existing_training_pose_ids'] = arrays['id'][arrays['training_mask']][overlap].tolist()
        if row['role'] != 'training' and overlap.any(): row['role'] = 'existing-training-resubstitution'
    raw = np.asarray([r['raw17'] for r in additions], dtype=np.float32)
    x = extended_features(rig, raw).astype(np.float32); x[:,:51] *= canonical.mask_vector
    if np.max(abs(decode(rig, x)-raw))>1e-6: raise ValueError('59D没有完整保留17轴')
    joined = {key:arrays[key] for key in ('raw17','x59','y5','supervised5_mask','confused_candidate_mask','id','group','label_original')}
    for key, value in {'raw17':raw,'x59':x,'y5':np.full(3,4),'supervised5_mask':np.ones(3,bool),
                      'confused_candidate_mask':np.zeros(3,bool),'id':np.array([r['id'] for r in additions]),
                      'group':np.array(['designed-anger-all-comparisons']*3),'label_original':np.array(['angry']*3)}.items():
        joined[key] = np.concatenate([joined[key], value])
    train = np.concatenate([arrays['training_mask'], np.ones(3,bool)])
    if train.sum()!=111 or np.any(joined['y5'][train]<0): raise ValueError('固定111条监督无效')
    # 同原型未训练档仅为暴露回归，绝不冠以组留出或独立测试。
    controls = [r for r in rows if r['role']!='training']
    if {r['prototype_group'] for r in additions} & {r['prototype_group'] for r in controls} != {'designed-anger-all-comparisons'}:
        raise ValueError('原型关系核对失败')
    protected_paths = [CURRENT_ONNX, CURRENT_ONNX.with_suffix('.json'), MODEL_PATH, MODEL_PATH.with_suffix('.json'),
                       RESEARCH, RESEARCH.with_suffix('.json'), RIG_PATH, RUN_PATH/'ckpt.pt',
                       BUNDLE/'annotation-original.json', STRENGTH_SOURCE, REPO/'.git/index',
                       REPO/'Assets/_Project/Scenes/laila.unity', REPO/'Assets/_Project/Scenes/LailaRecognitionPlaytest.unity']
    protected_paths += [p for p in CURRENT_REPAIR.rglob('*') if p.is_file()]
    protected_paths += [p for p in (BASE/'playtest-feedback/batch-20261005T003226-72aec951').rglob('*') if p.is_file()]
    protected_paths += list((BASE/'dev-annotations').glob('*.json'))
    protected = {p:digest(p) for p in protected_paths}
    out.mkdir(); backup = out/'baseline-backup'; backup.mkdir()
    for i, path in enumerate(protected_paths):
        target = backup/f'{i:03d}-{path.name}'; shutil.copyfile(path,target)
        if digest(target)!=protected[path]: raise ValueError('备份字节不同')
    config = {'seed':42,'epochs':80,'lr':3e-4,'batch_size':64,'weight_decay':.05,'label_smoothing':.1,
              'logit_adjust_tau':0.,'outlier_exposure':False,'gradient_clip':1.,'selection':'fixed-final-one-run-no-search',
              'initialization':'warm-start-current-feedback59-all-weights','model_name':ckpt['model_name'],
              'model_cfg':ckpt['model_cfg'],'parameter_count':count_params(old), 'class_keys':CLASS_KEYS,
              'train_n':int(train.sum()),'class_counts':np.bincount(joined['y5'][train],minlength=5).tolist(),
              'train_ids':joined['id'][train].tolist(),'new_training_ids':list(STRENGTH_TRAIN),
              'not_used_for_optimization_preset_ids':[r['id'] for r in controls if r['role']=='exposed-development-regression'],
              'original_pairs_regression_ids':source_config['calibration_ids'],
              'feedback_regression_ids':source_config['development_control_ids'], 'independent_test_ids':[],
              'temperature':ckpt['temperature'],'energy_threshold':ckpt['energy_threshold'],'min_confidence':ckpt['min_confidence'],
              'policy':'unchanged-stable-five-class-feedback-old-rejection-diagnostic-only',
              'source_hashes':{str(p.relative_to(REPO)).replace('\\','/'):h for p,h in protected.items()}}
    write_json(out/'config.json',config)
    write_json(out/'design-source-manifest.json',{'authorization':'2026-10-06-user-approved-one-targeted-candidate-round',
              'source_sha256':digest(STRENGTH_SOURCE),'rows':rows,'augmentation':False,
              'independent_test':False,'same_prototype_control_is_not_group_holdout':True,
              'nearest_existing_train_distance':[float(np.linalg.norm(arrays['raw17'][arrays['training_mask']]-r,axis=1).min()) for r in raw]})
    np.savez_compressed(out/'paired-inputs.npz',**joined,training_mask=train)
    import onnxruntime as ort
    options=ort.SessionOptions(); options.intra_op_num_threads=options.inter_op_num_threads=1
    source_session=ort.InferenceSession(str(CURRENT_ONNX),sess_options=options,providers=['CPUExecutionProvider'])
    before=logits(old,joined['x59']); bp,be=source_session.run(['probs','energy'],{'sliders':joined['raw17']})
    np.testing.assert_allclose(bp,softmax_np(before/ckpt['temperature']),atol=1e-5,rtol=0)
    np.testing.assert_allclose(be,energy_np(before,ckpt['temperature']),atol=1e-5,rtol=0)
    if not np.all(bp[-3:].argmax(1)==0): raise ValueError('三个原失败姿态已非中性，不启动另一任务')
    print(json.dumps({'training_starts':True,'config':{k:v for k,v in config.items() if k not in ('source_hashes','train_ids')},'baseline_verified':True}),flush=True)
    torch.set_num_threads(2); torch.use_deterministic_algorithms(True)
    model,history=fit(old,joined['x59'],joined['y5'],train,seed=42,epochs=80,logit_adjust_tau=0.)
    after=logits(model,joined['x59'])
    torch.save({'format':'laila-strength-feedback-repair-v1','dimension':59,'state_dict':model.state_dict(),
                'source_revision':779,'config':config,'history':history,'train_ids':config['train_ids'],'rejection_calibrated':False},out/'candidate59.pt')
    restored=expanded_model(baseline,59); restored.load_state_dict(torch.load(out/'candidate59.pt',weights_only=False)['state_dict'])
    np.testing.assert_array_equal(logits(restored,joined['x59']),after)
    path=out/'laila_research59_strength_20261006.onnx'
    checks=export_onnx(PlaytestModel(rig,canonical,model,ckpt['temperature']).eval(),rig,path)
    if not checks['ok']: raise ValueError('ONNX导出自检失败，保留失败候选不接Unity')
    meta['name']=path.stem;meta['onnx']={'file':path.name,'sha256':digest(path),'opset':15,**checks['io']}
    meta['checks']={k:v for k,v in checks.items() if k!='io'}
    meta['research'].update(checkpoint_sha256=digest(out/'candidate59.pt'),strength_training_ids=list(STRENGTH_TRAIN),
                            source_strength_manifest_sha256=digest(out/'design-source-manifest.json'),independent_validation_available=False,rejection_calibrated=False)
    write_json(path.with_suffix('.json'),meta)
    valid,message=verify_pair(path,path.with_suffix('.json'))
    if not valid: raise ValueError(message)
    candidate_session=ort.InferenceSession(str(path),sess_options=options,providers=['CPUExecutionProvider'])
    cp,ce=candidate_session.run(['probs','energy'],{'sliders':joined['raw17']})
    parity={'probabilities':float(np.max(abs(cp-softmax_np(after/ckpt['temperature'])))),
            'energy':float(np.max(abs(ce-energy_np(after,ckpt['temperature'])))),'rows':len(cp)}
    if max(parity['probabilities'],parity['energy'])>1e-5: raise ValueError('实际训练／回归输入ONNX数值不符')
    subsets={'original139':np.arange(len(train))<139,'previous_training108':np.concatenate([arrays['training_mask'],np.zeros(3,bool)]),
             'new_training3':np.arange(len(train))>=149,
             'previous_feedback_control7':np.concatenate([arrays['control_mask'],np.zeros(3,bool)]),
             'preserved_pairs6':np.concatenate([arrays['calibration_mask'],np.zeros(3,bool)])}
    results={key:{name:score(z,joined,ckpt['temperature'],ckpt['energy_threshold'],ckpt['min_confidence'],mask)
                  for name,z in [('before',before),('after',after)]} for key,mask in subsets.items()}
    # 这里仅计算安定后的展示规则；不是Unity跨帧实测，不修改原UI。
    probes=rows+[{'id':'zero','raw17':[0.]*17,'y5':0,'role':'exposed-development-regression'}]
    for axis in range(17):
        for direction in ((1,-1) if axis<14 else (1,)):
            r=[0.]*17;r[axis]=direction*.01
            probes.append({'id':f'tiny-{axis}-{direction}','raw17':r,'y5':0,'role':'numeric-neutral-zone-probe'})
    probe_raw=np.asarray([r['raw17'] for r in probes],dtype=np.float32)
    tol=np.array([.03]*6+[.02]*4+[.03]*4+[.02]*3)
    q=np.max(abs(probe_raw)/tol,axis=1)
    grid=[]
    for name,session in [('before',source_session),('after',candidate_session)]:
        p,e=session.run(['probs','energy'],{'sliders':probe_raw});f=decide(p,e,ckpt['energy_threshold'],ckpt['min_confidence'])
        grid.append({'model':name,'display_verification':'offline-settled-policy-only-not-Unity-runtime',
                     'rows':[dict(row,probabilities=p[i].tolist(),energy=float(e[i]),argmax=CLASS_KEYS[int(p[i].argmax())],
                                  settled_display=CLASS_KEYS[0 if q[i]<=1 else int(p[i].argmax())],
                                  neutral_override=bool(q[i]<=1),normalized_amplitude=float(q[i]),
                                  historical_final='unknown' if f[i]<0 else CLASS_KEYS[int(f[i])]) for i,row in enumerate(probes)]})
    if any(digest(p)!=h for p,h in protected.items()): raise ValueError('受保护原模型／标签／场景／index发生变化')
    outcome={'status':'offline-one-round-completed-not-independent-validation','results':results,'preset_and_neutral_probes':grid,
             'onnx_parity':parity,'export_checks':checks,'protected_unchanged':True,'unity_imported':False,
             'source_model_sha256':digest(CURRENT_ONNX),'candidate_model_sha256':digest(path),
             'limitations':['Three new labels are explicitly authorized design semantics, not new human blind labels.',
                            'All prior human observations and same-prototype controls are exposed development regression.',
                            'No synthetic neighborhoods or independent generalization estimate.',
                            'Settled display is computed offline; Unity importer/Sentis and temporal UI still unverified.']}
    write_json(out/'evaluation.json',outcome)
    shutil.copyfile(Path(__file__),out/'feedback-repair-source.py')
    shutil.copyfile(Path(fit.__code__.co_filename),out/'retrain-candidate-source.py')
    write_json(out/'artifact-hashes.json',{p.relative_to(out).as_posix():digest(p) for p in out.rglob('*') if p.is_file()})
    print(json.dumps({'completed':True,'out':reference(out),'candidate_hash':digest(path),'parity':parity,
                      'results':{k:{n:{f:r[f] for f in ('known_count','argmax_agree','final_agree','known_rejected')} for n,r in v.items()} for k,v in results.items()}}),flush=True)
    return outcome


def load_feedback(batch, rig, meta):
    paths = list(Path(batch).glob('*/record.json'))
    if len(paths) != 10 or list(Path(batch).rglob('record.partial')):
        raise ValueError('须完整十条且无partial，不自动挑选多个批次')
    rows = sorted([read_json(p) for p in paths], key=lambda r:r.get('attempt_slot',0))
    if [r.get('attempt_slot') for r in rows] != list(range(1,11)) or len({r.get('id') for r in rows}) != 10:
        raise ValueError('槽位或UUID重复／缺失')
    keys = [s['key'] for s in meta['sliders']]
    for row in rows:
        if not re.fullmatch('[0-9a-f]{32}',str(row.get('id',''))) or not (Path(batch)/row['id']/'record.json').is_file():
            raise ValueError('UUID与目录不符')
        if (row.get('schema_version') != 1 or row.get('source') != 'single-user-development-feedback'
                or row.get('purpose') != 'single-user-playtest-development-feedback-not-golden'
                or row.get('data_role') != 'playtest-feedback' or row.get('training_eligible') is not False
                or row.get('collection_mode') != 'bounded-development-pilot' or row.get('pilot_limit') != 10
                or row.get('capture') != 'synchronous-click-snapshot-baked-current-face'
                or row.get('scene') != 'Assets/_Project/Scenes/LailaRecognitionPlaytest.unity'
                or 'AUTOMATED' in row.get('note','')):
            raise ValueError('非真实有限开发反馈，不接受fixture或自动训练标记')
        if row.get('prediction_visible_before_selection') is not False or row.get('prediction_exposed_before_save') is not False:
            raise ValueError('预测已暴露，不能进入本轮预定监督')
        if row.get('model',{}).get('onnx_sha256') != meta['onnx']['sha256'] or row['model'].get('rig_hash') != rig.config_hash() or row['model'].get('research') != meta['research']:
            raise ValueError('模型／rig来源版本不符')
        if row.get('raw17_keys') != keys or len(row.get('raw17',[])) != 17:
            raise ValueError('17轴键／顺序不符')
        raw = np.array([number(v,keys[i]) for i,v in enumerate(row['raw17'])])
        if np.any(raw < rig.s_min) or np.any(raw > rig.s_max):
            raise ValueError('raw17越界')
        weights = row.get('weights31',{})
        if set(weights) != set(POSITIVE+NEGATIVE):
            raise ValueError('31权重缺失或额外键')
        restored = []
        for i,name in enumerate(POSITIVE):
            p = number(weights[name],name); n = number(weights[NEGATIVE[i]],NEGATIVE[i]) if i<14 else 0
            if min(p,n)<-.001 or max(p,n)>100.001 or (p>.001 and n>.001):
                raise ValueError('形态范围／互斥冲突')
            restored.append((np.clip(p,0,100)-np.clip(n,0,100))/100)
        if not np.allclose(raw,restored,atol=2e-7,rtol=0):
            raise ValueError('raw17与31权重不一致')
        if row.get('image',{}).get('file') != 'face.png':
            raise ValueError('图片路径非法')
        png = Path(batch)/row['id']/'face.png'
        if not png.is_file() or digest(png)!=row['image']['sha256']:
            raise ValueError('缺图／图片hash变化')
        image = png.read_bytes()
        if len(image)<24 or image[:8]!=b'\x89PNG\r\n\x1a\n' or struct.unpack('>II',image[16:24])!=(1920,1080):
            raise ValueError('PNG契约不符')
        if row.get('user_label') not in CLASS_KEYS or (row['attempt_slot'] in SLOTS and (row['id']!=SLOTS[row['attempt_slot']] or row['user_label']!='angry')):
            raise ValueError('人工标签或预定三个怒样本改变；不按target_hint补标')
        if row['attempt_slot'] not in SLOTS and row['user_label']=='angry':
            raise ValueError('本轮监督成员改变')
    if len({tuple(r['raw17']) for r in rows})!=10:
        raise ValueError('重复姿态')
    return rows


def combine(arrays, rows, rig):
    raw = np.asarray([r['raw17'] for r in rows],dtype=np.float32)
    x59 = extended_features(rig,raw).astype(np.float32)
    x59[:,:51] *= rig.canonical.mask_vector
    if np.max(abs(decode(rig,x59)-raw))>1e-6:
        raise ValueError('59D没有保留17轴')
    group = 'feedback-batch-20261005T003226-72aec951'
    additions = {'raw17':raw,'x59':x59,'y5':np.array([CLASS_KEYS.index(r['user_label']) for r in rows]),
        'supervised5_mask':np.ones(10,dtype=bool),'confused_candidate_mask':np.zeros(10,dtype=bool),
        'id':np.array([r['id'] for r in rows]),'group':np.array([group]*10),
        'label_original':np.array([r['user_label'] for r in rows])}
    if set(additions['id']) & set(arrays['id']):
        raise ValueError('新旧UUID冲突')
    return {key:np.concatenate([arrays[key],value]) for key,value in additions.items()}


def run(batch, out):
    batch,out = Path(batch).resolve(),Path(out).resolve()
    if not batch.is_relative_to((BASE/'playtest-feedback').resolve()) or batch.name!='batch-20261005T003226-72aec951':
        raise ValueError('只接受本轮显式真实批次')
    if not out.is_relative_to(BASE.resolve()) or out.exists():
        raise ValueError('独立新输出目录，拒绝覆盖')
    manifest,arrays = verify_bundle(BUNDLE)
    baseline,ckpt,canonical = load_checkpoint(RUN_PATH)
    rig = load_rig(RIG_PATH,canonical); meta = read_json(RESEARCH.with_suffix('.json'))
    hashes = read_json(CANDIDATE/'artifact-hashes.json')
    source_pt = CANDIDATE/'new59_tau0.pt'
    if digest(source_pt)!=hashes[source_pt.name] or digest(RESEARCH)!=meta['onnx']['sha256']:
        raise ValueError('旧研究模型hash变化')
    rows = load_feedback(batch,rig,meta); joined = combine(arrays,rows,rig)
    split = read_json(CANDIDATE/'split-plan.json')
    train = np.isin(joined['id'],split['train_ids']+list(SLOTS.values()))
    calibration = np.isin(joined['id'],split['calibration_ids'])
    feedback = np.arange(len(joined['id'])) >= len(arrays['id'])
    control = feedback & ~train
    if train.sum()!=108 or calibration.sum()!=6 or control.sum()!=7 or np.any(train&calibration):
        raise ValueError('固定105+3训练／6pairs／7开发回归角色变化')
    for raw in joined['raw17'][calibration]:
        if np.any(np.all(joined['raw17'][train]==raw,axis=1)):
            raise ValueError('训练与校准相同姿态泄漏')
    protected_paths = [MODEL_PATH,MODEL_PATH.with_suffix('.json'),RESEARCH,RESEARCH.with_suffix('.json'),RUN_PATH/'ckpt.pt',source_pt,RIG_PATH,BUNDLE/'annotation-original.json']
    protected_paths += [p for p in batch.rglob('*') if p.is_file()]
    protected = {p:digest(p) for p in protected_paths}
    out.mkdir(); backup = out/'baseline-backup'; backup.mkdir()
    for i,p in enumerate(protected_paths):
        target = backup/(str(i)+'-'+p.name);shutil.copyfile(p,target)
        if digest(target)!=protected[p]:raise ValueError('备份不一致')
    torch.set_num_threads(2); torch.use_deterministic_algorithms(True)
    old = expanded_model(baseline,59)
    saved = torch.load(source_pt,map_location='cpu',weights_only=False)
    old.load_state_dict(saved['state_dict'])
    config = {'seed':42,'epochs':80,'lr':3e-4,'batch_size':64,'tau':0.,'selection':'fixed-final-single-candidate-no-sweep',
        'initialization':'original51-checkpoint-expanded-to59-as-existing-r779-recipe','train_ids':joined['id'][train].tolist(),
        'calibration_ids':joined['id'][calibration].tolist(),'development_control_ids':joined['id'][control].tolist(),
        'independent_test_ids':[],'source_hashes':{reference(p):h for p,h in protected.items()},
        'policy':'unchanged-old-rejection-diagnostic-only-not-recalibrated'}
    write_json(out/'config.json',config)
    write_json(out/'feedback-manifest.json',{'source':reference(batch),'source_records_unchanged':True,
        'training_authorization':'user-approved-three-angry-development-repair','group_role':'single-session-development-not-independent-test',
        'rows':[{'id':r['id'],'record':reference(batch/r['id']/'record.json'),'record_sha256':digest(batch/r['id']/'record.json'),
                 'image_sha256':r['image']['sha256'],'slot':r['attempt_slot'],'label_source':'explicit-user-label-not-target',
                 'user_label':r['user_label'],'target_hint':r['target_hint'],'role':'training' if r['id'] in SLOTS.values() else 'development-regression',
                 'same_raw17_as_original_train':bool(np.any(np.all(arrays['raw17'][np.isin(arrays['id'],split['train_ids'])]==r['raw17'],axis=1))),
                 'nearest_original_train_distance':float(np.linalg.norm(arrays['raw17'][np.isin(arrays['id'],split['train_ids'])]-r['raw17'],axis=1).min())} for r in rows]})
    np.savez_compressed(out/'paired-inputs.npz',**joined,training_mask=train,calibration_mask=calibration,control_mask=control)
    before = logits(old,joined['x59'])
    saved_probabilities = np.asarray([r['prediction']['probabilities'] for r in rows])
    saved_energy = np.asarray([r['prediction']['energy'] for r in rows])
    np.testing.assert_allclose(softmax_np(before[-10:]/ckpt['temperature']),saved_probabilities,atol=1e-5,rtol=0)
    np.testing.assert_allclose(energy_np(before[-10:],ckpt['temperature']),saved_energy,atol=1e-5,rtol=0)
    model,history = fit(baseline,joined['x59'],joined['y5'],train,seed=42,epochs=80,logit_adjust_tau=0.)
    after = logits(model,joined['x59'])
    torch.save({'format':'laila-feedback-repair-v1','dimension':59,'state_dict':model.state_dict(),'config':config,
                'source_revision':779,'train_ids':config['train_ids'],'history':history,'rejection_calibrated':False},out/'candidate59.pt')
    restored = expanded_model(baseline,59);restored.load_state_dict(torch.load(out/'candidate59.pt',weights_only=False)['state_dict'])
    np.testing.assert_array_equal(logits(restored,joined['x59']),after)
    subsets = {'original_known_and_excluded':~feedback,'feedback_training_three':feedback&train,'feedback_control_seven':control,'preserved_pairs_six':calibration}
    results = {name:{'before':score(before,joined,ckpt['temperature'],ckpt['energy_threshold'],ckpt['min_confidence'],mask),
                     'after':score(after,joined,ckpt['temperature'],ckpt['energy_threshold'],ckpt['min_confidence'],mask)} for name,mask in subsets.items()}
    presets = preset_rows()
    for strength,scale in enumerate((.5,.75,1.)):
        v=presets[12+strength]['values'];v[0]=v[3]=-.85*scale;v[1]=v[4]=-.25*scale;v[2]=v[5]=.65*scale
    px=extended_features(rig,np.asarray([p['values'] for p in presets],dtype=np.float32)).astype(np.float32);px[:,:51]*=canonical.mask_vector
    typical=[]
    for name,m in [('before',old),('after',model)]:
        z=logits(m,px);p=softmax_np(z/ckpt['temperature']);e=energy_np(z,ckpt['temperature']);f=decide(p,e,ckpt['energy_threshold'],ckpt['min_confidence'])
        typical.append({'model':name,'rows':[{'title':row['title'],'human_label':None,'argmax':CLASS_KEYS[int(p[i].argmax())],
            'final':'unknown' if f[i]<0 else CLASS_KEYS[f[i]],'probabilities':p[i].tolist(),'energy':float(e[i])} for i,row in enumerate(presets)]})
    if any(digest(p)!=h for p,h in protected.items()):raise ValueError('训练期间原记录／模型变化')
    outcome={'status':'completed-development-repair-not-independent-validation','protected_unchanged':True,'results':results,'typical_faces':typical}
    write_json(out/'evaluation.json',outcome)
    shutil.copyfile(Path(__file__),out/'feedback-repair-source.py')
    write_json(out/'artifact-hashes.json',{p.relative_to(out).as_posix():digest(p) for p in out.rglob('*') if p.is_file()})
    fields = ('known_count','argmax_agree','final_agree','known_rejected','known_wrong_accepted')
    summary = {k:{name:{field:r[field] for field in fields} for name,r in v.items()} for k,v in results.items()}
    print(json.dumps({'completed':True,'out':reference(out),'results':summary},ensure_ascii=False),flush=True)
    return outcome


def export_candidate(repair, out):
    repair,out=Path(repair).resolve(),Path(out).resolve()
    if not repair.is_relative_to(BASE.resolve()) or not out.is_relative_to(BASE.resolve()) or out.exists():
        raise ValueError('仅从本机修复产物导出到独立新目录')
    hashes=read_json(repair/'artifact-hashes.json')
    for name,h in hashes.items():
        path=(repair/name).resolve()
        if not path.is_relative_to(repair) or digest(path)!=h:raise ValueError('修复产物hash变化')
    baseline,ckpt,canonical=load_checkpoint(RUN_PATH);rig=load_rig(RIG_PATH,canonical)
    saved=torch.load(repair/'candidate59.pt',map_location='cpu',weights_only=False)
    classifier=expanded_model(baseline,59);classifier.load_state_dict(saved['state_dict']);classifier.eval()
    deployed=PlaytestModel(rig,canonical,classifier,ckpt['temperature']).eval()
    out.mkdir();path=out/'laila_research59_feedback_20261005.onnx'
    checks=export_onnx(deployed,rig,path)
    if not checks['ok']:raise ValueError('候选ONNX导出自检失败')
    meta=read_json(RESEARCH.with_suffix('.json'))
    meta['name']='laila_research59_feedback_20261005'
    meta['onnx']={'file':path.name,'sha256':digest(path),'opset':15,**checks['io']}
    meta['checks']={k:v for k,v in checks.items() if k!='io'}
    meta['research']['checkpoint_sha256']=digest(repair/'candidate59.pt')
    meta['research']['feedback_training_ids']=list(SLOTS.values())
    meta['research']['source_feedback_manifest_sha256']=digest(repair/'feedback-manifest.json')
    meta['research']['independent_validation_available']=False
    meta['research']['rejection_calibrated']=False
    write_json(path.with_suffix('.json'),meta)
    ok,message=verify_pair(path,path.with_suffix('.json'))
    if not ok:raise ValueError(message)
    import onnxruntime as ort
    arrays=np.load(repair/'paired-inputs.npz',allow_pickle=False)
    session=ort.InferenceSession(str(path),providers=['CPUExecutionProvider'])
    p,e=session.run(['probs','energy'],{'sliders':arrays['raw17'].astype(np.float32)})
    z=logits(classifier,arrays['x59']);dp=float(np.max(abs(p-softmax_np(z/ckpt['temperature']))));de=float(np.max(abs(e-energy_np(z,ckpt['temperature']))))
    if max(dp,de)>1e-5:raise ValueError('新候选ONNX与149条本机研究输入不一致')
    write_json(out/'verification.json',{'checks':checks,'onnx_sha256':digest(path),'rows':len(p),'probability_error':dp,'energy_error':de,
        'assets_changed':False,'independent_validation':False,'rejection_calibrated':False})
    write_json(out/'artifact-hashes.json',{p.name:digest(p) for p in out.iterdir() if p.is_file()})
    print(json.dumps({'exported':True,'out':reference(out),'probability_error':dp,'energy_error':de,'assets_changed':False}),flush=True)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    source=parser.add_mutually_exclusive_group(required=True)
    source.add_argument('--batch',type=Path);source.add_argument('--export-repair',type=Path)
    source.add_argument('--strength-repair',action='store_true')
    parser.add_argument('--out',type=Path,required=True)
    args=parser.parse_args()
    if args.strength_repair:run_strength_repair(args.out)
    elif args.export_repair:export_candidate(args.export_repair,args.out)
    else:run(args.batch,args.out)
