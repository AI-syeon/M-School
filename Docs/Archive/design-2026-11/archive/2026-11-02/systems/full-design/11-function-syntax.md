# 함수 문법: 한 행동은 한 칸, 한 이벤트는 한 Row

2026-11-02 · 사용자 지시로 데이터 작성 형식을 바꾼다. 이 문서가 01–10 문서와 기존 공통 기반(v1)의 **작성 형식**보다 우선한다. 실행·임대·저장·커밋 계약은 바꾸지 않는다.

> 사용자 지시(2026-11-02 원문): "스타듀밸리에서 이벤트는 하나의 Row로 끝난다. 하나의 행동을 한 컬럼에서 함수와 매개변수로 파싱해서 쓰기 때문이다. 다른 시스템 기획에서도 함수 파싱으로 처리할 수 있는 건 함수 파싱으로 처리하게 하자."

## 무엇이 문제였나

이전 형식은 행동 하나를 `{id, op, 필드…}` 객체 하나로 썼다. 이벤트 한 개가 명령 객체 5–9개, 이동 한 번이 `move`+`await` 두 개, 병렬 연출이 `parallel`+가지 배열이었다. 조건과 효과도 `{"op":"all","args":[…]}` 같은 중첩 객체였다. 카탈로그는 한 방에 딸린 목표·힌트·퍼즐을 별도 표로 쪼갠 뒤 ID 배열로 다시 묶었다.

| 늘어난 Row | 이전 | 원인 |
|---|---|---|
| 이벤트 명령 | 명령마다 객체 1개 + 명령 ID | 행동을 함수 호출로 쓰지 않음 |
| 이동 | move + await 2개, 핸들 이름 2번 | 기본 대기 동작이 없음 |
| 병렬 연출 | parallel + branches 배열 | 비동기 핸들을 쓰지 않음 |
| 조건·효과 | 중첩 op 객체 | 식을 문자열로 파싱하지 않음 |
| goals 54 / hints 90 / puzzles 20 | 방의 속성을 별도 표로 분리 | 방 Row의 칸 하나로 쓸 수 있는 목록 |
| mainRooms 18 | 사건 노드와 1:1인데 별도 표 | 노드 Row와 같은 대상 |
| symbols 207 | 다른 표가 이미 정의한 ID를 다시 선언 | 참조 칸의 종류로 유도 가능 |
| bindings 40 | 모두 `PLACEHOLDER/<id>` | 규칙으로 유도 가능 |
| behaviorRegistry 16 | 코드에 있어야 할 함수 목록이 콘텐츠 표에 있음 | 함수 사전의 역할 |
| interactions | 이벤트와 1:1인 시작 방법 | 이벤트의 trigger 칸으로 표현 가능 |
| schedules.rows / shop.stock / ingredients | `{a,b}` 객체 배열 | 순서 있는 값 목록 |

## 참고한 스타듀밸리 형식

- `Data/Events`: 키 하나(이벤트 ID+전제조건)에 값 하나. 값은 `/`로 나눈 명령 문자열이고 각 명령은 `이름 인자 인자…`다(`speak Abigail "…"`, `move farmer 0 3 2`).
- 게임 상태 질의(Game State Query, 1.6): 조건을 문자열로 쓰고 `!`로 부정, `ANY "식" "식"`으로 OR를 쓴다.
- `Data/Characters` 일정: `"900 Town 47 87 0/1300 …"`처럼 시작 시각과 목적지를 `/`로 잇는다. 끝 시각은 다음 항목의 시작이다.
- `Data/CraftingRecipes`: 재료를 `"388 50 334 1"`처럼 한 칸에 ID·수량 쌍으로 쓴다.

채택: 한 칸에 함수 호출 목록을 쓰고 로더가 파싱한다. 기각: 스타듀의 숫자 ID·위치 기반 의미(가독성과 오류 위치가 나쁨), 이벤트 키 안에 전제조건을 넣는 방식(조건 진단이 어려워 `when` 칸으로 분리).

## 문법

