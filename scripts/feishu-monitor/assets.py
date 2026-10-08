# 执行载体：已运行的 feishu-monitor.timer；仅处理服务器配置中明确绑定的交付文件。
# 状态锚点：state.json 的 asset_commit 与远端 codex/feishu-sync 分支头。
# 退场条件：移除 asset_sync 配置或撤销服务器仓库密钥。
# 新建原因：复用监测快照与 CLI 下载；资产白名单发布与正文差异提醒职责不同。
import hashlib
import pathlib
import re
import subprocess
import tempfile
import zipfile

BRANCH = "codex/feishu-sync"
EXTENSIONS = {".png", ".jpg", ".jpeg", ".tga", ".webp", ".fbx", ".obj", ".mtl", ".blend", ".wav", ".ogg", ".mp3", ".meta"}

def checked_target(name):
    path = pathlib.PurePosixPath(name)
    if (not name.startswith(("Assets/_Project/Art/", "Assets/_Project/Audio/"))
            or any(c in name for c in "\\\r\n\t:") or ".." in path.parts or str(path) != name
            or path.suffix.lower() not in EXTENSIONS):
        raise RuntimeError("资产目标不在允许范围")
    return path

def download(binding, invoke):
    token = binding["token"]
    if not re.fullmatch(r"[A-Za-z0-9]+", token):
        raise RuntimeError("交付 token 非法")
    with tempfile.TemporaryDirectory() as directory:
        receipt = invoke(["docs", "+media-download", "--token", token,
                          "--output", "./asset", "--as", "user"], cwd=directory)
        path = pathlib.Path(receipt["saved_path"]).resolve()
        if pathlib.Path(directory).resolve() not in path.parents:
            raise RuntimeError("交付文件下载路径越界")
        if binding.get("member"):
            with zipfile.ZipFile(str(path)) as archive:
                info = archive.getinfo(binding["member"])
                if info.file_size > 50 * 1024 * 1024:
                    raise RuntimeError("单个交付文件超过 50MiB，需要核对大素材接入")
                return archive.read(info)
        if path.stat().st_size > 50 * 1024 * 1024:
            raise RuntimeError("单个交付文件超过 50MiB，需要核对大素材接入")
        return path.read_bytes()

def git(directory, *args):
    result = subprocess.run(["git", "-C", str(directory)] + list(args), stdout=subprocess.PIPE,
                            stderr=subprocess.PIPE, universal_newlines=True, timeout=120)
    if result.returncode:
        raise RuntimeError("Git {} 失败（退出码 {}）".format(args[0], result.returncode))
    return result.stdout.strip()

def prepare(config, home, include_documents=False):
    remote = config["repository"]
    if not re.fullmatch(r"https://github\.com/[A-Za-z0-9-]+/21Days\.git", remote):
        raise RuntimeError("仓库配置不符")
    directory = home / "asset-repo"
    if not (directory / ".git").exists():
        if directory.exists() and any(directory.iterdir()):
            raise RuntimeError("资产同步目录非空")
        with tempfile.TemporaryDirectory(dir=str(home)) as temporary:
            candidate = pathlib.Path(temporary) / "repo"
            candidate.mkdir()
            git(candidate, "init")
            git(candidate, "remote", "add", "origin", remote)
            git(candidate, "sparse-checkout", "init", "--cone")
            git(candidate, "sparse-checkout", "set", "Assets/_Project/Art", "Assets/_Project/Audio")
            branch = BRANCH if git(candidate, "ls-remote", "--heads", "origin", "refs/heads/" + BRANCH) else "main"
            git(candidate, "fetch", "--depth=1", "--filter=blob:none", "origin", branch)
            git(candidate, "checkout", "-b", BRANCH, "FETCH_HEAD")
            candidate.rename(str(directory))
    if (git(directory, "remote", "get-url", "origin") != remote
            or git(directory, "branch", "--show-current") != BRANCH):
        raise RuntimeError("仓库或分支身份不符")
    if git(directory, "status", "--porcelain"):
        raise RuntimeError("资产同步目录有未提交变更，保留现场")
    if include_documents:
        git(directory, "sparse-checkout", "set", "Assets/_Project/Art", "Assets/_Project/Audio",
            "docs/design/spotlight/auto-sync")
    git(directory, "remote", "set-url", "--push", "origin", remote.replace("https://github.com/", "git@github.com:"))
    git(directory, "config", "core.sshCommand", config["ssh_command"])
    git(directory, "config", "user.name", config["author_name"])
    git(directory, "config", "user.email", config["author_email"])
    if git(directory, "ls-remote", "--heads", "origin", "refs/heads/" + BRANCH):
        # 更新已有工作目录时保留祖先链，避免把新分支头截成无关历史。
        git(directory, "fetch", "--filter=blob:none", "origin", BRANCH)
        git(directory, "merge", "--ff-only", "FETCH_HEAD")
    return directory

