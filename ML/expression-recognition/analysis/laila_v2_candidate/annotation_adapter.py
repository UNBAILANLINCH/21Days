"""一次性本地dev数据适配；复用盲标包/采集护栏及59D探针，不训练或部署。
现有CLI只能诊断图片或读取51D训练NPZ，不能保留raw17和标注历史，故新增成对适配。
载体：显式CLI；5秒状态锚点为终端success/输出manifest；完成或异常即退出，无后台任务。
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import tempfile
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path

import numpy as np

from analysis.laila_v2_candidate.annotator import Dataset, Store, PACKET
from analysis.laila_v2_candidate.capture_diagnostics import (
    ROOT, RIG_PATH, FBX_PATH, POSITIVE, NEGATIVE, digest, read_json, validate_capture, decision,
)
from analysis.laila_v2_candidate.feature_probe import BROW, ERASED, extended_features, decode
from exprnet.canonical import load_canonical
from exprnet.rig import load_rig

CLASS_KEYS = ['neutral', 'happy', 'sad', 'surprise_fear', 'angry']
MAP5 = {k: k for k in ('neutral', 'happy', 'sad', 'angry')}
MAP5.update(surprise='surprise_fear', fear='surprise_fear')
PROBE_RIG_HASH = '56daba66fc94a2101f8501201e4d43b91ff644f71f8179d08abd2fd7c82e5b9c'
FEATURE_SCHEMA = 'laila-paired-dev-features-v1'


def review_tags(note):
    # 仅按原文出现“疑惑”建立复核候选；无辜／茫然不自动等同疑惑。
    return ['confused_candidate'] if '疑惑' in note else []


def reference(path):
    try:
        return Path(path).resolve().relative_to(ROOT.resolve()).as_posix()
    except ValueError:
        return 'external-fixture/' + Path(path).name


def policy_view(label, status, disgust_policy='exclude', ambiguous_policy='exclude'):
    if disgust_policy not in ('exclude', 'outside-class-reference') or ambiguous_policy not in ('exclude', 'uncertainty-reference'):
        raise ValueError('未知标签policy')
    if status != 'labeled' or not label:
        return {'label5': '', 'y5': -1, 'supervised5': False,
                'disgust_reference': False, 'uncertainty_reference': False, 'reason': 'unlabeled-or-skipped'}
    if label in MAP5:
        mapped = MAP5[label]
        return {'label5': mapped, 'y5': CLASS_KEYS.index(mapped), 'supervised5': True,
                'disgust_reference': False, 'uncertainty_reference': False, 'reason': 'single-user-dev-known-candidate'}
    if label not in ('disgust', 'ambiguous'):
        raise ValueError('非法原始标签')
    return {'label5': '', 'y5': -1, 'supervised5': False,
            'disgust_reference': label == 'disgust' and disgust_policy == 'outside-class-reference',
            'uncertainty_reference': label == 'ambiguous' and ambiguous_policy == 'uncertainty-reference',
            'reason': label + '-excluded-from-five-class-supervision'}


def prepare(annotation_path, packet=PACKET, captures=None, fbx_path=FBX_PATH,
            disgust_policy='exclude', ambiguous_policy='exclude', require_all_classes=False):
    """只读构造及验证完整包；不写原标签、图、快照，不建立任何新划分。"""
    annotation_path = Path(annotation_path)
    capture_root = Path(captures) if captures is not None else ROOT / 'data/laila-captures-v2'
    dataset = Dataset(packet)
    rig = load_rig(RIG_PATH, load_canonical())
    if rig.config_hash() != PROBE_RIG_HASH or rig.num_sliders != 17 or rig.canonical.dim != 51:
        raise ValueError('59D解码只适用于当前固定17轴候选，绑定变化需重审')
    source_bytes = annotation_path.read_bytes()
    state = read_json(annotation_path)
    if state.get('purpose') == 'single-annotator-pilot-role-preserved-not-training' or state.get('split') == 'pilot-dev-test-isolated' or 'sample_roles' in state:
        raise ValueError('独立试点含校准／锁定测试角色，禁止作为训练标注输入')
    if state.get('schema_version') != 1 or state.get('purpose') != 'single-annotator-development-not-golden':
        raise ValueError('只接受本工具单人dev意见，不冒用golden')
    if not isinstance(state.get('annotator'), str) or not state['annotator'].strip() or len(state['annotator']) > 80:
        raise ValueError('缺标注者代号')
    # 复用Store验证但不构造写目录或触发load恢复；此调用只访问dataset。
    validator = Store.__new__(Store)
    validator.dataset = dataset
    try:
        validator.validate(state, state['annotator'])
    except (KeyError, TypeError, AttributeError) as error:
        raise ValueError('标注字段／状态结构损坏') from error
    if type(state['revision']) is not int or state['revision'] < 0:
        raise ValueError('非法修订号')
    # 原始JSON保留所有历史；另外检查历史ID和每份非空前后标签，拒绝藏入未知样本。
    latest_history, history_indices = {}, {}
    for history_index, item in enumerate(state['history']):
        if not isinstance(item, dict) or item.get('id') not in dataset.ids:
            raise ValueError('历史含非法ID')
        for side in ('previous', 'current'):
            if item.get(side) is not None:
                try:
                    Store.fields(item[side])
                except (KeyError, TypeError, AttributeError) as error:
                    raise ValueError('历史标签字段损坏') from error
        if item.get('current') is None or not isinstance(item.get('at'), str):
            raise ValueError('历史缺当前意见／时间戳')
        try:
            if datetime.fromisoformat(item['at']).tzinfo is None:
                raise ValueError('历史时间戳缺时区')
        except ValueError as error:
            raise ValueError('历史时间戳无效') from error
        if item.get('previous') != latest_history.get(item['id']):
            raise ValueError('历史前后意见链断裂')
        latest_history[item['id']] = item['current']
        history_indices.setdefault(item['id'], []).append(history_index)
    for sid, last in latest_history.items():
        if state['annotations'].get(sid) != last:
            raise ValueError('历史末次意见与当前annotation不符')
    fbx_hash = digest(Path(fbx_path))
    inventory, groups, fingerprints = {}, {}, {str(annotation_path): hashlib.sha256(source_bytes).hexdigest()}
    for path in sorted(capture_root.rglob('*.json')):
        record = read_json(path)
        values = validate_capture(record, rig, fbx_hash)
        sid = record['id']
        if sid in inventory or path.stem != sid or not path.with_suffix('.png').is_file():
            raise ValueError('重复采集ID或PNG/JSON不配对：' + sid)
        group = record['group']
        if group in groups and groups[group] != record['split']:
            raise ValueError('近邻组跨划分泄漏：' + group)
        groups[group] = record['split']
        inventory[sid] = (path, record, values)
        fingerprints[str(path)] = digest(path)
    if not inventory:
        raise ValueError('没有原始采集')
    rows, raw17, raw31 = [], [], []
    for index, entry in enumerate(dataset.entries):
        sid = entry['id']
        if sid not in inventory:
            raise ValueError('缺少完整原始快照：' + sid)
        path, record, values = inventory[sid]
        image = path.with_suffix('.png')
        if record['split'] != 'dev' or any(record[k] != entry[k] for k in ('group', 'rig_hash', 'fbx_sha256')):
            raise ValueError('包与原始dev分组／版本不符：' + sid)
        if digest(path) != entry['capture_sha256'] or digest(image) != entry['image_sha256']:
            raise ValueError('原始快照／图片哈希不符：' + sid)
        fingerprints[str(image)] = digest(image)
        annotation = state['annotations'].get(sid, {'label': '', 'clarity': '', 'note': '', 'status': 'unlabeled'})
        view = policy_view(annotation['label'], annotation['status'], disgust_policy, ambiguous_policy)
        rows.append({'id': sid, 'order_index': index, 'group': record['group'], 'split': 'dev',
                     'annotator': state['annotator'], 'label_source': 'single-user-development-opinion',
                     'label_original': annotation['label'], 'annotation_status': annotation['status'],
                     'clarity': annotation['clarity'], 'note': annotation['note'],
                     'review_tags': review_tags(annotation['note']),
                     'review_tag_evidence': annotation['note'] if review_tags(annotation['note']) else '',
                     'history_indices': history_indices.get(sid, []),
                     'annotation_updated_at': annotation.get('updated_at'), 'policy_view': view,
                     'capture_json': reference(path), 'image': reference(image),
                     'capture_sha256': entry['capture_sha256'], 'image_sha256': entry['image_sha256']})
        raw17.append(values)
        raw31.append([record['weights'][key] for key in POSITIVE + NEGATIVE])
    raw17 = np.asarray(raw17, dtype=np.float32)
    x51 = (rig.forward(raw17) * rig.canonical.mask_vector).astype(np.float32)
    x59 = extended_features(rig, raw17).astype(np.float32)
    x59[:, :51] *= rig.canonical.mask_vector
    reconstruction_error = float(np.max(np.abs(raw17 - decode(rig, x59))))
    if reconstruction_error > 1e-6 or not np.isfinite(x51).all() or not np.isfinite(x59).all():
        raise ValueError('59D未保留17轴或特征非有限')
    np.testing.assert_array_equal(x51, x59[:, :51])
    names59 = rig.canonical.names + ['raw:' + rig.keys[i] for i in BROW] + ['negative:' + rig.keys[i] for i in ERASED]
    arrays = {'raw17': raw17, 'weights31': np.asarray(raw31, dtype=np.float32), 'x51': x51, 'x59': x59,
              'sliders17_names': np.asarray(rig.keys, dtype=str), 'weights31_names': np.asarray(POSITIVE + NEGATIVE, dtype=str),
              'feature_names51': np.asarray(rig.canonical.names, dtype=str), 'feature_names59': np.asarray(names59, dtype=str),
              'class_keys5': np.asarray(CLASS_KEYS, dtype=str)}
    for name in ('id', 'group', 'split', 'annotator', 'label_source', 'label_original', 'annotation_status', 'clarity', 'note'):
        arrays[name] = np.asarray([row[name] for row in rows], dtype=str)
    arrays['label5'] = np.asarray([row['policy_view']['label5'] for row in rows], dtype=str)
    arrays['confused_candidate_mask'] = np.asarray([bool(row['review_tags']) for row in rows], dtype=bool)
    arrays['y5'] = np.asarray([row['policy_view']['y5'] for row in rows], dtype=np.int32)
    for name in ('supervised5', 'disgust_reference', 'uncertainty_reference'):
        arrays[name + '_mask'] = np.asarray([row['policy_view'][name] for row in rows], dtype=bool)
    counts = Counter(arrays['label5'][arrays['supervised5_mask']].tolist())
    class_counts = {key: counts[key] for key in CLASS_KEYS}
    missing = [key for key in CLASS_KEYS if not counts[key]]
    if require_all_classes and missing:
        raise ValueError('五类监督候选缺类：' + ','.join(missing))
    used_groups = {row['group'] for row in rows}
    group_support = {key: sorted({row['group'] for row in rows if row['policy_view']['label5'] == key}) for key in CLASS_KEYS}
    manifest = {'schema_version': 1, 'format': FEATURE_SCHEMA, 'status': 'paired-development-candidate-not-training-ready',
                'created_at': datetime.now(timezone.utc).isoformat(),
                'training_ready': False, 'legacy_exprnet_npz_compatible': False,
                'source': {'annotation': reference(annotation_path), 'sha256': fingerprints[str(annotation_path)],
                           'revision': state['revision'], 'updated_at': state.get('updated_at'),
                           'purpose': state['purpose'], 'annotator': state['annotator'], 'label_source': 'single-user-development-opinion'},
                'dataset_version': dataset.version, 'split': 'dev', 'new_split_created': False,
                'rig_hash': rig.config_hash(), 'fbx_sha256': fbx_hash,
                'config_sha256': {'rig-snapshot.yaml': digest(RIG_PATH),
                                  'canonical-snapshot.yaml': digest(ROOT / 'configs/canonical.yaml')},
                'implementation_sha256': {reference(path): digest(path) for path in
                    [Path(__file__), Path(__file__).with_name('feature_probe.py'), ROOT / 'exprnet/rig.py', ROOT / 'exprnet/canonical.py']},
                'feature_shapes': {'raw17': list(raw17.shape), 'weights31': [len(rows), 31], 'x51': list(x51.shape), 'x59': list(x59.shape)},
                'feature59_schema': 'masked51 + signed raw brow indices 0,1,3,4 + negative half of raw indices 7,9,12,13',
                'max_raw17_reconstruction_error': reconstruction_error,
                'policy': {'known': 'merge-surprise-fear-only-in-five-class-view', 'disgust': disgust_policy,
                           'ambiguous': ambiguous_policy, 'blank': 'always-exclude-do-not-infer-from-note',
                           'unknown_is_supervised_class': False},
                'count': len(rows), 'raw_label_counts': dict(Counter(row['label_original'] or 'unlabeled' for row in rows)),
                'status_counts': dict(Counter(row['annotation_status'] for row in rows)),
                'note_counts': dict(Counter(row['note'] for row in rows if row['note'])),
                'blank_with_note': sum(not row['label_original'] and bool(row['note']) for row in rows),
                'blank_without_note': sum(not row['label_original'] and not row['note'] for row in rows),
                'history_count': len(state['history']),
                'review_tag_policy': 'literal-note-contains-疑惑; review-only, not class or OOD truth',
                'supervised5_candidate_count': int(arrays['supervised5_mask'].sum()), 'class_counts5': class_counts,
                'missing_supervised_classes': missing, 'independent_groups': len(used_groups), 'class_group_support': group_support,
                'limitations': ['All rows remain dev, not independent train/validation/calibration/test.',
                                'Single-user opinions are not independent consensus or golden truth.',
                                'Rows with notes but blank labels are not automatically ambiguous or neutral.',
                                'A paired feature bundle does not implement the downstream training loader.',
                                f'Present counts and {len(used_groups)} neighboring groups do not establish generalization or sufficient training support.'],
                'rows': rows}
    for source, expected in fingerprints.items():
        if digest(Path(source)) != expected:
            raise ValueError('适配期间原记录／采集发生更新，请重读')
    if set(str(p) for p in capture_root.rglob('*.json')) != set(str(p) for p, _, _ in inventory.values()):
        raise ValueError('适配期间采集清单变化，请重读')
    rechecked = Dataset(packet)
    if rechecked.version != dataset.version or rechecked.ids != dataset.ids:
        raise ValueError('适配期间盲标包变化，请重读')
    return manifest, arrays, source_bytes


def replay(manifest, arrays, model_path):
    """单次实际ONNX重放；只在五类标签视图计一致，备注样本只报输出分布。"""
    import onnxruntime as ort
    from exprnet.export import verify_pair
    model_path = Path(model_path)
    ok, message = verify_pair(model_path, model_path.with_suffix('.json'))
    if not ok:
        raise ValueError(message)
    meta = read_json(model_path.with_suffix('.json'))
    if meta['rig']['config_hash'] != manifest['rig_hash'] or [v['key'] for v in meta['labels']] != CLASS_KEYS:
        raise ValueError('部署绑定／类别不匹配')
    options = ort.SessionOptions()
    options.intra_op_num_threads = options.inter_op_num_threads = 1
    session = ort.InferenceSession(str(model_path), sess_options=options, providers=['CPUExecutionProvider'])
    probabilities, energies = session.run(['probs', 'energy'], {'sliders': arrays['raw17']})
    if probabilities.shape != (manifest['count'], 5) or energies.shape != (manifest['count'],):
        raise ValueError('部署输出维度错误')
    results = []
    for row, p, energy in zip(manifest['rows'], probabilities, energies):
        d = decision(p, float(energy), meta['energy_threshold'], meta['min_confidence'])
        argmax = CLASS_KEYS[d['argmax_index']]
        results.append({'id': row['id'], 'label_original': row['label_original'], 'note': row['note'],
                        'review_tags': row['review_tags'], 'label5': row['policy_view']['label5'],
                        'probabilities': dict(zip(CLASS_KEYS, p.tolist())), 'energy': float(energy),
                        'decision': dict(d, argmax_key=argmax, final_key='unknown' if d['rejected'] else argmax)})
    def distribution(rows):
        return {'count': len(rows), 'argmax': dict(Counter(r['decision']['argmax_key'] for r in rows)),
                'final': dict(Counter(r['decision']['final_key'] for r in rows))}
    def agreement(rows):
        return dict(distribution(rows),
                    argmax_agree=sum(r['decision']['argmax_key'] == r['label5'] for r in rows),
                    final_agree=sum(r['decision']['final_key'] == r['label5'] for r in rows),
                    rejected=sum(r['decision']['rejected'] for r in rows))
    known = [r for r in results if r['label5']]
    return {'status': 'single-user-dev-offline-replay-not-independent-accuracy',
            'revision': manifest['source']['revision'], 'onnx_sha256': digest(model_path),
            'metadata_sha256': digest(model_path.with_suffix('.json')),
            'energy_threshold': meta['energy_threshold'], 'min_confidence': meta['min_confidence'],
            'five_class': agreement(known),
            'by_class5': {key: agreement([r for r in known if r['label5'] == key]) for key in CLASS_KEYS},
            'excluded': {key or 'blank': distribution([r for r in results if r['label_original'] == key])
                         for key in ('disgust', 'ambiguous', '')},
            'confused_candidate_output_only': distribution([r for r in results if r['review_tags']]),
            'notes_output_only': {note: distribution([r for r in results if r['note'] == note])
                                 for note in manifest['note_counts']}, 'rows': results}


def revision_diff(source_bytes, previous_path):
    current = json.loads(source_bytes)
    previous = read_json(previous_path)
    if previous['dataset_version'] != current['dataset_version'] or previous['order'] != current['order'] or previous['annotator'] != current['annotator']:
        raise ValueError('旧版与当前ID／版本／标注者不同，不自动合并')
    return {'previous_revision': previous['revision'], 'current_revision': current['revision'],
            'previous_sha256': digest(previous_path), 'stable_ids_changed': False,
            'changes': [{'id': sid, 'order_index': i, 'previous': previous['annotations'].get(sid),
                         'current': current['annotations'].get(sid)} for i, sid in enumerate(current['order'])
                        if previous['annotations'].get(sid) != current['annotations'].get(sid)]}


def write_bundle(out, manifest, arrays, source_bytes, annotation_path):
    out = Path(out)
    if out.exists():
        raise ValueError('输出目录已存在，拒绝覆盖原结果')
    out.parent.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix=out.name + '.partial-', dir=out.parent))
    # 中途失败保留partial供检查；最终目录只在全部写出且重读验证后发布。
    np.savez_compressed(staging / 'features.npz', **arrays)
    (staging / 'annotation-original.json').write_bytes(source_bytes)
    captures = staging / 'capture-originals'
    captures.mkdir()
    for row in manifest['rows']:
        original = (ROOT / row['capture_json']).read_bytes() if not row['capture_json'].startswith('external-fixture/') else None
        if original is not None:
            if hashlib.sha256(original).hexdigest() != row['capture_sha256']:
                raise ValueError('发布前原始快照变化，partial保留')
            (captures / (row['id'] + '.json')).write_bytes(original)
    for name, path in [('rig-snapshot.yaml', RIG_PATH), ('canonical-snapshot.yaml', ROOT / 'configs/canonical.yaml')]:
        content = path.read_bytes()
        if hashlib.sha256(content).hexdigest() != manifest['config_sha256'][name]:
            raise ValueError('发布前配置变化，partial保留；禁止混用特征／配置快照')
        (staging / name).write_bytes(content)
    backup = Path(annotation_path).with_suffix('.bak')
    if backup.is_file():
        (staging / 'annotation-previous.bak').write_bytes(backup.read_bytes())
    with np.load(staging / 'features.npz', allow_pickle=False) as saved:
        for name, value in arrays.items():
            np.testing.assert_array_equal(saved[name], value)
    manifest = dict(manifest)
    if 'diagnosis' in manifest:
        (staging / 'diagnosis.json').write_text(json.dumps(manifest.pop('diagnosis'), ensure_ascii=False, indent=2, allow_nan=False), encoding='utf-8')
    manifest['files'] = {p.relative_to(staging).as_posix(): digest(p) for p in staging.rglob('*') if p.is_file()}
    (staging / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2, allow_nan=False), encoding='utf-8')
    if out.exists():
        raise ValueError('输出目录被并发创建，partial保留，不覆盖')
    try:
        os.rename(staging, out)
    except PermissionError:
        # Windows受限工作区可能允许写文件但拒绝目录重命名。
        # 独占建目录，逐文件复制并校验；manifest最后出现才表示完成。
        out.mkdir()
        for path in staging.rglob('*'):
            if path.is_file() and path.name != 'manifest.json':
                target = out / path.relative_to(staging)
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(path, target)
                if digest(target) != manifest['files'][path.relative_to(staging).as_posix()]:
                    raise ValueError('输出复制哈希不符，目录不含完成manifest')
        manifest['publication'] = 'verified-file-copy-manifest-last; staging-retained'
        (out / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2, allow_nan=False), encoding='utf-8')
    return manifest


def main(argv=None):
    parser = argparse.ArgumentParser(description='raw17＋单人dev标签→成对51D/59D候选包；不训练、不部署、不划分test')
    parser.add_argument('--annotations', type=Path, required=True, help='明确选择一份原标注JSON；不自动选或合并多人')
    parser.add_argument('--packet', type=Path, default=PACKET)
    parser.add_argument('--captures', type=Path, default=ROOT / 'data/laila-captures-v2')
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--disgust-policy', choices=['exclude', 'outside-class-reference'], default='exclude')
    parser.add_argument('--ambiguous-policy', choices=['exclude', 'uncertainty-reference'], default='exclude')
    parser.add_argument('--require-all-five-classes', action='store_true')
    parser.add_argument('--replay-model', type=Path, help='只读重放现有部署ONNX，不训练或改阈值')
    parser.add_argument('--previous', type=Path, help='同一标注者旧快照，只比较，不合并')
    args = parser.parse_args(argv)
    try:
        artifact_root = (ROOT / 'artifacts/laila_v2_candidate').resolve()
        if not all(p.resolve().is_relative_to(artifact_root) for p in (args.annotations, args.packet, args.out)):
            raise ValueError('标注／包／输出须在本项目artifacts/laila_v2_candidate范围，禁止覆盖源数据／部署')
        if not args.captures.resolve().is_relative_to((ROOT / 'data/laila-captures-v2').resolve()):
            raise ValueError('采集只从本项目data/laila-captures-v2读取')
        if args.out.resolve().is_relative_to(args.packet.resolve()) or args.annotations.resolve().is_relative_to(args.out.resolve()):
            raise ValueError('输出不能覆盖或混入原始包／记录')
        manifest, arrays, source = prepare(args.annotations, args.packet, args.captures,
            disgust_policy=args.disgust_policy, ambiguous_policy=args.ambiguous_policy,
            require_all_classes=args.require_all_five_classes)
        if args.previous:
            manifest['revision_diff'] = revision_diff(source, args.previous)
        if args.replay_model:
            manifest['diagnosis'] = replay(manifest, arrays, args.replay_model)
        manifest = write_bundle(args.out, manifest, arrays, source, args.annotations)
        print(json.dumps({'success': True, 'out': reference(args.out), 'rows': manifest['count'],
                          'supervised5_candidates': manifest['supervised5_candidate_count'],
                          'class_counts5': manifest['class_counts5'], 'groups': manifest['independent_groups'],
                          'missing_classes': manifest['missing_supervised_classes'], 'training_ready': False,
                          'reconstruction_error': manifest['max_raw17_reconstruction_error']}, ensure_ascii=False))
        return 0
    except (ValueError, KeyError, TypeError, OSError, AssertionError) as error:
        print(json.dumps({'success': False, 'error': str(error)}, ensure_ascii=False))
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
