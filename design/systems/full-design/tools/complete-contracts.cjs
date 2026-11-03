// Completes declarative design fixtures after editorial review. No gameplay interpreter.
// Data format: 11-function-syntax.md. A room's goals/hints are call columns on its node row; puzzles/goals/hints/mainRooms/symbols/bindings/behaviorRegistry are not authored.
const fs=require('node:fs'),path=require('node:path');const root=path.resolve(__dirname,'..'),read=p=>JSON.parse(fs.readFileSync(path.join(root,p))),write=(p,v)=>fs.writeFileSync(path.join(root,p),JSON.stringify(v,null,2)+'\n');
const c=read('examples/full-catalog.json'),s=read('examples/safe-save.json');
c.rules.supportAfterDefeats=3;c.rules.supportFocusBonus=30;c.rules.supportShieldHits=1;c.rules.supportDamageAbsorbRatio=0.5;c.rules.qualitySaleFactor=1;c.rules.batchAdditionalMinutes=2;
c.rules.lifeCastFocusCost=0;c.rules.nonresponsiveLifeCastConsumesItems=false;
c.rules.introFlagKey='story.intro_done';s.flags=[{id:'story.intro_done',value:true},{id:'memory.snack_done',value:false},{id:'world.stage_ready',value:false},{id:'notice.lunch_done',value:false}];
c.spells.forEach((x,i)=>{x.lifeTemplate='target';x.combatTemplate=['area','projectile','area','projectile','area','target','area','projectile','area','target','target','area'][i];delete x.template;x.costPolicy='combat-only-on-emission';x.effectDurationSeconds=[6,2,8,4,2,8,1,4,1,6,5,1.5][i];x.nearbyActionIds=[['activity.q24'],['activity.q01'],['activity.q04'],['activity.q06'],['activity.q10'],['activity.q21'],['activity.q18'],['activity.q17'],['activity.q08'],['activity.q13'],['activity.q20'],['activity.q23']][i];x.acceptTargetTags=[x.targetTag];});
Object.assign(c.spells[4],{targetTag:'seed',resultTag:'germination_ready'});Object.assign(c.spells[10],{targetTag:'record',resultTag:'illusion_ready'});
c.spells[5].acceptTargetTags=['broken_stone','shaped','repaired'];c.spells[9].acceptTargetTags=['broken','broken_stone','shaped','repaired'];c.spells[6].resultTag='wind_path';
c.spells[0].additionalStateTags=['lit'];c.spells[4].additionalStateTags=['germination_ready'];
c.spells.forEach(x=>x.additionalStateTags??=[]);
c.spells[0].acceptTargetTags.push('record');c.spells.forEach(x=>delete x.targetTag);
const comboPaths=[
[{triggerSpellId:'spell.frost',requiresTags:['wet']}],
[{triggerSpellId:'spell.echo',requiresTags:['wind_path']},{triggerSpellId:'spell.gust',requiresTags:['resonating']}],
[{triggerSpellId:'spell.barrier',requiresTags:['levitating']}],
[{triggerSpellId:'spell.ember',requiresTags:['dry_seed','germination_ready']}],
[{triggerSpellId:'spell.illusion',requiresTags:['lit','record']}],
[{triggerSpellId:'spell.repair',requiresTags:['shaped']},{triggerSpellId:'spell.shape',requiresTags:['repaired']}]
];
c.reactions.forEach((x,i)=>{delete x.requiresTag;delete x.order;delete x.requiredTags;delete x.directionPolicy;x.paths=(i<6?comboPaths[i]:[{triggerSpellId:'spell.water',requiresTags:['washable']}]).map(p=>p.triggerSpellId+' '+p.requiresTags.join(' ')).join(' / ');x.windowPolicy=i===1?'seconds-since-prerequisite':'until-room-reset';x.windowSeconds=i===1?6:0;});
// Explicit feedback keys are content IDs, not arbitrary generated dialogue.
c.reactions.forEach((x,i)=>{x.nonmatchObservationId='observation.reaction_'+(i+1);x.blockedTags=i===3?['living_crop','person','private_property']:['person','private_property'];});
const keys=[['charge','ink_dispersal'],['root_line','seed_lure'],['overheat_armor','cooling_disable'],['dive','lantern_perch'],['shadow_immunity','illusion_lure'],['frontal_shield','supply_cut'],['three_ring_wave','matching_bells'],['frost_trail','warm_stone_lure']];
// Behavior names are registered functions in schemas/functions.json (kind behavior), not catalog rows.
c.enemies.forEach((x,i)=>{const n=i<8?i:[0,1,2,6][i-8];x.behavior=keys[n].join(' / ');x.weak=['wet','heated','cooled','airborne','revealed','flanked','interrupted','heated'][n]+' x'+(n===4?1.5:1)+' disable=2';x.resolutionOutcomes=['defeated','pacified','disabled'];x.tokenPool='encounter-shared';x.initialDamageGate=n===4?'requires-revealed':n===5?'front-90-blocks':n===2?'overheated-half-damage':'none';x.ringCount=n===6?3:0;});
const oldRooms=c.rooms;c.encounters=[];
const enemy=['enemy.cleaner','enemy.vine','enemy.bird','enemy.orb','enemy.shadow','enemy.guard'];
const q=t=>JSON.stringify(String(t)); // only \" and \ escapes occur in fixture text
// Hint wording is a fixture, not final narrative. Level = order in the hints column.
const genericHints=['방에서 강조된 조작 대상의 상태를 관찰하세요.','대상의 성질과 입구 대여 도구의 같은 성질을 비교하세요.','수첩의 현재 목표 순서대로 도구를 적용하고 변경된 상태를 확인하세요.'];
const a3Hints=['인형 옆 배관과 두 밸브를 살펴보세요.','냉각하거나 마력 공급을 끊으면 회전이 멈춥니다.','옆 유지보수 길에서 밸브 두 개를 잠그고 공구로 명령 부품을 수리하세요.'];
const hintCalls=(texts,labels)=>['observation','property','action'].map((r,i)=>r+' '+q(texts[i])+(labels?' goal='+labels[i]:'')).join(' / ');
for(const[aIndex,a]of c.story.entries())for(const[nodeIndex,n]of a.nodes.entries()){
 const letter=a.sourceId.toLowerCase(),regionIndex=[1,0,3,2,0,1][aIndex],encounterId='encounter.'+letter+'3',obj='object.'+letter+(nodeIndex+1)+'_target_';
 n.regionId=c.regions[regionIndex].id;n.encounterId=nodeIndex===2?encounterId:null;n.resetPolicy='overlay-only-preserve-confirmed';
 n.goals=['observe: observe '+obj+'0 observed','device: setState '+obj+'1 supply_stopped'+(aIndex===0?' count=2':''),'repair: apply '+obj+'2 repaired'].join(' / ');
 n.hints=hintCalls(n.sourceId==='A3'?a3Hints:genericHints,['observe','device','repair']);
 if(nodeIndex===2)c.encounters.push({id:encounterId,source:n.sourceId,roomId:n.roomId,enemies:enemy[aIndex]+' 1',maxAttackers:3,deviceGoalIds:[n.roomId+':device'],successOutcomeId:a.combatOutcome,defeatKey:encounterId,supportAfterDefeats:3,targetMinMinutes:3,targetMaxMinutes:5,phases:1});
}
for(const room of oldRooms)room.hints=hintCalls(genericHints,null);
c.truthScenes=[{id:'truth.a',source:'§9 2개정리',requiredCaseCount:2,evidencePolicy:'owned-evidence-only',replayFromJournal:true},{id:'truth.b',source:'§9 4개정리',requiredCaseCount:4,evidencePolicy:'owned-evidence-only',replayFromJournal:true}];
c.finalDefinition={id:'final.practical',source:'§9 최종실습',requiredCaseCount:6,manualStart:true,archiveBeforeStart:true,friendRoleMinStage:2,staffFallback:true,roomId:'room.final_core',loanItemId:'item.loan_kit',goals:'stabilize: setState object.final_core stabilized',stages:'flow_1: stabilizeChannel / flow_2: stabilizeChannel / flow_3: stabilizeChannel',finalEncounterId:'encounter.final',endingIds:c.endings.map(e=>e.id),replayPolicy:'archive-or-recollection-no-reward-merge'};
c.encounters.push({id:'encounter.final',source:'§9 최종실습',roomId:'room.final_core',enemies:'enemy.guard 1',maxAttackers:3,deviceGoalIds:['room.final_core:stabilize'],successOutcomeId:'outcome.final_stopped',defeatKey:'encounter.final',supportAfterDefeats:3,targetMinMinutes:3,targetMaxMinutes:5,phases:3});
c.caseDerivation={owner:'CaseService',trigger:'node-complete-commit',resolveWhen:'node3-completed-and-all-internal-predecessors-completed',writes:['questStates.resolved','caseResolutionOrder','derived-resolution-flag','projection'],resolutionOrderPolicy:'append-on-first-commit-stable-sequence',truthInviteThresholds:[2,4],finalReadyThreshold:6,atomicWithNodeCompletion:true};
// Puzzles are not rows: perform puzzle <room> runs all goals, perform puzzle <room>:<label> runs one goal.
c.difficultyPresets=[{id:'difficulty.basic',source:'§7',damageMultiplier:1,telegraphMultiplier:1,maxAttackers:3},{id:'difficulty.comfort',source:'§7',damageMultiplier:0.5,telegraphMultiplier:1.5,maxAttackers:2},{id:'difficulty.challenge',source:'설계제안',damageMultiplier:1.25,telegraphMultiplier:0.85,maxAttackers:3}];
c.classes.forEach(x=>{x.requiresIntro=!x.introduced;x.grantOnFirstAccept='item.plant_1 3 grant';});
c.classes.forEach(x=>x.timeCostPolicy='definition-durationMinutes-success-once');
const recipeMap=[['recipe.tool_1'],['recipe.tool_3'],['recipe.furniture_1'],['recipe.cooking_1','recipe.cooking_4'],['recipe.tool_4'],['recipe.furniture_17'],['recipe.alchemy_1'],['recipe.furniture_12'],['recipe.tool_6'],['recipe.tool_2'],['recipe.tool_8'],['recipe.furniture_20']];
c.projects.forEach((x,i)=>{delete x.recipeId;x.recipeIds=recipeMap[i];x.grantOnAccept=x.recipeIds.flatMap(rid=>c.recipes.find(r=>r.id===rid).ingredients).map(z=>z.itemId+' '+z.quantity+' grant').join(' / ');x.loanTaskCompletionAllowed=true;x.personalLoanOutputAllowed=false;});
c.activities.forEach((x,i)=>{x.loanItemId='item.loan_kit';x.prerequisiteActivityIds=x.kind==='club'&&Number(x.sourceId.slice(1))%2===0?['activity.c'+String(Number(x.sourceId.slice(1))-1).padStart(2,'0')]:[];});
// Item names retain their semantic property, rather than the illustrative cyclic allocation.
c.items.find(x=>x.id==='item.alchemy_3').properties=['cool'];c.items.find(x=>x.id==='item.alchemy_4').properties=['adhesive'];
c.recipes.forEach(x=>{x.qualityRule='matched-process-point-plus-p07-facility-point';x.qualityThreshold=2;x.qualitySaleFactor=1;});
c.facilities=['alchemy','cooking','tool','furniture','shared_p07'].map(key=>({id:'facility.'+key,source:'제작시설명시',disciplines:key==='shared_p07'?['alchemy','cooking']:[key],unlockProjectId:key==='shared_p07'?'project.p07':null,qualityBonus:key==='shared_p07'?1:0,concurrentUsers:1}));
c.recipes.forEach(x=>{x.allowedFacilityIds=[x.facilityId,...(['alchemy','cooking'].includes(x.kind)?['facility.shared_p07']:[])];delete x.facilityId;});
c.schoolRule={id:'school.shared_tool_repair',source:'§4 공동도구후속',trigger:'shared-tool-damaged',followupTemplate:'repair-together',capabilityId:'cap.repair',loanItemId:'item.loan_kit',noExpulsion:true,noMainGate:true,budgetCategory:'incidental-life',repeatRewards:false};
s.settings.damageMultiplier=1;s.settings.telegraphMultiplier=1;s.settings.aimAssistStrength=0.5;s.settings.preset='basic';
s.discoveredRecipeIds=['recipe.alchemy_3'];s.spellApplicationSelections=[];s.acceptedActivityIds=['activity.q01'];s.invitedRelationIds=[];s.preAcceptedEvidenceIds=[];
s.mail=[{id:'mail.intro',kind:'info',createdAt:420,reservationId:null,state:'unread',attachments:[]}];
s.relations.forEach(x=>{x.rewardedActivityTypes=[];delete x.r1BaselineActivityTypes;x.r2ActivityTypesSinceR1=[];x.activityRecords=[];});
s.plots.forEach(x=>{x.provenance='normal';x.scopeId=null;});s.creatureState.forEach(x=>x.lastFoodProvenance='normal');
// Quantity lists become call columns after all numeric checks above used the structured values.
for(const r of c.recipes){r.ingredients=r.ingredients.map(z=>z.itemId+' '+z.quantity).join(' / ');r.substitutes=r.substitutes.map(z=>z.requiredProperty+' '+z.quantity+(z.slot?' slot='+z.slot:'')).join(' / ');}
for(const x of c.shops)x.stock=x.stock.map(z=>z.itemId+' '+z.quantity).join(' / ');
// Real asset addresses only; every assetKey without an entry resolves to PLACEHOLDER/<key> (bindings are derived by validate.cjs).
c.assetAddresses={};
write('examples/full-catalog.json',c);s.catalogHash=require('node:crypto').createHash('sha256').update(fs.readFileSync(path.join(root,'examples/full-catalog.json'))).digest('hex');write('examples/safe-save.json',s);
console.log('Reviewed data contracts completed');
