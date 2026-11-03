# 필드별 데이터 사전

값은 예제의 명시적 기본 프로파일이다. 콘텐츠별 값 변경은 가능하나 타입/범위/참조 제약은 유지한다. 스키마의 모든 닫힌 객체 필드가 대상이다. 거리=타일, Seconds=시뮬초, Minutes=게임분, Crowns/Price=정수크라운.

## catalog

| 필드 경로 | 타입 | 필수 | 예제 프로파일 | 제약 |
|---|---|---|---|---|
| schemaVersion | const | 필수 | 2 | const=2 |
| contentVersion | string | 필수 | &quot;full-design-0.2-proposal&quot; |  |
| status | const | 필수 | &quot;design-fixture&quot; | const=design-fixture |
| units.distance | string | 필수 | &quot;tile&quot; |  |
| units.duration | string | 필수 | &quot;simulation-second&quot; |  |
| units.calendar | string | 필수 | &quot;game-minute&quot; |  |
| units.currency | string | 필수 | &quot;crown-integer&quot; |  |
| rules.initialMoney | integer | 필수 | 200 | minimum=0 |
| rules.inventorySlots | integer | 필수 | 24 | minimum=0 |
| rules.focusMax | integer | 필수 | 100 | minimum=0 |
| rules.focusRecoveryDelaySeconds | integer | 필수 | 2 | minimum=0 |
| rules.focusRecoveryPerSecond | integer | 필수 | 5 | minimum=0 |
| rules.environmentFocusReward | integer | 필수 | 10 | minimum=0 |
| rules.maxAttackers | integer | 필수 | 3 | minimum=0 |
| rules.trackLimit | integer | 필수 | 3 | minimum=0 |
| rules.dayCycle | integer | 필수 | 80 | minimum=0 |
| rules.seasonDays | integer | 필수 | 20 | minimum=0 |
| rules.termDays | integer | 필수 | 40 | minimum=0 |
| rules.plantSlotsPersonal | integer | 필수 | 6 | minimum=0 |
| rules.plantSlotsShared | integer | 필수 | 12 | minimum=0 |
| rules.applicationContexts | integer | 필수 | 3 | minimum=0 |
| rules.saleRatio | number | 필수 | 0.4 | minimum=0 |
| rules.stackDefault | integer | 필수 | 99 | minimum=0 |
| rules.characterSpeed | integer | 필수 | 3 | minimum=0 |
| rules.collisionRadius | number | 필수 | 0.25 | minimum=0 |
| rules.dodgeInvulnerableSeconds | number | 필수 | 0.3 | minimum=0 |
| rules.dodgeCooldownSeconds | number | 필수 | 1.2 | minimum=0 |
| rules.baseHp | integer | 필수 | 100 | minimum=0 |
| rules.hitInvulnerableSeconds | number | 필수 | 0.6 | minimum=0 |
| rules.midnightNewEncounters | boolean | 필수 | false |  |
| rules.supportAfterDefeats | integer | 필수 | 3 | minimum=0 |
| rules.supportFocusBonus | integer | 필수 | 30 | minimum=0 |
| rules.supportShieldHits | integer | 필수 | 1 | minimum=0 |
| rules.supportDamageAbsorbRatio | number | 필수 | 0.5 | minimum=0 |
| rules.qualitySaleFactor | integer | 필수 | 1 | minimum=0 |
| rules.batchAdditionalMinutes | integer | 필수 | 2 | minimum=0 |
| rules.lifeCastFocusCost | integer | 필수 | 0 | minimum=0 |
| rules.nonresponsiveLifeCastConsumesItems | boolean | 필수 | false |  |
| rules.introFlagKey | string | 필수 | &quot;story.intro_done&quot; |  |
| spells[].id | string | 필수 | &quot;spell.lantern&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| spells[].source | string | 필수 | &quot;SRC-05-T1-R1&quot; |  |
| spells[].name | string | 필수 | &quot;등불&quot; |  |
| spells[].resultTag | string | 필수 | &quot;revealed&quot; |  |
| spells[].rangeTiles | number | 필수 | 5 | minimum=0; maximum=20 |
| spells[].radiusTiles | number | 필수 | 0 | minimum=0; maximum=10 |
| spells[].focusCost | integer | 필수 | 10 | minimum=10; maximum=30 |
| spells[].cooldownSeconds | number | 필수 | 1 | minimum=0; maximum=60 |
| spells[].windupSeconds | number | 필수 | 0.2 |  |
| spells[].projectileSpeedTilesPerSecond | integer | 필수 | 8 |  |
| spells[].damage | integer | 필수 | 0 | minimum=0; maximum=999 |
| spells[].lifeUse | string | 필수 | &quot;기숙사 조명 색과 밝기 조절&quot; |  |
| spells[].exploreUse | string | 필수 | &quot;숨은 잉크·발자국 드러내기&quot; |  |
| spells[].combatUse | string | 필수 | &quot;그림자 적의 약점 노출&quot; |  |
| spells[].usagePolicy | string | 필수 | &quot;valid-reaction-only&quot; |  |
| spells[].assetKey | string | 필수 | &quot;asset.lantern&quot; |  |
| spells[].lifeTemplate | const | 필수 | &quot;target&quot; | const=target |
| spells[].combatTemplate | string/string/string | 필수 | &quot;area&quot; | enum=target/area/projectile |
| spells[].costPolicy | string | 필수 | &quot;combat-only-on-emission&quot; |  |
| spells[].effectDurationSeconds | integer/number | 필수 | 6 |  |
| spells[].nearbyActionIds[] | string | 필수 | &quot;activity.q24&quot; |  |
| spells[].acceptTargetTags[] | string | 필수 | &quot;dark&quot; |  |
| spells[].additionalStateTags[] | string | 필수 | &quot;lit&quot; |  |
| classes[].id | string | 필수 | &quot;class.lantern_basic&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| classes[].source | string | 필수 | &quot;§5 M01 등불 basic&quot; |  |
| classes[].spellId | string | 필수 | &quot;spell.lantern&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| classes[].stage | string/string | 필수 | &quot;basic&quot; | enum=basic/application |
| classes[].tier | string/string/string/string | 필수 | &quot;intro&quot; | enum=intro/beginner/intermediate/advanced |
| classes[].prerequisiteClassIds[] | string | 필수 | 정의참조 | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| classes[].unlockTierCount | integer | 필수 | 0 |  |
| classes[].unlockTier | string | 필수 | &quot;none&quot; |  |
| classes[].durationMinutes | integer | 필수 | 60 | minimum=0; maximum=1440 |
| classes[].loanItemId | string | 필수 | &quot;item.loan_kit&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| classes[].rewardKey | string | 필수 | &quot;reward.lantern_basic&quot; |  |
| classes[].practiceContextId | string | 필수 | &quot;context.lantern_basic&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| classes[].introduced | boolean | 필수 | true |  |
| classes[].requiresIntro | boolean | 필수 | false |  |
| classes[].grantOnFirstAccept | string | 필수 | &quot;item.plant_1 3 grant&quot; | pattern=^[^\r\n]*$ |
| classes[].timeCostPolicy | string | 필수 | &quot;definition-durationMinutes-success-once&quot; |  |
| reactions[].id | string | 필수 | &quot;reaction.combo_1&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| reactions[].source | string | 필수 | &quot;§5 조합6종&quot; |  |
| reactions[].spellIds[] | string | 필수 | &quot;spell.water&quot; |  |
| reactions[].resultTag | string | 필수 | &quot;frozen&quot; |  |
| reactions[].priority | integer | 필수 | 100 |  |
| reactions[].persistentInRoom | boolean | 필수 | true |  |
| reactions[].consumeTag | boolean | 필수 | false |  |
| reactions[].windowSeconds | integer | 필수 | 0 |  |
| reactions[].scope | string | 필수 | &quot;explicit-target&quot; |  |
| reactions[].protectionFirst | boolean | 필수 | true |  |
| reactions[].paths | string | 필수 | &quot;spell.frost wet&quot; | pattern=^[^\r\n]*$ |
| reactions[].windowPolicy | string | 필수 | &quot;until-room-reset&quot; |  |
| reactions[].nonmatchObservationId | string | 필수 | &quot;observation.reaction_1&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| reactions[].blockedTags[] | string | 필수 | &quot;person&quot; |  |
| enemies[].id | string | 필수 | &quot;enemy.ink&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| enemies[].source | string | 필수 | &quot;SRC-07-T1-R1&quot; |  |
| enemies[].name | string | 필수 | &quot;잉크 방울&quot; |  |
| enemies[].baseId | string/null | 필수 | null |  |
| enemies[].archetype | string | 필수 | &quot;charge&quot; |  |
| enemies[].hp | integer | 필수 | 30 | minimum=1; maximum=9999 |
| enemies[].damage | integer | 필수 | 12 |  |
| enemies[].speedTilesPerSecond | integer | 필수 | 2 |  |
| enemies[].telegraphSeconds | number | 필수 | 1 | minimum=0.1; maximum=10 |
| enemies[].recoverSeconds | number | 필수 | 1.5 |  |
| enemies[].attackSeconds | number | 필수 | 0.3 |  |
| enemies[].rangeTiles | integer | 필수 | 4 |  |
| enemies[].targetLockedAt | string | 필수 | &quot;telegraph-start&quot; |  |
| enemies[].extraBehavior | string | 필수 | &quot;none&quot; |  |
| enemies[].telegraph | string | 필수 | &quot;몸이 한쪽으로 부풀어 돌진 준비&quot; |  |
| enemies[].counter | string | 필수 | &quot;옆 회피 후 공격&quot; |  |
| enemies[].alternative | string | 필수 | &quot;물결로 잉크 흐름 분산&quot; |  |
| enemies[].assetKey | string | 필수 | &quot;asset.enemy_ink&quot; |  |
| enemies[].behavior | string | 필수 | &quot;charge / ink_dispersal&quot; | pattern=^[^\r\n]*$ |
| enemies[].weak | string | 필수 | &quot;wet x1 disable=2&quot; | pattern=^[^\r\n]*$ |
| enemies[].resolutionOutcomes[] | string | 필수 | &quot;defeated&quot; |  |
| enemies[].tokenPool | string | 필수 | &quot;encounter-shared&quot; |  |
| enemies[].initialDamageGate | string | 필수 | &quot;none&quot; |  |
| enemies[].ringCount | integer | 필수 | 0 |  |
| regions[].id | string | 필수 | &quot;region.n_1&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| regions[].source | string | 필수 | &quot;§7 탐험4권역&quot; |  |
| regions[].name | string | 필수 | &quot;속삭임 숲&quot; |  |
| regions[].safePointId | string | 필수 | &quot;safe.n_1&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| regions[].mapId | string | 필수 | &quot;map.region_1&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| regions[].discoveryRequiredForTravel | boolean | 필수 | true |  |
| rooms[].id | string | 필수 | &quot;room.x01&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| rooms[].source | string | 필수 | &quot;SRC-07-T3-R1&quot; |  |
| rooms[].name | string | 필수 | &quot;발광 이끼를 이어 야행성 생물 경로 관찰&quot; |  |
| rooms[].regionId | string | 필수 | &quot;region.n_1&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| rooms[].purpose | string | 필수 | &quot;optional-investigation&quot; |  |
| rooms[].stages[] | string | 필수 | &quot;observe&quot; |  |
| rooms[].rewardDescription | string | 필수 | &quot;관찰도감과 조명 재료&quot; |  |
| rooms[].requiredSpellId | string | 필수 | &quot;spell.lantern&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| rooms[].loanItemId | string | 필수 | &quot;item.loan_kit&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| rooms[].resetPolicy | string | 필수 | &quot;current-room-entry&quot; |  |
| rooms[].completionKey | string | 필수 | &quot;complete.x01&quot; |  |
| rooms[].hints | string | 필수 | &quot;observation \&quot;방에서 강조된 조작 대상의 상태를 관찰하세요.\&quot; / property \&quot;대상의 성질과 입구 대여 도구의 같은 성질을 비교하세요.\&quot; / action \&quot;수첩의 현재 목표 순서대로 도구를 적용하고 변경된 상태를 확인하세요.\ | pattern=^[^\r\n]*$ |
| npcs[].id | string | 필수 | &quot;npc.serin&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| npcs[].source | string | 필수 | &quot;SRC-08-T1-R1&quot; |  |
| npcs[].sourceId | string | 필수 | &quot;N01&quot; |  |
| npcs[].name | string | 필수 | &quot;서린&quot; |  |
| npcs[].role | string | 필수 | &quot;student&quot; |  |
| npcs[].adult | boolean | 필수 | true |  |
| npcs[].romanceAllowed | boolean | 필수 | true |  |
| npcs[].places | string | 필수 | &quot;아침 식당, 밤 휴게실&quot; |  |
| npcs[].concern | string | 필수 | &quot;도움받는 것을 부끄러워하는 장학생&quot; |  |
| npcs[].activity | string | 필수 | &quot;공동 간식·실습 준비&quot; |  |
| npcs[].homeDestinationId | string | 필수 | &quot;dest.serin_home&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| npcs[].baseDialogueBudget | integer | 필수 | 24 |  |
| npcs[].situationalDialogueBudget | integer | 필수 | 12 |  |
| npcs[].accompanyDailyLimit | integer | 필수 | 1 |  |
| relationships[].id | string | 필수 | &quot;relation.r01&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| relationships[].source | string | 필수 | &quot;SRC-08-T2-R1&quot; |  |
| relationships[].sourceId | string | 필수 | &quot;R01&quot; |  |
| relationships[].npcId | string | 필수 | &quot;npc.serin&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| relationships[].index | integer | 필수 | 1 | minimum=1; maximum=3 |
| relationships[].previousId | string/null | 필수 | null |  |
| relationships[].description | string | 필수 | &quot;같이 간식을 만들다 실패한 실습 노트를 발견&quot; |  |
| relationships[].uniqueActivityTypesRequired | integer | 필수 | 1 |  |
| relationships[].followupInsideScene | boolean | 필수 | false |  |
| relationships[].friendshipAlways | boolean | 필수 | true |  |
| relationships[].romanceVariant | boolean | 필수 | false |  |
| relationships[].durationTargetMinutes | integer | 필수 | 9 |  |
| relationships[].completionKey | string | 필수 | &quot;complete.r01&quot; |  |
| story[].id | string | 필수 | &quot;case.a&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| story[].source | string | 필수 | &quot;SRC-09-T1-R1&quot; |  |
| story[].sourceId | string | 필수 | &quot;A&quot; |  |
| story[].name | string | 필수 | &quot;A 물길·저수로&quot; |  |
| story[].requiresIntro | boolean | 필수 | true |  |
| story[].externalCasePrerequisites[] | string | 필수 | 정의참조 | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| story[].nodes[].id | string | 필수 | &quot;node.a1&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| story[].nodes[].sourceId | string | 필수 | &quot;A1&quot; |  |
| story[].nodes[].description | string | 필수 | &quot;A1 식당의 뜨거운 물이 사라져 공급 경로 추적&quot; |  |
| story[].nodes[].evidenceId | string | 필수 | &quot;evidence.a1&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| story[].nodes[].loanItemId | string | 필수 | &quot;item.loan_a&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| story[].nodes[].roomId | string | 필수 | &quot;room.case_a1&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| story[].nodes[].regionId | string | 필수 | &quot;region.n_2&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| story[].nodes[].encounterId | null/string | 필수 | null |  |
| story[].nodes[].resetPolicy | string | 필수 | &quot;overlay-only-preserve-confirmed&quot; |  |
| story[].nodes[].goals | string | 필수 | &quot;observe: observe object.a1_target_0 observed / device: setState object.a1_target_1 supply_stopped count=2 / repair: apply object.a1_target_2 repaired&quot; | pattern=^[^\r\n]*$ |
| story[].nodes[].hints | string | 필수 | &quot;observation \&quot;방에서 강조된 조작 대상의 상태를 관찰하세요.\&quot; goal=observe / property \&quot;대상의 성질과 입구 대여 도구의 같은 성질을 비교하세요.\&quot; goal=device / action \&quot;수첩의 현재 목표 순서대로 | pattern=^[^\r\n]*$ |
| story[].truth | string | 필수 | &quot;고장은 외부 침입이 아니라 잘못된 중앙 명령&quot; |  |
| story[].resolutionFlag | string | 필수 | &quot;flag.a_resolved&quot; |  |
| story[].rewardCrowns | integer | 필수 | 150 |  |
| story[].combatOutcome | string | 필수 | &quot;outcome.a_stopped&quot; |  |
| story[].deviceOutcome | string | 필수 | &quot;outcome.a_stopped&quot; |  |
| story[].worldChangeId | string | 필수 | &quot;projection.a&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| story[].loanDescription | string | 필수 | &quot;수동 펌프·냉각 물통·수리 공구. A1 공급 추적, A2 밸브 시험, A3 공급 차단 모두 대응&quot; |  |
| activities[].id | string | 필수 | &quot;activity.h01&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| activities[].source | string | 필수 | &quot;SRC-10-T1-R1&quot; |  |
| activities[].sourceId | string | 필수 | &quot;H01&quot; |  |
| activities[].kind | string/string/string | 필수 | &quot;festival&quot; | enum=festival/club/request |
| activities[].name | string | 필수 | &quot;H01 환영 실습제&quot; |  |
| activities[].requiredCaseCount | integer | 필수 | 0 | minimum=0; maximum=6 |
| activities[].requiresIntro | boolean | 필수 | true |  |
| activities[].regularCycleDay | integer | 필수 | 9 | minimum=0; maximum=79 |
| activities[].practiceReservation | boolean | 필수 | true |  |
| activities[].description | string | 필수 | &quot;마법 체험 3개 중 선택, 서로의 이름 알기, 공용 장식 배치&quot; |  |
| activities[].rewardDescription | string | 필수 | &quot;관심 동아리와 친구 선택의 계기&quot; |  |
| activities[].durationTargetMinutes | integer | 필수 | 24 |  |
| activities[].repeatReward | boolean | 필수 | false |  |
| activities[].loanItemId | string | 필수 | &quot;item.loan_kit&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| activities[].prerequisiteActivityIds[] | string | 필수 | 정의참조 |  |
| projects[].id | string | 필수 | &quot;project.p01&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| projects[].source | string | 필수 | &quot;SRC-06-T3-R1&quot; |  |
| projects[].sourceId | string | 필수 | &quot;P01&quot; |  |
| projects[].name | string | 필수 | &quot;자동 급수 화분대&quot; |  |
| projects[].task | string | 필수 | &quot;수로 방향과 물결 범위 배치&quot; |  |
| projects[].change | string | 필수 | &quot;개인 화분 물주기 부담 감소&quot; |  |
| projects[].when | string | 필수 | &quot;any \&quot;completed class.water_basic\&quot; \&quot;discovered case.b\&quot;&quot; | pattern=^[^\r\n]*$ |
| projects[].worldChangeId | string | 필수 | &quot;projection.p01&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| projects[].permanentAfterInstall | boolean | 필수 | true |  |
| projects[].durationTargetMinutes | integer | 필수 | 20 |  |
| projects[].recipeIds[] | string | 필수 | &quot;recipe.tool_1&quot; |  |
| projects[].grantOnAccept | string | 필수 | &quot;item.plant_1 4 grant / item.scrap 1 grant&quot; | pattern=^[^\r\n]*$ |
| projects[].loanTaskCompletionAllowed | boolean | 필수 | true |  |
| projects[].personalLoanOutputAllowed | boolean | 필수 | false |  |
| plants[].id | string | 필수 | &quot;plant.n_1&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| plants[].source | string | 필수 | &quot;§6 재배 제안 프로파일&quot; |  |
| plants[].name | string | 필수 | &quot;달빛허브&quot; |  |
| plants[].growthDays | integer | 필수 | 2 | minimum=2; maximum=4 |
| plants[].seedItemId | string | 필수 | &quot;item.seed_1&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| plants[].harvestItemId | string | 필수 | &quot;item.plant_1&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| plants[].allowedParts[] | string/string/string | 필수 | &quot;leaf&quot; | enum=leaf/flower/seed |
| plants[].seasonBonus | integer | 필수 | 0 |  |
| plants[].bonusQuantity | integer | 필수 | 1 |  |
| plants[].baseQuantity | integer | 필수 | 1 |  |
| plants[].greenhouseAlways | boolean | 필수 | true |  |
| plants[].waterRequired | boolean | 필수 | true |  |
| plants[].parts.leaf.itemId | string | 필수 | &quot;item.plant_1&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| plants[].parts.leaf.quantity | integer | 필수 | 1 |  |
| plants[].parts.flower.itemId | string | 필수 | &quot;item.plant_1_flower&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| plants[].parts.flower.quantity | integer | 필수 | 1 |  |
| plants[].parts.seed.itemId | string | 필수 | &quot;item.seed_1&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| plants[].parts.seed.quantity | integer | 필수 | 1 |  |
| plants[].group | string | 필수 | &quot;herb&quot; |  |
| items[].id | string | 필수 | &quot;item.plant_1&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| items[].source | string | 필수 | &quot;§6 식물12종, 개별 이름·성질은 제안&quot; |  |
| items[].name | string | 필수 | &quot;달빛허브&quot; |  |
| items[].kind | string/string/string/string/string | 필수 | &quot;material&quot; | enum=material/seed/tool/furniture/consumable |
| items[].stackMax | integer | 필수 | 99 | minimum=1; maximum=999 |
| items[].buyPrice | integer | 필수 | 10 | minimum=0; maximum=999999 |
| items[].saleBasePrice | integer | 필수 | 10 | minimum=0; maximum=999999 |
| items[].properties[] | string | 필수 | &quot;calm&quot; |  |
| items[].qualityEnabled | boolean | 필수 | false |  |
| items[].protected | boolean | 필수 | false |  |
| recipes[].id | string | 필수 | &quot;recipe.alchemy_1&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| recipes[].source | string | 필수 | &quot;§6 설계 제안 프로파일&quot; |  |
| recipes[].name | string | 필수 | &quot;진정 향&quot; |  |
| recipes[].kind | string/string/string/string | 필수 | &quot;alchemy&quot; | enum=alchemy/cooking/tool/furniture |
| recipes[].ingredients | string | 필수 | &quot;item.plant_1 3 / item.plant_2 1&quot; | pattern=^[^\r\n]*$ |
| recipes[].substitutes | string | 필수 | &quot;calm 3&quot; | pattern=^[^\r\n]*$ |
| recipes[].requiredProperties[] | string | 필수 | &quot;calm&quot; |  |
| recipes[].outputItemId | string | 필수 | &quot;item.alchemy_1&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| recipes[].outputQuantity | integer | 필수 | 1 | minimum=1; maximum=999 |
| recipes[].batchMax | integer | 필수 | 20 | minimum=1; maximum=99 |
| recipes[].feeCrowns | integer | 필수 | 0 |  |
| recipes[].failureItemId | string | 필수 | &quot;item.scrap&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| recipes[].failureQuantity | integer | 필수 | 1 |  |
| recipes[].qualityThreshold | integer | 필수 | 2 |  |
| recipes[].process | string/string/string | 필수 | &quot;heat&quot; | enum=heat/cool/resonate |
| recipes[].timeCostMinutes | integer | 필수 | 10 | minimum=0; maximum=1440 |
| recipes[].qualityRule | string | 필수 | &quot;matched-process-point-plus-p07-facility-point&quot; |  |
| recipes[].qualitySaleFactor | integer | 필수 | 1 |  |
| recipes[].allowedFacilityIds[] | string | 필수 | &quot;facility.alchemy&quot; |  |
| shops[].id | string | 필수 | &quot;shop.bello&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| shops[].source | string | 필수 | &quot;§6 상점 정책 설계 제안&quot; |  |
| shops[].npcId | string | 필수 | &quot;npc.bello&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| shops[].restock | string | 필수 | &quot;daily-absolute-day&quot; |  |
| shops[].stock | string | 필수 | &quot;item.plant_1 -1 / item.plant_2 -1 / item.plant_3 -1 / item.plant_4 -1 / item.plant_5 -1 / item.plant_6 -1 / item.plant_7 -1 / item.plant_8 -1 / item.plant_9 -1 / it | pattern=^[^\r\n]*$ |
| shops[].loanItemIds[] | string | 필수 | &quot;item.loan_kit&quot; |  |
| shops[].buyback | boolean | 필수 | false |  |
| creatures[].id | string | 필수 | &quot;creature.greenhouse&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| creatures[].source | string | 필수 | &quot;§6 소동물 돌봄 설계 제안&quot; |  |
| creatures[].name | string | 필수 | &quot;온실 소동물&quot; |  |
| creatures[].foodProperty | string | 필수 | &quot;calm&quot; |  |
| creatures[].preferredLight | number | 필수 | 0.5 |  |
| creatures[].preferredShelter | boolean | 필수 | true |  |
| creatures[].dailyYieldItemId | string | 필수 | &quot;item.plant_1&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| creatures[].yieldQuantity | integer | 필수 | 1 |  |
| creatures[].neglectPolicy | string | 필수 | &quot;yield-paused-no-death&quot; |  |
| furniture[].id | string | 필수 | &quot;furniture.n_1&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| furniture[].source | string | 필수 | &quot;§6 배치 제안&quot; |  |
| furniture[].itemId | string | 필수 | &quot;item.furniture_1&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| furniture[].widthTiles | integer | 필수 | 2 |  |
| furniture[].heightTiles | integer | 필수 | 1 |  |
| furniture[].rotationSteps | integer | 필수 | 4 |  |
| furniture[].blocksMovement | boolean | 필수 | true |  |
| furniture[].minimumExitWidthTiles | integer | 필수 | 1 |  |
| furniture[].assetKey | string | 필수 | &quot;asset.furniture_1&quot; |  |
| objects[].id | string | 필수 | &quot;object.pot&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| objects[].source | string | 필수 | &quot;§15 물결 공통반응 예&quot; |  |
| objects[].tags[] | string | 필수 | &quot;plant&quot; |  |
| objects[].spellId | string | 필수 | &quot;spell.water&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| objects[].reactionId | string | 필수 | &quot;reaction.water_0&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| objects[].resultTag | string | 필수 | &quot;watered&quot; |  |
| objects[].applicationContextId | string | 필수 | &quot;context.pot&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| scenarios[].id | string | 필수 | &quot;scenario.school&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| scenarios[].source | string | 필수 | &quot;§3 연결&quot; |  |
| scenarios[].steps[] | string | 필수 | &quot;class.levitate_basic&quot; |  |
| scenarios[].expected | string | 필수 | &quot;고유 응용·공동경험·영구 무대 변화 각각 한 번&quot; |  |
| characterCreation.nameMinGraphemes | integer | 필수 | 1 |  |
| characterCreation.nameMaxGraphemes | integer | 필수 | 10 |  |
| characterCreation.unicodeNormalization | string | 필수 | &quot;NFC&quot; |  |
| characterCreation.defaultPronounKey | string | 필수 | &quot;neutral&quot; |  |
| characterCreation.appearanceIds[] | string | 필수 | &quot;appearance.default&quot; |  |
| characterCreation.allStudentsAdult | boolean | 필수 | true |  |
| characterCreation.confirmationRequired | boolean | 필수 | true |  |
| dialogues[].id | string | 필수 | &quot;dialogue.serin_special&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| dialogues[].source | string | 필수 | &quot;§8 특별반응 예제·대사제안&quot; |  |
| dialogues[].npcId | string | 필수 | &quot;npc.serin&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| dialogues[].tier | integer | 필수 | 4 | minimum=1; maximum=4 |
| dialogues[].when | string | 필수 | &quot;completed project.p08&quot; | pattern=^[^\r\n]*$ |
| dialogues[].oncePerSave | boolean | 필수 | true |  |
| dialogues[].cooldownDays | integer | 필수 | 0 |  |
| dialogues[].text | string | 필수 | &quot;무대가 생겼네. 다음에는 우리도 함께 준비할까?&quot; |  |
| endings[].id | string | 필수 | &quot;ending.supervised_core&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| endings[].source | string | 필수 | &quot;§9 최종3선택&quot; |  |
| endings[].label | string | 필수 | &quot;감독하에 중심핵 안정화&quot; |  |
| endings[].requiredResolvedCases | integer | 필수 | 6 |  |
| endings[].worldChangeId | string | 필수 | &quot;projection.ending_supervised_core&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| endings[].rewardKey | string | 필수 | &quot;reward.final_common&quot; |  |
| endings[].continueLife | boolean | 필수 | true |  |
| onboarding[].id | string | 필수 | &quot;intro.s1&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| onboarding[].source | string | 필수 | &quot;§12 첫90분&quot; |  |
| onboarding[].durationTargetMinutes | integer | 필수 | 10 |  |
| onboarding[].completionKey | string | 필수 | &quot;complete.intro_s1&quot; |  |
| onboarding[].forcedCombat | boolean | 필수 | false |  |
| onboarding[].grantedSpellId | string/null | 필수 | null |  |
| encounters[].id | string | 필수 | &quot;encounter.a3&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| encounters[].source | string | 필수 | &quot;A3&quot; |  |
| encounters[].roomId | string | 필수 | &quot;room.case_a3&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| encounters[].enemies | string | 필수 | &quot;enemy.cleaner 1&quot; | pattern=^[^\r\n]*$ |
| encounters[].maxAttackers | integer | 필수 | 3 |  |
| encounters[].deviceGoalIds[] | string | 필수 | &quot;room.case_a3:device&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z0-9_]+)+:[a-z][a-z0-9_]*$ |
| encounters[].successOutcomeId | string | 필수 | &quot;outcome.a_stopped&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| encounters[].defeatKey | string | 필수 | &quot;encounter.a3&quot; |  |
| encounters[].supportAfterDefeats | integer | 필수 | 3 |  |
| encounters[].targetMinMinutes | integer | 필수 | 3 |  |
| encounters[].targetMaxMinutes | integer | 필수 | 5 |  |
| encounters[].phases | integer | 필수 | 1 |  |
| truthScenes[].id | string | 필수 | &quot;truth.a&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| truthScenes[].source | string | 필수 | &quot;§9 2개정리&quot; |  |
| truthScenes[].requiredCaseCount | integer | 필수 | 2 |  |
| truthScenes[].evidencePolicy | string | 필수 | &quot;owned-evidence-only&quot; |  |
| truthScenes[].replayFromJournal | boolean | 필수 | true |  |
| finalDefinition.id | string | 필수 | &quot;final.practical&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| finalDefinition.source | string | 필수 | &quot;§9 최종실습&quot; |  |
| finalDefinition.requiredCaseCount | integer | 필수 | 6 |  |
| finalDefinition.manualStart | boolean | 필수 | true |  |
| finalDefinition.archiveBeforeStart | boolean | 필수 | true |  |
| finalDefinition.friendRoleMinStage | integer | 필수 | 2 |  |
| finalDefinition.staffFallback | boolean | 필수 | true |  |
| finalDefinition.roomId | string | 필수 | &quot;room.final_core&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| finalDefinition.loanItemId | string | 필수 | &quot;item.loan_kit&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| finalDefinition.goals | string | 필수 | &quot;stabilize: setState object.final_core stabilized&quot; | pattern=^[^\r\n]*$ |
| finalDefinition.stages | string | 필수 | &quot;flow_1: stabilizeChannel / flow_2: stabilizeChannel / flow_3: stabilizeChannel&quot; | pattern=^[^\r\n]*$ |
| finalDefinition.finalEncounterId | string | 필수 | &quot;encounter.final&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| finalDefinition.endingIds[] | string | 필수 | &quot;ending.supervised_core&quot; |  |
| finalDefinition.replayPolicy | string | 필수 | &quot;archive-or-recollection-no-reward-merge&quot; |  |
| caseDerivation.owner | string | 필수 | &quot;CaseService&quot; |  |
| caseDerivation.trigger | string | 필수 | &quot;node-complete-commit&quot; |  |
| caseDerivation.resolveWhen | string | 필수 | &quot;node3-completed-and-all-internal-predecessors-completed&quot; |  |
| caseDerivation.writes[] | string | 필수 | &quot;questStates.resolved&quot; |  |
| caseDerivation.resolutionOrderPolicy | string | 필수 | &quot;append-on-first-commit-stable-sequence&quot; |  |
| caseDerivation.truthInviteThresholds[] | integer | 필수 | 2 |  |
| caseDerivation.finalReadyThreshold | integer | 필수 | 6 |  |
| caseDerivation.atomicWithNodeCompletion | boolean | 필수 | true |  |
| difficultyPresets[].id | string | 필수 | &quot;difficulty.basic&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| difficultyPresets[].source | string | 필수 | &quot;§7&quot; |  |
| difficultyPresets[].damageMultiplier | integer/number | 필수 | 1 |  |
| difficultyPresets[].telegraphMultiplier | integer/number | 필수 | 1 |  |
| difficultyPresets[].maxAttackers | integer | 필수 | 3 |  |
| facilities[].id | string | 필수 | &quot;facility.alchemy&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| facilities[].source | string | 필수 | &quot;제작시설명시&quot; |  |
| facilities[].disciplines[] | string | 필수 | &quot;alchemy&quot; |  |
| facilities[].unlockProjectId | null/string | 필수 | null |  |
| facilities[].qualityBonus | integer | 필수 | 0 |  |
| facilities[].concurrentUsers | integer | 필수 | 1 |  |
| schoolRule.id | string | 필수 | &quot;school.shared_tool_repair&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| schoolRule.source | string | 필수 | &quot;§4 공동도구후속&quot; |  |
| schoolRule.trigger | string | 필수 | &quot;shared-tool-damaged&quot; |  |
| schoolRule.followupTemplate | string | 필수 | &quot;repair-together&quot; |  |
| schoolRule.capabilityId | string | 필수 | &quot;cap.repair&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| schoolRule.loanItemId | string | 필수 | &quot;item.loan_kit&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| schoolRule.noExpulsion | boolean | 필수 | true |  |
| schoolRule.noMainGate | boolean | 필수 | true |  |
| schoolRule.budgetCategory | string | 필수 | &quot;incidental-life&quot; |  |
| schoolRule.repeatRewards | boolean | 필수 | false |  |
| assetAddresses | object | 필수 | {} |  |

