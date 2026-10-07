"""合成fixture仅检查反馈适配边界，不冒充用户意见或模型语义测试。"""
import json
import struct
import uuid
import numpy as np
import torch

import pytest
from analysis.laila_v2_candidate.feedback_repair import load_feedback, SLOTS, RESEARCH
from analysis.laila_v2_candidate.capture_diagnostics import POSITIVE, NEGATIVE, RIG_PATH, digest, read_json
from exprnet.canonical import load_canonical
from exprnet.rig import load_rig
from analysis.laila_v2_candidate.feedback_repair import strength_rows, STRENGTH_SOURCE, reject_duplicate_additions
from analysis.laila_v2_candidate.retrain_candidate import expanded_model, fit
from exprnet.models import ResMLP


@pytest.fixture
def batch(tmp_path):
    rig=load_rig(RIG_PATH,load_canonical());meta=read_json(RESEARCH.with_suffix('.json'))
    root=tmp_path/'synthetic-feedback-fixture';root.mkdir()
    for slot in range(1,11):
        sample_id=SLOTS.get(slot,uuid.uuid4().hex);folder=root/sample_id;folder.mkdir()
        raw=[0.]*17;raw[0]=slot/100
        weights={key:0. for key in POSITIVE+NEGATIVE};weights[POSITIVE[0]]=slot
        png=folder/'face.png';png.write_bytes(b'\x89PNG\r\n\x1a\n'+b'\x00'*8+struct.pack('>II',1920,1080))
        row={'schema_version':1,'id':sample_id,'attempt_slot':slot,'source':'single-user-development-feedback',
            'purpose':'single-user-playtest-development-feedback-not-golden','data_role':'playtest-feedback','training_eligible':False,
            'collection_mode':'bounded-development-pilot','pilot_limit':10,'capture':'synchronous-click-snapshot-baked-current-face',
            'scene':'Assets/_Project/Scenes/LailaRecognitionPlaytest.unity','note':'','prediction_visible_before_selection':False,
            'prediction_exposed_before_save':False,'model':{'onnx_sha256':meta['onnx']['sha256'],'rig_hash':rig.config_hash(),'research':meta['research']},
            'raw17_keys':[s['key'] for s in meta['sliders']],'raw17':raw,'weights31':weights,
            'image':{'file':'face.png','sha256':digest(png)},'user_label':'angry' if slot in SLOTS else 'happy','target_hint':'neutral'}
        (folder/'record.json').write_text(json.dumps(row),encoding='utf-8')
    return root,rig,meta


def mutate(batch,change):
    root,_,_=batch;path=root/SLOTS[2]/'record.json';row=json.loads(path.read_text(encoding='utf-8'))
    change(row);path.write_text(json.dumps(row),encoding='utf-8')


def test_explicit_label_not_target_and_source_immutable(batch):
    root,rig,meta=batch;before={p:p.read_bytes() for p in root.rglob('*') if p.is_file()}
    rows=load_feedback(root,rig,meta)
    assert rows[1]['user_label']=='angry' and rows[1]['target_hint']=='neutral'
    assert before=={p:p.read_bytes() for p in before}


@pytest.mark.parametrize('field,value',[
    ('training_eligible',True),('prediction_exposed_before_save',True),('prediction_visible_before_selection',True),
    ('user_label','neutral'),('note','[AUTOMATED UI FIXTURE]'),('attempt_slot',1)])
def test_changed_role_label_exposure_or_fixture_rejected(batch,field,value):
    mutate(batch,lambda row:row.update({field:value}))
    with pytest.raises(ValueError):load_feedback(*batch)


def test_late_weight_conflict_and_missing_image_rejected(batch):
    mutate(batch,lambda row:row['weights31'].update({NEGATIVE[-1]:20,POSITIVE[13]:30}))
    with pytest.raises(ValueError):load_feedback(*batch)
    (batch[0]/SLOTS[2]/'face.png').unlink()
    with pytest.raises(ValueError):load_feedback(*batch)


def test_model_keys_nan_hash_and_partial_rejected(batch):
    root,rig,meta=batch;path=root/SLOTS[2]/'record.json';original=path.read_text(encoding='utf-8')
    changes=[lambda r:r['model'].update(onnx_sha256='bad'),lambda r:r['raw17_keys'].reverse(),
             lambda r:r['raw17'].__setitem__(16,float('nan')),lambda r:r['image'].update(sha256='bad')]
    for change in changes:
        path.write_text(original,encoding='utf-8');mutate(batch,change)
        with pytest.raises(ValueError):load_feedback(root,rig,meta)
    path.write_text(original,encoding='utf-8');(root/'record.partial').write_text('partial',encoding='utf-8')
    with pytest.raises(ValueError):load_feedback(root,rig,meta)


def test_strength_fixed_saved_sources_and_prototype_roles():
    rows=strength_rows(read_json(STRENGTH_SOURCE))
    trained=[r for r in rows if r['role']=='training']
    assert len(trained)==3 and all(r['label']=='angry' and not r['human_blind_label'] for r in trained)
    assert len({r['prototype_group'] for r in trained})==1
    assert len([r for r in rows if r['y5']==4 and r['role']!='training'])==6
    assert all(not r['synthetic_neighborhood'] for r in rows)


@pytest.mark.parametrize('change', ['raw','nan','model','id','counter','human'])
def test_changed_strength_evidence_rejected(change):
    proof=read_json(STRENGTH_SOURCE)
    if change=='raw':proof['rows'][16]['raw17'][7]=.2
    elif change=='nan':proof['rows'][16]['raw17'][16]=float('nan')
    elif change=='model':proof['rows'][16]['model']='old51'
    elif change=='id':proof['rows'][16]['name']='17-PreviousAngry-2'
    elif change=='counter':proof['rows'][16]['automatic_inference_count']=17
    else:proof['human_labels']=True
    with pytest.raises(ValueError):strength_rows(proof)


def test_strength_duplicates_and_label_conflicts_not_silently_merged():
    row=[r for r in strength_rows(read_json(STRENGTH_SOURCE)) if r['role']=='training'][0]
    raw=np.asarray([row['raw17']],np.float32)
    for y in (4,0,-1):
        with pytest.raises(ValueError):reject_duplicate_additions(raw,np.array([y]),[row])
    with pytest.raises(ValueError):reject_duplicate_additions(np.zeros((1,17)),np.array([0]),[row,row])


def test_fit_warm59_preserves_input_columns_and_source_model():
    baseline=ResMLP(load_canonical(),5,width=8,hidden=12,blocks=1,dropout=0)
    warm=expanded_model(baseline,59)
    with torch.no_grad():warm.inp.weight[:,51:].fill_(.4)
    before={k:v.clone() for k,v in warm.state_dict().items()}
    x=np.eye(5,59,dtype=np.float32);y=np.arange(5);mask=np.ones(5,bool)
    candidate,_=fit(warm,x,y,mask,epochs=1,logit_adjust_tau=0)
    assert torch.all(candidate.inp.weight[:,51:]>.39)
    assert all(torch.equal(v,before[k]) for k,v in warm.state_dict().items())
