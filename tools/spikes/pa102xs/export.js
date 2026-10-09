window.exportAll=async function(cfgs){const rafO=window.requestAnimationFrame;window.requestAnimationFrame=()=>0;const log=[];
 const post=(name,dir,body)=>fetch('/save?dir='+dir+'&name='+name,{method:'POST',body});
 for(const [sp,st,sd,t] of cfgs){document.getElementById('species').value=sp;document.getElementById('style').value=st;setSpecies();seed=sd;M=build(st,sd);ENV.m=4.5;ENV.h=10;FX.rain=0;FX.wind=0;FX.ov=0;FX.wet=0;FX.fallLast=-99;
  document.getElementById('t').value=Math.round(t*1000);envUpdate();const sc=scene(t);
  const key=sp+'_'+st+'_s'+sd+'_t'+Math.round(t*100);
  // build arrays (float64)
  const P=new Float64Array(M.P.length*2);M.P.forEach((p,i)=>{P[i*2]=p[0];P[i*2+1]=p[1];});const T=Float64Array.from(M.T);
  const parts=[['P',P],['T',T],['DAT',DAT],['CB',CB.subarray(0,nc*4)],['GM',GM.subarray(0,sc.ng*8)],['PE',PE.subarray(0,np*8)],['LEAF',LEAF.subarray(0,sc.nl*16)],['XLEAF',XLEAF.subarray(0,sc.nx*16)]];
  let off=0;const idx={};const bufs=[];for(const [n,a] of parts){const b=new Uint8Array(a.buffer,a.byteOffset,a.byteLength).slice();idx[n]={type:a instanceof Float64Array?'f64':'f32',offset:off,length:a.length};off+=b.byteLength;bufs.push(b);}
  const meta={key,species:sp,style:st,seed:sd,t,scene:sc,nc,np,env:{m:ENV.m,h:ENV.h,L:ENV.L,sunC:ENV.sunC,amb:ENV.amb,sI:ENV.sI,LL:Array.from(LL)},
   build:{n:M.n,Rmax:M.Rmax,cx:M.cx,bends:M.bends,roots:M.roots.map(r=>({az:r.az,len:r.len,th:r.th,e:r.e,wob:r.wob,ch:r.ch})),br:M.br.map(b=>Object.assign({},b,{pj:b.pj})),apex:M.apex,apj:M.apj},
   pads:PADS.map(p=>({c:p.c,a:p.a,b:p.b,yb:p.yb,hx:p.hx,pp:p.pp,dome:p.dome,key:p.key,gp:p.gp})),
   layout:{TW,NT,NTC,RP,MAXR,CP,RC,NSG,MAXG,MAXCPG,MAXCH,OB,ORB,OSG,ORW,NB,OR,OBR,MAXPAD,OPF},arrays:idx};
  await post(key+'.json','golden',JSON.stringify(meta));await post(key+'.bin','golden',new Blob(bufs));
  // reference render: 4 AA passes, alpha preserved
  setRes(SHI);draw();for(let k=0;k<JIT.length;k++){renderWood(JIT[k]);renderFrame(JIT[k],false);present(k);}
  const blob=await new Promise(r=>document.getElementById('gl').toBlob(r,'image/png'));await post(key+'.png','refs',blob);
  log.push(key+' nl='+sc.nl);}
 window.requestAnimationFrame=rafO;return log;};
