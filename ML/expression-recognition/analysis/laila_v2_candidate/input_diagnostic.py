"""标注器的只读输入探针；不写标签、训练数据、模型或阈值。"""
import numpy as np
import re

from analysis.laila_v2_candidate.capture_diagnostics import (
    ROOT, MODEL_PATH, RIG_PATH, FBX_PATH, POSITIVE, NEGATIVE,
    digest, read_json, validate_capture, audit, decision, number,
)
from analysis.laila_v2_candidate.feature_probe import extended_features
from exprnet.canonical import load_canonical
from exprnet.rig import load_rig


# 设计参考，不是FACS金标；用户须在实际脸型上确认方向与形态。
PRESETS = {
    'neutral': {},
    'happy': {7: .35, 9: .35, 10: .7, 11: .7, 12: .3, 13: .3},
    'sad': {0: .6, 3: .6, 2: -.3, 5: -.3, 10: -.6, 11: -.6},
    'angry': {0: -.65, 1: -.5, 3: -.65, 4: -.5, 6: -.25, 8: -.25, 7: .4, 9: .4},
    'surprise': {0: .6, 2: .7, 3: .6, 5: .7, 6: .65, 8: .65, 16: .65},
    'fear': {0: .6, 1: -.3, 3: .6, 4: -.3, 6: .6, 8: .6, 12: .65, 13: .65, 16: .3},
}
EXTRA_NAMES = ['raw_brow_L_inner_y', 'raw_brow_L_mid_y', 'raw_brow_R_inner_y',
               'raw_brow_R_mid_y', 'lowerL_down', 'lowerR_down', 'cornerL_in', 'cornerR_in']
REFERENCE_ROOT = ROOT / 'artifacts/laila_v2_candidate/reference-inputs-20261004-v2'


def load_references(path, rig, model_hash):
    """只接受已完成核验的独立诊断包，绝不并入139图或监督标签。"""
    proof_path = path / 'verification.json'
    if not proof_path.exists():
        return {}
    proof = read_json(proof_path)
    manifest_path = path / 'unity-manifest.json'
    if proof.get('verified') is not True or proof.get('model_sha256') != model_hash or proof.get('rig_hash') != rig.config_hash() or proof.get('manifest_sha256') != digest(manifest_path):
        raise ValueError('参考截图包版本或核验hash不一致')
    manifest = read_json(manifest_path)
    if not all(manifest.get(k) is True for k in ('completed', 'weights_restored', 'temporary_scene_closed', 'original_scene_restored')) or manifest.get('error') or manifest.get('capture_method') != 'Unity-BakeMesh-current-controller-weights-camera-render-not-live-preview':
        raise ValueError('参考截图采集未完成或未恢复场景')
    result = {}
    for row in manifest['entries']:
        key = row['key']
        if not isinstance(key, str) or not re.fullmatch('[a-z0-9-]{1,50}', key) or key in result or row['image'] != key + '.png' or row.get('human_label') is not None:
            raise ValueError('参考截图键重复/非法，或误带训练标签')
        s = validated(row['raw17'], rig)
        weights = row['weights31']
        if set(weights) != set(POSITIVE + NEGATIVE):
            raise ValueError('参考截图31形态不完整')
        for i, name in enumerate(POSITIVE):
            number(weights[name], name)
            if i < 14:
                number(weights[NEGATIVE[i]], NEGATIVE[i])
            if abs(weights[name] - max(float(s[i]), 0) * 100) > 1e-4 or (i < 14 and abs(weights[NEGATIVE[i]] - max(-float(s[i]), 0) * 100) > 1e-4):
                raise ValueError('参考截图17轴与31权重不一致')
        image = path / row['image']
        expected = proof['images'].get(key)
        if not expected or digest(image) != expected:
            raise ValueError('参考截图hash不一致：' + key)
        result[key] = {'values': s, 'image': image, 'image_sha256': expected, 'kind': row['kind']}
    if len(result) != 37 or not set(PRESETS).issubset(result):
        raise ValueError('参考截图数量/六参考不完整')
    for key, axes in PRESETS.items():
        s = [0.] * 17
        for i, value in axes.items():
            s[i] = value
        if not np.array_equal(result[key]['values'], validated(s, rig)):
            raise ValueError('经典参考配方变化，须重新生成截图')
    for i in range(17):
        for direction in ([1, -1] if i < 14 else [1]):
            key = f'axis-{i:02d}-' + ('positive' if direction > 0 else 'negative')
            s = [0.] * 17
            s[i] = direction * .7
            if key not in result or not np.array_equal(result[key]['values'], validated(s, rig)):
                raise ValueError('单轴参考输入不完整或发生变化')
    return result


def validated(values, rig):
    if not isinstance(values, list) or len(values) != 17:
        raise ValueError('必须提供按模型顺序排列的17轴')
    if any(type(v) not in (int, float) for v in values):
        raise ValueError('轴必须为数值，不能是布尔值或字符串')
    s = np.asarray(values, dtype=np.float32)
    if not np.isfinite(s).all() or np.any(s < rig.s_min) or np.any(s > rig.s_max):
        raise ValueError('输入非有限或越界；不静默裁剪')
    return s


def checked_decision(p, energy, threshold, confidence):
    p = np.asarray(p)
    if p.shape != (5,) or not np.isfinite(p).all() or np.any(p < 0) or np.any(p > 1) or abs(float(p.sum()) - 1) > .001:
        raise ValueError('模型概率无效，不记作拒识')
    if type(threshold) not in (int, float) or not np.isfinite(threshold):
        raise ValueError('energy试验阈值无效')
    if type(confidence) not in (int, float) or not np.isfinite(confidence) or not 0 <= confidence <= 1:
        raise ValueError('置信度试验阈值必须在0–1之间')
    return decision(p, energy, threshold, confidence)


