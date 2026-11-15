# 현재 필드 사전과 확장 절차

2026-11-06. 현 C# DTO가 직렬화 계약이다. 이전 full-catalog/functions/save-v2 스키마는 현 게임의 로더 계약이 아니다.

## 파일

런타임 입력: Assets/StreamingAssets/FirstConnection/{base,variants/...}/runner-bundle.json, staging-recipes.json, start-*.json. 빌드 입력은 Content/Fixtures/FirstConnection/generated의 동일 파일이다. GameBuild는 이를 복사한다. 기존 build-fixtures.cjs는 이전 스키마용이므로 현 DTO 대응 전 재생성 금지다.

## 콘텐츠 DTO

| 정의 | 필드와 의미 |
|---|---|
| Content | variables, maps, destinations, npcs, schedules, events, bed. recipes는 별도 파일 |
| VariableDef | id, type(bool/int), initial(JSON 값) |
| MapDef | id, width, height, blocked:[[x,y]], portals |
| PortalDef | id, x, y, to:{mapId,x,y} |
| DestinationDef | id, position:{mapId,x,y}, radius, facing |
| NpcDef | id, name, home(destination ID), position, speed. 실제 시작 배치는 일정 기준 |
| ScheduleDef | id, npcId, script |
| EventDef | id, trigger, staging, repeatPolicy, when, script, effects, costMinutes, forks:{key:script} |
| RecipeDef | eventId, join:[{actorId,joinAt:{mapId,x,y}}] |
| bed | {mapId,x,y}, 수면·기절 후 기상 위치 |

현재 배포 규칙은 repeatPolicy=once, staging=reserved이며 코드가 다른 정책을 구현한다고 해석하지 않는다. 미정의 ID는 조회 First에서 예외가 날 수 있다. 모든 NPC에 일정 하나와 유효 home이 있어야 한다. CostMinutes는 이벤트의 성공 비용이며 다른 곳에서 중복 가산하지 않는다.

## 지원 호출만 사용

| 칸 | 문법 | 의미 |
|---|---|---|
| trigger | interact npc.serin "함께 간식" 2 | NPC·표시명·거리 |
| when | eq story.intro_done true / !eq memory.snack_done true | 전부 만족해야 시작 |
| script/forks | move npc.serin dest.snack | 실제 도착 대기 |
| script/forks | say npc.serin "함께 가자." | 확인 대기 |
| script/forks | choice "무엇을 할까?" notes "정리" tea "차" | fork/문안 쌍 |
| effects | set memory.snack_done true / currency.add 20 | 성공 때만 순차 적용 |
| schedule.script | 420 home / 720 dest.notice / 1080 home | 시각별 목적지 |

따옴표 밖 공백과 /가 토큰/명령을 구분한다. 따옴표 안 /는 문자다. 인자는 문자열이며 이름 인자·백슬래시 이스케이프·async/await·perform·branch·카메라·등록형 함수 사전은 지원하지 않는다. 따옴표를 포함하는 문안은 현재 파서로 안전하게 표현되지 않으므로 별도 문안 데이터 확장 후 사용한다. 빈 when은 참이다. Fork 참조는 실제 키와 일치시키고 순환 삽입은 저작 검사에서 금지해야 한다.

## 현재 저장 필드

| JSON | 타입/기본·정책 |
|---|---|
| day | int, fixture 0, 표시할 때 +1 |
| minute | int, 기본 420, 생활일 내 1440 이상 가능 |
| player | id/mapId/x/y/facing, x/y double, facing 기본 south |
| wallet | int, fixture 200 |
| flags | ID → bool/int JToken 사전. 값이 없으면 Content 변수 initial 조회 |
| completed | eventId 문자열 목록. 현 once 중복 차단 |
| appointments | eventId → Offered 또는 Accepted |

saveVersion, generation, catalogHash, safePoint, npcPositions, clock.fraction, rewardLedger는 현 저장에 없다. Json.NET의 알 수 없는 옛 필드를 원본 트리로 보존하는 기능도 없다. 잘 파싱되는 JSON이라도 의미상 유효함을 보장하지 않는다.

## 확장 절차

후속 기능은 구체 Core 타입과 메서드 → Content 정의 → GameState 필드/기본값/로드 정규화 → Game 호출 → 사전 콘텐츠 검사 → Unity 입력·표시 순서로 추가한다. 새 문법을 사용할 Task는 파서 지원과 부정 사례를 함께 납품한다. version/migration을 넣으면 현재 무버전 저장을 기준 입력으로 한다. 기존 design 예제의 v1→v2 변환은 현 저장 migration이 아니다.
