# -*- coding: utf-8 -*-
"""subagent 模型审计 —— 查某次会话里每个 subagent 的派单档位与实际跑的模型。

验的是 `.claude/rules/model-routing.md` 那三条硬规则有没有真守住：
每次派单是否显式传了 model、有没有 subagent 被派成 fable、档位与实际模型对不对得上。
主窗口自称派了 opus 不算数，这里读的是落盘记录。

数据来源（Claude Code 自己写的，不是本工程产物）：
    <会话目录>/<会话id>/subagents/agent-*.meta.json   → model 字段 = 派单时传的档位
    <会话目录>/<会话id>/subagents/agent-*.jsonl       → assistant 消息的 model = 实际调用的模型 ID

用法：
    python .claude/skills/evolution/agent_models.py             # 最近一次有派单的会话
    python .claude/skills/evolution/agent_models.py 2198fc69    # 指定会话 id 前缀
    python .claude/skills/evolution/agent_models.py --all       # 本工程所有会话合并汇总
"""
import sys, os, json, glob, re, collections

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

# 脚本在 <工程根>/.claude/skills/evolution/，上溯三级得工程根
PROJECT_ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))


def sessions_root():
    """Claude Code 按「工程绝对路径的非字母数字字符换成 -」给会话目录命名。"""
    base = os.path.join(os.path.expanduser("~"), ".claude", "projects")
    slug = re.sub(r"[^A-Za-z0-9]", "-", PROJECT_ROOT)
    path = os.path.join(base, slug)
    if os.path.isdir(path):
        return path
    # 盘符大小写可能对不上，兜底做一次大小写不敏感匹配
    if os.path.isdir(base):
        for name in os.listdir(base):
            if name.lower() == slug.lower():
                return os.path.join(base, name)
    return path


def collect(session_dir):
    """读一个会话目录下所有 subagent 的派单档位与实际模型。"""
    rows = []
    for meta_path in sorted(glob.glob(os.path.join(session_dir, "subagents", "*.meta.json"))):
        try:
            meta = json.load(open(meta_path, encoding="utf-8"))
        except (OSError, ValueError):
            continue
        actual = collections.Counter()
        jsonl = meta_path[: -len(".meta.json")] + ".jsonl"
        if os.path.exists(jsonl):
            with open(jsonl, encoding="utf-8") as fh:
                for line in fh:
                    try:
                        rec = json.loads(line)
                    except ValueError:
                        continue
                    if rec.get("type") == "assistant":
                        actual[(rec.get("message") or {}).get("model")] += 1
        rows.append({
            "agent": meta.get("agentType") or "(未知)",
            "asked": meta.get("model"),
            "actual": "/".join(k for k in actual if k) or "(无回合)",
            "depth": meta.get("spawnDepth"),
            "desc": (meta.get("description") or "")[:30],
        })
    return rows


def audit(rows):
    """按 model-routing.md 的硬规则挑违规。"""
    bad = []
    for r in rows:
        if r["asked"] is None:
            bad.append(("派单没传 model", r))
        elif "fable" in str(r["asked"]).lower() or "fable" in r["actual"].lower():
            bad.append(("subagent 被派成 fable", r))
        elif r["actual"] not in ("(无回合)", "") and str(r["asked"]) not in r["actual"]:
            bad.append(("档位与实际模型对不上", r))
    return bad


def main():
    arg = sys.argv[1] if len(sys.argv) > 1 else None
    root = sessions_root()
    if not os.path.isdir(root):
        print("没找到本工程的会话目录：%s" % root)
        return 1

    dirs = [d for d in glob.glob(os.path.join(root, "*"))
            if os.path.isdir(d) and os.path.isdir(os.path.join(d, "subagents"))]
    if not dirs:
        print("本工程还没有 subagent 派单记录")
        return 0

    if arg == "--all":
        picked = sorted(dirs, key=os.path.getmtime)
        print("范围：本工程全部 %d 个有派单的会话" % len(picked))
    else:
        if arg:
            dirs = [d for d in dirs if os.path.basename(d).startswith(arg)]
            if not dirs:
                print("没有 id 以 %s 开头且含派单记录的会话" % arg)
                return 1
        picked = [max(dirs, key=os.path.getmtime)]
        print("会话：%s" % os.path.basename(picked[0]))

    rows = [r for d in picked for r in collect(d)]
    if not rows:
        print("该会话没有 subagent 派单")
        return 0

    print("\n%-18s%-12s%-20s%s" % ("agent 类型", "派单 model", "实际模型", "描述"))
    for r in sorted(rows, key=lambda x: (str(x["asked"]), x["agent"])):
        print("%-18s%-12s%-20s%s" % (r["agent"], r["asked"], r["actual"], r["desc"]))

    tally = collections.Counter((r["asked"], r["actual"]) for r in rows)
    print("\n汇总（派单档位 → 实际模型），共 %d 次派单：" % len(rows))
    for (asked, actual), n in tally.most_common():
        print("  %s → %s：%d 次" % (asked, actual, n))

    bad = audit(rows)
    if bad:
        print("\n违规 %d 处（对照 .claude/rules/model-routing.md）：" % len(bad))
        for why, r in bad:
            print("  [%s] %s / %s → %s：%s" % (why, r["agent"], r["asked"], r["actual"], r["desc"]))
        return 1
    print("\n派单合规：每单都显式传了 model，没有 fable，档位与实际模型一致。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