## save

| 필드 경로 | 타입 | 필수 | 예제 프로파일 | 제약 |
|---|---|---|---|---|
| saveVersion | const | 필수 | 2 | const=2 |
| contentVersion | string | 필수 | &quot;full-design-0.2-proposal&quot; |  |
| generation | integer | 필수 | 1 |  |
| safePoint | string/string/string/string | 필수 | &quot;world-safe&quot; | enum=world-safe/scene-entry/scene-complete/room-safe |
| clock.absoluteMinute | integer | 필수 | 420 | minimum=0 |
| clock.fraction | number | 필수 | 0 | minimum=0; exclusiveMaximum=1 |
| player.name | string | 필수 | &quot;새 학생&quot; |  |
| player.pronounKey | string | 필수 | &quot;neutral&quot; |  |
| player.appearanceId | string | 필수 | &quot;appearance.default&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| player.hp | integer | 필수 | 100 | minimum=0; maximum=100 |
| player.focus | integer | 필수 | 100 | minimum=0; maximum=100 |
| player.mapId | string | 필수 | &quot;map.courtyard&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| player.x | number | 필수 | 1.5 |  |
| player.y | number | 필수 | 1.5 |  |
| player.quickSlots[] | string/null | 필수 | &quot;spell.lantern&quot; |  |
| wallet | integer | 필수 | 200 | minimum=0; maximum=2147483647 |
| stacks[].itemId | string | 필수 | &quot;item.plant_1&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| stacks[].quantity | integer | 필수 | 6 | minimum=1; maximum=999 |
| stacks[].provenance | string/string | 필수 | &quot;normal&quot; | enum=normal/grant |
| stacks[].quality | string/string | 필수 | &quot;standard&quot; | enum=standard/fine |
| stacks[].scopeId | null | 필수 | null |  |
| mail[].id | string | 필수 | &quot;mail.intro&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| mail[].attachments[].itemId | string | 필수 | 정의참조 | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| mail[].attachments[].quantity | integer | 필수 | 정의참조 | minimum=1; maximum=999 |
| mail[].attachments[].provenance | string/string | 필수 | 정의참조 | enum=normal/grant |
| mail[].attachments[].quality | string/string | 필수 | 정의참조 | enum=standard/fine |
| mail[].attachments[].scopeId | null | 필수 | 정의참조 |  |
| mail[].kind | string/string/string/string | 필수 | &quot;info&quot; | enum=invite/reminder/overflow/info |
| mail[].createdAt | integer | 필수 | 420 | minimum=0 |
| mail[].reservationId | string/null | 필수 | null |  |
| mail[].state | string/string/string/string | 필수 | &quot;unread&quot; | enum=unread/accepted/deferred/declined |
| completed[] | string | 필수 | &quot;class.lantern_basic&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| evidence[] | string | 필수 | 정의참조 | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| learnedSpellIds[] | string | 필수 | &quot;spell.lantern&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| spellContexts[].spellId | string | 필수 | 정의참조 | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| spellContexts[].contextIds[] | string | 필수 | 정의참조 | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| relations[].npcId | string | 필수 | &quot;npc.serin&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| relations[].affinity | integer | 필수 | 0 | minimum=0; maximum=100 |
| relations[].greeted | boolean | 필수 | false |  |
| relations[].activityKeys[] | string | 필수 | 정의참조 | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| relations[].conflict | string/string | 필수 | &quot;none&quot; | enum=none/pending-reconcile |
| relations[].lastTalkDay | integer | 필수 | -1 |  |
| relations[].lastGiftDay | integer | 필수 | -1 |  |
| relations[].expression | string/string | 필수 | &quot;friendship&quot; | enum=friendship/romance |
| relations[].r1BaselineActivityKeys[] | string | 필수 | 정의참조 | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| relations[].stage | integer | 필수 | 0 | minimum=0; maximum=3 |
| relations[].rewardedActivityTypes[] | string/string/string/string/string/string/string | 필수 | 정의참조 | enum=class/project/club/meal/request/festival/companion |
| relations[].r2ActivityTypesSinceR1[] | string/string/string/string/string/string/string | 필수 | 정의참조 | enum=class/project/club/meal/request/festival/companion |
| relations[].activityRecords[].activityId | string | 필수 | 정의참조 | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| relations[].activityRecords[].activityType | string/string/string/string/string/string/string | 필수 | 정의참조 | enum=class/project/club/meal/request/festival/companion |
| relations[].activityRecords[].occurrenceSequence | integer | 필수 | 정의참조 | minimum=0 |
| plots[].plotId | string | 필수 | &quot;plot.personal_1&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| plots[].plantId | string/null | 필수 | &quot;plant.n_1&quot; |  |
| plots[].growthTicks | integer | 필수 | 0 |  |
| plots[].wateredDay | integer | 필수 | -1 |  |
| plots[].harvestPart | string/string/string | 필수 | &quot;leaf&quot; | enum=leaf/flower/seed |
| plots[].lastProcessedDay | integer | 필수 | -1 |  |
| plots[].provenance | string/string | 필수 | &quot;normal&quot; | enum=normal/grant |
| plots[].scopeId | null | 필수 | null |  |
| installedProjects[] | string | 필수 | 정의참조 | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| placements[].id | string | 필수 | 정의참조 | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| placements[].furnitureId | string | 필수 | 정의참조 | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| placements[].mapId | string | 필수 | 정의참조 | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| placements[].x | integer | 필수 | 정의참조 | minimum=0 |
| placements[].y | integer | 필수 | 정의참조 | minimum=0 |
| placements[].rotation | integer | 필수 | 정의참조 | minimum=0; maximum=3 |
| placements[].provenance | string/string | 필수 | 정의참조 | enum=normal/grant |
| appointments[].id | string | 필수 | 정의참조 | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| appointments[].status | string/string/string | 필수 | 정의참조 | enum=Offered/Accepted/Ready |
| appointments[].acceptedAt | integer | 필수 | 정의참조 | minimum=0 |
| roomCheckpoints[].roomId | string | 필수 | 정의참조 | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| roomCheckpoints[].completedObjectIds[] | string | 필수 | 정의참조 | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| roomCheckpoints[].confirmedRewardKeys[] | string | 필수 | 정의참조 | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| rewardLedger[] | string | 필수 | &quot;reward.lantern_basic&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| dayJobs[] | string | 필수 | 정의참조 | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| settings.textScale | integer | 필수 | 1 |  |
| settings.flash | boolean | 필수 | false |  |
| settings.shake | boolean | 필수 | false |  |
| settings.damageMultiplier | number | 필수 | 1 | minimum=0; maximum=3 |
| settings.telegraphMultiplier | number | 필수 | 1 | minimum=0; maximum=3 |
| settings.aimAssist | boolean | 필수 | true |  |
| settings.textSpeed | integer | 필수 | 30 |  |
| settings.particleDensity | number | 필수 | 0.7 |  |
| settings.holdToToggle | boolean | 필수 | true |  |
| settings.masterVolume | integer | 필수 | 1 |  |
| settings.subtitles | boolean | 필수 | true |  |
| settings.aimAssistStrength | number | 필수 | 0.5 | minimum=0; maximum=1 |
| settings.preset | string/string/string/string | 필수 | &quot;basic&quot; | enum=basic/comfort/challenge/custom |
| discoveredCaseIds[] | string | 필수 | &quot;case.a&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| questStates[].caseId | string | 필수 | &quot;case.a&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| questStates[].status | string/string/string/string | 필수 | &quot;discovered&quot; | enum=discovered/investigating/resolved/epilogue |
| questStates[].activeNodeId | string/null | 필수 | &quot;node.a1&quot; |  |
| questStates[].resolved | boolean | 필수 | false |  |
| classRecords[].classId | string | 필수 | &quot;class.lantern_basic&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| classRecords[].completed | boolean | 필수 | true |  |
| classRecords[].methodsUsed[] | string | 필수 | &quot;method.light_color&quot; |  |
| classRecords[].bestScore | integer | 필수 | 1 |  |
| classRecords[].supportClaimed | boolean | 필수 | true |  |
| discoveredSafePointIds[] | string | 필수 | &quot;safe.n_1&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| npcPositions[].npcId | string | 필수 | &quot;npc.serin&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| npcPositions[].mapId | string | 필수 | &quot;map.courtyard&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| npcPositions[].x | number | 필수 | 1.5 |  |
| npcPositions[].y | number | 필수 | 4.5 |  |
| defeats[].encounterId | string | 필수 | &quot;encounter.a3&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| defeats[].count | integer | 필수 | 0 | minimum=0 |
| shopState[].shopId | string | 필수 | &quot;shop.bello&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| shopState[].lastRestockDay | integer | 필수 | 0 |  |
| shopState[].remaining[].itemId | string | 필수 | &quot;item.tool_1&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| shopState[].remaining[].quantity | integer | 필수 | 5 |  |
| creatureState[].creatureId | string | 필수 | &quot;creature.greenhouse&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| creatureState[].fedDay | integer | 필수 | -1 |  |
| creatureState[].comfort | number | 필수 | 0.5 | minimum=0; maximum=1 |
| creatureState[].lastYieldDay | integer | 필수 | -1 |  |
| creatureState[].lastFoodProvenance | string/string | 필수 | &quot;normal&quot; | enum=normal/grant |
| journal[].entryId | string | 필수 | &quot;journal.case_a&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| journal[].read | boolean | 필수 | false |  |
| journal[].hintLevel | integer | 필수 | 0 | minimum=0; maximum=3 |
| dialogueHistory[].npcId | string | 필수 | &quot;npc.serin&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| dialogueHistory[].recentDialogueIds[] | string | 필수 | &quot;dialogue.serin_greeting&quot; |  |
| dialogueHistory[].lastSpokenDay | integer | 필수 | 0 |  |
| flags[].id | string | 필수 | &quot;story.intro_done&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| flags[].value | boolean/integer | 필수 | true |  |
| returnPending | boolean | 필수 | false |  |
| caseResolutionOrder[] | string | 필수 | 정의참조 | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| truthReadKeys[] | string | 필수 | 정의참조 | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| finalState.status | string/string/string/string | 필수 | &quot;locked&quot; | enum=locked/ready/running/done |
| finalState.choice | /null | 필수 | null |  |
| finalState.archiveGeneration | integer/null | 필수 | null |  |
| companion.npcId | string/null | 필수 | null |  |
| companion.usedDays[].npcId | string | 필수 | 정의참조 | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| companion.usedDays[].day | integer | 필수 | 정의참조 | minimum=0 |
| trackedActivityIds[] | string | 필수 | 정의참조 | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| discoveredRecipeIds[] | string | 필수 | &quot;recipe.alchemy_3&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| spellApplicationSelections[].spellId | string | 필수 | 정의참조 | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| spellApplicationSelections[].variantKey | string/string | 필수 | 정의참조 | enum=a/b |
| acceptedActivityIds[] | string | 필수 | &quot;activity.q01&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| invitedRelationIds[] | string | 필수 | 정의참조 | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| preAcceptedEvidenceIds[] | string | 필수 | 정의참조 | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| catalogHash | string | 필수 | &quot;ae3ed586e18205fe7e6d7ac3b29ecf8498bfdf34bffa19f51586463f5cc7aa33&quot; | pattern=^[a-f0-9]{64}$ |

