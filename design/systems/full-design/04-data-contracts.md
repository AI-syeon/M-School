# 데이터 계약과 제작자가 추가할 범위

## 번들과 기본값

`catalog.schema.json`은 게임 도메인 정의, `runner.schema.json`은 이벤트·일정 Row와 이동 시험맵, `save.schema.json`은 전체 안전 상태 예제, `functions.json`은 모든 호출 칸의 함수 사전이다. 호출 칸의 문법은 [11-function-syntax.md](#spec-syntax)이고, 스키마는 그 칸을 한 줄 문자열로만 검사한다. 함수 이름·인자 수·타입·참조 종류·이름표·가지·핸들은 validate.cjs가 함수 사전으로 검사한다. 세 파일은 같은 contentVersion을 사용하고 실제 예제는 full-catalog.json / runner-bundle.json / safe-save.json이다. `expected-outcomes.json`은 계약상의 기대 결과이며 게임 실행 기록이 아니다.

모든 객체는 닫힌 필드 집합이다. 오타 필드·타입오류·잘못된 enum·미등록 참조를 거부한다. 단위는 거리tile, 지속simulation-second, 달력game-minute, 가격crown-integer다. 원본의 평균 활동시간은 reality-minute 예산이며 게임분 비용이 아니다. JSON Schema default는 값을 넣어주는 기능이 아니므로 필수 필드는 예제처럼 명시한다. 저장의 새 상태는 migration의 명시 기본값으로만 채운다.

필드별 타입·기본 프로파일·범위·필수 여부는 자동 생성 `08-field-dictionary.md`와 내장 스키마에 모두 싣는다. 필드가 없는 채 엔진 기본값에 기대는 방식은 허용하지 않는다. 수치 프로파일은 카탈로그 버전으로 관리하고, 인수시험 후 변경 시 명세와 스키마를 같이 바꾼다.

## 배열별 의미

| 배열 | 원본 대응·책임 | 핵심 제약 |
|---|---|---|
| spells12/classes24 | M01–M12, 주문별기초/응용 | 응용수업은기초만, 숙련3context와별개. grantOnFirstAccept는 `item.plant_1 3 grant` 호출목록 |
| reactions9 | 원본조합6+물결대상반응예3 | 6종외임의조합없음. paths는 `spell.frost wet`(발동주문+필요태그) 호출목록 |
| enemies12 | 8기본+4행동변형 | 변형은baseId+행동1, 공격토큰공유. behavior는 함수 사전의 행동 이름(`charge / ink_dispersal`), weak는 `wet x1 disable=2` |
| regions4/rooms12 | 4권역·별도조사실X01–12 | 메인방과중복금지. 조사실 hints 칸 3단계(`observation "…" / property "…" / action "…"`)·대여경로 |
| story6×nodes3 | A–F 내부1→2→3, 메인방18 | 노드 Row가 방이다. roomId·regionId·loanItemId·encounterId·goals·hints. 선행 노드=노드 순서, mapId·safePointId=권역, 현장도움 단계=goals 순서 |
| encounters7 | 큰교전6+최종1 | enemies는 `enemy.cleaner 1`, deviceGoalIds는 방 목표 이름표(`room.case_a3:device`) |
| finalDefinition | 최종 실습 | roomId·goals(`stabilize: setState object.final_core stabilized`)·stages(`flow_1: stabilizeChannel / …`) |
| npcs12/relationships36 | N01–12/R01–36 | 학생6만연애최종표현, 주민/교직원우정 |
| activities36 | H4+C8+Q24 | 최초완료만수량집계, 행사해금/회차분리 |
| projects12 | P01–12 | when=`any "completed 수업" "discovered 사건"`(OR), grantOnAccept 호출목록, 영구설치projection |
| plants12/recipes48 | 원본수량·대표결과 | ingredients=`item.plant_1 3 / item.plant_2 1`, substitutes=`calm 3 [slot=0]`. 개별종명/배합은 제안프로파일 |
| items93 | 재료·씨앗·도구·산출물시험목록 | 출처는정의아닌개별스택, normal/grant/loan |
| shops/creatures/furniture | 재고·돌봄·배치대표사례 | stock=`item.plant_1 -1 / item.tool_1 5`(−1무한·0품절), 비살상돌봄·출구보호 |
| dialogues | 대사 예제 | when 조건 문자열. 빈 칸=항상 |
| assetAddresses | 실제 에셋 주소 덮어쓰기 | 비어 있음. 바인딩40개는 assetKey 칸에서 생성, 기본 주소 PLACEHOLDER/<key> |

저작하지 않는 표: goals54·hints90·puzzles20·mainRooms18은 노드·조사실 Row의 칸으로 합쳤다. 방 목표 ID는 `room.case_a1:observe`처럼 `방ID:이름표`로 부른다(이전 goal.a1_observe). 이전 puzzle.<노드>는 `perform puzzle room.case_a1`, puzzle.a3_device는 `perform puzzle room.case_a3:device`, puzzle.a3_repair는 `room.case_a3:repair`, puzzle.final_stabilize는 `room.final_core:stabilize`다. symbols207은 정의 칸(노드 evidenceId·roomId, 권역 mapId 등)·엔티티 Row·함수 사전에서 검증기가 색인을 만들어 validation/report.json에 싣는다. behaviorRegistry16은 함수 사전의 behavior 종류가 대체한다. 대사·힌트의 실제 문안은 작성 칸 그대로 둔다.

NPC 이름·주문 용도·관계/사건/프로젝트/조사실/행사/모임/부탁의 설명은 원본 표에서 직접 연결했다. 식물 12개·레시피48개의 개별 이름과 수량은 원본에 전부 없으므로 이번 예제의 나머지는 제안이다. 대사432묶음이나 완성 방30개를 제작했다고 주장하지 않는다. 실제 콘텐츠 제작자는 같은 틀에 대사·작업목표·오브젝트태그·소품·힌트·에셋을 채우고 검증한다.

## 필수 불변식

- 스택 키=(itemId,quality,provenance,scopeId). 서로 다른 출처를 합치지 않는다. grant의 판매가는0. loan은 scope밖반출·판매·선물·납품·영구설치 불가다.
- 제작 결과 provenance 우선순위는 loan > grant > normal이다. 섞으면 결과 전체에 가장 강한 제한을 전파한다. 대여재료를 넣은 음식도 scope밖으로 나가지 못한다. 실패반환도 같은 제한이다.
- 팔 때 단가=floor(saleBasePrice×0.4×qualityFactor), grant/loan=0 또는 금지. fine의 배율은 제안1이며 경제검사는 fine/대체재까지 포함해야 한다. 구매재료만으로 가능한 모든 배합의 판매가≤총구매가+수수료가 로드 lint다.
- 대체재는 같은 slot의 고정 quantity를 쓰며 모든 슬롯을 동시에 점유할 수 없다. 일괄수량은1–20 제안이며 모든 소비와 산출을 한 번에 계산한다. 재료 부족이면 전체 거부, “되는 만큼”으로 조용히 줄이지 않는다.
- 전리품은 room overlay 안에 있다가 방클리어/안전확정 커밋 때만 가방/우편에 들어간다. 원장키는 동일한 보상 공유키다.
- 모든 필수 도구는 무료이고 주요사건의 모든 단계에서 수령 가능하다. P11은 원격수령편의이며 대여서비스해금조건이 아니다.
- 상점 material/seed quantity=-1은무한, 양수는재고, 0은품절. restock은absoluteDay별1회이며 재로드·달력해순환으로중복되지않는다. stock가격quote는구매시재검사한다.
- 배치의 preview는임시다. 회전후AABB/출입구연결/충돌을 검사해confirm시만소비. undo는같은꾸미기세션의미저장조작만역순, 종료이후회수는무료로인벤토리/우편. 핵심문을막는배치는거부한다.

## 도메인 작업 인터페이스

`perform <도메인> <정의>`는 구현된 C# 서비스에 등록된 작업 정의를 전달한다. 문자열eval·임의메서드호출은없다. 각서비스는 taskDefinitionId·ownerToken·revision → Handle<Result>를 공통으로 지킨다. class는수업정의, encounter는교전정의, puzzle은방(`room.case_a1`) 또는 방목표(`room.case_a3:device`), craft는레시피, relationship는R정의, project는P정의, activity는H/C/Q정의를요구한다. 잘못된종류참조도로드오류다.

입력UI→조건조회→작업시작재검증→임시선택/재료예약→성공결과검사→도메인커밋순서다. 결과 슬롯은 없다. perform 결과는 이벤트당 하나로 고정되어 effects 커밋이 읽으며 저장하지 않는다. 별도await없이perform자체가대기한다. 취소하면예약재료해제·UI복원·토큰정리후실패전파한다.

`currency.add 크라운`은양의정수지급이며공유completionKey원장과같은커밋에서만실행한다. 구매/판매의지갑차감은이효과를음수로우회하지않고Trade서비스의검증된견적을통해처리한다.

## 제작·검증 절차

1. 원본추적ID와동작목표를정한다. 새원리가필요하면먼저도메인계약을추가한다.
2. 정의ID·대상태그·조건·시간비용·대여경로·힌트·안전복구를입력한다.
3. 호출칸파싱→함수사전대조→참조종류→이름표·가지·핸들→수량→경제→선행의존→보상공유키→원본대응lint를실행한다.
4. Unity첫이정표에서JSON만바꾸어이동/대화/저장을실행한다.
5. 실제길찾기·패드·접근성·실패복구·재미시험후프로파일을확정한다.

검증기는이문서의스키마키워드만구현한문서용검사기다. 표준전체JSON Schema구현이라고주장하지않는다. 향후Unity도입검증기는선정된표준검증기로같은정상/부정사례를실행해야한다.
