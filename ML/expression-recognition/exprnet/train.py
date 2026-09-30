"""训练 + 校准 + 评估（DESIGN §4–§7）。

产物放 artifacts/runs/<名字>/：
    ckpt.pt          模型权重、类别、规范空间与屏蔽维、温度 T、energy 阈值、训练绑定、数据来源
    config.yaml      本次实际生效的训练配置
    config_snapshot/ labels / canonical / facs / 绑定配置的原样拷贝
    eval_data.npz    验证集与留出负样本（evaluate 重评用）
    metrics.json     指标
    report.md        中文报告

类别集（--label-set）：输出类换成类别集里的类，合并类的合成样本由成员类各出一半，集外类不进正样本。
按绑定剔除变体（--drop-infeasible-variants）：核心 AU 投影到训练绑定后相对残差 ≥ 阈值的变体 / 备选不进训练目标，
剔除清单写进 ckpt 与 report.md。
金标在训练开始前就查：绑定名 / rig_hash 不符时报错退出（--allow-golden-skip 降级为跳过），值 / 标签 / id 不合法报错。

用法示例（在 ML/expression-recognition 目录下）：
    python -m exprnet.train --name rf_synth
    python -m exprnet.train --name mlp_synth --model resmlp
    python -m exprnet.train --name rf_mix --npz data/features/rafdb.npz data/features/ckplus.npz
    python -m exprnet.train --name laila_cand_opt_5c_resmlp --model resmlp --golden none \\
        --rig analysis/laila_upper_bound/laila_candidate_optimistic.yaml --label-set laila_5class --drop-infeasible-variants
"""

from __future__ import annotations

import copy
import math
import shutil
import sys
import time
from pathlib import Path

import numpy as np
import torch
import yaml

from .augment import intensity_scale, mirror, mixup, pick_projection
from .calibrate import energy_np, fit_temperature, pick_threshold
from .canonical import load_canonical
from .common import (make_parser, ARTIFACTS_DIR, CONFIG_DIR, label_set_path, load_label_set, load_labels, load_yaml,
                     portable_path, rel_to_project, resolve_cli_path, resolve_path, set_seed)
from .datasets import build_train_data
from .evaluate import classification_metrics, evaluate_run, logits_of
from .facs import FacsSynthesizer, plan_variant_filter
from .golden_check import GoldenError, check_entries, format_problems, load_golden, rig_mismatch
from .losses import ExprLoss
from .models import build_model, count_params
from .rig import load_rig


def load_config(path: str | Path | None = None) -> dict:
    return load_yaml(resolve_path(path or CONFIG_DIR / "train.yaml"))


def _lr_lambda(total: int, warmup: int):
    def f(step: int) -> float:
        if step < warmup:
            return (step + 1) / max(1, warmup)
        prog = (step - warmup) / max(1, total - warmup)
        return 0.5 * (1.0 + math.cos(math.pi * min(1.0, prog)))

    return f


def _param_groups(model: torch.nn.Module, wd: float):
    decay, no_decay = [], []
    for n, p in model.named_parameters():
        (decay if p.ndim >= 2 and "emb" not in n else no_decay).append(p)
    return [{"params": decay, "weight_decay": wd}, {"params": no_decay, "weight_decay": 0.0}]


def precheck_golden(golden: Path, rig, annotation_keys: list[str], allow_skip: bool, log=print) -> None:
    """训练开始前查金标，免得训完才发现绑定不符：不符时报错（allow_skip 时只提示，评估阶段跳过），数据不合法报错。"""
    g = load_golden(golden)
    mm = rig_mismatch(g, rig, rel_to_project(golden))
    if mm:
        if allow_skip:
            log(f"[金标] {mm}；已按 --allow-golden-skip 降级为跳过")
            return
        raise GoldenError(f"{mm}。换对应绑定的金标，或加 --golden none 不评金标；确实要跳过请加 --allow-golden-skip")
    problems = check_entries(g, rig, annotation_keys, rel_to_project(golden))
    if problems:
        raise GoldenError(f"金标 {rel_to_project(golden)} 有 {len(problems)} 个问题：\n{format_problems(problems)}")


