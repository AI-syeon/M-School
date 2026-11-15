'use strict';
// Current Core planning edition. Does not run legacy schema/fixture generators.
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const root = __dirname;
const read = p => fs.readFileSync(path.join(root,p),'utf8');
const esc = s => String(s).replaceAll('&','&amp;').replaceAll('<','&lt;').replaceAll('>','&gt;').replaceAll('"','&quot;');
const tasks = ['implementation-tasks/README.md','implementation-tasks/구현구조.md','implementation-tasks/01-기반과최소연결.md','implementation-tasks/02-게임시스템.md','implementation-tasks/03-통합과콘텐츠검증.md','implementation-tasks/인수기준.md'];
const systems = ['systems/README.md', ...['01-requirements','02-architecture','03-systems','04-data-authoring','05-implementation','06-verification','07-validation-results'].map(x=>'systems/'+x+'.md')];
const full = ['systems/full-design/README.md', ...['02-contracts','03-integration','04-data-contracts','05-implementation','08-field-dictionary','09-adoption','10-review-resolution','11-function-syntax'].map(x=>'systems/full-design/'+x+'.md'), ...['01','02','03'].map(x=>'systems/full-design/domains/domain-'+x+'.md')];
const active = [...new Set([...tasks,...systems,...full])];
const errors=[];
const cardFiles = tasks.slice(2,5);
const cards=[];
for(const file of cardFiles){
  const source=read(file);
  const matches=[...source.matchAll(/^### Task (\d+)-(\d+) — (.+)$/gm)];
  for(let i=0;i<matches.length;i++){
    const m=matches[i], body=source.slice(m.index, matches[i+1]?.index??source.length);
    const id=m[1]+'-'+m[2];
    const depLine=body.match(/^- 직접 선행: (.+)$/m)?.[1]??'';
    const deps=[...depLine.matchAll(/\b(\d+-\d+)\b/g)].map(x=>x[1]);
    for(const field of ['직접 선행','목적/소유','입력→출력','처리 구조','경계','저장/실패','완료 조건'])
      if(!body.includes('- '+field+':')) errors.push(id+' missing '+field);
    if(+m[1]>=6&&!body.includes('- 현 기반 연결:')) errors.push(id+' missing Core binding');
    cards.push({id,stage:+m[1],title:m[3],dependencies:deps,file});
  }
}
assert.equal(cards.length,56);
assert.equal(new Set(cards.map(x=>x.id)).size,56);
assert.equal(new Set(cards.map(x=>x.stage)).size,18);
const ids = new Set(cards.map(x=>x.id));
for(const c of cards) for(const dep of c.dependencies){
  if(!ids.has(dep)) errors.push(c.id+' unknown dependency '+dep);
  // Existing numbering is retained. 4-2 explicitly runs after 4-1 within the same stage.
  if(+dep.split('-')[0]>=c.stage && !(c.id==='4-2'&&dep==='4-1')) errors.push(c.id+' invalid dependency '+dep);
}
let checkedLinks=0;
for(const file of active){
  const source=read(file).replace(/```[\s\S]*?```/g,'');
  for(const m of source.matchAll(/(?<!!)\[[^\]\n]+\]\(([^)]+)\)/g)){
    const ref=m[1];
    if(/^(https?:|#)/.test(ref)) continue;
    const target=path.resolve(root,path.dirname(file),ref.split('#')[0]);
    checkedLinks++;
    if(!fs.existsSync(target)) errors.push(file+' broken link '+ref);
  }
}

// Small Markdown renderer for a portable reading edition; source Markdown remains authoritative.
function inline(value,file,out,index){
  let s=esc(value);
  s=s.replace(/`([^`]+)`/g,'<code>$1</code>');
  s=s.replace(/\*\*([^*]+)\*\*/g,'<strong>$1</strong>');
  s=s.replace(/\[([^\]]+)\]\(([^)]+)\)/g,(_,label,ref)=>{
    let href=ref;
    if(!/^(https?:|#)/.test(ref)){
      const dest=path.resolve(root,path.dirname(file),ref.split('#')[0]);
      const embedded=index.get(dest);
      href=embedded?'#'+embedded:path.relative(path.dirname(path.join(root,out)),dest).replaceAll('\\','/');
    }
    return '<a href="'+esc(href)+'">'+label+'</a>';
  });
  return s;
}
function markdown(text,file,out,index){
  const lines=text.split(/\r?\n/), result=[];
  let code=false, table=false, list=false;
  const end=()=>{if(table){result.push('</tbody></table></div>');table=false}if(list){result.push('</ul>');list=false}};
  for(const line of lines){
    if(line.startsWith('```')){end();result.push(code?'</code></pre>':'<pre><code>');code=!code;continue}
    if(code){result.push(esc(line)+'\n');continue}
    if(line.startsWith('|')){
      if(list){result.push('</ul>');list=false}
      if(/^\|[\s:|\-]+$/.test(line))continue;
      const cells=line.replace(/^\||\|$/g,'').split(/(?<!\\)\|/).map(x=>inline(x.trim(),file,out,index));
      const tag=table?'td':'th';
      if(!table){result.push('<div class="table-wrap"><table><tbody>');table=true}
      result.push('<tr>'+cells.map(c=>'<'+tag+'>'+c+'</'+tag+'>').join('')+'</tr>');continue;
    }
    if(table){result.push('</tbody></table></div>');table=false}
    if(/^\s*[-*] /.test(line)||/^\d+\. /.test(line)){
      if(!list){result.push('<ul>');list=true}
      result.push('<li>'+inline(line.replace(/^\s*(?:[-*]|\d+\.) /,''),file,out,index)+'</li>');continue;
    }
    if(list){result.push('</ul>');list=false}
    const heading=line.match(/^(#{1,6}) (.+)$/);
    if(heading){const n=Math.min(heading[1].length+1,6);result.push('<h'+n+'>'+inline(heading[2],file,out,index)+'</h'+n+'>');continue}
    if(line.startsWith('> ')){result.push('<blockquote>'+inline(line.slice(2),file,out,index)+'</blockquote>');continue}
    if(/^---+$/.test(line)){result.push('<hr>');continue}
    if(line.trim()) result.push('<p>'+inline(line,file,out,index)+'</p>');
  }
  end();return result.join('\n');
}
const css=`:root{color-scheme:light dark;--bg:#f4f3ee;--paper:#fffefa;--ink:#202b28;--muted:#596860;--line:#d6ded5;--accent:#215c4a}*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font:16px/1.8 "Malgun Gothic",system-ui,sans-serif}header{padding:48px max(24px,calc((100% - 1200px)/2));background:#183e34;color:#f1f5e8}header small{letter-spacing:.12em;color:#b5d5c4}h1{font-size:clamp(28px,4vw,43px);line-height:1.35;margin:12px 0}header p{max-width:860px;margin:12px 0 0}.layout{display:grid;grid-template-columns:250px minmax(0,1fr);max-width:1250px;margin:auto}nav{padding:28px 20px;position:sticky;top:0;align-self:start;max-height:100vh;overflow:auto}nav a{display:block;padding:6px 0;color:var(--accent);font-size:13px;text-decoration:none}main{min-width:0;padding:28px 24px 80px}.summary,article{padding:28px;background:var(--paper);border:1px solid var(--line);border-radius:12px;margin:0 0 22px}article{scroll-margin-top:16px}h2{font-size:26px;line-height:1.5}h3{font-size:22px;border-top:1px solid var(--line);padding-top:22px}h4{font-size:18px}a{color:var(--accent);overflow-wrap:anywhere}p,li{overflow-wrap:anywhere}code{background:#eaf0e7;padding:2px 5px;border-radius:4px;font:13px/1.6 Consolas,monospace}pre{overflow:auto;padding:18px;background:#eaf0e7;white-space:pre-wrap}blockquote{border-left:4px solid #b89d62;margin:20px 0;padding:12px 18px;background:#f5efdd}.table-wrap{overflow:auto}table{border-collapse:collapse;width:100%;font-size:14px;margin:16px 0}td,th{border:1px solid var(--line);padding:10px 12px;text-align:left;vertical-align:top}th{background:#edf2e9}ul{padding-left:24px}footer{color:var(--muted);font-size:13px}.source{font-size:12px;color:var(--muted)}@media(max-width:800px){.layout{display:block}nav{position:static;max-height:240px;border-bottom:1px solid var(--line)}main{padding:16px 12px}.summary,article{padding:18px}header{padding:32px 20px}}@media(prefers-color-scheme:dark){:root{--bg:#151d19;--paper:#1e2923;--ink:#e3ece4;--muted:#a8b9ac;--line:#3b4c40;--accent:#a2d7be}code,pre,th{background:#2b3b2e}blockquote{background:#3c3626}}@media print{nav{display:none}.layout{display:block}header{color:black;background:white}article{break-before:page;border:0}a{color:inherit}}`;
const outputs=[
 ['프로그래머_구현계획서.html','프로그래머 구현 계획',tasks],
 ['systems/시스템_기획서.html','현재 시스템 기획',systems],
 ['마법학교_전체시스템기획서.html','전체 시스템 기획',[...systems,...full,...tasks]],
];
const htmlResults=[];
for(const [out,title,files] of outputs){
  const unique=[...new Set(files)];
  const index=new Map(unique.map((file,i)=>[path.resolve(root,file),'doc-'+i]));
  const sections=unique.map((file,i)=>{
    return '<article id="doc-'+i+'"><div class="source">'+esc(file)+'</div>'+markdown(read(file),file,out,index)+'</article>';
  }).join('\n');
  const nav=unique.map((file,i)=>'<a href="#doc-'+i+'">'+esc(read(file).match(/^# (.+)$/m)?.[1]??file)+'</a>').join('');
  const html='<!doctype html><html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>매직캠퍼스 · '+esc(title)+'</title><style>'+css+'</style></head><body><header><small>MAGIC CAMPUS / 2026.11.06</small><h1>'+esc(title)+'</h1><p>현재 Core 기반 · 단일 프레임과 이벤트 · 수면/02시 하루 저장 · 32×32 타일 · 18단계 56개 Task</p></header><div class="layout"><nav aria-label="문서 목차">'+nav+'</nav><main><div class="summary"><strong>읽는 기준</strong><p>Task1~5는 현재 구현의 책임과 재인수 기준, Task6~18은 같은 Core를 확장하는 계획입니다. 실제 코드·데이터·검증 결과를 구분합니다. 기존 원본과 과거 문서는 보관하고 현재 계약을 우선합니다.</p></div>'+sections+'<footer>Markdown 원문에서 생성 · node design/build-current-plans.cjs · Unity 전체 인수 결과와 문서 검사는 별도입니다.</footer></main></div></body></html>';
  const found=[...html.matchAll(/id="([^"]+)"/g)].map(x=>x[1]);
  assert.equal(new Set(found).size,found.length,out+' duplicate anchors');
  for(const m of html.matchAll(/href="#([^"]+)"/g))assert(found.includes(m[1]),out+' broken anchor '+m[1]);
  fs.writeFileSync(path.join(root,out),html,'utf8');
  htmlResults.push({file:out,documents:unique.length,anchors:found.length});
}
const report={date:'2026-11-06',scope:'current Core planning edition; Core test results recorded separately; Unity not rerun',taskCount:cards.length,stageCount:18,checks:{uniqueTaskIds:true,requiredTaskFields:true,allFutureTasksBoundToCore:cards.filter(c=>c.stage>=6).length===40,dependencies:'prior stage, except explicit sequential 4-1 → 4-2',localLinksChecked:checkedLinks,htmlAnchors:true},coreEvidence:'../../Tests/Results/current-core-planning/core-tests.log',html:htmlResults,sourceHashes:Object.fromEntries(active.map(file=>[file,crypto.createHash('sha256').update(read(file)).digest('hex')])),errors,tasks:cards};
fs.writeFileSync(path.join(root,'implementation-tasks/문서검사.json'),JSON.stringify(report,null,2)+'\n');
fs.writeFileSync(path.join(root,'implementation-tasks/HTML검사.json'),JSON.stringify({date:report.date,scope:'generated structure and local links; visual/Unity interaction not executed',html:htmlResults,errors},null,2)+'\n');
if(errors.length){console.error(errors.join('\n'));process.exitCode=1}else console.log('PASS: 56 Tasks, 18 stages, 40 Core bindings, '+checkedLinks+' links, 3 HTML reading editions.');
