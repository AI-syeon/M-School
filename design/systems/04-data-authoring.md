# 콘텐츠 작성과 데이터 계약

> 2026-11-02 작성 형식 변경: 한 행동은 한 칸의 함수 호출, 한 이벤트는 JSON Row 하나(스타듀밸리 방식). 문법과 결정은 [함수 문법](full-design/11-function-syntax.md)이 정한다. 이 문서는 v1 공통 기반이 쓰는 함수 범위를 적는다. 실행·임대·저장·커밋 계약은 바뀌지 않았다.

## 지원 범위

v1 스키마는 **이 명세의 최소 연결 예제를 위한 범위**다. 맵·NPC·일정·조건·이벤트·기본 안전 저장 payload를 지원한다. 상호작용은 이벤트의 `trigger` 칸이 대신하므로 별도 표가 없다. 전체 출시 콘텐츠의 재배/전투/레시피/관계 집합/퀘스트 데이터 스키마는 아직 만들지 않았다. 시스템 설계의 확장 계약과 실제 스키마 지원을 구분한다. 향후 확장은 스키마 버전 또는 함수 사전에 함수를 등록해서 추가한다.

JSON Schema Draft 2020-12 문서이며 스키마는 칸의 모양(문자열인지, 필수인지)만 본다. 호출 문법·함수 이름·인자·참조의 실제 존재·중복 ID·경로·핸들 순서는 별도 의미 검사가 한다. 번들 내부에 `$defs`를 묶어 네트워크 없이 사용한다. 제공 검증기는 이 두 스키마에서 사용하는 키워드만 해석하는 작은 문서 검사 도구다. 범용 JSON Schema 구현이나 Unity 런타임이 아니다. Unity 구현 단계에는 표준 스키마 검증기와 계약 테스트를 선정하고 같은 예제/부정 사례를 실행해야 한다.

## 이벤트는 Row 하나

```json
{"id":"event.snack","category":"relationship","trigger":"interact npc.serin \"간식 모임 시작\" 1.5",
 "staging":"reserved","pause":"scene","repeatPolicy":"once",
 "resources":["npc.serin","space.map.lounge","input.player","camera.main"],
 "when":"eq story.intro_done true","costMinutes":10,
 "script":"move npc.serin dest.snack / say npc.serin \"실습 노트를 같이 정리해 볼래?\" / choice \"서린과 무엇부터 할까?\" notes \"노트부터 정리하자\" tea \"차부터 준비하자\"",
 "forks":{"notes":"say npc.serin \"같이 하니까 마음이 놓여.\"","tea":"say npc.serin \"좋아, 따뜻한 차가 필요했어.\""},
 "effects":"set memory.snack_done true"}
```

| 칸 | 규칙 |
|---|---|
| trigger | 시작 방법 호출 1개: `interact 대상 "표시문" 거리`, `clock`, `manual`. 이전 interactions 표를 대체 |
| when | 조건 호출목록. 빈 문자열은 항상 참 |
| script | 명령 호출목록. 순서대로 실행하며 필수 |
| forks | `choice`·`branch`가 부르는 이름 있는 하위 명령목록. 같은 Row 안에서만 쓴다. 끝나면 부른 자리 다음으로 돌아온다 |
| effects | 효과 호출목록. **script가 성공으로 끝나면 한 번 원자 커밋**한다. 이전의 마지막 commit 명령을 구조로 강제한다 |
| repeatPolicy | `once`(완료 원장에 eventId 기록, 재발동 불가) 또는 `practice`(형식만 허용, 규칙은 후속 설계). 이전의 `once` 불리언 대체 |

명령 ID는 없다. 로그와 진단은 호출 위치로 부른다: 본문은 `event.snack#3`, 가지는 `event.snack:notes#1`(번호는 1부터). 다른 칸은 `event.snack@when#1`, `event.snack@effects#1`, `event.snack@trigger`, 일정은 `schedule.serin#2`로 부른다.

## 파일과 기본값

