import {clone,validateChart,validateCandidate,differences,FORMAT,VERSION} from './base-model.mjs';
import {DRAFT_FORMAT,DRAFT_VERSION,validateDraft,snapTime,timing,MAX_AGE} from './draft.mjs';
const $=id=>document.getElementById(id),keys=['D','F','J','K'];
let rangeMode='full',playbackEpoch=0,starting=false;
let source,candidate,ctx,buffer,position=0,view=[0,40],selected,playback,nodes=new Set(),drag,waveCache;
const status=(message,error=false,target='editStatus')=>{for(const id of ['status',target]){$(id).textContent=message;$(id).className=error?'feedback error':'feedback';}};
const guard=(fn,target='editStatus')=>async(...args)=>{try{await fn(...args);}catch(e){status(e.message,true,target);}};
let undoStack=[],redoStack=[],savedSignature='',storageKey='',observedRecord=null;
const storagePrefix='21days-rhythm-draft:v1:';
const signature=()=>JSON.stringify({chart:candidate,voice:$('voice').value,reason:$('reason').value});
const pendingTime=()=>!!selected&&!!candidate&&Number($('time').value)!==candidate.notes.find(n=>n.id===selected)?.timeMs;
const dirty=()=>!!source&&(signature()!==savedSignature||pendingTime());
const active=()=>$('mode').value==='original'?source.chart:candidate;
function region(){
  const a=Number($('start').value),b=Number($('end').value);
  if(!Number.isFinite(a)||!Number.isFinite(b)||a<0||b>40||b-a<.05) throw Error('循环范围须在 0–40s 内且至少 50ms');
  return [a,b];
}
function current(){
  if(!playback)return position;
  const elapsed=Math.max(0,ctx.currentTime-playback.anchor),p=playback.from+elapsed;
  return playback.loop?playback.a+((p-playback.a)%(playback.b-playback.a)):Math.min(p,playback.b);
}
function stop(){
  playbackEpoch++;starting=false;position=current();playback=null;
  for(const node of nodes){try{node.stop();}catch{} node.disconnect();} nodes.clear();
  $('play').textContent='播放 / 暂停';
}
async function play(){
  if(!buffer)throw Error('先加载音频');
  const epoch=++playbackEpoch;starting=true;$('play').textContent='正在启动音频…';
  try{await ctx.resume();}catch(e){if(epoch===playbackEpoch)stop();throw e;}
  if(epoch!==playbackEpoch)return;starting=false;
  $('sourceAudio').pause();
  const loop=rangeMode!=='full'&&$('loop').checked,[a,b]=rangeMode==='full'?[0,40]:region();
  if(position<a||position>=b)position=a;
  const from=position,anchor=ctx.currentTime+.08,chart=active(),events=[];
  if($('noteClick').checked)for(const n of chart.notes){const t=(n.timeMs+chart.chartOffsetMs)/1000;if(t>=a&&t<b)events.push({t,frequency:900+n.lane*180});}
  if($('beatClick').checked){
    const bpm=Number($('bpm').value),origin=Number($('beatOrigin').value);
    if(!Number.isFinite(bpm)||bpm<30||bpm>300||!Number.isFinite(origin)||origin<0||origin>40)throw Error('参考节拍参数无效');
    const step=60/bpm;
    for(let t=origin+Math.ceil((a-origin)/step)*step;t<b;t+=step)events.push({t,frequency:500});
  }
  events.sort((x,y)=>x.t-y.t);
  const audio=ctx.createBufferSource();audio.buffer=buffer;audio.loop=loop;audio.loopStart=16+a;audio.loopEnd=16+b;audio.connect(ctx.destination);
  playback={from,anchor,a,b,loop};
  nodes.add(audio);audio.onended=()=>{nodes.delete(audio);audio.disconnect();};
  audio.start(anchor,16+from);if(!loop)audio.stop(anchor+b-from);
  if(events.length){
    // 参考click采用同采样率循环buffer，避开JS定时器在短循环/负载下的迟到补发。
    const rate=buffer.sampleRate,length=Math.max(1,Math.round((b-a)*rate)),clickBuffer=ctx.createBuffer(1,length,rate),samples=clickBuffer.getChannelData(0);
    for(const event of events){
      const head=Math.round((event.t-a)*rate),count=Math.round(.025*rate);
      for(let i=0;i<count&&head+i<length;i++)samples[head+i]+=.12*Math.sin(2*Math.PI*event.frequency*i/rate)*Math.exp(-9*i/count);
    }
    const clicks=ctx.createBufferSource();clicks.buffer=clickBuffer;clicks.loop=loop;clicks.connect(ctx.destination);nodes.add(clicks);
    clicks.onended=()=>{nodes.delete(clicks);clicks.disconnect();};clicks.start(anchor,from-a);if(!loop)clicks.stop(anchor+b-from);
  }
  $('play').textContent='暂停';
}
async function seek(value){const resume=!!playback;stop();value=Math.min(40,Math.max(0,value));if(rangeMode!=='full'){const [a,b]=region();if(value<a||value>=b)returnFull();}position=value;if(resume)await play();draw();}
async function restart(){if(playback){stop();await play();}}
function select(id){const previous=selected;selected=id;if(!candidate?.notes.some(n=>n.id===id)){$('noteInfo').textContent='未选择音符；可播放完整片段';$('time').value='';$('focusNote').disabled=true;$('audition').disabled=true;$('apply').disabled=true;for(const el of document.querySelectorAll('[data-step]'))el.disabled=true;if(rangeMode==='note')returnFull();renderRows();return;}$('focusNote').disabled=false;$('audition').disabled=false;$('apply').disabled=false;for(const el of document.querySelectorAll('[data-step]'))el.disabled=false;$('note').value=id;const a=source.chart.notes.find(n=>n.id===id),b=candidate.notes.find(n=>n.id===id);$('time').value=b.timeMs;$('noteInfo').textContent=`${id} · lane ${a.lane} (${keys[a.lane]}) · A ${a.timeMs} → B ${b.timeMs}ms · Δ ${(b.timeMs-a.timeMs).toFixed(1)}ms`;renderRows();const row=$('notes').querySelector('.selected'),scroller=$('notes').closest('.scroll');if(row)scroller.scrollTop+=row.getBoundingClientRect().top-scroller.getBoundingClientRect().top-scroller.clientHeight/2+row.getBoundingClientRect().height/2;if(rangeMode==='note'&&previous!==id)focusNote();}
function renderRows(){
  $('notes').replaceChildren();
  for(let i=0;i<candidate.notes.length;i++){
    const n=candidate.notes[i],a=source.chart.notes[i],tr=document.createElement('tr');
    if(n.id===selected)tr.className='selected';
    for(const value of [n.id,`${n.lane} / ${keys[n.lane]}`,a.timeMs,n.timeMs,(n.timeMs-a.timeMs).toFixed(3),(16+(n.timeMs+candidate.chartOffsetMs)/1000).toFixed(3)]){const td=document.createElement('td');td.textContent=value;tr.append(td);}
    tr.onclick=()=>select(n.id);$('notes').append(tr);
  }
  const diff=differences(source.chart,candidate);$('diff').textContent=diff.length?diff.map(n=>`${n.id}: ${n.originalTimeMs} → ${n.candidateTimeMs}ms (Δ ${n.deltaMs.toFixed(3)}ms)`).join('；'):'候选尚无修改';
}
async function adjust(value,useSnap=true){
  if(!source)throw Error('请等待加载');
  if(!Number.isFinite(value)||!Number.isInteger(value))throw Error('临时调整须为整数毫秒');
  if(useSnap&&$('snap').checked)value=snapTime(value,Number($('bpm').value),Number($('beatOrigin').value),Number($('division').value),candidate.chartOffsetMs);
  const next=clone(candidate);next.notes.find(n=>n.id===selected).timeMs=value;commit(next);await restart();status('已调整，尚未保存草稿；原谱未修改');
}
function draw(){
  const canvas=$('timeline'),g=canvas.getContext('2d'),w=canvas.width,h=canvas.height,[a,b]=view,x=t=>(t-a)/(b-a)*w;
  g.clearRect(0,0,w,h);g.font='12px system-ui';
  const interval=b-a>10?5:.5;
  for(let t=Math.ceil(a/interval)*interval;t<=b;t+=interval){g.strokeStyle='#34465e';g.beginPath();g.moveTo(x(t),0);g.lineTo(x(t),h);g.stroke();g.fillStyle='#acbfd5';g.fillText(`${t.toFixed(1)}s / ${(16+t).toFixed(1)}s`,x(t)+4,15);}
  if($('snap').checked||$('beatClick').checked){
    const bpm=Number($('bpm').value),origin=Number($('beatOrigin').value),division=Number($('division').value);
    if(Number.isFinite(bpm)&&bpm>=30&&bpm<=300&&Number.isFinite(origin)&&origin>=0&&origin<=40){
      const step=60/bpm/division;g.strokeStyle='#345470';g.setLineDash([2,4]);
      for(let t=origin+Math.ceil((a-origin)/step)*step;t<=b;t+=step){g.beginPath();g.moveTo(x(t),20);g.lineTo(x(t),h);g.stroke();}g.setLineDash([]);
    }
  }
  if(buffer){
    if(!waveCache||waveCache.buffer!==buffer||waveCache.a!==a||waveCache.b!==b){
      const data=buffer.getChannelData(0),rate=buffer.sampleRate,peaks=[];
      for(let px=0;px<w;px++){
        const lo=Math.floor((16+a+(b-a)*px/w)*rate),hi=Math.floor((16+a+(b-a)*(px+1)/w)*rate);let min=0,max=0;
        for(let i=lo;i<=hi;i++){min=Math.min(min,data[i]||0);max=Math.max(max,data[i]||0);}peaks.push([min,max]);
      }waveCache={buffer,a,b,peaks};
    }
    g.strokeStyle='#829bad';g.beginPath();
    for(let px=0;px<w;px++){
      const [min,max]=waveCache.peaks[px];g.moveTo(px,70-min*45);g.lineTo(px,70-max*45);
    }g.stroke();
  }
  for(let lane=0;lane<4;lane++){g.fillStyle='#b1c2d8';g.fillText(`${lane} ${keys[lane]}`,4,140+lane*35);}
  if(source)for(const [chart,color,offset] of [[source.chart,'#64e5c3',0],[candidate,'#ffb76a',8]])for(const n of chart.notes){const t=(n.timeMs+chart.chartOffsetMs)/1000;if(t<a||t>b)continue;g.fillStyle=color;g.fillRect(x(t)-3,125+n.lane*35+offset,6,14);if(n.id===selected){g.strokeStyle='#fff';g.strokeRect(x(t)-5,123+n.lane*35+offset,10,18);g.fillText(n.id,x(t)+6,125+n.lane*35+offset);}}
  $('viewInfo').textContent=`时间窗：片段 ${a.toFixed(3)}–${b.toFixed(3)}s / 原曲 ${(16+a).toFixed(3)}–${(16+b).toFixed(3)}s`;
  const p=current();g.strokeStyle='#fff';g.beginPath();g.moveTo(x(p),0);g.lineTo(x(p),h);g.stroke();
  $('seek').value=p;$('position').textContent=`片段 ${p.toFixed(3)}s / 原曲 ${(16+p).toFixed(3)}s`;
}
function animate(){if(playback&&!playback.loop&&current()>=playback.b)stop();draw();renderPlaybackState();requestAnimationFrame(animate);}
$('load').onclick=guard(async()=>{
  stop();status('加载并解码 MP3…');
  const response=await fetch('/source');if(!response.ok)throw Error(await response.text());
  const nextSource=await response.json();validateChart(nextSource.chart);
  ctx ||= new AudioContext();const audioResponse=await fetch('/audio');if(!audioResponse.ok)throw Error('音频读取失败');
  const bytes=await audioResponse.arrayBuffer();
  const decodedHash=Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',bytes)),b=>b.toString(16).padStart(2,'0')).join('');
  if(decodedHash!==nextSource.audioSha256)throw Error('音频读取期间发生变更，请重新加载');
  buffer=await ctx.decodeAudioData(bytes);if(buffer.duration<56)throw Error('音频长度不足 56s');
  source=nextSource;candidate=clone(source.chart);position=0;view=[0,40];savedSignature=signature();storageKey=storagePrefix+source.chartSha256+':'+source.audioSha256+':main';
  for(const id of ['play','middle','seek','range','all','note','apply','export','import'])$(id).disabled=false;
  $('note').replaceChildren(...candidate.notes.map(n=>{const option=document.createElement('option');option.value=n.id;option.textContent=n.id;return option;}));const empty=document.createElement('option');empty.value='';empty.textContent='未选择（完整片段仍可播放）';$('note').prepend(empty);select(candidate.notes[0].id);
  $('hash').textContent=`原谱 SHA-256 ${source.chartSha256} · 音频 SHA-256 ${source.audioSha256}`;
  $('load').textContent='原谱已加载（只读）';$('saveDraft').disabled=false;$('downloadDraft').disabled=false;
  status(`已加载 ${candidate.notes.length} 音符 · MP3 ${buffer.duration.toFixed(3)}s · 解码 ${buffer.sampleRate}Hz`);inspectDrafts();
});
$('play').onclick=guard(async()=>{if(playback||starting)stop();else await play();});
$('middle').onclick=guard(()=>{stop();rangeMode='custom';$('start').value=18.9;$('end').value=21.5;$('loop').checked=true;view=[18.9,21.5];position=18.9;select('legacy-26');});
$('seek').oninput=guard(()=>seek(Number($('seek').value)));
$('range').onclick=guard(()=>{stop();rangeMode='custom';view=region();position=view[0];});$('all').onclick=guard(()=>returnFull());
for(const id of ['mode','noteClick','beatClick','bpm','beatOrigin'])$(id).onchange=guard(restart);
$('loop').onchange=guard(restart);
for(const id of ['start','end'])$(id).onchange=guard(()=>{stop();rangeMode='custom';position=region()[0];});
$('note').onchange=()=>select($('note').value);$('apply').onclick=guard(()=>adjust(Number($('time').value)));

