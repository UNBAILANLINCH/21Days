#!/usr/bin/env python3
# 执行载体：服务器 systemd feishu-monitor.timer；部署后实测启用。
# 状态锚点：systemctl list-timers feishu-monitor.timer 与 state.json 的 checked_at。
# 退场条件：停止监测时禁用 timer；凭据撤销或连续失败时报告，不自动改项目资产。
# 新建原因：复用飞书 CLI 读取与发送；现有资产映射不负责差异比较、失败重试和状态保存。
import concurrent.futures
import datetime
import difflib
import hashlib
import json
import pathlib
import re
import subprocess
import sys
import tempfile
import time
import assets
import documents

def stamp():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()

def digest(value):
    raw = json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(raw.encode("utf-8")).hexdigest()

def invoke(args, cwd=None):
    for attempt in range(2):
        try:
            result = subprocess.run(["lark-cli"] + args, cwd=cwd, stdout=subprocess.PIPE,
                                    stderr=subprocess.PIPE, universal_newlines=True, timeout=65)
            data = json.loads(result.stdout)
            if result.returncode == 0 and data.get("ok") is True:
                return data["data"]
            error = data.get("error", {})
            if attempt == 0 and error.get("type") == "network":
                time.sleep(2)
                continue
            raise RuntimeError("{}: {}".format(error.get("type", "CLI"),
                                               error.get("subtype", "failed")))
        except (subprocess.TimeoutExpired, json.JSONDecodeError):
            if attempt:
                raise RuntimeError("CLI 请求超时或返回无效")
            time.sleep(2)

def sheet_value(data):
    if data.get("has_more") or any(r.get("truncated") for r in data["ranges"]):
        raise RuntimeError("表格读取被截断")
    cells = {}
    for region in data["ranges"]:
        rows, cols = region["row_indices"], region["col_indices"]
        for i, row in enumerate(region["cells"]):
            if i >= len(rows) or len(row) > len(cols):
                raise RuntimeError("表格坐标与数据不符")
            for j, cell in enumerate(row):
                if cell:
                    cells[cols[j] + str(rows[i])] = cell
    return cells

def board_value(data):
    nodes = data.get("nodes")
    if not isinstance(nodes, list) or any("id" not in n for n in nodes):
        raise RuntimeError("画板节点不完整")
    if len({n["id"] for n in nodes}) != len(nodes):
        raise RuntimeError("画板节点 ID 重复")
    return {n["id"]: n for n in nodes}

def column_name(number):
    text = ""
    while number:
        number, rest = divmod(number - 1, 26)
        text = chr(65 + rest) + text
    return text

