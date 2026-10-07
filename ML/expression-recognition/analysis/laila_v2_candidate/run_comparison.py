"""执行主体：手动CLI；5秒检查：artifacts/laila_v2_candidate/comparison.json的状态与hash。
退出：进程退出，不常驻；候选冻结后保留本脚本用于复现。
对照只锁住当前模型的新增自由度，不使用旧FBX，不证明人工准确率。
"""
from pathlib import Path
import json
import numpy as np
import torch
from exprnet.common import CONFIG_DIR, ARTIFACTS_DIR, load_yaml
from exprnet.canonical import load_canonical
from exprnet.rig import load_rig, rig_from_dict
from exprnet.train import train
from exprnet.models import load_checkpoint
from exprnet.export import DeployModel


def main():
    canonical = load_canonical()
    rig = load_rig(CONFIG_DIR / "rigs/laila_rig_v2_candidate.yaml", canonical)
    raw = rig.to_config()
    # 6眉轴限制成左右整眉各一轴：同侧三段只能同方向等权重；
    # 上唇只允许左右等权重。映射仍来自同一候选，避免混入旧资产差异。
    rows = raw["sliders"]
    merged = []
    for side, ids in (("L", [0, 1, 2]), ("R", [3, 4, 5])):
        row = {"key": "brow_" + side + "_y", "range": [-1, 1], "default": 0, "pos": {}, "neg": {}}
        for kind in ("pos", "neg"):
            for i in ids:
                for key, value in rows[i].get(kind, {}).items():
                    row[kind][key] = row[kind].get(key, 0) + value
        merged.append(row)
    merged += rows[6:14]
    merged.append({"key": "upper_lip", "range": [0, 1], "default": 0,
                   "pos": rows[14]["pos"] | rows[15]["pos"]})
    merged.append(rows[16])
    control = rig_from_dict({"name": "laila_v2_candidate_locked12", "sliders": merged}, canonical)
    out = ARTIFACTS_DIR / "laila_v2_candidate"
    out.mkdir(parents=True, exist_ok=True)
    import yaml
    path = out / "locked12.yaml"
    path.write_text(yaml.safe_dump(control.to_config(), allow_unicode=True, sort_keys=False), encoding="utf-8")
    # 直接复用CLI训练的同一默认配置与种子/合成样本预算。
    cfg = load_yaml(CONFIG_DIR / "train.yaml")
    cfg.update({"rig": str(path), "golden": None, "label_set": "configs/label_sets/laila_5class.yaml", "seed": 42})
    cfg["infeasible_variants"]["drop"] = True
    cfg["model"]["name"] = "resmlp"
    run = ARTIFACTS_DIR / "runs/laila_cand_locked12_s42_20261003"
    if not (run / "ckpt.pt").exists(): train(cfg, run.name)
    current = ARTIFACTS_DIR / "runs/laila_cand_v2_s42_20261003"
    metrics = {name: json.loads((folder / "metrics.json").read_text(encoding="utf-8"))
               for name, folder in (("locked12", run), ("full17", current))}
    report = {"status": "synthetic-candidate-only", "seed": 42,
              "caveat": "不是旧FBX对照；各绑定投影不同。变体过滤集合须核对；只跑一个种子，不能证明玩家准确率。",
              "full17_rig_hash": rig.config_hash(), "locked12_rig_hash": control.config_hash(), "metrics": metrics}
    (out / "comparison.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    # 固定数值fixture：零、单轴正负极值、随机合法组合，对照Unity/ONNX。
    classifier, ckpt, canonical = load_checkpoint(current)
    deploy = DeployModel(rig, canonical, classifier, ckpt["temperature"])
    values = rig.random_sliders(100, np.random.default_rng(314))
    values = np.concatenate([np.zeros((1, 17)), np.eye(17), -np.eye(17), values]).astype(np.float32)
    values = np.clip(values, rig.s_min, rig.s_max).astype(np.float32)
    with torch.no_grad(): probs, energy = deploy(torch.from_numpy(values))
    fixture = {"rig_hash": rig.config_hash(), "entries": [
        {"sliders": s.tolist(), "probs": p.tolist(), "energy": float(e)}
        for s, p, e in zip(values, probs.numpy(), energy.numpy())]}
    (out / "fixture.json").write_text(json.dumps(fixture), encoding="utf-8")
    print("Comparison and 135-input fixture saved", out)


if __name__ == "__main__": main()
