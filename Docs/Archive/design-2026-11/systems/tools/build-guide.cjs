// Build a portable visual document from the authoritative Markdown and JSON.
// The generated HTML contains all text, styles, scripts, schemas and example data.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '..');
const read = p => fs.readFileSync(path.join(root,p),'utf8');
const json = p => JSON.parse(read(p));
const escape = s => String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
const files = [
  ['README.md','guide','시스템 기획서 안내','조사 범위 · 기술 선택 · 완료 범위'],
  ['01-requirements.md','requirements','원본 요구사항과 공통 시스템 대응','요구 22개 · 설계 제안 · 미결정 항목'],
  ['02-architecture.md','architecture','전체 구조와 공통 실행 계약','상태 소유권 · 좌표 · 실행 순서 · 시간'],
  ['03-systems.md','systems','시스템별 실행 명세','입력과 출력 · 전이 · 실패와 중단 · Unity 적용'],
  ['04-data-authoring.md','authoring','콘텐츠 작성과 데이터 계약','이벤트 Row · 호출 문법 · 함수 사전 · 시험 맵 · 오류 표시'],
  ['05-implementation.md','implementation','Unity 구현 계획과 인수 기준','선행 의존성 · M0–M5 · 첫 이정표 A01–A10'],
  ['06-verification.md','verification','검증 시나리오와 기대 결과','T01–T20 · 장애와 복구 · 재사용성'],
  ['07-validation-results.md','results','문서와 데이터 검증 결과','실제로 검사한 것 · 교차 검토 · 남은 시험']
];
const dataFiles = [
  ['schemas/content.schema.json','content-schema','콘텐츠 JSON Schema'],
  ['schemas/save.schema.json','save-schema','안전 저장 JSON Schema'],
  ['examples/campus-demo.json','content-example','시험 콘텐츠 전체 JSON'],
  ['examples/save-v1.json','save-example','완료 후 안전 저장 예제'],
  ['examples/expected-trace.json','expected-trace','기대 실행 기록 · 실제 로그 아님'],
  ['validation/report.json','validation-report','기계 검증 보고서']
];
const lookup = new Map([...files,...dataFiles].map(([file,id]) => [file,'#spec-'+id]));
lookup.set('../마법학교_최초기획안.md','#spec-original');
lookup.set('시스템_기획서.html','#top');
function inline(text) {
  // Hold code spans out of link/emphasis processing.
  const codes=[];
  let s=text.replace(/`([^`]+)`/g,(_,code)=>`\u0001${codes.push('<code>'+escape(code)+'</code>')-1}\u0001`);
  s=escape(s).replace(/\[([^\]]+)\]\(([^)]+)\)/g,(_,label,href)=>'<a href="'+escape(lookup.get(href)||href)+'">'+label+'</a>')
    .replace(/\*\*(.+?)\*\*/g,'<strong>$1</strong>');
  return s.replace(/\u0001(\d+)\u0001/g,(_,i)=>codes[Number(i)]);
}
function markdown(source, prefix) {
  const lines=source.split(/\r?\n/), out=[];let i=0,heading=0;
  const isTableSeparator=s=>/^\|[\s:|\-]+\|$/.test(s||'');
  const cells=s=>s.trim().replace(/^\||\|$/g,'').split('|').map(x=>x.trim());
  while(i<lines.length) {
    const line=lines[i];if(!line.trim()){i++;continue;}
    if(line.startsWith('```')) {
      const code=[];i++;while(i<lines.length&&!lines[i].startsWith('```'))code.push(lines[i++]);i++;
      out.push('<pre><code>'+escape(code.join('\n'))+'</code></pre>');continue;
    }
    const h=line.match(/^(#{1,6}) (.+)$/);
    if(h){const level=Math.min(h[1].length+1,6);out.push(`<h${level} id="${prefix}-h${++heading}">${inline(h[2])}</h${level}>`);i++;continue;}
    if(line.startsWith('|')&&isTableSeparator(lines[i+1])) {
      const head=cells(line);i+=2;const rows=[];
      while(i<lines.length&&lines[i].startsWith('|')) {
        const row=cells(lines[i++]);assert.equal(row.length,head.length,'Table width mismatch '+prefix);rows.push('<tr>'+row.map(v=>'<td>'+inline(v)+'</td>').join('')+'</tr>');
      }
      out.push('<div class="table-scroll"><table><thead><tr>'+head.map(v=>'<th scope="col">'+inline(v)+'</th>').join('')+'</tr></thead><tbody>'+rows.join('')+'</tbody></table></div>');continue;
    }
    if(/^(- |\d+\. )/.test(line)) {
      const ordered=/^\d+/.test(line),items=[];
      while(i<lines.length&&(ordered?/^\d+\. /:/^- /).test(lines[i]))items.push('<li>'+inline(lines[i++].replace(/^(?:- |\d+\. )/,''))+'</li>');
      const tag=ordered?'ol':'ul';out.push('<'+tag+'>'+items.join('')+'</'+tag+'>');continue;
    }
    if(line.startsWith('> ')){out.push('<blockquote>'+inline(line.slice(2))+'</blockquote>');i++;continue;}
    const paragraph=[line];i++;
    while(i<lines.length&&lines[i].trim()&&!/^(?:#{1,6} |```|- |\d+\. |\|)/.test(lines[i]))paragraph.push(lines[i++]);
    out.push('<p>'+inline(paragraph.join(' '))+'</p>');
  }
  return out.join('\n');
}
const bundle=json('examples/campus-demo.json'), report=json('validation/report.json');
const {parseCalls}=require('./parse-calls.cjs');
function callTable(event) {
  const rows=[],add=(pos,text)=>parseCalls(text).forEach(c=>rows.push([pos(c.index),c.label?c.label+':':'',(c.negate?'!':'')+c.name,[...c.positional.map(a=>a.type==='text'?'"'+a.raw+'"':a.raw),...Object.entries(c.named).map(([k,a])=>k+'='+a.raw)].join(' ')]));
  add(i=>event.id+'@trigger',event.trigger);add(i=>event.id+'@when#'+i,event.when);add(i=>event.id+'#'+i,event.script);
  for(const [name,text] of Object.entries(event.forks||{}))add(i=>event.id+':'+name+'#'+i,text);
  add(i=>event.id+'@effects#'+i,event.effects);
  return '<div class="table-scroll"><table><thead><tr><th scope="col">위치</th><th scope="col">함수</th><th scope="col">인자</th></tr></thead><tbody>'+rows.map(([pos,label,fn,args])=>'<tr><td><code>'+escape(pos)+'</code></td><td><code>'+escape(label+fn)+'</code></td><td><code>'+escape(args)+'</code></td></tr>').join('')+'</tbody></table></div>';
}
const steps=[
  {label:'조건 확인',title:'이 장면을 시작해도 될까?',call:'event.snack@when#1  eq story.intro_done true',body:'도입을 마쳤는지, 이미 완료한 모임인지, 서린과 장소를 사용할 수 있는지 확인합니다.',rule:'같은 조건 판정기를 사건·대화·상호작용에서 함께 사용합니다. 조건이 맞지 않으면 이유를 알려줍니다.',dialogue:'모임을 시작하기 전, 도입 완료 여부와 초대 상태를 확인합니다.'},
  {label:'안전 준비',title:'진입 전 상태를 남기고 준비',call:'(호출 전)  resources 4개 임대 · 진입 안전 상태 A',body:'필요한 사용권을 확보하고 시작 전 안전 상태 A를 저장합니다. 예약 장면이므로 서린은 화면 밖에서 준비 지점에 합류합니다.',rule:'평상 일정 이동은 취소하고 이벤트에 제어권을 넘깁니다. 예약 합류 이후에는 공통 이동기를 사용합니다.',dialogue:'서린과 장소의 사용권을 확보했습니다. 시작 전 안전 상태 A를 남깁니다.'},
  {label:'이동 시작',title:'목표만 전달하고 이동 요청',call:'event.snack#1  move npc.serin dest.snack',body:'서린에게 간식 자리라는 목적지를 줍니다. 길 찾기와 실제 이동은 공통 이동 시스템이 맡습니다.',rule:'move 호출이 이동 작업을 시작합니다. 시작했다는 것이 도착했다는 뜻은 아니고, 기본 move는 도착할 때까지 다음 호출로 넘어가지 않습니다.',dialogue:'서린이 간식 자리로 이동 중입니다. 아직 대사를 시작하지 않습니다.'},
  {label:'도착 기다림',title:'실제 도착을 확인할 때까지',call:'event.snack#1  (같은 move 호출이 도착까지 대기)',body:'지정 반경 안에 도착하고 충돌 없이 멈출 수 있어야 다음 단계로 넘어갑니다.',rule:'같은 move 호출이 이동 결과를 기다립니다. 따로 기다리려면 async=핸들과 await를 씁니다. 길 막힘·실패·취소라면 장면을 안전하게 중단합니다.',dialogue:'간식 자리 도착을 확인했습니다. 다음 단계부터 대화할 수 있습니다.'},
  {label:'대사 표시',title:'도착한 다음 대화 시작',call:'event.snack#2  say npc.serin "실습 노트를 같이 정리해 볼래?"',body:'서린의 대사를 보여주고 플레이어의 확인을 기다립니다. 대화창과 입력도 이벤트가 관리합니다.',rule:'관계 장면에서는 게임 시계와 평상 NPC 이동을 멈춥니다. 필요한 장면 이동·연출은 계속할 수 있습니다.',dialogue:'실습 노트를 같이 정리해 볼래?'},
  {label:'선택과 반응',title:'선택한 응답만 이어서',call:'event.snack#3  choice … notes / tea  →  event.snack:notes#1 또는 event.snack:tea#1',body:'아래 선택지를 눌러 반응을 비교해 보세요. 이 예제에서는 두 선택 모두 같은 공동 경험으로 합류합니다.',rule:'선택은 고른 가지(forks)의 호출만 실행해 연출만 바꿉니다. 함께한 기억과 시간 비용은 마지막 단계에서 한 번에 확정합니다.'},
  {label:'결과 저장',title:'기억·완료·시간을 함께 확정',call:'effects  set memory.snack_done true',body:'함께한 기억을 남기고 모임을 완료 처리합니다. 표시했던 10분을 반영한 뒤 안전 상태 B를 저장합니다.',rule:'script가 성공으로 끝난 뒤 effects 칸을 한 번에 커밋합니다. 완료 기록으로 중복 실행을 막습니다. 다시 열어도 기억과 결과는 유지되고 시간 비용은 다시 더하지 않습니다.',dialogue:'함께한 기억이 남았습니다. 다음 일정은 현재 시각인 07:10을 기준으로 다시 계산합니다.'}
];

// Build-time static path drawing. It is only a diagram derived from authored data.
function findRoute(from,to) {
  const maps=new Map(bundle.maps.map(m=>[m.id,m]));
  const key=p=>`${p.mapId}:${Math.floor(p.x)},${Math.floor(p.y)}`;
  const can=p=>{const m=maps.get(p.mapId);return !!m&&p.x>=0&&p.y>=0&&p.x<m.width&&p.y<m.height&&!m.blocked.some(([x,y])=>x===Math.floor(p.x)&&y===Math.floor(p.y));};
  const nodes=[from],parent=new Map([[key(from),null]]),positions=new Map([[key(from),from]]);
  for(let i=0;i<nodes.length;i++) {
    const p=nodes[i];if(key(p)===key(to)){const route=[];for(let k=key(p);k;k=parent.get(k))route.unshift(positions.get(k));return route;}
    const next=[];
    for(let dy=-1;dy<=1;dy++)for(let dx=-1;dx<=1;dx++)if(dx||dy){const n={mapId:p.mapId,x:Math.floor(p.x)+dx+.5,y:Math.floor(p.y)+dy+.5};if(dx&&dy&&(!can({...n,x:p.x})||!can({...n,y:p.y})))continue;next.push(n);}
    for(const portal of maps.get(p.mapId).portals)if(portal.open&&Math.floor(p.x)===Math.floor(portal.x)&&Math.floor(p.y)===Math.floor(portal.y))next.push(portal.to);
    for(const n of next)if(can(n)&&!parent.has(key(n))){parent.set(key(n),key(p));positions.set(key(n),n);nodes.push(n);}
  }
  throw Error('No diagram path');
}
const start=bundle.npcs.find(n=>n.id==='npc.mira').position;
const goal=bundle.destinations.find(d=>d.id==='dest.stage').position;
const route=findRoute(start,goal);
const tile=38,pad=16;
function mapDiagram(map,index) {
  const width=map.width*tile+pad*2,height=map.height*tile+pad*2+39;
  let svg=`<svg viewBox="0 0 ${width} ${height}" role="img" aria-labelledby="route-title-${index}"><title id="route-title-${index}">${index===0?'교정의 미라 시작 위치와 벽을 우회하는 이동 경로':'휴게실 연결 문에서 무대까지의 이동 경로'}</title>`;
  for(let y=0;y<map.height;y++)for(let x=0;x<map.width;x++)svg+=`<rect class="route-tile${map.blocked.some(([bx,by])=>bx===x&&by===y)?' wall':''}" x="${pad+x*tile}" y="${pad+y*tile}" width="${tile}" height="${tile}" rx="2"/>`;
  const points=route.filter(p=>p.mapId===map.id);
  svg+=`<polyline class="route-line" points="${points.map(p=>[pad+p.x*tile,pad+p.y*tile].join(',')).join(' ')}"/>`;
  map.portals.forEach(p=>{const x=pad+p.x*tile,y=pad+p.y*tile;svg+=`<rect class="route-portal" x="${x-13}" y="${y-13}" width="26" height="26" rx="3"/><text x="${x}" y="${y+4}" text-anchor="middle" fill="#263d46" font-size="10">문</text>`;});
  if(start.mapId===map.id)svg+=`<circle cx="${pad+start.x*tile}" cy="${pad+start.y*tile}" r="13" fill="#355d49"/><text x="${pad+start.x*tile}" y="${pad+start.y*tile+4}" font-size="11" fill="#fff" text-anchor="middle">미라</text>`;
  if(goal.mapId===map.id){const x=pad+goal.x*tile,y=pad+goal.y*tile;svg+=`<path class="goal-marker" d="M${x} ${y-14}l14 14-14 14-14-14z" fill="#d9c388" stroke="#806b39"/><text x="${x}" y="${y+4}" font-size="9" text-anchor="middle" fill="#4f442a">무대</text>`;}
  svg+=`<text class="route-state-label" x="${width/2}" y="${height-10}" font-size="11" fill="#667760" text-anchor="middle">연결 문</text></svg>`;
  return `<figure class="route-map-figure"><figcaption>${index===0?'교정 · 8 × 6 타일':'휴게실 · 6 × 5 타일'}</figcaption>${svg}</figure>`;
}
const fullSpec=files.map(([file,id,title,description])=>{
  const source=read(file),rendered=markdown(source,'doc-'+id);
  const rows=source.split(/\r?\n/).filter(line=>line.startsWith('|')&&!/^\|[\s:|\-]+\|$/.test(line)).length;
  assert.equal((rendered.match(/<tr>/g)||[]).length,rows,'Source table rows not preserved: '+file);
  assert.equal((rendered.match(/<h[2-6] /g)||[]).length,(source.match(/^#{1,6} /gm)||[]).length,'Source headings not preserved: '+file);
  return `<details class="full-document" id="spec-${id}"><summary>${escape(title)}<small>${escape(description)}</small></summary><div class="document-body">${rendered}</div></details>`;
}).join('\n');
const original=`<details class="full-document" id="spec-original"><summary>원본 콘텐츠 기획서<small>기존 최종 Markdown 전문 · 원본 수정 없이 포함</small></summary><div class="document-body">${markdown(read('../마법학교_최초기획안.md'),'doc-original')}</div></details>`;
const appendix=dataFiles.map(([file,id,title])=>`<details class="full-document data-document" id="spec-${id}"><summary>${escape(title)}<small>${escape(file)}</small></summary><pre><code>${escape(JSON.stringify(json(file),null,2))}</code></pre></details>`).join('\n');
const guideData={steps,routeCases:{
  normal:{title:'도착 가능 · 문을 통해 다른 맵으로 이동',description:'교정의 벽을 우회 → 교정 연결 문 → 휴게실 연결 문 → 무대 도착. 도착 결과를 받은 뒤에만 대사와 결과 확정을 진행합니다.'},
  closed:{title:'실패 · 연결 문이 닫혀 경로가 없음',description:'이 시험 맵에서는 다른 연결 문이 없으므로 NO_ROUTE 또는 PORTAL_CLOSED를 반환합니다. 도착 처리와 완료 효과를 실행하지 않고 안전 상태로 복원합니다.'},
  blocked:{title:'실패 · 목적지에서 멈출 수 없음',description:'무대 목표 지점이 막혀 있으면 TARGET_BLOCKED를 반환합니다. 가까운 다른 칸에 서 있다고 도착 성공으로 간주하지 않습니다.'}
}};
const replacements={
  '__CSS__':read('tools/guide.css'),'__JS__':read('tools/guide.js'),
  '__STEPS__':steps.map((s,i)=>`<button type="button" data-step="${i}" aria-pressed="${i===0}"><span>${String(i+1).padStart(2,'0')}</span>${escape(s.label)}</button>`).join(''),
  '__SNACK_EVENT__':escape(JSON.stringify(bundle.events.find(e=>e.id==='event.snack'),null,2)),
  '__SNACK_CALLS__':callTable(bundle.events.find(e=>e.id==='event.snack')),
  '__ROUTE_MAPS__':bundle.maps.map(mapDiagram).join(''),
  '__FULL_SPEC__':fullSpec+'\n'+original,'__DATA_APPENDIX__':appendix,
  '__NEGATIVE_COUNT__':String(report.counts.negativeCases),
  '__GUIDE_DATA__':JSON.stringify(guideData).replace(/</g,'\\u003c')
};
let html=read('tools/guide.template.html');
for(const [key,value] of Object.entries(replacements)) {assert(html.includes(key),'Missing marker '+key);html=html.replace(key,()=>value);}
assert(!/__([A-Z][A-Z_]+)__/.test(html),'Unreplaced template marker');
const ids=[...html.matchAll(/\bid="([^"]+)"/g)].map(m=>m[1]);
assert.equal(ids.length,new Set(ids).size,'Duplicate HTML id');
let anchors=0;
for(const [,target] of html.matchAll(/href="#([^"]+)"/g)){assert(ids.includes(target),'Missing anchor '+target);anchors++;}
for(const [,script] of html.matchAll(/<script>([\s\S]*?)<\/script>/g))new Function(script);
assert(!/<(?:script|link|img)\b[^>]*(?:src|href)="https?:/i.test(html),'External resource');
assert(!/\b(?:fetch\s*\(|XMLHttpRequest|WebSocket)/.test(read('tools/guide.js')),'Unexpected network dependency');
const expectedTables=files.reduce((sum,[file])=>sum+(read(file).match(/^\| /gm)||[]).length-(read(file).match(/^\|[-: |]+\|$/gm)||[]).length,0);
const dataSummaries={};for(const [file,id] of dataFiles)dataSummaries[file]=crypto.createHash('sha256').update(read(file)).digest('hex');
const outputPath=path.join(root,'시스템_기획서.html');
fs.writeFileSync(outputPath,html,'utf8');
const validation={artifact:'시스템_기획서.html',scope:'HTML structure, embedded source and script syntax; browser QA recorded separately',bytes:Buffer.byteLength(html),embeddedSystemDocuments:files.length,embeddedOriginalContentDocuments:1,embeddedJsonDocuments:dataFiles.length,diagramTypes:['architecture','event-stepper','call-table','cross-map-route','control-priority','time-pause','safe-save','implementation-roadmap'],uniqueElementIds:ids.length,internalLinks:anchors,negativeDataCases:report.counts.negativeCases,expectedSystemTableRows:expectedTables,staticRouteNodes:route.length,externalResourceDependencies:0,embeddedDataHashes:dataSummaries};
fs.writeFileSync(path.join(root,'validation/html-report.json'),JSON.stringify(validation,null,2)+'\n');
console.log(JSON.stringify(validation,null,2));
