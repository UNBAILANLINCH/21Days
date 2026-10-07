import {clone,validateChart,validateCandidate,differences,FORMAT,VERSION} from './model.mjs';
const $=id=>document.getElementById(id),keys=['D','F','J','K'];
let source,candidate,ctx,buffer,position=0,view=[0,40],selected,playback,nodes=new Set(),timer,drag,waveCache;
const status=(message,error=false)=>{$('status').textContent=message;$('status').className=error?'error':'';};
const guard=fn=>async(...args)=>{try{await fn(...args);}catch(e){status(e.message,true);}};
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
  position=current();playback=null;clearInterval(timer);
  for(const node of nodes){try{node.stop();}catch{} node.disconnect();} nodes.clear();
  $('play').textContent='播放 / 暂停';
}
function clickAt(when,frequency){
  const oscillator=ctx.createOscillator(),gain=ctx.createGain();oscillator.frequency.value=frequency;
  gain.gain.setValueAtTime(.12,when);gain.gain.exponentialRampToValueAtTime(.0001,when+.025);
  oscillator.connect(gain);gain.connect(ctx.destination);nodes.add(oscillator);
  oscillator.onended=()=>{nodes.delete(oscillator);oscillator.disconnect();gain.disconnect();};
  oscillator.start(when);oscillator.stop(when+.03);
}
function schedule(){
  if(!playback)return;
  const p=playback,now=ctx.currentTime;
  if(ctx.state!=='running'){stop();status('AudioContext 已暂停，请重新试听',true);return;}
  if(!p.events.length)return;
  // 音频自身 sample 循环；JS 仅提前预约 click。后台限流时拒绝补发迟到 click。
  for(let count=0;count<200;count++){
    if(p.index>=p.events.length){if(!p.loop)return;p.cycle++;p.index=0;}
    const event=p.events[p.index];if(!event)return;
    const time=p.anchor+(event.t-p.from)+p.cycle*(p.b-p.a);
    if(time>now+.2)return;
    p.index++;
    if(time<now){stop();status('click 调度错过时间，已暂停；请保持页面前台并重新播放',true);return;}
    clickAt(time,event.frequency);
  }
}
async function play(){
  if(!buffer)throw Error('先加载音频');
  await ctx.resume();
  const loop=$('loop').checked,[a,b]=loop?region():[0,40];
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
  const index=events.findIndex(e=>e.t>=from);
  playback={from,anchor,a,b,loop,events,index:index<0?events.length:index,cycle:0};
  nodes.add(audio);audio.onended=()=>{nodes.delete(audio);audio.disconnect();};
  audio.start(anchor,16+from);if(!loop)audio.stop(anchor+b-from);
  $('play').textContent='暂停';schedule();timer=setInterval(schedule,25);
}
async function seek(value){const resume=!!playback;stop();position=Math.min(40,Math.max(0,value));if(resume)await play();draw();}
async function restart(){if(playback){stop();await play();}}
function select(id){selected=id;$('note').value=id;const a=source.chart.notes.find(n=>n.id===id),b=candidate.notes.find(n=>n.id===id);$('time').value=b.timeMs;$('noteInfo').textContent=`lane ${a.lane} (${keys[a.lane]}) · A ${a.timeMs}ms · B 原曲 ${(16+(b.timeMs+candidate.chartOffsetMs)/1000).toFixed(3)}s`;renderRows();}
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
async function adjust(value){
  if(!Number.isFinite(value)||!Number.isInteger(value))throw Error('临时调整须为整数毫秒');
  const next=clone(candidate);next.notes.find(n=>n.id===selected).timeMs=value;validateChart(next);candidate=next;select(selected);await restart();status('临时候选已更新；原谱未修改');
}
function draw(){
  const canvas=$('timeline'),g=canvas.getContext('2d'),w=canvas.width,h=canvas.height,[a,b]=view,x=t=>(t-a)/(b-a)*w;
  g.clearRect(0,0,w,h);g.font='12px system-ui';
  const interval=b-a>10?5:.5;
  for(let t=Math.ceil(a/interval)*interval;t<=b;t+=interval){g.strokeStyle='#34465e';g.beginPath();g.moveTo(x(t),0);g.lineTo(x(t),h);g.stroke();g.fillStyle='#acbfd5';g.fillText(`${t.toFixed(1)}s / ${(16+t).toFixed(1)}s`,x(t)+4,15);}
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
  const p=current();g.strokeStyle='#fff';g.beginPath();g.moveTo(x(p),0);g.lineTo(x(p),h);g.stroke();
  $('seek').value=p;$('position').textContent=`片段 ${p.toFixed(3)}s / 原曲 ${(16+p).toFixed(3)}s`;
}
function animate(){if(playback&&!playback.loop&&current()>=playback.b)stop();draw();requestAnimationFrame(animate);}
$('load').onclick=guard(async()=>{
  stop();status('加载并解码 MP3…');
  const response=await fetch('/source');if(!response.ok)throw Error(await response.text());
  const nextSource=await response.json();validateChart(nextSource.chart);
  ctx ||= new AudioContext();const audioResponse=await fetch('/audio');if(!audioResponse.ok)throw Error('音频读取失败');
  const bytes=await audioResponse.arrayBuffer();
  const decodedHash=Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',bytes)),b=>b.toString(16).padStart(2,'0')).join('');
  if(decodedHash!==nextSource.audioSha256)throw Error('音频读取期间发生变更，请重新加载');
  buffer=await ctx.decodeAudioData(bytes);if(buffer.duration<56)throw Error('音频长度不足 56s');
  source=nextSource;candidate=clone(source.chart);position=0;view=[0,40];
  for(const id of ['play','middle','seek','range','all','note','apply','reset','export','import'])$(id).disabled=false;
  $('note').replaceChildren(...candidate.notes.map(n=>{const option=document.createElement('option');option.value=n.id;option.textContent=n.id;return option;}));select(candidate.notes[0].id);
  $('hash').textContent=`原谱 SHA-256 ${source.chartSha256} · 音频 SHA-256 ${source.audioSha256}`;
  status(`已加载 ${candidate.notes.length} 音符 · MP3 ${buffer.duration.toFixed(3)}s · 解码 ${buffer.sampleRate}Hz`);
});
$('play').onclick=guard(async()=>{if(playback)stop();else await play();});
$('middle').onclick=guard(async()=>{stop();$('start').value=18.9;$('end').value=21.5;$('loop').checked=true;view=[18.9,21.5];await seek(18.9);select('legacy-26');});
$('seek').oninput=guard(()=>seek(Number($('seek').value)));
$('range').onclick=guard(()=>{view=region();});$('all').onclick=()=>{view=[0,40];};
for(const id of ['mode','loop','start','end','noteClick','beatClick','bpm','beatOrigin'])$(id).onchange=guard(restart);
$('note').onchange=()=>select($('note').value);$('apply').onclick=guard(()=>adjust(Number($('time').value)));
$('reset').onclick=guard(async()=>{candidate=clone(source.chart);select(selected);await restart();status('已还原全部临时候选');});
function bundle(){
  const data={format:FORMAT,version:VERSION,status:'unapproved-candidate',createdAt:new Date().toISOString(),source:{chartPath:source.chartPath,audioPath:source.audioPath,chartSha256:source.chartSha256,audioSha256:source.audioSha256},review:{voice:$('voice').value,reason:$('reason').value},timing:{domain:'decoded-project-mp3',songStartSeconds:16,clipDurationSeconds:40,decoderCorrectionMs:0},chart:clone(candidate),differences:differences(source.chart,candidate)};
  validateCandidate(data,source);return data;
}
function download(name,body,type){const url=URL.createObjectURL(new Blob([body],{type})),a=document.createElement('a');a.href=url;a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);}
$('export').onclick=guard(async()=>{
  const fresh=await (await fetch('/source')).json();if(fresh.chartSha256!==source.chartSha256||fresh.audioSha256!==source.audioSha256)throw Error('磁盘原谱/音频已变更，重新加载后再审核');
  const data=bundle(),json=JSON.stringify(data,null,2);validateCandidate(JSON.parse(json),source);
  download('chongerfei-candidate-v1.json',json,'application/json');
  const columns=['id','lane','originalTimeMs','candidateTimeMs','deltaMs','originalSongSeconds','candidateSongSeconds'];
  download('chongerfei-candidate-diff.csv',columns.join(',')+'\n'+data.differences.map(n=>columns.map(k=>JSON.stringify(n[k])).join(',')).join('\n'),'text/csv');
  status(`已导出并重新解析验证 ${data.differences.length} 项差异；仍为未批准候选`);
});
$('import').onchange=guard(async()=>{
  const file=$('import').files[0];if(!file)return;
  try{const fresh=await (await fetch('/source')).json();if(fresh.chartSha256!==source.chartSha256||fresh.audioSha256!==source.audioSha256)throw Error('磁盘来源已变更，先重新加载');const data=validateCandidate(JSON.parse(await file.text()),source);stop();candidate=clone(data.chart);$('voice').value=data.review.voice;$('reason').value=data.review.reason;select(selected);status('候选已重新解析并通过 hash / 音符 / 差异校验');}finally{$('import').value='';}
});
function pointerTime(e){const r=$('timeline').getBoundingClientRect();return view[0]+Math.min(1,Math.max(0,(e.clientX-r.left)/r.width))*(view[1]-view[0]);}
$('timeline').onpointerdown=guard(async e=>{
  if(!source)return;const r=$('timeline').getBoundingClientRect(),t=pointerTime(e),y=(e.clientY-r.top)/r.height*280;
  const chart=active(),offset=$('mode').value==='candidate'?8:0;
  const near=chart.notes.find(n=>Math.abs((n.timeMs+chart.chartOffsetMs)/1000-t)<(view[1]-view[0])*8/r.width && y>=123+n.lane*35+offset && y<=143+n.lane*35+offset);
  if(near){select(near.id);if($('mode').value==='candidate'){drag={id:near.id,time:near.timeMs,resume:!!playback};stop();$('timeline').setPointerCapture(e.pointerId);}}else await seek(t);
});
$('timeline').onpointermove=e=>{if(drag)$('time').value=Math.round(pointerTime(e)*1000-candidate.chartOffsetMs);};
$('timeline').onpointerup=guard(async()=>{if(!drag)return;const previous=drag;drag=null;try{await adjust(Number($('time').value));}catch(e){select(previous.id);status(e.message,true);}if(previous.resume)await play();});
$('timeline').onpointercancel=()=>{if(drag){select(drag.id);drag=null;}};
document.addEventListener('visibilitychange',()=>{if(document.hidden&&playback){stop();status('页面隐藏，已暂停');}});
window.addEventListener('blur',()=>{if(playback){stop();status('页面失焦，已暂停');}});
window.addEventListener('pagehide',stop);
animate();
