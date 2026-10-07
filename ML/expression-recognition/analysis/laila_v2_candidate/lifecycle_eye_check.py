"""手动一次性基线/恢复检查，无Unity连接；终端状态和产物mtime为5秒锚点。
复用诊断哈希函数；本轮需保护最新巡逻者变更，旧round基线不能替代新基线。
"""
import argparse
import subprocess
from analysis.laila_v2_candidate.capture_diagnostics import ROOT, REPO, digest, read_json, write_json

OUT = ROOT / 'artifacts/laila_v2_candidate/lifecycle-eye-20261003'
CHANGED = {'Assets/_Project/Scripts/Runtime/Gameplay/FaceBlendShapeController.cs',
           'Assets/_Project/Scripts/Tests/EditMode/LailaFace/FaceDragHandleTests.cs'}


def main():
    ap = argparse.ArgumentParser(); ap.add_argument('action', choices=['baseline', 'final']); args = ap.parse_args()
    if args.action == 'baseline':
        if (OUT / 'before.json').exists(): raise ValueError('禁止覆盖新窗口基线')
        paths = subprocess.check_output(['git', 'ls-files', '-m', '-o', '--exclude-standard', '--',
                                         'Assets', 'ProjectSettings', 'Packages'], cwd=REPO).decode('utf-8').splitlines()
        paths += ['Assets/_Project/Scenes/RhythmDemo.unity', 'Assets/_Project/Scenes/Boot.unity',
                  'Assets/Scenes/SampleScene.unity', 'Assets/_Project/Scenes/LailaRecognitionPlaytest.unity',
                  'Assets/_Project/Art/Fonts/Font_NotoSansSC_Regular SDF.asset']
        hashes = {p: digest(REPO / p) for p in sorted(set(paths)) if (REPO / p).is_file()}
        write_json(OUT / 'before.json', hashes); print({'protected_files': len(hashes)}); return
    before = read_json(OUT / 'before.json')
    changed = [p for p, h in before.items() if not (REPO / p).is_file() or digest(REPO / p) != h]
    unexpected = [p for p in changed if p not in CHANGED]
    write_json(OUT / 'after.json', {'changed': changed, 'unexpected': unexpected})
    print({'expected_changed': [p for p in changed if p in CHANGED], 'unexpected': unexpected})


if __name__ == '__main__': main()
