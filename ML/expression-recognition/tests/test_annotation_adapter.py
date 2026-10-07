"""适配fixture为数值/假图片测试，不是人工意见或真实采集证据。"""
import csv
import hashlib
import json
from pathlib import Path

import numpy as np
import pytest

from analysis.laila_v2_candidate.annotation_adapter import (
    CLASS_KEYS, main, policy_view, prepare, write_bundle, review_tags, revision_diff,
)
from analysis.laila_v2_candidate.annotator import Dataset
from analysis.laila_v2_candidate.capture_diagnostics import RIG_PATH, POSITIVE, NEGATIVE
from analysis.laila_v2_candidate.feature_probe import decode, extended_features
from exprnet.canonical import load_canonical
from exprnet.rig import load_rig


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False), encoding='utf-8')


@pytest.fixture
def fixture(tmp_path):
    rig = load_rig(RIG_PATH, load_canonical())
    packet = tmp_path / '中文盲标包'
    captures = tmp_path / '原始采集'
    images = packet / 'annotators/images'
    images.mkdir(parents=True)
    fbx = tmp_path / 'model.fbx'
    fbx.write_bytes(b'fixture-not-real-fbx')
    entries = []
    values = np.random.default_rng(17).uniform(rig.s_min, rig.s_max, size=(6,17))
    for index, row in enumerate(values):
        sid = f'{index:032x}'
        weights = dict.fromkeys(POSITIVE + NEGATIVE, 0.)
        for i, value in enumerate(row):
            weights[POSITIVE[i]] = max(value,0)*100
            if i < 14:
                weights[NEGATIVE[i]] = max(-value,0)*100
        record={'schema_version':1,'status':'unlabeled','source':'unity-editor-play-capture',
                'id':sid,'rig':rig.name,'rig_hash':rig.config_hash(),'fbx_sha256':sha(fbx),
                'split':'dev','group':'g'+str(index%2),'sliders':dict(zip(rig.keys,row.tolist())),'weights':weights}
        capture=captures/'dev'/(sid+'.json')
        write_json(capture,record)
        image=capture.with_suffix('.png')
        image.write_bytes(b'fixture-image-'+bytes([index]))
        (images/image.name).write_bytes(image.read_bytes())
        entries.append({'id':sid,'group':record['group'],'split':'dev','capture_json':capture.name,
                        'capture_sha256':sha(capture),'image_sha256':sha(image),
                        'rig_hash':rig.config_hash(),'fbx_sha256':sha(fbx)})
    write_json(packet/'steward-manifest.json',{'schema_version':1,'entries':entries})
    for n in range(1,4):
        with (packet/f'annotators/annotator_{n}.csv').open('w',encoding='utf-8-sig',newline='') as stream:
            writer=csv.writer(stream);writer.writerow(['id','label','clarity','note'])
            writer.writerows([[e['id'],'','',''] for e in entries])
    ds=Dataset(packet)
    labels=['neutral','happy','surprise','disgust','ambiguous','']
    annotations={e['id']:{'label':label,'status':'labeled' if label else 'unlabeled',
                         'clarity':'uncertain' if label=='ambiguous' else '',
                         'note':'疑惑' if not label else '', 'updated_at':'2026-10-03T00:00:00+00:00'} for e,label in zip(entries,labels)}
    state={'schema_version':1,'purpose':'single-annotator-development-not-golden','split':'dev','annotator':'用户甲',
           'dataset_version':ds.version,'order':ds.ids,'revision':4,'cursor':0,
           'created_at':'2026-10-03T00:00:00+00:00','updated_at':'2026-10-03T00:00:00+00:00',
           'annotations':annotations,'history':[{'id':ds.ids[0],'previous':None,'current':annotations[ds.ids[0]],'at':'2026-10-03T00:00:00+00:00'}]}
    source=tmp_path/'原标注.json';write_json(source,state)
    return {'rig':rig,'packet':packet,'captures':captures,'fbx':fbx,'source':source,'state':state,'entries':entries,'raw17':values}


def prepare_fixture(fixture, **kwargs):
    return prepare(fixture['source'], fixture['packet'], fixture['captures'], fixture['fbx'], **kwargs)


