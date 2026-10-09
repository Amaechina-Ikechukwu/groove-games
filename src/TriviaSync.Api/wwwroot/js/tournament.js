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
    $('tHost').textContent = `Tournament · Hosted by ${info.hostName}`;
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
    buttons.push(`<button class="btn btn-ghost btn-icon btn-sm" data-act="remove" aria-label="Delete ${UI.escape(s.title)}" title="Delete session">${UI.icon('trash')}</button>`);
    return buttons.join('');
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
            <span class="subtle">${s.questionCount} questions${s.mode === 'SelfPaced' && s.closesAt && s.status !== 'Draft' ? ` · ${s.status === 'Open' ? 'closes' : 'closed'} ${UI.escape(UI.formatDateTime(s.closesAt))}` : ''}</span>
          </div>
        </div>
        <div class="session-actions">${canManage ? manageActions(s) : memberActions(s)}</div>
      </div>`).join('');

    list.querySelectorAll('[data-act]').forEach(b => {
      const s = sessions.find(x => x.id === b.closest('[data-id]').dataset.id);
      const handlers = {
        live: () => runLive(s, b), open: () => openSession(s, 'open'), extend: () => openSession(s, 'extend'),
        reopen: () => openSession(s, 'reopen'), close: () => closeSession(s), results: () => showResults(s), remove: () => deleteSession(s),
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
    if (!quizzes.length) {
      return UI.notice({ title: 'No quizzes yet', message: Session.isAdmin() ? 'Create a quiz in Admin first.' : 'Ask an admin to add a quiz first.' });
    }

    UI.openDialog({
      labelledBy: 'addTitle',
      render(el, close) {
        el.innerHTML = `
          <h2 class="dialog-title" id="addTitle">Add a session</h2>
          <form class="stack mt-4" novalidate>
            <div class="field">
              <label class="label" for="addQuiz">Quiz</label>
              <select class="select" id="addQuiz">${quizzes.map(q => `<option value="${UI.escape(q.id)}">${UI.escape(q.title)} · ${q.questions.length} questions</option>`).join('')}</select>
            </div>
            <div class="field">
              <label class="label" for="addTitleInput">Session name <span class="subtle">(optional)</span></label>
              <input class="input" id="addTitleInput" maxlength="100" placeholder="Uses the quiz title if left empty">
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
          try {
            await UI.busy(el.querySelector('[type=submit]'), () => UI.api(`/api/tournaments/${encodeURIComponent(tournamentId)}/sessions`, { method: 'POST', body }));
            close();
            UI.toast('Session added', 'success');
            refreshSessions();
          } catch (err) {
            UI.toast(err.message, 'error');
          }
        };
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
            <thead><tr><th>#</th><th>Player</th><th class="right">Score</th><th class="right">Correct</th><th>Status</th></tr></thead>
            <tbody><tr><td colspan="5" class="empty-row">Loading…</td></tr></tbody>
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
            </tr>`).join('') || '<tr><td colspan="5" class="empty-row">No one has played yet.</td></tr>';
        }).catch(err => {
          el.querySelector('tbody').innerHTML = `<tr><td colspan="5" class="empty-row">${UI.escape(err.message)}</td></tr>`;
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
          <td class="hide-sm muted">${new Date(m.joinedAt).toLocaleDateString()}</td>
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
