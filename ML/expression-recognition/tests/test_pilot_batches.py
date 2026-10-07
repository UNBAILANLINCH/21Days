import csv
import io
import json
import pytest
import threading
import urllib.request
import urllib.error

from analysis.laila_v2_candidate.annotator import Dataset, Store, ROOT, serve
from analysis.laila_v2_candidate.pilot_batches import build_batch
from analysis.laila_v2_candidate.annotation_adapter import prepare
from analysis.laila_v2_candidate.input_diagnostic import InputDiagnostic


@pytest.fixture
def captures(tmp_path):
    original = Dataset()
    root = tmp_path / 'captures'
    for i, split in enumerate(('dev', 'test')):
        entry = original.entries[i]
        raw = json.loads(original.capture_path(entry).read_text(encoding='utf-8'))
        raw.update(id=f'{i + 1:032x}', split=split, group='pilot-' + split)
        folder = root / split
        folder.mkdir(parents=True)
        (folder / (raw['id'] + '.json')).write_text(json.dumps(raw), encoding='utf-8')
        (folder / (raw['id'] + '.png')).write_bytes(original.image(entry['id']).read_bytes())
    return root


def test_pilot_save_reopen_export_and_training_refusal(captures, tmp_path):
    packet = build_batch(captures, tmp_path / 'packets')
    dataset = Dataset(packet)
    assert build_batch(captures, tmp_path / 'packets') == packet
    store = Store(dataset, tmp_path / 'fixture-records')
    state = store.load('fixture-only')
    for sid in dataset.ids:
        state = store.save({'annotator': state['annotator'], 'revision': state['revision'],
                            'cursor': 0, 'id': sid, 'entry': {'label': 'ambiguous',
                            'status': 'labeled', 'clarity': 'uncertain', 'note': 'fixture-only'}})
    assert Store(Dataset(packet), store.output).load('fixture-only') == state
    rows = list(csv.DictReader(io.StringIO(store.export('fixture-only', 'csv')[0].decode('utf-8-sig'))))
    assert {r['data_role'] for r in rows} == {'calibration', 'locked-test'}
    assert {r['split'] for r in rows} == {'dev', 'test'}
    with pytest.raises(ValueError):
        prepare(store.path('fixture-only'))
    assert len(Dataset().ids) == 139


@pytest.mark.parametrize('fault', ['group', 'pose', 'missing-image', 'duplicate'])
def test_builder_rejects_leakage_and_incomplete_capture(captures, tmp_path, fault):
    dev = next((captures / 'dev').glob('*.json'))
    test = next((captures / 'test').glob('*.json'))
    raw, other = json.loads(dev.read_text()), json.loads(test.read_text())
    if fault == 'group':
        other['group'] = raw['group']
    elif fault == 'pose':
        other = {**raw, 'id': other['id'], 'split': 'test', 'group': other['group']}
    elif fault == 'missing-image':
        test.with_suffix('.png').unlink()
    else:
        other['id'] = raw['id']
    test.write_text(json.dumps(other), encoding='utf-8')
    with pytest.raises(ValueError):
        build_batch(captures, tmp_path / 'packets')


def test_candidate_inference_and_test_catalog_isolation(captures, tmp_path):
    dataset = Dataset(build_batch(captures, tmp_path / 'packets'))
    probe = InputDiagnostic(dataset)
    dev = next(sid for sid, role in dataset.roles.items() if role['split'] == 'dev')
    test = next(sid for sid, role in dataset.roles.items() if role['split'] == 'test')
    assert probe.catalog()['ids'] == [dev]
    with pytest.raises(ValueError):
        probe.sample(test)
    values = probe.sample(dev)['values']
    original = probe.evaluate({'values': values})
    candidate = probe.evaluate({'values': values, 'model': 'research59'})
    assert not original['features59_model_used']
    assert candidate['features59_model_used'] and candidate['research_rejection_uncalibrated']
    assert candidate['model_sha256'] != original['model_sha256']
    assert len(candidate['probabilities']) == 5
    assert probe.evaluate({'values': values}) == original
    with pytest.raises(ValueError):
        probe.evaluate({'values': values, 'model': 'bad'})


def test_http_packet_label_reopen_and_candidate(captures, tmp_path):
    dataset = Dataset(build_batch(captures, tmp_path / 'packets'))
    store = Store(dataset, tmp_path / 'fixture-records')
    server = serve(dataset, store, 'fixture-token')
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    base = f'http://127.0.0.1:{server.server_port}'
    def call(path, body=None):
        request = urllib.request.Request(base + path + ('&' if '?' in path else '?') + 'token=fixture-token',
                    data=None if body is None else json.dumps(body).encode(),
                    headers={'Content-Type': 'application/json'})
        with urllib.request.urlopen(request, timeout=30) as response:
            return json.load(response)
    try:
        assert call('/context')['pilot']
        state = call('/state?annotator=fixture-only')['state']
        sid = next(s for s,r in dataset.roles.items() if r['split']=='test')
        body = {'annotator':'fixture-only','revision':state['revision'],'cursor':0,'id':sid,
                'entry':{'label':'ambiguous','status':'labeled','clarity':'','note':'fixture-only'}}
        saved = call('/save', body)['state']
        assert call('/state?annotator=fixture-only')['state']==saved
        assert Store(Dataset(dataset.packet), store.output).load('fixture-only')==saved
        catalog = call('/diagnostic/catalog')
        assert sid not in catalog['ids']
        with pytest.raises(urllib.error.HTTPError):
            call('/diagnostic/sample?id='+sid)
        values = call('/diagnostic/sample?id='+catalog['ids'][0])['values']
        assert call('/diagnostic/evaluate', {'values':values,'model':'research59'})['features59_model_used']
    finally:
        server.shutdown()
        server.server_close()
        thread.join(timeout=2)


def test_packet_role_and_hash_tampering(captures, tmp_path):
    packet = build_batch(captures, tmp_path / 'packets')
    manifest = packet/'steward-manifest.json'
    original = manifest.read_bytes()
    value = json.loads(original)
    value['entries'][0]['data_role']='training'
    manifest.write_text(json.dumps(value),encoding='utf-8')
    with pytest.raises(ValueError):
        Dataset(packet)
    manifest.write_bytes(original)
    next((packet/'captures').glob('*.json')).write_text('{}')
    with pytest.raises(ValueError):
        Dataset(packet)
