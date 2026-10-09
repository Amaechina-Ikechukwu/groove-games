// Groove admin
(function () {
  'use strict';

  const $ = id => document.getElementById(id);

  const EXAMPLE = `Q1: Which planet is closest to the sun?
A) Venus
B) Mercury *
C) Mars
D) Earth
Time: 15s

Q2: The Pacific is the largest ocean.
[x] True
[ ] False
Time: 10s`;

  let draft = null;          // parsed quiz being edited
  let draftDirty = false;    // unsaved edits in the preview
  let people = [];
  let searchTimer = null;

  // ---------------------------------------------------------------------------
  // Access
  // ---------------------------------------------------------------------------
  function showView(id) {
    document.querySelectorAll('.view').forEach(v => v.classList.toggle('is-active', v.id === id));
  }

  function route() {
    UI.renderAccount($('account'), { onSignIn: signIn, onSignOut: signOut });
    const s = Session.get();
    if (!s) return showView('viewGate');
    if (!Session.isAdmin(s)) {
      $('deniedEmail').textContent = s.email;
      $('deniedRole').textContent = s.role;
      return showView('viewDenied');
    }
    showView('viewAdmin');
    loadQuizzes();
    loadTournaments();
    loadGames();
    const active = document.querySelector('.tab[aria-selected="true"]').dataset.tab;
    if (active === 'panelLeaderboard') loadLeaderboard();
    if (active === 'panelPeople') loadPeople();
  }

  async function signIn() {
    await UI.signIn({ title: 'Admin sign in', allowSignup: false });
  }

  async function signOut() {
    const warning = hasUnsavedWork() ? 'You have a quiz that hasn\'t been saved. It will be lost.' : null;
    await UI.signOut({ warning });
  }

  function handleAuthError(err) {
    UI.toast(err.message, 'error');
    if (err.status === 401 || err.status === 403) route();
  }

  // ---------------------------------------------------------------------------
  // Tabs
  // ---------------------------------------------------------------------------
  function selectTab(panelId) {
    document.querySelectorAll('.tab').forEach(t => t.setAttribute('aria-selected', String(t.dataset.tab === panelId)));
    document.querySelectorAll('.panel').forEach(p => p.classList.toggle('is-active', p.id === panelId));
    if (panelId === 'panelQuizzes') loadQuizzes();
    if (panelId === 'panelLeaderboard') loadLeaderboard();
    if (panelId === 'panelPeople') loadPeople();
    if (panelId === 'panelTournaments') loadTournaments();
    if (panelId === 'panelGames') loadGames();
  }

  // ---------------------------------------------------------------------------
  // Tournaments & live games (admins see every host's)
  // ---------------------------------------------------------------------------
  async function loadTournaments() {
    const tbody = $('tournamentTable');
    try {
      const items = await UI.api('/api/tournaments/hosting');
      $('tournamentCount').textContent = items.length;
      tbody.innerHTML = items.length ? items.map(t => `
        <tr>
          <td><a href="/tournament.html?id=${encodeURIComponent(t.id)}" style="font-weight: 600;">${UI.escape(t.name)}</a></td>
          <td class="muted">${UI.escape(t.hostName)}<div class="subtle hide-sm">${UI.escape(t.hostEmail || '')}</div></td>
          <td class="right num">${t.memberCount}</td>
          <td class="right hide-sm num">${t.sessionCount}</td>
          <td class="right">${t.openCount ? `<span class="badge badge-live">${t.openCount}</span>` : '<span class="subtle">–</span>'}</td>
          <td class="hide-sm muted">${UI.stamp(t.createdAt)}</td>
        </tr>`).join('') : '<tr><td colspan="6" class="empty-row">No tournaments yet.</td></tr>';
    } catch (err) {
      tbody.innerHTML = `<tr><td colspan="6" class="empty-row">${UI.escape(err.message)}</td></tr>`;
    }
  }

  const GAME_STATES = {
    Lobby: ['Waiting for players', 'badge-accent'],
    GameEnded: ['Finished', ''],
  };

  async function loadGames() {
    const tbody = $('gamesTable');
    try {
      const games = await UI.api('/api/sessions');
      $('gameCount').textContent = games.length;
      tbody.innerHTML = games.length ? games.map(g => {
        const [label, cls] = GAME_STATES[g.state] || ['In progress', 'badge-live'];
        return `
          <tr>
            <td><span class="pin" style="font-size: 1.125rem;">${UI.escape(g.pin)}</span></td>
            <td><div style="font-weight: 600;">${UI.escape(g.quizTitle)}</div>
              <div class="subtle">${g.sessionType === 'Tournament' ? UI.escape(g.tournamentName) : 'Quick game'}</div></td>
            <td class="hide-sm muted">${UI.escape(g.hostId)}</td>
            <td><span class="badge ${cls}">${label}</span></td>
            <td class="right num">${g.connectedPlayers}</td>
            <td class="right" style="white-space: nowrap;">
              <a class="btn btn-ghost btn-sm" href="/host.html?pin=${encodeURIComponent(g.pin)}">Open</a>
              <button class="btn btn-danger-ghost btn-sm" data-end="${UI.escape(g.pin)}" data-state="${UI.escape(g.state)}" data-players="${g.connectedPlayers}" data-host="${UI.escape(g.hostId)}">End</button>
            </td>
          </tr>`;
      }).join('') : '<tr><td colspan="6" class="empty-row">No games are running.</td></tr>';
      tbody.querySelectorAll('[data-end]').forEach(b => { b.onclick = () => endGame(b.dataset); });
    } catch (err) {
      tbody.innerHTML = `<tr><td colspan="6" class="empty-row">${UI.escape(err.message)}</td></tr>`;
    }
  }

  async function endGame({ end: pin, state, players, host }) {
    const finished = state === 'GameEnded';
    const ok = await UI.confirm({
      title: finished ? `Close game ${pin}?` : `End game ${pin} for everyone?`,
      message: finished
        ? 'The PIN will stop working. Final scores are already saved.'
        : `This game belongs to ${host}. ${Number(players) ? `${players} player${players === '1' ? '' : 's'} will be sent out. ` : ''}Scores from an unfinished game aren't saved.`,
      confirmText: finished ? 'Close game' : 'End game',
      danger: !finished,
    });
    if (!ok) return;
    try {
      await UI.api(`/api/sessions/${encodeURIComponent(pin)}`, { method: 'DELETE' });
      UI.toast(`Game ${pin} ${finished ? 'closed' : 'ended'}`, 'success');
      loadGames();
    } catch (err) {
      handleAuthError(err);
    }
  }

  // ---------------------------------------------------------------------------
  // Quiz editor
  // ---------------------------------------------------------------------------
  const hasUnsavedWork = () => draftDirty || (!draft && $('rawText').value.trim().length > 0);

  async function parse() {
    const rawText = $('rawText').value;
    if (!rawText.trim()) {
      UI.toast('Write or paste some questions first.', 'error');
      $('rawText').focus();
      return;
    }
    if (draftDirty) {
      const ok = await UI.confirm({
        title: 'Replace your edits?',
        message: 'Previewing again rebuilds the questions from the text on the left. Changes you made in the preview will be lost.',
        confirmText: 'Replace',
        danger: true,
      });
      if (!ok) return;
    }

    try {
      const result = await UI.busy($('parseButton'), () => UI.api('/api/quizzes/parse', {
        method: 'POST',
        body: { rawText, title: $('quizTitle').value.trim() || 'Untitled quiz' },
      }));
      renderMessages(result);
      if (result.success && result.quiz.questions.length) {
        draft = result.quiz;
        draftDirty = true;
        renderPreview();
      } else {
        draft = null;
        draftDirty = false;
        renderPreview();
      }
    } catch (err) {
      handleAuthError(err);
    }
  }

  function renderMessages(result) {
    const box = $('parseMessages');
    const errors = result.errors || [];
    const warnings = result.warnings || [];
    if (!errors.length && !warnings.length) {
      box.hidden = true;
      return;
    }
    box.hidden = false;
    box.innerHTML = `
      ${errors.length ? `<div class="form-error parse-messages">${UI.icon('alert')}<div><strong>Some questions couldn't be read</strong><ul>${errors.map(e => `<li>${UI.escape(e)}</li>`).join('')}</ul></div></div>` : ''}
      ${warnings.length ? `<div class="callout callout-warning parse-messages">${UI.icon('info')}<div><ul>${warnings.map(w => `<li>${UI.escape(w)}</li>`).join('')}</ul></div></div>` : ''}`;
  }

  function renderPreview() {
    const container = $('preview');
    const count = draft ? draft.questions.length : 0;
    $('previewCount').textContent = count ? `(${count})` : '';
    $('saveButton').hidden = !count;

    if (!count) {
      container.innerHTML = '<div class="empty"><p>Your questions will show up here after you preview them.</p></div>';
      return;
    }

    container.innerHTML = draft.questions.map((q, qi) => `
      <div class="q-card" data-q="${qi}">
        <div class="q-card-head">
          <span class="grow">Question ${qi + 1}</span>
          <label class="q-meta">Seconds <input class="input input-sm" type="number" min="5" max="120" value="${q.timeLimitSeconds || 20}" data-field="time"></label>
          <label class="q-meta">Points <input class="input input-sm" type="number" min="0" max="5000" step="100" value="${q.points ?? 1000}" data-field="points"></label>
          <button type="button" class="btn btn-ghost btn-icon btn-sm" data-remove-question aria-label="Delete question ${qi + 1}" title="Delete question">${UI.icon('trash')}</button>
        </div>
        <label class="sr-only" for="qtext-${qi}">Question ${qi + 1} text</label>
        <input class="input" id="qtext-${qi}" value="${UI.escape(q.text)}" data-field="text" style="font-weight: 600;">
        <div class="mt-2">
          ${q.choices.map((c, ci) => `
            <div class="choice-row ${ci === q.correctIndex ? 'is-correct' : ''}">
              <input type="radio" name="correct-${qi}" ${ci === q.correctIndex ? 'checked' : ''} data-correct="${ci}" aria-label="Mark choice ${ci + 1} as correct">
              <input class="input input-sm" value="${UI.escape(c)}" data-choice="${ci}" aria-label="Choice ${ci + 1}">
              <button type="button" class="btn btn-ghost btn-icon btn-sm" data-remove-choice="${ci}" aria-label="Remove choice ${ci + 1}" ${q.choices.length <= 2 ? 'disabled' : ''}>${UI.icon('x')}</button>
            </div>`).join('')}
        </div>
        ${q.choices.length < 6 ? `<button type="button" class="btn btn-ghost btn-sm mt-2" data-add-choice>${UI.icon('plus')}Add choice</button>` : ''}
      </div>`).join('');
  }

  function onPreviewInput(e) {
    const card = e.target.closest('[data-q]');
    if (!card || !draft) return;
    const q = draft.questions[Number(card.dataset.q)];
    const t = e.target;
    draftDirty = true;
    if (t.dataset.field === 'text') q.text = t.value;
    else if (t.dataset.field === 'time') q.timeLimitSeconds = Math.max(5, Math.min(120, parseInt(t.value, 10) || 20));
    else if (t.dataset.field === 'points') q.points = Math.max(0, parseInt(t.value, 10) || 0);
    else if (t.dataset.choice !== undefined) q.choices[Number(t.dataset.choice)] = t.value;
    else if (t.dataset.correct !== undefined) {
      q.correctIndex = Number(t.dataset.correct);
      card.querySelectorAll('.choice-row').forEach((row, i) => row.classList.toggle('is-correct', i === q.correctIndex));
    }
  }

  async function onPreviewClick(e) {
    const card = e.target.closest('[data-q]');
    if (!card || !draft) return;
    const qi = Number(card.dataset.q);
    const q = draft.questions[qi];

    const removeQuestion = e.target.closest('[data-remove-question]');
    const removeChoice = e.target.closest('[data-remove-choice]');
    const addChoice = e.target.closest('[data-add-choice]');

    if (removeQuestion) {
      const ok = await UI.confirm({
        title: `Delete question ${qi + 1}?`,
        html: `<p>“${UI.escape(q.text || 'Untitled question')}” will be removed from this quiz.</p>`,
        confirmText: 'Delete question',
        danger: true,
      });
      if (!ok) return;
      draft.questions.splice(qi, 1);
      draftDirty = true;
      renderPreview();
    } else if (removeChoice) {
      const ci = Number(removeChoice.dataset.removeChoice);
      if (q.choices.length <= 2) return;
      q.choices.splice(ci, 1);
      if (q.correctIndex === ci) q.correctIndex = 0;
      else if (q.correctIndex > ci) q.correctIndex--;
      draftDirty = true;
      renderPreview();
    } else if (addChoice) {
      q.choices.push('');
      draftDirty = true;
      renderPreview();
      const inputs = $('preview').querySelectorAll(`[data-q="${qi}"] [data-choice]`);
      inputs[inputs.length - 1].focus();
    }
  }

  function validateDraft() {
    for (let i = 0; i < draft.questions.length; i++) {
      const q = draft.questions[i];
      if (!q.text.trim()) return `Question ${i + 1} has no text.`;
      if (q.choices.some(c => !c.trim())) return `Question ${i + 1} has an empty choice.`;
      if (q.choices.length < 2) return `Question ${i + 1} needs at least two choices.`;
    }
    return null;
  }

  async function save() {
    if (!draft || !draft.questions.length) return;
    const problem = validateDraft();
    if (problem) {
      UI.toast(problem, 'error');
      return;
    }
    draft.title = $('quizTitle').value.trim() || 'Untitled quiz';
    try {
      await UI.busy($('saveButton'), () => UI.api('/api/quizzes', { method: 'POST', body: draft }));
      UI.toast(`Saved “${draft.title}”`, 'success');
      resetEditor();
      selectTab('panelQuizzes');
    } catch (err) {
      handleAuthError(err);
    }
  }

  function resetEditor() {
    draft = null;
    draftDirty = false;
    $('rawText').value = '';
    $('quizTitle').value = '';
    $('parseMessages').hidden = true;
    renderPreview();
  }

  async function clearEditor() {
    if (hasUnsavedWork() || draft) {
      const ok = await UI.confirm({
        title: 'Clear this quiz?',
        message: 'The text and the preview will be cleared. This quiz hasn\'t been saved.',
        confirmText: 'Clear',
        danger: true,
      });
      if (!ok) return;
    }
    resetEditor();
  }

  async function insertExample() {
    if ($('rawText').value.trim()) {
      const ok = await UI.confirm({
        title: 'Replace your text?',
        message: 'The example will replace what\'s in the questions box.',
        confirmText: 'Replace',
      });
      if (!ok) return;
    }
    $('rawText').value = EXAMPLE;
    if (!$('quizTitle').value.trim()) $('quizTitle').value = 'Example quiz';
    $('rawText').focus();
  }

  // ---------------------------------------------------------------------------
  // Quiz library
  // ---------------------------------------------------------------------------
  async function loadQuizzes() {
    const list = $('quizList');
    try {
      const quizzes = await UI.api('/api/quizzes');
      $('quizCount').textContent = quizzes.length;
      if (!quizzes.length) {
        list.innerHTML = '<div class="empty" style="grid-column: 1 / -1;"><h3>No quizzes yet</h3><p>Create one in the New quiz tab.</p></div>';
        return;
      }
      list.innerHTML = quizzes.map(q => `
        <div class="card quiz-card">
          <h3>${UI.escape(q.title)}</h3>
          <p class="subtle">${q.questions.length} question${q.questions.length === 1 ? '' : 's'} · ${UI.stamp(q.createdAt)}${q.createdBy ? ` · ${UI.escape(q.createdBy)}` : ''}</p>
          <div class="row mt-4">
            <button type="button" class="btn btn-secondary btn-sm" data-start="${UI.escape(q.id)}">${UI.icon('play')}Start a game</button>
            <span class="grow"></span>
            <button type="button" class="btn btn-danger-ghost btn-sm" data-delete="${UI.escape(q.id)}" data-title="${UI.escape(q.title)}">Delete</button>
          </div>
        </div>`).join('');
      list.querySelectorAll('[data-start]').forEach(b => { b.onclick = () => startGame(b, b.dataset.start); });
      list.querySelectorAll('[data-delete]').forEach(b => { b.onclick = () => deleteQuiz(b.dataset.delete, b.dataset.title); });
    } catch (err) {
      list.innerHTML = `<p class="muted">${UI.escape(err.message)}</p>`;
    }
  }

  async function startGame(button, quizId) {
    try {
      const data = await UI.busy(button, () => UI.api('/api/sessions', { method: 'POST', body: { quizId, autoAdvance: false } }));
      window.location.href = `/host.html?pin=${encodeURIComponent(data.pin)}`;
    } catch (err) {
      handleAuthError(err);
    }
  }

  async function deleteQuiz(id, title) {
    const ok = await UI.confirm({
      title: 'Delete this quiz?',
      html: `<p><strong>${UI.escape(title)}</strong> will be permanently deleted. Games already running with it aren't affected. This can't be undone.</p>`,
      confirmText: 'Delete quiz',
      danger: true,
    });
    if (!ok) return;
    try {
      await UI.api(`/api/quizzes/${encodeURIComponent(id)}`, { method: 'DELETE' });
      UI.toast(`Deleted “${title}”`, 'success');
      loadQuizzes();
    } catch (err) {
      handleAuthError(err);
    }
  }

  // ---------------------------------------------------------------------------
  // Leaderboard
  // ---------------------------------------------------------------------------
  async function loadLeaderboard() {
    const tbody = $('lbTable');
    const search = $('lbSearch').value.trim();
    try {
      const players = await UI.api(`/api/leaderboard?limit=200&search=${encodeURIComponent(search)}`);
      if (!players.length) {
        tbody.innerHTML = `<tr><td colspan="8" class="empty-row">${search ? 'No players match your search.' : 'No scores yet. They appear here after a game finishes.'}</td></tr>`;
        return;
      }
      tbody.innerHTML = players.map((p, i) => `
        <tr>
          <td>${UI.rankBadge(i + 1)}</td>
          <td><div class="player-cell">${UI.avatar(p.fullName)}${UI.escape(p.fullName)}</div></td>
          <td class="hide-sm muted">${UI.escape(p.identifier || '—')}</td>
          <td class="right score">${UI.formatNumber(p.totalPointsAllTime)}</td>
          <td class="right hide-sm num">${p.quizzesPlayed || 0}</td>
          <td class="right num">${Math.round(p.accuracyPercentage || 0)}%</td>
          <td class="right hide-sm num">${p.highestStreak || 0}</td>
          <td class="hide-sm muted">${UI.stamp(p.lastActive)}</td>
        </tr>`).join('');
    } catch (err) {
      tbody.innerHTML = `<tr><td colspan="8" class="empty-row">${UI.escape(err.message)}</td></tr>`;
    }
  }

  async function resetLeaderboard() {
    const ok = await UI.confirm({
      title: 'Reset all scores?',
      message: 'Every player\'s quick-game points, games played and streaks will be permanently erased. Tournament standings aren\'t affected, and neither are exports you\'ve downloaded. This can\'t be undone.',
      confirmText: 'Reset all scores',
      danger: true,
      requireText: 'reset',
    });
    if (!ok) return;
    try {
      await UI.api('/api/leaderboard/reset', { method: 'POST' });
      UI.toast('Scores reset', 'success');
      loadLeaderboard();
    } catch (err) {
      handleAuthError(err);
    }
  }

  async function exportLeaderboard(kind, button) {
    try {
      await UI.busy(button, () => UI.download(`/api/leaderboard/export/${kind}`, `Groove_Leaderboard.${kind === 'csv' ? 'csv' : 'xls'}`));
    } catch (err) {
      handleAuthError(err);
    }
  }

  // ---------------------------------------------------------------------------
  // People
  // ---------------------------------------------------------------------------
  async function loadPeople() {
    try {
      people = await UI.api('/api/auth/users');
      renderPeople();
    } catch (err) {
      $('peopleTable').innerHTML = `<tr><td colspan="4" class="empty-row">${UI.escape(err.message)}</td></tr>`;
    }
  }

  function renderPeople() {
    const me = (Session.get() || {}).email;
    const q = $('peopleSearch').value.trim().toLowerCase();
    const rows = people.filter(u => !q || u.email.toLowerCase().includes(q) || (u.displayName || '').toLowerCase().includes(q));
    if (!rows.length) {
      $('peopleTable').innerHTML = `<tr><td colspan="4" class="empty-row">${q ? 'No one matches your search.' : 'No accounts yet.'}</td></tr>`;
      return;
    }
    $('peopleTable').innerHTML = rows.map(u => `
      <tr>
        <td><div class="player-cell">${UI.avatar(u.displayName || u.email)}${UI.escape(u.displayName || '—')}${u.email === me ? ' <span class="badge">You</span>' : ''}</div></td>
        <td class="muted">${UI.escape(u.email)}</td>
        <td>
          <label class="sr-only" for="role-${UI.escape(u.uid)}">Role for ${UI.escape(u.email)}</label>
          <select class="select input-sm" style="width: 120px;" id="role-${UI.escape(u.uid)}" data-email="${UI.escape(u.email)}" data-current="${UI.escape(u.role)}">
            ${['Player', 'Host', 'Admin'].map(r => `<option ${r === u.role ? 'selected' : ''}>${r}</option>`).join('')}
          </select>
        </td>
        <td class="hide-sm muted">${UI.stamp(u.createdAt)}</td>
      </tr>`).join('');
    $('peopleTable').querySelectorAll('select').forEach(sel => { sel.onchange = () => changeRole(sel); });
  }

  async function changeRole(select) {
    const email = select.dataset.email;
    const from = select.dataset.current;
    const to = select.value;
    const me = (Session.get() || {}).email;
    const isSelf = email === me;

    let message = `${email} will change from ${from} to ${to}.`;
    if (to === 'Admin') message += ' Admins can manage quizzes, erase scores, and change anyone\'s role.';
    if (isSelf && from === 'Admin' && to !== 'Admin') message += ' You will lose access to this page immediately.';

    const ok = await UI.confirm({
      title: isSelf ? 'Change your own role?' : `Make this person ${to === 'Admin' ? 'an' : 'a'} ${to}?`,
      message,
      confirmText: `Make ${to}`,
      danger: to === 'Admin' || (isSelf && from === 'Admin'),
    });
    if (!ok) {
      select.value = from;
      return;
    }
    try {
      await UI.api('/api/auth/assign-role', { method: 'POST', body: { email, role: to } });
      UI.toast(`${email} is now ${to}`, 'success');
      if (isSelf) {
        const s = Session.get();
        Session.set(Object.assign({}, s, { role: to }));
        return;
      }
      loadPeople();
    } catch (err) {
      select.value = from;
      handleAuthError(err);
    }
  }

  // ---------------------------------------------------------------------------
  // Boot
  // ---------------------------------------------------------------------------
  document.addEventListener('DOMContentLoaded', () => {
    $('gateSignIn').onclick = signIn;
    $('switchAccount').onclick = async () => {
      Session.clear();
      await signIn();
    };

    document.querySelectorAll('.tab').forEach(t => { t.onclick = () => selectTab(t.dataset.tab); });
    document.querySelectorAll('[data-goto]').forEach(b => { b.onclick = () => selectTab(b.dataset.goto); });

    $('parseButton').onclick = parse;
    $('clearButton').onclick = clearEditor;
    $('insertExample').onclick = insertExample;
    $('saveButton').onclick = save;
    $('preview').addEventListener('input', onPreviewInput);
    $('preview').addEventListener('change', onPreviewInput);
    $('preview').addEventListener('click', onPreviewClick);

    $('lbSearch').addEventListener('input', () => {
      clearTimeout(searchTimer);
      searchTimer = setTimeout(loadLeaderboard, 300);
    });
    $('resetLeaderboard').onclick = resetLeaderboard;
    $('exportCsv').onclick = e => exportLeaderboard('csv', e.currentTarget);
    $('exportExcel').onclick = e => exportLeaderboard('excel', e.currentTarget);
    $('peopleSearch').addEventListener('input', renderPeople);
    $('refreshGames').onclick = loadGames;

    document.querySelectorAll('[data-guard-nav]').forEach(a => {
      a.addEventListener('click', async e => {
        if (!hasUnsavedWork()) return;
        e.preventDefault();
        const ok = await UI.confirm({
          title: 'Leave without saving?',
          message: 'Your quiz hasn\'t been saved and will be lost.',
          confirmText: 'Leave',
          cancelText: 'Keep editing',
          danger: true,
        });
        if (ok) {
          draftDirty = false;
          $('rawText').value = '';
          window.location.href = a.href;
        }
      });
    });

    window.addEventListener('beforeunload', e => {
      if (hasUnsavedWork()) {
        e.preventDefault();
        e.returnValue = '';
      }
    });

    Session.onChange(route);
    route();
  });
})();
