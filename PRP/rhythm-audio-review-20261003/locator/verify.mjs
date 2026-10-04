// 执行载体：显式 node verify.mjs <已安装 playwright 包路径>；锚点：stdout PASS；工具被替代时删除。
import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import {readFile} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {loadSource} from './server.mjs';
import {validateCandidate,validateChart,clone} from './model.mjs';
const require=createRequire(import.meta.url);
if(!process.argv[2])throw Error('传入现有 playwright 包路径；本工具不安装依赖');
const {chromium}=require(process.argv[2]);
const before=await loadSource(),browser=await chromium.launch({headless:true,channel:'chrome'});
const page=await browser.newPage({viewport:{width:1250,height:1100},acceptDownloads:true}),errors=[];
page.on('pageerror',e=>errors.push(e.message));
const evidence={};
try{
  await page.goto('http://127.0.0.1:8766');
  assert.equal((await page.request.post('http://127.0.0.1:8766/source',{data:'no writes'})).status(),405);
  assert.equal((await page.request.get('http://127.0.0.1:8766/unknown')).status(),404);
  const audio=page.waitForResponse(r=>r.url().endsWith('/audio'));
  await page.locator('#load').click();assert.equal((await audio).status(),200);
  await page.waitForFunction(()=>document.querySelector('#status').textContent.includes('已加载 56'));
  evidence.loaded=await page.locator('#status').textContent();
  assert.equal(await page.locator('#notes tr').count(),56);
  await page.locator('#middle').click();assert.match(await page.locator('#position').textContent(),/18\.900s.*34\.900s/);
  await page.locator('#noteClick').check();await page.locator('#beatClick').check();
  await page.locator('#start').fill('19.8');await page.locator('#end').fill('20.4');
  await page.locator('#range').click();await page.locator('#play').click();
  await page.waitForTimeout(1500);
  const p=Number(await page.locator('#seek').inputValue());assert.ok(p>=19.8&&p<20.4);
  assert.equal(await page.locator('#play').textContent(),'暂停');evidence.loopPosition=p;
  await page.locator('#play').click();
  await page.locator('#seek').evaluate(el=>{el.value='20.1';el.dispatchEvent(new Event('input'));});
  await page.waitForFunction(()=>document.querySelector('#position').textContent.includes('20.100s'));
  evidence.seek=await page.locator('#position').textContent();
  // 原谱/候选各实际播放，以相同循环区间对照。
  await page.locator('#play').click();await page.waitForTimeout(200);await page.locator('#mode').selectOption('candidate');
  await page.waitForTimeout(200);await page.locator('#play').click();
  await page.locator('#note').selectOption('legacy-27');await page.locator('#time').fill('20300');await page.locator('#apply').click();
  assert.match(await page.locator('#diff').textContent(),/legacy-27: 20260 → 20300ms/);
  // 拖动 B 标记：20.300 -> 20.340 秒，随后用数字调回可追踪的 +40ms。
  const box=await page.locator('#timeline').boundingBox();
  const x=t=>box.x+(t-19.8)/.6*box.width,y=box.y+244/280*box.height;
  await page.mouse.move(x(20.3),y);await page.mouse.down();await page.mouse.move(x(20.34),y,{steps:5});await page.mouse.up();
  await page.waitForFunction(()=>document.querySelector('#time').value==='20340');
  evidence.dragTime=await page.locator('#time').inputValue();
  await page.locator('#time').fill('20300');await page.locator('#apply').click();
  // 负例：越界和同轨窗冲突不能改变候选。
  await page.locator('#time').fill('40001');await page.locator('#apply').click();assert.match(await page.locator('#status').textContent(),/超出片段/);
  await page.locator('#time').fill('19100');await page.locator('#apply').click();assert.match(await page.locator('#status').textContent(),/冲突/);
  await page.locator('#export').click();await page.waitForFunction(()=>document.querySelector('#status').textContent.includes('请选择旋律'));assert.match(await page.locator('#status').textContent(),/请选择旋律/);
  await page.locator('#voice').selectOption('melody');await page.locator('#reason').fill('浏览器自动验证候选，未做人耳贴拍验收');
  const downloads=[];page.on('download',d=>downloads.push(d));
  await page.locator('#export').click();await page.waitForFunction(()=>document.querySelector('#status').textContent.includes('已导出'));
  await page.waitForTimeout(200);assert.equal(downloads.length,2);
  const paths=[];for(const d of downloads){const path=join(tmpdir(),`locator-test-${d.suggestedFilename()}`);await d.saveAs(path);paths.push(path);}
  const jsonPath=paths.find(p=>p.endsWith('.json')),bundle=validateCandidate(JSON.parse(await readFile(jsonPath,'utf8')),before);
  assert.equal(bundle.differences.length,1);assert.equal(bundle.differences[0].id,'legacy-27');assert.equal(bundle.differences[0].deltaMs,40);
  assert.match(await readFile(paths.find(p=>p.endsWith('.csv')),'utf8'),/legacy-27/);
  await page.locator('#reset').click();await page.locator('#import').setInputFiles(jsonPath);
  await page.waitForFunction(()=>document.querySelector('#status').textContent.includes('重新解析'));
  assert.match(await page.locator('#diff').textContent(),/20260 → 20300/);
  for(const mutate of [b=>b.version=99,b=>b.source.chartSha256='0'.repeat(64),b=>b.chart.notes[1].id=b.chart.notes[0].id,b=>b.chart.notes[0].lane=4,b=>b.chart.notes[0].timeMs=NaN,b=>b.chart.notes[0].durationMs=1,b=>b.differences=[]]){const invalid=clone(bundle);mutate(invalid);assert.throws(()=>validateCandidate(invalid,before));}
  const overlap=clone(before.chart);overlap.notes[4].timeMs=2300;assert.throws(()=>validateChart(overlap));
  await page.screenshot({path:join(tmpdir(),'rhythm-locator-verified.png'),fullPage:true});
  const after=await loadSource();assert.equal(after.chartSha256,before.chartSha256);assert.equal(after.audioSha256,before.audioSha256);assert.deepEqual(errors,[]);
  evidence.exports=paths;evidence.chartSha256=after.chartSha256;evidence.audioSha256=after.audioSha256;evidence.pageErrors=errors;
  console.log('PASS '+JSON.stringify(evidence,null,2));
}finally{await browser.close();}
