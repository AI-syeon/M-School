const fs=require('node:fs');
const path=require('node:path');
const md=fs.readFileSync(path.join(__dirname,'마법학교_최초기획안.md'),'utf8');
const count=re=>(md.match(re)||[]).length;
const quantities={
  sections:count(/^## /gm),
  spells:count(/^\| M\d{2} /gm),
  npcs:count(/^\| N\d{2} /gm),
  projects:count(/^\| P\d{2} /gm),
  sideRooms:count(/^\| X\d{2} /gm),
  relationshipEvents:new Set(md.match(/R\d{2}/g)).size,
  mainNodes:new Set(md.match(/\b[A-F][123]\b/g)).size,
  clubs:new Set(md.match(/\bC0[1-8]\b/g)).size,
  festivals:count(/^\| H0[1-4] /gm)
};
const expected={sections:17,spells:12,npcs:12,projects:12,sideRooms:12,relationshipEvents:36,mainNodes:18,clubs:8,festivals:4};
for(const [key,value] of Object.entries(expected)){if(quantities[key]!==value)throw Error(`${key}: ${quantities[key]} != ${value}`);}
const minutes=[90,18*25,22*7,36*9,12*20,12*20,4*24,8*12,24*8,80*2,270,90];
const total=minutes.reduce((a,b)=>a+b,0);
if(total!==2402)throw Error('Time budget mismatch');
if(/TODO|TBD|작성 예정|16–24시간/.test(md))throw Error('Outdated placeholder or estimate');
const html=fs.readFileSync(path.join(__dirname,'마법학교_최초기획안.html'),'utf8');
const ids=new Set([...html.matchAll(/id="([^"]+)"/g)].map(x=>x[1]));
for(const match of html.matchAll(/href="#([^"]+)"/g)){if(!ids.has(match[1]))throw Error('Missing anchor '+match[1]);}
if(count(/^\| /gm)!==(html.match(/<tr>/g)||[]).length)throw Error('Markdown table row count mismatch');
const crypto=require('node:crypto');
const manifest=require('./screens/manifest.json');
const figures=html.match(/<figure class="game-figure"/g)||[];
const embedded=[...html.matchAll(/<img src="data:image\/png;base64,([A-Za-z0-9+/=]+)"/g)];
if(figures.length!==16||embedded.length!==16||manifest.screens.length!==16)throw Error('Expected 16 distinct game screens');
const diskHashes=new Set(manifest.screens.map(s=>crypto.createHash('sha256').update(fs.readFileSync(path.join(__dirname,'screens',s.id+'.png'))).digest('hex')));
const embeddedHashes=new Set(embedded.map(m=>crypto.createHash('sha256').update(Buffer.from(m[1],'base64')).digest('hex')));
if(diskHashes.size!==16||embeddedHashes.size!==16||[...diskHashes].some(h=>!embeddedHashes.has(h)))throw Error('Embedded images differ from saved assets');
if(/<img[^>]+src="(?!data:)/.test(html))throw Error('External image dependency');
for(const match of html.matchAll(/<script>([\s\S]*?)<\/script>/g))new Function(match[1]);
console.log(JSON.stringify({quantities,minutes:total,hours:total/60,mainRequiredMinutes:90+450+90,tableRows:count(/^\| /gm),tocLinks:(html.match(/href="#/g)||[]).length,uniqueScreens:figures.length,embeddedImages:embedded.length,htmlMB:Math.round(Buffer.byteLength(html)/104857.6)/10,note:'Document structure, arithmetic and embedded asset integrity only; no gameplay or browser layout validation.'},null,2));
