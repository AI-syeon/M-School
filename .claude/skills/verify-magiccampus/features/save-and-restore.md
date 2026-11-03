# 저장과 복원

게임은 수락·장면 진입·장면 종료 때 자동 저장한다. 디스크 쓰기가 실패하면 "미저장"으로 표시되고 재시도할 수 있다. 장면 도중 게임이 강제로 꺼지면 다음 실행은 장면 진입 직전 상태로 돌아온다. 완료한 이벤트와 보상은 재시작 뒤에도 유지된다.

## Sub-features

- `save-auto`: 수락과 장면 경계마다 디스크에 저장 세대가 생긴다.
- `save-disk-fail`: 저장 실패 시 보상은 메모리에 남고 `미저장` 상태가 된다. 재시도하면 저장되고, 다시 불러와도 보상이 한 번뿐이다.
- `restore-after-kill`: 이동·대사·선택지 시점에 강제 종료해도 새 프로세스가 장면 진입 상태(수락됨, 200 크라운, 미완료)로 복원한다.
- `restore-completed`: 완료 뒤 재시작해도 상태 전체가 같고, 이벤트 재실행은 막힌다.
- `save-manual`: `F5` 저장, `F9` 불러오기, 메뉴의 `저장 (F5)`·`불러오기 (F9)`, 미저장일 때 나오는 `저장 재시도` 버튼.

## How to get to it (user POV)

- 자동 저장은 플레이 중 저절로 일어난다. 상단에 `● 저장됨` 또는 `● 미저장`이 표시된다.
- `F5`/`F9` 키, 또는 `Esc` 메뉴의 버튼을 쓴다.
- 게임 창을 강제로 닫은 뒤 `MagicCampus.exe`를 다시 실행한다. 기본 저장 폴더는 `%USERPROFILE%\AppData\LocalLow\MagicCampus\MagicCampus\Stage5`다.

## Driving it with verify-magiccampus scripts

Preconditions:

- `scripts\doctor.ps1 -Build Stage5-verify`의 종료 코드가 0이다.
- 완료 복원을 보려면 같은 빌드로 `success-notes`를 먼저 돌린 run-id가 있어야 한다.

- **디스크 실패.** `scripts\run-probe.ps1 -Cases disk-fail -Run <id>`를 실행한다. `disk-fail.memory-reward`, `disk-fail.retry-saved`, `disk-fail.reload-no-extra`가 통과여야 한다.
- **강제 종료 후 복원.** `scripts\run-restart.ps1 -Phases move,say,choice -Run <id>`. 시점마다 `kill-<phase> -> reload pass=True exit=0 checks=11`이 나와야 한다(2026-11-03 실측: say, choice). `reload-<phase>\process-result.json`의 `distinct`가 `true`여야 한다. 강제 종료한 PID와 복원한 PID가 다르다는 뜻이다.
- **완료 복원.** `scripts\run-restart.ps1 -Phases none -SuccessFrom <success-notes run-id> -Run <id>`. 출력은 `reload-success-notes pass=True exit=0 checks=52`(2026-11-03 실측)이어야 한다. 이 케이스는 원본 저장을 복사해서 쓴다.
- **증거.** `kill-<phase>\checkpoint.json`(시점, PID, 리비전), `termination.json`(종료 방법), `entry.json`(기대 상태), `reload-<phase>\reload-entry.png`.
- **F5/F9 사람 입력.** `scripts\play.ps1 -Action start -Run <id>`로 띄우고 사용자에게 이동 → `F5` → 더 이동 → `F9`를 부탁한다. `-Action status`로 좌표가 F5 시점으로 돌아왔는지와 `unsaved:false`를 확인한다.

## Gotchas

- `disk-fail`은 `Rt.FailDisk` 스위치로 디스크 실패를 주입한다. 실제 디스크 꽉 참이나 권한 오류를 재현한 것이 아니다.
- `run-restart.ps1`에서 강제 종료 시점을 건너뛰려면 `-Phases none`을 넘긴다. `-Phases`를 생략하면 세 시점을 모두 돈다.
- kill probe는 checkpoint를 쓴 뒤 일부러 멈춰 있다. 스크립트 밖에서 띄웠다면 그 PID를 직접 종료해야 한다. 이름으로 일괄 종료하지 않는다.
- `F5`/`F9`는 InputSystem의 `Keyboard.current`를 직접 읽는다. probe의 가상 키보드로는 이 경로를 증명하지 않았다.
