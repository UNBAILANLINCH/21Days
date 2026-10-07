// 只读核验实际整合资产；不调用 Unity、不改 YAML 或生成物。
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
const root = process.cwd();
const resourcesOnly = process.argv.includes('--resources-only');
const folder = path.join(root, 'PRP/rhythm-song-progression-20261004');
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8');
const hash = relative => crypto.createHash('sha256').update(fs.readFileSync(path.join(root, relative))).digest('hex');
const guid = relative => read(relative + '.meta').match(/^guid: (\w+)$/m)[1];
for (const [id, name] of [['hybeboy-guitar', 'HybeboyGuitar'], ['attention', 'AttentionInstrumental']]) {
  const bundle = JSON.parse(fs.readFileSync(path.join(folder, id + '.chart.json'), 'utf8'));
  const asset = 'Assets/_Project/Data/Rhythm/' + name + '.asset';
  const audio = 'Assets/_Project/Audio/Rhythm/' + name + '.mp3';
  const text = read(asset);
  const scalar = key => text.match(new RegExp('^  ' + key + ': (.+)$', 'm'))?.[1].trim();
  assert.equal(hash(audio), bundle.source.sha256, id + ' uploaded audio bytes');
  assert.equal(fs.statSync(path.join(root, audio)).size, bundle.source.bytes);
  assert.ok(text.includes('guid: ' + guid(audio)), id + ' AudioClip reference');
  for (const key of ['chartId', 'audioKey']) assert.equal(scalar(key), bundle.chart[key], id + ' ' + key);
  for (const key of ['schemaVersion', 'laneCount', 'chartOffsetMs', 'perfectMs', 'goodMs', 'clipStartSeconds', 'durationSeconds']) {
    assert.ok(Math.abs(Number(scalar(key)) - bundle.chart[key]) < 0.00001, id + ' ' + key);
  }
  const section = text.match(/^  notes:\r?\n([\s\S]*?)^  bpmSegments:/m)[1];
  const notes = section.split(/^  - id: /m).slice(1).map(chunk => {
    const lines = chunk.trim().split(/\r?\n/);
    const note = { id: lines.shift().trim() };
    assert.equal(lines.length, 4);
    for (const key of ['lane', 'timeMs', 'type', 'durationMs']) note[key] = Number(lines.find(line => line.trim().startsWith(key + ': ')).trim().slice(key.length + 2));
    return note;
  });
  assert.deepEqual(notes, bundle.chart.notes, id + ' all note fields');
  assert.ok(text.includes('guid: ' + guid('Assets/_Project/Scripts/Runtime/Rhythm/RhythmConfig.cs')), id + ' existing config script reference');
  assert.ok(text.includes('guid: ' + guid('Assets/_Project/Data/Rhythm/RhythmInput.asset')), id + ' existing input reference');
  assert.ok(guid(asset), id + ' config meta');
  if (!resourcesOnly) assert.ok(read('Assets/_Project/Data/Rhythm/RhythmSongCatalog.asset').includes('guid: ' + guid(asset)), id + ' catalog reference');
  console.log(id + ': uploaded bytes, clip/config/input references and all ' + notes.length + ' notes PASS');
}
if (!resourcesOnly) {
assert.equal(hash('Assets/_Project/Data/Rhythm/ChongErFei.asset'), hash('PRP/rhythm-song-progression-20261004/ChongErFei58.before.asset.txt'), 'intro unchanged');
assert.ok(read('Assets/_Project/Scenes/RhythmDemo.unity').includes('catalog: {fileID: 11400000, guid: ' + guid('Assets/_Project/Data/Rhythm/RhythmSongCatalog.asset')));
for (const name of ['RhythmCatalogConfig', 'RhythmProgressData', 'RhythmProgressRules', 'RhythmSelectionRules', 'RhythmSongData']) assert.ok(fs.existsSync(path.join(root, 'Assets/_Project/Scripts/Runtime/Rhythm/' + name + '.cs.meta')));
assert.ok(fs.existsSync(path.join(root, 'Assets/_Project/Scripts/Tests/EditMode/Rhythm/RhythmProgressTests.cs.meta')));
console.log('intro backup hash, scene catalog reference and new script meta pairs PASS');
} else console.log('PASS resource-only verification; catalog and scene integration excluded');
