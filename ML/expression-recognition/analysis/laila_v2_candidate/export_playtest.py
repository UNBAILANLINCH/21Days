"""手动一次导出59D研究试玩模型；自检完成或失败即退出，不训练／覆盖旧部署。
终端JSON为状态锚点，图内保留17轴→59D；预设只是待视觉确认的试玩意图。
"""
import argparse
import json
from pathlib import Path
import numpy as np
import torch

from exprnet.export import DeployModel, export_onnx, verify_pair
from exprnet.models import load_checkpoint
from exprnet.rig import load_rig
from exprnet.canonical import load_canonical
from analysis.laila_v2_candidate.retrain_candidate import expanded_model
from analysis.laila_v2_candidate.feature_probe import extended_features, BROW, ERASED
from analysis.laila_v2_candidate.capture_diagnostics import ROOT, RIG_PATH, RUN_PATH, MODEL_PATH, read_json, digest
from analysis.laila_v2_candidate.input_diagnostic import CANDIDATE_ROOT


class PlaytestModel(DeployModel):
    def __init__(self, rig, canonical, classifier, temperature):
        super().__init__(rig, canonical, classifier, temperature)
        for name, indices in [('brow_selector', BROW), ('erased_selector', ERASED)]:
            selector = torch.zeros(17, 4)
            for column, axis in enumerate(indices):
                selector[axis, column] = 1
            self.register_buffer(name, selector)

    def features(self, sliders):
        s = torch.minimum(torch.maximum(sliders, self.s_min), self.s_max)
        x = torch.clamp(torch.relu(s) @ self.w_pos + torch.relu(-s) @ self.w_neg, 0., 1.) * self.mask
        return torch.cat([x, s @ self.brow_selector, torch.relu(-s) @ self.erased_selector], dim=1)

    def forward(self, sliders):
        z = self.classifier(self.features(sliders)) / self.temperature
        probs = torch.softmax(z, dim=-1)
        m = torch.amax(z, dim=-1, keepdim=True)
        energy = -self.temperature * (m.squeeze(-1) + torch.log(torch.sum(torch.exp(z-m), dim=-1)))
        return probs, energy


def preset_rows():
    intentions = [('中性', {}), ('高兴', {7:.35,9:.35,10:.7,11:.7,12:.3,13:.3}),
                  ('悲伤', {0:.6,3:.6,2:-.3,5:-.3,10:-.6,11:-.6}),
                  ('惊讶／恐惧', {0:.6,2:.7,3:.6,5:.7,6:.65,8:.65,14:.2,15:.2,16:.65}),
                  ('愤怒', {0:-.65,1:-.5,3:-.65,4:-.5,6:-.25,8:-.25,7:.4,9:.4})]
    rows = []
    for title, axes in intentions:
        for scale, strength in [(0.5,'轻'),(.75,'中'),(1.,'强')]:
            values = [0.] * 17
            for axis, value in axes.items():
                values[axis] = value * scale
            if not axes and strength != '中':
                values[0] = .03 if strength == '轻' else -.03
                values[3] = values[0]
            rows.append({'title': title + ('附近' if not axes else ' · ' + strength),
                         'intent': title, 'values': values, 'human_label': None})
    return rows


def run(out):
    out = Path(out).resolve()
    if not out.is_relative_to((ROOT/'artifacts/laila_v2_candidate').resolve()) or out.exists():
        raise ValueError('须使用artifacts下尚不存在的独立输出目录')
    candidate = CANDIDATE_ROOT/'new59_tau0.pt'
    if digest(candidate) != read_json(CANDIDATE_ROOT/'artifact-hashes.json')['new59_tau0.pt']:
        raise ValueError('研究checkpoint hash不一致')
    checkpoint = torch.load(candidate, map_location='cpu', weights_only=False)
    if checkpoint['dimension'] != 59 or checkpoint['source_revision'] != 779:
        raise ValueError('须使用固定r779新59D候选')
    baseline, old, canonical = load_checkpoint(RUN_PATH)
    model = expanded_model(baseline, 59)
    model.load_state_dict(checkpoint['state_dict'])
    rig = load_rig(RIG_PATH, canonical)
    deployed = PlaytestModel(rig, canonical, model.eval(), old['temperature']).eval()
    values = np.random.default_rng(42).uniform(rig.s_min, rig.s_max, (1000,17)).astype(np.float32)
    expected = extended_features(rig, values)
    expected[:, :51] *= canonical.mask_vector
    with torch.no_grad():
        np.testing.assert_allclose(deployed.features(torch.from_numpy(values)).numpy(), expected, atol=1e-7, rtol=0)
    out.mkdir(parents=True)
    path = out/'laila_research59_r779.onnx'
    checks = export_onnx(deployed, rig, path)
    if not checks['ok']:
        raise ValueError('导出自检失败，保留现场，不导入Unity：'+str(checks))
    meta = read_json(MODEL_PATH.with_suffix('.json'))
    meta.update(name='laila_research59_r779_playtest', metrics_summary={}, training_data=['single-user-r779-development'],
                checks={k:v for k,v in checks.items() if k!='io'},
                research={'purpose':'single-user-development-playtest-not-deployment', 'feature_dimension':59,
                          'checkpoint_sha256':digest(candidate),'source_annotation_sha256':checkpoint['source_sha256'],
                          'source_revision':779,'rejection_calibrated':False,'threshold_policy':'old-policy-reference-only'},
                playtest_presets=preset_rows())
    meta['onnx'] = {'sha256':digest(path),'file':path.name,'opset':15,**checks['io']}
    meta['threshold_calibration'] = {'rule':'uncalibrated-old-policy-research-reference','full_rejection_calibrated':False}
    meta['energy_threshold_rule'] = 'uncalibrated-old-policy-research-reference'
    meta['onnx']['notes'] = '外部17轴；图内59D。沿用旧温度和拒识门槛，不代表研究模型已校准。'
    path.with_suffix('.json').write_text(json.dumps(meta,ensure_ascii=False,indent=2),encoding='utf-8')
    assert verify_pair(path,path.with_suffix('.json'))[0]
    import onnxruntime as ort
    session = ort.InferenceSession(str(path),providers=['CPUExecutionProvider'])
    probes = np.vstack([np.zeros((1,17),dtype=np.float32),np.eye(17,dtype=np.float32)*.5,
                       -np.eye(17,dtype=np.float32)[:14]*.5,
                       np.array([r['values'] for r in preset_rows()],dtype=np.float32),values[:100]])
    p,e = session.run(['probs','energy'],{'sliders':probes})
    proof = {'checks':checks,'source_checkpoint_sha256':digest(candidate),'onnx_sha256':digest(path),
             'rejection_calibrated':False,'rows':[{'raw17':s.tolist(),'probabilities':v.tolist(),'energy':float(a)}
                for s,v,a in zip(probes,p,e)]}
    (out/'verification-inputs.json').write_text(json.dumps(proof,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({'ok':True,'out':str(out.relative_to(ROOT)),'checks':checks,'sha256':digest(path)},ensure_ascii=False),flush=True)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out',type=Path,required=True)
    run(parser.parse_args().out)
