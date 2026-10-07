// 独立草稿契约，复用原谱合法性检查，不伪造审核声部或依据。
import {validateChart,differences} from '../model.mjs';
export const DRAFT_FORMAT='21days-rhythm-draft';
export const DRAFT_VERSION=1;
export const MAX_AGE=30*24*60*60*1000;
export const timing={domain:'decoded-project-mp3',songStartSeconds:16,clipDurationSeconds:40,decoderCorrectionMs:0};
export function validateDraft(bundle,source,{checkAge=false}={}){
  const fail=message=>{throw Error(message);};
  if(!bundle||Object.keys(bundle).sort().join()!==['format','version','status','createdAt','source','review','timing','chart','differences'].sort().join())fail('草稿字段不完整或未知');
  if(bundle.format!==DRAFT_FORMAT||bundle.version!==DRAFT_VERSION||bundle.status!=='unreviewed-draft')fail('不支持的草稿版本/状态');
  const date=Date.parse(bundle.createdAt);
  if(!Number.isFinite(date)||date>Date.now()+60000)fail('非法草稿时间');
  if(checkAge&&Date.now()-date>MAX_AGE)fail('草稿已超过30天，保留原记录且不覆盖当前编辑');
  for(const k of ['chartPath','audioPath','chartSha256','audioSha256'])if(bundle.source?.[k]!==source[k])fail('草稿来源 hash/路径不匹配，未改变当前候选');
  if(JSON.stringify(bundle.timing)!==JSON.stringify(timing))fail('草稿时间域不匹配');
  if(!bundle.review||!['','melody','accompaniment'].includes(bundle.review.voice)||typeof bundle.review.reason!=='string')fail('非法草稿审核字段');
  const a=source.chart,b=bundle.chart;validateChart(b);
  if(Object.keys(a).sort().join()!==Object.keys(b).sort().join()||a.notes.length!==b.notes.length)fail('禁止增删字段或音符');
  for(const k of Object.keys(a).filter(k=>k!=='notes'))if(JSON.stringify(a[k])!==JSON.stringify(b[k]))fail(`禁止修改 ${k}`);
  for(let i=0;i<a.notes.length;i++){
    if(Object.keys(a.notes[i]).sort().join()!==Object.keys(b.notes[i]).sort().join())fail('未知音符字段');
    for(const k of ['id','lane','type','durationMs'])if(a.notes[i][k]!==b.notes[i][k])fail(`禁止修改 ${k}`);
  }
  if(JSON.stringify(bundle.differences)!==JSON.stringify(differences(a,b)))fail('草稿差异清单不匹配');
  return bundle;
}
export function snapTime(timeMs,bpm,originSeconds,subdivision,offsetMs=0){
  if(!Number.isFinite(bpm)||bpm<30||bpm>300||!Number.isFinite(originSeconds)||originSeconds<0||originSeconds>40||![1,2,4,8].includes(subdivision))throw Error('参考网格 BPM/原点/细分无效');
  const step=60000/bpm/subdivision,origin=originSeconds*1000-offsetMs;
  return Math.round(origin+Math.round((timeMs-origin)/step)*step);
}