def _variant_filter(cfg: dict, canonical, labels, facs_cfg: dict, rig, log=print) -> dict:
    """按绑定定剔除清单（配置 infeasible_variants）。没打开时返回 {"enabled": False}。"""
    fcfg = cfg.get("infeasible_variants") or {}
    if not fcfg.get("drop"):
        return {"enabled": False}
    probe = FacsSynthesizer(canonical, labels.keys, cfg=facs_cfg, class_members=labels.members())
    info = plan_variant_filter(probe, rig, float(fcfg.get("rel_residual_threshold", 0.9)),
                               float(fcfg.get("probe_intensity", 0.6)))
    if rig.identity:
        log(f"[变体剔除] 已打开，但{info['note']}")
        return info
    log(f"[变体剔除] 绑定 {rig.name}，阈值：相对残差 ≥ {info['rel_residual_threshold']:g}（统一强度 {info['probe_intensity']:g}），"
        f"检查 {info['n_checked']} 个变体 / 备选，剔除 {len(info['dropped'])} 个")
    for r in info["dropped"]:
        alt = f" 备选 #{r['choose']}" if r["choose"] is not None else ""
        log(f"           - {r['class']}（{r['emotion']}）变体 #{r['variant']}{alt} [{r['source']}] "
            f"{'+'.join(r['aus'])}：相对残差 {r['rel_residual']:.3f}")
    return info


