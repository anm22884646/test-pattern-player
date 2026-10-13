const {app,BrowserWindow,ipcMain,dialog,screen,protocol,net,Menu,powerSaveBlocker}=require('electron');
const path=require('node:path');
const fs=require('node:fs');
const {pathToFileURL}=require('node:url');
const crypto=require('node:crypto');
const {Readable}=require('node:stream');
protocol.registerSchemesAsPrivileged([{scheme:'pattern',privileges:{standard:true,secure:true,supportFetchAPI:true,stream:true}}]);
app.commandLine.appendSwitch('autoplay-policy','no-user-gesture-required');
let control,output,selected,full=false,blocker;
const files=new Map();
function media(file,name){if(!fs.existsSync(file))throw Error('Media file missing: '+name);const id=crypto.randomUUID();files.set(id,file);return {name,url:'pattern://media/'+id};}
function displays(){return screen.getAllDisplays().map((d,i)=>({id:d.id,name:`画面 ${i+1} · ${d.label||''} ${d.size.width} × ${d.size.height}`,primary:d.id===screen.getPrimaryDisplay().id}));}
function send(channel,data){if(control&&!control.isDestroyed())control.webContents.send(channel,data);}
function position(){const d=screen.getAllDisplays().find(d=>d.id===selected)||screen.getPrimaryDisplay();selected=d.id;
 if(full){output.setBounds(d.bounds);output.setAlwaysOnTop(d.id!==screen.getPrimaryDisplay().id,'screen-saver');}
 else {output.setAlwaysOnTop(false);output.setBounds({x:d.workArea.x+30,y:d.workArea.y+30,width:Math.min(640,d.workArea.width),height:Math.min(360,d.workArea.height)});}
 control.setAlwaysOnTop(d.id===screen.getPrimaryDisplay().id,'floating');send('displays',{items:displays(),selected,fullscreen:full});}