def test_paired_export_preserves_source_and_empty_labels(fixture,tmp_path):
    original=fixture['source'].read_bytes()
    manifest,arrays,source=prepare_fixture(fixture)
    assert manifest['training_ready'] is False and manifest['legacy_exprnet_npz_compatible'] is False
    assert manifest['split']=='dev' and not manifest['new_split_created']
    assert manifest['supervised5_candidate_count']==3
    assert manifest['class_counts5']==dict(neutral=1,happy=1,sad=0,surprise_fear=1,angry=0)
    assert manifest['missing_supervised_classes']==['sad','angry']
    assert arrays['x51'].shape==(6,51) and arrays['x59'].shape==(6,59)
    np.testing.assert_array_equal(arrays['x51'],arrays['x59'][:,:51])
    np.testing.assert_allclose(decode(fixture['rig'],arrays['x59']),fixture['raw17'],atol=1e-6,rtol=0)
    assert arrays['y5'].tolist()==[0,1,3,-1,-1,-1]
    assert arrays['label_original'][-1]=='' and arrays['note'][-1]=='疑惑'
    assert not arrays['uncertainty_reference_mask'].any() and not arrays['disgust_reference_mask'].any()
    assert source==original
    out=tmp_path/'成对特征';saved=write_bundle(out,manifest,arrays,source,fixture['source'])
    assert fixture['source'].read_bytes()==original
    assert (out/'annotation-original.json').read_bytes()==original
    with np.load(out/'features.npz',allow_pickle=False) as z:
        assert 'x' not in z.files and z['label_original'].tolist()==arrays['label_original'].tolist()
    assert saved['files']['features.npz']==sha(out/'features.npz')
    with pytest.raises(ValueError,match='拒绝覆盖'):
        write_bundle(out,manifest,arrays,source,fixture['source'])


def test_explicit_reference_policy_never_supervises_unknown(fixture):
    manifest,arrays,_=prepare_fixture(fixture,disgust_policy='outside-class-reference',ambiguous_policy='uncertainty-reference')
    assert arrays['disgust_reference_mask'].tolist()==[False,False,False,True,False,False]
    assert arrays['uncertainty_reference_mask'].tolist()==[False,False,False,False,True,False]
    assert arrays['supervised5_mask'].sum()==3
    assert manifest['policy']['unknown_is_supervised_class'] is False
    assert policy_view('fear','labeled')['label5']=='surprise_fear'
    with pytest.raises(ValueError):
        policy_view('ambiguous','labeled',ambiguous_policy='automatic-truth')


def test_missing_class_is_reported_and_optional_hard_guard(fixture):
    with pytest.raises(ValueError,match='缺类'):
        prepare_fixture(fixture,require_all_classes=True)
    state=fixture['state']
    for entry in state['annotations'].values():
        entry['label']='';entry['status']='unlabeled'
    state['history']=[]  # 构造新空表，而非伪造不匹配的重标历史。
    write_json(fixture['source'],state)
    manifest,arrays,_=prepare_fixture(fixture)
    assert manifest['supervised5_candidate_count']==0 and manifest['missing_supervised_classes']==CLASS_KEYS
    assert (arrays['y5']==-1).all()


@pytest.mark.parametrize('fault',['label','status','version','order','unknown-id','duplicate-json','bad-history','history-mismatch','history-time','purpose'])
def test_annotation_faults_rejected(fixture,fault):
    state=fixture['state'];first=state['order'][0]
    if fault=='label':state['annotations'][first]['label']='puzzled'
    if fault=='status':state['annotations'][first]['status']='skipped'
    if fault=='version':state['dataset_version']='0'*64
    if fault=='order':state['order']=state['order'][::-1]
    if fault=='unknown-id':state['annotations']['f'*32]=state['annotations'][first].copy()
    if fault=='bad-history':state['history'][0]['id']='f'*32
    if fault=='history-mismatch':state['history'][0]['current']=dict(state['history'][0]['current'],label='sad')
    if fault=='history-time':state['history'][0]['at']='not-a-time'
    if fault=='purpose':state['purpose']='human-labeled'
    write_json(fixture['source'],state)
    if fault=='duplicate-json':
        text=fixture['source'].read_text(encoding='utf-8')
        fixture['source'].write_text('{"revision":999,'+text[1:],encoding='utf-8')
    with pytest.raises(ValueError):prepare_fixture(fixture)


@pytest.mark.parametrize('fault',['missing-png','changed-png','changed-json','axis-mismatch','duplicate-id','group-leak','locked-test'])
def test_capture_and_leakage_faults_rejected(fixture,fault):
    sid=fixture['entries'][0]['id'];path=fixture['captures']/'dev'/(sid+'.json')
    record=json.loads(path.read_text(encoding='utf-8'))
    if fault=='missing-png':path.with_suffix('.png').unlink()
    if fault=='changed-png':path.with_suffix('.png').write_bytes(b'changed')
    if fault=='changed-json':record['extra']='mutated';write_json(path,record)
    if fault=='axis-mismatch':record['sliders'][fixture['rig'].keys[0]]=0;write_json(path,record)
    if fault in ('duplicate-id','group-leak'):
        if fault=='group-leak':record['id']='f'*32
        record['split']='test'
        target=fixture['captures']/'test'/(record['id']+'.json')
        write_json(target,record);target.with_suffix('.png').write_bytes(b'fixture')
    if fault=='locked-test':
        manifest=json.loads((fixture['packet']/'steward-manifest.json').read_text(encoding='utf-8'))
        manifest['entries'][0]['split']='test';write_json(fixture['packet']/'steward-manifest.json',manifest)
    with pytest.raises(ValueError):prepare_fixture(fixture)


