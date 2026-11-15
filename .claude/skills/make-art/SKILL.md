---
name: make-art
description: MagicCampus의 아트 리소스(32×32 바닥 타일, 여러 칸짜리 기물, 애니메이션 기물, 키 아트를 옮긴 Unity 타일맵 장면)를 Codex에 맡겨 만든다. Codex가 이미지 생성 도구로 원본을 만들고, Art/CampusGrid 레시피와 2D-pixel-asset-pipeline으로 도트화·32×32 분할해 Unity Grid에 들어갈 수 있게 한다. "타일/소품/기물/스프라이트/배경 만들어", "키 아트를 타일맵으로", "시안 여러 개" 같은 아트 제작 요청에 쓴다.
---

# MagicCampus 아트 리소스 제작

아트 리소스는 **Codex가 만들고 Claude가 지시·검수한다**(2026-11-11 사용자 결정: "아트 리소스는 위와 같은 방식으로 Codex로 만든다."). Claude가 직접 그림을 그리거나 다른 이미지 도구를 쓰지 않는다.

모든 명령은 프로젝트 루트(`C:\Users\syeon\Workspace\MagicCampus`)에서 PowerShell 5.1로 실행한다. 스크립트는 `.claude\skills\make-art\scripts\`에 있다.

## 지향점

출처: Codex 세션 "재현 가능한 타일 아트 파이프라인 구축"(2026-11-10)에서 사용자가 한 요청과 지적.

1. **키 아트를 실제 타일 에셋으로 옮긴다.** `design/screens/NN-*.png`가 기준 화면이다. Unity Grid(32×32, 1칸 = 1유닛)로 다시 그릴 수 있어야 한다.
2. **방법이 명확하고 재현 가능하다.** 원본 PNG와 생성 프롬프트를 `Art/CampusGrid/source/`에 보관한다. 자를 영역, 칸 수, 충돌, 프레임은 레시피 JSON에 적는다. `rebuild.ps1`을 다시 실행하면 결과가 바이트 단위로 같아야 한다. 일회성 손작업과 임시 스크립트로 만든 결과는 정식 자산이 아니다.
3. **기물은 여러 타일을 붙여 만든다.** 예를 들어 분수는 4×3칸이다. 프레임 전체를 먼저 도트화한 뒤 32×32로 자른다. 그래야 조각 경계가 이어진다.
4. **움직일 것은 애니메이션으로 만든다.** 예를 들어 분수의 물줄기와 잔물결이다. 움직이는 부분만 바꾸고, 모든 프레임의 캔버스 크기와 기준점을 맞춘다.
5. **장면은 키 아트와 같아 보여야 한다.** 사용자는 기물 하나만 닮은 결과에 "이 씬이 키 아트랑 똑같다고 생각해?"라고 지적했다. 건물, 정원, 동선, 빛, 깊이 같은 구도 전체를 맞춘다.
6. **빛과 그림자는 타일에 굽지 않는다.** 시간에 따라 그림자, 해의 위치, 빛의 온기가 달라져야 한다. 바닥과 기물에는 발밑의 작은 접지 그림자만 둔다. 나뭇잎 그림자, 해 방향, 색온도는 `CampusDaylight`의 레이어와 `map.json`의 `daylight`·`shadows`·`glows`가 맡는다.
7. **원본과 도트 결과를 함께 남긴다.** 둘을 나란히 비교할 수 있어야 한다.
8. **노멀맵은 기본 출력이다.** 색상 프레임과 해상도·알파·아틀라스 좌표를 맞춘다. 별도 제작 스크립트도 공용 `pixel_pipeline.normal`을 사용한다.

## 파이프라인 구조 (이미 있음)

| 위치 | 역할 |
|---|---|
| `Art/CampusGrid/README.md` | 레시피 형식, 좌표 규칙, 재생성 방법. **Codex가 반드시 읽는다.** |
| `Art/CampusGrid/art.json` + 라이브러리(`architecture.json`, `garden.json`, `students.json`) | 바닥 타일, 기물(`size`, `frames`, `fps`, `blocked`, `crop`, `colors`) |
| `Art/CampusGrid/map.json` | 안뜰 배치, 카메라, 빛·그림자 설정 |
| `Art/CampusGrid/source/` | 생성 원본 PNG와 `*-prompt.json` |
| `Art/CampusGrid/rebuild.ps1` → `build_grid.py` | 도트화, 32×32 분할, `Assets/MagicCampus/CampusGrid/`(`layout.json`, 셀 PNG, `scenic.campusatlas`) 생성, `review/` 검수 이미지 |
| Unity `MagicCampus → Art → Rebuild Campus Courtyard` | 타일, 프리팹, `Generated/CampusCourtyard.unity` 생성 |
| `C:/Users/syeon/Workspace/2D-pixel-asset-pipeline/.venv/Scripts/python.exe` | 파이프라인 Python(Pillow) |

## 작업 종류

| 종류 | 언제 | 결과 위치 | 브리프 |
|---|---|---|---|
| **시안** | 방향을 정할 때. 5~15개를 같은 조건에서 비교한다. | `ArtSamples/<이름>/` (레시피에 넣지 않음) | `briefs/draft.md` |
| **자산** | 고른 시안 또는 확정 자산을 정식 등록할 때 | `Art/CampusGrid/source/` + 레시피 + `Assets/MagicCampus/CampusGrid/` | `briefs/asset.md` |
| **장면** | 키 아트 한 장을 타일맵 장면으로 옮기거나 고칠 때 | 위와 같음 + `map.json` | `briefs/scene.md` |

모양이나 분위기가 아직 정해지지 않았으면 **시안부터** 만든다. 사용자가 고르면 고른 안의 요소는 고정하고 지적한 부분만 바꿔 **자산**으로 등록한다.

## 절차

### 1. 요청을 자산 목록으로 바꾼다

자산마다 다음을 정해 표로 적는다.

- `id`: 소문자와 하이픈만 쓴다.
- 종류: 바닥 / 기물 / 애니메이션 기물 / 장면
- 칸 수: `[가로, 세로]`. 32×32 기준이고 발밑 크기로 정한다.
- 충돌 칸: 좌하단이 `[0,0]`이다.
- 프레임 수와 FPS (애니메이션만)
- 참고 키 아트: 파일과 대략의 위치
- 들어갈 라이브러리: 기존 `garden.json` 등에 넣거나, 새 `<주제>.json`을 만든다.

칸 수, 충돌, 기존 자산 교체처럼 사용자가 정해야 하는 것이 애매하면 브리프를 쓰기 전에 묻는다. 기존 원본은 덮어쓰지 않는다. 바꿀 때는 새 이름이나 `-v2`를 붙인다.

### 2. 브리프를 쓴다

`briefs/<종류>.md`를 복사해 `{{...}}` 자리를 채운다. 파일은 `Art/_runs/` 아래가 아니라 아무 임시 위치에 둬도 된다. 실행 스크립트가 실행 폴더에 사본을 남긴다. 공통 규칙(`briefs/common.md`)은 스크립트가 자동으로 앞에 붙인다. 브리프는 영어로 쓴다. Codex의 이미지 프롬프트도 영어가 결과가 안정적이다.

### 3. Codex를 실행한다 (백그라운드)

```powershell
powershell -ExecutionPolicy Bypass -File .claude\skills\make-art\scripts\run-codex.ps1 -Brief <브리프.md> -Name <짧은-이름>
```

- Codex 설정: 모델 `gpt-6.1-sol`, 추론 `medium`(프로젝트 AGENTS.md), `workspace-write` 샌드박스, 파이프라인 폴더 추가 접근.
- 실행 폴더: `Art/_runs/<시각>-<이름>/`
  - `prompt.md`: 실제로 보낸 지시
  - `codex-log.txt`: 실행 기록
  - `last.txt`: Codex 최종 보고
  - `changed-files.txt`: 실행 중 바뀐 파일
- 시안 1~3개는 몇 분, 장면 작업은 20분 이상 걸릴 수 있다. 반드시 `run_in_background`로 실행하고 사용자에게 지금 무엇을 맡겼는지 알린다.
- 사용자에게 Codex 사용량을 알린다. `codex-log.txt` 끝의 `tokens used`를 본다.

### 4. 직접 검수한다 (Codex 보고만 믿지 않는다)

1. `last.txt`와 `changed-files.txt`를 읽는다. 허용 범위 밖의 파일이 바뀌었으면 사용자에게 알린다. 이 목록은 실행 시간 동안 바뀐 파일을 모두 모으므로, 동시에 일한 다른 세션의 수정도 섞여 있다. 그 파일 이름이 `codex-log.txt`에 나오는지 먼저 확인한다. 허용 범위는 브리프에 적은 경로, `Art/_runs/`, `Assets/MagicCampus/CampusGrid/`(자산·장면 작업)다.
2. 규격을 기계로 검사한다.
   ```powershell
   & C:/Users/syeon/Workspace/2D-pixel-asset-pipeline/.venv/Scripts/python.exe -I .claude\skills\make-art\scripts\check_png.py --kind prop <PNG 또는 폴더>...
   ```
   `--kind`는 다음 중 하나다.
   - `prop`: 가장자리 투명, 크기는 32의 배수
   - `ground`: 불투명, 32×32
   - `any`: 크기만 검사

   자산·장면 작업이면 `rebuild.ps1`이 오류 없이 끝났는지, `Art/CampusGrid/review/build-report.json`에 새 id가 있는지도 확인한다.
3. **이미지를 직접 연다.** Read 도구로 원본, 도트 결과, 비교 이미지를 본다. 다음을 확인한다.
   - 게임 크기에서 실루엣이 읽히는가
   - 키 아트와 시점·팔레트·광원(좌상단)이 같은가
   - 기존 자산 옆에 놓아도 어울리는가
   - AI 티가 나지 않는가: 흐린 그라데이션, 노이즈 같은 디더링, 픽셀 크기 섞임, 시점 혼합, 의미 없는 잔 디테일, 깨진 글자
4. 재현성을 확인한다. 자산·장면 작업이면 `rebuild.ps1`을 한 번 더 돌린다. `Assets/MagicCampus/CampusGrid/layout.json`과 `review/build-report.json`의 해시가 그대로인지 본다.

문제가 있으면 무엇이 문제인지 구체적으로 적은 수정 브리프로 Codex를 한 번 더 돌린다. 마음에 든 요소는 고정하라고 적는다.

### 5. Unity에 반영한다 (자산·장면 작업)

```powershell
powershell -ExecutionPolicy Bypass -File .claude\skills\make-art\scripts\unity-rebuild.ps1
```

- Unity를 batchmode로 띄워 `CampusGridImport.Rebuild`를 실행한다. 메뉴의 `MagicCampus → Art → Rebuild Campus Courtyard`와 같다.
- 에디터에 프로젝트가 열려 있으면 batchmode를 쓸 수 없다. 스크립트가 멈추고 알려 준다. 이때는 사용자에게 에디터에서 메뉴를 실행해 달라고 부탁한다.
- 본편 맵에 넣어 실제 게임에서 확인할 때는 `verify-magiccampus` 스킬을 쓴다.

### 6. 보고한다

한국어로 짧게 쓴다.

- 만든 것: 비교 이미지 경로를 함께 적는다.
- 직접 확인한 것: 기능·규격 통과
- 내 눈으로 본 품질 판단: 사람이 봐야 아는 것과 구분한다.
- 남은 문제
- Codex 사용량

최종 품질 판단은 사용자가 한다. 커밋은 사용자가 요청할 때만, 경로를 지정해서 한다.
