// Rebuild and verify document artifacts only. Does not invoke Claude or Unity.
const {spawnSync}=require('node:child_process');const path=require('node:path');
for(const script of ['build-catalog.cjs','complete-contracts.cjs','build-schemas.cjs','build-runner.cjs','adopt-drafts.cjs','refine-trace.cjs','validate.cjs','claude-evidence.cjs','build-html.cjs','check-html.cjs']){
 const result=spawnSync(process.execPath,[path.join(__dirname,script)],{encoding:'utf8',windowsHide:true});
 if(result.status!==0){process.stderr.write(result.stderr||result.stdout||script+' failed');process.exit(result.status||1);}
 console.log(script+': pass');
}
