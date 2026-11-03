const fs=require('node:fs'),path=require('node:path');const root=path.resolve(__dirname,'..');
const docs=['README.md','01-source-trace.md','02-contracts.md','03-integration.md','04-data-contracts.md','05-implementation.md','06-validation.md','09-adoption.md'];
const json=['schemas/catalog.schema.json','schemas/runner.schema.json','schemas/save.schema.json','examples/full-catalog.json','examples/runner-bundle.json','examples/safe-save.json','examples/expected-outcomes.json','validation/report.json'];
let text='4회 최종 교차검토입니다. 아래는 실제 생성된 통합 본문 전체, 원본17절 대응표343행, 닫힌스키마3개, 실제 예제 전체, 실제 검증 결과입니다. 앞서 당신이 직접 작성한 도메인3원고도 최종문서에 포함하며 09-adoption의 보정과 공통계약이 우선합니다. 외부도구·파일·화면조작 없이 검토하세요. 전체요구 누락/잘못된원본해석/계약모순/JSON참조·수식·상태누락을 실제내용과비교해 지적해주세요. 단순칭찬·향후다시검토제안은말고, 심각도/정확한파일·필드/재현상황/필요수정으로 우선순위 높은 문제최대12개와 완료판정을 주세요. 개별대사/에셋/실제Unity미구현은이번시스템기획의범위밖이며, 설계계약자체가빠진것과구분하세요. 숫자는시험프로파일 제안임을고려하되 시스템원리가없는것은지적하세요. 앞선당신원고의명백한오류도보정표로충분히해결되는지확인하세요. 약5000자이내로답변해주세요.\n\n';
for(const p of docs)text+='\n=== '+p+' ===\n'+fs.readFileSync(path.join(root,p),'utf8');
for(const p of json)text+='\n=== '+p+' ===\n'+JSON.stringify(JSON.parse(fs.readFileSync(path.join(root,p),'utf8')));
fs.writeFileSync(path.join(root,'source/claude-request-04.txt'),text);console.log({characters:text.length});
