const fs=require('node:fs'),path=require('node:path');const root=path.resolve(__dirname,'..');fs.mkdirSync(path.join(root,'domains'),{recursive:true});
for(const round of ['01','02','03']){let s=fs.readFileSync(path.join(root,'source/claude-draft-'+round+'.md'),'utf8');
if(round==='01')s='# 캐릭터·수업·마법·전투·탐험 상세\n\n'+s.slice(s.indexOf('## 3. 캐릭터·입력'));
if(round==='03')s=s.slice(0,s.indexOf('## 12. 구현 이정표'))+'## 구현 계획\n\n전체판 05-implementation.md의 M0–M8과 A01–A10이 최종 단계 계약이다.\n';
s=s.replace(/lesson\./g,'class.').replace(/spell\.ripple/g,'spell.water').replace(/class\.ripple/g,'class.water').replace(/stonecraft/g,'shape').replace(/spell\.mend/g,'spell.repair').replace(/class\.mend/g,'class.repair').replace(/spell\.ward/g,'spell.barrier').replace(/class\.ward/g,'class.barrier').replace(/`granted`/g,'`grant`').replace(/`rental/g,'`loan').replace(/normal \| fine/g,'standard | fine');
s=s.replace('보너스 계절 수확물은 fine 품질로 나옵니다.','보너스 계절은 수확 수량만 늘립니다. 식물 재료에는 별도 품질을 만들지 않습니다.');
s=s.replace('원장과 완료 플래그가 일치하지 않으면 원장을 우선합니다.','원장과 완료 플래그가 일치하지 않으면 손상 저장으로 거부하고 정상 백업을 제안합니다.');
s=s.replace('공동 활동 | `activityKeys`에 새 키 추가 | +8','공동 활동 | 새 활동유형의 첫 키 추가 | +8');
s=s.replace('`activityKeysSinceR1`','`activityKeysSinceR1` (저장: activityKeys−r1BaselineActivityKeys로 계산)');
s=s.replace('같은 저장 계열 안에서는 원장 키 `final.*`로 중복 지급을 막습니다.','같은 저장 계열에서는 원장 키 `final.*`로 중복 지급을 막습니다. 보관 저장에서 다른 선택을 볼 때 보상은 다시 생성하지 않는 회상 모드를 사용합니다.');
s=s.replace('보관 저장을 로드해 다른 엔딩을 고르면 별도 저장 계열입니다. 그 계열의 원장 기준으로 판정합니다.','보관 저장은 분기점의 상태를 보존합니다. 현재 저장으로 보상을 합치거나 원장을 복사하는 기능은 없고, 다른 선택은 회상/별도 슬롯에서만 봅니다.');
s=s.replace('기본: rental(같은 scope) → granted → normal','기본: loan(같은 scope) → grant → normal');
const intro='> Claude Opus 5.5 작성 원고를 전체판에 채택한 상세 명세다. 필드 정규명·시험 기본 수치·충돌한 대안의 최종 선택은 09-adoption.md와 내장 스키마가 우선한다. 초안의 추가 제안은 [제], 미정은 [미]이며 원본 확정 사실로 바꾸지 않는다.\n\n> 데이터 작성 형식은 11-function-syntax.md(함수 호출 칸, 한 이벤트 한 Row)를 따른다. 원고의 명령 객체·goal/hint/puzzle 표 예시는 작성 당시 형식이며 같은 의미를 새 형식으로 쓴다(예: goal.a3_device → room.case_a3:device).\n\n';
fs.writeFileSync(path.join(root,'domains/domain-'+round+'.md'),intro+s);}
