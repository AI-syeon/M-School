---
name: verify-magiccampus
description: MagicCampus Unity 게임(Windows 실행 파일)을 빌드하고 실제 WindowsPlayer 프로세스로 실행해 변경이 동작함을 증명할 때 쓴다. 걸어가서 E로 초대 열기·수락·이벤트 진행·선택지·취소·조건 불충족·침대 취침·새벽 2시 기절과 저장·다시 불러오기·긴 대사창을 내장 probe 시나리오로 구동하고 result.json·스크린샷·저장 파일을 증거로 남긴다. 게임 코드·콘텐츠·자산을 바꾼 뒤 "실제 게임에서 되는지" 확인해야 할 때 사용한다.
---

# MagicCampus 실행 검증

MagicCampus는 Unity 6000.6.3f1 Windows 게임이다. 화면은 960×640 창 하나다. 규칙은 `Assets/MagicCampus/Core`(Unity 없음)에 있고 `Tests/Core`가 검사한다. 이 스킬은 그 규칙이 Unity 화면·입력·디스크와 함께 실제로 도는지를 **빌드한 실행 파일**로 증명한다.

조작 수단은 게임에 내장된 probe다(`Assets/MagicCampus/Game/Probe.cs`). 실행 파일에 `--probe <케이스>`를 주면 가상 키보드로 W·E·Enter·방향키·Esc·F9를 누르고, 초대 창의 `수락 후 시작`은 화면 버튼과 같은 공개 메서드(`GameRoot.AcceptAndStart`)로 부른다. 상태를 검사하고 스크린샷을 찍은 뒤 `result.json`을 쓰고 스스로 종료한다(통과 0, 실패 1).