모든 함수 칸은 같은 규칙으로 파싱한다.

```text
호출목록 := 호출 ( "/" 호출 )*            빈 문자열 = 호출 0개
호출     := [이름표 ":"] ["!"] 함수이름 인자*
인자     := 위치인자 | 키 "=" 값
값       := ID | 수 | true | false | 단어 | "따옴표 문자열"
```

- `/`는 호출을 나누고, 공백은 인자를 나눈다. 앞뒤 공백은 무시한다.
- 공백·`/`·`=`·`:`가 들어가는 글은 큰따옴표로 감싼다. 따옴표 안의 `\"`와 `\\`만 이스케이프한다.
- 위치 인자가 먼저, `키=값` 인자가 뒤에 온다. 생략 가능한 인자는 함수 사전의 기본값을 쓴다.
- `이름표:`는 목록 안의 호출에 이름을 붙인다. 다른 칸은 `소유ID:이름표`로 참조한다(예: `room.case_a3:device`).
- `!`는 조건 함수에만 쓴다.
- 함수 이름은 등록된 이름만 허용한다. 문자열 eval·임의 메서드 호출·리플렉션은 없다. 로더는 문자열을 기존과 같은 타입 객체로 바꾸고, 실행기는 그 객체만 다룬다. 따라서 실행·임대·저장 계약은 그대로다.

함수 이름·인자 순서·타입·참조 종류·기본값·소유 서비스는 **함수 사전** `schemas/functions.json` 한 곳에 둔다. 스키마·검증기·문서 표는 이 사전에서 만든다. 새 함수를 추가하려면 사전·C# 구현·정상례·부정례를 함께 추가한다.

## 이벤트 = 한 Row

```json
{"id":"event.snack","category":"relationship","trigger":"interact npc.serin \"간식 모임 시작\" 1.5",
 "staging":"reserved","pause":"scene","executionMode":"scene","repeatPolicy":"once",
 "resources":["npc.serin","space.map.lounge","input.player","camera.main"],
 "when":"eq story.intro_done true","costMinutes":10,
 "script":"move npc.serin dest.snack / say npc.serin \"실습 노트를 같이 정리해 볼래?\" / choice \"서린과 무엇부터 할까?\" notes \"노트부터 정리하자\" tea \"차부터 준비하자\"",
 "forks":{"notes":"say npc.serin \"같이 하니까 마음이 놓여.\"","tea":"say npc.serin \"좋아, 따뜻한 차가 필요했어.\""},
 "effects":"set memory.snack_done true"}
```

| 칸 | 규칙 |
|---|---|
| trigger | `interact <대상> "<표시문>" <거리>` / `clock` / `manual`. 이전 interactions 표를 대체한다 |
| when | 조건 호출목록. 빈 문자열은 항상 참 |
| script | 명령 호출목록. 순서대로 실행 |
| forks | 선택·분기가 부르는 이름 있는 하위 명령목록. 같은 Row 안에만 존재. 끝나면 부른 자리 다음으로 돌아온다 |
| effects | 효과 호출목록. **script가 성공으로 끝나면 한 번 원자 커밋**한다. 이전의 마지막 commit 명령을 구조로 강제한다 |
| repeatPolicy | `once`/`practice`. 이전의 `once` 불리언은 여기서 유도하므로 삭제 |

명령 ID는 쓰지 않는다. 로그·진단·검증은 위치로 부른다: 본문 `event.snack#3`, 가지 `event.snack:notes#1`(1부터).

### 명령 함수