function bundle(){
  const data={format:FORMAT,version:VERSION,status:'unapproved-candidate',createdAt:new Date().toISOString(),source:{chartPath:source.chartPath,audioPath:source.audioPath,chartSha256:source.chartSha256,audioSha256:source.audioSha256},review:{voice:$('voice').value,reason:$('reason').value},timing:{domain:'decoded-project-mp3',songStartSeconds:16,clipDurationSeconds:40,decoderCorrectionMs:0},chart:clone(candidate),differences:differences(source.chart,candidate)};
  validateCandidate(data,source);return data;
}
function download(name,body,type){const url=URL.createObjectURL(new Blob([body],{type})),a=document.createElement('a');a.href=url;a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);}
function pointerTime(e){const r=$('timeline').getBoundingClientRect();return view[0]+Math.min(1,Math.max(0,(e.clientX-r.left)/r.width))*(view[1]-view[0]);}
$('timeline').onpointerdown=guard(async e=>{
  if(!source)return;const r=$('timeline').getBoundingClientRect(),t=pointerTime(e),y=(e.clientY-r.top)/r.height*$('timeline').height;
  const chart=active(),offset=$('mode').value==='candidate'?8:0;
  const near=chart.notes.find(n=>Math.abs((n.timeMs+chart.chartOffsetMs)/1000-t)<(view[1]-view[0])*8/r.width && y>=123+n.lane*35+offset && y<=143+n.lane*35+offset);
  if(near){select(near.id);if($('mode').value==='candidate'){drag={id:near.id,time:near.timeMs,resume:!!playback};stop();$('timeline').setPointerCapture(e.pointerId);}}else await seek(t);
});
$('timeline').onpointermove=guard(e=>{if(drag){let value=Math.round(pointerTime(e)*1000-candidate.chartOffsetMs);if($('snap').checked)value=snapTime(value,Number($('bpm').value),Number($('beatOrigin').value),Number($('division').value),candidate.chartOffsetMs);$('time').value=value;}});
$('timeline').onpointerup=guard(async()=>{if(!drag)return;const previous=drag;drag=null;try{await adjust(Number($('time').value));}catch(e){select(previous.id);status(e.message,true);}if(previous.resume)await play();});
$('timeline').onpointercancel=()=>{if(drag){select(drag.id);drag=null;}};
document.addEventListener('visibilitychange',()=>{if(document.hidden&&playback){stop();status('页面隐藏，已暂停');}});
window.addEventListener('blur',()=>{if(playback){stop();status('页面失焦，已暂停');}});
window.addEventListener('pagehide',stop);
animate();