| 필드 | 규칙 |
|---|---|
| schemaVersion, contentVersion | 필수. 번들 schemaVersion은 2. 1(객체 형식)이나 모르는 버전은 오류. 저장의 saveVersion과는 별개 |
| 위치·방향·NPC 속도·목적지 radius | 필수 명시. 생략해 엔진 기본값에 기대지 않음 |
| move timeout | 생략 시 30 시뮬초; 0 이하 불가 |
| move portals | 생략 시 true |
| when | 필수 칸. 항상 허용은 빈 문자열 |
| repeatPolicy | 필수. v1 예제는 once만 쓴다 |
| pause | none / dialogue(calendar+player) / scene(calendar+ambient+player) |
| staging | current는 실제 위치부터 이동. reserved는 임대 후 숨김 합류; 아래 규칙 |
| resources | 전체 쓰기 대상 선언, 중복 불가. 이동 경유 맵도 space.map.* 포함 |
| clock.minute_of_day | 시계가 제공하는 읽기 전용 파생 int. initial=420은 시험 시작값; `set` 금지 |
| 변수 | v1 bool/int만. 선언과 initial/조건/효과 타입 일치 필수 |

reserved의 v1 준비 위치는 각 참여 NPC 정의의 position이다. 이는 **시험 번들의 고정 준비 지점**이며 home(일정 fallback)과 다르다. 여러 예약 장면이 다른 준비 지점을 요구하면 후속 스키마에 event별 stagingPositions를 명시적으로 추가해야 한다. 이 범위에서는 서린이 dest.lounge_entry와 같은 좌표로 숨김 합류한 다음 dest.snack으로 걸어간다. current에는 보정이 없다. 이 제약을 문서 밖 추론으로 확장하지 않는다.

resources 외에 Runner가 snapshot.world를 암묵적으로 잡는다. v1의 모든 이벤트는 script 성공 뒤 effects 커밋(효과가 없어도 완료 키와 시간 비용)을 한 번 하므로 전경 장면 전체가 직렬 실행된다. NPC 일정은 장면 진입 시 취소·양도 가능한 낮은 제어권이며, 실행 중 다른 장면의 제어권과 다르다. 배경 표시와 한 장면 내 async 연출은 이 직렬 제한과 양립한다.

## 호출 문법

모든 호출 칸(trigger·when·script·forks·effects·일정 script)이 같은 규칙을 쓴다. 전체 문법은 [함수 문법](full-design/11-function-syntax.md)에 있고 파서는 `tools/parse-calls.cjs` 한 곳이다.

```text
호출목록 := 호출 ( "/" 호출 )*        빈 문자열 = 호출 0개
호출     := [이름표 ":"] ["!"] 함수이름 인자*
인자     := 위치인자 | 키 "=" 값
```

- `/`는 호출을, 공백은 인자를 나눈다. 공백·`/`·`=`·`:`가 든 글은 큰따옴표로 감싸고 `\"`와 `\\`만 이스케이프한다.
- 위치 인자가 먼저, `키=값`이 뒤에 온다. 생략한 인자는 함수 사전의 기본값을 쓴다.
- `!`는 조건 함수에만 쓴다. 함수 이름은 등록된 이름만 허용한다. eval·임의 메서드 호출은 없다.
- 로더는 문자열을 기존과 같은 타입 객체로 바꾼다. 실행기는 그 객체만 다루므로 실행·임대·저장 계약은 그대로다.

## 함수 사전 (v1 범위)

v1 검증기가 등록한 함수만 아래에 있다. 전체 함수 사전(`perform`, 도메인 조건·효과 등)은 [함수 문법](full-design/11-function-syntax.md)을 따르며 v1 번들에서는 UNKNOWN_FUNCTION이다.

### 명령 (script, forks)