def collect(config):
    reader = config.get("read_as", "bot")
    document = invoke(["docs", "+fetch", "--doc", config["document_url"],
                       "--as", reader, "--doc-format", "markdown"])["document"]
    if not document.get("content") or document.get("document_id") != config["document_id"]:
        raise RuntimeError("正文为空或文档身份不符")
    content = document["content"]
    snapshots = {"document": {"kind": "document", "label": "文档正文", "value": content}}
    jobs = []
    errors, known = [], ["document"]
    groups = {"ddVEoY": "主线角色", "pKSkSs": "玩法角色", "iIfihK": "模型",
              "4sjpvZ": "UI", "hGHTpX": "小游戏界面"}
    sheets = re.findall(r'<sheet sheet-id="([A-Za-z0-9]+)" token="([A-Za-z0-9]+)"', content)
    workbooks = {}
    for sheet_id, token in sheets:
        known.append("sheet:" + token + ":" + sheet_id)
        if token not in workbooks:
            try:
                workbooks[token] = invoke(["sheets", "+workbook-info", "--spreadsheet-token",
                                           token, "--as", reader])
            except Exception as error:
                workbooks[token] = {"error": str(error)}
        if "error" in workbooks[token]:
            errors.append(groups.get(sheet_id, sheet_id) + ": " + workbooks[token]["error"])
            continue
        meta = next((s for s in workbooks[token]["sheets"] if s["sheet_id"] == sheet_id), None)
        if not meta or meta.get("resource_type") != "sheet":
            errors.append(groups.get(sheet_id, sheet_id) + ": 引用子表未找到或类型不符")
            continue
        area = "A1:{}{}".format(column_name(meta["column_count"]), meta["row_count"])
        jobs.append(("sheet:" + token + ":" + sheet_id, "sheet", groups.get(sheet_id, sheet_id),
                     ["sheets", "+cells-get", "--spreadsheet-token", token, "--sheet-id", sheet_id,
                      "--range", area, "--include", "value,formula,style,comment", "--as", reader]))
    for token in re.findall(r'<whiteboard token="([A-Za-z0-9]+)"', content):
        known.append("board:" + token)
        jobs.append(("board:" + token, "board", "序章画板",
                     ["whiteboard", "+export", "--whiteboard-token", token,
                      "--output-type", "raw", "--as", reader]))
    media = dict((token, name) for name, token in re.findall(
        r'<source name="([^"]+)" mime="[^"]+" size="[^"]+" token="([A-Za-z0-9]+)"', content))
    for token in re.findall(r'https://feishu.cn/file/([A-Za-z0-9]+)', content):
        media.setdefault(token, "参考图片 " + token[:6])
    for token in re.findall(r'<img\b[^>]*\btoken="([A-Za-z0-9]+)"', content):
        media.setdefault(token, "正文图片 " + token[:6])
    for token, name in media.items():
        known.append("media:" + token)
        jobs.append(("media:" + token, "media", name, token))
    if len(jobs) > 100:
        raise RuntimeError("资源数量超过监测上限，需要调整覆盖范围")
    def read(job):
        key, kind, label, args = job
        if kind == "sheet":
            value = sheet_value(invoke(args))
        elif kind == "board":
            value = board_value(invoke(args))
        else:
            with tempfile.TemporaryDirectory() as directory:
                receipt = invoke(["docs", "+media-download", "--token", args,
                                  "--output", "./asset", "--as", reader], cwd=directory)
                path = pathlib.Path(receipt["saved_path"]).resolve()
                if pathlib.Path(directory).resolve() not in path.parents:
                    raise RuntimeError("下载返回了目录外路径")
                value = hashlib.sha256(path.read_bytes()).hexdigest()
        return key, {"kind": kind, "label": label, "value": value}
    with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
        pending = {pool.submit(read, job): job for job in jobs}
        for future in concurrent.futures.as_completed(pending):
            try:
                key, value = future.result()
                snapshots[key] = value
            except Exception as error:
                errors.append(pending[future][2] + ": " + str(error))
    return snapshots, sorted(errors), document.get("revision_id"), known

def change_lines(old, new, known=()):
    lines = []
    for key in sorted(set(old) | set(new)):
        before, after = old.get(key), new.get(key)
        if before == after:
            continue
        if before is None and key in known:
            continue
        item = after or before
        label = item["label"]
        if before is None:
            lines.append("新增：" + label)
        elif after is None:
            lines.append("删除引用：" + label + "（项目资产不会自动删除）")
        elif item["kind"] == "document":
            a, b = document_lines(before["value"]), document_lines(after["value"])
            edits = [op for op in difflib.SequenceMatcher(None, a, b, autojunk=False).get_opcodes()
                     if op[0] != "equal"]
            if not edits:
                continue
            lines.append("修改：文档正文，{} 处".format(len(edits)))
            for tag, i, end_i, j, end_j in edits[:8]:
                heading = next((line.lstrip("# ") for line in reversed(b[:j] or a[:i])
                                if line.startswith("#")), "正文")
                left, right = change_text("\n".join(a[i:end_i]), "\n".join(b[j:end_j]))
                lines.append("• {}\n  之前：{}\n  现在：{}".format(heading, left, right))
            if len(edits) > 8:
                lines.append("另有 {} 处修改，请查看原文".format(len(edits) - 8))
        elif item["kind"] == "sheet":
            a, b = before["value"], after["value"]
            positions = [p for p in sorted(set(a) | set(b), key=lambda p: (int(re.search(r"\d+$", p)[0]), p))
                         if a.get(p) != b.get(p)]
            lines.append("修改：{}，{} 个单元格".format(label, len(positions)))
            for position in positions[:12]:
                col, row = re.fullmatch(r"([A-Z]+)(\d+)", position).groups()
                name = compact((b.get("A" + row) or a.get("A" + row) or {}).get("value"))
                header = (b.get(col + "1") or a.get(col + "1") or {}).get("value")
                if header and label in ("主线角色", "玩法角色"):
                    header = "附件" if "附件" in str(header) else str(header)
                else:
                    header = position
                lines.append("• {} / {}（{}）".format(name, header, position))
                fields = list(changed_fields(a.get(position) or {}, b.get(position) or {}))
                for field, left, right in fields[:6]:
                    left, right = change_text(left, right)
                    lines.append("  {}：{} → {}".format(field_label(field), left, right))
                if len(fields) > 6:
                    lines.append("  另有 {} 项属性变化，请查看原表".format(len(fields) - 6))
            if len(positions) > 12:
                lines.append("另有 {} 个单元格变化，请查看原表".format(len(positions) - 12))
        elif item["kind"] == "board":
            a, b = before["value"], after["value"]
            ids = [i for i in sorted(set(a) | set(b)) if a.get(i) != b.get(i)]
            lines.append("修改：{}，{} 个节点".format(label, len(ids)))
            for node_id in ids[:8]:
                lines.append("• 节点 " + node_id)
                fields = list(changed_fields(a.get(node_id) or {}, b.get(node_id) or {}))
                for field, left, right in fields[:4]:
                    left, right = change_text(left, right)
                    lines.append("  {}：{} → {}".format(field_label(field), left, right))
                if len(fields) > 4:
                    lines.append("  其余节点属性请查看原画板")
            if len(ids) > 8:
                lines.append("其余 {} 个节点请查看原画板".format(len(ids) - 8))
        elif before["value"] == after["value"]:
            lines.append("引用名称修改：{} → {}".format(before["label"], after["label"]))
        else:
            lines.append("文件内容更新：" + label + "（需查看文件确认具体改动）")
    return lines