function updateHistory(){
  $('undo').disabled=!undoStack.length;$('redo').disabled=!redoStack.length;
  $('saveDraft').textContent=dirty()?'保存本地草稿（未保存）':'保存本地草稿';
}
function historySnapshot(){return {chart:clone(candidate),voice:$('voice').value,reason:$('reason').value};}
function commit(next){
  validateChart(next);if(JSON.stringify(next)===JSON.stringify(candidate))return;
  undoStack.push(historySnapshot());if(undoStack.length>100)undoStack.shift();redoStack=[];
  candidate=next;select(selected);updateHistory();
}
async function history(direction){
  const from=direction==='undo'?undoStack:redoStack,to=direction==='undo'?redoStack:undoStack;
  if(!from.length)return;to.push(historySnapshot());const snapshot=from.pop();candidate=snapshot.chart;$('voice').value=snapshot.voice;$('reason').value=snapshot.reason;select(selected);updateHistory();await restart();status(direction==='undo'?'已撤销；原谱未修改':'已重做；原谱未修改');
}
function setView(center,width){
  width=Math.max(.25,Math.min(40,width));const a=Math.max(0,Math.min(40-width,center-width/2));view=[a,a+width];draw();
}
function focusNote(){
  if(!source)throw Error('请等待加载');
  const n=candidate.notes.find(n=>n.id===selected),a=source.chart.notes.find(n=>n.id===selected);
  if(!n||!a)throw Error('先选择音符；完整片段播放不需要选择音符');
  const before=Number($('before').value),after=Number($('after').value);
  if(!Number.isFinite(before)||!Number.isFinite(after)||before<.05||after<.05||before>10||after>10)throw Error('试听前后范围应在0.05–10秒');
  stop();const start=Math.max(0,Math.min(n.timeMs,a.timeMs)/1000+candidate.chartOffsetMs/1000-before),end=Math.min(40,Math.max(n.timeMs+n.durationMs,a.timeMs+a.durationMs)/1000+candidate.chartOffsetMs/1000+after);
  rangeMode='note';$('start').value=start.toFixed(3);$('end').value=end.toFixed(3);$('loop').checked=true;view=[start,end];position=start;draw();
}
$('undo').onclick=guard(()=>history('undo'));$('redo').onclick=guard(()=>history('redo'));
for(const button of document.querySelectorAll('[data-step]'))button.onclick=guard(()=>adjust(Math.round(candidate.notes.find(n=>n.id===selected).timeMs)+Number(button.dataset.step),false));
for(const [id,delta] of [['prev',-1],['next',1]])$(id).onclick=guard(()=>{const i=candidate.notes.findIndex(n=>n.id===selected);select(candidate.notes[Math.max(0,Math.min(candidate.notes.length-1,i+delta))].id);});
$('focusNote').onclick=guard(focusNote);
$('audition').onclick=guard(async()=>{focusNote();await play();status('循环试听当前音符；A/B共用同一区间');});
$('zoomIn').onclick=guard(()=>setView(source?((candidate.notes.find(n=>n.id===selected).timeMs+candidate.chartOffsetMs)/1000):current(),(view[1]-view[0])/2));
$('zoomOut').onclick=guard(()=>setView((view[0]+view[1])/2,(view[1]-view[0])*2));
$('panLeft').onclick=()=>setView((view[0]+view[1])/2-(view[1]-view[0])/2,view[1]-view[0]);
$('panRight').onclick=()=>setView((view[0]+view[1])/2+(view[1]-view[0])/2,view[1]-view[0]);
$('snap').onchange=()=>status($('snap').checked?'参考吸附已开启；未移动已有音符，BPM/原点未经校准':'吸附关闭，输入/拖动自由调整');
$('voice').onchange=updateHistory;$('reason').oninput=updateHistory;
document.addEventListener('keydown',guard(async e=>{
  if(!source||e.isComposing||e.target.closest('input,textarea,select,[contenteditable="true"]')||e.altKey)return;
  if((e.ctrlKey||e.metaKey)&&e.key.toLowerCase()==='z'){e.preventDefault();await history(e.shiftKey?'redo':'undo');return;}
  if(e.ctrlKey||e.metaKey)return;
  if(e.key==='['||e.key===']'||e.code==='BracketLeft'||e.code==='BracketRight'){
    e.preventDefault();const sign=(e.code==='BracketLeft'||e.key==='[')?-1:1;await adjust(Math.round(candidate.notes.find(n=>n.id===selected).timeMs)+sign*(e.shiftKey?25:10),false);
  }
}));
window.addEventListener('beforeunload',e=>{if(dirty()){e.preventDefault();e.returnValue='';}});
function sourceIdentity(){return {chartPath:source.chartPath,audioPath:source.audioPath,chartSha256:source.chartSha256,audioSha256:source.audioSha256};}
function draftBundle(){
  if(pendingTime())throw Error('时间输入尚未应用；先点击“应用输入”，再保存/下载草稿');
  const b={format:DRAFT_FORMAT,version:DRAFT_VERSION,status:'unreviewed-draft',createdAt:new Date().toISOString(),source:sourceIdentity(),review:{voice:$('voice').value,reason:$('reason').value},timing,chart:clone(candidate),differences:differences(source.chart,candidate)};
  return validateDraft(b,source);
}
async function freshSource(){
  const response=await fetch('/source');if(!response.ok)throw Error(await response.text());const fresh=await response.json();
  if(fresh.chartSha256!==source.chartSha256||fresh.audioSha256!==source.audioSha256)throw Error('磁盘来源已变化，当前草稿保留；先下载备份再处理来源变化');
}
function listDrafts(){
  const selectedKey=$('localDraft').value;$('localDraft').replaceChildren();let stale=0;
  for(let i=0;i<localStorage.length;i++){
    const key=localStorage.key(i);if(!key.startsWith(storagePrefix))continue;
    try{const b=JSON.parse(localStorage.getItem(key));validateDraft(b,source,{checkAge:true});const option=document.createElement('option');option.value=key;option.textContent=new Date(b.createdAt).toLocaleString()+' · '+b.differences.length+'项 · '+(key.endsWith(':main')?'主草稿':'副本');$('localDraft').append(option);}
    catch{stale++;}
  }
  if([...$('localDraft').options].some(o=>o.value===selectedKey))$('localDraft').value=selectedKey;
  return stale;
}
function inspectDrafts(){
  try{
    observedRecord=localStorage.getItem(storageKey);const stale=listDrafts();
    if(observedRecord){try{validateDraft(JSON.parse(observedRecord),source,{checkAge:true});}catch{storageKey=storagePrefix+source.chartSha256+':'+source.audioSha256+':'+crypto.randomUUID();observedRecord=null;}}
    if(stale)status('保留'+stale+'份过期/不同来源/无效记录；不会恢复或覆盖当前编辑',false,'saveStatus');
    if($('localDraft').options.length){
      if([...$('localDraft').options].some(o=>o.value===storageKey))$('localDraft').value=storageKey;
      $('restoreSummary').textContent=$('localDraft').selectedOptions[0].textContent+'。恢复不会自动播放；从原谱开始会另存新副本，旧草稿保留。';
      $('restorePrompt').showModal();
    }
  }catch(e){status('本地保存不可用：'+e.message+'。可使用下载草稿JSON。',true,'saveStatus');}
}
function applyBundle(b){
  stop();commit(clone(b.chart));$('voice').value=b.review.voice;$('reason').value=b.review.reason;select(selected);updateHistory();
}
async function restoreKey(key){
  await freshSource();const raw=localStorage.getItem(key);if(!raw)throw Error('此草稿不存在');
  const b=validateDraft(JSON.parse(raw),source,{checkAge:true});
  if(dirty()&&!confirm('恢复将替换当前未保存候选；可先取消并另存草稿。继续恢复？'))return;
  applyBundle(b);storageKey=key;observedRecord=raw;savedSignature=signature();updateHistory();$('conflict').hidden=true;
  status('已恢复本地草稿 '+new Date(b.createdAt).toLocaleString()+'，未审核',false,'saveStatus');
}
$('resumeSaved').onclick=guard(async()=>{await restoreKey($('localDraft').value);$('restorePrompt').close();},'saveStatus');
$('freshSession').onclick=()=>{storageKey=storagePrefix+source.chartSha256+':'+source.audioSha256+':'+crypto.randomUUID();observedRecord=null;$('restorePrompt').close();status('从原谱开始；已有草稿保留，新修改将另存副本',false,'saveStatus');};
$('restorePrompt').addEventListener('cancel',e=>e.preventDefault());
$('restoreDraft').onclick=guard(()=>restoreKey($('localDraft').value),'saveStatus');
async function saveDraft(){
  await freshSource();
  if(!navigator.locks)throw Error('浏览器缺少安全多页面保存锁；请下载草稿JSON备份');
  await navigator.locks.request('rhythm-draft:'+storageKey,()=>{
  const raw=localStorage.getItem(storageKey);
  if(raw!==observedRecord){$('conflict').hidden=false;throw Error('其他页面更新了这份草稿；选择恢复其他页或另存副本，未覆盖任何记录');}
  const b=draftBundle(),record=JSON.stringify(b);validateDraft(JSON.parse(record),source);localStorage.setItem(storageKey,record);
  if(localStorage.getItem(storageKey)!==record)throw Error('本地保存未确认，请下载JSON备份');
  observedRecord=record;savedSignature=signature();updateHistory();listDrafts();$('localDraft').value=storageKey;$('conflict').hidden=true;
  $('editStatus').textContent='当前修改已保存本地草稿（未审核）';$('editStatus').className='feedback';
  status('已保存本地草稿 '+new Date(b.createdAt).toLocaleTimeString()+' · '+b.differences.length+'项 · 未审核',false,'saveStatus');
  });
}
$('saveDraft').onclick=guard(saveDraft,'saveStatus');
$('saveCopy').onclick=guard(async()=>{storageKey=storagePrefix+source.chartSha256+':'+source.audioSha256+':'+crypto.randomUUID();observedRecord=null;await saveDraft();},'saveStatus');
$('loadRemote').onclick=guard(()=>restoreKey(storageKey),'saveStatus');
window.addEventListener('storage',e=>{if(e.key===storageKey&&e.newValue!==observedRecord){$('conflict').hidden=false;status('其他页面保存了新的草稿；当前候选保留，请选择恢复或另存',true,'saveStatus');}});
$('downloadDraft').onclick=guard(()=>{
  const b=draftBundle(),body=JSON.stringify(b,null,2);validateDraft(JSON.parse(body),source);
  download('chongerfei-unreviewed-draft-v1.json',body,'application/json');
  status('已发起单个草稿JSON下载，不要求声部/依据。请确认浏览器下载完成；本地保存状态未改变。',false,'saveStatus');
},'saveStatus');
$('export').onclick=guard(async()=>{
  if(pendingTime())throw Error('时间输入尚未应用，先点击“应用输入”');
  await freshSource();const b=bundle(),body=JSON.stringify(b,null,2);validateCandidate(JSON.parse(body),source);
  download('chongerfei-candidate-v1.json',body,'application/json');
  status('已发起严格候选JSON下载 · '+b.differences.length+'项 · 未批准，不自动应用',false,'exportStatus');
},'exportStatus');
$('import').onchange=guard(async()=>{
  const file=$('import').files[0];if(!file)return;
  try{
    await freshSource();const raw=JSON.parse(await file.text());
    const b=raw.format===DRAFT_FORMAT?validateDraft(raw,source,{checkAge:true}):validateCandidate(raw,source);
    if(dirty()&&!confirm('导入将替换当前未保存候选。可取消并先保存草稿。继续？'))return;
    applyBundle(b);status('导入成功，来源/结构/差异已验证；尚未保存本地草稿',false,'saveStatus');
  }finally{$('import').value='';}
},'saveStatus');
$('migrate').onclick=guard(()=>{
  if($('oldChartHash').value.trim().toLowerCase()!==source.chartSha256||$('oldAudioHash').value.trim().toLowerCase()!==source.audioSha256)throw Error('粘贴的旧页两个hash不匹配，未改变候选');
  const parts=$('oldDiff').value.trim().split(/[；;]/).map(p=>p.trim()).filter(Boolean),next=clone(source.chart),ids=new Set();
  if(!parts.length)throw Error('请复制旧页差异文字');
  for(const part of parts){
    const m=part.match(/^([^:\s]+):\s*(\d+(?:\.\d+)?)\s*→\s*(\d+(?:\.\d+)?)ms\s*\(Δ\s*(-?\d+(?:\.\d+)?)ms\)$/);
    if(!m)throw Error('差异文字格式不完整，请完整复制旧页底部差异');
    const n=next.notes.find(n=>n.id===m[1]);if(!n||ids.has(n.id)||Math.abs(n.timeMs-Number(m[2]))>1e-6||Math.abs(Number(m[3])-Number(m[2])-Number(m[4]))>.01)throw Error('差异ID、原值或Δ不匹配');
    ids.add(n.id);n.timeMs=Number(m[3]);
  }
  validateChart(next);
  if(dirty()&&!confirm('恢复旧页差异将替换当前未保存候选。继续？'))return;
  stop();commit(next);status('已从粘贴文字恢复'+ids.size+'项，尚未保存；请核对后保存草稿',false,'saveStatus');
},'saveStatus');