| 함수 | 인자 | 완료 기준 | 실패/취소·저장 |
|---|---|---|---|
| move | 행위자 목적지 `[timeout=30] [portals=true] [async=핸들]` | 기본은 실제 도착까지 대기(이전 move+await). `async`를 주면 바로 다음 호출로 가고 핸들을 남김 | 대기 중 실패는 그 자리에서 이벤트 중단, async는 await에서 전파; 취소는 이동 정지; 미저장 |
| await | 핸들… | 나열한 핸들이 모두 끝날 때까지 대기 | 하나라도 실패하면 나머지를 취소하고 중단(이전 parallel join=all); 별도 무한 대기 없음(이동 timeout 사용) |
| say | 화자 "대사" | 표시 후 사용자 확인 | 대화창 닫음·event 취소; 미저장 |
| choice | "질문" 가지 "선택문"… | 하나 선택, 고른 가지 완료 뒤 다음 호출 | 전체 취소는 진입 상태 복원. 선택지는 v1 모두 enabled; 조건 선택지는 미지원 |
| branch | "조건" 참가지 `[거짓가지]` | 조건에 맞는 가지만 실행 | 평가 오류 abort; 로컬 분기 PC 미저장 |
| wait | 초(0 이상) | 시뮬시간 경과 | 하드 메뉴 정지 제외, calendar 정지와 독립; 취소 즉시 종결 |
| camera | 목적지 초 `[async=핸들]` | 목적지 맵 표시 로드·카메라 이동 완료 | 대상 로드 실패/timeout abort, 임대 해제 시 이전 모드 복원 |
| animate | 행위자 클립 초 `[async=핸들]` | 등록 클립 wave 또는 idle 연출 완료 | 미등록/timeout abort; 임대 actor 필수 |

삭제된 이전 명령: `commit`은 `effects` 칸이, `parallel`은 `async=`+`await`가, 명령 `id`와 `slot`은 위치 번호와 기본 대기 `move`가 대신한다. 이 이름을 script에 쓰면 UNKNOWN_FUNCTION이다.

병렬 연출 예: `camera dest.stage 0.5 async=cam / animate npc.mira wave 0.5 async=wave / await cam wave`

### 조건 (when, branch의 조건)

| 함수 | 인자 | 규칙 |
|---|---|---|
| eq | 변수 값 | 변수 선언 타입과 값 타입이 같아야 한다 |
| gte | 변수 정수 | int 변수만 |
| any | "조건목록" "조건목록"… | 하나라도 참이면 참(OR) |

`/`로 이은 목록은 모두 참이어야 참(AND)이고, `!`는 `eq`·`gte`·`any` 앞에 붙여 부정한다. 이전 `all`/`not`은 `/`와 `!`로 쓴다. 예: `!eq memory.snack_done true / gte clock.minute_of_day 720`

### 효과 (effects)

| 함수 | 인자 | 규칙 |
|---|---|---|
| set | 변수 값 | 선언 타입과 일치. `clock.*`는 쓸 수 없음(READ_ONLY). 같은 변수를 한 목록에서 두 번 쓸 수 없음(EFFECT_DUPLICATE) |

의미는 [시스템 명세 5절](03-systems.md)을 따른다: 모두 먼저 검증하고 하나라도 불가하면 전체 미적용, 완료 키·시간 비용과 같은 트랜잭션. 효과는 장면 중에 영속 값을 바꾸지 않는다.

### 시작 방법 (trigger)

| 형태 | 규칙 |
|---|---|
| `interact 대상 "표시문" 거리` | 대상은 NPC ID, 거리는 타일 단위 양수. 이전 interactions 표의 targetId·label·range |
| `clock` | 조건이 열리면 Ready 초대로 등록(아래 실행 예) |
| `manual` | 호출 코드가 직접 시작 |

### 일정 (schedules[].script)

`420 dest.snack / 720 dest.notice / 1080 home`처럼 `시작분 목적지`를 `/`로 잇는다. 시작분은 0–1439이고 앞 항목보다 커야 한다(SCHEDULE_ORDER, SCHEDULE_RANGE). 끝은 다음 항목의 시작이고 마지막은 1440까지다. 첫 항목 전과 빈 구간은 home이다. `home`은 NPC의 home 목적지다. 같은 NPC의 일정은 하나만 둔다(SCHEDULE_DUPLICATE).

### 핸들과 가지 규칙

핸들 규칙은 이전과 같고 표기만 바뀌었다.

