# Opus 최종 교차 검토와 보완 결과

Opus 5.5는 4회 검토에서 “미완료(조건부)”로 판정하고 12건을 지적했다. 검토 원문은 source/claude-draft-04.md에 보존했다. 이 판정을 합격으로 바꿔 기록하지 않는다. 아래 보완을 실제 데이터·스키마·검증기에 적용했다.

> 2026-11-02 데이터 형식 전환(11-function-syntax.md) 메모: 아래 표는 당시 기록이다. 표에 나오는 mainRooms·goals·hints·puzzles·behaviorRegistry·symbols·bindings는 이제 별도 표가 아니다. 방 목표 goal.a1_observe는 room.case_a1:observe, puzzle.a3_device·puzzle.a3_repair는 perform puzzle room.case_a3:device·room.case_a3:repair, hint.a1_1은 노드 node.a1의 hints 칸 1번째 호출, behavior.charge는 함수 사전의 behavior 함수 charge다. 같은 검사는 호출 칸 기준으로 validate.cjs에 남아 있다.

| 검토 지적 | 실제 보완 | 확인 |
|---|---|---|
| 1 사건해결·진실·최종상태 소유누락 | caseDerivation=CaseService. node3+내부선행완료를같은커밋에서case해결·resolutionOrder·projection으로전개. truthScenes2/finalDefinition/ending3/onboarding6실제정의 | 원자전개·6조건·720순서에서2/4정리검사 |
| 2 메인방·교전정의누락 | mainRooms18·encounters7·goals54·puzzles20·hints90. 각방대여/거점/목표/힌트/현장도움/reset. save.defeats참조를typed검사 | 누락교전/적ID오용/힌트누락부정사례추가 |
| 3 조합과단일반응충돌 | sprout=발아준비, illusion=투사준비. 조합별paths(triggerSpellId,requiresTags), 창정책·역순관찰ID·보호태그를분리. 성형/수선태그는누적 | 콤보우회·빈선행조건·창값검사 |
| 4 주문템플릿단일 | lifeTemplate=target, combatTemplate3종, 비용combat-only-on-emission, 지속시간·가까운활용ID | 닫힌스키마와참조검사 |
| 5 적행동자유텍스트 | behaviorRegistry16·behaviorKeys·weaknessRules·damageGate·ringCount·resolutionOutcomes·공유공격토큰 | 행동키참조종류검사 |
| 6 큰교전장면충돌 | executionMode=room-activity와scene분리. 두경로는outcome.a_stopped, 별도resolution연출이node.a3·보상확정. 실제map.region_2자원. practice정책·oncefalse | 방활동입력잠금/연습보상거부검사 |
| 7 접근성배율정수 | settings.damageMultiplier/telegraphMultiplier/aimAssistStrength=number. basic/comfort/challenge/custom, 프리셋3정의 | 0.5·1.5정상값을별도스키마검사 |
| 8 fine불가능·판매배율 | score=맞는process1점+P07시설1점, threshold2이면음식/물약fine. 판매배율은1로통일 | 식물품질없어도P07에서fine가능, 손익검사 |
| 9 저장상태누락 | discoveredRecipeIds, spellApplicationSelections, mail종류/예약연결, acceptedActivityIds, invitedRelationIds, preAcceptedEvidenceIds, C02등선행모임정의 | 스키마·참조·최대추적3검사 |
| 10 지급/대여·프로젝트매핑 | classes.grantOnFirstAccept, projects.grantOnAccept/recipeIds, 모든H/C/Q loanItemId. P04차2종·P09표식·P10결계·P11부적함·P12정원장식 | 실제배합정의참조검사 |
| 11 활동집계·시간비용 | activityRecords와rewardedActivityTypes/r1BaselineActivityTypes분리, 행사/동행enum추가. 수업cost는class정의단일원천·도입완료가드 | 도입가드·완료불일치부정사례 |
| 12 요구연결부족 | spell.nearbyActionIds, schoolRule공동도구수리후속. 추적343행에명세파일/절·실제JSON Pointer425개·인수ID추가. 냉각/접착성질교정 | Pointer존재·343고유ID·원문보존검사 |

## 사건 상태를 실제로 누가 기록하는가

CaseService는 complete(node.*)의 커밋 준비 단계에 참여한다. 해당노드가3이고1·2가완료이면같은트랜잭션에resolvedCaseId를추가하고questStates.resolved=true, activeNodeId=null을설정한다. 같은revision에서caseResolutionOrder에처음한번만추가한다. resolutionFlag와worldChangeId는이집합에서파생하므로따로어긋나게저장하지않는다. 해결수2/4는아직등록하지않은truth초대를추가하며, 해결수6은finalState.ready를기록한다. 실패하면위변경전체를적용하지않는다. 재커밋은노드공유키때문에NO_OP이다.

## 방 활동과 해결 연출의 정확한 경계

event.a3_combat/기구경로는방활동컨트롤러에전달된다. snapshot.world·input.player·camera.main을장면임대로잡지않고방overlay를사용한다. 전투입력은CombatInputMap으로계속받는다. 두작업은outcome.a_stopped를짧게확정한다. 해결연출event.a3_aftermath만400순위·대화/카메라임대를받아공통수리·증거·node.a3·150크라운을같은커밋으로확정한다. 중간종료시정지된인형은유지하고미완료연출만재등록한다.

## 마지막으로 하나로 정한 규칙

