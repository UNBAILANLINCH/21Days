// 显式启动验证；stdout PASS 为状态锚点；启动器被替代后删除。
import assert from 'node:assert/strict';
import http from 'node:http';
import {spawn} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import {tmpdir} from 'node:os';
const launcher=fileURLToPath(new URL('./launch.mjs',import.meta.url));
async function run(port){
  return new Promise((resolve,reject)=>{
    const child=spawn(process.execPath,[launcher,String(port)],{cwd:tmpdir(),windowsHide:true});let output='';
    child.stdout.on('data',data=>output+=data);child.stderr.on('data',data=>output+=data);
    child.on('error',reject);child.on('exit',code=>resolve({code,output}));
  });
}
const before=await (await fetch('http://127.0.0.1:8766/health')).json();
const reused=await run(8766);assert.equal(reused.code,0);assert.match(reused.output,/Existing locator verified/);
const after=await (await fetch('http://127.0.0.1:8766/health')).json();assert.equal(after.pid,before.pid);
const invalid=await run(80);assert.equal(invalid.code,1);assert.match(invalid.output,/Port must be an integer/);
const fixture=http.createServer((req,res)=>res.end('unrelated-test-service'));
await new Promise(resolve=>fixture.listen(0,'127.0.0.1',resolve));const port=fixture.address().port;
try{
  const conflict=await run(port);assert.equal(conflict.code,1);assert.match(conflict.output,/occupied by an unverified service/);
  assert.equal(await (await fetch(`http://127.0.0.1:${port}`)).text(),'unrelated-test-service');
  console.log(`PASS launcher: different cwd, verified reuse PID ${after.pid}, invalid port, unknown conflict preserved`);
}finally{await new Promise(resolve=>fixture.close(resolve));}
