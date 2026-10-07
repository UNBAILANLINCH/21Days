import json
import os
import subprocess
import threading
import urllib.error
import urllib.request

import numpy as np
import pytest

from analysis.laila_v2_candidate.annotator import Dataset, Store, serve
from analysis.laila_v2_candidate.input_diagnostic import (
    InputDiagnostic, validated, checked_decision, PRESETS, ROOT, RIG_PATH, load_references, REFERENCE_ROOT,
)
from exprnet.canonical import load_canonical
from exprnet.rig import load_rig


@pytest.mark.skipif(os.name != 'nt', reason='Windows双击入口回归')
def test_real_launcher_from_other_working_directory(tmp_path):
    # 使用真实cmd入口；不添加sys.path/PYTHONPATH，也不导入测试server替代启动。
    env = os.environ.copy()
    env.pop('PYTHONPATH', None)
    result = subprocess.run(['cmd.exe', '/d', '/c', str(ROOT / 'launch_annotator.cmd'), '--check-diagnostic'],
                            cwd=tmp_path, env=env, capture_output=True, timeout=30)
    assert result.returncode == 0, result.stderr.decode('utf-8', errors='replace')
    line = next(line for line in result.stdout.decode('utf-8', errors='replace').splitlines() if line.startswith('{'))
    data = json.loads(line)
    assert data['ok'] and data['presets'] == 6 and data['sliders'] == 17
    assert data['captures'] == 139 and data['feature_dims'] == [51, 59]


def test_input_and_decision_boundaries():
    rig = load_rig(RIG_PATH, load_canonical())
    for axes in PRESETS.values():
        s = [0.] * 17
        for i, v in axes.items():
            s[i] = v
        assert validated(s, rig).shape == (17,)
    for s in ([0.] * 16, [True] + [0.] * 16, [float('nan')] + [0.] * 16,
              [1.01] + [0.] * 16, [0.] * 16 + [-.01]):
        with pytest.raises(ValueError):
            validated(s, rig)
    p = [.4, .2, .2, .1, .1]
    assert not checked_decision(p, -1.5, -1.5, .4)['rejected']
    assert checked_decision(p, -1.49, -1.5, .41)['reasons'] == ['energy_above_threshold', 'confidence_below_minimum']
    for p in ([.4] * 5, [-.1, .8, .1, .1, .1], [float('nan')] * 5, [.5, .5]):
        with pytest.raises(ValueError):
            checked_decision(p, -2, -1.5, .4)
    for t, c in ((float('nan'), .4), (-2, 2), (-2, True)):
        with pytest.raises(ValueError):
            checked_decision([.4, .2, .2, .1, .1], -2, t, c)


def test_real_probe_no_mutation_and_lost_directions():
    dataset = Dataset()
    protected = list((ROOT / 'artifacts/laila_v2_candidate/dev-annotations').glob('*.json'))
    before = {p: p.read_bytes() for p in protected}
    probe = InputDiagnostic(dataset)
    catalog = probe.catalog()
    assert len(catalog['presets']) == 6 and not catalog['live_render_available']
    zero = probe.evaluate({'values': [0.] * 17})
    assert len(zero['features51']) == 51 and len(zero['features59']) == 59
    assert zero['features59'][:51] == zero['features51']
    for i in (7, 9, 12, 13):
        s = [0.] * 17
        s[i] = -.7
        result = probe.evaluate({'values': s, 'threshold': 100, 'confidence': 0})
        assert result['features51'] == zero['features51']
        assert result['features59'] != zero['features59']
        assert not result['threshold_persisted'] and not result['features59_model_used']
        assert result['argmax_key'] == zero['argmax_key']
        assert result['trial_final'] == result['argmax_key']
    sid = dataset.ids[0]
    snapshot = probe.sample(sid)
    result = probe.evaluate({'values': snapshot['values']})
    assert sid in result['exact_image_ids']
    assert all(p.read_bytes() == b for p, b in before.items())
    with pytest.raises(ValueError):
        probe.sample('../bad')


def test_http_diagnostic_separate_from_store(tmp_path):
    dataset = Dataset()
    store = Store(dataset, tmp_path / 'annotations')
    server = serve(dataset, store, 'test-token')
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    base = f'http://127.0.0.1:{server.server_port}'
    def get(path):
        with urllib.request.urlopen(base + path) as response:
            return response.read()
    try:
        assert '输入诊断' in get('/diagnostic?token=test-token').decode()
        assert b'features59' not in get('/?token=test-token')
        catalog = json.loads(get('/diagnostic/catalog?token=test-token'))
        request = urllib.request.Request(base + '/diagnostic/evaluate?token=test-token',
                                         json.dumps({'values': catalog['presets'][0]['values']}).encode(),
                                         {'Content-Type': 'application/json'}, method='POST')
        with urllib.request.urlopen(request) as response:
            assert json.load(response)['source'] == 'offline-deployed-onnx-input-probe'
        assert not list(store.output.iterdir())
        with pytest.raises(urllib.error.HTTPError) as error:
            get('/diagnostic/catalog?token=wrong')
        assert error.value.code == 403
    finally:
        server.shutdown()
        server.server_close()
        thread.join(timeout=2)


@pytest.mark.skipif(not (REFERENCE_ROOT / 'verification.json').exists(), reason='本地Unity参考包未生成')
def test_verified_reference_pack_and_image_route():
    probe = InputDiagnostic(Dataset())
    assert len(probe.references) == 37
    for key in PRESETS:
        values = probe.reference(key)['values']
        result = probe.evaluate({'values': values})
        assert key in result['exact_reference_keys']
        assert probe.reference_image(key).startswith(b'\x89PNG')
    with pytest.raises(ValueError):
        probe.reference_image('../neutral')


@pytest.mark.parametrize('fault', ['duplicate', 'weights-nan', 'preset-changed', 'image-changed'])
@pytest.mark.skipif(not (REFERENCE_ROOT / 'verification.json').exists(), reason='本地Unity参考包未生成')
def test_reference_pack_rejects_corruption(tmp_path, fault):
    from analysis.laila_v2_candidate.capture_diagnostics import read_json, digest
    rig = load_rig(RIG_PATH, load_canonical())
    proof = read_json(REFERENCE_ROOT / 'verification.json')
    manifest = read_json(REFERENCE_ROOT / 'unity-manifest.json')
    for row in manifest['entries']:
        image = tmp_path / row['image']
        image.write_bytes(row['key'].encode())
        proof['images'][row['key']] = digest(image)
    if fault == 'duplicate':
        manifest['entries'][1] = manifest['entries'][0].copy()
    elif fault == 'weights-nan':
        manifest['entries'][0]['weights31']['Brow_L_Inner_Up'] = float('nan')
    elif fault == 'preset-changed':
        manifest['entries'][0]['raw17'][0] = .5
        manifest['entries'][0]['weights31']['Brow_L_Inner_Up'] = 50
    else:
        (tmp_path / 'neutral.png').write_bytes(b'changed')
    path = tmp_path / 'unity-manifest.json'
    path.write_text(json.dumps(manifest), encoding='utf-8')
    proof['manifest_sha256'] = digest(path)
    (tmp_path / 'verification.json').write_text(json.dumps(proof), encoding='utf-8')
    with pytest.raises(ValueError):
        load_references(tmp_path, rig, proof['model_sha256'])
