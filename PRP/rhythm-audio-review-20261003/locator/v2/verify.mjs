// 显式 Playwright 验证；PASS 为状态锚点；旧版退役后保留与实际界面配套的测试。
import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import {readFile} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {loadSource} from '../server.mjs';
import {validateCandidate,clone} from '../model.mjs';
import {validateDraft,snapTime} from './draft.mjs';
import {legacyCandidateFixture} from '../test-fixtures.mjs';
const require=createRequire(import.meta.url),{chromium}=require(process.argv[2]);
const before=await loadSource(),browser=await chromium.launch({channel:'chrome',headless:true});
const context=await browser.newContext({viewport:{width:1300,height:1050},acceptDownloads:true}),page=await context.newPage(),errors=[];
let confirmMode='accept';const dialogs=[];
function watch(p){p.on('pageerror',e=>errors.push(e.message));p.on('dialog',async d=>{dialogs.push(d.type());if(confirmMode==='dismiss')await d.dismiss();else await d.accept();});}
watch(page);
const value=()=>page.locator('#time').inputValue();
const waitSaved=()=>page.waitForFunction(()=>document.querySelector('#saveStatus').textContent.includes('已保存本地草稿'));
async function loaded(p){await p.waitForFunction(()=>document.querySelector('#load').textContent.includes('原谱已加载'));}
async function apply(time){await page.locator('#time').fill(String(time));await page.locator('#apply').click();await page.waitForTimeout(60);}
async function draftDownload(){const event=page.waitForEvent('download');await page.locator('#downloadDraft').click();const d=await event,path=join(tmpdir(),'rhythm-v2-'+d.suggestedFilename());await d.saveAs(path);return {path,data:validateDraft(JSON.parse(await readFile(path,'utf8')),before)};}
try{
  const audio=page.waitForResponse(r=>r.url().endsWith('/audio'));await page.goto('http://127.0.0.1:8767');assert.equal((await audio).status(),200);await loaded(page);assert.equal(await page.locator('#notes tr').count(),56);
  await page.locator('#note').selectOption('legacy-27');assert.match(await page.locator('#noteInfo').textContent(),/legacy-27.*lane 3.*20260.*20260/);assert.equal(await page.locator('#play').textContent(),'播放 / 暂停');
  await page.locator('[data-step="10"]').click();assert.equal(await value(),'20270');await page.locator('#undo').click();assert.equal(await value(),'20260');await page.locator('#redo').click();assert.equal(await value(),'20270');
  await page.locator('h1').click();await page.keyboard.press('[');assert.equal(await value(),'20260');await page.keyboard.press('Shift+]');assert.equal(await value(),'20285');
  await page.locator('#time').focus();await page.keyboard.press(']');assert.equal(await value(),'20285');await page.locator('#time').press('Control+z');assert.equal(await value(),'20285');
  await page.locator('#time').fill('20300');await page.locator('#saveDraft').click();await page.waitForFunction(()=>document.querySelector('#saveStatus').textContent.includes('时间输入尚未应用'));assert.equal(await value(),'20300');await page.locator('#time').fill('20285');
  await page.locator('#snap').check();assert.equal(await value(),'20285');await apply(20300);assert.equal(Number(await value()),snapTime(20300,103,0,4));const snap4=await value();await page.locator('#division').selectOption('2');assert.equal(await value(),snap4);await apply(20300);assert.equal(Number(await value()),snapTime(20300,103,0,2));await page.locator('#division').selectOption('4');await page.locator('#snap').uncheck();await apply(20300);assert.equal(await value(),'20300');
  await page.locator('#focusNote').click();const start=Number(await page.locator('#start').inputValue()),end=Number(await page.locator('#end').inputValue());assert.ok(start<20.26&&end>20.3);
  // 实际鼠标拖动 B 音符，再撤销/重做。
  const box=await page.locator('#timeline').boundingBox(),x=t=>box.x+(t-start)/(end-start)*box.width,y=box.y+244/270*box.height;
  await page.mouse.move(x(20.3),y);await page.mouse.down();await page.mouse.move(x(20.34),y,{steps:4});await page.mouse.up();await page.waitForFunction(()=>document.querySelector('#time').value==='20340');await page.locator('#undo').click();assert.equal(await value(),'20300');await page.locator('#redo').click();assert.equal(await value(),'20340');
  await page.locator('#before').fill('0.1');await page.locator('#after').fill('0.2');await page.locator('#noteClick').check();await page.locator('#beatClick').check();await page.locator('#audition').click();await page.waitForTimeout(900);
  const loopP=Number(await page.locator('#seek').inputValue()),loopA=Number(await page.locator('#start').inputValue()),loopB=Number(await page.locator('#end').inputValue());assert.ok(loopP>=loopA&&loopP<loopB);assert.equal(await page.locator('#play').textContent(),'暂停',await page.locator('#editStatus').textContent());
  await page.locator('#mode').selectOption('original');await page.waitForTimeout(120);assert.equal(await page.locator('#play').textContent(),'暂停');await page.locator('#mode').selectOption('candidate');await page.locator('#play').click();
  const oldView=await page.locator('#viewInfo').textContent();await page.locator('#zoomIn').click();assert.notEqual(await page.locator('#viewInfo').textContent(),oldView);await page.locator('#zoomOut').click();
  assert.equal(await page.locator('#voice').inputValue(),'');assert.equal(await page.locator('#reason').inputValue(),'');await page.locator('#saveDraft').click();await waitSaved();
  const saved=await draftDownload();assert.equal(saved.data.status,'unreviewed-draft');assert.deepEqual(saved.data.review,{voice:'',reason:''});assert.equal(saved.data.differences[0].candidateTimeMs,20340);
  await page.reload();await loaded(page);await page.locator('#resumeSaved').click();await page.waitForFunction(()=>!document.querySelector('#restorePrompt').open);await page.locator('#note').selectOption('legacy-27');assert.equal(await value(),'20340');
  await page.locator('#import').setInputFiles(saved.path);await page.waitForFunction(()=>document.querySelector('#saveStatus').textContent.includes('导入成功'));assert.equal(await value(),'20340');
  const bad=clone(saved.data);bad.source.chartSha256='0'.repeat(64);await page.locator('#import').setInputFiles({name:'wrong-source.json',mimeType:'application/json',buffer:Buffer.from(JSON.stringify(bad))});await page.waitForFunction(()=>document.querySelector('#saveStatus').textContent.includes('hash/路径不匹配'));assert.equal(await value(),'20340');
  // 固定小样例验证理由和未批准状态；不依赖实际候选或操作旧tab。
  const research=legacyCandidateFixture(before),fixture={name:'legacy-fixture.json',mimeType:'application/json',buffer:Buffer.from(JSON.stringify(research))};
  await page.locator('[data-step="10"]').click();confirmMode='dismiss';await page.locator('#import').setInputFiles(fixture);await page.waitForTimeout(200);assert.equal(await value(),'20350');assert.ok(dialogs.includes('confirm'));
  await page.locator('#saveDraft').click();await waitSaved();confirmMode='accept';await page.locator('#import').setInputFiles(fixture);await page.waitForFunction(()=>document.querySelector('#voice').value==='melody');assert.equal(await page.locator('#reason').inputValue(),research.review.reason);
  await page.locator('summary').filter({hasText:'严格审核候选'}).click();const researchDownload=page.waitForEvent('download');await page.locator('#export').click();const rd=await researchDownload,rp=join(tmpdir(),'v2-research-roundtrip.json');await rd.saveAs(rp);const roundtrip=validateCandidate(JSON.parse(await readFile(rp,'utf8')),before);assert.equal(roundtrip.status,'unapproved-candidate');assert.deepEqual(roundtrip.review,research.review);assert.deepEqual(roundtrip.differences,research.differences);
  await page.locator('#saveDraft').click();await waitSaved();
  await page.locator('#undo').click();assert.equal(await page.locator('#voice').inputValue(),'');await page.locator('#redo').click();assert.equal(await page.locator('#reason').inputValue(),research.review.reason);await page.locator('#saveDraft').click();await waitSaved();
  // 同浏览器两个隔离测试 tab 的保存冲突。
  const second=await context.newPage();watch(second);await second.goto('http://127.0.0.1:8767');await loaded(second);await second.locator('#resumeSaved').click();await second.waitForFunction(()=>!document.querySelector('#restorePrompt').open);await second.locator('#note').selectOption('legacy-27');await second.locator('[data-step="10"]').click();await second.locator('#saveDraft').click();await second.waitForFunction(()=>document.querySelector('#saveStatus').textContent.includes('已保存本地草稿'));
  await page.waitForFunction(()=>!document.querySelector('#conflict').hidden);await page.locator('#saveDraft').click();await page.waitForFunction(()=>document.querySelector('#saveStatus').textContent.includes('未覆盖任何记录'));await page.locator('#saveCopy').click();await waitSaved();assert.ok(await page.locator('#localDraft option').count()>=2);await second.close();
  const stale=clone(saved.data);stale.createdAt='2000-01-01T00:00:00.000Z';const staleRecord=JSON.stringify(stale);await page.evaluate(raw=>localStorage.setItem('21days-rhythm-draft:v1:expired-test',raw),staleRecord);
  const expiredContext=await browser.newContext();const expiredPage=await expiredContext.newPage();watch(expiredPage);await expiredPage.goto('http://127.0.0.1:8767');await loaded(expiredPage);const expiredKey='21days-rhythm-draft:v1:'+before.chartSha256+':'+before.audioSha256+':main';await expiredPage.evaluate(([key,raw])=>localStorage.setItem(key,raw),[expiredKey,staleRecord]);await expiredPage.reload();await loaded(expiredPage);assert.equal(await expiredPage.locator('#restorePrompt').evaluate(el=>el.open),false);await expiredPage.locator('[data-step="10"]').click();await expiredPage.locator('#saveDraft').click();await expiredPage.waitForFunction(()=>document.querySelector('#saveStatus').textContent.includes('已保存本地草稿'));assert.equal(await expiredPage.evaluate(key=>localStorage.getItem(key),expiredKey),staleRecord);await expiredContext.close();
  await page.reload();await loaded(page);await page.locator('#freshSession').click();await page.locator('#note').selectOption('legacy-27');assert.equal(await value(),'20260');assert.equal(await page.evaluate(()=>localStorage.getItem('21days-rhythm-draft:v1:expired-test')),staleRecord);
  await page.locator('#import').setInputFiles({name:'expired.json',mimeType:'application/json',buffer:Buffer.from(staleRecord)});await page.waitForFunction(()=>document.querySelector('#saveStatus').textContent.includes('超过30天'));assert.equal(await value(),'20260');
  // 空审核导出明确阻止，错误位于导出按钮旁；离页提示在改动后出现。
  await page.locator('summary').filter({hasText:'严格审核候选'}).click();await page.locator('#export').click();await page.waitForFunction(()=>document.querySelector('#exportStatus').textContent.includes('请选择旋律'));
  await page.locator('[data-step="10"]').click();confirmMode='dismiss';await page.reload().catch(()=>{});assert.ok(dialogs.includes('beforeunload'));await page.locator('#saveDraft').click();await waitSaved();confirmMode='accept';
  await page.screenshot({path:join(tmpdir(),'rhythm-locator-v2-verified.png'),fullPage:true});
  const after=await loadSource();assert.equal(after.chartSha256,before.chartSha256);assert.equal(after.audioSha256,before.audioSha256);assert.deepEqual(errors,[]);
  console.log('PASS v2: edit/drag/undo/redo/snap/loop/AB/zoom/empty-review draft/reload/JSON import/hash mismatch/conflict/expiry/keyboard guards/beforeunload/research compatibility; original hashes unchanged; pageErrors=0');
}finally{await browser.close();}
