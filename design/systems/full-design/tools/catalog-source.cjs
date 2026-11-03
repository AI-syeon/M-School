// Source indexing only; no gameplay execution.
const fs=require('node:fs'),path=require('node:path');
const root=path.resolve(__dirname,'..');
const read=p=>fs.readFileSync(path.join(root,p),'utf8');
const decode=s=>s.replace(/<[^>]+>/g,' ').replace(/&amp;/g,'&').replace(/&lt;/g,'<').replace(/&gt;/g,'>').replace(/&quot;/g,'"').replace(/\s+/g,' ').trim();
const body=read('source/original-body.html');
const sections=body.split(/(?=<h2\b)/).slice(1);
const domains={1:'overview',2:'character',3:'clock',4:'classes',5:'magic',6:'life',7:'combat',8:'relationships',9:'story',10:'activities',11:'budget',12:'onboarding',13:'recovery',14:'presentation',15:'architecture',16:'verification',17:'provenance'};
const rows=[];
sections.forEach((html,i)=>{
 const section=i+1;let n=0,table=0;
 const pieces=[...html.matchAll(/<table\b[\s\S]*?<\/table>|<(p|li|aside)\b[^>]*>[\s\S]*?<\/\1>/g)];
 pieces.forEach(m=>{if(m[0].startsWith('<table')){table++;let r=0;for(const tr of m[0].matchAll(/<tr\b[^>]*>([\s\S]*?)<\/tr>/g)){r++;const cells=[...tr[1].matchAll(/<t[dh][^>]*>([\s\S]*?)<\/t[dh]>/g)].map(x=>decode(x[1]));if(r>1)rows.push({id:`SRC-${section.toString().padStart(2,'0')}-T${table}-R${r-1}`,section,locator:`§${section} 표${table} 행${r-1}`,text:cells.join(' | '),domain:domains[section],kind:section===17?'history':'requirement',review:'indexed',spec:`#domain-${domains[section]}`,example:'examples/full-catalog.json',verification:`V-${domains[section]}`});}}else{const text=decode(m[0]);if(!text||/^(화면의 핵심|크게 보기)/.test(text))return;rows.push({id:`SRC-${section.toString().padStart(2,'0')}-P${++n}`,section,locator:`§${section} 본문${n}`,text,domain:domains[section],kind:section===17?'history':'requirement',review:'indexed',spec:`#domain-${domains[section]}`,example:'examples/full-catalog.json',verification:`V-${domains[section]}`});}});
});
fs.writeFileSync(path.join(root,'source/requirements.json'),JSON.stringify(rows,null,2));
fs.writeFileSync(path.join(root,'01-source-trace.md'),'# 원본 전체 대응표\n\nHTML 본문을 실제 추출한 §·표·행 단위 인덱스. 각 행은 원본 전문을 보존한다. `indexed`는 자동 분류 상태이며 구현 검증 통과를 뜻하지 않는다. 상세 구현 규칙은 도메인 명세, 재현 사례는 연결 시나리오를 따른다. §17은 과거 Sonnet 이력이며 이번 Opus 사용 증거와 분리한다.\n\n| 추적 ID | 원본 위치 | 원본 요구·내용 | 명세 | 예제 | 인수 시나리오 |\n|---|---|---|---|---|---|\n'+rows.map(r=>`| ${r.id} | ${r.locator} | ${r.text.replace(/\|/g,' / ')} | ${r.domain} | full-catalog | ${r.verification} |`).join('\n')+'\n');
console.log(JSON.stringify({sections:sections.length,records:rows.length,tableRows:rows.filter(r=>r.id.includes('-T')).length,sourceParagraphs:rows.filter(r=>r.id.includes('-P')).length}));