def document_lines(text):
    lines, fence = [], None
    for line in text.splitlines():
        marker = re.match(r"^ {0,3}(`{3,}|~{3,})", line)
        if marker:
            token = marker[1]
            if fence is None:
                fence = token
            elif token[0] == fence[0] and len(token) >= len(fence) and line.strip() == token:
                fence = None
        # 只忽略重复空行；保留段落边界及代码块内的原始空行。
        if fence is None and not line.strip() and lines and not lines[-1].strip():
            continue
        lines.append(line)
    return lines

def changed_fields(before, after, prefix=""):
    if before == after:
        return
    if isinstance(before, dict) and isinstance(after, dict):
        priority = {key: i for i, key in enumerate(("value", "rich_text", "formula", "comment"))}
        for key in sorted(set(before) | set(after), key=lambda k: (priority.get(k, 4), k)):
            yield from changed_fields(before.get(key), after.get(key), prefix + key + ".")
    else:
        yield prefix.rstrip("."), before, after

def field_label(field):
    names = {"value": "内容", "rich_text": "富文本/附件", "formula": "公式", "comment": "批注",
             "cell_background_color": "底色", "background_color": "底色", "border_styles": "边框",
             "style": "样式", "font_color": "文字颜色", "text": "文字", "color": "颜色"}
    return "/".join(names.get(part, part) for part in field.split("."))

def compact(value):
    if value is None or value == "":
        return "（空）"
    if isinstance(value, str):
        return value
    if isinstance(value, list):
        return "；".join(compact(v) for v in value) or "（空）"
    if isinstance(value, dict) and value.get("type") == "attachment":
        size = value.get("file_size")
        return "附件《{}》{}".format(value.get("text", "未命名"),
                                    "（{} 字节）".format(size) if size is not None else "")
    return json.dumps(value, ensure_ascii=False, sort_keys=True)

def change_text(before, after):
    left, right = compact(before), compact(after)
    if left == right and before != after:
        right += "（显示内容相同，引用或属性已变化）"
    index = next((i for i, pair in enumerate(zip(left, right)) if pair[0] != pair[1]),
                 min(len(left), len(right)))
    start = max(0, index - 60) if max(len(left), len(right)) > 320 else 0
    def excerpt(text):
        offset = min(start, max(0, len(text) - 60))
        return ("…" if offset else "") + text[offset:offset + 320] + ("…" if len(text) > offset + 320 else "")
    return excerpt(left), excerpt(right)

def send(config, text):
    # 文档差异作为文本发送，避免 CLI 自动下载并上传 Markdown 内的图片。
    invoke(["im", "+messages-send", "--as", "bot", "--user-id", config["recipient"],
            "--text", text, "--idempotency-key", "fm-" + digest(text)[:40]])

