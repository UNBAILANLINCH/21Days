"""手动运行：分析 Unity PCM 起音。既有长曲脚本固定从 15 秒分析，不适用于本段 7 秒音频。"""
import hashlib
import json
from pathlib import Path

import numpy as np

root = Path(__file__).resolve().parents[2]
source = root / "Assets/_Project/Audio/Rhythm/GuoWanPiaoPen.mp3"
data = json.loads((Path(__file__).parent / "envelope.json").read_text())
rms = np.array(data["rms"])
hop = data["hop"] / data["sampleRate"]
assert len(rms) > 100 and np.isfinite(rms).all() and rms.max() > 0
onset = np.maximum(0, rms - np.convolve(rms, np.ones(8) / 8, "full")[:len(rms)])
threshold = max(0.01, float(onset.max()) * 0.25)
peaks = [i for i in range(5, len(rms) - 5)
         if onset[i] > threshold and onset[i] == max(onset[i - 5:i + 6])]
selected = []
for peak in sorted(peaks, key=lambda i: -onset[i]):
    if all(abs(peak - other) * hop >= 0.18 for other in selected):
        selected.append(peak)
selected.sort()
# 取每次明显起音上升段的起点，保留检测证据，不声称已人工确认拍号或声部。
times = []
for peak in selected:
    begin = peak
    while begin > max(0, peak - 6) and onset[begin - 1] > onset[peak] * 0.2:
        begin -= 1
    times.append(round(begin * hop * 1000))
assert times == sorted(set(times)) and all(b - a >= 150 for a, b in zip(times, times[1:]))
duration = data["samples"] / data["sampleRate"]
assert 5 < len(times) < 40 and times[-1] < duration * 1000
chart = {
    "title": "锅碗瓢盆", "schemaVersion": 2, "chartId": "guowanpiaopen-demo",
    "audioKey": "GuoWanPiaoPen", "clipStartSeconds": 0, "durationSeconds": duration,
    "approachSeconds": 1.2, "countdownSeconds": 3, "noteTimes": [], "noteLanes": [],
    "chartOffsetMs": 0, "bpmSegments": [],
    "notes": [{"id": f"gwp-{i + 1:02}", "lane": i % 4, "timeMs": time,
               "type": 0, "durationMs": 0} for i, time in enumerate(times)],
}
evidence = {
    "sourceSHA256": hashlib.sha256(source.read_bytes()).hexdigest(),
    "decoder": "Unity AudioClip.GetData; PCM DecompressOnLoad",
    "sampleRate": data["sampleRate"], "channels": data["channels"], "samples": data["samples"],
    "durationSeconds": duration, "hopSeconds": hop, "threshold": threshold,
    "peakSeconds": [round(i * hop, 3) for i in selected], "noteTimesMs": times,
    "status": "onset-derived-demo; not-human-audition-approved; no-inferred-holds",
}
for name, value in [("chart.json", chart), ("analysis.json", evidence)]:
    (Path(__file__).parent / name).write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"duration": duration, "notes": len(times), "timesMs": times, "sourceSHA256": evidence["sourceSHA256"]}))