class InputDiagnostic:
    def __init__(self, dataset):
        import onnxruntime as ort
        from exprnet.export import verify_pair
        self.dataset = dataset
        self.rig = load_rig(RIG_PATH, load_canonical())
        self.meta = read_json(MODEL_PATH.with_suffix('.json'))
        ok, message = verify_pair(MODEL_PATH, MODEL_PATH.with_suffix('.json'))
        if not ok:
            raise ValueError(message)
        if self.rig.config_hash() != self.meta['rig']['config_hash'] or self.rig.keys != [r['key'] for r in self.meta['sliders']]:
            raise ValueError('诊断rig与部署输入顺序/hash不一致')
        if self.meta['canonical_dims'] != self.rig.canonical.names or self.meta['masked_dims'] != self.rig.canonical.masked_names:
            raise ValueError('诊断规范空间与部署不一致')
        options = ort.SessionOptions()
        options.intra_op_num_threads = options.inter_op_num_threads = 1
        self.session = ort.InferenceSession(str(MODEL_PATH), sess_options=options, providers=['CPUExecutionProvider'])
        self.hashes = {str(p): digest(p) for p in (MODEL_PATH, MODEL_PATH.with_suffix('.json'), RIG_PATH)}
        self.captures = {}
        fbx_hash = digest(FBX_PATH)
        for entry in dataset.entries:
            path = ROOT / 'data/laila-captures-v2/dev' / (entry['id'] + '.json')
            if digest(path) != entry['capture_sha256']:
                raise ValueError('原始快照hash变化：' + entry['id'])
            record = read_json(path)
            s = validate_capture(record, self.rig, fbx_hash)
            if record['id'] != entry['id'] or record['split'] != 'dev' or record['group'] != entry['group']:
                raise ValueError('快照ID或分组不一致')
            self.captures[entry['id']] = s.astype(np.float32)
        self.references = load_references(REFERENCE_ROOT, self.rig, self.meta['onnx']['sha256'])

    def catalog(self):
        refs = []
        for key, axes in PRESETS.items():
            values = [0.] * 17
            for index, value in axes.items():
                values[index] = value
            refs.append({'key': key, 'values': values, 'source': 'design-reference-awaiting-user-confirmation'})
        return {'sliders': self.meta['sliders'], 'presets': refs, 'ids': self.dataset.ids,
                'mapping': audit(self.rig)['axes'], 'extra_names': EXTRA_NAMES,
                'canonical_names': self.rig.canonical.names, 'labels': self.meta['labels'],
                'threshold': self.meta['energy_threshold'], 'confidence': self.meta['min_confidence'],
                'model_sha256': self.meta['onnx']['sha256'], 'live_render_available': False,
                'reference_keys': list(self.references)}

    def reference(self, key):
        if key not in self.references:
            raise ValueError('未知诊断参考')
        row = self.references[key]
        return {'key': key, 'values': row['values'].tolist(), 'source': 'offline-unity-baked-reference'}

    def reference_image(self, key):
        if key not in self.references:
            raise ValueError('未知诊断参考图')
        row = self.references[key]
        if digest(row['image']) != row['image_sha256']:
            raise ValueError('诊断参考图已变化')
        return row['image'].read_bytes()

    def sample(self, sid):
        if sid not in self.captures:
            raise ValueError('未知dev快照ID')
        return {'id': sid, 'values': self.captures[sid].tolist(), 'source': 'archived-unity-capture'}

    def evaluate(self, request):
        if any(digest(path) != expected for path, expected in self.hashes.items()):
            raise ValueError('诊断期间模型或rig变化，请重启标注器')
        s = validated(request['values'], self.rig)
        x51 = self.rig.forward(s[None]) * self.rig.canonical.mask_vector
        x59 = extended_features(self.rig, s[None])
        x59[:, :51] = x51
        p, e = self.session.run(['probs', 'energy'], {'sliders': s[None]})
        threshold = request.get('threshold', self.meta['energy_threshold'])
        confidence = request.get('confidence', self.meta['min_confidence'])
        formal = checked_decision(p[0], float(e[0]), self.meta['energy_threshold'], self.meta['min_confidence'])
        trial = checked_decision(p[0], float(e[0]), threshold, confidence)
        keys = [row['key'] for row in self.meta['labels']]
        matches = [sid for sid, raw in self.captures.items() if np.array_equal(raw, s)]
        weights = {}
        for i, name in enumerate(POSITIVE):
            weights[name] = float(max(s[i], 0) * 100)
            if i < len(NEGATIVE):
                weights[NEGATIVE[i]] = float(max(-s[i], 0) * 100)
        return {'raw17': s.tolist(), 'weights31': weights, 'features51': x51[0].tolist(),
                'features59': x59[0].tolist(), 'probabilities': p[0].tolist(), 'energy': float(e[0]),
                'argmax_key': keys[formal['argmax_index']],
                'formal_final': 'unknown' if formal['rejected'] else keys[formal['argmax_index']],
                'trial_final': 'unknown' if trial['rejected'] else keys[trial['argmax_index']],
                'formal': formal, 'trial': trial, 'exact_image_ids': matches,
                'exact_reference_keys': [key for key, row in self.references.items() if np.array_equal(row['values'], s)],
                'source': 'offline-deployed-onnx-input-probe', 'features59_model_used': False,
                'threshold_persisted': False, 'live_render_verified': False}
