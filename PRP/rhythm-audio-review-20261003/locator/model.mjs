// 原 A/B 页是固定 WAV 对照，无法按 noteID 定位或导出；这里仅增加只读来源与候选契约。
export const FORMAT = '21days-rhythm-review';
export const VERSION = 1;
export const clone = value => JSON.parse(JSON.stringify(value));
const fail = message => { throw new Error(message); };
export function validateChart(chart) {
  if (chart.schemaVersion !== 2 || chart.laneCount !== 4 || !chart.chartId || !chart.audioKey) fail('谱面版本/身份/轨道数不支持');
  for (const k of ['clipStartSeconds','durationSeconds','chartOffsetMs','goodMs']) if (!Number.isFinite(chart[k])) fail(`非法 ${k}`);
  if (chart.clipStartSeconds !== 16 || chart.durationSeconds !== 40 || chart.goodMs < 0) fail('本工具仅支持虫儿飞 16–56 秒片段');
  if (!Array.isArray(chart.notes) || !chart.notes.length) fail('缺少 notes');
  const ids = new Set(), lanes = [[],[],[],[]];
  for (const n of chart.notes) {
    if (typeof n.id !== 'string' || !n.id || ids.has(n.id)) fail('重复/无效 noteID');
    ids.add(n.id);
    if (!Number.isInteger(n.lane) || n.lane < 0 || n.lane > 3) fail(`非法 lane: ${n.id}`);
    if (![0,1].includes(n.type) || !Number.isFinite(n.timeMs) || !Number.isFinite(n.durationMs)) fail(`非法音符: ${n.id}`);
    if (n.timeMs < 0 || n.durationMs < 0 || (n.type === 0 && n.durationMs !== 0) || (n.type === 1 && n.durationMs <= 0)) fail(`非法 Tap/Hold: ${n.id}`);
    const head = n.timeMs + chart.chartOffsetMs, end = head + n.durationMs;
    if (head < 0 || end > chart.durationSeconds * 1000) fail(`超出片段: ${n.id}`);
    lanes[n.lane].push(n);
  }
  for (const lane of lanes) {
    lane.sort((a,b)=>a.timeMs-b.timeMs);
    for(let i=1;i<lane.length;i++) {
      const a=lane[i-1], b=lane[i];
      if(b.timeMs-a.timeMs <= chart.goodMs*2 || b.timeMs <= a.timeMs+a.durationMs) fail(`同轨判定窗/Hold 占用冲突: ${a.id}/${b.id}`);
    }
  }
  return chart;
}
export function parseAsset(text) {
  const scalar = key => {
    const match = text.match(new RegExp(`^  ${key}: (.+)$`, 'm'));
    if (!match) fail(`资产缺少 ${key}`);
    return match[1].trim();
  };
  const section = text.match(/^  notes:\r?\n([\s\S]*?)^  bpmSegments:/m);
  if(!section) fail('无法解析 notes');
  const chunks = section[1].split(/^  - id: /m).slice(1);
  const notes = chunks.map(chunk => {
    const lines=chunk.trim().split(/\r?\n/), n={id:lines.shift().trim()};
    if(lines.length !== 4) fail('notes 含未知字段，拒绝部分解析');
    for(const key of ['lane','timeMs','type','durationMs']) {
      const match=lines.find(line=>line.trim().startsWith(`${key}: `));
      if(!match) fail(`音符缺少 ${key}`);
      n[key]=Number(match.trim().slice(key.length+2));
    }
    return n;
  });
  const chart={chartId:scalar('chartId'),audioKey:scalar('audioKey'),notes};
  for(const key of ['schemaVersion','laneCount','clipStartSeconds','durationSeconds','chartOffsetMs','goodMs']) chart[key]=Number(scalar(key));
  if(!/^\[\]$/.test(scalar('bpmSegments'))) fail('本工具不解析非空 BPM 段');
  return validateChart(chart);
}
export function differences(original,candidate) {
  const map=new Map(original.notes.map(n=>[n.id,n]));
  return candidate.notes.filter(n=>n.timeMs!==map.get(n.id)?.timeMs).map(n=>({id:n.id,lane:n.lane,originalTimeMs:map.get(n.id).timeMs,candidateTimeMs:n.timeMs,deltaMs:n.timeMs-map.get(n.id).timeMs,originalSongSeconds:16+(map.get(n.id).timeMs+original.chartOffsetMs)/1000,candidateSongSeconds:16+(n.timeMs+candidate.chartOffsetMs)/1000}));
}
export function validateCandidate(bundle,source) {
  if(!bundle || typeof bundle!=='object' || Object.keys(bundle).sort().join()!==['format','version','status','createdAt','source','review','timing','chart','differences'].sort().join()) fail('候选字段不完整或含未知字段');
  if(bundle.format!==FORMAT || bundle.version!==VERSION || bundle.status!=='unapproved-candidate') fail('不支持的候选版本/状态');
  if(typeof bundle.createdAt!=='string' || !Number.isFinite(Date.parse(bundle.createdAt))) fail('无效导出时间');
  if(JSON.stringify(bundle.timing)!==JSON.stringify({domain:'decoded-project-mp3',songStartSeconds:16,clipDurationSeconds:40,decoderCorrectionMs:0})) fail('不支持的时间域或隐式解码补偿');
  if(bundle.source?.chartSha256!==source.chartSha256 || bundle.source?.audioSha256!==source.audioSha256 || bundle.source?.chartPath!==source.chartPath || bundle.source?.audioPath!==source.audioPath) fail('原谱/音频 hash 或路径不匹配，禁止导入');
  if(!['melody','accompaniment'].includes(bundle.review?.voice) || typeof bundle.review.reason!=='string' || !bundle.review.reason.trim()) fail('请选择旋律或伴奏，并填写审核依据');
  if(!bundle.chart || typeof bundle.chart!=='object') fail('缺少候选谱面');
  validateChart(bundle.chart);
  const base=source.chart, chart=bundle.chart;
  for(const k of Object.keys(base).filter(k=>k!=='notes')) if(JSON.stringify(base[k])!==JSON.stringify(chart[k])) fail(`只允许修改单音符时间: ${k}`);
  if(Object.keys(chart).sort().join()!==Object.keys(base).sort().join() || chart.notes.length!==base.notes.length) fail('不允许增删字段或音符');
  for(let i=0;i<base.notes.length;i++) {
    const a=base.notes[i],b=chart.notes[i];
    if(Object.keys(a).sort().join()!==Object.keys(b).sort().join()) fail('未知音符字段');
    for(const k of ['id','lane','type','durationMs']) if(a[k]!==b[k]) fail(`禁止修改 ${k}`);
  }
  if(JSON.stringify(bundle.differences)!==JSON.stringify(differences(base,chart))) fail('差异清单与候选不一致');
  return bundle;
}
