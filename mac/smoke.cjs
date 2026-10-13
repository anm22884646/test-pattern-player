const path=require('node:path');const fs=require('node:fs');
module.exports=async({app,control,output,media})=>{
 const delay=ms=>new Promise(r=>setTimeout(r,ms));
 const waitLoaded=w=>new Promise(r=>{if(!w.webContents.isLoading())r();else w.webContents.once('did-finish-load',r);});
 const results=[];
 try{
 await Promise.all([waitLoaded(control),waitLoaded(output)]);await delay(1200);
 const buttons=await control.webContents.executeJavaScript("document.querySelectorAll('#patterns button').length");if(buttons!==4)throw Error('Built-in pattern controls missing');
 const screenCount=await control.webContents.executeJavaScript("document.querySelectorAll('#screens button').length");if(screenCount<1)throw Error('Display controls missing');
 results.push({test:'controls',patterns:buttons,displays:screenCount});
 for(const name of ['FHD_TestPattern.mp4','UHD_TestPattern.mp4','FHD_MotionColorBars.mp4','UHD_MotionColorBars.mp4']){
 const m=media(path.join(__dirname,'TestPatterns',name),name);output.webContents.send('command',{type:'load',media:m,loop:true});
 let state;
 for(let i=0;i<80;i++){await delay(200);state=await output.webContents.executeJavaScript('({ready,playing,error,width:active.videoWidth,height:active.videoHeight})');if(state.error)throw Error(state.error);if(state.ready)break;}
 if(!state.ready||!state.width)throw Error('Decode failed: '+name);
 output.webContents.send('command',{type:'play'});await delay(700);
 const t=await output.webContents.executeJavaScript('active.currentTime');if(t<=0)throw Error('Playback did not advance');
 output.webContents.send('command',{type:'pause'});await delay(150);const paused=await output.webContents.executeJavaScript('active.currentTime');await delay(300);const after=await output.webContents.executeJavaScript('active.currentTime');if(Math.abs(after-paused)>0.05)throw Error('Pause failed');
 output.webContents.send('command',{type:'seek',time:1.5});let sought;
 for(let i=0;i<30;i++){await delay(100);sought=await output.webContents.executeJavaScript('({time:active.currentTime,seeking:active.seeking,seekable:active.seekable.length})');if(!sought.seeking&&Math.abs(sought.time-1.5)<0.1)break;}
 if(sought.seeking||Math.abs(sought.time-1.5)>0.1)throw Error('Seek failed: '+JSON.stringify(sought));
 output.webContents.send('command',{type:'play'});await delay(150);const id=await output.webContents.executeJavaScript('active.id');await output.webContents.executeJavaScript('active.currentTime=active.duration-0.035');await delay(1500);const swapped=await output.webContents.executeJavaScript('({id:active.id,playing,time:active.currentTime,error})');if(swapped.id===id||!swapped.playing||swapped.time>3)throw Error('Loop swap failed: '+JSON.stringify(swapped));
 results.push({test:name,width:state.width,height:state.height,play:true,pause:true,seek:true,loop:true});
 }
 await control.webContents.executeJavaScript("document.getElementById('audio').muted=true;document.getElementById('audioCheck').checked=true;playing=true;audioSync()");await delay(800);
 const audio=await control.webContents.executeJavaScript("({time:document.getElementById('audio').currentTime,paused:document.getElementById('audio').paused,sink:typeof document.getElementById('audio').setSinkId,devices:document.getElementById('audioOutput').options.length})");if(audio.time<=0||audio.paused)throw Error('Audio Check failed');
 output.webContents.send('command',{type:'pause'});await delay(300);const audioPaused=await control.webContents.executeJavaScript("document.getElementById('audio').paused");if(!audioPaused)throw Error('Audio pause failed');
 results.push({test:'audio',...audio,pause:true});
 console.log('SMOKE_OK '+JSON.stringify(results));if(process.env.PATTERN_SMOKE_RESULTS)fs.writeFileSync(process.env.PATTERN_SMOKE_RESULTS,JSON.stringify({ok:true,platform:process.platform,arch:process.arch,results},null,2));app.exit(0);
 }catch(e){console.error('SMOKE_FAILED',e);if(process.env.PATTERN_SMOKE_RESULTS)fs.writeFileSync(process.env.PATTERN_SMOKE_RESULTS,JSON.stringify({ok:false,error:e.stack,results},null,2));app.exit(1);}
};
