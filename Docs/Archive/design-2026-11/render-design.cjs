const fs = require('node:fs');
const path = require('node:path');
const gallery = require('./screen-gallery.cjs');
const input = path.join(__dirname, '마법학교_최초기획안.md');
const escape = s => s.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
const inline = s => escape(s).replace(/\*\*(.+?)\*\*/g,'<strong>$1</strong>').replace(/`([^`]+)`/g,'<code>$1</code>');
const lines = fs.readFileSync(input,'utf8').split(/\r?\n/);
let result=[], toc=[], list=null, paragraph=[], i=0, section=0;
function flush(){if(paragraph.length){result.push('<p>'+inline(paragraph.join(' '))+'</p>');paragraph=[];} if(list){result.push('</'+list+'>');list=null;}}
while(i<lines.length){
  const line=lines[i];
  if(!line.trim()){flush(); i++; continue;}
  const heading=line.match(/^(#{1,4}) (.+)$/);
  if(heading){flush();const level=heading[1].length;const id='section-'+(++section);result.push(`<h${level} id="${id}">${inline(heading[2])}</h${level}>`);if(level===2){toc.push(`<a href="#${id}">${inline(heading[2])}</a>`);result.push(gallery.forHeading(heading[2]));}i++;continue;}
  if(line.startsWith('|') && lines[i+1]?.match(/^\|[\s:|\-]+\|$/)){
    flush();const cells=s=>s.trim().replace(/^\||\|$/g,'').split('|').map(c=>c.trim());
    const heads=cells(line);i+=2;let rows=[];while(i<lines.length && lines[i].startsWith('|')){const row=cells(lines[i++]);if(row.length!==heads.length)throw Error('Table width mismatch: '+row.join(' / '));rows.push('<tr>'+row.map(c=>'<td>'+inline(c)+'</td>').join('')+'</tr>');}
    result.push('<div class="table-wrap"><table><thead><tr>'+heads.map(c=>'<th>'+inline(c)+'</th>').join('')+'</tr></thead><tbody>'+rows.join('')+'</tbody></table></div>');continue;
  }
  const item=line.match(/^(?:- |\d+\. )(.+)$/);
  if(item){if(paragraph.length)flush();const kind=/^\d/.test(line)?'ol':'ul';if(list!==kind){if(list)result.push('</'+list+'>');result.push('<'+kind+'>');list=kind;}result.push('<li>'+inline(item[1])+'</li>');i++;continue;}
  if(line.startsWith('> ')){flush();result.push('<aside>'+inline(line.slice(2))+'</aside>');result.push(gallery.index());i++;continue;}
  paragraph.push(line);i++;
}
flush();
const html=`<!doctype html><html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>마법학교 생활 RPG · 예상 게임화면 포함 기획안</title><style>
:root{--ink:#23362f;--muted:#65746b;--paper:#fbfaf6;--line:#dfe4da;--green:#28634d}*{box-sizing:border-box}html{scroll-behavior:smooth;scroll-padding-top:30px}body{margin:0;color:var(--ink);background:var(--paper);font-family:'Malgun Gothic','Apple SD Gothic Neo',sans-serif;font-size:16px;line-height:1.85}nav{position:fixed;width:260px;inset:0 auto 0 0;overflow:auto;padding:35px 22px;background:#edf1e9;border-right:1px solid var(--line)}nav b{display:block;font-size:18px;margin-bottom:20px}nav a{display:block;color:#42584a;text-decoration:none;padding:6px 0;font-size:13px;line-height:1.65}nav a:hover{color:var(--green);text-decoration:underline}main{max-width:1210px;margin-left:260px;padding:60px 58px 100px}h1{font-size:38px;letter-spacing:-1.8px;line-height:1.4;margin:0 0 25px}h2{font-size:25px;line-height:1.5;margin:66px 0 20px;padding-top:20px;border-top:2px solid var(--line);letter-spacing:-.7px}h3{font-size:19px;margin-top:35px;color:var(--green)}h4{font-size:17px}p{max-width:900px;margin:14px 0}strong{color:#194c37}aside{padding:18px 22px;background:#e9f0e5;border-left:4px solid #75966c;margin:24px 0}.table-wrap{overflow:auto;margin:22px 0 28px}table{border-collapse:collapse;width:100%;min-width:650px;font-size:14px;line-height:1.75}th{background:#e8eee3;text-align:left;font-weight:700}td,th{padding:12px 14px;border:1px solid var(--line);vertical-align:top}tbody tr:nth-child(even){background:#f2f4ee}code{font-family:inherit;font-size:.9em;background:#eef0e8;padding:2px 5px;border-radius:4px}li{margin:8px 0}footer{margin-top:65px;color:var(--muted);font-size:13px}.badge{font-size:12px;letter-spacing:2px;color:var(--green);margin-bottom:15px}@media(max-width:1000px){nav{position:static;width:auto;max-height:290px}main{margin-left:0;padding:35px 22px}h1{font-size:29px}h2{font-size:23px}table{font-size:13px}}@media print{nav,.badge{display:none}main{margin:0;padding:0;max-width:none}body{font-size:10pt;background:white}h1{font-size:25pt}h2{font-size:17pt;break-after:avoid;margin-top:25pt}h3{break-after:avoid}table{min-width:0;font-size:8pt}td,th{padding:6px}tr{break-inside:avoid}.table-wrap{overflow:visible}a{color:inherit;text-decoration:none}aside{break-inside:avoid}}
${gallery.styles}
.shot-dialog-head strong{color:#f0efdc}.nav-preview{margin:0 0 17px;padding:10px 12px;background:#dce7d5;border:1px solid #cad9bf;border-radius:5px;font-weight:700}
</style></head><body><nav><b>마법학교 생활 RPG<br>시스템·콘텐츠와 예상 화면</b><a href="#screen-overview" class="nav-preview">예상 게임화면 16개 보기 ↗</a>${toc.join('')}</nav><main><div class="badge">MAGIC CAMPUS / DESIGN PROPOSAL 02 · VISUAL EDITION</div>${result.join('\n')}<footer>2026-11-01 · 예상 게임화면 포함판 · 이미지와 UI는 AI 생성 시안이며 실제 구현 화면이 아닙니다. 플레이 시간과 밸런스 수치는 프로토타입에서 검증할 목표값입니다.</footer></main>${gallery.dialog}</body></html>`;
fs.writeFileSync(path.join(__dirname,'마법학교_최초기획안.html'),html,'utf8');
console.log(JSON.stringify({sections:toc.length,characters:lines.join('\n').length,htmlBytes:Buffer.byteLength(html)}));