function trusted(event,window){return window&&!window.isDestroyed()&&event.sender===window.webContents;}
function create(){const primary=screen.getPrimaryDisplay();selected=(screen.getAllDisplays().find(d=>d.id!==primary.id)||primary).id;
 const prefs={preload:path.join(__dirname,'preload.cjs'),contextIsolation:true,nodeIntegration:false,sandbox:true,backgroundThrottling:false};
 control=new BrowserWindow({width:860,height:800,minWidth:620,minHeight:550,title:'Pattern Player',backgroundColor:'#101419',webPreferences:prefs});
 control.center();output=new BrowserWindow({width:640,height:360,title:'Pattern Player Output',frame:false,backgroundColor:'#000000',webPreferences:prefs});
 for(const w of [control,output]){w.webContents.setWindowOpenHandler(()=>({action:'deny'}));w.webContents.on('will-navigate',e=>e.preventDefault());w.webContents.on('context-menu',e=>e.preventDefault());w.webContents.session.setPermissionRequestHandler((_wc,permission,cb)=>cb(permission==='speaker-selection'));}
 output.on('close',e=>{if(!app.isQuitting){e.preventDefault();output.hide();send('output-hidden',true);}});
 control.on('closed',()=>app.quit());control.loadFile(path.join(__dirname,'control.html'));output.loadFile(path.join(__dirname,'output.html'));position();
 output.webContents.on('before-input-event',(_e,input)=>{if(input.type==='keyDown'&&input.key==='Escape')setFullscreen(false);});
}
function setFullscreen(value){full=!!value;output.show();if(process.platform==='darwin'){if(output.isSimpleFullScreen())output.setSimpleFullScreen(false);position();if(full)output.setSimpleFullScreen(true);}else{output.setFullScreen(full);position();}position();control.focus();return full;}
app.whenReady().then(()=>{
 protocol.handle('pattern',request=>{
 const id=new URL(request.url).pathname.slice(1),file=files.get(id);if(!file)return new Response('Not found',{status:404});
 const size=fs.statSync(file).size,range=request.headers.get('range');let start=0,end=size-1,status=200;
 const headers={'Content-Type':({'.mp4':'video/mp4','.m4v':'video/mp4','.mov':'video/quicktime','.webm':'video/webm','.mp3':'audio/mpeg'})[path.extname(file).toLowerCase()]||'application/octet-stream','Accept-Ranges':'bytes'};
 if(range){const match=/^bytes=(\d*)-(\d*)$/.exec(range);if(!match||(!match[1]&&!match[2]))return new Response(null,{status:416,headers:{'Content-Range':`bytes */${size}`}});if(match[1]){start=Number(match[1]);if(match[2])end=Math.min(Number(match[2]),end);}else{start=Math.max(0,size-Number(match[2]));}if(start>=size||end<start)return new Response(null,{status:416,headers:{'Content-Range':`bytes */${size}`}});status=206;headers['Content-Range']=`bytes ${start}-${end}/${size}`;}
 headers['Content-Length']=String(end-start+1);return new Response(request.method==='HEAD'?null:Readable.toWeb(fs.createReadStream(file,{start,end})),{status,headers});
 });
 create();Menu.setApplicationMenu(Menu.buildFromTemplate([{label:'Pattern Player',submenu:[{role:'about'},{type:'separator'},{role:'quit'}]},{label:'表示',submenu:[{role:'minimize'}]}]));
 ipcMain.handle('init',event=>{if(!trusted(event,control))return;return {displays:{items:displays(),selected,fullscreen:full},patterns:['FHD_TestPattern.mp4','UHD_TestPattern.mp4','FHD_MotionColorBars.mp4','UHD_MotionColorBars.mp4'].map(n=>media(path.join(__dirname,'TestPatterns',n),n)),audio:media(path.join(__dirname,'Audio','TestingBeat.mp3'),'Testing Beat')};});
 ipcMain.handle('open',async event=>{if(!trusted(event,control))return;const result=await dialog.showOpenDialog(control,{title:'動画を開く',properties:['openFile'],filters:[{name:'動画',extensions:['mp4','mov','m4v','webm']}]});if(result.canceled)return null;return media(result.filePaths[0],path.basename(result.filePaths[0]));});
 ipcMain.handle('display',(event,id)=>{if(!trusted(event,control))return;if(!screen.getAllDisplays().some(d=>d.id===id))throw Error('画面が見つかりません');selected=id;setFullscreen(full);return displays();});
 ipcMain.handle('fullscreen',(event,value)=>trusted(event,control)?setFullscreen(value):false);
 ipcMain.on('command',(event,data)=>{if(!trusted(event,control)||!['load','play','pause','seek','loop'].includes(data?.type))return;if(data.type==='load'&&!files.has(new URL(data.media.url).pathname.slice(1)))return;output.show();output.webContents.send('command',data);});
 ipcMain.on('status',(event,data)=>{if(!trusted(event,output))return;send('status',data);if(data.playing&&blocker===undefined)blocker=powerSaveBlocker.start('prevent-display-sleep');if(!data.playing&&blocker!==undefined){powerSaveBlocker.stop(blocker);blocker=undefined;}});
 const changed=()=>{const removed=!screen.getAllDisplays().some(d=>d.id===selected);if(removed){output.webContents.send('command',{type:'pause'});full=false;if(process.platform==='darwin')output.setSimpleFullScreen(false);else output.setFullScreen(false);}position();};
 screen.on('display-added',changed);screen.on('display-removed',changed);screen.on('display-metrics-changed',changed);
 if(process.argv.includes('--smoke-test'))require('./smoke.cjs')({app,control,output,media});
}).catch(error=>{console.error(error);app.exit(1);});
app.on('before-quit',()=>{app.isQuitting=true;if(blocker!==undefined)powerSaveBlocker.stop(blocker);});
app.on('window-all-closed',()=>app.quit());