## runner

| 필드 경로 | 타입 | 필수 | 예제 프로파일 | 제약 |
|---|---|---|---|---|
| schemaVersion | const | 필수 | 2 | const=2 |
| contentVersion | string | 필수 | &quot;full-design-0.2-proposal&quot; |  |
| variables[].id | string | 필수 | &quot;story.intro_done&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| variables[].type | string/string | 필수 | &quot;bool&quot; | enum=bool/int |
| variables[].initial | boolean/integer | 필수 | true |  |
| maps[].id | string | 필수 | &quot;map.courtyard&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| maps[].width | integer | 필수 | 8 | minimum=1 |
| maps[].height | integer | 필수 | 6 | minimum=1 |
| maps[].blocked[][] | integer | 필수 | 3 | minimum=0 |
| maps[].portals[].id | string | 필수 | &quot;portal.to_lounge&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| maps[].portals[].x | number | 필수 | 7.5 | minimum=0 |
| maps[].portals[].y | number | 필수 | 2.5 | minimum=0 |
| maps[].portals[].to.mapId | string | 필수 | &quot;map.lounge&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| maps[].portals[].to.x | number | 필수 | 0.5 | minimum=0 |
| maps[].portals[].to.y | number | 필수 | 2.5 | minimum=0 |
| maps[].portals[].open | boolean | 필수 | true |  |
| destinations[].id | string | 필수 | &quot;dest.courtyard_home&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| destinations[].position.mapId | string | 필수 | &quot;map.courtyard&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| destinations[].position.x | number | 필수 | 1.5 | minimum=0 |
| destinations[].position.y | number | 필수 | 1.5 | minimum=0 |
| destinations[].radius | number | 필수 | 0.2 | maximum=0.5; exclusiveMinimum=0 |
| destinations[].facing | string/string/string/string | 필수 | &quot;south&quot; | enum=north/south/east/west |
| npcs[].id | string | 필수 | &quot;npc.serin&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| npcs[].source | string | 필수 | &quot;N01&quot; |  |
| npcs[].name | string | 필수 | &quot;서린&quot; |  |
| npcs[].position.mapId | string | 필수 | &quot;map.lounge&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| npcs[].position.x | number | 필수 | 0.5 | minimum=0 |
| npcs[].position.y | number | 필수 | 3.5 | minimum=0 |
| npcs[].home | string | 필수 | &quot;dest.snack&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| npcs[].speed | number | 필수 | 3 | exclusiveMinimum=0 |
| schedules[].id | string | 필수 | &quot;schedule.serin&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| schedules[].npcId | string | 필수 | &quot;npc.serin&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| schedules[].script | string | 필수 | &quot;420 dest.snack / 720 dest.notice / 1080 home&quot; | pattern=^[^\r\n]+$ |
| events[].id | string | 필수 | &quot;event.snack&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| events[].category | string/string/string/string/string/string | 필수 | &quot;relationship&quot; | enum=resolution/relationship/festival/interaction/general/schedule |
| events[].trigger | string | 필수 | &quot;interact npc.serin \&quot;간식 모임 시작\&quot; 1.5&quot; | pattern=^(clock\|manual\|interact .+)$ |
| events[].staging | string/string | 필수 | &quot;reserved&quot; | enum=current/reserved |
| events[].pause | string/string/string | 필수 | &quot;scene&quot; | enum=dialogue/scene/none |
| events[].executionMode | string/string | 필수 | &quot;scene&quot; | enum=scene/room-activity |
| events[].repeatPolicy | string/string | 필수 | &quot;once&quot; | enum=once/practice |
| events[].completionKey | string | 선택 | 정의참조 | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| events[].resources[] | string | 필수 | &quot;npc.serin&quot; | pattern=^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ |
| events[].when | string | 필수 | &quot;eq story.intro_done true&quot; | pattern=^[^\r\n]*$ |
| events[].costMinutes | integer | 필수 | 10 | minimum=0 |
| events[].script | string | 필수 | &quot;move npc.serin dest.snack / say npc.serin \&quot;실습 노트를 같이 정리해 볼래?\&quot; / choice \&quot;서린과 무엇부터 할까?\&quot; notes \&quot;노트부터 정리하자\&quot; tea \&quot;차부터 준비하자\&quo | pattern=^[^\r\n]+$ |
| events[].forks | object | 선택 | {&quot;notes&quot;:&quot;say npc.serin \&quot;같이 하니까 마음이 놓여.\&quot;&quot;,&quot;tea&quot;:&quot;say npc.serin \&quot;좋아, 따뜻한 차가 필요했어.\&quot;&quot;} |  |
| events[].effects | string | 필수 | &quot;set memory.snack_done true&quot; | pattern=^[^\r\n]*$ |

