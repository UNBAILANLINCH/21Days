// 工具envelope v2支持新增/删除；原SO chart.schemaVersion仍为2，绝不写回原谱。
import * as legacy from '../../../model.mjs';
import * as legacyDraft from '../../draft.mjs';
export const FORMAT=legacy.FORMAT,VERSION=2,DRAFT_FORMAT=legacyDraft.DRAFT_FORMAT,DRAFT_VERSION=2;
export const clone=legacy.clone,timing=legacyDraft.timing,MAX_AGE=legacyDraft.MAX_AGE,snapTime=legacyDraft.snapTime;
const fail=message=>{throw Error(message);};
export const addedIdPattern=/^review-[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;
export function validateChart(chart){
  if(!chart||chart.schemaVersion!==2||chart.laneCount!==4||!chart.chartId||!chart.audioKey)fail('谱面身份/版本不支持');
  for(const k of ['clipStartSeconds','durationSeconds','chartOffsetMs','goodMs'])if(!Number.isFinite(chart[k]))fail('非法 '+k);
  if(chart.clipStartSeconds!==16||chart.durationSeconds!==40||chart.goodMs<0)fail('片段应为16–56s');
  if(!Array.isArray(chart.notes))fail('缺少notes数组');
  const ids=new Set(),lanes=[[],[],[],[]];
  for(const n of chart.notes){
    if(typeof n.id!=='string'||!n.id||ids.has(n.id))fail('重复/无效noteID');ids.add(n.id);
    if(Object.keys(n).sort().join()!==['id','lane','timeMs','type','durationMs'].sort().join())fail('未知音符字段');
    if(!Number.isInteger(n.lane)||n.lane<0||n.lane>3)fail('非法lane: '+n.id);
    if(![0,1].includes(n.type)||!Number.isFinite(n.timeMs)||!Number.isFinite(n.durationMs)||n.timeMs<0||n.durationMs<0||(n.type===0&&n.durationMs!==0)||(n.type===1&&n.durationMs<=0))fail('非法Tap/Hold时间: '+n.id);
    const head=n.timeMs+chart.chartOffsetMs;if(head<0||head+n.durationMs>40000||(addedIdPattern.test(n.id)&&head>=40000))fail('音符超出0–40s片段: '+n.id);
    lanes[n.lane].push(n);
  }
  for(const lane of lanes){lane.sort((a,b)=>a.timeMs-b.timeMs);for(let i=1;i<lane.length;i++){const a=lane[i-1],b=lane[i];if(b.timeMs-a.timeMs<=chart.goodMs*2||b.timeMs<=a.timeMs+a.durationMs)fail('同轨判定窗/Hold占用冲突: '+a.id+'/'+b.id);}}
  return chart;
}
export function differences(original,candidate){
  const base=new Map(original.notes.map(n=>[n.id,n])),next=new Map(candidate.notes.map(n=>[n.id,n])),out=[];
  const song=(n,chart)=>n?16+(n.timeMs+chart.chartOffsetMs)/1000:null;
  function row(operation,a,b){const n=b||a;return {operation,id:n.id,lane:n.lane,originalTimeMs:a?.timeMs??null,candidateTimeMs:b?.timeMs??null,deltaMs:a&&b?b.timeMs-a.timeMs:null,originalSongSeconds:song(a,original),candidateSongSeconds:song(b,candidate)};}
  for(const a of original.notes){const b=next.get(a.id);if(!b)out.push(row('remove',a,null));else if(a.timeMs!==b.timeMs)out.push(row('move',a,b));}
  for(const b of candidate.notes)if(!base.has(b.id))out.push(row('add',null,b));
  return out;
}
function validateEnvelope(bundle,source,draft,{checkAge=false}={}){
  if(!bundle||Object.keys(bundle).sort().join()!==['format','version','status','createdAt','source','review','timing','chart','differences'].sort().join())fail('未知或不完整候选字段');
  if(bundle.format!==(draft?DRAFT_FORMAT:FORMAT)||bundle.version!==2||bundle.status!==(draft?'unreviewed-draft':'unapproved-candidate'))fail('不支持的工具版本/状态');
  const date=Date.parse(bundle.createdAt);if(!Number.isFinite(date)||date>Date.now()+60000)fail('非法创建时间');if(checkAge&&Date.now()-date>MAX_AGE)fail('草稿已超过30天，保留且不覆盖');
  for(const k of ['chartPath','audioPath','chartSha256','audioSha256'])if(bundle.source?.[k]!==source[k])fail('来源hash/路径不匹配，当前候选未改变');
  if(JSON.stringify(bundle.timing)!==JSON.stringify(timing))fail('时间域不匹配');
  const review=bundle.review;if(!review||typeof review.reason!=='string'||!['','melody','accompaniment'].includes(review.voice))fail('非法审核字段');
  if(!draft&&(!['melody','accompaniment'].includes(review.voice)||!review.reason.trim()))fail('请选择旋律或伴奏，并填写审核依据');
  if(Object.keys(bundle.source||{}).sort().join()!==['chartPath','audioPath','chartSha256','audioSha256'].sort().join())fail('Unknown source fields');
  if(Object.keys(review).sort().join()!==['voice','reason'].sort().join())fail('Unknown review fields');
  const a=source.chart,b=validateChart(bundle.chart);
  for(const n of b.notes)if(addedIdPattern.test(n.id)&&!Number.isInteger(n.timeMs))fail('Added Tap time must be integer milliseconds');
  if(Object.keys(a).sort().join()!==Object.keys(b).sort().join())fail('禁止新增谱面字段');
  for(const k of Object.keys(a).filter(k=>k!=='notes'))if(JSON.stringify(a[k])!==JSON.stringify(b[k]))fail('禁止修改 '+k);
  const base=new Map(a.notes.map(n=>[n.id,n]));
  for(const n of b.notes){const old=base.get(n.id);if(old){for(const k of ['lane','type','durationMs'])if(n[k]!==old[k])fail('禁止修改原音符 '+k+': '+n.id);}else if(!addedIdPattern.test(n.id)||n.type!==0||n.durationMs!==0)fail('新增音符须为review-UUID的Tap，不能伪造legacy ID或新增Hold');}
  if(JSON.stringify(bundle.differences)!==JSON.stringify(differences(a,b)))fail('add/remove/move差异清单不匹配');
  return bundle;
}
export function validateCandidate(bundle,source){
  if(bundle?.version===1){legacy.validateCandidate(bundle,source);const migrated=clone(bundle);migrated.version=2;migrated.differences=differences(source.chart,migrated.chart);return validateEnvelope(migrated,source,false);}
  return validateEnvelope(bundle,source,false);
}
export function validateDraft(bundle,source,options={}){
  if(bundle?.version===1){legacyDraft.validateDraft(bundle,source,options);const migrated=clone(bundle);migrated.version=2;migrated.differences=differences(source.chart,migrated.chart);return validateEnvelope(migrated,source,true,options);}
  return validateEnvelope(bundle,source,true,options);
}
