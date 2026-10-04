"""离线一次性探针，不接Unity、不改51规范空间/部署、不训练；完成即退出。
5秒锚点是终端完成结果。复用现有Rig，检查附加8个纯rig特征能否保留当前17轴。
这是信息保留候选，不是AU映射或人工可分性证据，也不声称数学最少维数。
"""
import json
import numpy as np
from analysis.laila_v2_candidate.capture_diagnostics import ROOT, RIG_PATH, read_json
from exprnet.canonical import load_canonical
from exprnet.rig import load_rig

BROW = [0, 1, 3, 4]  # 左右内眉/眉中；独立保留有符号rig值。
ERASED = [7, 9, 12, 13]  # 下睑Down、嘴角In的四个负向量。


def extended_features(rig, sliders):
    s = np.asarray(sliders)
    return np.concatenate([rig.forward(s), s[:, BROW], np.maximum(-s[:, ERASED], 0)], axis=1)


def decode(rig, features):
    # 针对当前候选，无饱和、每一侧Outer通道及Down通道的确定系数。
    # 不将这个解码公式移植到修改过的绑定。
    c = rig.canonical
    x, extra = features[:, :c.dim], features[:, c.dim:]
    s = np.zeros((len(features), 17))
    s[:, BROW] = extra[:, :4]
    for inner, mid, outer, side in [(0, 1, 2, 'Right'), (3, 4, 5, 'Left')]:
        pos = (x[:, c.index('browOuterUp' + side)] - .25 * np.maximum(s[:, mid], 0)) / .75
        neg = (x[:, c.index('browDown' + side)] - .4 * np.maximum(-s[:, inner], 0)
               - .35 * np.maximum(-s[:, mid], 0)) / .25
        s[:, outer] = pos - neg
    for i, side in [(6, 'Right'), (8, 'Left')]:
        s[:, i] = x[:, c.index('eyeWide' + side)] - x[:, c.index('eyeBlink' + side)]
    for offset, i, side in [(0, 7, 'Right'), (1, 9, 'Left')]:
        s[:, i] = x[:, c.index('eyeSquint' + side)] - extra[:, 4 + offset]
    for i, side in [(10, 'Right'), (11, 'Left')]:
        s[:, i] = x[:, c.index('mouthSmile' + side)] - x[:, c.index('mouthFrown' + side)]
    for offset, i, side in [(2, 12, 'Right'), (3, 13, 'Left')]:
        s[:, i] = x[:, c.index('mouthStretch' + side)] - extra[:, 4 + offset]
    s[:, 14] = x[:, c.index('mouthUpperUpRight')]
    s[:, 15] = x[:, c.index('mouthUpperUpLeft')]
    s[:, 16] = x[:, c.index('mouthLowerDownLeft')]
    return s


def main():
    rig = load_rig(RIG_PATH, load_canonical())
    if rig.config_hash() != '56daba66fc94a2101f8501201e4d43b91ff644f71f8179d08abd2fd7c82e5b9c':
        raise ValueError('只验证本轮候选，绑定变化需重审解码公式')
    m = read_json(ROOT / 'artifacts/laila_v2_candidate/unity-round-20261003/unity-manifest.json')
    real = np.array([[read_json(ROOT / 'data/laila-captures-v2/dev' / (e['id'] + '.json'))['sliders'][k]
                      for k in rig.keys] for e in m['entries']])
    random = np.random.default_rng(42).uniform(rig.s_min, rig.s_max, size=(10000, 17))
    result = {'candidate': '51 canonical + 4 signed brow rig axes + 4 omitted negative rig directions',
              'deployment_changed': False, 'human_accuracy_measured': False}
    for name, values in [('real_development', real), ('random_numeric_probe', random)]:
        encoded = extended_features(rig, values)
        error = float(np.max(np.abs(values - decode(rig, encoded))))
        if error > 1e-6:
            raise ValueError('不能还原当前17轴：' + str(error))
        result[name] = {'n': len(values), 'features': encoded.shape[1], 'max_17_reconstruction_error': error}
    print(json.dumps(result, ensure_ascii=False))


if __name__ == '__main__':
    main()