| 함수 | 인자 | 동작 |
|---|---|---|
| move | 행위자 목적지 `[timeout=30] [portals=true] [async=핸들]` | 기본은 도착까지 대기(이전 move+await). `async`를 주면 바로 다음으로 가고 핸들을 남긴다 |
| await | 핸들… | 나열한 핸들이 모두 끝날 때까지 대기. 하나라도 실패하면 나머지를 취소하고 이벤트 중단(이전 parallel join=all) |
| say | 화자 "대사" | 표시 후 확인 대기 |
| choice | "질문" (가지 "선택문")… | 고른 가지를 실행한 뒤 다음 명령으로 |
| branch | "조건" 참가지 `[거짓가지]` | 조건에 맞는 가지만 실행 |
| wait | 초 | 시뮬 시간 대기 |
| camera | 목적지 초 `[async=핸들]` | 카메라 이동 |
| animate | 행위자 클립 초 `[async=핸들]` | 등록 연출 |
| perform | 도메인 정의ID `[mode=practice]` | 도메인 작업을 시작하고 성공까지 대기. 결과는 effects 커밋이 읽는다 |

삭제: `commit`(effects 칸이 대체), `parallel`(async+await가 대체), 명령 `id`, `slot`/`resultSlot`(이동은 기본 대기, 결과는 이벤트당 perform 결과로 고정).

핸들 규칙은 유지한다. `async` 핸들은 같은 명령목록 안에서 `await`로 닫는다. 닫히지 않은 핸들이 있으면 say·choice·branch·perform·목록 끝을 거부한다. 핸들을 가지로 넘기지 않는다. 같은 행위자·카메라·대화창을 동시에 쓰는 async 두 개는 오류다.

병렬 연출 예: `camera dest.stage 0.5 async=cam / animate npc.mira wave 0.5 async=wave / await cam wave`

### 조건 함수

`eq 변수 값`, `gte 변수 정수`, `completed ID`, `discovered 사건`, `evidence ID`, `learned 주문`, `caseCountGte 수`, `any "조건목록" "조건목록"…`.
`/`로 이은 목록은 모두 참이어야 참(AND)이다. `!`는 부정, `any`는 OR다. 이전 `all`/`not`은 `/`와 `!`로 표현한다.

예: `completed node.a2 / !completed outcome.a_stopped`, `any "completed class.water_basic" "discovered case.b"`

### 효과 함수

`set 변수 값`, `complete ID`, `evidence.add ID`, `item.grant 아이템 수량 출처 [quality=standard]`, `experience.record NPC 활동 유형 [participationOnly=false]`, `project.install 프로젝트`, `spell.learn 주문`, `currency.add 크라운`, `case.reward 사건`. 의미·재검증·실패 규칙은 02-contracts.md의 효과 표를 따른다.

## 카탈로그에 적용한 것

| 대상 | 바꾼 형식 | 예 |
|---|---|---|
| 사건 노드 + mainRooms + 기본 퍼즐 | 방 정보를 노드 Row의 칸으로 합침. mapId·safePointId는 권역에서 유도, staffHelpSteps는 goals 순서, previousNodeId는 노드 순서에서 유도 | `"goals":"observe: observe object.a1_target_0 observed / device: setState object.a1_target_1 supply_stopped count=2 / repair: apply object.a1_target_2 repaired"` |
| goals 54 | 방의 `goals` 칸 호출. 대여 도구는 방의 것을 씀 | 참조는 `room.case_a1:device` |
| hints 90 | 방·조사실의 `hints` 칸 호출. 단계는 순서 | `"hints":"observation \"…\" goal=observe / property \"…\" goal=device / action \"…\" goal=repair"` |
| puzzles 20 | 방 목표를 실행하는 perform으로 대체 | `perform puzzle room.case_a3:device` |
| symbols 207 | 저작 표 삭제. 정의 칸(예: node.evidenceId, region.mapId, class 규칙 ID)이 선언하고 검증기가 색인을 생성 | 생성 결과는 검증 보고서에 수록 |
| bindings 40 | 저작 표 삭제. assetKey 칸이 바인딩을 선언, 주소 기본값 `PLACEHOLDER/<key>`, 실제 주소만 `assetAddresses` 덮어쓰기 | |
| behaviorRegistry 16 | 함수 사전의 `behavior` 함수로 이동 | `"behavior":"charge / ink_dispersal"` |
| weaknessRules | 호출목록 | `"weak":"wet x1 disable=2"` |
| reactions.paths | 호출목록 | `"paths":"spell.frost wet / spell.echo wind_path"` |
| encounters.enemies, ingredients, substitutes, grant 목록, shop.stock | ID·수량 호출목록 | `"ingredients":"item.plant_1 3 / item.plant_2 1"`, `"grantOnFirstAccept":"item.plant_1 3 grant"` |
| 조건 객체(projects.unlock, dialogues.condition 등) | 조건 문자열. dialogues.requiresCompletedId는 조건과 중복이라 삭제 | `"when":"completed project.p08"` |
| 일정 rows | 시작분·목적지 목록. 끝은 다음 항목의 시작, `home`은 기본 위치 | `"script":"420 dest.snack / 720 dest.notice / 1080 home"` |

