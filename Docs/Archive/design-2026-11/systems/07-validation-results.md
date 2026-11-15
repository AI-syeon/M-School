# 현재 기반 검증과 문서 개정 결과

2026-11-06. 코드 변경 없이 현 구현을 확인하고 계획을 수정했다. 사용자 수정 FirstConnection.unity와 기존 미추적 Docs/System-Architecture.html은 변경하지 않았다.

## 실제 실행

- dotnet run --no-restore --project Tests/Core/MagicCampus.Core.Tests.csproj: **72개 PASS, ALL PASS, 종료0**.
- 검사 범위: 배포3번들·부정6종·시작 fixture/복사·상호작용/수락·양 선택·조건 false·취소·일정·맵 간 BFS·벽/포털·수면·24시 지속/02시 기절·하루 저장 왕복·bak 복구.
- [원본 검사 로그](../../Tests/Results/current-core-planning/core-tests.log).
- 최초 실행은 기본 임시 폴더 파일 이동 권한 오류로 저장 검사에서 중단됐다. 같은 검사 프로세스의 TEMP/TMP를 작업공간 안으로 지정해 재실행했고 전체 통과했다. 게임 규칙이나 시험 기대값은 변경하지 않았다.

## 이번에 바꾼 계획

Task1–5 16개 카드를 현 Core/Game 파일과 메서드로 재정의했다. 이후40개 Task 각각에 새 기반 접점을 추가했다. 공통 계약·호출 문법·필드·A01–A10/T01–T22·도메인 시간/저장 규칙을 통일했다. 이전 문서는 design/archive/2026-11-02에 보존했다. 새 Claude 검토/외부 전송/이미지 작업은 수행하지 않았다.

## 미실행과 한계

현재 코드의 Unity 재빌드·Windows 실제 플레이·A01–A10 전체 조작·사용자 체험·디스크 장애 주입은 이번에 실행하지 않았다. 따라서 Task5 전체 인수와 Task6 진입 장벽을 자동 합격 처리하지 않는다. 기존 Player 로그도 변경 전후 코드가 일치하는지 확인하지 않고 재사용하지 않는다.

현재 제한: 파서의 엄격 오류/이스케이프 부재, 효과 예외 부분 적용 가능성, StartEvent의 수락 가드 부재, SaveFile 의미 검증 부재/IO 예외 전파, 저장 실패 시 메모리 다음날 진행, 이전 fixture 생성기 미대응, 기존 Stage5Art의16px와32×32 기준 불일치. 이 항목은 새 계획의 Task5/6-6/9-1/13-2/15-7에서 추적한다.

문서의 Task 수·선행·필수 필드·현 기반 연결·로컬 링크·HTML 앵커 검사는 [문서검사](../implementation-tasks/문서검사.json)에 기록한다. 재현: node design/build-current-plans.cjs. 이 검사와 Core 통과는 Unity 전체 인수와 다르다.
