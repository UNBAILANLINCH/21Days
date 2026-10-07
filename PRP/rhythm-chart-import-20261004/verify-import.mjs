// 显式导入核验；PASS 为状态锚点；完成本次谱面接入后作为来源/回退回归保留。
// 不写资产，不执行上传内容；游戏保存仍须通过 Unity SerializedObject。
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import {fileURLToPath} from 'node:url';
import {resolve,dirname} from 'node:path';
import {parseAsset} from '../rhythm-audio-review-20261003/locator/model.mjs';
const here=dirname(fileURLToPath(import.meta.url)),root=resolve(here,'../..');
const sha=bytes=>createHash('sha256').update(bytes).digest('hex');
const raw=await readFile(resolve(here,'chongerfei-unreviewed-draft-v3.library-read.json'),'utf8');
const bundle=JSON.parse(raw),chart=bundle.chart;
assert.deepEqual(Object.keys(bundle).sort(),['format','version','status','createdAt','source','review','timing','chart','differences'].sort());
assert.equal(bundle.format,'21days-rhythm-draft');assert.equal(bundle.version,3);assert.equal(bundle.status,'unreviewed-draft');
assert.deepEqual(bundle.review,{voice:'',reason:''});
assert.deepEqual(bundle.timing,{domain:'decoded-project-mp3',songStartSeconds:16,clipDurationSeconds:40,decoderCorrectionMs:0});
assert.equal(bundle.source.chartPath,'Assets/_Project/Data/Rhythm/ChongErFei.asset');
assert.equal(bundle.source.audioPath,'Assets/_Project/Audio/Rhythm/ChongErFei.mp3');
const backup=await readFile(resolve(here,'ChongErFei.before.asset.txt'));
assert.equal(sha(backup),bundle.source.chartSha256);
assert.equal(sha(await readFile(resolve(root,bundle.source.audioPath))),bundle.source.audioSha256);
const original=parseAsset(backup.toString('utf8'));
assert.deepEqual(Object.keys(chart).sort(),Object.keys(original).sort());
for(const key of Object.keys(original).filter(k=>k!=='notes'))assert.deepEqual(chart[key],original[key],key);
const ids=new Set(),lanes=[[],[],[],[]];
for(const n of chart.notes){
  assert.deepEqual(Object.keys(n).sort(),['id','lane','timeMs','type','durationMs'].sort());
  assert.equal(typeof n.id,'string');assert.ok(n.id&&!ids.has(n.id));ids.add(n.id);
  assert.ok(Number.isInteger(n.lane)&&n.lane>=0&&n.lane<4);
  assert.ok([0,1].includes(n.type)&&Number.isFinite(n.timeMs)&&Number.isFinite(n.durationMs));
  assert.ok(n.timeMs>=0&&(n.type===0?n.durationMs===0:n.durationMs>0));
  assert.ok(n.timeMs+chart.chartOffsetMs>=0&&n.timeMs+n.durationMs+chart.chartOffsetMs<=40000);
  lanes[n.lane].push(n);
}
// 与 Runtime 相同：Hold 后下一枚的头部 Good 窗口也不能侵入尾部。
for(const lane of lanes){
  lane.sort((a,b)=>a.timeMs-b.timeMs);
  for(let i=1;i<lane.length;i++){
    const a=lane[i-1],b=lane[i];assert.ok(b.timeMs-a.timeMs>chart.goodMs*2+1e-6,`${a.id}/${b.id}: head window`);
    if(a.type===1)assert.ok(b.timeMs-chart.goodMs>a.timeMs+a.durationMs+1e-6,`${a.id}/${b.id}: Hold occupancy`);
  }
}
const before=new Map(original.notes.map(n=>[n.id,n])),after=new Map(chart.notes.map(n=>[n.id,n]));
const diff=[];
const song=n=>n?16+n.timeMs/1000:null,end=n=>n?n.timeMs+n.durationMs:null,songEnd=n=>n?16+end(n)/1000:null;
function row(operation,a,b){const n=b||a;return {operation,id:n.id,lane:n.lane,originalTimeMs:a?.timeMs??null,candidateTimeMs:b?.timeMs??null,deltaMs:a&&b?b.timeMs-a.timeMs:null,originalSongSeconds:song(a),candidateSongSeconds:song(b),originalType:a?.type??null,candidateType:b?.type??null,originalDurationMs:a?.durationMs??null,candidateDurationMs:b?.durationMs??null,originalEndMs:end(a),candidateEndMs:end(b),originalSongEndSeconds:songEnd(a),candidateSongEndSeconds:songEnd(b)};}
for(const a of original.notes){const b=after.get(a.id);if(!b)diff.push(row('remove',a,null));else{for(const k of ['lane','type','durationMs'])assert.equal(b[k],a[k]);if(a.timeMs!==b.timeMs)diff.push(row('move',a,b));}}
for(const b of chart.notes)if(!before.has(b.id)){assert.match(b.id,/^review-[0-9a-f-]{36}$/);diff.push(row('add',null,b));}
assert.deepEqual(bundle.differences,diff);
assert.equal(diff.filter(x=>x.operation==='move').length,6);assert.equal(diff.filter(x=>x.operation==='remove').length,1);assert.equal(diff.filter(x=>x.operation==='add').length,3);
assert.equal(chart.notes.length,58);assert.equal(chart.notes.filter(n=>n.type===0).length,56);assert.equal(chart.notes.filter(n=>n.type===1).length,2);
const sorted=[...chart.notes].sort((a,b)=>a.timeMs-b.timeMs);assert.equal(sorted[0].timeMs,464);assert.equal(sorted.at(-1).timeMs,39466);
const lastEnd=Math.max(...chart.notes.map(n=>n.timeMs+n.durationMs));
assert.ok(lastEnd+chart.goodMs+300<40000,'最大正补偿的最后判定窗口仍在音频片段内');
const current=await readFile(resolve(root,bundle.source.chartPath));
if(process.argv.includes('--after'))assert.deepEqual(parseAsset(current.toString('utf8')),chart);
else assert.equal(sha(current),bundle.source.chartSha256);
console.log(JSON.stringify({result:'PASS',phase:process.argv.includes('--after')?'installed':'prepared',notes:58,tap:56,hold:2,moves:6,removed:1,added:3,firstMs:464,lastMs:lastEnd,maxCompensatedDeadlineMs:lastEnd+chart.goodMs+300,audioSha256:bundle.source.audioSha256,assetSha256:sha(current),libraryReadTextSha256:sha(Buffer.from(raw)),originalUploadBytesVerified:false},null,2));