def train(cfg: dict, name: str, out_root: Path | None = None, log=print, use_cache: bool = True) -> Path:
    t0 = time.time()
    seed = int(cfg.get("seed", 0))
    set_seed(seed)
    torch.set_num_threads(max(1, min(8, torch.get_num_threads())))
    labels = load_labels(cfg.get("labels"))
    if cfg.get("label_set"):
        labels = load_label_set(cfg["label_set"], base=labels)
        log(f"[类别] 类别集 {labels.label_set['name']}：" + "，".join(
            f"{c['key']} ← {'+'.join(c['from'])}" for c in labels.label_set["classes"])
            + (f"；集外类 {', '.join(labels.dropped)}（不进正样本）" if labels.dropped else ""))
    canonical = load_canonical(cfg.get("canonical"))
    facs_cfg = load_yaml(resolve_path(cfg.get("facs") or CONFIG_DIR / "facs.yaml"))
    rig = load_rig(cfg.get("rig"), canonical)
    golden = resolve_path(cfg["golden"]) if cfg.get("golden") else None
    allow_golden_skip = bool(cfg.get("golden_allow_skip", False))
    if golden is not None:
        precheck_golden(golden, rig, labels.annotation_keys, allow_golden_skip, log=log)
    vfilter = _variant_filter(cfg, canonical, labels, facs_cfg, rig, log=log)
    synth = FacsSynthesizer(canonical, labels.keys, cfg=facs_cfg, class_members=labels.members(),
                            exclude=vfilter.get("dropped"))
    run_dir = (out_root or ARTIFACTS_DIR / "runs") / name
    run_dir.mkdir(parents=True, exist_ok=True)
    log(f"[训练] 产物目录 {rel_to_project(run_dir)}")

    data = build_train_data(cfg, canonical, labels, synth, rig, log=log, use_cache=use_cache)
    C = labels.num_classes
    mcfg = cfg["model"]
    model = build_model(mcfg["name"], mcfg, canonical, C)
    n_params = count_params(model)
    log(f"[训练] 模型 {mcfg['name']}，参数量 {n_params:,}")

    ocfg = cfg["optim"]
    acfg = cfg.get("augment", {})
    lcfg = cfg.get("loss", {})
    B = int(ocfg.get("batch_size", 256))
    epochs = int(ocfg.get("epochs", 10))
    proj_p = 0.0 if rig.identity else float(cfg["data"].get("projection_prob", 0.7))
    neg_ratio = float(lcfg.get("neg_batch_ratio", 0.5))
    loss_fn = ExprLoss(lcfg, torch.as_tensor(data.class_prior))
    opt = torch.optim.AdamW(_param_groups(model, float(ocfg.get("weight_decay", 0.05))), lr=float(ocfg.get("lr", 3e-3)))
    N = len(data.x_train)
    steps = math.ceil(N / B)
    sched = torch.optim.lr_scheduler.LambdaLR(opt, _lr_lambda(steps * epochs, int(float(ocfg.get("warmup_epochs", 1)) * steps)))
    gen = torch.Generator().manual_seed(seed)

    xr = torch.from_numpy(data.x_train)
    xp = torch.from_numpy(data.xp_train)
    yt = torch.from_numpy(data.y_train)
    real = torch.from_numpy(data.real_train)
    nr = torch.from_numpy(data.neg_train)
    npj = torch.from_numpy(data.negp_train)
    mask = torch.from_numpy(canonical.mask_vector).view(1, -1)
    mirror_idx = torch.from_numpy(canonical.mirror_index)
    icfg = acfg.get("intensity", {})
    apply_to = set(icfg.get("apply_to", ["real"]))
    mix = acfg.get("mixup", {})
    mirror_p = float(acfg.get("mirror_prob", 0.5))

    best = (-1.0, 0, None)
    history = []
    patience = int(ocfg.get("early_stop_patience", 5))
    for ep in range(1, epochs + 1):
        model.train()
        perm = torch.randperm(N, generator=gen)
        tot, tot_ce, tot_oe, nb = 0.0, 0.0, 0.0, 0
        te = time.time()
        for s in range(0, N, B):
            idx = perm[s : s + B]
            x = pick_projection(xr[idx], xp[idx], proj_p, gen)
            r = real[idx]
            scale_rows = torch.zeros_like(r)
            if "real" in apply_to:
                scale_rows |= r
            if "synthetic" in apply_to:
                scale_rows |= ~r
            x = intensity_scale(x, scale_rows, float(icfg.get("prob", 0.0)), float(icfg.get("low", 0.8)), float(icfg.get("high", 2.0)), gen)
            x = mirror(x, mirror_idx, mirror_p, gen) * mask
            y = yt[idx]
            if mix and torch.rand(1, generator=gen).item() < float(mix.get("prob", 0.0)):
                x, y = mixup(x, y, float(mix.get("alpha", 0.4)), gen)
            n_neg = int(len(idx) * neg_ratio)
            if n_neg > 0 and len(nr):
                j = torch.randint(0, len(nr), (n_neg,), generator=gen)
                xn = mirror(pick_projection(nr[j], npj[j], proj_p, gen), mirror_idx, mirror_p, gen) * mask
                logits = model(torch.cat([x, xn]))
                lp, ln = logits[: len(x)], logits[len(x) :]
            else:
                lp, ln = model(x), None
            loss, parts = loss_fn(lp, y, ln)
            opt.zero_grad(set_to_none=True)
            loss.backward()
            torch.nn.utils.clip_grad_norm_(model.parameters(), 1.0)
            opt.step()
            sched.step()
            tot += float(loss.detach()); tot_ce += parts["ce"]; tot_oe += parts.get("oe", 0.0); nb += 1
        # 验证（部署视角：有绑定时用投影版）
        lz = logits_of(model, data.xp_val, canonical.mask_vector)
        vm = classification_metrics(data.y_val, lz.argmax(-1), labels.keys)
        h = {"epoch": ep, "loss": tot / nb, "ce": tot_ce / nb, "oe": tot_oe / nb, "val_acc": vm["accuracy"],
             "val_macro_f1": vm["macro_f1"], "lr": sched.get_last_lr()[0], "seconds": time.time() - te}
        history.append(h)
        log(f"[训练] epoch {ep:>2}/{epochs}  损失 {h['loss']:.4f}（CE {h['ce']:.4f}，OE 超出 {h['oe']:.4f}）  "
            f"验证 acc {h['val_acc']:.4f}  macro-F1 {h['val_macro_f1']:.4f}  {h['seconds']:.1f}s")
        if vm["macro_f1"] > best[0]:
            best = (vm["macro_f1"], ep, copy.deepcopy(model.state_dict()))
        elif ep - best[1] >= patience:
            log(f"[训练] 验证 macro-F1 连续 {patience} 个 epoch 未提升，早停")
            break
    model.load_state_dict(best[2])
    model.eval()

    # 校准（DESIGN §6）
    ccfg = cfg.get("calibration", {})
    lz = logits_of(model, data.xp_val, canonical.mask_vector)
    T = fit_temperature(lz, data.y_val)
    rule = str(ccfg.get("threshold_rule", "tnr95"))
    tnr, tpr = float(ccfg.get("tnr", 0.95)), float(ccfg.get("tpr", 0.95))
    # TNR95 只在「标定用负样本」上取；留出负样本不参与定阈值，只用来报拒识率
    e_neg_calib = energy_np(logits_of(model, data.negp_calib, canonical.mask_vector), T)
    thr, both = pick_threshold(rule, energy_np(lz, T), e_neg_calib, tnr, tpr)
    log(f"[校准] 温度 T = {T:.4f}；energy_threshold = {thr:.4f}（规则 {rule}；"
        f"对照：TNR95 {both['tnr95']:.4f}，ID95 {both['id95']:.4f}）")

    ckpt = {
        "model_name": mcfg["name"],
        "model_cfg": dict(mcfg.get(mcfg["name"], {}) or {}),
        "state_dict": model.state_dict(),
        "params": n_params,
        "class_keys": labels.keys,
        "class_zh": labels.zh,
        "label_set": labels.label_set,
        "variant_filter": vfilter,
        "canonical_names": canonical.names,
        "masked_names": canonical.masked_names,
        "temperature": T,
        "energy_threshold": thr,
        "threshold_rule": rule,
        "threshold_params": {"tnr": tnr, "tpr": tpr},
        "energy_threshold_tnr95": both["tnr95"],
        "energy_threshold_id95": both["id95"],
        "min_confidence": float(ccfg.get("min_confidence", 0.4)),
        "train_rig": rig.to_config(),
        "train_rig_hash": rig.config_hash(),
        "data_sources": data.sources,
        "negatives": data.negatives_info,
        "commercial_use_allowed": data.commercial_use_allowed,
        "seed": seed,
    }
    torch.save(ckpt, run_dir / "ckpt.pt")
    np.savez_compressed(run_dir / "eval_data.npz", x_val=data.x_val, xp_val=data.xp_val, y_val=data.y_val,
                        src_val=data.src_val, neg_heldout=data.neg_heldout, negp_heldout=data.negp_heldout,
                        neg_calib=data.neg_calib, negp_calib=data.negp_calib)
    _snapshot(cfg, run_dir)

    training = {"epochs_run": len(history), "best_epoch": best[1], "best_val_macro_f1": best[0],
                "seconds": time.time() - t0, "history": history}
    m = evaluate_run(run_dir, golden=golden, extra={"training": training}, log=log, allow_golden_skip=allow_golden_skip)
    log(f"[评估] 验证 acc {m['val']['accuracy']:.4f}，macro-F1 {m['val']['macro_f1']:.4f}；"
        f"ECE {m['calibration']['ece_before']:.4f} → {m['calibration']['ece_after']:.4f}；"
        f"OOD AUROC {m['ood']['auroc']:.4f}，FPR95 {m['ood']['fpr95']:.4f}")
    for name, r in (m.get("thresholds") or {}).get("by_rule", {}).items():
        gk = f"，金标已知类通过 {r['golden_known_pass']}/{r['golden_n_known']}" if r.get("golden_n_known") else ""
        log(f"[阈值] {name}：{r['threshold']:.4f}  验证集通过率 {r['id_pass_rate']:.4f}（加 min_conf {r['id_pass_rate_with_min_conf']:.4f}），"
            f"留出集拒识率 {r['heldout_neg_reject_rate']:.4f}（加 min_conf {r['heldout_neg_reject_rate_with_min_conf']:.4f}）{gk}")
    g = m.get("golden")
    if g and "accuracy" in g:
        log(f"[评估] 金标准确率 {g['accuracy']:.4f}（{g['n']} 条；已知类 {g['known_correct']}/{g['n_known']}，怪脸 {g['weird_correct']}/{g['n_weird']}）")
    log(f"[训练] 完成，用时 {time.time() - t0:.0f} 秒；报告 {rel_to_project(run_dir / 'report.md')}")
    return run_dir