## functions

`schemas/functions.json`에서 생성한 함수 사전. 이름이 `*`인 종류는 호출 이름 자체가 값이다(nameRule).

| 종류 | 함수 | 위치 인자 | 이름 인자 | 소유 | 비고 |
|---|---|---|---|---|---|
| command | move | actor:id→npc destination:id→destination | timeout=number(30) portals=bool(true) async=word | World/Movement | writes actor |
| command | await | handles:word… |  | Scene/Control |  |
| command | say | speaker:id→npc text:text |  | Scene/Control | 열린 핸들 금지, writes dialogue |
| command | choice | prompt:text options:fork:word label:text… |  | Scene/Control | 열린 핸들 금지, writes dialogue |
| command | branch | condition:text then:word [else:word] |  | Scene/Control | 열린 핸들 금지 |
| command | wait | seconds:number |  | Clock/Schedule |  |
| command | camera | destination:id→destination seconds:number | async=word | Scene/Control | writes camera |
| command | animate | actor:id→npc clip:word seconds:number | async=word | Scene/Control | writes actor |
| command | perform | domain:enum:class\|encounter\|puzzle\|craft\|relationship\|project\|activity definition:idOrLabel | mode=enum:normal\|practice(normal) | 도메인 서비스 | 열린 핸들 금지 |
| condition | [!]eq | variable:id→variable value:value |  | Catalog |  |
| condition | [!]gte | variable:id→variable value:int |  | Catalog |  |
| condition | [!]completed | id:id→completable |  | Relationship/Story |  |
| condition | [!]discovered | case:id→case |  | Relationship/Story |  |
| condition | [!]evidence | evidence:id→evidence |  | Relationship/Story |  |
| condition | [!]learned | spell:id→spell |  | Ability/Combat |  |
| condition | [!]caseCountGte | count:int |  | Relationship/Story |  |
| condition | [!]any | alternatives:text… |  | Catalog |  |
| effect | set | variable:id→variable value:value |  | Catalog |  |
| effect | complete | id:id→completable |  | Relationship/Story |  |
| effect | evidence.add | evidence:id→evidence |  | Relationship/Story |  |
| effect | item.grant | item:id→item quantity:int provenance:enum:normal\|grant | quality=enum:standard\|fine(standard) | Inventory/Economy |  |
| effect | experience.record | npc:id→npc activity:id→experienceSource activityType:enum:class\|project\|club\|meal\|request\|festival\|companion | participationOnly=bool(false) | Relationship/Story |  |
| effect | project.install | project:id→project |  | Life |  |
| effect | spell.learn | spell:id→spell |  | Ability/Combat |  |
| effect | currency.add | amountCrowns:int |  | Inventory/Economy |  |
| effect | case.reward | case:id→case |  | CaseService |  |
| trigger | interact | target:id→npc label:text range:number |  | Scene/Control |  |
| trigger | clock |  |  | Clock/Schedule |  |
| trigger | manual |  |  | Scene/Control |  |
| scheduleEntry | * ({"pattern":"^\\d+$","min":0,"max":1439,"ascending":true}) | destination:word→destination |  | Clock/Schedule |  |
| goal | observe | object:id⇒logicalObject state:word |  | Ability/Combat | cap.observe, observe-tag |
| goal | setState | object:id⇒logicalObject state:word | count=int(1) | Ability/Combat | cap.manual_control, set-object-state |
| goal | apply | object:id⇒logicalObject state:word |  | Ability/Combat | cap.repair, apply-capability |
| hint | observation | text:text | goal=word | Journal/Presentation |  |
| hint | property | text:text | goal=word | Journal/Presentation |  |
| hint | action | text:text | goal=word | Journal/Presentation |  |
| behavior | charge |  |  | Ability/Combat |  |
| behavior | ink_dispersal |  |  | Ability/Combat |  |
| behavior | root_line |  |  | Ability/Combat |  |
| behavior | seed_lure |  |  | Ability/Combat |  |
| behavior | overheat_armor |  |  | Ability/Combat |  |
| behavior | cooling_disable |  |  | Ability/Combat |  |
| behavior | dive |  |  | Ability/Combat |  |
| behavior | lantern_perch |  |  | Ability/Combat |  |
| behavior | shadow_immunity |  |  | Ability/Combat |  |
| behavior | illusion_lure |  |  | Ability/Combat |  |
| behavior | frontal_shield |  |  | Ability/Combat |  |
| behavior | supply_cut |  |  | Ability/Combat |  |
| behavior | three_ring_wave |  |  | Ability/Combat |  |
| behavior | matching_bells |  |  | Ability/Combat |  |
| behavior | frost_trail |  |  | Ability/Combat |  |
| behavior | warm_stone_lure |  |  | Ability/Combat |  |
| weakness | * ({"type":"word"}) | [multiplier:word=x1] | disable=number(0) | Ability/Combat |  |
| reactionPath | * ({"type":"id","refKind":"spell"}) | tags:word… |  | Ability/Combat |  |
| encounterEnemy | * ({"type":"id","refKind":"enemy"}) | [count:int=1] |  | Ability/Combat |  |
| itemQuantity | * ({"type":"id","refKind":"item"}) | quantity:int |  | Inventory/Economy |  |
| itemGrant | * ({"type":"id","refKind":"item"}) | quantity:int [provenance:enum:normal\|grant=grant] |  | Inventory/Economy |  |
| substitute | * ({"type":"word","enum":["calm","glow","adhesive","cool"]}) | quantity:int | slot=int(0) | Inventory/Economy |  |
| finalStage | stabilizeChannel |  |  | CaseService | stabilize-channel |

| 파일 | 표 | 칸 | 종류 |
|---|---|---|---|
| runner | events | trigger | trigger |
| runner | events | when | condition |
| runner | events | script | command |
| runner | events | forks/* | command |
| runner | events | effects | effect |
| runner | schedules | script | scheduleEntry |
| catalog | story/*/nodes | goals | goal |
| catalog | story/*/nodes | hints | hint |
| catalog | rooms | hints | hint |
| catalog | finalDefinition | goals | goal |
| catalog | finalDefinition | stages | finalStage |
| catalog | enemies | behavior | behavior |
| catalog | enemies | weak | weakness |
| catalog | reactions | paths | reactionPath |
| catalog | encounters | enemies | encounterEnemy |
| catalog | recipes | ingredients | itemQuantity |
| catalog | recipes | substitutes | substitute |
| catalog | shops | stock | itemQuantity |
| catalog | classes | grantOnFirstAccept | itemGrant |
| catalog | projects | grantOnAccept | itemGrant |
| catalog | projects | when | condition |
| catalog | dialogues | when | condition |