function returnFull(){
  stop();$('sourceAudio').pause();rangeMode='full';$('loop').checked=false;$('start').value=0;$('end').value=40;view=[0,40];position=0;renderPlaybackState();draw();
  status('已退出循环，完整片段0–40s待播放；候选和撤销记录未改变');
}
function renderPlaybackState(){
  const [a,b]=rangeMode==='full'?[0,40]:[Number($('start').value),Number($('end').value)];
  $('playRange').value=rangeMode;$('loop').disabled=rangeMode==='full';$('start').disabled=rangeMode==='full';$('end').disabled=rangeMode==='full';
  const label=rangeMode==='full'?'完整游戏片段':rangeMode==='note'?'音符附近 '+(selected||'未选择'):'自定区间';
  $('rangeStatus').textContent=label+' · '+a.toFixed(3)+'–'+b.toFixed(3)+'s / 原曲 '+(16+a).toFixed(3)+'–'+(16+b).toFixed(3)+'s · '+(rangeMode!=='full'&&$('loop').checked?'循环':'播到终点停止')+' · '+(starting?'正在启动音频':playback?'正在播放':'已暂停');
}
$('exitLoop').onclick=guard(returnFull);
$('playFull').onclick=guard(async()=>{returnFull();await play();});
$('stopPlayback').onclick=()=>{stop();$('sourceAudio').pause();position=rangeMode==='full'?0:region()[0];renderPlaybackState();};
$('playRange').onchange=guard(()=>{
  const next=$('playRange').value;
  if(next==='full')returnFull();
  else if(next==='note'){if(!selected){returnFull();throw Error('先选音符，或使用完整片段播放');}focusNote();}
  else{stop();rangeMode='custom';$('start').value=0;$('end').value=40;$('loop').checked=false;position=0;view=[0,40];}
});
$('sourceAudio').addEventListener('play',()=>{stop();status('正在试听整首原音频；独立音频控件计时，谱面范围仍为16–56s。');});

$('load').onclick();