def save(path, data):
    temporary = path.with_suffix(".tmp")
    temporary.write_text(json.dumps(data, ensure_ascii=False, sort_keys=True), encoding="utf-8")
    temporary.chmod(0o600)
    temporary.replace(str(path))

def run(config, state_path):
    state = json.loads(state_path.read_text(encoding="utf-8")) if state_path.exists() else {}
    try:
        snapshots, errors, revision, known = collect(config)
    except Exception as error:
        snapshots, errors, revision, known = {}, ["正文读取失败: " + str(error)], None, state.get("known_keys", [])
    old = state.get("snapshots", {})
    failed_labels = {e.split(": ", 1)[0] for e in errors}
    merged = dict(snapshots)
    if not snapshots:
        merged = dict(old)
    else:
        for key, item in old.items():
            if item["label"] in failed_labels:
                merged[key] = item
    changes = change_lines(old, merged, state.get("known_keys", [])) if state.get("initialized") else []
    asset_hashes, asset_commit = state.get("asset_hashes", {}), None
    document_hashes, document_commit = state.get("document_hashes", {}), None
    if config.get("document_sync"):
        try:
            if errors:
                raise RuntimeError("资源读取不完整，保留上次策划快照")
            document_hashes, document_commit = documents.publish(
                config["document_sync"], state_path.parent, document_hashes, snapshots,
                config["document_url"], config.get("read_as", "bot"), invoke)
        except Exception as error:
            errors.append("策划同步: " + str(error))
            errors.sort()
    if config.get("asset_sync"):
        try:
            asset_hashes, asset_commit = assets.publish(config["asset_sync"], state_path.parent,
                                                       asset_hashes, invoke)
        except Exception as error:
            errors.append("资产同步: " + str(error))
            errors.sort()
    message = ""
    if changes:
        message = "聚光灯文档有更新（正文版本 {}）\n{}\n\n{}".format(
            revision, config["document_url"], "\n".join(changes))
        if not config.get("document_sync"):
            message += "\n\n策划自动同步未配置，需求变动只提醒。"
        if not document_commit and not asset_commit:
            if errors:
                message += "\n\nGit：同步未完成，详情见故障说明。"
            else:
                message += "\n\nGit：本轮没有文件推送，不触发新 PR。交付包尚需核对文件绑定与 Unity 导入；新素材的 .meta 由项目生成，无需美术提供。"
    if document_commit:
        message += "\n\n新增策划附件已推送到 {}：\n{}/commit/{}\n附件见 docs/design/spotlight/auto-sync/；审核后再合并。".format(
            assets.BRANCH, config["document_sync"]["repository"][:-4], document_commit)
    if asset_commit:
        message += "\n\n素材已推送到 {}：\n{}/commit/{}\n审核后再合并到主分支。".format(
            assets.BRANCH, config["asset_sync"]["repository"][:-4], asset_commit)
    error_id = digest(errors) if errors else None
    if errors and error_id != state.get("notified_error"):
        message += "\n\n监测覆盖不足：\n" + "\n".join(errors)
    if not errors and not state.get("initialized"):
        message = "服务器监测已启用：已建立正文和 {} 项内嵌资源基线，每30分钟检查，有变动才提醒。\n{}\n表格颜色是否返回取决于接口；未返回颜色时不能判断白色免做。".format(
            len(snapshots) - 1, config["document_url"]) + message
    elif not errors and state.get("notified_error"):
        message += "\n\n此前监测故障已恢复。"
    if message.strip():
        send(config, message if len(message) <= 12000 else message[:11800] + "\n摘要超长，其余变化请查看源文档。")
    state.update(snapshots=merged, checked_at=stamp(), revision=revision,
                 initialized=state.get("initialized", False) or bool(snapshots), known_keys=known,
                 notified_error=error_id, errors=errors)
    if config.get("asset_sync"):
        state.update(asset_hashes=asset_hashes, asset_commit=asset_commit or state.get("asset_commit"))
    if config.get("document_sync"):
        state.update(document_hashes=document_hashes,
                     document_commit=document_commit or state.get("document_commit"))
    save(state_path, state)
    print("checked_at={} resources={} changes={} errors={}".format(
        state["checked_at"], len(merged), len(changes), len(errors)))
    return bool(errors)

if __name__ == "__main__":
    config_path = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else "config.json")
    config = json.loads(config_path.read_text(encoding="utf-8"))
    sys.exit(1 if run(config, config_path.with_name("state.json")) else 0)