## 바꾸지 않은 것과 이유

주문·수업·적·NPC·관계 사건·활동·프로젝트·식물·아이템·레시피·가구·시설·엔딩·대사·시나리오는 Row를 유지한다. 각 Row가 따로 만들고 따로 참조하는 콘텐츠 단위다. 스타듀도 이벤트·아이템·NPC마다 Row 하나를 쓴다. ID만 담은 배열(`loanItemIds`, `prerequisiteActivityIds`)은 이미 한 칸이므로 그대로 둔다. 표를 합치면서 생긴 중복 칸만 지웠고, 모든 Row가 같은 값을 가진 상수 칸 정리는 이번 범위에 넣지 않았다. 맵 충돌 격자와 문은 시험용 맵 데이터이며 실제로는 Unity 타일맵에서 온다.

## 검증과 진단

로더 순서: 문자열 파싱 → 함수 사전 대조(이름·인자 수·타입·기본값) → 참조 종류 → 핸들·가지 → 기존 의미 검사. 진단 형식에 호출 위치를 더한다.

```text
{severity, code, file, pointer, entityId, call, offset, message, expected, actual}
예: ARG_TYPE /events/1/script event.stage#2 offset 31 — timeout은 양수, 실제 "abc"
```

코드: `PARSE_ERROR`(닫히지 않은 따옴표 등), `UNKNOWN_FUNCTION`, `ARG_COUNT`(모르는 이름 인자 포함), `ARG_TYPE`, `REF_NOT_FOUND`, `REF_KIND`, `LABEL_DUP`, `FORK_NOT_FOUND`, `FORK_UNUSED`(아무도 부르지 않는 가지), `FORK_CYCLE`(가지끼리 서로 부름), `HANDLE_UNAWAITED`, `HANDLE_UNDEFINED`(열리지 않은 핸들을 await), `HANDLE_DUPLICATE`(한 목록에서 핸들 이름 재사용), `HANDLE_CONFLICT`.

## 구현하며 정한 세부 형식 (2026-11-02)

| 칸 | 형식 |
|---|---|
| 일정 | 시작분 0–1439, 오름차순 |
| 대체 재료 | `성질 수량 [slot=N]` 예: `calm 3` |
| 상점 재고 | `아이템 수량` (−1 무한, 0 품절) |
| 지급 목록 | `아이템 수량 [출처]`, 출처 기본값 grant |
| any | 대안 2개 이상 |
| perform puzzle | 방 ID(방 전체) 또는 `방:이름표`(목표 하나) |
| 최종 실습 | finalDefinition에 roomId·loanItemId·goals 칸, stages는 이름표 호출목록. 이전 puzzle.final_stabilize는 `room.final_core:stabilize` |
| 방 목표 이름표 | 소유자는 roomId. hints의 `goal=`은 같은 방 goals 이름표 |

이벤트 Row는 `executionMode`와 `completionKey`(공유 보상키) 칸을 유지한다. 이름 인자 값에는 따옴표 문자열을 쓰지 않는다. domains/*.md는 Opus 원고 보존본이라 옛 형식 예시가 남아 있으며, 형식은 이 문서가 우선한다. 스프레드시트에서 작성해도 한 칸이 한 문자열이므로 같은 진단을 셀 위치로 보여 줄 수 있다.
