// 用户显式启动的本机只读工具；GET /source 立即显示 hash；被正式定位工具替代时可删除此目录。
import http from 'node:http';
import {readFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import {fileURLToPath} from 'node:url';
import {resolve,dirname} from 'node:path';
import {parseAsset} from './model.mjs';
const here=dirname(fileURLToPath(import.meta.url)),root=resolve(here,'../../..');
const chartPath='Assets/_Project/Data/Rhythm/ChongErFei.asset',audioPath='Assets/_Project/Audio/Rhythm/ChongErFei.mp3';
const hash=bytes=>createHash('sha256').update(bytes).digest('hex');
const startedAt=new Date().toISOString();
export async function loadSource() {
  const [chartBytes,audioBytes]=await Promise.all([readFile(resolve(root,chartPath)),readFile(resolve(root,audioPath))]);
  return {chartPath,audioPath,chartSha256:hash(chartBytes),audioSha256:hash(audioBytes),chart:parseAsset(chartBytes.toString('utf8'))};
}
export const server=http.createServer(async(req,res)=>{
  try {
    if(req.method!=='GET') {res.writeHead(405);res.end('Read only');return;}
    const route=new URL(req.url,'http://localhost').pathname;
    res.setHeader('Cache-Control','no-store');
    if(route==='/health') {res.setHeader('Content-Type','application/json; charset=utf-8');res.end(JSON.stringify({service:'21days-rhythm-locator',version:1,pid:process.pid,startedAt}));return;}
    if(route==='/source') {res.setHeader('Content-Type','application/json; charset=utf-8');res.end(JSON.stringify(await loadSource()));return;}
    const routes={'/':['index.html','text/html; charset=utf-8'],'/app.mjs':['app.mjs','text/javascript; charset=utf-8'],'/model.mjs':['model.mjs','text/javascript; charset=utf-8'],'/audio':[resolve(root,audioPath),'audio/mpeg']};
    if(!routes[route]) {res.writeHead(404);res.end('Not found');return;}
    const [path,type]=routes[route];res.setHeader('Content-Type',type);res.end(await readFile(resolve(here,path)));
  } catch(error) {res.writeHead(500);res.end(error.message);}
});
if(process.argv[1]===fileURLToPath(import.meta.url)) {
  const port=Number(process.argv[2]||8766);
  if(!Number.isInteger(port)||port<1024||port>65535)throw Error('Port must be an integer from 1024 to 65535.');
  server.on('error',error=>{console.error(error.code==='EADDRINUSE'?`Port ${port} is occupied. No process was stopped. Use launch.cmd to check an existing locator, or choose another port.`:error.message);process.exitCode=1;});
  server.listen(port,'127.0.0.1',()=>console.log(`Rhythm locator: http://127.0.0.1:${port} (Ctrl+C stops)`));
}
