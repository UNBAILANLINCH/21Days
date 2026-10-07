"""显式试点启动时冻结新采集批次，完成／失败即退出；不训练或写人工标签。
复用现有采集、Dataset和CSV标注流程；固定139包不能保存dev/test双角色，
因此仅新增组包编排，原包、原记录和用户服务不变。
"""
from __future__ import annotations

import csv
import hashlib
import json
from pathlib import Path

from analysis.laila_v2_candidate.annotator import ROOT, Dataset, atomic, digest

FORMAT = 'laila-independent-pilot-batch-v1'
BATCH_ROOT = ROOT/'artifacts/laila_v2_candidate/pilot-batches'


def is_pilot_group(group):
    return isinstance(group,str) and group.startswith(('pilot-', '试点-'))


def build_batch(captures=None, output=None):
    """原UUID／组／split不变；只纳入明确试点组，忽略旧139及其他实验。"""
    from analysis.laila_v2_candidate.capture_diagnostics import validate_capture, FBX_PATH, RIG_PATH
    from exprnet.canonical import load_canonical
    from exprnet.rig import load_rig
    captures = Path(captures or ROOT/'data/laila-captures-v2')
    output = Path(output or BATCH_ROOT)
    rig = load_rig(RIG_PATH,load_canonical())
    entries, originals, groups, poses, ids = [], {}, {}, {}, set()
    for split in ('dev','test'):
        for path in sorted((captures/split).glob('*.json')):
            raw = json.loads(path.read_text(encoding='utf-8'))
            if not is_pilot_group(raw.get('group')):
                continue
            values = validate_capture(raw,rig,digest(FBX_PATH))
            sid, group = raw['id'], raw['group']
            if raw['split'] != split or path.stem != sid or sid in ids:
                raise ValueError('试点UUID重复或文件／划分不符')
            if group in groups and groups[group] != split:
                raise ValueError('同一姿态组跨校准／测试')
            pose = tuple(float(v) for v in values)
            if pose in poses and poses[pose] != split:
                raise ValueError('校准和测试出现完全相同脸参数；请使用不同基础姿态')
            image = path.with_suffix('.png')
            if not image.is_file():
                raise ValueError('试点采集缺少配套图片')
            ids.add(sid); groups[group] = split; poses[pose] = split
            entries.append({'id':sid,'group':group,'split':split,
                            'data_role':'calibration' if split=='dev' else 'locked-test',
                            'rig_hash':raw['rig_hash'],'fbx_sha256':raw['fbx_sha256'],
                            'capture_sha256':digest(path),'image_sha256':digest(image)})
            originals[sid] = (path,image)
    if not entries:
        return None
    # 顺序固定、匿名；不按目标表情或模型预测排序。
    entries.sort(key=lambda e: hashlib.sha256(('pilot-order:'+e['id']).encode()).hexdigest())
    version = hashlib.sha256(json.dumps(entries,sort_keys=True,separators=(',',':')).encode()).hexdigest()
    packet = output/version
    if packet.exists():
        Dataset(packet)
        return packet
    packet.mkdir(parents=True)
    (packet/'captures').mkdir()
    (packet/'annotators/images').mkdir(parents=True)
    for e in entries:
        capture,image = originals[e['id']]
        for source,target,sha in ((capture,packet/'captures'/(e['id']+'.json'),e['capture_sha256']),
                                  (image,packet/'annotators/images'/(e['id']+'.png'),e['image_sha256'])):
            content = source.read_bytes()
            if hashlib.sha256(content).hexdigest()!=sha:
                raise ValueError('组包期间原采集变化；未完成包保留，不覆盖原件')
            target.write_bytes(content)
    for n in range(1,4):
        with (packet/f'annotators/annotator_{n}.csv').open('w',encoding='utf-8-sig',newline='') as stream:
            writer = csv.DictWriter(stream,fieldnames=['id','label'])
            writer.writeheader()
            writer.writerows({'id':e['id'],'label':''} for e in entries)
    atomic(packet/'steward-manifest.json',{'format':FORMAT,'purpose':'pilot-calibration-and-locked-test-not-training',
                                         'training_eligible':False,'entries':entries})
    Dataset(packet)
    return packet