- 원본의 “첫 유형 +8”은 class/project/club/meal/request/festival/companion 유형당최초1회다. activityKeys는고유콘텐츠기록, rewardedActivityTypes는점수원장, r1BaselineActivityTypes는R1완료시점기준이다. R2는R1이후새로운다른유형2개, R3는R2와장면안후속준비다. 선물은전부선택이다.
- 수업정의 durationMinutes가유일한게임분비용이다. EventRunner가수업의비용을중복가산하지않는다. perform완료시필요효과와한번만커밋하며연습모드는cost0·보상0이다. 다른domain도정의시간과event시간을더하지않고event작성검사에서하나의소유자를지정한다.
- fine점수는matchedProcess1+P07시설1, threshold=2. 실패실험은품질이아니라실패반환경로다. fine판매배율1, 효과는회복+5또는연출차이다.
- 행사정규일은catalog의cycleDay9/29/49/69(0기준)로시험한다. 원고의다른날짜제안은미채택이다. 해금후즉시연습예약이가능하므로날짜는입장조건이아니다.
- 최종친구담당기준은관계stage≥2제안을채택한다. 미달이면교직원담당이며실습진행·보상은동일하다.
- 관계R3의후속부탁은그사건9분내행동이며24개Q밖의별도콘텐츠로계상하지않는다. 도구파손후속도incidental-life에한번귀속한다.

## 품질과 반복 거래

일괄제작의각unit마다필요수량을예약하고출처최대제한을계산한다. normal/grant를분리생산할수있지만동일한unit에혼합되면그unit전체가grant다. loan이한개라도있으면그unit은같은scope의loan이고다른scope혼합은거부한다. 전체batch커밋전중단은소비0이다. 공용프로젝트의과제완료표시는scope안작업결과이며반출가능물품을생성하지않으므로대여설치금지와모순되지않는다.

이 보완 뒤의 재검토 결과와 실제 정적검증 결과는 07-claude-record.md와 validation에 기록한다. Unity 구현 완료 판정은 여전히 범위 밖이다.

## 5회 재검토의 국소 지적 5건 반영 · 최종 우선 규칙

Opus 5회 판정은 “조건부 통과(문서·정적 데이터 설계 한정)”이며, 아래 5개 국소 수정을 조건으로 제시했다. 이전 표의 관련 문장보다 이 절이 우선한다.

1. **수업 시간은 한 곳에서만 제안한다.** class.durationMinutes=60, class를 실행하는 event.costMinutes=0을 강제한다. perform은 아직 영속 시간을 쓰지 않고 결과에60분을 담는다. 마지막 commit이 해당 결과시간을 딱 한 번 가산한다. 연습은0분. 0이아닌event비용은DOUBLE_CLASS_TIME으로거부한다.
2. **R2는 과거에 못 해본 유형을 요구하지 않는다.** R1완료시 r2ActivityTypesSinceR1을비운다. 이후공동활동의실제참여발생을occurrenceSequence로기록하고그유형을집합에추가한다. 기존에해본수업·식사재참여도인정한다. 서로다른2유형이면R2초대를낸다. +8점의rewardedActivityTypes는평생첫유형점수만담고R2판정에는사용하지않는다. 따라서R1전에7유형을다했어도잠기지않는다. 연습event의experience.record(participationOnly=true)는참여만기록하고점수·고유콘텐츠키·보상은추가하지않는다.
3. **P07시설을 명시한다.** facility.shared_p07은project.p07완료로해금되고alchemy/cooking을모두허용한다. 음식/연금레시피의allowedFacilityIds에기본시설과이시설을함께넣는다. 해당시설qualityBonus=1과정확한process1점을합쳐fine을판정한다. fine음식도실제로만들수있다.
4. **등불은 기록물에도 비춘다.** spell.lantern.acceptTargetTags는dark와record를모두허용한다. lit태그를받은record에환영을적용하면R5가된다. acceptTargetTags가유일한대상집합이며이중해석을막기위해단수targetTag는제거했다.
5. **사건 보상은 정의를 참조한다.** case.reward(caseId)는CaseService가해당사건의rewardCrowns를읽어지급한다. 이벤트의숫자literal150은제거했다. node3완료와공유키를같은커밋에서요구하는lint가있다. CaseService의상태파생단계는별도지갑지급을하지않는다. 동시에공격할수있는수는min(교전.maxAttackers, 프리셋.maxAttackers, 전역3)이다.

위 수정의 정상 예제와 오류 변조 사례를 실제 검증기에 반영했다. 같은 모델의 조건부 판정을 무조건 합격으로 바꾸어 기록하지 않는다.

## 출처를 잃지 않는 나머지 변환

지급 씨앗을 심으면 화분의 provenance에 grant를 보관하고 수확물·다시 받은 씨앗에도 grant를 전파한다. 먹이의 출처는 CreatureState.lastFoodProvenance로 보관하고 해당 먹이로 발생한 수급에도 전파한다. 가구는 배치 인스턴스에 provenance를 저장해 회수해도 grant가 normal로 바뀌지 않는다. loan 화분/먹이/가구는 과제 scope overlay에만 존재하며 영속 저장의 stacks/mail/plots/placements에는 들어가지 않는다. 영속 스키마의 provenance는 normal/grant만 허용하고 scopeId=null이다. 로드 후 대여는 현장 서비스에서 재수령한다.
