---
name: verify-magiccampus
description: MagicCampus Unity 게임(Windows 실행 파일)을 빌드하고 실제 WindowsPlayer 프로세스로 실행해 변경이 동작함을 증명할 때 쓴다. 초대 수락·이벤트 진행·선택지·취소·실패 복구·저장/강제종료 후 복원·콘텐츠 검증·대화창 UI를 내장 probe 시나리오로 구동하고 result.json·스크린샷·저장 파일을 증거로 남긴다. 게임 코드·콘텐츠·자산을 바꾼 뒤 "실제 게임에서 되는지" 확인해야 할 때 사용한다.
---

# MagicCampus 실행 검증

MagicCampus는 Unity 6000.6.3f1 Windows 게임이다. 사용자가 만지는 화면은 960×640 창 하나다. 단위 테스트(`Tests/PureDomain`)로는 Unity 화면, 입력, 실제 디스크 저장이 함께 도는지 알 수 없다. 이 스킬은 **빌드한 실행 파일**을 띄워 그것을 증명한다.

조작 수단은 게임에 내장된 probe다(`Assets/MagicCampus/Bootstrap/FirstConnection/FirstConnectionProbe.cs`). 실행 파일에 `--probe <모드>`를 주면 실제 Runtime 서비스와 Dialogue에 명령을 보내고, 가상 키보드로 Enter를 누르고, 상태와 화면을 검사한 뒤 `result.json`을 쓰고 스스로 종료한다(통과 0, 실패 1).

