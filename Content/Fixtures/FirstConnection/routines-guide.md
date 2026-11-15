# NPC 루틴 작성 안내

NPC가 하루 동안 "몇 시에 어디로 가서 무엇을 하는지"는 번들 폴더마다 있는 `routines.json` 한 파일로 정한다. 코드를 고칠 필요가 없다.

- 파일: `generated/base/routines.json`, `generated/variants/a10-mira/routines.json`, `generated/variants/dest-moved/routines.json`. 빌드가 `Assets/StreamingAssets/FirstConnection/`으로 복사한다(Unity 빌드를 돌리지 않고 `Tests/Core`만 돌릴 때는 StreamingAssets 쪽 복사본도 같이 고친다).
- 이 파일이 없는 번들의 NPC는 하루 종일 자기 집(`home`)에 서 있다.

## 모양

NPC id를 키로, 그 NPC의 단계(step) 목록을 시간순으로 적는다.

```json
{
  "npc.serin": [
    { "at": "07:00", "place": "home",                 "action": "idle",  "note": "아침 마당" },
    { "at": "09:00", "place": "dest.library_desk",    "action": "read",  "note": "도서관에서 책 읽기" },
    { "at": "12:30", "place": "dest.cafeteria_table", "action": "eat" },
    { "at": "16:30", "place": "dest.hall_board",      "action": "chat",  "facing": "south" },
    { "at": "24:30", "place": "home" }
  ]
}
```

| 필드 | 뜻 |
|---|---|
| `at` | 이 단계가 시작되는 시각 `"HH:MM"`. 필수. |
| `place` | 갈 곳. 번들 `destinations`의 id(예: `dest.library_desk`) 또는 `"home"`(그 NPC의 `home` 장소). 필수. |
| `action` | 도착해서 하는 행동 id. 생략하면 `"idle"`(그냥 서 있기). |
| `facing` | 도착해서 보는 방향 `north`·`south`·`east`·`west`. 생략하면 그 장소의 `facing`. |
| `note` | 사람이 읽는 메모. 게임은 읽지 않는다. |

## 시간

- 24시간제 `"HH:MM"`. 하루는 07:00에 시작해 02:00에 끝난다(자정을 넘은 새벽 시간은 `"24:30"`, `"25:00"`처럼 24를 넘겨 쓴다). `"26:00"` 이상은 쓸 수 없다.
- 단계는 시각이 빠른 순서로, 같은 시각을 두 번 쓰지 않는다(`Tests/Core`가 확인한다).
- 어느 시각이든 "그 시각 이전의 가장 늦은 단계"가 지금 단계다. 첫 단계보다 이른 시각(07:00 직전)에는 목록의 마지막 단계가 지금 단계다(밤을 보낸 곳). 그래서 보통 `07:00 home`으로 시작한다.
- 걷는 시간은 단계 사이 간격 안에 들어가야 한다. 걸음은 1초에 3칸, 게임 시계는 현실 1초가 1분이다. 건물을 가로지르면 몇 분에서 십여 분이 걸린다.

## 실행 규칙

- 단계 시각이 되면 NPC가 그 장소로 걸어간다(구역을 넘는 길도 걷는다). 걷는 동안은 걷기 애니메이션이다.
- 도착하면 `facing`(없으면 장소의 방향)을 보고 `action`이 시작된다.
- 게임을 불러오거나 새 날이 시작되면 NPC는 지금 단계의 자리에 이미 서서 행동 중이다.
- 이벤트(초대 장면)에 불려 간 NPC는 행동을 멈춘다. 이벤트가 끝나면 지금 단계의 장소로 걸어가 다시 행동한다. 이벤트를 취소하면 하던 그대로 돌아온다.
- 행동은 저장되지 않는다. 시각만 저장되고, 불러오면 위 규칙으로 다시 정해진다.
- 가구를 옮겨 길이 막히면 걷는 NPC가 같은 장소로 다시 길을 찾는다.

## 쓸 수 있는 행동

행동은 그 NPC의 캐릭터 그림 `Assets/StreamingAssets/UiArt/Characters/<캐릭터>/manifest.json`의 `animations.south.<행동>`에 클립이 있어야 쓸 수 있다. 지금은 서린(`npc-serin`)과 미라(`npc-mira`) 둘 다 다음을 가진다.

| 행동 | 모습 |
|---|---|
| `idle` | 서 있기(클립이 필요 없다. 모든 NPC가 쓸 수 있다) |
| `read` | 책 읽기 |
| `eat` | 간식 먹기 |
| `write` | 수첩에 필기 |
| `chat` | 손짓하며 이야기 |

- 행동 그림은 남쪽(화면 쪽)을 보는 모습만 있다. 다른 방향을 보고 있어도 남쪽 그림으로 그려진다. 그 방향의 클립(`animations.north.read` 등)을 만들면 그쪽이 먼저 쓰인다.
- 클립이 없는 행동 이름을 쓰면 게임은 서 있는 모습으로 대신하지만(멈추지 않는다), `Tests/Core`가 실패한다. `walk`는 행동 이름으로 쓸 수 없다.

## 새 행동 클립 추가

1. 캐릭터 그림 제작 레시피 `Art/Characters/actions.json`에 행동(예: `sweep`)을 정의한다. 아트는 Codex에 맡긴다(`AGENTS.md`의 이미지 절차, `Art/Characters/build_characters.py`).
2. 빌드 스크립트가 시트와 `manifest.json`의 `animations.south.sweep`(`frames`, `fps`, `loop`, `rectangles`)을 만든다. 서린·미라가 쓸 행동이면 둘 다 만든다.
3. `routines.json`의 `action`에 `"sweep"`을 쓴다. 코드는 바꾸지 않는다. 키는 게임 안에서 `south.sweep`이 된다.

## 새 NPC나 새 장소

- 새 NPC: 번들 `npcs`에 넣고(`home` 필수) `routines.json`에 같은 id로 단계 목록을 만든다. 번들의 모든 NPC는 루틴이 있어야 한다.
- 새 장소: `world/world.json`의 `destinations`에 넣고 `tools/derive_world.py`를 다시 돌린다(README 참고).

## 확인

```
dotnet build Tests\Core\MagicCampus.Core.Tests.csproj -p:Deterministic=false
dotnet run --no-build --project Tests\Core\MagicCampus.Core.Tests.csproj
```

`content rules hold for <번들>: ...` 줄 뒤에 붙는 영어 문장이 틀린 곳을 알려 준다(알 수 없는 NPC·장소, 읽을 수 없는 시각, 순서 어긋남, 클립 없는 행동, 갈 수 없는 장소 쌍). 게임 안에서는 probe `routine`·`routine-mira`(`.claude/skills/verify-magiccampus/features/npc-routine.md`)가 걷기와 행동 그림을 증명한다.
