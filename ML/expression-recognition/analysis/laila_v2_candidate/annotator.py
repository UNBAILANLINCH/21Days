"""本地dev辅助标注器；原CLI只准备空表，不能续标，故新增交互载体。
用户双击launch_annotator.cmd启动；5秒锚点为页面保存状态/终端本机URL。
页面退出按钮或Ctrl+C关闭服务并释放锁；无Unity、模型、上传或后台定时任务。
"""
from __future__ import annotations

import csv
import hashlib
import io
import json
import os
import re
import secrets
import shutil
import time
import webbrowser
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path
from urllib.parse import parse_qs, urlparse

ROOT = Path(__file__).resolve().parents[2]
PACKET = ROOT / 'artifacts/laila_v2_candidate/unity-round-20261003/blind-packet'
OUTPUT = ROOT / 'artifacts/laila_v2_candidate/dev-annotations'
LABELS = ['neutral', 'happy', 'sad', 'surprise', 'fear', 'disgust', 'angry', 'ambiguous']
CLARITY = ['', 'clear', 'uncertain']


def now():
    return datetime.now(timezone.utc).isoformat()


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def atomic(path, data):
    temp = path.with_suffix(path.suffix + '.tmp')
    with temp.open('w', encoding='utf-8', newline='') as stream:
        json.dump(data, stream, ensure_ascii=False, indent=2, allow_nan=False)
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(temp, path)


class Dataset:
    def __init__(self, packet=PACKET):
        self.packet = Path(packet)
        manifest = json.loads((self.packet / 'steward-manifest.json').read_text(encoding='utf-8'))
        self.entries = manifest['entries']
        self.ids = [e['id'] for e in self.entries]
        if not self.ids or len(set(self.ids)) != len(self.ids):
            raise ValueError('空数据或重复ID')
        groups = set()
        for e in self.entries:
            if e['split'] != 'dev' or not e.get('group') or not re.fullmatch('[0-9a-f]{32}', e['id']):
                raise ValueError('只接受有分组的匿名dev样本，禁止混入test')
            groups.add(e['group'])
            image = self.image(e['id'])
            if not image.is_file() or digest(image) != e['image_sha256']:
                raise ValueError('图片缺失或版本不符：' + e['id'])
            if self.packet.resolve() == PACKET.resolve():
                capture = ROOT / 'data/laila-captures-v2/dev' / (e['id'] + '.json')
                if not capture.is_file() or digest(capture) != e['capture_sha256']:
                    raise ValueError('原始dev快照缺失或版本不符：' + e['id'])
                raw = json.loads(capture.read_text(encoding='utf-8'))
                if any(raw.get(key) != e[key] for key in ('id', 'group', 'split', 'rig_hash', 'fbx_sha256')):
                    raise ValueError('原始快照分组/版本不符：' + e['id'])
        if {p.stem for p in (self.packet / 'annotators/images').glob('*.png')} != set(self.ids):
            raise ValueError('图片与清单不匹配')
        for n in range(1, 4):
            with (self.packet / f'annotators/annotator_{n}.csv').open(encoding='utf-8-sig', newline='') as stream:
                rows = list(csv.DictReader(stream))
            if [r['id'] for r in rows] != self.ids or any(r['label'] for r in rows):
                raise ValueError('起步表顺序不符或不是空盲标表')
        self.group_count = len(groups)
        self.version = hashlib.sha256(json.dumps(self.entries, sort_keys=True, separators=(',', ':')).encode()).hexdigest()

    def image(self, sample_id):
        if not re.fullmatch('[0-9a-f]{32}', sample_id):
            raise ValueError('非法ID')
        return self.packet / 'annotators/images' / (sample_id + '.png')