- `async` 핸들은 **같은 호출목록**(script 또는 한 가지) 안에서 `await`로 닫는다. 닫지 않은 핸들이 있으면 `say`·`choice`·`branch`와 목록 끝을 거부한다(HANDLE_UNAWAITED).
- 핸들은 가지로 넘기지 않는다. 가지 안의 `await`가 바깥 핸들을 부르면 HANDLE_UNDEFINED다. 핸들 이름은 한 목록에서 한 번만 쓴다(HANDLE_DUPLICATE).
- 열려 있는 async가 쓰는 행위자·카메라를 같은 목록의 다른 호출이 쓰면 오류다(HANDLE_CONFLICT). 대화창은 `say`·`choice`가 열린 핸들을 거부하므로 겹칠 수 없다.
- 가지 이름은 같은 Row의 `forks`에 있어야 한다(FORK_NOT_FOUND). 어디서도 부르지 않는 가지는 FORK_UNUSED, 서로 부르는 순환은 FORK_CYCLE다.
- 효과는 `effects` 칸에만 있으므로 병렬 쓰기 충돌을 런타임 임의 승자로 해결하지 않는다.

## 시험 맵

단위는 타일. `#`은 정적 충돌, `>`/`<`는 출입구. 외곽 밖도 충돌. 중앙 벽을 돌아가야 하므로 직선 보간만으로는 통과할 수 없다.

```text
map.courtyard (8×6)       map.lounge (6×5)
........                  ......
.M.#.N..                  ..#.T.
...#...>                  <.#...
...#....                  S...D.
........                  ......
........
M 미라 초기위치(1.5,1.5), N 공지목표(5.5,1.5)
S 서린 준비(0.5,3.5), D 간식목표(4.5,3.5), T 무대(4.5,1.5)
교정문(7.5,2.5) ↔ 휴게실문(0.5,2.5)
```

문은 인접 타일 자동 순간이동이 아니라 MoveService의 명시적 구간 전환이다. 맵 안에서 단순히 그 타일을 밟았다는 이유로 원치 않는 왕복 전환을 일으키지 않는다. 경로망 검사 도구는 양방향 문을 포함한 정적 8방향 도달 가능성만 확인한다. 실제 sweep·로드·밀집 동작은 Unity 구현 인수 시험이다.

## 최소 연결 실행 예

초기 absoluteMinute=420, 플레이어 actor.player는 휴게실(3.5,3.5), 서린은 준비 지점. 도입 완료 true. 테스트마다 초기 프로필을 별도로 사용한다.

1. 번들 로드·호출 파싱·타입/참조 검증. event.snack의 trigger(`interact npc.serin "간식 모임 시작" 1.5`)가 만드는 행동 조회는 거리·조건(when)·자원 상태를 반환한다. reserved는 초대 수락/시작 UI를 통해 준비하고, 일반 interact와 동일한 TryStart로 연결한다. 멀리 있는 NPC 초대는 range를 우회하는 전용 예약 진입 UI이며 임대/조건은 우회하지 않는다.
2. 플레이어가 간식 초대를 수락한다. 진입 안전 저장에는 Accepted와 기존 위치가 들어간다. 자원 임대·숨김 합류·calendar/ambient/player 정지.
3. `event.snack#1`(`move npc.serin dest.snack`)로 서린이 현 준비점→(4.5,3.5)로 이동한다. 이 호출은 실제 Arrived까지 대기한 뒤 다음으로 넘어간다. 단순 시간이 지났다는 이유로 도착을 간주하지 않는다.
4. `event.snack#2` 대사, `event.snack#3` 선택지 두 개 중 하나와 그 가지(`event.snack:notes#1` 또는 `event.snack:tea#1`)의 응답. 선택은 이번 예제에서 연출만 바꾸며 완료 효과는 동일하다. 선택 결과를 저장하는 기능을 구현했다고 주장하지 않는다.
5. script가 끝나면 effects(`set memory.snack_done true`)·완료 키 event.snack·시각 430을 한 번에 커밋한다. 임대/오버레이 해제→완료 후 저장. [save-v1.json](examples/save-v1.json)은 이 상태다.
6. 재로드하면 서린 위치·기억·완료·시각이 같고 event.snack은 ALREADY_COMPLETED로 재발동 불가. `event.snack#3`(choice) 중 저장/종료하면 **완료 저장이 아닌 진입 스냅샷**으로 돌아가 다시 시작한다.