def test_fifty_nine_preserves_actual_erased_and_shared_axes():
    rig=load_rig(RIG_PATH,load_canonical())
    raw=np.zeros((4,17),dtype=np.float32)
    raw[0,0]=.5;raw[1,3]=.5;raw[2,7]=-.75;raw[3,12]=-.9
    features=extended_features(rig,raw)
    np.testing.assert_array_equal(features[0,:51],features[1,:51])
    np.testing.assert_array_equal(features[2,:51],features[3,:51])
    assert not np.array_equal(features[0,51:],features[1,51:])
    np.testing.assert_allclose(decode(rig,features),raw,atol=1e-6,rtol=0)


def test_cli_rejects_output_outside_artifact_boundary(tmp_path):
    out=tmp_path/'source-overwrite'
    assert main(['--annotations',str(tmp_path/'a.json'),'--out',str(out)])==1
    assert not out.exists()


def test_configuration_snapshot_mismatch_does_not_publish(fixture,tmp_path):
    manifest,arrays,source=prepare_fixture(fixture)
    manifest['config_sha256']['rig-snapshot.yaml']='0'*64
    out=tmp_path/'unpublished'
    with pytest.raises(ValueError,match='配置变化'):
        write_bundle(out,manifest,arrays,source,fixture['source'])
    assert not out.exists()
    assert list(tmp_path.glob('unpublished.partial-*'))


def test_confused_review_preserves_literal_evidence(fixture):
    assert review_tags('无辜/茫然') == []
    assert review_tags('疑惑/无辜') == ['confused_candidate']
    assert review_tags('或是疑惑？') == ['confused_candidate']
    manifest, arrays, source = prepare_fixture(fixture)
    assert manifest['rows'][-1]['review_tag_evidence'] == '疑惑'
    assert arrays['confused_candidate_mask'].tolist() == [False]*5 + [True]
    assert not arrays['supervised5_mask'][-1]
    assert manifest['rows'][0]['history_indices'] == [0]
    assert json.loads(source)['history'] == fixture['state']['history']


def test_history_chain_break_rejected(fixture):
    state = fixture['state']
    state['history'][0]['previous'] = state['history'][0]['current'].copy()
    write_json(fixture['source'], state)
    with pytest.raises(ValueError, match='链断裂'):
        prepare_fixture(fixture)


def test_revision_comparison_is_stable_and_rejects_other_annotator(fixture, tmp_path):
    original = fixture['source'].read_bytes()
    previous = tmp_path/'previous.json'
    previous.write_bytes(original)
    state = fixture['state']
    sid = state['order'][-1]
    state['annotations'][sid]['note'] = '无辜/茫然'
    state['revision'] += 1
    write_json(fixture['source'], state)
    result = revision_diff(fixture['source'].read_bytes(), previous)
    assert not result['stable_ids_changed']
    assert [r['id'] for r in result['changes']] == [sid]
    assert previous.read_bytes() == original
    state['annotator'] = '用户乙'
    with pytest.raises(ValueError, match='不自动合并'):
        revision_diff(json.dumps(state).encode(), previous)


def test_full_information_random_and_bounds():
    rig = load_rig(RIG_PATH, load_canonical())
    random = np.random.default_rng(71).uniform(rig.s_min, rig.s_max, (1000,17))
    raw = np.concatenate([random, rig.s_min[None], rig.s_max[None]])
    encoded = extended_features(rig, raw)
    encoded[:,:51] *= rig.canonical.mask_vector
    assert encoded.shape == (1002,59)
    np.testing.assert_allclose(decode(rig, encoded), raw, atol=1e-6, rtol=0)


def test_missing_original_snapshot_rejected(fixture):
    path = fixture['captures']/'dev'/(fixture['entries'][0]['id']+'.json')
    path.unlink()
    with pytest.raises(ValueError, match='缺少完整原始快照'):
        prepare_fixture(fixture)


def test_windows_directory_rename_denied_uses_verified_copy(fixture, tmp_path, monkeypatch):
    from analysis.laila_v2_candidate import annotation_adapter
    def denied(*args):
        raise PermissionError('fixture')
    monkeypatch.setattr(annotation_adapter.os, 'rename', denied)
    manifest, arrays, source = prepare_fixture(fixture)
    out = tmp_path/'copy-published'
    saved = write_bundle(out, manifest, arrays, source, fixture['source'])
    assert saved['publication'].startswith('verified-file-copy')
    stored = json.loads((out/'manifest.json').read_text(encoding='utf-8'))
    for name, expected in stored['files'].items():
        assert sha(out/name) == expected