class Store:
    def __init__(self, dataset, output=OUTPUT):
        self.dataset, self.output = dataset, Path(output)
        self.output.mkdir(parents=True, exist_ok=True)

    def path(self, annotator):
        if not isinstance(annotator, str) or not annotator.strip() or len(annotator) > 80:
            raise ValueError('请填写1–80字的标注者代号')
        key = hashlib.sha256(annotator.strip().encode()).hexdigest()[:24]
        return self.output / (key + '.json')

    def fresh(self, annotator):
        return {'schema_version': 1, 'purpose': 'single-annotator-development-not-golden',
                'split': 'dev', 'dataset_version': self.dataset.version, 'annotator': annotator.strip(),
                'created_at': now(), 'revision': 0, 'cursor': 0, 'order': self.dataset.ids,
                'annotations': {}, 'history': []}

    def validate(self, state, annotator):
        if state.get('dataset_version') != self.dataset.version or state.get('order') != self.dataset.ids or state.get('annotator') != annotator.strip() or state.get('split') != 'dev':
            raise ValueError('保存记录的数据版本/标注者不符，拒绝混用')
        if not isinstance(state.get('revision'), int) or not 0 <= state.get('cursor', -1) < len(self.dataset.ids):
            raise ValueError('保存记录损坏')
        if not isinstance(state.get('history'), list) or not isinstance(state.get('annotations'), dict):
            raise ValueError('保存记录损坏')
        for sid, entry in state['annotations'].items():
            if sid not in self.dataset.ids:
                raise ValueError('保存记录含未知ID')
            self.fields(entry)
        return state

    def load(self, annotator):
        path = self.path(annotator)
        if not path.exists():
            return self.fresh(annotator)
        try:
            return self.validate(json.loads(path.read_text(encoding='utf-8')), annotator)
        except (ValueError, KeyError, TypeError):
            backup = path.with_suffix('.bak')
            if not backup.exists():
                raise ValueError('保存损坏且没有备份；保留原文件，请恢复备份')
            recovered = self.validate(json.loads(backup.read_text(encoding='utf-8')), annotator)
            shutil.copyfile(path, path.with_name(path.name + '.corrupt-' + str(time.time_ns())))
            atomic(path, recovered)
            recovered['recovery_notice'] = '已从上一份备份恢复，请复核最后一次标注；损坏文件已保留'
            return recovered

    @staticmethod
    def fields(entry):
        if entry.get('label') not in ['', *LABELS] or entry.get('clarity') not in CLARITY or not isinstance(entry.get('note'), str) or len(entry['note']) > 4000 or entry.get('status') not in ('unlabeled', 'labeled', 'skipped'):
            raise ValueError('标签/清晰度/备注/状态不合法')
        if (entry['status'] == 'labeled') != bool(entry['label']):
            raise ValueError('未标或跳过不能带标签')

    def save(self, request):
        annotator = request['annotator']
        state = self.load(annotator)
        if request['revision'] != state['revision']:
            raise ValueError('另一页面已修改，请重新加载，避免覆盖')
        cursor = request['cursor']
        if type(cursor) is not int or not 0 <= cursor < len(self.dataset.ids):
            raise ValueError('非法位置')
        if 'entry' in request:
            sid = request['id']
            if sid not in self.dataset.ids:
                raise ValueError('未知样本')
            entry = request['entry']
            self.fields(entry)
            previous = state['annotations'].get(sid)
            fields = ('label', 'clarity', 'note', 'status')
            if previous is None or any(previous[k] != entry[k] for k in fields):
                entry = {k: entry[k] for k in fields}
                entry['updated_at'] = now()
                state['history'].append({'id': sid, 'previous': previous, 'current': entry.copy(), 'at': entry['updated_at']})
                state['annotations'][sid] = entry
        state['cursor'] = cursor
        state['revision'] += 1
        state['updated_at'] = now()
        path = self.path(annotator)
        if path.exists():
            # 原子更新备份，崩溃不破坏上一版有效快照。
            backup_temp = path.with_suffix('.bak.tmp')
            shutil.copyfile(path, backup_temp)
            os.replace(backup_temp, path.with_suffix('.bak'))
        atomic(path, state)
        atomic(self.output / 'last-annotator.json', {'annotator': state['annotator']})
        return state

    def export(self, annotator, fmt):
        state = self.load(annotator)
        if fmt == 'json':
            return json.dumps(state, ensure_ascii=False, indent=2).encode('utf-8'), 'application/json'
        stream = io.StringIO(newline='')
        fields = ['id', 'label', 'clarity', 'note', 'status', 'annotator', 'split', 'dataset_version', 'order_index', 'updated_at']
        writer = csv.DictWriter(stream, fieldnames=fields)
        writer.writeheader()
        for index, sid in enumerate(self.dataset.ids):
            row = {'id': sid, 'label': '', 'clarity': '', 'note': '', 'status': 'unlabeled', 'updated_at': ''}
            row.update(state['annotations'].get(sid, {}))
            row.update(annotator=state['annotator'], split='dev', dataset_version=self.dataset.version, order_index=index)
            # CSV可安全在表格软件打开；JSON保留备注原文供后续适配。
            for key in ('note', 'annotator'):
                if row[key].lstrip().startswith(('=', '+', '-', '@')):
                    row[key] = "'" + row[key]
            writer.writerow(row)
        return ('\ufeff' + stream.getvalue()).encode('utf-8'), 'text/csv'


