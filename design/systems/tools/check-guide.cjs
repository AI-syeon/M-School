// Pure document-script harness. No browser automation or Unity gameplay execution.
const fs=require('node:fs'),path=require('node:path'),vm=require('node:vm'),assert=require('node:assert/strict');
const root=path.resolve(__dirname,'..');
const html=fs.readFileSync(path.join(root,'시스템_기획서.html'),'utf8');
class Element {
  constructor(tag='div'){this.tagName=tag.toUpperCase();this.attributes={};this.dataset={};this.events={};this.children=[];this._text='';this.hidden=false;this.open=false;this.disabled=false;this.className='';this.parentElement=null;const classes=new Set();this.classList={toggle:(name,force)=>{const on=force??!classes.has(name);if(on)classes.add(name);else classes.delete(name);return on;},contains:name=>classes.has(name)};}
  set textContent(value){this._text=String(value);this.children=[];}
  get textContent(){return this._text+this.children.map(c=>c.textContent).join('');}
  setAttribute(name,value){this.attributes[name]=String(value);}
  getAttribute(name){return this.attributes[name]??null;}
  addEventListener(event,callback){(this.events[event]??=[]).push(callback);}
  append(...children){for(const c of children){c.parentElement=this;this.children.push(c);}}
  replaceChildren(...children){this._text='';this.children=[];this.append(...children);}
  scrollIntoView(){this.scrolled=true;}
  click(){if(!this.disabled)for(const cb of this.events.click||[])cb({target:this});}
}
const ids=new Map();
for(const [,tag,id]of html.matchAll(/<([a-z][a-z0-9-]*)\b[^>]*\bid="([^"]+)"[^>]*>/gi)){const e=new Element(tag);e.id=id;ids.set(id,e);}
const data=html.match(/<script id="guide-data" type="application\/json">([\s\S]*?)<\/script>/)[1];
ids.get('guide-data').textContent=data;
const steps=[...html.matchAll(/<button[^>]*data-step="(\d+)"/g)].map(m=>{const e=new Element('button');e.dataset.step=m[1];return e;});
const choices=['notes','tea'].map(key=>{const e=new Element('button');e.dataset.choice=key;return e;});
const details=[...html.matchAll(/<details\b([^>]*)>/g)].map(m=>{const id=m[1].match(/id="([^"]+)"/)?.[1];return id?ids.get(id):new Element('details');});
const fullDetails=details.filter(e=>e.id?.startsWith('spec-'));
const labels=[new Element('text'),new Element('text')],mapPair=new Element();
const document={
  getElementById:id=>{assert(ids.has(id),'Unknown queried id: '+id);return ids.get(id);},
  createElement:tag=>new Element(tag),
  querySelector:selector=>{assert.equal(selector,'.map-pair');return mapPair;},
  querySelectorAll:selector=>{const found={'[data-step]':steps,'[data-choice]':choices,'.route-state-label':labels,'#full-spec > details':fullDetails,'details':details}[selector];assert(found,'Unknown selector: '+selector);return found;}
};
const windowEvents={};const window={addEventListener:(name,cb)=>(windowEvents[name]??=[]).push(cb),print:()=>{window.printed=true;}};
const location={hash:''};ids.get('route-case').value='normal';
const js=html.match(/<script>([\s\S]*?)<\/script>/)[1];
vm.runInNewContext(js,{document,window,location,requestAnimationFrame:fn=>fn(),console},{timeout:1000});
const checkNames=[];const check=(name,fn)=>{fn();checkNames.push(name);};
check('initial step, state and prev button',()=>{assert.equal(ids.get('step-count').textContent,'STEP 01 / 07');assert.equal(ids.get('scene-time').textContent,'07:00');assert(ids.get('prev-step').disabled);});
check('all seven event steps',()=>{for(let i=0;i<7;i++){steps[i].click();assert.equal(ids.get('step-title').textContent,JSON.parse(data).steps[i].title);assert.equal(ids.get('step-call').textContent,JSON.parse(data).steps[i].call);assert(JSON.parse(data).steps[i].call.length>0);assert.equal(steps.filter(e=>e.getAttribute('aria-pressed')==='true').length,1);}});
check('completed event time, memory and movement marker',()=>{assert.equal(ids.get('scene-time').textContent,'07:10');assert.equal(ids.get('scene-memory').textContent,'함께한 기억 저장됨');assert.equal(ids.get('serin-marker').getAttribute('transform'),'translate(380 193)');assert(ids.get('next-step').disabled);});
check('step calls match the real event positions',()=>{const bundle=JSON.parse(fs.readFileSync(path.join(root,'examples/campus-demo.json'),'utf8')),ev=bundle.events.find(e=>e.id==='event.snack'),s=JSON.parse(data).steps;assert(s[2].call.includes('event.snack#1')&&ev.script.startsWith('move npc.serin dest.snack'));assert(s[4].call.includes('event.snack#2')&&ev.script.includes('say npc.serin'));assert(s[5].call.includes('event.snack#3')&&ev.script.includes('choice'));assert(Object.keys(ev.forks).every(k=>s[5].call.includes('event.snack:'+k+'#1')));assert(s[6].call.includes(ev.effects));assert(s[0].call.includes(ev.when));assert(html.includes('event.snack:notes#1')&&!/cmd\.|&quot;commands&quot;|&quot;interactions&quot;/.test(html));});
check('both choice responses',()=>{steps[5].click();assert(!ids.get('scene-choices').hidden);choices[1].click();assert(ids.get('scene-dialogue-text').textContent.includes('따뜻한 차'));choices[0].click();assert(ids.get('scene-dialogue-text').textContent.includes('마음이 놓여'));});
check('previous and next controls',()=>{ids.get('prev-step').click();assert.equal(ids.get('step-count').textContent,'STEP 05 / 07');ids.get('next-step').click();assert.equal(ids.get('step-count').textContent,'STEP 06 / 07');});
check('normal, closed and blocked routes',()=>{for(const mode of ['normal','closed','blocked']){ids.get('route-case').value=mode;ids.get('route-case').events.change[0]();assert.equal(mapPair.className,'map-pair route-case-'+mode);assert.equal(ids.get('route-result').classList.contains('error'),mode!=='normal');assert(ids.get('route-result').textContent.includes(JSON.parse(data).routeCases[mode].title));}});
check('restore safe entry and complete states',()=>{ids.get('restore-complete').click();assert(ids.get('restore-output').textContent.includes('07:10'));assert(ids.get('restore-output').textContent.includes('안전 상태 B'));ids.get('restore-entry').click();assert(ids.get('restore-output').textContent.includes('07:00'));assert(ids.get('restore-output').textContent.includes('안전 상태 A'));});
check('expand and collapse full specification',()=>{ids.get('expand-all').click();assert(fullDetails.every(e=>e.open));ids.get('collapse-all').click();assert(fullDetails.every(e=>!e.open));});
check('embedded schema and example JSON identity',()=>{
  for(const [file,id]of [['schemas/content.schema.json','content-schema'],['schemas/save.schema.json','save-schema'],['examples/campus-demo.json','content-example'],['examples/save-v1.json','save-example'],['examples/expected-trace.json','expected-trace'],['validation/report.json','validation-report']]){
    const block=html.slice(html.indexOf('id="spec-'+id+'"'));
    const encoded=block.match(/<pre><code>([\s\S]*?)<\/code><\/pre>/)[1];
    const decoded=encoded.replace(/&quot;/g,'"').replace(/&gt;/g,'>').replace(/&lt;/g,'<').replace(/&amp;/g,'&');
    assert.deepEqual(JSON.parse(decoded),JSON.parse(fs.readFileSync(path.join(root,file),'utf8')));
  }
});
check('print opens details and restores prior states',()=>{fullDetails[0].open=true;const initial=details.map(e=>e.open);windowEvents.beforeprint[0]();assert(details.every(e=>e.open));windowEvents.afterprint[0]();assert.deepEqual(details.map(e=>e.open),initial);ids.get('print-guide').click();assert(window.printed);});
check('internal reference opens its parent document',()=>{location.hash='#spec-architecture';windowEvents.hashchange[0]();assert(ids.get('spec-architecture').open);assert(ids.get('spec-architecture').scrolled);});
const report={artifact:'시스템_기획서.html',scope:'Pure document-script harness and embedded data identity; no actual browser rendering',checks:checkNames.map(name=>({name,status:'pass'})),browserRendering:{status:'not-executed',reason:'In-app browser could not connect to local preview; local file navigation was blocked by URL policy; native Chrome access was not approved. No browser restriction bypass was attempted.'},responsiveDesign:{cssBreakpoints:[1200,1000,680],actualViewportRendering:'not-executed'},gameRuntime:'not-implemented'};
fs.writeFileSync(path.join(root,'validation/html-interaction-report.json'),JSON.stringify(report,null,2)+'\n');
console.log(JSON.stringify(report,null,2));
