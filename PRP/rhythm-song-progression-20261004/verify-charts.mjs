// 手动只读校验；PASS 为状态锚点。音频必须显式传入；不读取用户档案、不触碰 Unity。
import fs from 'node:fs';
import crypto from 'node:crypto';
import path from 'node:path';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
const root = path.dirname(fileURLToPath(import.meta.url));
const audioSources = process.argv.slice(2);
if (audioSources.length !== 2) throw new Error('usage: node verify-charts.mjs GUITAR_MP3 ATTENTION_MP3');
const chartFiles = ['hybeboy-guitar.chart.json', 'attention.chart.json'];
let previousDensity = 58 / 40;
for (let index = 0; index < chartFiles.length; index++) {
  const bundle = JSON.parse(fs.readFileSync(path.join(root, chartFiles[index]), 'utf8'));
  const chart = bundle.chart;
  const bytes = fs.readFileSync(audioSources[index]);
  assert.equal(bytes.length, bundle.source.bytes);
  assert.equal(crypto.createHash('sha256').update(bytes).digest('hex'), bundle.source.sha256);
  assert.equal(bundle.status, 'automatic-test-chart-not-human-reviewed');
  assert.equal(chart.schemaVersion, 2); assert.equal(chart.chartOffsetMs, 0); assert.equal(chart.laneCount, 4);
  assert.deepEqual(chart.noteTimes, []); assert.deepEqual(chart.noteLanes, []);
  assert.equal(bundle.song.prerequisiteId, 'intro'); assert.equal(bundle.song.passScorePercent, 60);
  assert.ok(chart.clipStartSeconds >= 0 && chart.durationSeconds > 0);
  const analysis = JSON.parse(fs.readFileSync(path.join(root, 'audio-analysis.json'), 'utf8')).reports[index];
  assert.ok(chart.clipStartSeconds + chart.durationSeconds <= analysis.decodedDuration);
  assert.equal(chart.bpmSegments[0].bpm, analysis.bpm);
  const notes = [...chart.notes].sort((a, b) => a.timeMs - b.timeMs);
  const ids = new Set(), previous = Array(4).fill(null);
  let last = 0, holds = 0, simultaneous = 0, lastHead = -1;
  for (const note of notes) {
    assert.ok(!ids.has(note.id)); ids.add(note.id);
    assert.ok(Number.isInteger(note.lane) && note.lane >= 0 && note.lane < 4);
    assert.ok(Number.isFinite(note.timeMs) && note.timeMs >= 0);
    assert.ok(Number.isFinite(note.durationMs) && (note.type === 0 ? note.durationMs === 0 : note.type === 1 && note.durationMs > 0));
    const before = previous[note.lane];
    if (before) {
      assert.ok(note.timeMs - before.timeMs > chart.goodMs * 2, 'head overlap ' + note.id);
      if (before.type === 1) assert.ok(note.timeMs - chart.goodMs > before.timeMs + before.durationMs, 'hold overlap ' + note.id);
    }
    const end = note.timeMs + note.durationMs;
    assert.ok(end <= chart.durationSeconds * 1000);
    last = Math.max(last, end); holds += Number(note.type === 1);
    simultaneous += Number(note.timeMs === lastHead); lastHead = note.timeMs;
    previous[note.lane] = note;
    const evidence = bundle.noteEvidence.find(x => x.id === note.id);
    assert.ok(evidence);
    if (evidence.kind === 'supported-eighth') assert.ok(evidence.onsetStrength >= 1.6 && evidence.onsetSeconds !== null);
    if (note.type === 1) assert.ok(evidence.holdMeanRms > 0.001, 'hold over silence ' + note.id);
  }
  assert.ok(holds > 2); assert.ok(simultaneous > 0);
  const density = notes.length / chart.durationSeconds;
  assert.ok(density > previousDensity, 'difficulty density must increase'); previousDensity = density;
  const deadline = last + chart.goodMs + 300;
  assert.ok(deadline < chart.durationSeconds * 1000, 'max positive input offset needs tail room');
  assert.ok(chart.countdownSeconds >= chart.approachSeconds + 0.3);
  console.log(JSON.stringify({ result: 'PASS', song: bundle.song.id, notes: notes.length, holds, simultaneous, density,
    maxOffsetDeadlineMs: deadline, durationMs: chart.durationSeconds * 1000,
    sourceSHA256: bundle.source.sha256, review: 'automatic-only; musical phrase and Unity PCM alignment unreviewed' }));
}
