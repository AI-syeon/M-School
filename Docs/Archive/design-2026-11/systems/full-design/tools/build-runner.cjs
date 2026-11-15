// Builds the full-design runner bundle from the canonical function-call demo (../examples/campus-demo.json).
// Format: 11-function-syntax.md — one event = one row; script/forks/effects/when/trigger are call strings.
const fs=require('node:fs'),path=require('node:path');const root=path.resolve(__dirname,'..');
const base=JSON.parse(fs.readFileSync(path.resolve(root,'../examples/campus-demo.json')));base.schemaVersion=2;base.contentVersion='full-design-0.2-proposal';
const c=JSON.parse(fs.readFileSync(path.join(root,'examples/full-catalog.json')));
for(const region of c.regions)if(!base.maps.some(m=>m.id===region.mapId))base.maps.push({id:region.mapId,width:20,height:20,blocked:[],portals:[]});
for(const n of c.npcs){if(!base.destinations.some(d=>d.id===n.homeDestinationId))base.destinations.push({id:n.homeDestinationId,position:{mapId:'map.courtyard',x:1.5,y:4.5},radius:0.2,facing:'south'});if(!base.npcs.some(x=>x.id===n.id))base.npcs.push({id:n.id,source:n.sourceId,name:n.name,position:{mapId:'map.courtyard',x:1.5,y:4.5},home:n.homeDestinationId,speed:3});}
// Demo rows run as foreground scenes; the field is explicit because engine defaults are not allowed.
const order=['id','category','trigger','staging','pause','executionMode','repeatPolicy','completionKey','resources','when','costMinutes','script','forks','effects'];
const tidy=e=>Object.fromEntries(order.filter(k=>k in e).map(k=>[k,e[k]]).concat(Object.entries(e).filter(([k])=>!order.includes(k))));
base.events=base.events.map(e=>tidy({executionMode:'scene',...e}));
// A domain scene: perform waits for the domain task; effects commit once after success.
function scene(name,domain,target,effects,o={}){return tidy({id:'event.'+name,category:domain==='relationship'?'relationship':'interaction',trigger:'manual',staging:'reserved',pause:'scene',executionMode:'scene',repeatPolicy:'once',resources:['input.player','camera.main','space.map.courtyard'],when:'',costMinutes:10,script:`perform ${domain} ${target}`,effects:effects.join(' / '),...o});}
// Real-time room activity: no global scene lease, no player/camera, room overlay checkpoint.
const room={staging:'current',pause:'none',executionMode:'room-activity',resources:['space.room.case_a3'],completionKey:'outcome.a_stopped',costMinutes:0,when:'completed node.a2 / !completed outcome.a_stopped'};
base.events.push(
 // Class time is the class definition's durationMinutes, so the scene adds 0 (DOUBLE_CLASS_TIME).
 scene('water_class','class','class.water_basic',['spell.learn spell.water','complete class.water_basic','experience.record npc.serin class.water_basic class'],{when:'eq story.intro_done true',costMinutes:0}),
 scene('pump_project','project','project.p01',['project.install project.p01','complete project.p01'],{when:c.projects.find(p=>p.id==='project.p01').when}),
 // Two routes share completionKey outcome.a_stopped; the shared node reward is committed once by the aftermath.
 scene('a3_combat','encounter','encounter.a3',['complete outcome.a_stopped'],room),
 scene('a3_device','puzzle','room.case_a3:device',['complete outcome.a_stopped'],room),
 scene('serin_r01','relationship','relation.r01',['complete relation.r01']),
 scene('coolant_craft','craft','recipe.alchemy_3',['complete recipe.alchemy_3']),
 scene('first_festival','activity','activity.h01',['complete activity.h01']),
 // case.reward reads the case definition's rewardCrowns; no literal amount in content.
 scene('a3_aftermath','puzzle','room.case_a3:repair',['evidence.add evidence.a3','complete node.a3','case.reward case.a'],{category:'resolution',staging:'current',completionKey:'node.a3',resources:['input.player','camera.main','space.map.region_2'],when:'completed outcome.a_stopped'}),
 // Practice: no unique completion, no reward; only participation is recorded.
 scene('water_practice','class','class.water_basic mode=practice',['experience.record npc.serin class.water_basic class participationOnly=true'],{repeatPolicy:'practice',when:'completed class.water_basic',costMinutes:0})
);
fs.writeFileSync(path.join(root,'examples/runner-bundle.json'),JSON.stringify(base,null,2)+'\n');
const outcomes={kind:'expected-contract-examples-not-runtime-trace',cases:[{id:'A3',paths:[{route:'combat',event:'event.a3_combat',perform:'encounter encounter.a3'},{route:'device',event:'event.a3_device',perform:'puzzle room.case_a3:device'}],routeCompletionKey:'outcome.a_stopped',aftermath:{event:'event.a3_aftermath',perform:'puzzle room.case_a3:repair'},shared:{completionKey:'node.a3',evidenceId:'evidence.a3',walletReward:150,confirmed:true},replay:'ALREADY_COMMITTED'},{id:'overflow',before:{slotsUsed:24},commit:{rewardKey:'reward.q01',normalItemQuantity:1,evidenceCount:1},after:{mailAttachments:1,newEvidence:1,duplicateRewards:0}},{id:'midnight',before:{absoluteMinute:1439,encounter:'active',newEncounterAllowed:true},afterBoundary:{absoluteMinute:1440,encounter:'active',newEncounterAllowed:false,returnPending:true},afterSafeReturn:{absoluteMinute:1860,roomOverlay:'discarded',confirmedEvidence:'preserved'}},{id:'plant',before:{growthTicks:0,wateredDay:-1,lastProcessedDay:-1},dayBoundary:{absoluteDay:1},after:{growthTicks:0,alive:true},duplicateBoundary:'NO_OP'},{id:'economy',buyInputs:35,saleOutput:6,profit:-29,loanOutputDisposition:'scope-only'},{id:'save',interruptedDuring:'choice',restore:'scene-entry',completedRewardRestore:'scene-complete',duplicateGrant:0}]};
fs.writeFileSync(path.join(root,'examples/expected-outcomes.json'),JSON.stringify(outcomes,null,2)+'\n');
console.log(JSON.stringify({events:base.events.length,maps:base.maps.length,destinations:base.destinations.length,npcs:base.npcs.length}));
