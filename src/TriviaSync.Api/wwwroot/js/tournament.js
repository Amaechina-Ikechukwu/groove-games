// Tournament page: management for its host and admins, play for members, public standings for everyone.
(function () {
  'use strict';

  const $ = id => document.getElementById(id);
  const tournamentId = new URLSearchParams(location.search).get('id') || '';

  let detail = null;      // GET /api/tournaments/{id} (members and managers only)
  let info = null;        // public tournament info from the standings endpoint
  let canManage = false;
  let isMember = false;
  let activeTab = null;

  // ---------------------------------------------------------------------------
  // Loading
  // ---------------------------------------------------------------------------
  async function load() {
    UI.renderAccount($('account'));
    detail = null;
    if (Session.get()) {
      try {
        detail = await UI.api(`/api/tournaments/${encodeURIComponent(tournamentId)}`);
      } catch (err) {
        if (err.status === 404) return showNotFound();
        // 403: signed in but not a member. Fall back to the public view.
      }
    }

    let standings;
    try {
      standings = await UI.api(`/api/tournaments/${encodeURIComponent(tournamentId)}/standings`);
    } catch (err) {
      return showNotFound();
    }

    info = standings.tournament;
    canManage = !!(detail && detail.tournament.canManage);
    isMember = !!(detail && detail.isMember);
    renderHeader();
    renderStandings(standings.standings);
    if (detail) renderSessions();
    if (canManage) loadMembers();

    $('loading').hidden = true;
    $('content').hidden = false;
    if (!activeTab) selectTab(detail ? 'panelSessions' : 'panelStandings');
  }

  function showNotFound() {
    $('loading').hidden = true;
    $('content').hidden = true;
    $('notFound').hidden = false;
  }

  async function refreshSessions() {
    try {
      detail = await UI.api(`/api/tournaments/${encodeURIComponent(tournamentId)}`);
      renderSessions();
    } catch (_) { /* keep what's on screen */ }
  }

  async function refreshStandings() {
    try {
      const s = await UI.api(`/api/tournaments/${encodeURIComponent(tournamentId)}/standings`);
      renderStandings(s.standings);
    } catch (_) { /* keep what's on screen */ }
  }

  // ---------------------------------------------------------------------------
  // Header
  // ---------------------------------------------------------------------------
  function renderHeader() {
    document.title = `${info.name} · Groove`;
    $('tHost').innerHTML = `Tournament · Hosted by ${UI.escape(info.hostName)}${info.createdAt ? ` · Created ${UI.stamp(info.createdAt)}` : ''}`;
    $('tName').textContent = info.name;
    $('tDescription').textContent = info.description || '';
    $('tDescription').hidden = !info.description;

    $('codeCard').hidden = !canManage;
    if (canManage) $('joinCode').textContent = detail.joinCode;

    document.querySelector('[data-tab="panelSessions"]').hidden = !detail;
    document.querySelector('[data-tab="panelMembers"]').hidden = !canManage;
    $('sessionsHead').hidden = !canManage;

    const actions = $('tActions');
    actions.innerHTML = '';
    if (canManage) {
      actions.innerHTML = `
        <button type="button" class="btn btn-secondary btn-sm" data-act="edit">${UI.icon('edit')}Edit details</button>
        <button type="button" class="btn btn-danger-ghost btn-sm" data-act="delete">Delete tournament</button>`;
    } else if (isMember) {
      actions.innerHTML = `<span class="badge badge-outline">You're in this tournament</span>
        <button type="button" class="btn btn-ghost btn-sm" data-act="leave">Leave</button>`;
    }
    actions.querySelectorAll('[data-act]').forEach(b => {
      b.onclick = { edit: editDetails, delete: deleteTournament, leave: leaveTournament }[b.dataset.act];
    });

    const note = $('publicNote');
    if (!detail) {
      note.hidden = false;
      note.innerHTML = Session.get()
        ? `${UI.icon('info')}<div>You're viewing the public standings. Have a code from the host? <a href="/tournaments.html">Join the tournament</a> to play its sessions.</div>`
        : `${UI.icon('info')}<div>You're viewing the public standings. <a href="#" data-signin>Sign in</a> to play if you're in this tournament.</div>`;
      const link = note.querySelector('[data-signin]');
      if (link) link.onclick = e => { e.preventDefault(); UI.signIn(); };
    } else {
      note.hidden = true;
    }
  }

  // ---------------------------------------------------------------------------
  // Sessions
  // ---------------------------------------------------------------------------
  function statusBadge(s) {
    switch (s.status) {
      case 'Live': return '<span class="badge badge-live">Live now</span>';
      case 'Open': return `<span class="badge badge-live">Open · closes ${UI.escape(UI.relativeTime(s.closesAt))}</span>`;
      case 'Closed': return '<span class="badge">Closed</span>';
      default: return '<span class="badge">Not started</span>';
    }
  }

  function memberActions(s) {
    const a = s.myAttempt;
    if (s.mode === 'Live') {
      if (s.status === 'Live') return `<a class="btn btn-primary" href="/player.html?pin=${encodeURIComponent(s.livePin)}">Join live game</a>`;
      if (a) return `<span class="score">${UI.formatNumber(a.score)} pts</span>`;
      return s.status === 'Closed' ? '<span class="subtle">You didn\'t play this one</span>' : '<span class="subtle">Your host will start this live</span>';
    }
    if (s.status === 'Open') {
      if (a && a.completed) return `<span class="score">${UI.formatNumber(a.score)} pts</span><span class="badge badge-outline">Done</span>`;
      return `<a class="btn btn-primary" href="/play.html?session=${encodeURIComponent(s.id)}">${a ? 'Continue' : 'Play'}</a>`;
    }
    if (a) return `<span class="score">${UI.formatNumber(a.score)} pts</span>`;
    return s.status === 'Closed' ? '<span class="subtle">You didn\'t play this one</span>' : '<span class="subtle">Not open yet</span>';
  }

  function manageActions(s) {
    const buttons = [];
    if (s.mode === 'Live') {
      if (s.status === 'Live') buttons.push(`<a class="btn btn-primary btn-sm" href="/host.html?pin=${encodeURIComponent(s.livePin)}">Open host screen</a>`);
      else if (s.status === 'Draft') buttons.push(`<button class="btn btn-primary btn-sm" data-act="live">${UI.icon('play')}Run live</button>`);
    } else if (s.status === 'Draft') {
      buttons.push('<button class="btn btn-primary btn-sm" data-act="open">Open for play…</button>');
    } else if (s.status === 'Open') {
      buttons.push('<button class="btn btn-secondary btn-sm" data-act="extend">Change deadline…</button>');
      buttons.push('<button class="btn btn-ghost btn-sm" data-act="close">Close now</button>');
    } else if (s.status === 'Closed') {
      buttons.push('<button class="btn btn-ghost btn-sm" data-act="reopen">Reopen…</button>');
    }
    if (s.attemptCount) buttons.push(`<button class="btn btn-ghost btn-sm" data-act="results">Results (${s.attemptCount})</button>`);
    buttons.push(`<button class="btn btn-ghost btn-sm" data-act="edit">${UI.icon('edit')}Edit</button>`);
    buttons.push(`<button class="btn btn-ghost btn-sm" data-act="share">${UI.icon('qr')}Share</button>`);
    buttons.push(`<button class="btn btn-danger-ghost btn-sm" data-act="remove" aria-label="Delete ${UI.escape(s.title)}">${UI.icon('trash')}Delete</button>`);
    return buttons.join('');
  }

  /** "Created … · Started … · Closed …" for a session card. */
  function sessionTimeline(s) {
    const parts = [`Created ${UI.stamp(s.createdAt)}`];
    if (s.openedAt) parts.push(`${s.mode === 'Live' ? 'Started' : 'Opened'} ${UI.stamp(s.openedAt)}`);
    if (s.status === 'Open' && s.closesAt) parts.push(`Closes ${UI.stamp(s.closesAt)}`);
    else if (s.status === 'Closed') {
      const end = s.closedAt || s.closesAt;
      if (end) parts.push(`Closed ${UI.stamp(end)}`);
    }
    return parts.join(' · ');
  }

  function renderSessions() {
    const list = $('sessionList');
    const sessions = detail.sessions || [];
    if (!sessions.length) {
      list.innerHTML = canManage
        ? '<div class="empty"><h3>No sessions yet</h3><p>Add a live game or a self-paced quiz with a deadline.</p></div>'
        : '<div class="empty"><p>Your host hasn\'t added any sessions yet.</p></div>';
      return;
    }
    list.innerHTML = sessions.map(s => `
      <div class="card card-sm session" data-id="${UI.escape(s.id)}">
        <div>
          <h3>${UI.escape(s.title)}</h3>
          <div class="session-meta">
            <span class="badge badge-outline">${s.mode === 'Live' ? 'Live game' : 'Self-paced'}</span>
            ${statusBadge(s)}
            ${canManage && s.code ? `<span class="badge" title="Access code">Code ${UI.escape(s.livePin || s.code)}</span>` : ''}
            <span class="subtle">${s.questionCount} question${s.questionCount === 1 ? '' : 's'}</span>
          </div>
          <p class="subtle mt-1" style="font-size: 0.8125rem;">${sessionTimeline(s)}</p>
        </div>
        <div class="session-actions">${canManage ? manageActions(s) : memberActions(s)}</div>
      </div>`).join('');

    list.querySelectorAll('[data-act]').forEach(b => {
      const s = sessions.find(x => x.id === b.closest('[data-id]').dataset.id);
      const handlers = {
        live: () => runLive(s, b), open: () => openSession(s, 'open'), extend: () => openSession(s, 'extend'),
        reopen: () => openSession(s, 'reopen'), close: () => closeSession(s), results: () => showResults(s), remove: () => deleteSession(s),
        edit: () => editSession(s), share: () => shareSession(s),
      };
      b.onclick = handlers[b.dataset.act];
    });
  }

  const sessionUrl = (s, suffix = '') => `/api/tournaments/${encodeURIComponent(tournamentId)}/sessions/${encodeURIComponent(s.id)}${suffix}`;

  async function addSession() {
    let quizzes;
    try {
      quizzes = await UI.api('/api/quizzes');
    } catch (err) {
      return UI.toast(err.message, 'error');
    }
    UI.openDialog({
      labelledBy: 'addTitle',
      render(el, close) {
        el.innerHTML = `
          <h2 class="dialog-title" id="addTitle">Add a session</h2>
          <form class="stack mt-4" novalidate>
            <div class="field">
              <label class="label" for="addQuiz">Questions</label>
              <select class="select" id="addQuiz"><option value="">Write my own questions</option>${quizzes.map(q => `<option value="${UI.escape(q.id)}">${UI.escape(q.title)} · ${q.questions.length} questions</option>`).join('')}</select>
            </div>
            <div class="field">
              <label class="label" for="addTitleInput">Session name</label>
              <input class="input" id="addTitleInput" maxlength="100" placeholder="e.g. Week 1 quiz">
              <span class="hint">Optional when you pick a saved quiz.</span>
            </div>
            <fieldset class="field" style="border: 0;">
              <legend class="label mb-2">How it's played</legend>
              <div class="stack-sm">
                <label class="option-card"><input type="radio" name="mode" value="Live" checked>
                  <div class="option-body"><div class="option-title">Live game</div><div class="option-desc">You run it on a shared screen. Players join with a PIN and answer together.</div></div></label>
                <label class="option-card"><input type="radio" name="mode" value="SelfPaced">
                  <div class="option-body"><div class="option-title">Self-paced</div><div class="option-desc">You open it until a deadline. Players answer on their own time, each question still timed.</div></div></label>
              </div>
            </fieldset>
            <div class="dialog-actions">
              <button type="button" class="btn btn-secondary" data-cancel>Cancel</button>
              <button type="submit" class="btn btn-primary">Add session</button>
            </div>
          </form>`;
        el.querySelector('[data-cancel]').onclick = () => close();
        el.querySelector('form').onsubmit = async e => {
          e.preventDefault();
          const body = {
            quizId: el.querySelector('#addQuiz').value,
            title: el.querySelector('#addTitleInput').value.trim(),
            mode: el.querySelector('input[name=mode]:checked').value,
          };
          if (!body.quizId && !body.title) {
            el.querySelector('#addTitleInput').setAttribute('aria-invalid', 'true');
            el.querySelector('#addTitleInput').focus();
            return UI.toast('Give the session a name.', 'error');
          }
          try {
            const created = await UI.busy(el.querySelector('[type=submit]'), () => UI.api(`/api/tournaments/${encodeURIComponent(tournamentId)}/sessions`, { method: 'POST', body }));
            close();
            UI.toast('Session added', 'success');
            await refreshSessions();
            if (!body.quizId) editSession(created);
          } catch (err) {
            UI.toast(err.message, 'error');
          }
        };
      },
    });
  }

  // ---------------------------------------------------------------------------
  // Session editor
  // ---------------------------------------------------------------------------
  const emptyQuestion = () => ({ text: '', choices: ['', '', '', ''], correctIndex: 0, timeLimitSeconds: 20, points: 1000 });

  function questionCard(q, i, total, locked) {
    const dis = locked ? 'disabled' : '';
    return `
      <div class="q-card" data-qi="${i}">
        <div class="q-card-head">
          <span class="grow">Question ${i + 1}</span>
          <label class="q-meta">Seconds <input class="input input-sm" type="number" min="5" max="120" value="${q.timeLimitSeconds}" data-f="time" ${dis}></label>
          <label class="q-meta">Points <input class="input input-sm" type="number" min="0" max="5000" step="100" value="${q.points}" data-f="points" ${dis}></label>
          ${locked ? '' : `
            <button type="button" class="btn btn-ghost btn-icon btn-sm" data-act="up" aria-label="Move question ${i + 1} up" ${i === 0 ? 'disabled' : ''}>${UI.icon('back', 'rot-up')}</button>
            <button type="button" class="btn btn-ghost btn-icon btn-sm" data-act="down" aria-label="Move question ${i + 1} down" ${i === total - 1 ? 'disabled' : ''}>${UI.icon('back', 'rot-down')}</button>
            <button type="button" class="btn btn-ghost btn-icon btn-sm" data-act="remove-q" aria-label="Delete question ${i + 1}">${UI.icon('trash')}</button>`}
        </div>
        <label class="sr-only" for="qt-${i}">Question ${i + 1} text</label>
        <input class="input" id="qt-${i}" value="${UI.escape(q.text)}" data-f="text" maxlength="300" placeholder="Type the question" style="font-weight: 600;" ${dis}>
        <div class="mt-2">
          ${q.choices.map((c, ci) => `
            <div class="choice-row ${ci === q.correctIndex ? 'is-correct' : ''}">
              <input type="radio" name="correct-${i}" ${ci === q.correctIndex ? 'checked' : ''} data-correct="${ci}" aria-label="Mark answer ${ci + 1} as correct" ${dis}>
              <input class="input input-sm" value="${UI.escape(c)}" data-choice="${ci}" maxlength="200" placeholder="Answer ${ci + 1}" aria-label="Answer ${ci + 1}" ${dis}>
              ${locked ? '' : `<button type="button" class="btn btn-ghost btn-icon btn-sm" data-act="remove-c" data-ci="${ci}" aria-label="Remove answer ${ci + 1}" ${q.choices.length <= 2 ? 'disabled' : ''}>${UI.icon('x')}</button>`}
            </div>`).join('')}
        </div>
        ${!locked && q.choices.length < 6 ? `<button type="button" class="btn btn-ghost btn-sm mt-2" data-act="add-c">${UI.icon('plus')}Add answer</button>` : ''}
      </div>`;
  }

  function problemWith(draft) {
    for (let i = 0; i < draft.questions.length; i++) {
      const q = draft.questions[i], n = i + 1;
      if (!q.text.trim()) return { n: i, msg: `Question ${n} has no text.` };
      if (q.choices.some(c => !c.trim())) return { n: i, msg: `Question ${n} has an empty answer. Fill it in or remove it.` };
      const lower = q.choices.map(c => c.trim().toLowerCase());
      if (new Set(lower).size !== lower.length) return { n: i, msg: `Question ${n} has two identical answers.` };
      if (q.timeLimitSeconds < 5 || q.timeLimitSeconds > 120) return { n: i, msg: `Question ${n}: time must be 5 to 120 seconds.` };
    }
    return null;
  }

  async function editSession(s) {
    let data;
    try {
      data = await UI.api(sessionUrl(s));
    } catch (err) {
      return UI.toast(err.message, 'error');
    }
    const locked = !!data.session.questionsLocked;
    const draft = { title: data.session.title, questions: data.questions.map(q => Object.assign({}, q, { choices: [...q.choices] })) };
    let dirty = false;
    const lockedWhy = data.session.status === 'Live'
      ? 'A live game is running, so the questions are locked. End it first to change them.'
      : 'Players have already played this session, so changing the questions would change their scores. You can still rename it.';

    const handle = UI.openDialog({
      wide: true,
      dismissible: false,
      labelledBy: 'editSessionTitle',
      render(el, close) {
        el.classList.add('dialog-editor');
        el.innerHTML = `
          <div class="dialog-head">
            <h2 class="dialog-title" id="editSessionTitle">Edit session</h2>
            <button type="button" class="btn btn-ghost btn-icon btn-sm" data-act="cancel" aria-label="Close">${UI.icon('x')}</button>
          </div>
          ${locked ? `<div class="callout callout-warning mb-4">${UI.icon('alert')}<div>${UI.escape(lockedWhy)}</div></div>` : ''}
          <div class="field mb-4">
            <label class="label" for="editName">Session name</label>
            <input class="input" id="editName" maxlength="100" value="${UI.escape(draft.title)}">
          </div>
          <div class="spread mb-2">
            <h3>Questions <span class="muted num" id="qCount"></span></h3>
            ${locked ? '' : `<div class="row-wrap">
              <button type="button" class="btn btn-secondary btn-sm" data-act="toggle-paste" aria-expanded="false">${UI.icon('copy')}Paste questions</button>
              <button type="button" class="btn btn-secondary btn-sm" data-act="add-q">${UI.icon('plus')}Add question</button>
            </div>`}
          </div>
          ${locked ? '' : `
            <div class="card card-sm mb-4" id="pastePanel" hidden>
              <label class="label" for="pasteText">Paste your questions</label>
              <p class="hint mb-2">From a document, a chat, or a spreadsheet. Mark the correct answer with a *, a ✓, bold text, or an "Answer: B" line. Questions can be numbered or just separated by blank lines.</p>
              <textarea class="textarea mono" id="pasteText" spellcheck="false" style="min-height: 160px;" placeholder="1. What is the capital of France?&#10;A) London&#10;B) Paris *&#10;C) Madrid&#10;&#10;2. Water boils at 100°C.&#10;Answer: True"></textarea>
              <div id="pasteResult" class="mt-3" aria-live="polite"></div>
              <div class="row-wrap mt-3">
                <button type="button" class="btn btn-primary btn-sm" data-act="import" disabled>Add questions</button>
                <button type="button" class="btn btn-ghost btn-sm" data-act="toggle-paste">Close</button>
              </div>
            </div>`}
          <div id="qList"></div>
          <div class="dialog-actions">
            <button type="button" class="btn btn-secondary" data-act="cancel">Cancel</button>
            <button type="button" class="btn btn-primary" data-act="save">Save changes</button>
          </div>`;

        const list = el.querySelector('#qList');
        const render = () => {
          el.querySelector('#qCount').textContent = `(${draft.questions.length})`;
          list.innerHTML = draft.questions.length
            ? draft.questions.map((q, i) => questionCard(q, i, draft.questions.length, locked)).join('')
            : '<div class="empty"><p>No questions yet. Add one to get started.</p></div>';
        };
        render();

        const qFor = node => draft.questions[Number(node.closest('[data-qi]').dataset.qi)];
        const touch = () => { dirty = true; };

        el.querySelector('#editName').addEventListener('input', touch);
        if (el.querySelector('#pasteText')) el.querySelector('#pasteText').addEventListener('input', touch);
        list.addEventListener('input', e => {
          const t = e.target, q = t.closest('[data-qi]') && qFor(t);
          if (!q) return;
          touch();
          if (t.dataset.f === 'text') q.text = t.value;
          else if (t.dataset.f === 'time') q.timeLimitSeconds = parseInt(t.value, 10) || 0;
          else if (t.dataset.f === 'points') q.points = parseInt(t.value, 10) || 0;
          else if (t.dataset.choice !== undefined) q.choices[Number(t.dataset.choice)] = t.value;
          else if (t.dataset.correct !== undefined) {
            q.correctIndex = Number(t.dataset.correct);
            t.closest('.q-card').querySelectorAll('.choice-row').forEach((row, i) => row.classList.toggle('is-correct', i === q.correctIndex));
          }
        });

        const addQuestion = () => {
          draft.questions.push(emptyQuestion());
          touch();
          render();
          const last = list.querySelector('.q-card:last-child [data-f="text"]');
          last.scrollIntoView({ block: 'center', behavior: 'smooth' });
          last.focus();
        };

        // Paste: parse as you type, show what was understood, add on confirmation.
        let parsed = [];
        let parseTimer = null;
        let parseSeq = 0;
        const pasteBox = () => el.querySelector('#pasteText');
        const resultBox = () => el.querySelector('#pasteResult');
        const importBtn = () => el.querySelector('[data-act="import"]');

        function togglePaste() {
          const panel = el.querySelector('#pastePanel');
          panel.hidden = !panel.hidden;
          el.querySelector('[data-act="toggle-paste"]').setAttribute('aria-expanded', String(!panel.hidden));
          if (!panel.hidden) { panel.scrollIntoView({ block: 'nearest', behavior: 'smooth' }); pasteBox().focus(); }
        }

        async function parsePasted() {
          const text = pasteBox().value;
          const mine = ++parseSeq;
          if (!text.trim()) {
            parsed = [];
            resultBox().innerHTML = '';
            importBtn().disabled = true;
            importBtn().textContent = 'Add questions';
            return;
          }
          let r;
          try {
            r = await UI.api('/api/quizzes/parse', { method: 'POST', body: { rawText: text, title: draft.title } });
          } catch (err) {
            if (mine === parseSeq) resultBox().innerHTML = `<div class="form-error">${UI.icon('alert')}<span>${UI.escape(err.message)}</span></div>`;
            return;
          }
          if (mine !== parseSeq) return;
          parsed = (r.quiz && r.quiz.questions) || [];
          // A JSON import can include questions the server flagged as invalid, so keep only complete ones.
          parsed = parsed.filter(q => q.text && q.choices && q.choices.length >= 2 && q.choices.length <= 6 && q.correctIndex >= 0 && q.correctIndex < q.choices.length);
          const errors = r.errors || [];
          const warnings = r.warnings || [];
          resultBox().innerHTML = `
            ${parsed.length ? `<p class="success-line" style="color: var(--success); font-weight: 600;">${UI.icon('check')} Found ${parsed.length} question${parsed.length === 1 ? '' : 's'}</p>
              <ol class="paste-preview">${parsed.map(q => `<li><span>${UI.escape(q.text)}</span><span class="subtle">Answer: ${UI.escape(q.choices[q.correctIndex])}</span></li>`).join('')}</ol>` : ''}
            ${errors.length ? `<div class="form-error mt-2">${UI.icon('alert')}<div><strong>${parsed.length ? 'Couldn\'t read these, so they will be skipped:' : 'Nothing could be read:'}</strong><ul style="margin-left: 18px;">${errors.map(e => `<li>${UI.escape(e)}</li>`).join('')}</ul></div></div>` : ''}
            ${warnings.length && parsed.length ? `<p class="subtle mt-2">${warnings.map(UI.escape).join(' ')}</p>` : ''}`;
          importBtn().disabled = parsed.length === 0;
          importBtn().textContent = parsed.length ? `Add ${parsed.length} question${parsed.length === 1 ? '' : 's'}` : 'Add questions';
        }

        function importPasted() {
          if (!parsed.length) return;
          parsed.forEach(q => draft.questions.push({
            text: q.text, choices: q.choices, correctIndex: q.correctIndex,
            timeLimitSeconds: q.timeLimitSeconds || 20, points: q.points ?? 1000,
          }));
          const n = parsed.length;
          parsed = [];
          pasteBox().value = '';
          resultBox().innerHTML = '';
          importBtn().disabled = true;
          el.querySelector('#pastePanel').hidden = true;
          el.querySelector('[data-act="toggle-paste"]').setAttribute('aria-expanded', 'false');
          touch();
          render();
          UI.toast(`Added ${n} question${n === 1 ? '' : 's'}`, 'success');
          list.querySelector('.q-card:last-child').scrollIntoView({ block: 'center', behavior: 'smooth' });
        }

        if (pasteBox()) {
          pasteBox().addEventListener('input', () => { clearTimeout(parseTimer); parseTimer = setTimeout(parsePasted, 450); });
        }

        async function cancel() {
          if (dirty) {
            const ok = await UI.confirm({ title: 'Discard your changes?', message: "You've edited this session but haven't saved.", confirmText: 'Discard changes', cancelText: 'Keep editing', danger: true });
            if (!ok) return;
          }
          close();
        }

        async function save() {
          draft.title = el.querySelector('#editName').value.trim();
          if (!draft.title) {
            el.querySelector('#editName').setAttribute('aria-invalid', 'true');
            el.querySelector('#editName').focus();
            return UI.toast('Give the session a name.', 'error');
          }
          if (!locked) {
            const problem = problemWith(draft);
            if (problem) {
              const card = list.querySelectorAll('.q-card')[problem.n];
              card.scrollIntoView({ block: 'center', behavior: 'smooth' });
              return UI.toast(problem.msg, 'error');
            }
          }
          try {
            await UI.busy(el.querySelector('[data-act="save"]'), () => UI.api(sessionUrl(s), {
              method: 'PUT',
              body: locked ? { title: draft.title } : { title: draft.title, questions: draft.questions },
            }));
            dirty = false;
            close();
            UI.toast('Session saved', 'success');
            refreshSessions();
          } catch (err) {
            UI.toast(err.message, 'error');
          }
        }

        el.addEventListener('click', e => {
          const b = e.target.closest('[data-act]');
          if (!b) return;
          const act = b.dataset.act;
          if (act === 'cancel') return cancel();
          if (act === 'save') return save();
          if (act === 'add-q') return addQuestion();
          if (act === 'import') return importPasted();
          if (act === 'toggle-paste') return togglePaste();
          const card = b.closest('[data-qi]');
          if (!card) return;
          const i = Number(card.dataset.qi), q = draft.questions[i];
          if (act === 'up' && i > 0) draft.questions.splice(i - 1, 0, draft.questions.splice(i, 1)[0]);
          else if (act === 'down' && i < draft.questions.length - 1) draft.questions.splice(i + 1, 0, draft.questions.splice(i, 1)[0]);
          else if (act === 'remove-q') {
            UI.confirm({
              title: `Delete question ${i + 1}?`,
              message: q.text.trim() ? `“${q.text.trim()}” will be removed from this session.` : 'This empty question will be removed.',
              confirmText: 'Delete question', danger: true,
            }).then(ok => { if (ok) { draft.questions.splice(i, 1); touch(); render(); } });
            return;
          }
          else if (act === 'add-c' && q.choices.length < 6) q.choices.push('');
          else if (act === 'remove-c' && q.choices.length > 2) {
            const ci = Number(b.dataset.ci);
            q.choices.splice(ci, 1);
            if (q.correctIndex === ci) q.correctIndex = 0;
            else if (q.correctIndex > ci) q.correctIndex--;
          } else return;
          touch();
          render();
          if (act === 'add-c') {
            const inputs = list.querySelectorAll(`[data-qi="${i}"] [data-choice]`);
            inputs[inputs.length - 1].focus();
          }
        });
      },
    });
    // Escape asks before throwing away edits.
    document.addEventListener('keydown', function esc(e) {
      if (!document.body.contains(handle.el)) return document.removeEventListener('keydown', esc);
      if (e.key === 'Escape' && !document.querySelector('.dialog-backdrop ~ .dialog-backdrop')) {
        const x = handle.el.querySelector('[data-act="cancel"]');
        if (x) x.click();
      }
    });
  }

  // ---------------------------------------------------------------------------
  // Full-screen join view (for a projector or shared screen)
  // ---------------------------------------------------------------------------
  /**
   * Fills the screen with a QR code, the web address and the code, large enough to read from across a room.
   * Options: eyebrow, title, code, link (what the QR code opens), urlText, countText (optional async () => string).
   */
  function showFullscreenCode({ eyebrow, title, code, link, urlText, countText }) {
    const overlay = document.createElement('div');
    overlay.className = 'fs-overlay';
    overlay.setAttribute('role', 'dialog');
    overlay.setAttribute('aria-modal', 'true');
    overlay.setAttribute('aria-label', `Join ${title}`);
    overlay.innerHTML = `
      <button type="button" class="btn btn-secondary fs-close">${UI.icon('x')}Close</button>
      <div class="fs-body">
        <p class="eyebrow">${UI.escape(eyebrow)}</p>
        <h1 class="fs-title">${UI.escape(title)}</h1>
        <div class="fs-grid">
          <div class="qr-box fs-qr" id="fsQr" aria-label="QR code to join"></div>
          <div class="fs-info">
            <p class="fs-step">Scan the code, or go to</p>
            <p class="fs-url">${UI.escape(urlText)}</p>
            <p class="fs-step">and enter the code</p>
            <div class="pin fs-code">${UI.escape(code)}</div>
            <p class="fs-count" id="fsCount" aria-live="polite"></p>
          </div>
        </div>
      </div>`;

    const box = overlay.querySelector('#fsQr');
    if (typeof QRCode !== 'undefined') {
      // Drawn large, scaled to the screen by CSS so it stays sharp on a projector.
      new QRCode(box, { text: link, width: 512, height: 512, colorDark: '#000000', colorLight: '#ffffff', correctLevel: QRCode.CorrectLevel.M });
    } else {
      box.remove();
    }

    const previouslyFocused = document.activeElement;
    let timer = null;
    let closed = false;

    function close() {
      if (closed) return;
      closed = true;
      clearInterval(timer);
      document.removeEventListener('keydown', onKey, true);
      document.removeEventListener('fullscreenchange', onFullscreenChange);
      overlay.remove();
      document.body.style.overflow = '';
      if (document.fullscreenElement) document.exitFullscreen().catch(() => {});
      if (previouslyFocused && previouslyFocused.focus) previouslyFocused.focus();
    }
    function onKey(e) {
      if (e.key === 'Escape') { e.stopPropagation(); close(); }
    }
    function onFullscreenChange() {
      // The browser uses Escape to leave full screen; treat that as closing the view.
      if (!document.fullscreenElement) close();
    }

    overlay.querySelector('.fs-close').onclick = close;
    document.addEventListener('keydown', onKey, true);
    document.body.appendChild(overlay);
    document.body.style.overflow = 'hidden';
    overlay.querySelector('.fs-close').focus();

    if (overlay.requestFullscreen) {
      overlay.requestFullscreen().then(() => document.addEventListener('fullscreenchange', onFullscreenChange)).catch(() => { /* the overlay still fills the window */ });
    }

    if (countText) {
      const refresh = async () => {
        try { overlay.querySelector('#fsCount').textContent = await countText(); } catch (_) { /* keep the last value */ }
      };
      refresh();
      timer = setInterval(refresh, 5000);
    }
  }

  async function playersJoinedText() {
    const members = await UI.api(`/api/tournaments/${encodeURIComponent(tournamentId)}/members`);
    return members.length === 1 ? '1 player has joined' : `${members.length} players have joined`;
  }

  function showTournamentJoinScreen() {
    showFullscreenCode({
      eyebrow: `Join the tournament · Hosted by ${info.hostName}`,
      title: info.name,
      code: detail.joinCode,
      link: `${location.origin}/tournaments.html?join=${encodeURIComponent(detail.joinCode)}`,
      urlText: `${location.host}/tournaments.html`,
      countText: playersJoinedText,
    });
  }

  // ---------------------------------------------------------------------------
  // Share
  // ---------------------------------------------------------------------------
  function shareSession(s) {
    const code = s.livePin || s.code;
    const live = s.mode === 'Live';
    const link = live ? `${location.origin}/player.html?pin=${encodeURIComponent(code)}` : `${location.origin}/play.html?session=${encodeURIComponent(s.id)}`;

    UI.openDialog({
      labelledBy: 'shareTitle',
      render(el, close) {
        el.innerHTML = `
          <div class="dialog-head">
            <h2 class="dialog-title" id="shareTitle">${UI.escape(s.title)}</h2>
            <button type="button" class="btn btn-ghost btn-icon btn-sm" data-close aria-label="Close">${UI.icon('x')}</button>
          </div>
          <div class="text-center">
            <p class="muted">Players enter this code at <strong>${UI.escape(location.host)}/player.html</strong></p>
            <div class="pin mt-2" style="font-size: 3rem;">${UI.escape(code)}</div>
            <div class="qr-box mt-4" id="shareQr"></div>
            <p class="subtle mt-3">${live
              ? 'The game starts when you press Run live. Signed-in players who enter the code join the tournament automatically.'
              : `Only players in this tournament can play it, so they need the tournament code <strong>${UI.escape(detail.joinCode || '')}</strong> first.${s.status === 'Open' ? '' : ' It isn\'t open yet.'}`}</p>
          </div>
          <div class="dialog-actions">
            <button class="btn btn-secondary" data-fullscreen>${UI.icon('screen')}Full screen</button>
            <button class="btn btn-secondary" data-copy-code>${UI.icon('copy')}Copy code</button>
            <button class="btn btn-primary" data-copy-link>${UI.icon('link')}Copy link</button>
          </div>`;
        el.querySelector('[data-close]').onclick = () => close();
        el.querySelector('[data-copy-code]').onclick = () => UI.copyText(code, 'Code copied');
        el.querySelector('[data-fullscreen]').onclick = () => {
          close();
          showFullscreenCode({
            eyebrow: live ? 'Join the game' : 'Play this quiz',
            title: s.title,
            code,
            link,
            urlText: `${location.host}/player.html`,
          });
        };
        el.querySelector('[data-copy-link]').onclick = () => UI.copyText(link, 'Link copied');
        const box = el.querySelector('#shareQr');
        if (typeof QRCode !== 'undefined') new QRCode(box, { text: link, width: 200, height: 200, colorDark: '#000000', colorLight: '#ffffff', correctLevel: QRCode.CorrectLevel.M });
        else box.remove();
      },
    });
  }

  async function runLive(s, button) {
    try {
      const { pin } = await UI.busy(button, () => UI.api(sessionUrl(s, '/live'), { method: 'POST' }));
      location.href = `/host.html?pin=${encodeURIComponent(pin)}`;
    } catch (err) {
      UI.toast(err.message, 'error');
    }
  }

  function openSession(s, kind) {
    const titles = { open: 'Open for play', extend: 'Change the deadline', reopen: 'Reopen this session' };
    const initial = kind === 'extend' && s.closesAt ? new Date(s.closesAt) : new Date(Date.now() + 3 * 86400000);
    const picks = [['1 hour', 3600000], ['1 day', 86400000], ['3 days', 3 * 86400000], ['1 week', 7 * 86400000]];

    UI.openDialog({
      labelledBy: 'openTitle',
      render(el, close) {
        el.innerHTML = `
          <h2 class="dialog-title" id="openTitle">${titles[kind]}</h2>
          <p class="dialog-body">Players in this tournament can play <strong>${UI.escape(s.title)}</strong> until the deadline. Each player gets one attempt.</p>
          <form class="stack mt-4" novalidate>
            <div class="field">
              <label class="label" for="deadline">Closes at</label>
              <input class="input" type="datetime-local" id="deadline" value="${UI.toLocalInput(initial)}" required>
              <div class="quick-picks mt-2">${picks.map(([label, ms]) => `<button type="button" class="btn btn-ghost btn-sm" data-ms="${ms}">${label} from now</button>`).join('')}</div>
              <span class="hint" id="deadlineHint"></span>
            </div>
            <div class="dialog-actions">
              <button type="button" class="btn btn-secondary" data-cancel>Cancel</button>
              <button type="submit" class="btn btn-primary">${kind === 'extend' ? 'Save deadline' : 'Open session'}</button>
            </div>
          </form>`;
        const input = el.querySelector('#deadline');
        const hint = el.querySelector('#deadlineHint');
        const updateHint = () => {
          const d = new Date(input.value);
          hint.textContent = isNaN(d) ? '' : `Closes ${UI.relativeTime(d)} (${UI.formatDateTime(d)})`;
        };
        updateHint();
        input.addEventListener('input', updateHint);
        el.querySelectorAll('[data-ms]').forEach(b => {
          b.onclick = () => { input.value = UI.toLocalInput(new Date(Date.now() + Number(b.dataset.ms))); updateHint(); };
        });
        el.querySelector('[data-cancel]').onclick = () => close();
        el.querySelector('form').onsubmit = async e => {
          e.preventDefault();
          const d = new Date(input.value);
          if (isNaN(d) || d <= new Date()) {
            input.setAttribute('aria-invalid', 'true');
            return UI.toast('Pick a deadline in the future.', 'error');
          }
          try {
            await UI.busy(el.querySelector('[type=submit]'), () => UI.api(sessionUrl(s, '/open'), { method: 'POST', body: { closesAt: d.toISOString() } }));
            close();
            UI.toast(kind === 'extend' ? 'Deadline updated' : `${s.title} is open`, 'success');
            refreshSessions();
          } catch (err) {
            UI.toast(err.message, 'error');
          }
        };
      },
    });
  }

  async function closeSession(s) {
    const ok = await UI.confirm({
      title: 'Close this session now?',
      message: 'Nobody can start it after this, and anyone partway through stops where they are. Points already earned still count. You can reopen it later.',
      confirmText: 'Close now',
      danger: true,
    });
    if (!ok) return;
    try {
      await UI.api(sessionUrl(s, '/close'), { method: 'POST' });
      UI.toast('Session closed', 'success');
      refreshSessions();
    } catch (err) {
      UI.toast(err.message, 'error');
    }
  }

  async function deleteSession(s) {
    const played = s.attemptCount || 0;
    const ok = await UI.confirm({
      title: `Delete “${s.title}”?`,
      message: (s.status === 'Live' ? 'The live game will end for everyone. ' : '') +
        (played ? `${played} player${played === 1 ? '' : 's'}' scores from this session will be removed from the standings. ` : '') + 'This can\'t be undone.',
      confirmText: 'Delete session',
      danger: true,
    });
    if (!ok) return;
    try {
      await UI.api(sessionUrl(s), { method: 'DELETE' });
      UI.toast('Session deleted', 'success');
      refreshSessions();
      refreshStandings();
    } catch (err) {
      UI.toast(err.message, 'error');
    }
  }

  function showResults(s) {
    UI.openDialog({
      wide: true,
      labelledBy: 'resultsTitle',
      render(el, close) {
        el.innerHTML = `
          <div class="dialog-head">
            <h2 class="dialog-title" id="resultsTitle">${UI.escape(s.title)}</h2>
            <button class="btn btn-ghost btn-icon btn-sm" data-close aria-label="Close">${UI.icon('x')}</button>
          </div>
          <div class="table-wrap"><table class="table">
            <thead><tr><th>#</th><th>Player</th><th class="right">Score</th><th class="right">Correct</th><th>Status</th><th class="hide-sm">Started</th><th class="hide-sm">Finished</th></tr></thead>
            <tbody><tr><td colspan="7" class="empty-row">Loading…</td></tr></tbody>
          </table></div>`;
        el.querySelector('[data-close]').onclick = () => close();
        UI.api(sessionUrl(s, '/results')).then(r => {
          el.querySelector('tbody').innerHTML = r.attempts.map((a, i) => `
            <tr>
              <td>${UI.rankBadge(i + 1)}</td>
              <td><div class="player-cell">${UI.avatar(a.displayName)}<div>${UI.escape(a.displayName)}<div class="subtle">${UI.escape(a.email)}</div></div></div></td>
              <td class="right score">${UI.formatNumber(a.score)}</td>
              <td class="right num">${a.correct}/${a.questionCount}</td>
              <td>${a.completed ? '<span class="badge badge-success">Finished</span>' : `<span class="badge badge-warning">${a.answered} of ${a.questionCount}</span>`}</td>
              <td class="hide-sm muted">${UI.stamp(a.startedAt)}</td>
              <td class="hide-sm muted">${a.completedAt ? UI.stamp(a.completedAt) : '—'}</td>
            </tr>`).join('') || '<tr><td colspan="7" class="empty-row">No one has played yet.</td></tr>';
        }).catch(err => {
          el.querySelector('tbody').innerHTML = `<tr><td colspan="7" class="empty-row">${UI.escape(err.message)}</td></tr>`;
        });
      },
    });
  }

  // ---------------------------------------------------------------------------
  // Standings & members
  // ---------------------------------------------------------------------------
  function renderStandings(rows) {
    $('standingsUpdated').textContent = `Updated ${new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`;
    const podium = $('podium');
    if (rows.length) {
      podium.hidden = false;
      podium.innerHTML = [1, 0, 2].map(i => {
        const p = rows[i];
        return `<div class="podium-place p${i + 1} ${p ? '' : 'is-empty'}">
          <div class="podium-name">${p ? UI.escape(p.displayName) : ''}</div>
          <div class="podium-score">${p ? UI.formatNumber(p.totalScore) + ' pts' : ''}</div>
          <div class="podium-block">${i + 1}</div></div>`;
      }).join('');
    } else {
      podium.hidden = true;
    }
    $('standingsTable').innerHTML = rows.length ? rows.map(r => `
      <tr class="${r.isMe ? 'is-me' : ''}">
        <td>${UI.rankBadge(r.rank)}</td>
        <td><div class="player-cell">${UI.avatar(r.displayName)}${UI.escape(r.displayName)}${r.isMe ? ' <span class="badge badge-accent">You</span>' : ''}</div></td>
        <td class="right score">${UI.formatNumber(r.totalScore)}</td>
        <td class="right hide-sm num">${r.sessionsPlayed}</td>
        <td class="right num">${r.correct}/${r.answered}</td>
      </tr>`).join('') : '<tr><td colspan="5" class="empty-row">No scores yet. They appear once players finish a session.</td></tr>';
  }

  async function loadMembers() {
    try {
      const members = await UI.api(`/api/tournaments/${encodeURIComponent(tournamentId)}/members`);
      $('memberCount').textContent = members.length;
      $('membersTable').innerHTML = members.length ? members.map(m => `
        <tr>
          <td><div class="player-cell">${UI.avatar(m.displayName)}${UI.escape(m.displayName)}</div></td>
          <td class="hide-sm muted">${UI.escape(m.email)}</td>
          <td class="right score">${UI.formatNumber(m.totalScore)}</td>
          <td class="hide-sm muted">${UI.stamp(m.joinedAt)}</td>
          <td class="right"><button class="btn btn-ghost btn-sm" data-remove="${UI.escape(m.email)}" data-name="${UI.escape(m.displayName)}">Remove</button></td>
        </tr>`).join('') : '<tr><td colspan="5" class="empty-row">No one has joined yet. Share the code above.</td></tr>';
      $('membersTable').querySelectorAll('[data-remove]').forEach(b => {
        b.onclick = () => removeMember(b.dataset.remove, b.dataset.name);
      });
    } catch (err) {
      $('membersTable').innerHTML = `<tr><td colspan="5" class="empty-row">${UI.escape(err.message)}</td></tr>`;
    }
  }

  async function removeMember(email, name) {
    const ok = await UI.confirm({
      title: `Remove ${name}?`,
      message: 'They won\'t be able to play new sessions. Their past scores stay in the standings. Anyone with the code can join again.',
      confirmText: 'Remove',
      danger: true,
    });
    if (!ok) return;
    try {
      await UI.api(`/api/tournaments/${encodeURIComponent(tournamentId)}/members/${encodeURIComponent(email)}`, { method: 'DELETE' });
      UI.toast(`${name} removed`, 'success');
      loadMembers();
    } catch (err) {
      UI.toast(err.message, 'error');
    }
  }

  // ---------------------------------------------------------------------------
  // Tournament-level actions
  // ---------------------------------------------------------------------------
  function editDetails() {
    UI.openDialog({
      labelledBy: 'editTitle',
      render(el, close) {
        el.innerHTML = `
          <h2 class="dialog-title" id="editTitle">Edit tournament</h2>
          <form class="stack mt-4" novalidate>
            <div class="field"><label class="label" for="editName">Name</label>
              <input class="input" id="editName" maxlength="80" value="${UI.escape(info.name)}"></div>
            <div class="field"><label class="label" for="editDesc">Description <span class="subtle">(optional)</span></label>
              <textarea class="textarea" id="editDesc" maxlength="300" style="min-height: 90px;">${UI.escape(info.description || '')}</textarea></div>
            <div class="dialog-actions">
              <button type="button" class="btn btn-secondary" data-cancel>Cancel</button>
              <button type="submit" class="btn btn-primary">Save</button>
            </div>
          </form>`;
        el.querySelector('[data-cancel]').onclick = () => close();
        el.querySelector('form').onsubmit = async e => {
          e.preventDefault();
          try {
            await UI.busy(el.querySelector('[type=submit]'), () => UI.api(`/api/tournaments/${encodeURIComponent(tournamentId)}`, {
              method: 'PUT', body: { name: el.querySelector('#editName').value, description: el.querySelector('#editDesc').value },
            }));
            close();
            UI.toast('Saved', 'success');
            load();
          } catch (err) {
            UI.toast(err.message, 'error');
          }
        };
      },
    });
  }

  async function deleteTournament() {
    const ok = await UI.confirm({
      title: `Delete ${info.name}?`,
      message: 'Every session, result and player membership in this tournament will be permanently deleted, and any live game ends. This can\'t be undone.',
      confirmText: 'Delete tournament',
      danger: true,
      requireText: 'delete',
    });
    if (!ok) return;
    try {
      await UI.api(`/api/tournaments/${encodeURIComponent(tournamentId)}`, { method: 'DELETE' });
      UI.toast('Tournament deleted', 'success');
      location.href = '/host.html';
    } catch (err) {
      UI.toast(err.message, 'error');
    }
  }

  async function leaveTournament() {
    const ok = await UI.confirm({
      title: `Leave ${info.name}?`,
      message: 'You won\'t see its sessions any more. Your scores so far stay in the standings, and you can rejoin with the code.',
      confirmText: 'Leave tournament',
      danger: true,
    });
    if (!ok) return;
    try {
      await UI.api(`/api/tournaments/${encodeURIComponent(tournamentId)}/members/me`, { method: 'DELETE' });
      UI.toast('You left the tournament');
      location.href = '/tournaments.html';
    } catch (err) {
      UI.toast(err.message, 'error');
    }
  }

  function selectTab(id) {
    activeTab = id;
    document.querySelectorAll('.tab').forEach(t => t.setAttribute('aria-selected', String(t.dataset.tab === id)));
    document.querySelectorAll('.panel').forEach(p => p.classList.toggle('is-active', p.id === id));
    if (id === 'panelStandings') refreshStandings();
    if (id === 'panelMembers') loadMembers();
  }

  // ---------------------------------------------------------------------------
  // Boot
  // ---------------------------------------------------------------------------
  document.addEventListener('DOMContentLoaded', () => {
    if (!tournamentId) return showNotFound();
    document.querySelectorAll('.tab').forEach(t => { t.onclick = () => selectTab(t.dataset.tab); });
    $('addSession').onclick = addSession;
    $('copyCode').onclick = () => UI.copyText(detail.joinCode, 'Code copied');
    $('fullscreenCode').onclick = showTournamentJoinScreen;
    $('joinCode').onclick = showTournamentJoinScreen;
    $('joinCode').title = 'Click to show full screen';
    $('copyLink').onclick = () => UI.copyText(`${location.origin}/tournaments.html?join=${encodeURIComponent(detail.joinCode)}`, 'Invite link copied');

    // Keep live status, deadlines and scores fresh while the page is open.
    setInterval(() => {
      if (document.hidden || !info) return;
      if (activeTab === 'panelSessions' && detail) refreshSessions();
      if (activeTab === 'panelStandings') refreshStandings();
    }, 20000);

    Session.onChange(load);
    load();
  });
})();
