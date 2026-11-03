// Counts authored rows/objects before vs after the function-call format (11-function-syntax.md).
// Usage: node row-reduction.cjs <before full-design dir> [before html]. Not part of build-all: the "before" copy is a one-time backup.
const fs=require('node:fs'),path=require('node:path');const root=path.resolve(__dirname,'..');
const {parseCalls}=require(path.resolve(root,'../tools/parse-calls.cjs'));
const beforeDir=process.argv[2];if(!beforeDir||!fs.existsSync(beforeDir)){console.error('before dir required');process.exit(1);}
const beforeHtml=process.argv[3];
const j=p=>JSON.parse(fs.readFileSync(p,'utf8'));
const objects=v=>Array.isArray(v)?v.reduce((n,x)=>n+objects(x),0):v&&typeof v==='object'?1+Object.values(v).reduce((n,x)=>n+objects(x),0):0;
const strings=v=>Array.isArray(v)?v.reduce((n,x)=>n+strings(x),0):v&&typeof v==='object'?Object.values(v).reduce((n,x)=>n+strings(x),0):typeof v==='string'?1:0;
const rows=v=>Array.isArray(v)?v.length:v&&typeof v==='object'?1:0;
function table(before,after){const keys=[...new Set([...Object.keys(before),...Object.keys(after)])].filter(k=>typeof before[k]==='object'||typeof after[k]==='object');
 return Object.fromEntries(keys.map(k=>[k,{rowsBefore:rows(before[k]),rowsAfter:rows(after[k]),objectsBefore:objects(before[k]),objectsAfter:objects(after[k])}]));}
const cb=j(path.join(beforeDir,'examples/full-catalog.json')),ca=j(path.join(root,'examples/full-catalog.json'));
const rb=j(path.join(beforeDir,'examples/runner-bundle.json')),ra=j(path.join(root,'examples/runner-bundle.json'));
const reg=j(path.join(root,'schemas/functions.json'));
// Calls now authored as strings, per registry column.
let callCells=0,calls=0;const callsByColumn={};
for(const col of reg.columns){const data=col.file==='runner'?ra:ca;const list=col.table==='story/*/nodes'?ca.story.flatMap(a=>a.nodes):Array.isArray(data[col.table])?data[col.table]:data[col.table]?[data[col.table]]:[];
 let n=0,cells=0;for(const row of list){const texts=col.column==='forks/*'?Object.values(row.forks||{}):col.column in row?[row[col.column]]:[];for(const t of texts){cells++;n+=parseCalls(t).length;}}
 callsByColumn[col.table+'.'+col.column]={cells,calls:n};callCells+=cells;calls+=n;}
const nodesBefore=cb.story.flatMap(a=>a.nodes),nodesAfter=ca.story.flatMap(a=>a.nodes);
const commandObjects=evs=>{let n=0;const walk=v=>{if(Array.isArray(v))v.forEach(walk);else if(v&&typeof v==='object'){if(typeof v.op==='string')n++;Object.values(v).forEach(walk);}};walk(evs);return n;};
const merged=['mainRooms','goals','hints','puzzles','symbols','bindings','behaviorRegistry'];
const size=p=>fs.existsSync(p)?fs.statSync(p).size:null;
const files=['examples/full-catalog.json','examples/runner-bundle.json','examples/safe-save.json','examples/expected-outcomes.json','schemas/catalog.schema.json','schemas/runner.schema.json','schemas/save.schema.json','schemas/functions.json','validation/report.json','08-field-dictionary.md','01-source-trace.md'];
const report={date:'2026-11-02',method:'tools/row-reduction.cjs: JSON 객체 수=배열·중첩 포함 모든 {} 개수, Row 수=최상위 배열 길이(단일 객체는 1). before=형식 전환 직전 백업, after=현재 생성물. 손으로 센 값 없음.',
 catalog:{tables:table(cb,ca),storyNodes:{rowsBefore:nodesBefore.length,rowsAfter:nodesAfter.length,objectsBefore:objects(nodesBefore),objectsAfter:objects(nodesAfter)},
  mergedOrRemoved:Object.fromEntries(merged.map(k=>[k,{rowsBefore:rows(cb[k]),objectsBefore:objects(cb[k]),rowsAfter:rows(ca[k]),replacedBy:{mainRooms:'story[].nodes[] 칸',goals:'nodes[].goals 호출',hints:'nodes[].hints·rooms[].hints 호출',puzzles:'perform puzzle <room>[:label]',symbols:'validation/report.json derivedSymbolIndex(생성)',bindings:'assetKey 칸 + assetAddresses(생성)',behaviorRegistry:'schemas/functions.json kinds.behavior'}[k]}])),
  totalObjectsBefore:objects(cb),totalObjectsAfter:objects(ca),totalStringsBefore:strings(cb),totalStringsAfter:strings(ca)},
 runner:{tables:table(rb,ra),events:{rowsBefore:rb.events.length,rowsAfter:ra.events.length,objectsBefore:objects(rb.events),objectsAfter:objects(ra.events),commandAndConditionObjectsBefore:commandObjects(rb.events),callsAfter:['trigger','when','script','effects'].reduce((n,k)=>n+ra.events.reduce((m,e)=>m+parseCalls(e[k]||'').length,0),0)+ra.events.reduce((n,e)=>n+Object.values(e.forks||{}).reduce((m,t)=>m+parseCalls(t).length,0),0)},
  interactions:{rowsBefore:rows(rb.interactions),rowsAfter:rows(ra.interactions),replacedBy:'events[].trigger'},
  totalObjectsBefore:objects(rb),totalObjectsAfter:objects(ra)},
 callColumnsAfter:{cells:callCells,calls,byColumn:callsByColumn},
 fileBytes:Object.fromEntries(files.map(f=>[f,{before:size(path.join(beforeDir,f)),after:size(path.join(root,f))}])),
 html:beforeHtml?{before:size(beforeHtml),after:size(path.resolve(root,'../../마법학교_전체시스템기획서.html'))}:null};
fs.writeFileSync(path.join(root,'validation/row-reduction.json'),JSON.stringify(report,null,2)+'\n');
console.log(JSON.stringify({catalogObjects:[report.catalog.totalObjectsBefore,report.catalog.totalObjectsAfter],runnerObjects:[report.runner.totalObjectsBefore,report.runner.totalObjectsAfter],events:report.runner.events,calls,bytes:report.fileBytes['examples/full-catalog.json']},null,1));
