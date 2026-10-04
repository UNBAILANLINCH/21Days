// 显式本机启动；/health 是状态锚点；旧 locator 退役后可将此版本合并，不改旧页源码。
import http from 'node:http';
import {readFile} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
import {dirname,resolve} from 'node:path';
import {loadSource} from '../../server.mjs';
const here=dirname(fileURLToPath(import.meta.url)),root=resolve(here,'../../../../..'),startedAt=new Date().toISOString();
export const server=http.createServer(async(req,res)=>{
  try{
    if(req.method!=='GET'){res.writeHead(405);res.end('Read only');return;}
    const route=new URL(req.url,'http://localhost').pathname;res.setHeader('Cache-Control','no-store');
    if(route==='/health'){res.setHeader('Content-Type','application/json');res.end(JSON.stringify({service:'21days-rhythm-locator-playback-fix',version:2,pid:process.pid,startedAt}));return;}
    if(route==='/source'){res.setHeader('Content-Type','application/json; charset=utf-8');res.end(JSON.stringify(await loadSource()));return;}
    const paths={'/':['index.html','text/html; charset=utf-8'],'/app.mjs':['app.mjs','text/javascript; charset=utf-8'],'/draft.mjs':['../draft.mjs','text/javascript; charset=utf-8'],'/model.mjs':['../../model.mjs','text/javascript; charset=utf-8'],'/base-model.mjs':['../../model.mjs','text/javascript; charset=utf-8'],'/audio':[resolve(root,'Assets/_Project/Audio/Rhythm/ChongErFei.mp3'),'audio/mpeg']};
    if(!paths[route]){res.writeHead(404);res.end('Not found');return;}
    const [path,type]=paths[route];res.setHeader('Content-Type',type);res.end(await readFile(resolve(here,path)));
  }catch(e){res.writeHead(500);res.end(e.message);}
});
if(process.argv[1]===fileURLToPath(import.meta.url)){
  const port=Number(process.argv[2]||8768);if(!Number.isInteger(port)||port<1024||port>65535)throw Error('Invalid port');
  await loadSource();
  server.on('error',e=>{console.error(e.code==='EADDRINUSE'?`Port ${port} is occupied; no process stopped. Choose another port.`:e.message);process.exitCode=1;});
  server.listen(port,'127.0.0.1',()=>console.log(`READY http://127.0.0.1:${port} | PID ${process.pid}\nKeep this window open. Existing ports 8766/8767 are untouched.`));
}
