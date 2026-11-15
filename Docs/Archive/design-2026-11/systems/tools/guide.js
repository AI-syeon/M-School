(() => {
  'use strict';
  const data = JSON.parse(document.getElementById('guide-data').textContent);
  const $ = id => document.getElementById(id);
  let step = 0;
  let choice = 'notes';
  const defaultDialogue = '실습 노트를 같이 정리해 볼래?';
  function renderStep() {
    const current = data.steps[step];
    document.querySelectorAll('[data-step]').forEach(button => button.setAttribute('aria-pressed', String(Number(button.dataset.step) === step)));
    $('step-count').textContent = 'STEP ' + String(step + 1).padStart(2, '0') + ' / 07';
    $('step-title').textContent = current.title;
    $('step-body').textContent = current.body;
    $('step-rule').textContent = current.rule;
    $('step-call').textContent = current.call;
    $('scene-time').textContent = step === 6 ? '07:10' : '07:00';
    $('scene-memory').textContent = step === 6 ? '함께한 기억 저장됨' : '아직 없음';
    $('scene-owner').textContent = step === 0 || step === 6 ? '평상 일정' : '간식 모임 이벤트';
    $('scene-save').textContent = step === 6 ? '완료 후 안전 상태 B' : '시작 전 안전 상태 A';
    $('serin-marker').setAttribute('transform', 'translate(' + (step < 2 ? 60 : step === 2 ? 220 : 380) + ' 193)');
    $('snack-route').setAttribute('opacity', step < 2 ? '.25' : '1');
    $('scene-dialogue-text').textContent = step === 5 ? (choice === 'tea' ? '좋아, 따뜻한 차가 필요했어.' : '같이 하니까 마음이 놓여.') : current.dialogue || defaultDialogue;
    $('scene-choices').hidden = step !== 5;
    document.querySelectorAll('[data-choice]').forEach(button => button.setAttribute('aria-pressed', String(button.dataset.choice === choice)));
    $('prev-step').disabled = step === 0;
    $('next-step').disabled = step === data.steps.length - 1;
  }
  document.querySelectorAll('[data-step]').forEach(button => button.addEventListener('click', () => { step = Number(button.dataset.step); renderStep(); }));
  $('prev-step').addEventListener('click', () => { if (step > 0) step--; renderStep(); });
  $('next-step').addEventListener('click', () => { if (step < data.steps.length - 1) step++; renderStep(); });
  document.querySelectorAll('[data-choice]').forEach(button => button.addEventListener('click', () => { choice = button.dataset.choice; renderStep(); }));
  function renderRoute() {
    const selected = $('route-case').value;
    const panel = document.querySelector('.map-pair');
    panel.className = 'map-pair route-case-' + selected;
    const output = $('route-result');
    output.classList.toggle('error', selected !== 'normal');
    const entry = data.routeCases[selected];
    output.replaceChildren();
    const heading = document.createElement('strong'); heading.textContent = entry.title;
    const description = document.createElement('span'); description.textContent = entry.description;
    output.append(heading, description);
    document.querySelectorAll('.route-state-label').forEach(el => el.textContent = selected === 'closed' ? '문 닫힘' : '연결 문');
  }
  $('route-case').addEventListener('change', renderRoute);
  function renderRestore(complete) {
    $('restore-entry').setAttribute('aria-pressed', String(!complete));
    $('restore-complete').setAttribute('aria-pressed', String(complete));
    const output = $('restore-output'); output.replaceChildren();
    const grid = document.createElement('div'); grid.className = 'restore-grid';
    const values = complete ? [['복원 시각', '07:10'], ['함께한 기억', '저장됨'], ['초대 상태', '완료됨']] : [['복원 시각', '07:00'], ['함께한 기억', '아직 없음'], ['초대 상태', '수락 상태 유지']];
    for (const [label, value] of values) { const box = document.createElement('div'), small = document.createElement('small'), strong = document.createElement('strong'); small.textContent = label; strong.textContent = value; box.append(small,strong); grid.append(box); }
    const text = document.createElement('p'); text.className = 'restore-description';
    text.textContent = complete ? '안전 상태 B를 엽니다. 기억과 완료는 유지되고, 같은 이벤트의 결과나 10분 비용을 다시 적용하지 않습니다.' : '안전 상태 A를 엽니다. 서린은 장면 진입 전 위치로 돌아갑니다. 초대는 남아 있으므로 장면을 처음부터 다시 시작할 수 있습니다.';
    output.append(grid,text);
  }
  $('restore-entry').addEventListener('click', () => renderRestore(false));
  $('restore-complete').addEventListener('click', () => renderRestore(true));
  const detailNodes = () => [...document.querySelectorAll('#full-spec > details')];
  $('expand-all').addEventListener('click', () => detailNodes().forEach(el => el.open = true));
  $('collapse-all').addEventListener('click', () => detailNodes().forEach(el => el.open = false));
  function revealHash() {
    if (!location.hash) return;
    let target;
    try { target = document.getElementById(decodeURIComponent(location.hash.slice(1))); } catch { return; }
    if (!target) return;
    for (let el = target; el; el = el.parentElement) if (el.tagName === 'DETAILS') el.open = true;
    // Native anchor navigation happens before a collapsed detail is opened.
    requestAnimationFrame(() => target.scrollIntoView({ block: 'start' }));
  }
  window.addEventListener('hashchange', revealHash);
  let beforePrintState = [];
  window.addEventListener('beforeprint', () => { beforePrintState = [...document.querySelectorAll('details')].map(el => [el,el.open]); beforePrintState.forEach(([el]) => el.open = true); });
  window.addEventListener('afterprint', () => beforePrintState.forEach(([el,open]) => el.open = open));
  $('print-guide').addEventListener('click', () => window.print());
  if ('IntersectionObserver' in window) {
    const observer = new IntersectionObserver(entries => {
      const visible = entries.filter(entry => entry.isIntersecting).sort((a,b) => a.boundingClientRect.top-b.boundingClientRect.top);
      if (!visible.length) return;
      const id = visible[0].target.id;
      document.querySelectorAll('.sidebar nav a').forEach(link => { const active = link.getAttribute('href') === '#' + id; link.classList.toggle('active', active); if(active)link.setAttribute('aria-current','location');else link.removeAttribute('aria-current'); });
    }, { rootMargin: '-10% 0px -65% 0px', threshold: 0 });
    document.querySelectorAll('section.chapter').forEach(el => observer.observe(el));
  }
  renderStep(); renderRoute(); renderRestore(false); revealHash();
})();
