# 执行载体：feishu-monitor.timer；启用 document_sync 后随监测运行，部署状态需实测。
# 状态锚点：state.json 的 document_commit 与远端 codex/feishu-sync 分支头。
# 退场条件：移除 document_sync 配置；保留最后一次成功快照。
# 新建原因：复用 assets.prepare/git；其素材格式与 meta 校验不适用于新增策划附件。
import hashlib
import pathlib
import re
import tempfile
import assets

ROOT = "docs/design/spotlight/auto-sync/"
EXTENSIONS = {".docx", ".pdf"}

def build_files(snapshots, source_url, reader, invoke, baseline_tokens=()):
    files = {}
    links = []
    for key, item in sorted(snapshots.items()):
        # 正文、内嵌表、画板和参考图只监测；素材仍由资产绑定处理。
        if item.get("kind") != "media" or pathlib.PurePath(item["label"]).suffix.lower() not in EXTENSIONS:
            continue
        kind, token = key.split(":", 1)
        if kind != "media" or not re.fullmatch(r"[A-Za-z0-9]+", token):
            raise RuntimeError("策划资源标识不合法")
        if token in baseline_tokens:
            continue
        name = key.replace(":", "_")
        with tempfile.TemporaryDirectory() as temporary:
            receipt = invoke(["docs", "+media-download", "--token", token,
                              "--output", "./asset", "--as", reader], cwd=temporary)
            path = pathlib.Path(receipt["saved_path"]).resolve()
            if pathlib.Path(temporary).resolve() not in path.parents:
                raise RuntimeError("策划附件下载路径越界")
            if (path.suffix.lower() != pathlib.PurePath(item["label"]).suffix.lower()
                    or path.stat().st_size > 50 * 1024 * 1024):
                raise RuntimeError("策划附件格式不符或超过 50MiB")
            data = path.read_bytes()
            if hashlib.sha256(data).hexdigest() != item["value"]:
                raise RuntimeError("策划附件在读取期间发生变化，稍后重试")
            name += path.suffix.lower()
        files[ROOT + name] = data
        label = item["label"].replace("[", "\\[").replace("]", "\\]").replace("\n", " ").replace("\r", " ")
        links.append("- [{}]({})".format(label, name))
    if files:
        header = "# 聚光灯 · 新增策划文档\n\n仅同步初始基线之后新增的策划附件及其后续更新。飞书正文、内嵌表格、画板、参考图和原有人工转写不在写入范围；素材由独立资产映射处理。\n\n{}\n".format("\n".join(links))
        files[ROOT + "README.md"] = header.encode("utf-8")
    return files

def publish(config, home, previous, snapshots, source_url, reader, invoke):
    baseline_tokens = config.get("baseline_tokens")
    if not isinstance(baseline_tokens, list) or any(not isinstance(t, str) or not re.fullmatch(r"[A-Za-z0-9]+", t) for t in baseline_tokens):
        raise RuntimeError("初始策划附件基线未配置，禁止上传存量文档")
    files = build_files(snapshots, source_url, reader, invoke, baseline_tokens)
    if not files:
        return previous, None
    hashes = {name: hashlib.sha256(data).hexdigest() for name, data in files.items()}
    if hashes == previous:
        return previous, None
    directory = assets.prepare(config, home, include_documents=True)
    for name in files:
        path = directory / name
        if directory.resolve() not in path.resolve().parents or path.is_symlink():
            raise RuntimeError("策划同步路径越界或为符号链接")
    for name, data in files.items():
        path = directory / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
    assets.git(directory, "add", "--", *sorted(files))
    staged = assets.git(directory, "-c", "core.quotePath=false", "diff", "--cached", "--name-only").splitlines()
    if any(name not in files for name in staged):
        raise RuntimeError("索引包含非策划同步文件")
    if staged:
        assets.git(directory, "commit", "-m", "docs(design): 同步新增策划附件", "--", *sorted(files))
    assets.git(directory, "push", "origin", "HEAD:refs/heads/" + assets.BRANCH)
    sha = assets.git(directory, "rev-parse", "HEAD")
    if not assets.git(directory, "ls-remote", "--heads", "origin", "refs/heads/" + assets.BRANCH).startswith(sha + "\t"):
        raise RuntimeError("远端策划分支校验失败")
    return hashes, sha
