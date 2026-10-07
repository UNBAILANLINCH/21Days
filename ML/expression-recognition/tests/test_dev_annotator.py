import csv
import hashlib
import json
from pathlib import Path

import pytest

from analysis.laila_v2_candidate.annotator import Dataset, InstanceLock, Store, LABELS


@pytest.fixture
def dataset(tmp_path):
    packet = tmp_path / '中文目录' / '盲标包'
    images = packet / 'annotators/images'
    images.mkdir(parents=True)
    entries = []
    for i in range(3):
        sid = f'{i:032x}'
        data = b'png-fixture-' + bytes([i])
        (images / (sid + '.png')).write_bytes(data)
        entries.append({'id': sid, 'group': 'g1', 'split': 'dev', 'image_sha256': hashlib.sha256(data).hexdigest()})
    (packet / 'steward-manifest.json').write_text(json.dumps({'entries': entries}), encoding='utf-8')
    for n in range(1, 4):
        with (packet / f'annotators/annotator_{n}.csv').open('w', encoding='utf-8-sig', newline='') as stream:
            writer = csv.writer(stream)
            writer.writerow(['id', 'label', 'clarity', 'note'])
            writer.writerows([[e['id'], '', '', ''] for e in entries])
    return Dataset(packet)


def request(state, sid, label='', status='unlabeled', note=''):
    return {'annotator': state['annotator'], 'revision': state['revision'], 'cursor': 1,
            'id': sid, 'entry': {'label': label, 'status': status, 'clarity': '', 'note': note}}


def test_save_resume_revision_export_history(dataset, tmp_path):
    store = Store(dataset, tmp_path / '保存中文')
    initial = store.load('首轮用户')
    assert initial['annotations'] == {}
    saved = store.save(request(initial, dataset.ids[0], 'fear', 'labeled', '有些惊讶'))
    assert Store(dataset, store.output).load('首轮用户') == saved
    with pytest.raises(ValueError, match='另一页面'):
        store.save(request(initial, dataset.ids[0], 'happy', 'labeled'))
    corrected = store.save(request(saved, dataset.ids[0], 'surprise', 'labeled'))
    assert len(corrected['history']) == 2
    assert corrected['history'][1]['previous']['label'] == 'fear'
    data, _ = store.export('首轮用户', 'csv')
    rows = list(csv.DictReader(data.decode('utf-8-sig').splitlines()))
    assert rows[0]['label'] == 'surprise' and rows[1]['label'] == '' and rows[2]['status'] == 'unlabeled'
    data, _ = store.export('首轮用户', 'json')
    assert json.loads(data)['purpose'] == 'single-annotator-development-not-golden'


def test_skip_clear_and_all_labels(dataset, tmp_path):
    store = Store(dataset, tmp_path / 'out')
    state = store.load('a')
    for label in LABELS:
        state = store.save(request(state, dataset.ids[0], label, 'labeled'))
    state = store.save(request(state, dataset.ids[0], '', 'skipped'))
    assert state['annotations'][dataset.ids[0]]['label'] == ''
    state = store.save(request(state, dataset.ids[0]))
    assert state['annotations'][dataset.ids[0]]['status'] == 'unlabeled'
    with pytest.raises(ValueError):
        store.save(request(state, dataset.ids[0], 'neutral', 'unlabeled'))


def test_backup_recovery_and_atomic_failure(dataset, tmp_path, monkeypatch):
    store = Store(dataset, tmp_path / 'out')
    first = store.save(request(store.load('a'), dataset.ids[0], 'sad', 'labeled'))
    second = store.save(request(first, dataset.ids[0], 'angry', 'labeled'))
    store.path('a').write_text('broken', encoding='utf-8')
    recovered = store.load('a')
    assert recovered['annotations'][dataset.ids[0]]['label'] == 'sad'
    assert list(store.output.glob('*.corrupt-*'))
    import analysis.laila_v2_candidate.annotator as module
    original = module.os.replace
    def fail(src, dst):
        if Path(dst).suffix == '.json':
            raise OSError('simulated interruption')
        return original(src, dst)
    monkeypatch.setattr(module.os, 'replace', fail)
    with pytest.raises(OSError):
        store.save(request(recovered, dataset.ids[1], 'happy', 'labeled'))
    assert store.load('a')['annotations'][dataset.ids[0]]['label'] == 'sad'


def test_missing_image_test_split_order_and_versions(dataset, tmp_path):
    image = dataset.image(dataset.ids[0])
    image.unlink()
    with pytest.raises(ValueError, match='图片缺失'):
        Dataset(dataset.packet)
    store = Store(dataset, tmp_path / 'out')
    state = store.save(request(store.load('a'), dataset.ids[0]))
    state['dataset_version'] = 'changed'
    store.path('a').write_text(json.dumps(state), encoding='utf-8')
    with pytest.raises(ValueError, match='保存损坏'):
        store.load('a')
    manifest = dataset.packet / 'steward-manifest.json'
    obj = json.loads(manifest.read_text())
    obj['entries'][0]['split'] = 'test'
    manifest.write_text(json.dumps(obj))
    with pytest.raises(ValueError, match='禁止混入test'):
        Dataset(dataset.packet)


def test_duplicate_start_lock(tmp_path):
    first = InstanceLock(tmp_path / '中文锁')
    try:
        with pytest.raises(RuntimeError, match='已经运行'):
            InstanceLock(tmp_path / '中文锁')
    finally:
        first.close()
    InstanceLock(tmp_path / '中文锁').close()
