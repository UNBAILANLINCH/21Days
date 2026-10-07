// 用户显式双击 launch.cmd；/health 可在 5 秒内查 PID；正式工具替代后删除本目录。
// 原 server.mjs 依赖调用方保留终端，不能提供一键启动/安全复用，故增加此入口。
import {spawn} from 'node:child_process';
import {server,loadSource} from './server.mjs';
const port=Number(process.argv[2]||8766),url=`http://127.0.0.1:${port}`;
function openBrowser(){
  const child=spawn('cmd.exe',['/d','/c','start','""',url],{detached:true,stdio:'ignore',windowsHide:true});
  child.on('error',error=>console.error(`Browser could not open: ${error.message}. Open ${url} manually.`));child.unref();
}
async function main(){
  if(!Number.isInteger(port)||port<1024||port>65535)throw Error('Port must be an integer from 1024 to 65535.');
  const source=await loadSource();
  try{
    await new Promise((resolve,reject)=>{server.once('error',reject);server.listen(port,'127.0.0.1',()=>{server.removeListener('error',reject);resolve();});});
  }catch(error){
    if(error.code!=='EADDRINUSE')throw error;
    let health,existing;
    try{
      const h=await fetch(`${url}/health`,{signal:AbortSignal.timeout(2000)});health=await h.json();
      if(!h.ok||health.service!=='21days-rhythm-locator'||health.version!==1)throw Error('Not this tool');
      const s=await fetch(`${url}/source`,{signal:AbortSignal.timeout(3000)});existing=await s.json();
      if(!s.ok||existing.chartSha256!==source.chartSha256||existing.audioSha256!==source.audioSha256)throw Error('Source mismatch');
    }catch{throw Error(`Port ${port} is occupied by an unverified service. No process was stopped. Close the known owner yourself, or run launch.cmd 8767 and use port 8767.`);}
    console.log(`Existing locator verified: ${url} (PID ${health.pid}). Keep its server window open.`);openBrowser();return;
  }
  console.log(`READY ${url} | PID ${process.pid} | ${new Date().toISOString()}`);
  console.log(`Source OK: ${source.chart.notes.length} notes | chart SHA-256 ${source.chartSha256}`);
  console.log('Keep this window open. Ctrl+C or closing this window stops the local server. No autostart service is installed.');
  server.on('error',error=>console.error(`Server error: ${error.message}`));
  openBrowser();
}
main().catch(error=>{console.error(`ERROR: ${error.message}`);process.exitCode=1;});