event.stage는 별도 초기 프로필에서 미라를 교정 현재 위치부터 벽 우회→교정문→휴게실문→무대로 이동시킨다(`event.stage#1`이 도착까지 대기). 도착 후 `camera`와 `animate`를 `async=`로 함께 시작하고 `await cam wave`로 둘이 끝나길 기다린 뒤 world.stage_ready를 커밋한다. event.lunch는 시각 720 미만에서 false 원인을 표시하며 720 경계에서 후보가 된다. 조건이 열린 `clock` 이벤트는 자동 연출이 아니라 **Ready 초대 등록**으로 처리한다. 안전한 사용자 수락 후 시작하므로 이미 하던 활동을 끊지 않는다. `branch`가 간식 완료 여부로 가지(`known`/`new`)를 고른다. 시간만 다른 구조가 아니라 동일 이동·조건·대사·effects 커밋을 재사용한다.

## 오류 표시와 시험 모드

로더 순서: 문자열 파싱 → 함수 사전 대조(이름·인자 수·타입·기본값) → 참조 종류 → 핸들·가지 → 기존 의미 검사. 검증 진단 형식: `{severity,code,file,pointer,entityId,call,offset,message,expected,actual}`. `call`은 호출 위치, `offset`은 칸 문자열 안의 문자 위치(0부터)다. 예: `ARG_TYPE /events/1/script event.stage#1 offset 33: timeout은 number, 실제 abc`, `REF_NOT_FOUND /events/0/script event.snack#1 offset 15: dest.missing 없음`. 스프레드시트로 작성해도 한 칸이 한 문자열이므로 같은 진단을 셀 위치로 보여 줄 수 있다. 개발 모드에서는 오류 번들 시작을 막고 마지막 유효 카탈로그를 유지한다.

| 코드 | 뜻 |
|---|---|
| PARSE_ERROR | 닫히지 않은 따옴표, 빈 호출, 명령에 붙은 `!` 등 문법 오류 |
| UNKNOWN_FUNCTION | 등록되지 않은 함수 이름(칸 종류별 사전에서 찾는다) |
| ARG_COUNT, ARG_TYPE | 인자 수 불일치·모르는 키, 타입·범위 불일치 |
| REF_NOT_FOUND, REF_KIND | 없는 ID, 종류가 다른 ID(목적지 자리에 NPC 등) |
| LABEL_DUP, FORK_NOT_FOUND, FORK_UNUSED, FORK_CYCLE | 이름표·가지 오류 |
| HANDLE_UNAWAITED, HANDLE_UNDEFINED, HANDLE_DUPLICATE, HANDLE_CONFLICT | async 핸들 규칙 위반 |
| TYPE_MISMATCH, READ_ONLY, EFFECT_DUPLICATE, LEASE_UNDECLARED, NO_ROUTE 등 | 기존 의미 검사 |

`ExplainEligibility(event.lunch)`는 다음 이유를 함께 반환한다: 조건 `gte clock.minute_of_day 720` actual420 false; repeatPolicy once 원장 미완료; npc.serin 임대 소유자; 장소/입력 임대; 예약 상태; 플레이어 안전 상태. false 한 개만 찍고 나머지를 숨기지 않는다. 일반 플레이 수첩은 숨겨진 정답을 노출하지 않는 label만 표시한다.

실행 로그 레코드: sessionId, tick, sequence, instanceId, eventId, call, phase, handleId, ownerToken, stateRevision, code, details. `call`은 위 호출 위치이고 handleId는 실행기가 만든다(작성 데이터에 없음). 이동은 mapId/pos/target/navRevision/replans/portal, 효과는 transactionId/old/new, 저장은 generation/hash를 추가한다. [expected-trace.json](examples/expected-trace.json)은 **기대 로그 명세**이며 실제 게임 로그가 아니다. 대사 전문·사용자 이름은 기본 로그에서 제외한다.

Unity 시험 UI 요구: 번들 선택→초기 프로필/시각 선택→이벤트 수락→한 틱 진행·일시정지→경로/충돌/목표 표시→임대 소유자·조건 이유→강제 막힘/문 닫기/로드 실패/저장 실패 주입→안전 복원. 이번 작업에는 이 UI나 게임 실행기를 구현하지 않았다.
