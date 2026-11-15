> 과거 설계 검증/모델 사용 이력이다. 현 기반의 검증은 [2026-11-06 결과](../07-validation-results.md)를 따른다. 이전 스키마 검사를 현재 런타임 합격으로 사용하지 않는다.

# 실제 수행한 검증과 제한

2026-11-01 작성, 2026-11-02 함수 문법(11-function-syntax.md) 전환 후 재실행. 이 보고서는 문서·정적 데이터 검증이다. Unity 실행과 재미 검증은 수행하지 않았다.

## 재현

프로젝트 루트에서 `node design/systems/full-design/tools/validate.cjs`를 실행한다. report.json은 정상 스키마3개·호출 칸 검사·도메인 참조·수량·레시피 손익·60개 부정사례·독립 사건순서720개·원본17절·화면16개 해시·2402분 덧셈 결과와, 생성한 심볼 색인·에셋 바인딩 목록이다. HTML 검증은 build-html.cjs와 별도 interaction 보고서로 남긴다.

| 검사 | 실제 범위 | 해석 |
|---|---|---|
| 원본HTML | 본문574행·17절 실제구간열람, 내장이미지분리·캡션/UI, 표243행 | Markdown만읽은것아님 |
| 화면 | PNG16개해시일치·접촉모음2장시각확인 | AI시안이며Unity구현증거아님 |
| 데이터 | catalog/save/runner스키마+실제예제, ID참조·원본ID수량 | 코드실행기미구현 |
| 호출 칸 | 22개 칸 종류·293칸·566호출을 parse-calls로 파싱하고 functions.json으로 이름·인자수·타입·참조존재와 종류·이름표 중복·이름표 참조·가지 존재와 미사용·핸들 대기와 재사용·같은 행위자 동시 async를 검사 | 정적 검사. 실제 실행 순서는 Unity에서 확인 |
| 생성 색인 | 엔티티 Row·정의 칸·함수 사전에서 심볼731개, assetKey에서 바인딩40개(전부 PLACEHOLDER)를 만들어 report.json에 수록 | 저작 symbols/bindings 표 대체 |
| 경제 | 모든레시피 구매재료/대체슬롯의판매차익검사 | 생산시간/실제밸런스는미측정 |
| 부정사례 | 60건을각각실제변조해거부확인. 기대 코드가 실제로 나오는지 확인 | 전체가능오류를증명하지않음 |
| 순서 | A–F720순열의외부독립·내부선행·최종6완료도달 | 대사·길찾기·720회Unity플레이아님 |
| 저장예제 | 필드·참조·버전·범위정적검사, 기대복구사례 | 디스크교체·강제종료미실행 |
| HTML | 내부링크/ID/스크립트구문/내장JSON/표본문보존/전환검사 | 실제브라우저검사는별도기록 |

검사기의 JSON Schema 지원은 report.json에 나열한 키워드 부분집합이다. 표준검증기 전체를 새로 만들지 않았다. 생성프로파일의 에셋주소는 전부 PLACEHOLDER이며 실제 에셋 수신을 주장하지 않는다. 원본의 36관계사건·24부탁 등의 플레이목표는 연결했지만 모든 대사/맵을 완성한 콘텐츠팩은 아니다.

이번 작업이 끝나도 실제 Unity의 sweep·애니메이션완료·패드초점·입력반복·720순서대사·전구간플레이시간·재미·OS저장교체·마이그레이션은 05-implementation의 인수시험으로 확인해야 한다.

## 최종 실행 결과

2026-11-02 `node design/systems/full-design/tools/build-all.cjs`의 10단계(build-catalog → complete-contracts → build-schemas → build-runner → adopt-drafts → refine-trace → validate → claude-evidence → build-html → check-html)가 모두 통과했다.

스키마3개와 전체 예제는 형식·참조·수량·손익·소유상태 검사를 통과했다. 호출 칸은 정상 데이터에서 진단 0건이다. 부정사례는 이전 37건을 새 형식으로 다시 쓰고(명령 ID·commit·parallel 대신 호출 문자열 변조), 호출 진단 23건을 더해 60건 모두 거부됐다. 각 부정례는 기대 코드를 지정하고 실제로 그 코드가 나왔는지 확인한다.

| 진단 코드 | 부정례 예 | 실제 진단 위치 |
|---|---|---|
| PARSE_ERROR | 닫히지 않은 따옴표, 효과의 `!`, 이름표 없는 최종 단계 | `/events/0/script event.snack#2 offset 49` |
| UNKNOWN_FUNCTION | 등록 안 된 적 행동 teleport, 삭제된 commit 명령 | `/enemies/0/behavior enemy.ink[behavior]#2 offset 9` |
| ARG_COUNT | 목적지 없는 move, 인자가 남는 say | `/events/1/script event.stage#1 offset 0` |
| ARG_TYPE | timeout=abc, 약점 배율 형식, bool 변수에 3, 내림차순 일정 | `/events/1/script event.stage#1 offset 33` |
| REF_NOT_FOUND | 없는 재료, 없는 방 목표 이름표, 방에 없는 힌트 goal= | `/events/6/script event.a3_device#1 offset 15` |
| REF_KIND | 교전 대신 적 ID, 교전 적에 NPC, 장치 목표에 최종 단계 | `/events/5/script event.a3_combat#1 offset 18` |
| LABEL_DUP | 한 방 goals에 device 두 번 | `/story/0/nodes/0/goals node.a1[goals]#3 offset 116` |
| FORK_NOT_FOUND | choice가 부르는 tea 가지 삭제 | `/events/0/script event.snack#3 offset 104` |
| FORK_UNUSED | 부르지 않는 가지 추가 | `/events/0/forks/extra event.snack:extra#0` |
| HANDLE_UNAWAITED | await 없이 say, 목록 끝까지 열린 핸들 | `/events/1/script event.stage#4 offset 124` |
| HANDLE_CONFLICT | 같은 NPC에 async 두 개, 핸들 이름 재사용 | `/events/1/script event.stage#3 offset 63` |

FORK_UNUSED는 11-function-syntax.md의 코드 목록에 없는 추가 코드다(미사용 가지 검사용).

720개 순서에서 사건내부 선행(노드 순서)·외부독립·진실2/4·최종6도달을 검사했다. 원본343행의 명세파일과 401개 JSON Pointer가 실제로 존재함을 확인했다(이전 425개 중 hints 표 36개는 조사실 Row의 hints 칸 12개로, bindings·symbols 대체 포인터는 /assetAddresses·/story로 옮김). 저장 예제의 카탈로그SHA256과 버전도 일치한다.

HTML은 도메인별 흐름20개·연결사례5개와 기존 구조도/맵/우선권/정지/저장모형을 포함한다. 11-function-syntax.md를 개발용 명세 두 번째 절로 펼친 상태로 싣고, 내장 JSON 10개(functions.json 추가)가 원본 파일과 같음을 확인했다. 스크립트VM검사12개가 통과했다. 이는 실제 브라우저 렌더링이 아니다.

실제 좁은화면 레이아웃과 인쇄 미리보기의 외관은 미확인이다. 반응형/인쇄CSS·키보드 기본컨트롤·포커스/상태안내는 작성했고 문서 전환 로직만 검증했다. 기반 문서(../02-architecture.md 등)와 ../시스템_기획서.html은 다른 작업에서 함께 바뀌고 있어, 최종 HTML은 두 작업이 끝난 뒤 다시 생성·검사해야 한다.