def validate_files(directory, files):
    for name, data in files.items():
        checked_target(name)
        path = directory / name
        if directory.resolve() not in path.resolve().parents or path.is_symlink():
            raise RuntimeError("资产路径越界或为符号链接")
        if name.endswith(".meta"):
            if not re.search(rb"(?m)^guid: [0-9a-f]{32}\s*$", data):
                raise RuntimeError("交付 meta 缺少合法 GUID")
            if path.exists() and path.read_bytes() != data:
                raise RuntimeError("禁止替换已有 meta")
        else:
            suffix = pathlib.PurePosixPath(name).suffix.lower()
            signatures = {".png": b"\x89PNG\r\n\x1a\n", ".jpg": b"\xff\xd8\xff",
                          ".jpeg": b"\xff\xd8\xff", ".blend": b"BLENDER", ".ogg": b"OggS"}
            valid = bool(data)
            if suffix in signatures:
                valid = data.startswith(signatures[suffix])
            elif suffix in (".wav", ".webp"):
                valid = data[:4] == b"RIFF" and data[8:12] == (b"WAVE" if suffix == ".wav" else b"WEBP")
            elif suffix == ".mp3":
                valid = data.startswith(b"ID3") or (len(data) > 1 and data[0] == 255 and data[1] & 224 == 224)
            elif suffix == ".fbx":
                valid = data.startswith(b"Kaydara FBX Binary") or bool(re.search(rb"(?m)^(; FBX|FBXHeaderExtension:)", data[:4096]))
            elif suffix == ".obj":
                valid = bool(re.search(rb"(?m)^v\s+[-+0-9.]", data))
            elif suffix == ".mtl":
                valid = bool(re.search(rb"(?m)^newmtl\s+\S", data))
            elif suffix == ".tga":
                valid = len(data) >= 18 and data[2] in (2, 3, 10, 11) and any(data[12:14]) and any(data[14:16])
            if not valid:
                raise RuntimeError("文件内容与绑定的素材格式不符")
            if not (directory / (name + ".meta")).exists() and name + ".meta" not in files:
                raise RuntimeError("新素材缺少 Unity 生成的 meta，暂不提交")
        # 新目录的 GUID 也由 Unity 生成，不能只核对素材本身。
        for parent in path.parents:
            if parent == directory / "Assets" or parent == directory:
                break
            meta = str(parent.relative_to(directory)).replace("\\", "/") + ".meta"
            if not (directory / meta).exists() and meta not in files:
                raise RuntimeError("新素材目录缺少 Unity 生成的 meta，暂不提交")

def publish(config, home, previous, invoke):
    bindings = config.get("bindings", [])
    if not bindings:
        return previous, None
    files = {}
    for binding in bindings:
        target = binding["target"]
        checked_target(target)
        if target in files:
            raise RuntimeError("重复资产目标")
        files[target] = download(binding, invoke)
    hashes = {name: hashlib.sha256(data).hexdigest() for name, data in files.items()}
    if hashes == previous:
        return previous, None
    directory = prepare(config, home)
    validate_files(directory, files)
    for name, data in files.items():
        path = directory / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
    git(directory, "add", "--", *sorted(files))
    staged = git(directory, "-c", "core.quotePath=false", "diff", "--cached", "--name-only").splitlines()
    if any(name not in files for name in staged):
        raise RuntimeError("索引包含非交付文件")
    if staged:
        git(directory, "commit", "-m", "feat(art): 更新已绑定的飞书交付素材", "--", *sorted(files))
    # 非强制推送；分支并发修改会拒绝，保留提交等待处理。
    git(directory, "push", "origin", "HEAD:refs/heads/" + BRANCH)
    sha = git(directory, "rev-parse", "HEAD")
    if not git(directory, "ls-remote", "--heads", "origin", "refs/heads/" + BRANCH).startswith(sha + "\t"):
        raise RuntimeError("远端资产分支校验失败")
    return hashes, sha
