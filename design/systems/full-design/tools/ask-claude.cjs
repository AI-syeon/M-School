// Official Claude Code CLI, text-only requests. No UI automation or gameplay code.
const fs=require('node:fs'),path=require('node:path'),{spawn}=require('node:child_process');
const root=path.resolve(__dirname,'..');
const cli='C:/Users/syeon/AppData/Local/Claude-Data/claude_pzs8sxrjxfjjc/claude-code/2.1.309/9078b9c1d466/claude.exe';
const round=process.argv[2]||'01';
const args=['-p','--model','claude-opus-5-5','--tools','','--safe-mode','--strict-mcp-config','--output-format','stream-json','--verbose'];
const stateFile=path.join(root,'source/claude-cli-session.json');
if(round!=='01'){const state=JSON.parse(fs.readFileSync(stateFile));args.push('--resume',state.sessionId);}
let request=fs.readFileSync(path.join(root,`source/claude-request-${round}.txt`),'utf8');
if(round==='01')request+='\n\n[전달 이력] 기존 앱 일반대화 61bbca34-bd48-4af3-aecd-eabd4ed430a8는 CLI에서 conversation not found로 확인되어, 같은 원본·공통계약을 여기서 다시 제공합니다. 앱 응답은 아직 수신하지 않았습니다. 아래 원본 기준으로 독립 작성하세요.\n\n'+fs.readFileSync(path.join(root,'source/original-text.txt'),'utf8');
fs.writeFileSync(path.join(root,`source/claude-sent-${round}.txt`),request);
const out=fs.createWriteStream(path.join(root,`source/claude-response-${round}.jsonl`));
const err=fs.createWriteStream(path.join(root,`source/claude-stderr-${round}.txt`));
const child=spawn(cli,args,{cwd:path.resolve(root,'../../..'),windowsHide:true,stdio:['pipe','pipe','pipe']});
let pending='',result='';
child.stdout.on('data',d=>{out.write(d);pending+=d.toString();let ix;while((ix=pending.indexOf('\n'))>=0){const line=pending.slice(0,ix);pending=pending.slice(ix+1);try{const v=JSON.parse(line);if(v.type==='system'&&v.subtype==='init'){fs.writeFileSync(stateFile,JSON.stringify({sessionId:v.session_id,model:v.model,transport:'Claude Code CLI',version:v.claude_code_version,date:'2026-11-01'},null,2));console.log(JSON.stringify({type:'init',sessionId:v.session_id,model:v.model}));}if(v.type==='assistant'){const t=(v.message?.content||[]).filter(c=>c.type==='text').map(c=>c.text).join('\n');if(t){result+=t+'\n';fs.writeFileSync(path.join(root,`source/claude-draft-${round}.md`),result);console.log(JSON.stringify({type:'received',characters:result.length,model:v.message?.model}));}}if(v.type==='result')console.log(JSON.stringify({type:'result',subtype:v.subtype,is_error:v.is_error,sessionId:v.session_id}));}catch{}}});
child.stderr.on('data',d=>err.write(d));
child.on('error',e=>{console.error(e.message);process.exitCode=1;});
child.on('close',code=>{out.end();err.end();console.log(JSON.stringify({exitCode:code,characters:result.length}));process.exitCode=code||0;});
child.stdin.end(request);
