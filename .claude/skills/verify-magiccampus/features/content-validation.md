# 콘텐츠 검증

게임은 시작할 때 이벤트 카탈로그를 컴파일한다. 잘못된 콘텐츠는 실행하지 않고 거부하며, 오류 위치(파일, JSON 포인터, 호출, 오프셋)를 진단 메시지로 남긴다. 플레이어에게는 시작 실패 문구와 진단 목록으로 보인다.

## Sub-features

- `content-base`: 기본 카탈로그(`base`)가 오류 없이 컴파일된다.
- `content-reject`: `variants\invalid\` 아래의 잘못된 변형은 모두 거부된다.
- `content-location`: 거부 진단에 `/events/` 포인터, 0 이상의 오프셋, 호출 이름이 들어간다.

## How to get to it (user POV)

- `MagicCampus.exe --variant <변형 경로>`로 실행한다. 잘못된 변형이면 화면 상단에 오류와 진단(`· ...`)이 표시된다.
- 콘텐츠 원본은 `Content\Fixtures\FirstConnection\generated\`다. 빌드 때 `Assets\StreamingAssets\FirstConnection\`으로 복사된다.

## Driving it with verify-magiccampus scripts

Preconditions:

- 콘텐츠를 바꿨다면 `scripts\build.ps1`로 다시 빌드한다. 실행 파일은 StreamingAssets에 복사된 콘텐츠를 읽는다.
- `scripts\doctor.ps1 -Build Stage5-verify`가 `fresh OK`를 보고한다.

- **컴파일과 거부.** `scripts\run-probe.ps1 -Cases validation -Run <id>`를 실행한다. `validation.base-compiles`, `validation.invalid-variants-found`, 변형마다 `validation.rejected:<이름>`과 `validation.location:<이름>`이 통과여야 한다.
- **진단 증거.** `Tests\Results\verify\<id>\validation\validation-<이름>.txt`에 상태, 코드, 진단 줄(`code= message= file= pointer= call= offset=`)이 있다.
- **화면 경로.** 잘못된 변형을 화면으로 보려면 run 폴더 안에서 직접 실행한다. `Builds\Stage5-verify\MagicCampus.exe --fresh --variant variants/invalid/<이름> --save-dir <run>\play\save --evidence-dir <run>\play -screen-fullscreen 0`. 그다음 `last-state.json`의 `phase`와 `diagnostics`를 읽는다. 이 경로는 아직 실측하지 않았다.

## Gotchas

- probe는 `Application.streamingAssetsPath`의 복사본을 읽는다. `Content\Fixtures`만 고치고 빌드하지 않으면 예전 콘텐츠를 검증하게 된다. `doctor`가 `STALE`로 알려 준다.
- 새 잘못된 변형 폴더를 추가하면 케이스 수가 늘어난다. 검사 수가 바뀐 것을 실패로 오해하지 않는다.
- 직접 실행한 화면 경로는 스스로 종료되지 않는다. 띄운 PID를 기록해 두고 직접 닫는다.