class InstanceLock:
    def __init__(self, path):
        path.parent.mkdir(parents=True, exist_ok=True)
        self.file = path.open('a+b')
        if path.stat().st_size == 0:
            self.file.write(b' ')
            self.file.flush()
        self.file.seek(0)
        try:
            if os.name == 'nt':
                import msvcrt
                msvcrt.locking(self.file.fileno(), msvcrt.LK_NBLCK, 1)
            else:
                import fcntl
                fcntl.flock(self.file.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        except OSError:
            self.file.close()
            raise RuntimeError('标注器已经运行，请回到现有窗口；不要同时启动第二份') from None

    def close(self):
        self.file.close()


def serve(dataset, store, token):
    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *_):
            pass

        def respond(self, data, mime='application/json', status=200):
            if isinstance(data, dict):
                data = json.dumps(data, ensure_ascii=False).encode('utf-8')
            self.send_response(status)
            self.send_header('Content-Type', mime + '; charset=utf-8')
            self.send_header('Cache-Control', 'no-store')
            self.send_header('X-Content-Type-Options', 'nosniff')
            self.send_header('Content-Length', str(len(data)))
            self.end_headers()
            self.wfile.write(data)

        def do_GET(self):
            self.dispatch(False)

        def do_POST(self):
            self.dispatch(True)

        def dispatch(self, post):
            try:
                parsed = urlparse(self.path)
                q = parse_qs(parsed.query)
                host = f'127.0.0.1:{self.server.server_port}'
                if self.headers.get('Host') != host or q.get('token', [''])[0] != token:
                    self.respond({'error': '无效本机会话'}, status=403)
                    return
                if post and self.headers.get('Origin', 'http://' + host) != 'http://' + host:
                    self.respond({'error': '拒绝跨站写入'}, status=403)
                    return
                if not post and parsed.path == '/':
                    self.respond(Path(__file__).with_name('annotator.html').read_bytes(), 'text/html')
                elif not post and parsed.path == '/state':
                    state = store.load(q['annotator'][0])
                    self.respond({'state': state, 'ids': dataset.ids, 'count': len(dataset.ids), 'groups': dataset.group_count})
                elif not post and parsed.path == '/profile':
                    profile = store.output / 'last-annotator.json'
                    try:
                        value = json.loads(profile.read_text(encoding='utf-8')) if profile.exists() else {'annotator': ''}
                    except ValueError:
                        value = {'annotator': ''}
                    self.respond(value)
                elif not post and parsed.path.startswith('/image/'):
                    sid = parsed.path.removeprefix('/image/')
                    if sid not in dataset.ids:
                        raise ValueError('未知图片')
                    expected = dataset.entries[dataset.ids.index(sid)]['image_sha256']
                    if digest(dataset.image(sid)) != expected:
                        raise ValueError('图片版本发生变化，停止标注')
                    self.respond(dataset.image(sid).read_bytes(), 'image/png')
                elif not post and parsed.path == '/export':
                    fmt = q.get('format', ['json'])[0]
                    if fmt not in ('csv', 'json'):
                        raise ValueError('未知导出格式')
                    data, mime = store.export(q['annotator'][0], fmt)
                    self.respond(data, mime)
                elif post and parsed.path == '/save':
                    length = int(self.headers.get('Content-Length', 0))
                    if not 0 < length < 20000:
                        raise ValueError('请求过大或为空')
                    request = json.loads(self.rfile.read(length))
                    self.respond({'state': store.save(request)})
                elif post and parsed.path == '/exit':
                    self.respond({'ok': True})
                    self.server.stop_requested = True
                else:
                    self.respond({'error': '不存在的入口'}, status=404)
            except (ValueError, KeyError, TypeError, OSError) as error:
                self.respond({'error': str(error)}, status=400)
    return HTTPServer(('127.0.0.1', 0), Handler)


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    try:
        lock = InstanceLock(OUTPUT / 'server.lock')
    except RuntimeError:
        endpoint = OUTPUT / 'endpoint.json'
        if endpoint.exists():
            webbrowser.open(json.loads(endpoint.read_text())['url'])
        print('标注器已运行：已打开原页面。')
        return
    try:
        dataset = Dataset()
        secret = secrets.token_urlsafe(32)
        server = serve(dataset, Store(dataset), secret)
        url = f'http://127.0.0.1:{server.server_port}/?token={secret}'
        atomic(OUTPUT / 'endpoint.json', {'url': url})
        print(f'dev辅助标注：{len(dataset.ids)}张，{dataset.group_count}组。仅本地，不是独立金标。\n{url}\n退出：页面按钮或Ctrl+C。', flush=True)
        webbrowser.open(url)
        server.timeout = .5
        server.stop_requested = False
        try:
            while not server.stop_requested:
                server.handle_request()
        except KeyboardInterrupt:
            pass
        finally:
            server.server_close()
    finally:
        lock.close()


if __name__ == '__main__':
    main()