def _snapshot(cfg: dict, run_dir: Path) -> None:
    with open(run_dir / "config.yaml", "w", encoding="utf-8", newline="\n") as f:
        yaml.safe_dump(cfg, f, allow_unicode=True, sort_keys=False)
    snap = run_dir / "config_snapshot"
    snap.mkdir(exist_ok=True)
    for key in ("labels", "canonical", "facs", "rig", "label_set"):
        p = cfg.get(key)
        if p:
            src = resolve_path(p)
            if src and src.exists():
                shutil.copyfile(src, snap / src.name)


def main(argv=None) -> int:
    ap = make_parser("python -m exprnet.train", "训练表情分类网络，结束后自动做温度校准、能量阈值标定和评估。")
    ap.add_argument("--config", default=None, help="训练配置 yaml，默认 configs/train.yaml")
    ap.add_argument("--name", required=True, help="本次训练的名字，产物放 artifacts/runs/<名字>/")
    ap.add_argument("--model", choices=["regionformer", "resmlp"], default=None, help="网络结构（覆盖配置）")
    ap.add_argument("--epochs", type=int, default=None, help="最多训练多少个 epoch（覆盖配置）")
    ap.add_argument("--batch-size", type=int, default=None, help="batch 大小（覆盖配置）")
    ap.add_argument("--lr", type=float, default=None, help="学习率（覆盖配置）")
    ap.add_argument("--seed", type=int, default=None, help="随机种子（覆盖配置）")
    ap.add_argument("--rig", default=None, help="绑定配置 yaml；写 none 表示单位绑定（覆盖配置）")
    ap.add_argument("--golden", default=None, help="金标集 json；写 none 表示不评金标（覆盖配置）")
    ap.add_argument("--allow-golden-skip", action="store_true",
                    help="金标的绑定名 / rig_hash 与训练绑定不符时降级为跳过（默认报错退出），报告里会写明跳过")
    ap.add_argument("--label-set", default=None,
                    help="类别集：configs/label_sets/ 下的名字（如 laila_5class）或 yaml 路径；写 none 表示用默认七类（覆盖配置）")
    ap.add_argument("--drop-infeasible-variants", action="store_true",
                    help="按训练绑定剔除做不出来的合成变体（核心 AU 投影后相对残差 ≥ 阈值），剔除清单写进报告与 ckpt")
    ap.add_argument("--infeasible-threshold", type=float, default=None,
                    help="变体剔除的相对残差阈值（覆盖配置，默认 0.9）")
    ap.add_argument("--n-per-class", type=int, default=None, help="合成数据每类样本数（覆盖配置里 synthetic 来源）")
    ap.add_argument("--npz", nargs="*", default=None, help="追加公开集特征缓存 npz 作为训练来源（会使 commercial_use_allowed=false）")
    ap.add_argument("--no-synthetic", action="store_true", help="不用合成数据（只用 --npz）")
    ap.add_argument("--no-cache", action="store_true", help="不读写绑定投影缓存")
    args = ap.parse_args(argv)

    cfg = load_config(resolve_cli_path(args.config) if args.config else None)
    if args.model:
        cfg["model"]["name"] = args.model
    if args.epochs is not None:
        cfg["optim"]["epochs"] = args.epochs
    if args.batch_size is not None:
        cfg["optim"]["batch_size"] = args.batch_size
    if args.lr is not None:
        cfg["optim"]["lr"] = args.lr
    if args.seed is not None:
        cfg["seed"] = args.seed
    if args.rig is not None:
        cfg["rig"] = None if args.rig.lower() == "none" else portable_path(resolve_cli_path(args.rig))
    if args.golden is not None:
        cfg["golden"] = None if args.golden.lower() == "none" else portable_path(resolve_cli_path(args.golden))
    if args.allow_golden_skip:
        cfg["golden_allow_skip"] = True
    if args.label_set is not None:
        cfg["label_set"] = None if args.label_set.lower() == "none" else portable_path(label_set_path(args.label_set))
    if args.drop_infeasible_variants or args.infeasible_threshold is not None:
        fv = cfg.get("infeasible_variants") or {}
        cfg["infeasible_variants"] = fv
        if args.drop_infeasible_variants:
            fv["drop"] = True
        if args.infeasible_threshold is not None:
            fv["rel_residual_threshold"] = args.infeasible_threshold
    srcs = cfg["data"]["sources"]
    if args.no_synthetic:
        srcs = [s for s in srcs if s["kind"] != "synthetic"]
    if args.n_per_class is not None:
        for s in srcs:
            if s["kind"] == "synthetic":
                s["n_per_class"] = args.n_per_class
    for p in args.npz or []:
        srcs.append({"kind": "npz", "path": portable_path(resolve_cli_path(p)), "weight": 1.0, "baseline_norm": True, "split": "auto"})
    if not srcs:
        ap.error("没有任何训练数据来源")
    cfg["data"]["sources"] = srcs
    try:
        train(cfg, args.name, use_cache=not args.no_cache)
    except (GoldenError, FileNotFoundError) as e:
        print(f"[错误] {e}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
