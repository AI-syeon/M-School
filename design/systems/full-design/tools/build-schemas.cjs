// Closed design-data schema authoring, with explicit semantic constraints below.
const fs=require('node:fs'),path=require('node:path');const root=path.resolve(__dirname,'..');
const catalog=JSON.parse(fs.readFileSync(path.join(root,'examples/full-catalog.json'))),save=JSON.parse(fs.readFileSync(path.join(root,'examples/safe-save.json')));
const str={type:'string'},bool={type:'boolean'},num={type:'number'},int={type:'integer'},ID={type:'string',pattern:'^[a-z][a-z0-9_]*(\\.[a-z][a-z0-9_]*)+$'};
const enumOf=(...v)=>({enum:v});const arr=s=>({type:'array',items:s});
function merge(a,b){if(JSON.stringify(a)===JSON.stringify(b))return a;if(a.type==='object'&&b.type==='object'){const p={...a.properties};for(const[k,v]of Object.entries(b.properties))p[k]=p[k]?merge(p[k],v):v;return{type:'object',additionalProperties:false,properties:p,required:[...new Set([...a.required,...b.required])]};}if(a.type==='array'&&b.type==='array')return{type:'array',items:merge(a.items,b.items)};const alts=[...(a.anyOf||[a]),...(b.anyOf||[b])];return{anyOf:alts.filter((v,i)=>alts.findIndex(x=>JSON.stringify(v)===JSON.stringify(x))===i)};}
function shape(v,k=''){if(v===null)return{type:'null'};if(Array.isArray(v))return{type:'array',items:v.length?v.map(x=>shape(x)).reduce(merge):str};if(typeof v==='object')return{type:'object',additionalProperties:false,required:Object.keys(v),properties:Object.fromEntries(Object.entries(v).map(([k,x])=>[k,shape(x,k)]))};if(typeof v==='string')return(k!=='sourceId'&&(k==='id'||/Id$/.test(k)))?ID:str;return typeof v==='number'?(Number.isInteger(v)?int:num):bool;}
const cs=shape(catalog);Object.assign(cs,{$schema:'https://json-schema.org/draft/2020-12/schema',$id:'urn:magic-campus:full-catalog:2',title:'Full domain design fixture v2'});cs.properties.schemaVersion={const:2};cs.properties.status={const:'design-fixture'};
const item=k=>cs.properties[k].items;
const range=(obj,key,lo,hi,integer=false)=>obj.properties[key]={type:integer?'integer':'number',minimum:lo,...(hi===null?{}:{maximum:hi})};
for(const k of Object.keys(catalog.rules))if(typeof catalog.rules[k]==='number')range(cs.properties.rules,k,0,null,Number.isInteger(catalog.rules[k]));
item('spells').properties.lifeTemplate={const:'target'};item('spells').properties.combatTemplate=enumOf('target','area','projectile');range(item('spells'),'focusCost',10,30,true);range(item('spells'),'rangeTiles',0,20);range(item('spells'),'radiusTiles',0,10);range(item('spells'),'cooldownSeconds',0,60);range(item('spells'),'damage',0,999,true);
item('classes').properties.stage=enumOf('basic','application');item('classes').properties.tier=enumOf('intro','beginner','intermediate','advanced');item('classes').properties.prerequisiteClassIds={...arr(ID),uniqueItems:true};range(item('classes'),'durationMinutes',0,1440,true);
range(item('enemies'),'telegraphSeconds',0.1,10);range(item('enemies'),'hp',1,9999,true);item('enemies').properties.baseId={anyOf:[ID,{type:'null'}]};
range(item('relationships'),'index',1,3,true);item('relationships').properties.previousId={anyOf:[ID,{type:'null'}]};
item('story').properties.externalCasePrerequisites={...arr(ID),maxItems:0};
range(item('plants'),'growthDays',2,4,true);item('plants').properties.allowedParts={type:'array',minItems:1,uniqueItems:true,items:enumOf('leaf','flower','seed')};
item('items').properties.kind=enumOf('material','seed','tool','furniture','consumable');range(item('items'),'stackMax',1,999,true);range(item('items'),'buyPrice',0,999999,true);range(item('items'),'saleBasePrice',0,999999,true);
item('recipes').properties.kind=enumOf('alchemy','cooking','tool','furniture');range(item('recipes'),'outputQuantity',1,999,true);range(item('recipes'),'batchMax',1,99,true);
item('recipes').properties.process=enumOf('heat','cool','resonate');range(item('recipes'),'timeCostMinutes',0,1440,true);
item('activities').properties.kind=enumOf('festival','club','request');range(item('activities'),'requiredCaseCount',0,6,true);range(item('activities'),'regularCycleDay',0,79,true);
// Call columns are one-line strings. Names, arguments and references are checked by validate.cjs against schemas/functions.json.
const callStr={type:'string',pattern:'^[^\\r\\n]*$'},callReq={...callStr,minLength:1};const node=item('story').properties.nodes.items;node.properties.goals=callReq;node.properties.hints=callStr;item('rooms').properties.hints=callStr;cs.properties.finalDefinition.properties.goals=callReq;cs.properties.finalDefinition.properties.stages=callReq;item('enemies').properties.behavior=callReq;item('enemies').properties.weak=callStr;item('reactions').properties.paths=callReq;item('encounters').properties.enemies=callReq;item('encounters').properties.deviceGoalIds=arr({type:'string',pattern:'^[a-z][a-z0-9_]*(\\.[a-z0-9_]+)+:[a-z][a-z0-9_]*$'});item('recipes').properties.ingredients=callReq;item('recipes').properties.substitutes=callStr;item('shops').properties.stock=callStr;item('classes').properties.grantOnFirstAccept=callStr;item('projects').properties.grantOnAccept=callStr;item('projects').properties.when=callStr;item('dialogues').properties.when=callStr;cs.properties.assetAddresses={type:'object',additionalProperties:{type:'string',minLength:1}};
range(item('dialogues'),'tier',1,4,true);item('onboarding').properties.grantedSpellId={anyOf:[ID,{type:'null'}]};
const ss=shape(save);Object.assign(ss,{$schema:cs.$schema,$id:'urn:magic-campus:full-save:2',title:'Safe snapshot v2 design fixture'});ss.properties.saveVersion={const:2};ss.properties.safePoint=enumOf('world-safe','scene-entry','scene-complete','room-safe');range(ss,'wallet',0,2147483647,true);range(ss.properties.clock,'absoluteMinute',0,null,true);ss.properties.clock.properties.fraction={type:'number',minimum:0,exclusiveMaximum:1};range(ss.properties.player,'hp',0,100,true);range(ss.properties.player,'focus',0,100,true);
ss.properties.flags.items.properties.value={oneOf:[bool,int]};
ss.properties.catalogHash={type:'string',pattern:'^[a-f0-9]{64}$'};
ss.properties.player.properties.quickSlots={type:'array',minItems:4,maxItems:4,items:{anyOf:[ID,{type:'null'}]}};
ss.properties.caseResolutionOrder={...arr(ID),uniqueItems:true,maxItems:6};ss.properties.truthReadKeys={...arr(ID),uniqueItems:true};ss.properties.trackedActivityIds={...arr(ID),uniqueItems:true,maxItems:3};
ss.properties.finalState.properties.status=enumOf('locked','ready','running','done');ss.properties.finalState.properties.choice={anyOf:[enumOf('supervised_core','distributed','periodic_rest'),{type:'null'}]};ss.properties.finalState.properties.archiveGeneration={anyOf:[{type:'integer',minimum:1},{type:'null'}]};
ss.properties.companion.properties.npcId={anyOf:[ID,{type:'null'}]};ss.properties.companion.properties.usedDays=arr({type:'object',additionalProperties:false,required:['npcId','day'],properties:{npcId:ID,day:{type:'integer',minimum:0}}});ss.properties.relations.items.properties.r1BaselineActivityKeys={...arr(ID),uniqueItems:true};range(ss.properties.relations.items,'stage',0,3,true);
ss.properties.discoveredCaseIds={...arr(ID),uniqueItems:true};ss.properties.discoveredSafePointIds={...arr(ID),uniqueItems:true};
ss.properties.questStates.items.properties.status=enumOf('discovered','investigating','resolved','epilogue');range(ss.properties.journal.items,'hintLevel',0,3,true);range(ss.properties.creatureState.items,'comfort',0,1);range(ss.properties.defeats.items,'count',0,null,true);
ss.properties.questStates.items.properties.activeNodeId={anyOf:[ID,{type:'null'}]};ss.properties.plots.items.properties.plantId={anyOf:[ID,{type:'null'}]};ss.properties.plots.items.properties.harvestPart=enumOf('leaf','flower','seed');
const stack={type:'object',additionalProperties:false,required:['itemId','quantity','provenance','quality','scopeId'],properties:{itemId:ID,quantity:{type:'integer',minimum:1,maximum:999},provenance:enumOf('normal','grant','loan'),quality:enumOf('standard','fine'),scopeId:{anyOf:[ID,{type:'null'}]}}};
stack.properties.provenance=enumOf('normal','grant');stack.properties.scopeId={type:'null'};
ss.properties.stacks=arr(stack);ss.properties.mail=arr({type:'object',additionalProperties:false,required:['id','attachments'],properties:{id:ID,attachments:arr(stack)}});
for(const k of ['completed','evidence','learnedSpellIds','installedProjects','rewardLedger','dayJobs'])ss.properties[k]={...arr(ID),uniqueItems:true};
ss.properties.spellContexts=arr({type:'object',additionalProperties:false,required:['spellId','contextIds'],properties:{spellId:ID,contextIds:{...arr(ID),uniqueItems:true}}});
ss.properties.placements=arr({type:'object',additionalProperties:false,required:['id','furnitureId','mapId','x','y','rotation'],properties:{id:ID,furnitureId:ID,mapId:ID,x:{type:'integer',minimum:0},y:{type:'integer',minimum:0},rotation:{type:'integer',minimum:0,maximum:3}}});
ss.properties.placements.items.required.push('provenance');ss.properties.placements.items.properties.provenance=enumOf('normal','grant');ss.properties.plots.items.properties.provenance=enumOf('normal','grant');ss.properties.plots.items.properties.scopeId={type:'null'};ss.properties.creatureState.items.properties.lastFoodProvenance=enumOf('normal','grant');
ss.properties.appointments=arr({type:'object',additionalProperties:false,required:['id','status','acceptedAt'],properties:{id:ID,status:enumOf('Offered','Accepted','Ready'),acceptedAt:{type:'integer',minimum:0}}});
ss.properties.roomCheckpoints=arr({type:'object',additionalProperties:false,required:['roomId','completedObjectIds','confirmedRewardKeys'],properties:{roomId:ID,completedObjectIds:{...arr(ID),uniqueItems:true},confirmedRewardKeys:{...arr(ID),uniqueItems:true}}});
range(ss.properties.relations.items,'affinity',0,100,true);ss.properties.relations.items.properties.activityKeys={...arr(ID),uniqueItems:true};ss.properties.relations.items.properties.conflict=enumOf('none','pending-reconcile');ss.properties.relations.items.properties.expression=enumOf('friendship','romance');
for(const k of ['damageMultiplier','telegraphMultiplier','aimAssistStrength'])ss.properties.settings.properties[k]={type:'number',minimum:0,maximum:k==='aimAssistStrength'?1:3};ss.properties.settings.properties.preset=enumOf('basic','comfort','challenge','custom');
for(const k of ['discoveredRecipeIds','acceptedActivityIds','invitedRelationIds','preAcceptedEvidenceIds'])ss.properties[k]={...arr(ID),uniqueItems:true};
ss.properties.spellApplicationSelections=arr({type:'object',additionalProperties:false,required:['spellId','variantKey'],properties:{spellId:ID,variantKey:enumOf('a','b')}});
const activityType=enumOf('class','project','club','meal','request','festival','companion');
for(const k of ['rewardedActivityTypes','r2ActivityTypesSinceR1'])ss.properties.relations.items.properties[k]={...arr(activityType),uniqueItems:true};
ss.properties.relations.items.properties.activityRecords=arr({type:'object',additionalProperties:false,required:['activityId','activityType','occurrenceSequence'],properties:{activityId:ID,activityType,occurrenceSequence:{type:'integer',minimum:0}}});
ss.properties.mail.items.properties.kind=enumOf('invite','reminder','overflow','info');ss.properties.mail.items.properties.createdAt={type:'integer',minimum:0};ss.properties.mail.items.properties.reservationId={anyOf:[ID,{type:'null'}]};ss.properties.mail.items.properties.state=enumOf('unread','accepted','deferred','declined');ss.properties.mail.items.required.push('kind','createdAt','reservationId','state');
// ---- Function registry (schemas/functions.json): one place for names, params, ref kinds, owners. See 11-function-syntax.md.
// Param format = parse-calls checkCall signature. Extra keys checked by validate.cjs: refKind, defines, pattern, allow, min, pairs, parse, fork, labelIn.
const P=(name,type,o={})=>({name,type,...o});
const F=(params,o={})=>({params,named:{},negatable:false,...o});
const H={async:{type:'word'}};
const domains=['class','encounter','puzzle','craft','relationship','project','activity'];
const behaviorKeys=['charge','ink_dispersal','root_line','seed_lure','overheat_armor','cooling_disable','dive','lantern_perch','shadow_immunity','illusion_lure','frontal_shield','supply_cut','three_ring_wave','matching_bells','frost_trail','warm_stone_lure'];
const registry={
 $id:'urn:magic-campus:functions:1',title:'Magic Campus function registry',grammar:'full-design/11-function-syntax.md',
 types:{id:'점이 있는 소문자 ID',int:'정수',number:'수',bool:'true/false',text:'큰따옴표 문자열',word:'단어(ID도 통과, refKind/pattern으로 추가 검사)',value:'bool/int/number',labelref:'소유ID:이름표',idOrLabel:'ID 또는 소유ID:이름표','enum:a|b':'열거 값'},
 paramKeys:{refKind:'참조해야 하는 색인 종류(또는 refKinds의 묶음)',defines:'이 인자가 새 ID를 선언함',pattern:'단어 형식',allow:'참조 대신 허용하는 예약어',min:'rest 인자 최소 개수',pairs:'rest 인자를 [타입,타입] 쌍으로 읽음',parse:'인자 문자열을 다시 이 종류의 호출목록으로 파싱',fork:'같은 Row의 forks 이름',labelIn:'같은 Row의 이 칸에 있는 이름표',refKindFrom:'다른 인자 값에 따라 참조 종류 결정'},
 refKinds:{completable:['class','project','relationship','node','outcome','recipe','activity','onboarding','room','truth','ending'],experienceSource:['class','project','activity','relationship'],puzzleTarget:['room','goal']},
 columns:[
  {file:'runner',table:'events',column:'trigger',kind:'trigger',nonEmpty:true,maxCalls:1},
  {file:'runner',table:'events',column:'when',kind:'condition'},
  {file:'runner',table:'events',column:'script',kind:'command',nonEmpty:true},
  {file:'runner',table:'events',column:'forks/*',kind:'command',nonEmpty:true},
  {file:'runner',table:'events',column:'effects',kind:'effect'},
  {file:'runner',table:'schedules',column:'script',kind:'scheduleEntry',nonEmpty:true},
  {file:'catalog',table:'story/*/nodes',column:'goals',kind:'goal',nonEmpty:true,labelOwner:'roomId'},
  {file:'catalog',table:'story/*/nodes',column:'hints',kind:'hint',labelOwner:'roomId'},
  {file:'catalog',table:'rooms',column:'hints',kind:'hint'},
  {file:'catalog',table:'finalDefinition',column:'goals',kind:'goal',nonEmpty:true,labelOwner:'roomId'},
  {file:'catalog',table:'finalDefinition',column:'stages',kind:'finalStage',nonEmpty:true,labelOwner:'id'},
  {file:'catalog',table:'enemies',column:'behavior',kind:'behavior',nonEmpty:true},
  {file:'catalog',table:'enemies',column:'weak',kind:'weakness'},
  {file:'catalog',table:'reactions',column:'paths',kind:'reactionPath',nonEmpty:true},
  {file:'catalog',table:'encounters',column:'enemies',kind:'encounterEnemy',nonEmpty:true},
  {file:'catalog',table:'recipes',column:'ingredients',kind:'itemQuantity',nonEmpty:true},
  {file:'catalog',table:'recipes',column:'substitutes',kind:'substitute'},
  {file:'catalog',table:'shops',column:'stock',kind:'itemQuantity'},
  {file:'catalog',table:'classes',column:'grantOnFirstAccept',kind:'itemGrant'},
  {file:'catalog',table:'projects',column:'grantOnAccept',kind:'itemGrant'},
  {file:'catalog',table:'projects',column:'when',kind:'condition'},
  {file:'catalog',table:'dialogues',column:'when',kind:'condition'}
 ],
 kinds:{
  command:{description:'이벤트 script·forks. 순서대로 실행',nameRule:'registered',functions:{
   move:F([P('actor','id',{refKind:'npc'}),P('destination','id',{refKind:'destination'})],{named:{timeout:{type:'number',default:30,min:0.001},portals:{type:'bool',default:true},...H},owner:'World/Movement',writes:'actor',description:'도착까지 대기. async=핸들이면 바로 다음으로'}),
   await:F([P('handles','word',{rest:true,min:1})],{owner:'Scene/Control',description:'핸들이 모두 끝날 때까지 대기. 하나라도 실패하면 나머지 취소 후 중단'}),
   say:F([P('speaker','id',{refKind:'npc'}),P('text','text')],{owner:'Scene/Control',requiresClosedHandles:true,writes:'dialogue'}),
   choice:F([P('prompt','text'),P('options','word',{rest:true,min:1,pairs:[{name:'fork',type:'word',fork:true},{name:'label',type:'text'}]})],{owner:'Scene/Control',requiresClosedHandles:true,writes:'dialogue',description:'고른 가지를 실행한 뒤 다음 명령으로'}),
   branch:F([P('condition','text',{parse:'condition'}),P('then','word',{fork:true}),P('else','word',{optional:true,fork:true})],{owner:'Scene/Control',requiresClosedHandles:true}),
   wait:F([P('seconds','number',{min:0})],{owner:'Clock/Schedule'}),
   camera:F([P('destination','id',{refKind:'destination'}),P('seconds','number',{min:0})],{named:{...H},owner:'Scene/Control',writes:'camera'}),
   animate:F([P('actor','id',{refKind:'npc'}),P('clip','word'),P('seconds','number',{min:0})],{named:{...H},owner:'Scene/Control',writes:'actor'}),
   perform:F([P('domain','enum:'+domains.join('|')),P('definition','idOrLabel',{refKindFrom:{param:'domain',map:{class:'class',encounter:'encounter',puzzle:'puzzleTarget',craft:'recipe',relationship:'relationship',project:'project',activity:'activity'}}})],{named:{mode:{type:'enum:normal|practice',default:'normal'}},owner:'도메인 서비스',requiresClosedHandles:true,description:'도메인 작업 성공까지 대기. 결과는 effects 커밋이 읽는다'})
  }},
  condition:{description:'when·branch·any. /는 AND, !는 부정, any는 OR',nameRule:'registered',functions:{
   eq:F([P('variable','id',{refKind:'variable'}),P('value','value')],{negatable:true,owner:'Catalog'}),
   gte:F([P('variable','id',{refKind:'variable'}),P('value','int')],{negatable:true,owner:'Catalog'}),
   completed:F([P('id','id',{refKind:'completable'})],{negatable:true,owner:'Relationship/Story'}),
   discovered:F([P('case','id',{refKind:'case'})],{negatable:true,owner:'Relationship/Story'}),
   evidence:F([P('evidence','id',{refKind:'evidence'})],{negatable:true,owner:'Relationship/Story'}),
   learned:F([P('spell','id',{refKind:'spell'})],{negatable:true,owner:'Ability/Combat'}),
   caseCountGte:F([P('count','int',{min:0,max:6})],{negatable:true,owner:'Relationship/Story'}),
   any:F([P('alternatives','text',{rest:true,min:2,parse:'condition'})],{negatable:true,owner:'Catalog'})
  }},
  effect:{description:'effects. script 성공 뒤 한 번 원자 커밋. 도메인 전제조건을 재검증',nameRule:'registered',functions:{
   set:F([P('variable','id',{refKind:'variable'}),P('value','value')],{owner:'Catalog'}),
   complete:F([P('id','id',{refKind:'completable'})],{owner:'Relationship/Story'}),
   'evidence.add':F([P('evidence','id',{refKind:'evidence'})],{owner:'Relationship/Story'}),
   'item.grant':F([P('item','id',{refKind:'item'}),P('quantity','int',{min:1}),P('provenance','enum:normal|grant')],{named:{quality:{type:'enum:standard|fine',default:'standard'}},owner:'Inventory/Economy'}),
   'experience.record':F([P('npc','id',{refKind:'npc'}),P('activity','id',{refKind:'experienceSource'}),P('activityType','enum:class|project|club|meal|request|festival|companion')],{named:{participationOnly:{type:'bool',default:false}},owner:'Relationship/Story'}),
   'project.install':F([P('project','id',{refKind:'project'})],{owner:'Life'}),
   'spell.learn':F([P('spell','id',{refKind:'spell'})],{owner:'Ability/Combat'}),
   'currency.add':F([P('amountCrowns','int',{min:0,max:999999})],{owner:'Inventory/Economy'}),
   'case.reward':F([P('case','id',{refKind:'case'})],{owner:'CaseService',description:'사건 정의의 rewardCrowns를 지급. 금액 리터럴 없음'})
  }},
  trigger:{description:'events.trigger. 이전 interactions 표를 대체',nameRule:'registered',functions:{
   interact:F([P('target','id',{refKind:'npc'}),P('label','text'),P('range','number',{min:0})],{owner:'Scene/Control'}),
   clock:F([],{owner:'Clock/Schedule'}),manual:F([],{owner:'Scene/Control'})
  }},
  scheduleEntry:{description:'schedules.script. 이름=시작분, 끝은 다음 항목의 시작(마지막은 1440). home은 NPC 기본 위치',nameRule:{pattern:'^\\d+$',min:0,max:1439,ascending:true},functions:{'*':F([P('destination','word',{refKind:'destination',allow:['home']})],{owner:'Clock/Schedule'})}},
  goal:{description:'방 목표. 이름표 필수, 참조는 <roomId>:<이름표>. 대여 도구는 같은 Row의 loanItemId',nameRule:'registered',labelRequired:true,functions:{
   observe:F([P('object','id',{defines:'logicalObject'}),P('state','word')],{owner:'Ability/Combat',capability:'cap.observe',template:'observe-tag'}),
   setState:F([P('object','id',{defines:'logicalObject'}),P('state','word')],{named:{count:{type:'int',default:1,min:1}},owner:'Ability/Combat',capability:'cap.manual_control',template:'set-object-state'}),
   apply:F([P('object','id',{defines:'logicalObject'}),P('state','word')],{owner:'Ability/Combat',capability:'cap.repair',template:'apply-capability'})
  }},
  hint:{description:'방·조사실 힌트. 단계=순서(1부터)',nameRule:'registered',functions:Object.fromEntries(['observation','property','action'].map(k=>[k,F([P('text','text')],{named:{goal:{type:'word',labelIn:'goals'}},owner:'Journal/Presentation'})]))},
  behavior:{description:'적 행동 규칙. C# CombatService에 같은 이름의 구현이 있어야 한다(이전 behaviorRegistry)',nameRule:'registered',functions:Object.fromEntries(behaviorKeys.map(k=>[k,F([],{owner:'Ability/Combat',stateOwner:'CombatService',implementationRequired:true})]))},
  weakness:{description:'적 약점. 이름=상태 태그, xN=피해 배율, disable=공격 불가 초',nameRule:{type:'word'},functions:{'*':F([P('multiplier','word',{optional:true,default:'x1',pattern:'^x\\d+(\\.\\d+)?$'})],{named:{disable:{type:'number',default:0,min:0}},owner:'Ability/Combat'})}},
  reactionPath:{description:'반응 경로. 이름=발동 주문, 인자=필요 태그(모두)',nameRule:{type:'id',refKind:'spell'},functions:{'*':F([P('tags','word',{rest:true,min:1})],{owner:'Ability/Combat'})}},
  encounterEnemy:{description:'교전 적 구성. 이름=적, 인자=수',nameRule:{type:'id',refKind:'enemy'},functions:{'*':F([P('count','int',{optional:true,default:1,min:1})],{owner:'Ability/Combat'})}},
  itemQuantity:{description:'재료·재고. 이름=아이템, 인자=수량(재고 -1=무한, 0=품절)',nameRule:{type:'id',refKind:'item'},functions:{'*':F([P('quantity','int',{min:-1})],{owner:'Inventory/Economy'})}},
  itemGrant:{description:'지급 목록. 이름=아이템, 수량, 출처',nameRule:{type:'id',refKind:'item'},functions:{'*':F([P('quantity','int',{min:1}),P('provenance','enum:normal|grant',{optional:true,default:'grant'})],{owner:'Inventory/Economy'})}},
  substitute:{description:'대체재. 이름=요구 성질, 인자=수량, slot=바꿀 재료 순번(0부터)',nameRule:{type:'word',enum:['calm','glow','adhesive','cool']},functions:{'*':F([P('quantity','int',{min:1})],{named:{slot:{type:'int',default:0,min:0}},owner:'Inventory/Economy'})}},
  finalStage:{description:'최종 실습 단계. 이름표 필수',nameRule:'registered',labelRequired:true,functions:{stabilizeChannel:F([],{owner:'CaseService',template:'stabilize-channel'})}}
 }
};
// ---- Runner schema: standalone (no longer derived from ../schemas/content.schema.json).
const calls={type:'string',pattern:'^[^\\r\\n]*$',description:'함수 호출목록 문자열. 의미 검사는 validate.cjs + functions.json'};
const callsNonEmpty={...calls,minLength:1,pattern:'^[^\\r\\n]+$'};
const position={type:'object',additionalProperties:false,required:['mapId','x','y'],properties:{mapId:ID,x:{type:'number',minimum:0},y:{type:'number',minimum:0}}};
const obj=(props,required=Object.keys(props))=>({type:'object',additionalProperties:false,required,properties:props});
const runner=obj({
 schemaVersion:{const:2},contentVersion:{type:'string',minLength:1},
 variables:arr(obj({id:ID,type:enumOf('bool','int'),initial:{oneOf:[bool,int]}})),
 maps:{type:'array',minItems:1,items:obj({id:ID,width:{type:'integer',minimum:1},height:{type:'integer',minimum:1},blocked:{type:'array',uniqueItems:true,items:{type:'array',minItems:2,maxItems:2,items:{type:'integer',minimum:0}}},portals:arr(obj({id:ID,x:{type:'number',minimum:0},y:{type:'number',minimum:0},to:position,open:bool}))})},
 destinations:arr(obj({id:ID,position,radius:{type:'number',exclusiveMinimum:0,maximum:0.5},facing:enumOf('north','south','east','west')})),
 npcs:arr(obj({id:ID,source:str,name:{type:'string',minLength:1},position,home:ID,speed:{type:'number',exclusiveMinimum:0}})),
 schedules:arr(obj({id:ID,npcId:ID,script:callsNonEmpty})),
 events:arr(obj({id:ID,category:enumOf('resolution','relationship','festival','interaction','general','schedule'),trigger:{...callsNonEmpty,pattern:'^(clock|manual|interact .+)$'},staging:enumOf('current','reserved'),pause:enumOf('dialogue','scene','none'),executionMode:enumOf('scene','room-activity'),repeatPolicy:enumOf('once','practice'),completionKey:ID,resources:{type:'array',uniqueItems:true,items:ID},when:calls,costMinutes:{type:'integer',minimum:0},script:callsNonEmpty,forks:{type:'object',additionalProperties:callsNonEmpty},effects:calls},['id','category','trigger','staging','pause','executionMode','repeatPolicy','resources','when','costMinutes','script','effects']))
});
Object.assign(runner,{$schema:cs.$schema,$id:'urn:magic-campus:runner:2',title:'Runner bundle v2 (function-call columns)',description:'한 이벤트 = Row 하나. 호출 칸은 문자열이며 의미는 functions.json과 validate.cjs가 검사한다.'});
for(const [name,value]of [['catalog.schema.json',cs],['save.schema.json',ss],['runner.schema.json',runner],['functions.json',registry]])fs.writeFileSync(path.join(root,'schemas',name),JSON.stringify(value,null,2)+'\n');
console.log('Closed catalog/save/runner schemas and function registry authored');