모든 명령은 프로젝트 루트(`C:\Users\syeon\Workspace\MagicCampus`)에서 PowerShell 5.1로 실행한다. 스크립트 경로는 `.claude\skills\verify-magiccampus\scripts\`다.

## Launch

1. **빌드.** 소스·콘텐츠·자산을 바꿨으면 먼저 빌드한다. 스크립트 컴파일이 다시 필요하면 몇 분 걸리므로 백그라운드로 돌린다.
   ```
   powershell -ExecutionPolicy Bypass -File .claude\skills\verify-magiccampus\scripts\build.ps1 -Build Stage5-verify
   ```
   - Unity를 `-batchmode`로 띄워 `MagicCampus.Game.Editor.GameBuild.Build`를 실행한다(편집기 메뉴 `MagicCampus/Build Stage5`와 같다). 결과는 `Builds\Stage5-verify\MagicCampus.exe`다.
   - 완료 표시는 마지막 줄 `result=Succeeded errors=0 ... exit=0`이다. 실패하면 종료 코드 1과 로그 위치가 나온다.
   - 빌드는 `Content\Fixtures\FirstConnection\generated`를 `Assets\StreamingAssets\FirstConnection`에, `Art\Stage5-v1`을 `Assets\StreamingAssets\Stage5Art`에 복사하고 `Assets\MagicCampus\Scenes\FirstConnection.unity`를 새로 만든다. 정식 빌드 경로이므로 되돌리지 않는다.
   - Unity 편집기가 이 프로젝트를 열고 있으면 스크립트가 거부한다. 편집기를 대신 닫지 말고 사용자에게 묻는다.
2. **실행.** probe 실행 한 번이 프로세스 하나다. 실행마다 `--fresh`와 별도의 `--save-dir`, `--evidence-dir`을 쓰므로 사용자 저장(`%USERPROFILE%\AppData\LocalLow\MagicCampus\MagicCampus\Stage5\save.json`)을 건드리지 않는다.
3. **준비 완료 판정.** probe는 스스로 시작을 기다린다. 일반 플레이(`play.ps1 -Action start`)는 `initial-state.json`이 생기면 준비된 것이다. 이때 상태줄은 "친구에게 다가가 E키로 말을 걸어 보세요."다.

## Doctor

이상하면 가장 먼저 실행한다. 읽기만 한다.
```
powershell -ExecutionPolicy Bypass -File .claude\skills\verify-magiccampus\scripts\doctor.ps1 -Build Stage5-verify
```
- `exe`와 `build`가 OK여야 구동할 가치가 있다. 종료 코드 0이 사용 가능, 1이 사용 불가다.
- `fresh STALE`이면 빌드 뒤에 바뀐 소스가 있다. 다시 빌드한다.
- `running INFO`는 이미 떠 있는 MagicCampus.exe다. 내가 띄우지 않은 프로세스는 사용자의 플레이다. 조작하거나 종료하지 않는다.

## Drive

### probe 시나리오
```
powershell -ExecutionPolicy Bypass -File .claude\skills\verify-magiccampus\scripts\run-probe.ps1 -Cases success-notes,cancel -Build Stage5-verify -Run <run-id>
```
- 케이스: `success-notes` `success-tea` `moved` `a10` `intro-false` `cancel` `day-save` `pass-out` `ui-long` `ui-200`. 케이스별 `--variant`, `--initial`은 스크립트가 붙인다.
- 출력은 케이스마다 `pass=True exit=0 checks=N failedChecks=0` 한 줄이다. 실패한 검사는 `FAIL 이름: 상세`, 예외는 `ERROR`로 나온다. 하나라도 실패하면 종료 코드 1이다.
- 케이스 하나가 90초 안에 끝나지 않으면 스크립트가 띄운 그 PID만 종료하고 `TIMEOUT`을 보고한다.
- 기능별 검사 이름과 절차는 [features/README.md](features/README.md)에 있다.

### 일반 플레이(사람 입력)
```
powershell -ExecutionPolicy Bypass -File .claude\skills\verify-magiccampus\scripts\play.ps1 -Action start -Run <run-id>
powershell -ExecutionPolicy Bypass -File .claude\skills\verify-magiccampus\scripts\play.ps1 -Action status -Run <run-id>
powershell -ExecutionPolicy Bypass -File .claude\skills\verify-magiccampus\scripts\play.ps1 -Action stop -Run <run-id>
```
- probe 없이 게임을 띄운다. `last-state.json`이 1초마다 갱신된다(맵, 플레이어 좌표, 날·분, 소지금, 진행 중 이벤트, 상태줄, 약속, 완료 목록).
- 마우스 클릭(초대 창 버튼, 메뉴 버튼)이나 실제 키보드 경로가 필요하면 사용자에게 직접 플레이를 부탁하고 `status`로 기록을 읽는다. 이 경로를 probe 결과로 대신 보고하지 않는다.
- `stop`은 창을 정상 종료해 마지막 상태를 쓰게 하고, 8초 안에 닫히지 않으면 강제 종료한다.

## Evidence

- 위치: `Tests\Results\verify\<run-id>\<case>\`(일반 플레이는 `...\<run-id>\play\`). 빌드 로그는 `Tests\Results\verify\build-<시각>\`.
- 케이스 폴더: `result.json`(검사 목록과 상세, 예외, probe가 누른 키와 부른 메서드 `steps`), `final.json`(끝 상태, 저장 형식 그대로), `*.png` 스크린샷, `save\save.json`(day-save, pass-out), `player.log`, `pid.txt`, `process-result.json`.
- 증명 기준:
  - 동작과 결과 상태를 함께 남긴다. 선택지 스크린샷(`choose.png`)과 그 뒤 소지금·시계·완료 검사를 같이 둔다.
  - 저장은 하루를 마칠 때(침대 취침 또는 새벽 2시 기절)만 일어난다. `day-save`가 `save\save.json`을 실제로 쓰고, 그 파일로 새 `Core.Game`을 만들어 다시 읽는다. 다른 프로세스에서 다시 읽는 검사는 없다.
  - `ui-long`·`ui-200`의 글자 잘림·겹침은 사람이 PNG를 보고 따로 판단한다. `pass=True`만으로 "UI 품질 통과"라고 쓰지 않는다.
  - `result.json`이 `pass=true`이고 종료 코드가 0일 때만 "통과"라고 쓴다.

## Cleanup

- probe는 스스로 종료한다. 시간 초과 프로세스는 스크립트가 **자기가 띄운 PID만** 종료한다. `common.ps1`의 `Stop-OwnPlayer`는 이 프로젝트 `Builds\` 아래의 MagicCampus.exe가 아니면 거부한다.
- 프로세스 이름으로 일괄 종료(`Stop-Process -Name MagicCampus`)하지 않는다.
- 증거 폴더와 `Builds\Stage5-verify\`는 지우지 않는다. 같은 run-id를 다시 쓰면 스크립트가 거부하므로 새 run-id를 쓴다.

## Helpers

| 스크립트 | 하는 일 |
|---|---|
| `scripts\build.ps1 [-Build Stage5-verify] [-TimeoutMinutes 25]` | Unity 배치 빌드, `build-result.json` 판정 |
| `scripts\doctor.ps1 [-Build Stage5-verify]` | 빌드 유무·성공·최신 여부, 실행 중 프로세스(읽기 전용) |
| `scripts\run-probe.ps1 -Cases <a,b> [-Build] [-Run]` | probe 케이스를 하나씩 새 프로세스로 실행, 요약 출력 |
| `scripts\play.ps1 -Action start/status/stop [-Build] -Run <id>` | 일반 플레이 시작·상태·종료 |
| `scripts\common.ps1` | 경로와 `Stop-OwnPlayer`(다른 스크립트가 불러 씀) |

## 알려진 함정

- 새 빌드 폴더의 exe를 처음 실행하면 Windows 방화벽 창이 뜰 수 있다(Unity Player connection의 UDP 방송). 게임은 네트워크가 필요 없다. 에이전트는 이 창을 조작하지 않는다.
- 스크린샷은 실제 창을 찍는다. 창을 최소화하거나 `-batchmode`/`-nographics`로 실행하지 않는다.
- probe는 실제 키보드도 함께 읽는다. probe가 도는 동안 키보드를 누르면 결과가 흔들린다.
- 콘텐츠 규칙(명령 이름·인자 수·참조 id)은 실행 중에 검사하지 않는다. `dotnet build Tests\Core\MagicCampus.Core.Tests.csproj -p:Deterministic=false` 뒤 `dotnet run --no-build --project Tests\Core\MagicCampus.Core.Tests.csproj`가 `ALL PASS`여야 한다. 이 스킬의 범위는 아니다.
