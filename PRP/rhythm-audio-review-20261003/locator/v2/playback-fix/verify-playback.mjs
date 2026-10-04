// 显式真实按钮回归；PASS为状态锚点；与播放范围入口同步保留。
import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import {loadSource} from '../../server.mjs';
const require=createRequire(import.meta.url),{chromium}=require(process.argv[2]);
const before=await loadSource(),browser=await chromium.launch({channel:'chrome',headless:true}),page=await browser.newPage({viewport:{width:1450,height:1100}}),errors=[];
page.on('pageerror',e=>errors.push(e.message));page.on('dialog',d=>d.accept());
const click=id=>page.locator('#'+id).click();
async function full(paused=true){await page.waitForFunction(expected=>document.querySelector('#play').textContent===expected,paused?'播放 / 暂停':'暂停');assert.equal(await page.locator('#playRange').inputValue(),'full');assert.equal(await page.locator('#loop').isChecked(),false);assert.equal(await page.locator('#start').inputValue(),'0');assert.equal(await page.locator('#end').inputValue(),'40');assert.equal(await page.locator('#play').textContent(),paused?'播放 / 暂停':'暂停',await page.locator('#editStatus').textContent());}
async function seek(t){await page.locator('#seek').evaluate((el,value)=>{el.value=String(value);el.dispatchEvent(new Event('input'));},t);}
async function selected(id){await page.locator('#note').selectOption(id);}
try{
  await page.goto('http://127.0.0.1:8768');await page.waitForFunction(()=>document.querySelector('#load').textContent.includes('原谱已加载'));await selected('legacy-27');await page.locator('[data-step="25"]').click();assert.equal(await page.locator('#time').inputValue(),'20285');
  await click('saveDraft');await page.waitForFunction(()=>document.querySelector('#saveStatus').textContent.includes('已保存本地草稿'));const saved=await page.evaluate(()=>localStorage.getItem(Object.keys(localStorage).find(k=>k.endsWith(':main'))));
  // 完整片段→音符附近→退出→完整片段，用真实按钮。
  await click('playFull');await full(false);await page.waitForFunction(()=>Number(document.querySelector('#seek').value)>0);
  await click('audition');assert.equal(await page.locator('#playRange').inputValue(),'note');assert.equal(await page.locator('#loop').isChecked(),true);await click('exitLoop');await full();assert.equal(await page.locator('#time').inputValue(),'20285');
  await click('playFull');await page.waitForTimeout(100);await full(false);await click('stopPlayback');await full();assert.equal(Number(await page.locator('#seek').inputValue()),0);
  // 暂停/AB/换note/修改/重复试听后，仍能退出。
  await click('audition');await click('play');assert.equal(await page.locator('#play').textContent(),'播放 / 暂停');await click('exitLoop');await full();
  await click('audition');await page.locator('#mode').selectOption('original');await page.waitForTimeout(80);await page.locator('#mode').selectOption('candidate');await selected('legacy-28');assert.equal(await page.locator('#play').textContent(),'播放 / 暂停');assert.match(await page.locator('#rangeStatus').textContent(),/legacy-28/);
  await page.locator('[data-step="10"]').click();assert.equal(await page.locator('#time').inputValue(),'20850');await click('audition');await click('audition');assert.equal(await page.locator('#play').textContent(),'暂停');await click('all');await full();assert.equal(await page.locator('#time').inputValue(),'20850');
  await click('undo');assert.equal(await page.locator('#time').inputValue(),'20840');await click('redo');assert.equal(await page.locator('#time').inputValue(),'20850');await selected('legacy-27');assert.equal(await page.locator('#time').inputValue(),'20285');
  // 已保存副本与修改历史未被范围操作清空。
  assert.equal(await page.evaluate(()=>localStorage.getItem(Object.keys(localStorage).find(k=>k.endsWith(':main')))),saved);assert.match(await page.locator('#diff').textContent(),/20285/);assert.match(await page.locator('#diff').textContent(),/20850/);
  // 最后note，区间到40；关闭循环后在40正确结束。
  await selected('legacy-55');await page.locator('#after').fill('5');await click('audition');assert.equal(Number(await page.locator('#end').inputValue()),40);await page.locator('#loop').uncheck();await seek(39.85);await page.waitForTimeout(450);assert.equal(await page.locator('#play').textContent(),'播放 / 暂停');assert.equal(Number(await page.locator('#seek').inputValue()),40);await click('exitLoop');await full();
  // 未选择note也能完整播放；请求note模式保持明确完整状态。
  await selected('');assert.equal(await page.locator('#audition').isDisabled(),true);await click('playFull');await page.waitForTimeout(100);await full(false);await page.locator('#playRange').selectOption('note');await full();assert.match(await page.locator('#editStatus').textContent(),/先选音符/);
  // 自定循环、停止、退出；以及拖出窄区间自动回到完整模式。
  await page.locator('#playRange').selectOption('custom');await page.locator('#start').fill('0.1');await page.locator('#end').fill('0.3');await page.locator('#loop').check();await click('range');await click('play');await page.waitForTimeout(550);const p=Number(await page.locator('#seek').inputValue());assert.ok(p>=.1&&p<.3);await click('stopPlayback');assert.equal(Number(await page.locator('#seek').inputValue()),.1);await click('play');await seek(2);await full(false);await click('exitLoop');await full();
  // 整首原音频独立控件，不扩展谱面；返回完整片段会暂停它。
  await page.locator('#sourceAudio').evaluate(el=>el.play());await page.waitForTimeout(200);assert.equal(await page.locator('#sourceAudio').evaluate(el=>el.paused),false);assert.ok(await page.locator('#sourceAudio').evaluate(el=>el.duration)>160);await click('playFull');await full(false);assert.equal(await page.locator('#sourceAudio').evaluate(el=>el.paused),true);
  // 完整片段终点正确结束，下一次完整播放从0开始。
  await seek(39.9);await page.waitForTimeout(400);await full();assert.equal(Number(await page.locator('#seek').inputValue()),40);await click('playFull');await page.waitForTimeout(120);assert.ok(Number(await page.locator('#seek').inputValue())<1);await click('stopPlayback');
  await selected('legacy-27');assert.equal(await page.locator('#time').inputValue(),'20285');await selected('legacy-28');assert.equal(await page.locator('#time').inputValue(),'20850');assert.equal(await page.locator('#undo').isDisabled(),false);
  const after=await loadSource();assert.equal(after.chartSha256,before.chartSha256);assert.equal(after.audioSha256,before.audioSha256);assert.deepEqual(errors,[]);
  console.log('PASS real-button playback regression: full→note→exit→full; pause/stop/AB/change/edit/repeat; last-note/end/no-note/custom/out-of-range seek/full-source; candidate/draft/undo/source hashes retained; pageErrors=0');
}finally{await browser.close();}