모든 명령은 프로젝트 루트(`C:\Users\syeon\Workspace\MagicCampus`)에서 PowerShell 5.1로 실행한다. 스크립트 경로는 `.claude\skills\verify-magiccampus\scripts\`다.

## Launch

1. **빌드.** 소스·콘텐츠·자산을 바꿨으면 먼저 빌드한다. 2026-11-03 실측은 약 30초였다. 스크립트 컴파일이 다시 필요하면 몇 분까지 걸릴 수 있으니 백그라운드로 돌린다.
   ```
   powershell -ExecutionPolicy Bypass -File .claude\skills\verify-magiccampus\scripts\build.ps1 -Build Stage5-verify
   ```
   - Unity를 `-batchmode`로 띄워 `MagicCampus.Bootstrap.Editor.FirstConnectionBuild.Build`를 실행한다. 결과는 `Builds\Stage5-verify\MagicCampus.exe`다.
   - 완료 표시는 마지막 줄 `result=Succeeded errors=0 ... exit=0`이다. 실패하면 종료 코드 1이 나오고 로그 위치가 출력된다.
   - 빌드 이름은 `Stage5` 또는 `Stage5-<영숫자>`만 허용된다.
   - 빌드는 프로젝트를 수정한다. `Content\Fixtures\FirstConnection\generated`를 `Assets\StreamingAssets\FirstConnection`에, `Art\Stage5-v1`을 `Assets\StreamingAssets\Stage5Art`에 복사한다. `Assets\MagicCampus\Scenes\FirstConnection.unity`를 새로 만들고 PlayerSettings를 덮어쓴다. 프로젝트의 정식 빌드 경로이므로 되돌리지 않는다.
   - Unity 편집기가 이 프로젝트를 열고 있으면 스크립트가 거부한다. 편집기를 대신 닫지 말고 사용자에게 묻는다.
2. **실행.** 서버처럼 계속 떠 있는 것은 없다. probe 실행 한 번이 프로세스 하나다. 실행마다 `--fresh`와 별도의 `--save-dir`, `--evidence-dir`을 쓰므로 사용자 저장 폴더(`%USERPROFILE%\AppData\LocalLow\MagicCampus\MagicCampus\Stage5`)를 건드리지 않는다. 여러 실행을 나란히 돌려도 서로 섞이지 않는다.
3. **준비 완료 판정.** probe는 `Runtime.Ready`가 될 때까지 스스로 기다린다. 일반 플레이(`play.ps1 -Action start`)는 `initial-state.json`이 생기고 `phase`가 `Ready`일 때 준비된 것이다. 이 시점에 화면 상태줄은 "친구에게 다가가 E키로 말을 걸어 보세요."다.

## Doctor

이상하면 가장 먼저 실행한다. 읽기만 한다.
```
powershell -ExecutionPolicy Bypass -File .claude\skills\verify-magiccampus\scripts\doctor.ps1 -Build Stage5-verify
```
- `exe`와 `build`가 OK여야 구동할 가치가 있다. 종료 코드 0이 사용 가능, 1이 사용 불가다.
- `fresh STALE`이 나오면 빌드 뒤에 바뀐 소스가 있다. 그 빌드로 얻은 증거는 지금 코드의 증거가 아니므로 다시 빌드한다.
- `running INFO`는 이미 떠 있는 MagicCampus.exe와 그 명령줄이다. 내가 띄우지 않은 프로세스(명령줄에 `--evidence-dir`이 없거나 다른 run 폴더)는 사용자의 플레이다. 조작하거나 종료하지 않는다.

## Drive

### probe 시나리오(기본)
```
powershell -ExecutionPolicy Bypass -File .claude\skills\verify-magiccampus\scripts\run-probe.ps1 -Cases success-notes,cancel -Build Stage5-verify -Run <run-id>
```
- 케이스: `success-notes` `success-tea` `moved` `a10` `cancel` `closed` `blocked` `map-fail` `disk-fail` `intro-false` `validation` `ui-long` `ui-200`. 케이스마다 필요한 `--variant`, `--initial`은 스크립트가 붙인다.
- 출력: 케이스마다 `pass=True exit=0 checks=N failedChecks=0` 한 줄. 실패한 검사는 `FAIL 이름: 상세`, 예외는 `ERROR`로 나온다. 하나라도 실패하면 종료 코드 1이다.
- 케이스 하나가 90초 안에 끝나지 않으면 스크립트가 띄운 그 PID만 종료하고 `TIMEOUT`을 보고한다.

### 강제 종료 후 복원
```
powershell -ExecutionPolicy Bypass -File .claude\skills\verify-magiccampus\scripts\run-restart.ps1 -Phases move,say,choice -SuccessFrom <success-notes를 돌린 run-id> -Run <run-id>
```
- `kill-<phase>` probe가 이동·대사·선택지 시점에서 `checkpoint.json`을 쓰고 멈춘다. 스크립트는 그 PID를 강제 종료한 뒤, 새 프로세스로 `reload-entry`를 돌려 장면 진입 저장으로 복원됐는지 검사한다.
- `-SuccessFrom`을 주면 그 run의 `success-notes` 저장을 복사해 `reload-success`를 돌린다. 완료된 보상(지갑 220)이 재시작 뒤에도 유지되고 이벤트를 다시 실행할 수 없는지 검사한다. 원본 증거는 건드리지 않는다.

### 일반 플레이(사람 입력)
```
powershell -ExecutionPolicy Bypass -File .claude\skills\verify-magiccampus\scripts\play.ps1 -Action start -Run <run-id>
powershell -ExecutionPolicy Bypass -File .claude\skills\verify-magiccampus\scripts\play.ps1 -Action status -Run <run-id>
powershell -ExecutionPolicy Bypass -File .claude\skills\verify-magiccampus\scripts\play.ps1 -Action stop -Run <run-id>
```
- probe 없이 실제 게임을 띄운다. `last-state.json`이 1초마다 갱신된다. 담기는 값은 맵, 플레이어 좌표, 시간, 선택된 이벤트, 상태줄 문구, 저장 안 됨 여부, 진단 메시지다.
- 키 입력을 대신 넣는 도구는 없다. 물리 키보드·마우스 경로(WASD 이동, E 상호작용, 초대 창 클릭, F5/F9)가 필요하면 사용자에게 직접 플레이를 부탁한다. 그 뒤 `status`로 상태 기록을 읽는다. 이 경로를 probe 결과로 대신 보고하지 않는다.
- `stop`은 창을 정상 종료해 마지막 상태를 쓰게 하고, 8초 안에 닫히지 않으면 강제 종료한다.

## Evidence

- 위치: `Tests\Results\verify\<run-id>\<case>\`(일반 플레이는 `...\<run-id>\play\`). 빌드 로그는 `Tests\Results\verify\build-<시각>\`.
- probe 케이스 폴더의 파일:
  - `result.json`: 모드, 통과 여부, 검사 목록과 상세, 예외, 건너뛴 검사, Unity 버전, 카탈로그 해시, 토큰 보고
  - `input.jsonl`: probe가 보낸 명령 순서와 경로(공개 Runtime 명령인지 가상 키보드인지)
  - `initial.json`, `final.json`: 시작·끝 전체 상태
  - `trace.log`: Runtime 추적
  - `*.png`: `choose`, `branch`, `final-world`, `failure` 등 실제 프레임버퍼 스크린샷
  - `save\gen-*.mcgen`: 실제 디스크 저장 세대
  - `player.log`: Unity 로그
  - `pid.txt`, `process-result.json`
- 증명 기준:
  - 동작과 그 결과 상태를 함께 남긴다. 예를 들어 선택지 스크린샷(`choose.png`)과 그 뒤 지갑·시계·원장 검사를 같이 둔다. 마지막 화면 하나로는 부족하다.
  - 부수 효과를 확인한다. 저장은 `save\`에 세대 파일이 실제로 생겼는지, 복원은 다른 PID의 새 프로세스가 같은 상태를 읽는지 본다.
  - 경로를 구분해 보고한다. probe의 수락·이벤트 시작은 **공개 Runtime 명령**(`Rt.Accept`, `Rt.StartEvent`)이고, 확인 입력은 InputSystem **가상 키보드**다. 플레이어가 NPC에게 걸어가 E를 누르는 경로는 probe가 지나지 않는다. 그 경로의 증명은 일반 플레이에서 사람이 입력한 기록뿐이다.
  - `map-fail`·`closed`·`blocked`·`disk-fail`은 Runtime의 장애 주입 스위치(`FailMapId`, 포털 닫기, 칸 막기, `FailDisk`)를 쓴다. 실제 장애가 아니라 주입이라고 적는다.
  - `ui-long`·`ui-200`은 `ui.human-review`를 건너뛴 채 끝난다. 스크린샷을 사람이 봐야 끝나는 항목이다. 글자 잘림이나 겹침이 없는지는 PNG를 열어 직접 보고 판단을 따로 적는다.
  - `result.json`이 `pass=true`이고 종료 코드가 0이고 `skipped`를 모두 설명했을 때만 "통과"라고 쓴다.

## Cleanup

- probe는 스스로 종료한다. 시간 초과나 checkpoint 대기 프로세스는 스크립트가 **자기가 띄운 PID만** 종료한다. `common.ps1`의 `Stop-OwnPlayer`는 이 프로젝트 `Builds\` 아래의 MagicCampus.exe가 아니면 거부한다.
- 일반 플레이는 `play.ps1 -Action stop -Run <run-id>`로 끝낸다.
- 프로세스 이름으로 일괄 종료(`Stop-Process -Name MagicCampus`)하지 않는다. 사용자 플레이가 같이 죽는다.
- 증거 폴더 `Tests\Results\verify\<run-id>\`와 `Builds\Stage5-verify\`는 지우지 않는다. 같은 run-id를 다시 쓰면 스크립트가 덮어쓰기를 거부하므로 새 run-id를 쓴다.

## Helpers

| 스크립트 | 하는 일 |
|---|---|
| `scripts\build.ps1 [-Build Stage5-verify] [-TimeoutMinutes 25]` | Unity 배치 빌드, `build-result.json` 판정 |
| `scripts\doctor.ps1 [-Build Stage5-verify]` | 빌드 유무·성공·최신 여부, 실행 중 프로세스(읽기 전용) |
| `scripts\run-probe.ps1 -Cases <a,b> [-Build] [-Run]` | probe 케이스를 하나씩 새 프로세스로 실행, 요약 출력 |
| `scripts\run-restart.ps1 -Phases <move,say,choice> [-SuccessFrom <run>] [-Build] [-Run]` | 강제 종료 후 새 프로세스 복원 |
| `scripts\play.ps1 -Action start/status/stop [-Build] -Run <id>` | 일반 플레이 시작·상태·종료 |
| `scripts\common.ps1` | 경로와 `Stop-OwnPlayer`(직접 실행하지 않고 다른 스크립트가 불러 씀) |

기능별 구동 절차와 함정은 [features/README.md](features/README.md)에 있다.

## 알려진 함정

- 새 빌드 폴더의 exe를 처음 실행하면 Windows 방화벽이 네트워크 권한을 물을 수 있다. 원인은 Unity Player connection의 UDP 방송이다. 게임에는 네트워크가 필요 없다. 에이전트는 이 창을 조작하지 않는다. 떠 있으면 사용자에게 취소를 부탁하고, probe는 창이 떠 있어도 계속 진행된다.
- 스크린샷은 실제 창을 찍는다. 창을 최소화하거나 `-batchmode`/`-nographics`로 실행하지 않는다.
- 빌드 직후 `doctor`가 `fresh STALE`이라고 하면, 빌드 중에 바뀐 파일이나 빌드가 다시 쓴 파일이 있는지 본다. 빌드가 만드는 씬 폴더는 판정에서 뺐다.
- 순수 도메인 테스트(`Tests\PureDomain\*.csproj`)는 이 스킬의 범위가 아니다. 실행 결과는 `design\implementation-tasks\execution\2026-11-03-stage5\baseline-tests.log` 형식을 참고한다.
