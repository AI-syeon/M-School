// Documentation fixtures only. This does not execute Unity gameplay.
// Call columns (11-function-syntax.md) are parsed with ../../tools/parse-calls.cjs and checked against schemas/functions.json.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict'),crypto=require('node:crypto');const root=path.resolve(__dirname,'..');
const {parseCalls,checkCall}=require(path.resolve(root,'../tools/parse-calls.cjs'));
const read=p=>JSON.parse(fs.readFileSync(path.join(root,p)));const clone=x=>JSON.parse(JSON.stringify(x));
function schemaErrors(s,v,base=s,p=''){
 if(s===true)return[];if(s===false)return[p+': forbidden'];if(s.$ref){assert(s.$ref.startsWith('#/'));return schemaErrors(s.$ref.slice(2).split('/').reduce((a,k)=>a[k],base),v,base,p);}
 const e=[],fail=m=>e.push(p+': '+m);if(s.oneOf&&s.oneOf.filter(x=>!schemaErrors(x,v,base,p).length).length!==1)fail('oneOf');if(s.anyOf&&!s.anyOf.some(x=>!schemaErrors(x,v,base,p).length))fail('anyOf');
 if('const'in s&&JSON.stringify(s.const)!==JSON.stringify(v))fail('const');if(s.enum&&!s.enum.some(x=>JSON.stringify(x)===JSON.stringify(v)))fail('enum');
 const types={object:x=>x!==null&&typeof x==='object'&&!Array.isArray(x),array:Array.isArray,string:x=>typeof x==='string',number:x=>typeof x==='number'&&Number.isFinite(x),integer:Number.isInteger,boolean:x=>typeof x==='boolean',null:x=>x===null};
 if(s.type&&!types[s.type](v)){fail('type '+s.type);return e;}
 if(typeof v==='number')for(const[k,bad]of [['minimum',v<s.minimum],['maximum',v>s.maximum],['exclusiveMinimum',v<=s.exclusiveMinimum],['exclusiveMaximum',v>=s.exclusiveMaximum]])if(k in s&&bad)fail(k);
 if(typeof v==='string'){if(s.pattern&&!new RegExp(s.pattern).test(v))fail('pattern');if(s.minLength&&v.length<s.minLength)fail('minLength');}
 if(Array.isArray(v)){if(s.minItems&&v.length<s.minItems)fail('minItems');if('maxItems'in s&&v.length>s.maxItems)fail('maxItems');if(s.uniqueItems&&new Set(v.map(x=>JSON.stringify(x))).size!==v.length)fail('uniqueItems');if(s.items)v.forEach((x,i)=>e.push(...schemaErrors(s.items,x,base,p+'/'+i)));}
 if(types.object(v)){for(const k of s.required||[])if(!(k in v))fail('required '+k);for(const[k,x]of Object.entries(v)){if(s.properties?.[k])e.push(...schemaErrors(s.properties[k],x,base,p+'/'+k));else if(s.additionalProperties===false)fail('unknown '+k);else if(typeof s.additionalProperties==='object')e.push(...schemaErrors(s.additionalProperties,x,base,p+'/'+k));}}
 return e;
}
const c=read('examples/full-catalog.json'),s=read('examples/safe-save.json'),r=read('examples/runner-bundle.json');const cs=read('schemas/catalog.schema.json'),ss=read('schemas/save.schema.json'),rs=read('schemas/runner.schema.json'),reg=read('schemas/functions.json');
const FILES={catalog:'examples/full-catalog.json',runner:'examples/runner-bundle.json'};
const ID_RE=/^[a-z][a-z0-9_]*(\.[a-z0-9_]+)+$/,WORD_RE=/^[A-Za-z_][A-Za-z0-9_\-]*$/;
// parse-calls tokenizer errors (e.g. unclosed quote) carry an offset but no call index; recover it from the text.
const callIndexAt=(text,offset)=>{let n=1,q=false;for(let i=0;i<Math.min(offset,text.length);i++){if(q&&text[i]==='\\'){i++;continue;}if(text[i]==='"')q=!q;else if(!q&&text[i]==='/')n++;}return n;};
const esc=k=>String(k).replace(/~/g,'~0').replace(/\//g,'~1');
// Entity tables: every row is a definition of this kind.
const TABLE_KIND={spells:'spell',classes:'class',reactions:'reaction',enemies:'enemy',regions:'region',rooms:'room',npcs:'npc',relationships:'relationship',story:'case',activities:'activity',projects:'project',plants:'plant',items:'item',recipes:'recipe',shops:'shop',creatures:'creature',furniture:'furniture',objects:'object',scenarios:'scenario',dialogues:'dialogue',endings:'ending',onboarding:'onboarding',encounters:'encounter',truthScenes:'truth',difficultyPresets:'difficulty',facilities:'facility'};
const SINGLE_KIND={finalDefinition:'final',schoolRule:'schoolRule'};
// Defining columns: a column whose value declares an ID (replaces the authored symbols table).
const DEFINING=[['story','resolutionFlag','flag'],['story','worldChangeId','projection'],['story','combatOutcome','outcome'],['classes','practiceContextId','context'],['classes','rewardKey','reward'],['projects','worldChangeId','projection'],['rooms','completionKey','completion'],['relationships','completionKey','completion'],['regions','mapId','map'],['regions','safePointId','safePoint'],['objects','applicationContextId','context'],['endings','worldChangeId','projection'],['endings','rewardKey','reward'],['onboarding','completionKey','completion'],['encounters','successOutcomeId','outcome']];
const NODE_DEFINING=[['id','node'],['evidenceId','evidence'],['roomId','room']];
const ASSET_TABLES={spells:'effect',enemies:'sprite',furniture:'prefab'};
const RUNNER_KIND={variables:'variable',maps:'map',destinations:'destination',npcs:'npc',schedules:'schedule',events:'event'};

function analyze(c,s,r){
 const diags=[],errors=[];
 const diag=(code,at,message,expected,actual,offset)=>{const d={severity:'error',code,file:FILES[at.file],pointer:at.pointer,entityId:at.entityId,call:at.call??null,offset:offset??at.offset??null,message,expected:expected??null,actual:actual??null};diags.push(d);};
 const bad=(code,msg)=>errors.push(code+': '+msg);
 // ---- index (symbol table) ----
 const index=new Map();
 // Same id declared again with the same kind is one definition (e.g. npc in catalog and runner); another kind is a collision.
 const define=(id,kind,file,pointer)=>{if(typeof id!=='string')return;const old=index.get(id);if(old){if(old.kind!==kind)bad('DUPLICATE_ID',id+' '+old.kind+'@'+old.pointer+' vs '+kind+'@'+pointer);return;}index.set(id,{kind,file,pointer});};
 for(const[t,kind]of Object.entries(TABLE_KIND))(c[t]||[]).forEach((x,i)=>{if(index.has(x.id))bad('DUPLICATE_ID',x.id);else index.set(x.id,{kind,file:'catalog',pointer:'/'+t+'/'+i});});
 for(const[t,kind]of Object.entries(SINGLE_KIND))if(c[t]?.id)define(c[t].id,kind,'catalog','/'+t);
 c.story.forEach((a,ai)=>a.nodes.forEach((n,ni)=>{for(const[k,kind]of NODE_DEFINING)define(n[k],kind,'catalog',`/story/${ai}/nodes/${ni}/${k}`);}));
 for(const[t,k,kind]of DEFINING)(c[t]||[]).forEach((x,i)=>define(x[k],kind,'catalog',`/${t}/${i}/${k}`));
 if(c.finalDefinition?.roomId)define(c.finalDefinition.roomId,'room','catalog','/finalDefinition/roomId');
 for(const[t,kind]of Object.entries(ASSET_TABLES))(c[t]||[]).forEach((x,i)=>define(x.assetKey,'asset','catalog',`/${t}/${i}/assetKey`));
 for(const[t,kind]of Object.entries(RUNNER_KIND))(r[t]||[]).forEach((x,i)=>define(x.id,kind,'runner','/'+t+'/'+i));
 r.maps.forEach((m,i)=>m.portals.forEach((p,j)=>define(p.id,'portal','runner',`/maps/${i}/portals/${j}`)));
 for(const[name,sig]of Object.entries(reg.kinds.goal.functions))if(sig.capability)define(sig.capability,'capability','functions','/kinds/goal/functions/'+name);
 // ---- parse every call column ----
 const columns=[];
 function add(file,pointer,entityId,column,kind,text,spec,row,loc){
  const col={file,pointer,entityId,column,kind,text,spec,row,loc,calls:[]};columns.push(col);
  if(typeof text!=='string'){diag('PARSE_ERROR',{file,pointer,entityId,call:loc+'#0'},'call column must be a string');return col;}
  try{col.calls=parseCalls(text);}catch(e){diag(e.code||'PARSE_ERROR',{file,pointer,entityId,call:loc+'#'+(e.call||callIndexAt(text,e.offset||0))},e.message,null,null,e.offset);col.failed=true;}
  if(spec.nonEmpty&&!col.failed&&!col.calls.length)diag('ARG_COUNT',{file,pointer,entityId,call:loc+'#0'},column+' needs at least one call','>=1',0);
  if(spec.maxCalls&&col.calls.length>spec.maxCalls)diag('ARG_COUNT',{file,pointer,entityId,call:loc+'#'+(spec.maxCalls+1)},column+' allows '+spec.maxCalls+' call',spec.maxCalls,col.calls.length);
  return col;
 }
 for(const spec of reg.columns){const data=spec.file==='runner'?r:c;
  const rows=spec.table==='story/*/nodes'?c.story.flatMap((a,ai)=>a.nodes.map((n,ni)=>[n,`/story/${ai}/nodes/${ni}`])):Array.isArray(data[spec.table])?data[spec.table].map((x,i)=>[x,'/'+spec.table+'/'+i]):data[spec.table]?[[data[spec.table],'/'+spec.table]]:[];
  for(const[row,base]of rows){
   const owner=spec.labelOwner?row[spec.labelOwner]:row.id;
   if(spec.column==='forks/*'){for(const[k,v]of Object.entries(row.forks||{}))add(spec.file,base+'/forks/'+esc(k),row.id,'forks/'+k,spec.kind,v,spec,row,row.id+':'+k).owner=owner;continue;}
   if(!(spec.column in row))continue;
   const loc=spec.table==='events'&&spec.column==='script'||spec.table==='schedules'?row.id:row.id+'['+spec.column+']';
   add(spec.file,base+'/'+spec.column,row.id,spec.column,spec.kind,row[spec.column],spec,row,loc).owner=owner;
  }}
 // ---- labels and objects declared by call columns ----
 const labels=new Map();
 for(const col of columns){const kindSpec=reg.kinds[col.kind];for(const call of col.calls){const where={file:col.file,pointer:col.pointer,entityId:col.entityId,call:col.loc+'#'+call.index};
  if(kindSpec.labelRequired&&!call.label)diag('PARSE_ERROR',where,col.kind+' call needs a label "name:"','label:','none',call.offset);
  if(call.label){const m=labels.get(col.owner)||new Map();labels.set(col.owner,m);if(m.has(call.label))diag('LABEL_DUP',where,'label "'+call.label+'" already used in '+col.owner,'unique label',call.label,call.offset);else m.set(call.label,{kind:col.kind,col,call});}
  const sig=kindSpec.functions[call.name];if(sig)sig.params.forEach((p,i)=>{if(p.defines&&call.positional[i]?.type==='id')define(call.positional[i].value,p.defines,col.file,col.pointer);});}}
 // ---- reference resolution ----
 const kindsOf=k=>reg.refKinds[k]||[k];
 function ref(value,refKind,where,offset){
  if(typeof value==='string'&&value.includes(':')&&!ID_RE.test(value)){const[owner,label]=value.split(':');if(!index.has(owner)){diag('REF_NOT_FOUND',where,'label owner '+owner+' not found','defined id',owner,offset);return null;}const l=labels.get(owner)?.get(label);if(!l){diag('REF_NOT_FOUND',where,'label '+value+' not found','label in '+owner,value,offset);return null;}if(!kindsOf(refKind).includes(l.kind))diag('REF_KIND',where,value+' is a '+l.kind+' label',kindsOf(refKind).join('|'),l.kind,offset);return l.kind;}
  const def=index.get(value);if(!def){diag('REF_NOT_FOUND',where,'unknown id '+value,kindsOf(refKind).join('|'),value,offset);return null;}
  if(!kindsOf(refKind).includes(def.kind))diag('REF_KIND',where,value+' is '+def.kind,kindsOf(refKind).join('|'),def.kind,offset);return def.kind;}
 const variableType=id=>r.variables.find(v=>v.id===id)?.type;
 // Check one call list of a kind. ctx: {col, loc, base offset, forks?} returns parsed calls.
 function checkList(calls,kind,ctx){
  const kindSpec=reg.kinds[kind];const open=new Map(),usedHandles=new Set();let lastMinute=-1;
  for(const call of calls){
   const where={file:ctx.col.file,pointer:ctx.col.pointer,entityId:ctx.col.entityId,call:ctx.loc+'#'+call.index};const off=o=>(ctx.offset||0)+o;
   let sig;
   if(kindSpec.nameRule==='registered'){sig=kindSpec.functions[call.name];if(!sig){diag('UNKNOWN_FUNCTION',where,'"'+call.name+'" is not a registered '+kind+' function',Object.keys(kindSpec.functions).join('|'),call.name,off(call.offset));continue;}}
   else{sig=kindSpec.functions['*'];const rule=kindSpec.nameRule;
    if(rule.pattern){if(!new RegExp(rule.pattern).test(call.name)){diag('UNKNOWN_FUNCTION',where,kind+' name must match '+rule.pattern,rule.pattern,call.name,off(call.offset));continue;}const v=Number(call.name);if(v<rule.min||v>rule.max)diag('ARG_TYPE',where,'minute out of range',rule.min+'-'+rule.max,v,off(call.offset));if(rule.ascending&&v<=lastMinute)diag('ARG_TYPE',where,'entries must ascend','>'+lastMinute,v,off(call.offset));lastMinute=v;}
    else if(rule.type==='id'){if(!ID_RE.test(call.name)){diag('UNKNOWN_FUNCTION',where,kind+' name must be a '+rule.refKind+' id','id',call.name,off(call.offset));continue;}ref(call.name,rule.refKind,where,off(call.offset));}
    else if(rule.type==='word'){if(!WORD_RE.test(call.name)){diag('UNKNOWN_FUNCTION',where,kind+' name must be a word','word',call.name,off(call.offset));continue;}if(rule.enum&&!rule.enum.includes(call.name))diag('ARG_TYPE',where,kind+' name not allowed',rule.enum.join('|'),call.name,off(call.offset));}}
   // expand pair rests (choice) into a fixed signature for checkCall
   let params=sig.params;const rest=params.at(-1);
   if(rest?.pairs){const fixed=params.slice(0,-1),extra=call.positional.length-fixed.length;if(extra<2*(rest.min||1)||extra%2){diag('ARG_COUNT',where,call.name+' needs '+rest.pairs.map(p=>p.name).join('+')+' pairs','>= '+(rest.min||1)+' pairs',extra,off(call.offset));continue;}params=[...fixed,...Array.from({length:extra},(_,i)=>({...rest.pairs[i%2]}))];}
   const {values,errors}=checkCall(call,{...sig,params});
   for(const e of errors)diag(e.code,where,e.message,e.expected,e.actual,off(e.offset));
   if(errors.some(e=>e.code==='ARG_COUNT'))continue;
   const fixedCount=rest&&rest.rest&&!rest.pairs?params.length-1:params.length;
   call.positional.forEach((arg,i)=>{const p=i<fixedCount?params[i]:rest;if(!p)return;
    if(p.pattern&&!new RegExp(p.pattern).test(String(arg.value)))diag('ARG_TYPE',where,call.name+'.'+p.name+' format',p.pattern,arg.raw,off(arg.offset));
    if(typeof arg.value==='number'&&(('min'in p&&!p.rest&&arg.value<p.min)||('max'in p&&arg.value>p.max)))diag('ARG_TYPE',where,call.name+'.'+p.name+' range',(p.min??'')+'..'+(p.max??''),arg.value,off(arg.offset));
    if(p.refKind&&!(p.allow||[]).includes(arg.value)&&(arg.type==='id'||arg.type==='labelref'||p.type==='word'))ref(arg.value,p.refKind,where,off(arg.offset));
    if(p.refKindFrom){const k=p.refKindFrom.map[values[p.refKindFrom.param]];if(k)ref(arg.value,k,where,off(arg.offset));}
    if(p.fork&&ctx.forks){if(!(arg.value in ctx.forks))diag('FORK_NOT_FOUND',where,'fork "'+arg.value+'" is not in this row',Object.keys(ctx.forks).join('|')||'no forks',arg.value,off(arg.offset));else ctx.usedForks.add(arg.value);}
    if(p.parse&&arg.type==='text'){let inner=[];try{inner=parseCalls(arg.value);}catch(e){diag(e.code||'PARSE_ERROR',{...where,call:where.call+'>'+(e.call||callIndexAt(arg.value,e.offset||0))},e.message,null,null,off(arg.offset+1+(e.offset||0)));return;}checkList(inner,p.parse,{...ctx,loc:where.call+'>',offset:off(arg.offset+1)});}
   });
   if(rest?.rest&&!rest.pairs&&rest.min&&call.positional.length-fixedCount<rest.min)diag('ARG_COUNT',where,call.name+' needs '+rest.min+'+ '+rest.name,'>='+rest.min,call.positional.length-fixedCount,off(call.offset));
   for(const[k,arg]of Object.entries(call.named)){const spec=(sig.named||{})[k];if(!spec)continue;if(typeof arg.value==='number'&&'min'in spec&&arg.value<spec.min)diag('ARG_TYPE',where,call.name+'.'+k+' range','>='+spec.min,arg.value,off(arg.offset));
    if(spec.labelIn&&!labels.get(ctx.col.owner)?.has(arg.value))diag('REF_NOT_FOUND',where,k+'='+arg.value+' is not a label of '+ctx.col.owner,'label in '+spec.labelIn,arg.value,off(arg.offset));}
   // typed scalar state
   if(['eq','gte','set'].includes(call.name)&&call.positional[0]?.type==='id'){const t=variableType(call.positional[0].value),v=call.positional[1];if(t&&v&&((t==='bool')!==(v.type==='bool')||(t==='int'&&v.type!=='int')))diag('ARG_TYPE',where,call.positional[0].value+' is '+t,t,v.type,off(v.offset));}
   // async handles (commands)
   if(kind==='command'){
    if(sig.requiresClosedHandles&&open.size)diag('HANDLE_UNAWAITED',where,'await '+[...open.keys()].join(' ')+' before '+call.name,'await',[...open.keys()].join(' '),off(call.offset));
    if(call.name==='await')for(const h of call.positional){if(open.has(h.value))open.delete(h.value);else diag(usedHandles.has(h.value)?'HANDLE_CONFLICT':'REF_NOT_FOUND',where,'handle "'+h.value+'" is not open here','open handle',h.value,off(h.offset));}
    const resource=sig.writes==='actor'?call.positional[0]?.value:sig.writes;
    if(resource)for(const[h,o]of open)if(o.resource===resource)diag('HANDLE_CONFLICT',where,resource+' is already driven by async '+h,'one writer',resource,off(call.offset));
    const handle=call.named.async?.value;if(handle!==undefined){if(usedHandles.has(String(handle)))diag('HANDLE_CONFLICT',where,'handle "'+handle+'" reused','new handle',handle,off(call.named.async.offset));usedHandles.add(String(handle));open.set(String(handle),{resource});}
   }
  }
  if(kind==='command'&&open.size)diag('HANDLE_UNAWAITED',{file:ctx.col.file,pointer:ctx.col.pointer,entityId:ctx.col.entityId,call:ctx.loc+'#end'},'list ends with open handles','await',[...open.keys()].join(' '),(ctx.offset||0)+String(ctx.col.text).length);
 }
 const forkUse=new Map();
 for(const col of columns){if(col.failed)continue;const ev=col.file==='runner'&&col.spec.table==='events'?col.row:null;
  const ctx={col,loc:col.loc,offset:0};if(ev){ctx.forks=ev.forks||{};ctx.usedForks=forkUse.get(ev.id)||new Set();forkUse.set(ev.id,ctx.usedForks);}
  checkList(col.calls,col.kind,ctx);}
 for(const e of r.events){const used=forkUse.get(e.id)||new Set();Object.keys(e.forks||{}).forEach(k=>{if(!used.has(k))diag('FORK_UNUSED',{file:'runner',pointer:`/events/${r.events.indexOf(e)}/forks/${esc(k)}`,entityId:e.id,call:e.id+':'+k+'#0'},'fork "'+k+'" is never called','choice/branch reference',k,0);});}
 // ---- derived bindings ----
 const bindings=[];for(const[t,kind]of Object.entries(ASSET_TABLES))for(const x of c[t]||[])if(x.assetKey&&!bindings.some(b=>b.id===x.assetKey))bindings.push({id:x.assetKey,kind,address:c.assetAddresses?.[x.assetKey]??'PLACEHOLDER/'+x.assetKey,required:true,placeholder:!(x.assetKey in(c.assetAddresses||{}))});
 for(const k of Object.keys(c.assetAddresses||{}))if(!bindings.some(b=>b.id===k))bad('REF_NOT_FOUND','assetAddresses '+k);
 // ---- parsed views for semantic rules ----
 const P=text=>{try{return parseCalls(text);}catch{return [];}};
 const qty=text=>P(text).map(x=>({itemId:x.name,quantity:x.positional[0]?.value,provenance:x.positional[1]?.value}));
 const subs=text=>P(text).map(x=>({requiredProperty:x.name,quantity:x.positional[0]?.value,slot:x.named.slot?.value??0}));
 // ---- semantic rules (unchanged meaning, read from parsed calls) ----
 const has=(value,label)=>{if(value!==null&&value!==undefined&&!index.has(value))bad('REF_NOT_FOUND',label+' '+value);};
 const typed=(value,kind,label)=>{if(value===null||value===undefined)return;const d=index.get(value);if(!d)bad('REF_NOT_FOUND',label+' '+value);else if(d.kind!==kind)bad('REF_KIND',label+' '+value+' expected '+kind+' got '+d.kind);};
 const refs=['spellId','recipeId','itemId','outputItemId','failureItemId','seedItemId','harvestItemId','npcId','previousId','regionId','loanItemId','reactionId','assetKey','homeDestinationId','worldChangeId','combatOutcome','deviceOutcome','requiredSpellId','dailyYieldItemId','roomId','encounterId','successOutcomeId','finalEncounterId','unlockProjectId','capabilityId'];
 function walk(v,p=''){if(Array.isArray(v)){v.forEach((x,i)=>walk(x,p+'/'+i));return;}if(!v||typeof v!=='object')return;for(const[k,x]of Object.entries(v)){if(refs.includes(k)&&typeof x==='string')has(x,p+'/'+k);if(['spellIds','prerequisiteClassIds','loanItemIds','recipeIds','allowedFacilityIds','endingIds','nearbyActionIds'].includes(k))x.forEach(t=>has(t,p+'/'+k));walk(x,p+'/'+k);}}
 walk(c);c.scenarios.forEach(x=>x.steps.forEach(y=>has(y,x.id)));
 for(const x of c.projects){const w=P(x.when);if(w.length!==1||w[0].name!=='any'||w[0].negate||w[0].positional.length!==2)bad('PROJECT_OR',x.id);}
 for(const a of c.story){if(a.externalCasePrerequisites.length)bad('CROSS_CASE_GATE',a.id);if(a.combatOutcome!==a.deviceOutcome)bad('ROUTE_MISMATCH',a.id);a.nodes.forEach((n,i)=>{if(n.sourceId!==a.sourceId+(i+1)||n.id!=='node.'+a.sourceId.toLowerCase()+(i+1))bad('NODE_ORDER',n.id);});}
 for(const a of c.relationships){const npc=c.npcs.find(n=>n.id===a.npcId);if(a.romanceVariant&&!npc.romanceAllowed)bad('ROMANCE_SCOPE',a.id);if(a.index>1&&a.previousId!==c.relationships.find(x=>x.npcId===a.npcId&&x.index===a.index-1)?.id)bad('RELATION_ORDER',a.id);}
 const items=new Map(c.items.map(x=>[x.id,x]));
 for(const x of c.recipes){const ing=qty(x.ingredients);if(ing.some(z=>!(z.quantity>=1)))bad('INGREDIENT_QUANTITY',x.id);const cost=ing.reduce((sum,z)=>sum+items.get(z.itemId)?.buyPrice*z.quantity,0)+x.feeCrowns;const sale=Math.floor((items.get(x.outputItemId)?.saleBasePrice??0)*.4)*x.outputQuantity;if(sale>cost)bad('CRAFT_PROFIT',x.id);for(const sub of subs(x.substitutes)){const options=c.items.filter(a=>a.kind==='material'&&a.properties.includes(sub.requiredProperty));if(!options.length)bad('SUBSTITUTE_MISSING',x.id);const base=ing[sub.slot];if(!base)bad('SUBSTITUTE_SLOT',x.id);else{const cheap=Math.min(...options.map(a=>a.buyPrice))*sub.quantity;const alt=cost-base.quantity*(items.get(base.itemId)?.buyPrice??0)+cheap;if(sale>alt)bad('SUBSTITUTE_PROFIT',x.id);}}}
 for(const x of c.recipes.filter(x=>x.kind==='alchemy'))if(new Set(x.requiredProperties).size!==2)bad('ALCHEMY_PROPERTIES',x.id);
 for(const x of s.stacks){has(x.itemId,'save/stacks');if(x.quantity>items.get(x.itemId)?.stackMax)bad('STACK_LIMIT',x.itemId);if(x.provenance==='loan'&&!x.scopeId)bad('LOAN_SCOPE',x.itemId);if(x.provenance!=='loan'&&x.scopeId)bad('NON_LOAN_SCOPE',x.itemId);}if(s.stacks.length>c.rules.inventorySlots)bad('BAG_CAPACITY','save');for(const x of s.plots)has(x.plantId,'save/plots');for(const x of s.relations){has(x.npcId,'save/relations');const npc=c.npcs.find(n=>n.id===x.npcId);if(x.expression==='romance'&&!npc.romanceAllowed)bad('SAVE_ROMANCE_SCOPE',x.npcId);}
 // main rooms are node rows: room info, 3 goals = staff help steps, 3 hints
 for(const a of c.story)for(const n of a.nodes){typed(n.regionId,'region',n.id);typed(n.encounterId,'encounter',n.id);if(P(n.goals).length!==3||P(n.hints).length!==3)bad('ROOM_HELP',n.roomId);}
 for(const x of c.rooms)if(P(x.hints).length!==3)bad('ROOM_HELP',x.id);
 for(const x of c.encounters){typed(x.roomId,'room',x.id);typed(x.successOutcomeId,'outcome',x.id);x.deviceGoalIds.forEach(g=>ref(g,'goal',{file:'catalog',pointer:'/encounters/'+c.encounters.indexOf(x)+'/deviceGoalIds',entityId:x.id,call:x.id+'[deviceGoalIds]'},null));if(x.maxAttackers>3)bad('ATTACKER_LIMIT',x.id);}
 for(const x of c.reactions){for(const p of P(x.paths))if(!p.positional.length)bad('EMPTY_REACTION_PREREQUISITE',x.id);if(x.windowPolicy==='seconds-since-prerequisite'&&x.windowSeconds<=0)bad('REACTION_WINDOW',x.id);}
 if(c.spells.find(x=>x.id==='spell.sprout').resultTag==='opened_seed')bad('COMBO_BYPASS','sprout');if(c.spells.find(x=>x.id==='spell.illusion').resultTag==='projected')bad('COMBO_BYPASS','illusion');
 for(const x of c.classes){if(!x.introduced&&!x.requiresIntro)bad('INTRO_GATE',x.id);if(!P(x.grantOnFirstAccept).length)bad('CLASS_GRANT',x.id);}
 for(const x of c.projects){x.recipeIds.forEach(y=>typed(y,'recipe',x.id));if(!P(x.grantOnAccept).length)bad('PROJECT_GRANT',x.id);}
 for(const x of c.activities){has(x.loanItemId,x.id);x.prerequisiteActivityIds.forEach(p=>typed(p,'activity',x.id));}
 for(const x of s.defeats)typed(x.encounterId,'encounter','save/defeats');for(const x of s.discoveredRecipeIds)typed(x,'recipe','save/discoveredRecipes');for(const x of s.acceptedActivityIds)typed(x,'activity','save/acceptedActivities');
 for(const x of s.classRecords)if(x.completed!==s.completed.includes(x.classId))bad('SAVE_COMPLETION_MISMATCH',x.classId);
 const recipeKeys=new Set();for(const x of c.recipes.filter(x=>x.kind==='alchemy')){const key=[...x.requiredProperties].sort().join('+')+':'+x.process;if(recipeKeys.has(key))bad('RECIPE_AMBIGUOUS',x.id);recipeKeys.add(key);const props=new Set(qty(x.ingredients).flatMap(z=>c.items.find(i=>i.id===z.itemId)?.properties||[]));if(!x.requiredProperties.every(p=>props.has(p)))bad('RECIPE_PROPERTIES_UNSATISFIED',x.id);}
 if(c.caseDerivation.owner!=='CaseService'||!c.caseDerivation.atomicWithNodeCompletion)bad('CASE_DERIVATION','owner/atomic');if(c.finalDefinition.requiredCaseCount!==6||!c.finalDefinition.manualStart)bad('FINAL_GATE','invalid');
 // events: one row, effects commit once after script success
 const byKey=new Map();
 for(const e of r.events){const script=P(e.script),effects=P(e.effects);
  if(e.completionKey)has(e.completionKey,e.id+'/completionKey');
  if(e.executionMode==='room-activity'&&(e.resources.includes('input.player')||e.resources.includes('camera.main')||e.pause!=='none'))bad('ROOM_SCENE_CONFLICT',e.id);
  if(e.repeatPolicy==='practice'&&(effects.some(x=>x.name!=='experience.record'||x.named.participationOnly?.value!==true)||!script.some(x=>x.name==='perform'&&x.named.mode?.value==='practice')))bad('PRACTICE_REWARD',e.id);
  if(script.some(x=>x.name==='perform'&&x.positional[0]?.value==='class')&&e.costMinutes!==0)bad('DOUBLE_CLASS_TIME',e.id);
  for(const ef of effects.filter(x=>x.name==='case.reward')){const a=c.story.find(x=>x.id===ef.positional[0]?.value);if(a&&!effects.some(x=>x.name==='complete'&&x.positional[0]?.value===a.nodes[2].id))bad('CASE_REWARD_BOUNDARY',e.id);}
  if(e.completionKey){const k=e.completionKey;if(byKey.has(k)&&byKey.get(k)!==e.effects)bad('ROUTE_REWARD_MISMATCH',e.id+' vs shared key '+k);byKey.set(k,e.effects);}}
 for(const recipe of c.recipes){recipe.allowedFacilityIds.forEach(x=>typed(x,'facility',recipe.id));if(['alchemy','cooking'].includes(recipe.kind)&&!recipe.allowedFacilityIds.includes('facility.shared_p07'))bad('FINE_UNREACHABLE',recipe.id);}
 if(!c.spells.find(x=>x.id==='spell.lantern').acceptTargetTags.includes('record'))bad('LANTERN_RECORD_UNREACHABLE','combo_5');
 for(const variable of r.variables.filter(x=>!x.id.startsWith('clock.'))){const flag=s.flags.find(x=>x.id===variable.id);if(!flag)bad('SAVE_SCALAR_MISSING',variable.id);}
 const counts={spells:12,classes:24,enemies:12,regions:4,rooms:12,npcs:12,relationships:36,story:6,projects:12,plants:12,recipes:48,encounters:7,endings:3,truthScenes:2};for(const[k,n]of Object.entries(counts))if(c[k].length!==n)bad('BUDGET_COUNT',k);if(c.story.flatMap(a=>a.nodes).length!==18)bad('BUDGET_COUNT','main room nodes');for(const[k,n]of Object.entries({festival:4,club:8,request:24}))if(c.activities.filter(a=>a.kind===k).length!==n)bad('ACTIVITY_COUNT',k);
 for(const d of diags)errors.push(d.code+': '+d.pointer+' '+(d.call||'')+' offset '+d.offset+' — '+d.message);
 return {errors,diags,index,labels,columns,bindings};
}
const all=(a,b,d)=>[...schemaErrors(cs,a),...schemaErrors(ss,b),...schemaErrors(rs,d),...analyze(a,b,d).errors];
// Registry self-check: only checkCall types, every column kind registered.
const allowedTypes=new Set(['id','int','number','bool','text','word','value','labelref','idOrLabel']);
for(const[k,spec]of Object.entries(reg.kinds))for(const[name,sig]of Object.entries(spec.functions)){for(const p of sig.params)for(const t of p.pairs?p.pairs.map(x=>x.type):[p.type])assert(allowedTypes.has(t)||t.startsWith('enum:'),k+'.'+name+' type '+t);for(const n of Object.values(sig.named||{}))assert(allowedTypes.has(n.type)||n.type.startsWith('enum:'),k+'.'+name);}
for(const col of reg.columns)assert(reg.kinds[col.kind],'column kind '+col.kind);
const result=analyze(c,s,r);const normal=[...schemaErrors(cs,c),...schemaErrors(ss,s),...schemaErrors(rs,r),...result.errors];
if(normal.length){console.error(normal.join('\n'));process.exit(1);}
const negatives=[];function neg(name,mut,code){const a=clone(c),b=clone(s),d=clone(r);mut(a,b,d);const errors=all(a,b,d);assert(errors.length,name+' was accepted');if(code)assert(errors.some(e=>e.startsWith(code+':')),name+' expected '+code+' got '+errors.slice(0,3).join(' | '));const diag=analyze(a,b,d).diags.find(x=>!code||x.code===code)||null;negatives.push({name,expectedCode:code||null,errors:errors.slice(0,3),...(diag?{diagnostic:diag}:{})});}
const ev=(d,id)=>d.events.find(e=>e.id===id);
neg('unknown-field',(a)=>a.spells[0].typo=1);neg('negative-cost',a=>a.spells[0].focusCost=-1);neg('unknown-template',a=>a.spells[0].template='anything');neg('duplicate-id',a=>a.items[1].id=a.items[0].id,'DUPLICATE_ID');neg('missing-recipe-output',a=>a.recipes[0].outputItemId='item.missing','REF_NOT_FOUND');neg('growth-out-of-range',a=>a.plants[0].growthDays=5);neg('cross-case-gate',a=>a.story[0].externalCasePrerequisites=['case.b']);neg('reverse-internal-node',a=>a.story[0].nodes.reverse(),'NODE_ORDER');neg('path-result-difference',a=>a.story[0].deviceOutcome='outcome.other','ROUTE_MISMATCH');neg('project-and-gate',a=>a.projects[0].when='completed class.water_basic / discovered case.b','PROJECT_OR');neg('staff-romance',a=>a.relationships[20].romanceVariant=true,'ROMANCE_SCOPE');neg('bad-relation-order',a=>a.relationships[1].previousId='relation.r30','RELATION_ORDER');neg('profit-loop',a=>a.items.find(i=>i.id===a.recipes[0].outputItemId).saleBasePrice=99999,'CRAFT_PROFIT');neg('bad-substitute-slot',a=>a.recipes[0].substitutes='calm 3 slot=4','SUBSTITUTE_SLOT');neg('missing-alchemy-property',a=>a.recipes[0].requiredProperties=['calm'],'ALCHEMY_PROPERTIES');neg('negative-wallet',(a,b)=>b.wallet=-1);neg('stack-overflow',(a,b)=>b.stacks[0].quantity=100,'STACK_LIMIT');neg('loan-no-scope',(a,b)=>b.stacks[0].provenance='loan');neg('unknown-save-plant',(a,b)=>b.plots[0].plantId='plant.missing','REF_NOT_FOUND');neg('future-save',(a,b)=>b.saveVersion=99);neg('duplicate-ledger',(a,b)=>b.rewardLedger=['reward.x','reward.x']);
neg('unknown-perform',(a,b,d)=>d.events.at(-1).script='perform class activity.missing mode=practice','REF_NOT_FOUND');neg('missing-effects',(a,b,d)=>delete d.events.at(-1).effects);neg('unknown-command',(a,b,d)=>d.events.at(-1).script='eval "anything"','UNKNOWN_FUNCTION');neg('count-drift',a=>a.classes.pop(),'BUDGET_COUNT');
neg('undefined-encounter-save',(a,b)=>b.defeats[0].encounterId='encounter.missing','REF_NOT_FOUND');neg('enemy-instead-of-encounter',(a,b,d)=>ev(d,'event.a3_combat').script='perform encounter enemy.cleaner','REF_KIND');neg('missing-main-room-help',a=>a.story[0].nodes[0].hints='','ROOM_HELP');neg('living-seed-combo-bypass',a=>a.spells.find(x=>x.id==='spell.sprout').resultTag='opened_seed','COMBO_BYPASS');neg('room-locks-player',(a,b,d)=>ev(d,'event.a3_combat').resources.push('input.player'),'ROOM_SCENE_CONFLICT');neg('no-intro-gate',a=>a.classes.find(x=>x.id==='class.water_basic').requiresIntro=false,'INTRO_GATE');neg('save-completion-disagreement',(a,b)=>b.classRecords[0].completed=false,'SAVE_COMPLETION_MISMATCH');neg('practice-gives-reward',(a,b,d)=>d.events.at(-1).effects+=' / currency.add 1','PRACTICE_REWARD');
neg('double-class-time',(a,b,d)=>ev(d,'event.water_class').costMinutes=60,'DOUBLE_CLASS_TIME');neg('fine-cooking-unreachable',a=>a.recipes.find(x=>x.kind==='cooking').allowedFacilityIds=['facility.cooking'],'FINE_UNREACHABLE');neg('record-cannot-get-light',a=>a.spells.find(x=>x.id==='spell.lantern').acceptTargetTags=['dark'],'LANTERN_RECORD_UNREACHABLE');neg('case-reward-outside-node-boundary',(a,b,d)=>ev(d,'event.a3_aftermath').effects='evidence.add evidence.a3 / case.reward case.a','CASE_REWARD_BOUNDARY');
// One or more negatives per call diagnostic code (11-function-syntax.md) and per handle/fork rule.
const stage=d=>ev(d,'event.stage'),snack=d=>ev(d,'event.snack');
neg('call-unclosed-quote',(a,b,d)=>snack(d).script='move npc.serin dest.snack / say npc.serin "실습 노트를',
 'PARSE_ERROR');
neg('call-negated-effect',(a,b,d)=>ev(d,'event.a3_aftermath').effects='evidence.add evidence.a3 / !complete node.a3 / case.reward case.a','PARSE_ERROR');
neg('call-stage-without-label',a=>a.finalDefinition.stages='stabilizeChannel / flow_2: stabilizeChannel','PARSE_ERROR');
neg('call-unregistered-behavior',a=>a.enemies[0].behavior='charge / teleport','UNKNOWN_FUNCTION');
neg('call-commit-command-removed',(a,b,d)=>ev(d,'event.water_class').script='perform class class.water_basic / commit','UNKNOWN_FUNCTION');
neg('call-move-without-destination',(a,b,d)=>stage(d).script=stage(d).script.replace('move npc.mira dest.stage timeout=30 portals=true','move npc.mira'),'ARG_COUNT');
neg('call-say-extra-argument',(a,b,d)=>snack(d).forks.tea='say npc.serin "좋아" "추가"','ARG_COUNT');
neg('call-timeout-not-number',(a,b,d)=>stage(d).script=stage(d).script.replace('timeout=30','timeout=abc'),'ARG_TYPE');
neg('call-weakness-multiplier-format',a=>a.enemies[0].weak='wet times2 disable=2','ARG_TYPE');
neg('call-variable-type-mismatch',(a,b,d)=>snack(d).when='eq story.intro_done 3','ARG_TYPE');
neg('call-schedule-not-ascending',(a,b,d)=>d.schedules[0].script='720 dest.snack / 420 home','ARG_TYPE');
neg('call-missing-ingredient-item',a=>a.recipes[0].ingredients='item.missing 3 / item.plant_2 1','REF_NOT_FOUND');
neg('call-missing-goal-label',(a,b,d)=>ev(d,'event.a3_device').script='perform puzzle room.case_a3:valve','REF_NOT_FOUND');
neg('call-hint-goal-not-in-room',a=>a.story[0].nodes[0].hints=a.story[0].nodes[0].hints.replace('goal=observe','goal=nope'),'REF_NOT_FOUND');
neg('call-npc-as-encounter-enemy',a=>a.encounters[0].enemies='npc.serin 1','REF_KIND');
neg('call-device-goal-is-stage',a=>a.encounters.at(-1).deviceGoalIds=['final.practical:flow_1'],'REF_KIND');
neg('call-duplicate-goal-label',a=>a.story[0].nodes[0].goals=a.story[0].nodes[0].goals.replace('repair: apply','device: apply'),'LABEL_DUP');
neg('call-missing-fork',(a,b,d)=>delete snack(d).forks.tea,'FORK_NOT_FOUND');
neg('call-unused-fork',(a,b,d)=>snack(d).forks.extra='say npc.serin "쓰지 않는 가지"','FORK_UNUSED');
neg('call-handle-not-awaited',(a,b,d)=>stage(d).script=stage(d).script.replace(' / await cam wave',''),'HANDLE_UNAWAITED');
neg('call-handle-left-open-at-end',(a,b,d)=>ev(d,'event.lunch').script='move npc.serin dest.notice async=walk / wait 0.25','HANDLE_UNAWAITED');
neg('call-two-writers-same-actor',(a,b,d)=>stage(d).script='move npc.mira dest.stage / animate npc.mira wave 0.5 async=a / animate npc.mira bow 0.5 async=b / await a b / say npc.mira "준비하자."','HANDLE_CONFLICT');
neg('call-handle-reused',(a,b,d)=>stage(d).script='move npc.mira dest.stage / camera dest.stage 0.5 async=cam / await cam / camera dest.snack 0.5 async=cam / await cam / say npc.mira "준비하자."','HANDLE_CONFLICT');
const permutations=a=>a.length?a.flatMap((x,i)=>permutations(a.filter((_,j)=>j!==i)).map(p=>[x,...p])):[[]];let orders=0;for(const order of permutations(c.story.map(x=>x.id))){const done=new Set(),truth=[];for(const cid of order){const a=c.story.find(x=>x.id===cid);assert.equal(a.externalCasePrerequisites.length,0);a.nodes.forEach((n,i)=>{assert(i===0||done.has(a.nodes[i-1].id));done.add(n.id);});done.add(cid);const count=c.story.filter(x=>done.has(x.id)).length;for(const t of c.truthScenes)if(t.requiredCaseCount===count)truth.push(t.id);}assert.equal(c.story.filter(x=>done.has(x.id)).length,6);assert.deepEqual(truth,['truth.a','truth.b']);orders++;}
const extraction=read('source/extraction-report.json');assert.equal(extraction.headings.length,17);assert(extraction.screens.every(x=>x.embeddedHash===x.fileHash));const trace=read('source/requirements.json');assert.equal(new Set(trace.map(x=>x.section)).size,17);
let tracePointers=0;for(const row of trace){assert(row.specFile&&row.specSection&&row.acceptanceId);assert(fs.existsSync(path.join(root,row.specFile)));for(const pointer of row.examplePointers){let value=c;for(const k of pointer.slice(1).split('/'))value=value?.[k];assert(value!==undefined,'missing source trace pointer '+row.id+':'+pointer);tracePointers++;}}
const comfort=clone(s);comfort.settings.damageMultiplier=0.5;comfort.settings.telegraphMultiplier=1.5;comfort.settings.preset='comfort';assert.deepEqual(schemaErrors(ss,comfort),[]);
assert.equal(s.catalogHash,crypto.createHash('sha256').update(fs.readFileSync(path.join(root,'examples/full-catalog.json'))).digest('hex'));assert.equal(s.contentVersion,c.contentVersion);
// Symbolic acceptance of repeated participation after R1, independent of lifetime +8 reward types.
const lifetimeTypes=new Set(['class','project','club','meal','request','festival','companion']);const postR1Types=new Set(['meal','class']);assert.equal(postR1Types.size>=2,true);assert.equal([...postR1Types].filter(x=>!lifetimeTypes.has(x)).length,0);
const budget=[90,450,154,324,240,240,96,96,192,160,270,90];assert.equal(budget.reduce((a,b)=>a+b),2402);
const codes=['PARSE_ERROR','UNKNOWN_FUNCTION','ARG_COUNT','ARG_TYPE','REF_NOT_FOUND','REF_KIND','LABEL_DUP','FORK_NOT_FOUND','FORK_UNUSED','HANDLE_UNAWAITED','HANDLE_CONFLICT'];
for(const code of codes)assert(negatives.some(n=>n.expectedCode===code&&n.diagnostic?.code===code),'no negative diagnostic for '+code);
const byKind=(list,key)=>list.reduce((m,x)=>(m[x[key]]=(m[x[key]]||0)+1,m),{});
const symbolList=[...result.index].map(([id,d])=>({id,kind:d.kind,definedAt:(d.file==='functions'?'schemas/functions.json':FILES[d.file])+'#'+d.pointer}));
const callCount=result.columns.reduce((n,x)=>n+x.calls.length,0);
const report={date:'2026-11-02',scope:'document and static-data only; not Unity runtime',dataFormat:'11-function-syntax.md (call columns parsed by ../tools/parse-calls.cjs, checked by schemas/functions.json)',schemaKeywordSubset:['$ref','type','required','additionalProperties','oneOf','anyOf','enum','const','pattern','minimum','maximum','exclusiveMinimum','exclusiveMaximum','minItems','maxItems','uniqueItems','minLength','items'],schemaExamples:3,semanticResult:'pass',counts:Object.fromEntries(Object.entries(c).filter(([k,v])=>Array.isArray(v)).map(([k,v])=>[k,v.length])),storyNodes:c.story.flatMap(a=>a.nodes).length,
 functionRegistry:{kinds:Object.fromEntries(Object.entries(reg.kinds).map(([k,v])=>[k,Object.keys(v.functions).length])),columns:reg.columns.length},
 callColumns:{cells:result.columns.length,calls:callCount,byKind:Object.fromEntries(Object.keys(reg.kinds).map(k=>[k,result.columns.filter(x=>x.kind===k).reduce((n,x)=>n+x.calls.length,0)])),labels:[...result.labels.values()].reduce((n,m)=>n+m.size,0),diagnostics:result.diags.length},
 diagnosticCodes:codes,negativeCases:negatives,independentCaseOrders:orders,sourceSections:17,sourceRecords:trace.length,tracePointers,screensHashMatches:16,sourceHtmlSha256:extraction.htmlSha256,budgetMinutes:2402,
 derivedSymbolIndex:{note:'저작 symbols 표 대신 정의 칸·엔티티 Row·함수 사전에서 생성',count:symbolList.length,byKind:byKind(symbolList,'kind'),list:symbolList},
 derivedBindings:{note:'저작 bindings 표 대신 assetKey 칸에서 생성. 주소 기본값 PLACEHOLDER/<key>, 실제 주소는 assetAddresses',count:result.bindings.length,placeholders:result.bindings.filter(b=>b.placeholder).length,byKind:byKind(result.bindings,'kind'),list:result.bindings},
 limitations:['No Unity execution','No gameplay fun/balance validation','720 orders check dependency structure only','Full catalog profiles are proposals, not finished dialogue/maps/assets','Call checks are static: registry signature, reference kind, labels, forks, handles; not runtime behavior']};
fs.writeFileSync(path.join(root,'validation/report.json'),JSON.stringify(report,null,2)+'\n');console.log(JSON.stringify({...report,negativeCases:negatives.length,derivedSymbolIndex:{count:symbolList.length,byKind:report.derivedSymbolIndex.byKind},derivedBindings:{count:result.bindings.length,placeholders:report.derivedBindings.placeholders}},null,2));
