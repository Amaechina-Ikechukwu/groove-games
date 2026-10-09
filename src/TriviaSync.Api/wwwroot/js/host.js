// Groove host screen
(function () {
  'use strict';

  const $ = id => document.getElementById(id);

  let connection = null;
  let pin = '';
  let room = null;            // last RoomState
  let state = '';             // current game state name
  let players = [];           // connected players
  let question = null;        // current QuestionStarted payload
  let questionTotal = 20;
  let answeredCount = 0;
  let attaching = false;
  let countdownTimer = null;

  const IN_PROGRESS = ['QuestionCountdown', 'QuestionActive', 'AnswerReveal', 'RoundLeaderboard'];

  // ---------------------------------------------------------------------------
  // Views
  // ---------------------------------------------------------------------------
  function showView(id) {
    document.querySelectorAll('.view').forEach(v => v.classList.toggle('is-active', v.id === id));
    window.scrollTo(0, 0);
  }

  function showStage(id) {
    document.querySelectorAll('.game-stage').forEach(s => s.classList.toggle('is-active', s.id === id));
  }

  const inGameView = () => $('viewGame').classList.contains('is-active');
  const gameNeedsHost = () => inGameView() && (IN_PROGRESS.includes(state) || (state === 'Lobby' && players.length > 0));

  function setUrl(params) {
    const url = new URL(window.location.href);
    url.search = '';
    Object.entries(params || {}).forEach(([k, v]) => v && url.searchParams.set(k, v));
    history.replaceState(null, '', url);
  }

  function renderAccount() {
    UI.renderAccount($('account'), {
      onSignIn: () => openSignIn('signin'),
      onSignOut: async () => {
        const warning = gameNeedsHost()
          ? "Your game keeps running, but you won't be able to control it until you sign back in."
          : null;
        await UI.signOut({ warning });
      },
    });
  }

  async function openSignIn(mode) {
    const session = await UI.signIn({
      title: mode === 'signup' ? 'Create a host account' : 'Sign in to host',
      signupRole: 'Host',
      startWith: mode,
    });
    if (session) UI.toast(`Signed in as ${session.displayName || session.email}`, 'success');
  }

  function route() {
    renderAccount();
    const session = Session.get();
    if (!session) return showView('viewGate');
    if (!Session.canHost(session)) {
      $('upgradeEmail').textContent = session.email;
      return showView('viewUpgrade');
    }

    const params = new URLSearchParams(window.location.search);
    const pinParam = params.get('pin');

    if (pinParam) {
      attach(pinParam.trim().toUpperCase());
    } else {
      showSetup();
    }
  }

  // ---------------------------------------------------------------------------
  // Upgrade player -> host
  // ---------------------------------------------------------------------------
  async function becomeHost() {
    const s = Session.get();
    const ok = await UI.confirm({
      title: 'Switch to a host account?',
      html: `<p><strong>${UI.escape(s.email)}</strong> will be able to create and run games. You can still join games as a player.</p>`,
      confirmText: 'Become a host',
    });
    if (!ok) return;
    try {
      const auth = await UI.busy($('upgradeButton'), () => UI.api('/api/auth/become-host', { method: 'POST' }));
      Session.set(auth);
      UI.toast('You can host games now', 'success');
    } catch (err) {
      UI.toast(err.message, 'error');
    }
  }

  // ---------------------------------------------------------------------------
  // Setup
  // ---------------------------------------------------------------------------
  function showSetup() {
    setUrl({});
    showView('viewSetup');
    loadTournaments();
    loadQuizzes();
    loadRunning();
  }

  async function loadQuizzes() {
    const select = $('quizSelect');
    const hint = $('quizHint');
    try {
      const quizzes = await UI.api('/api/quizzes');
      if (!quizzes.length) {
        select.innerHTML = '<option value="">No quizzes yet</option>';
        select.disabled = true;
        hint.innerHTML = Session.isAdmin()
          ? 'Add one in <a href="/admin.html">Admin</a> first.'
          : 'Ask an admin to add a quiz first.';
        $('createButton').disabled = true;
        return;
      }
      const previous = select.value;
      select.disabled = false;
      $('createButton').disabled = false;
      select.innerHTML = quizzes.map(q =>
        `<option value="${UI.escape(q.id)}">${UI.escape(q.title)} · ${q.questions.length} question${q.questions.length === 1 ? '' : 's'}</option>`).join('');
      if (previous && quizzes.some(q => q.id === previous)) select.value = previous;
      hint.textContent = '';
    } catch (err) {
      select.innerHTML = '<option value="">Couldn\'t load quizzes</option>';
      hint.textContent = err.message;
      if (err.status === 401) route();
    }
  }

  const STATE_LABELS = {
    Lobby: ['Waiting for players', 'badge-accent'],
    QuestionCountdown: ['In progress', 'badge-success'],
    QuestionActive: ['In progress', 'badge-success'],
    AnswerReveal: ['In progress', 'badge-success'],
    RoundLeaderboard: ['In progress', 'badge-success'],
    GameEnded: ['Finished', ''],
  };

  async function loadRunning() {
    const list = $('runningList');
    try {
      const sessions = await UI.api('/api/sessions');
      if (!sessions.length) {
        list.innerHTML = '<div class="empty"><p>No games running. Games you create show up here so you can get back to them.</p></div>';
        return;
      }
      list.innerHTML = sessions.map(s => {
        const [label, cls] = STATE_LABELS[s.state] || [s.state, ''];
        const round = s.sessionType === 'Tournament' ? `${s.tournamentName} · ` : '';
        return `
          <div class="running-item">
            <span class="pin">${UI.escape(s.pin)}</span>
            <div class="meta">
              <div style="font-weight: 600;">${UI.escape(s.quizTitle)}</div>
              <div class="subtle">${UI.escape(round)}${s.connectedPlayers} player${s.connectedPlayers === 1 ? '' : 's'} · <span class="badge ${cls}">${label}</span></div>
            </div>
            <button type="button" class="btn btn-secondary btn-sm" data-open="${UI.escape(s.pin)}">Open</button>
            <button type="button" class="btn btn-ghost btn-icon btn-sm" data-end="${UI.escape(s.pin)}" data-state="${UI.escape(s.state)}" data-players="${s.connectedPlayers}" aria-label="End game ${UI.escape(s.pin)}" title="End game">${UI.icon('trash')}</button>
          </div>`;
      }).join('');
      list.querySelectorAll('[data-open]').forEach(b => { b.onclick = () => attach(b.dataset.open); });
      list.querySelectorAll('[data-end]').forEach(b => {
        b.onclick = async () => {
          if (await endGame(b.dataset.end, b.dataset.state, Number(b.dataset.players))) loadRunning();
        };
      });
    } catch (err) {
      list.innerHTML = `<p class="subtle">${UI.escape(err.message)}</p>`;
    }
  }

  async function createGame(e) {
    e.preventDefault();
    const quizId = $('quizSelect').value;
    if (!quizId) {
      UI.toast('Pick a quiz first.', 'error');
      return;
    }
    const body = { quizId, autoAdvance: $('autoAdvance').checked, sessionType: 'Single' };

    try {
      const data = await UI.busy($('createButton'), () => UI.api('/api/sessions', { method: 'POST', body }));
      attach(data.pin);
    } catch (err) {
      UI.toast(err.message, 'error');
      if (err.status === 401) route();
    }
  }

  // ---------------------------------------------------------------------------
  // Tournaments
  // ---------------------------------------------------------------------------
  async function loadTournaments() {
    const list = $('tournamentList');
    const admin = Session.isAdmin();
    $('tournamentsHeading').textContent = admin ? 'All tournaments' : 'Your tournaments';
    try {
      const items = await UI.api('/api/tournaments/hosting');
      if (!items.length) {
        list.innerHTML = '<div class="empty" style="grid-column: 1 / -1;"><h3>No tournaments yet</h3><p>Create one, share its code, then add live games or self-paced sessions.</p></div>';
        return;
      }
      list.innerHTML = items.map(t => `
        <div class="card t-card">
          <div class="spread mb-1">
            <h3><a href="/tournament.html?id=${encodeURIComponent(t.id)}" style="color: inherit; text-decoration: none;">${UI.escape(t.name)}</a></h3>
            ${t.openCount ? `<span class="badge badge-live">${t.openCount} open</span>` : ''}
          </div>
          <p class="subtle">${admin ? `${UI.escape(t.hostName)} · ` : ''}${t.memberCount} player${t.memberCount === 1 ? '' : 's'} · ${t.sessionCount} session${t.sessionCount === 1 ? '' : 's'}</p>
          <div class="row mt-4">
            <a class="btn btn-secondary btn-sm" href="/tournament.html?id=${encodeURIComponent(t.id)}">Manage</a>
            <span class="grow"></span>
            <button type="button" class="btn btn-danger-ghost btn-sm" data-delete="${UI.escape(t.id)}">${UI.icon('trash')}Delete</button>
          </div>
        </div>`).join('');
      list.querySelectorAll('[data-delete]').forEach(b => {
        b.onclick = () => deleteTournament(items.find(t => t.id === b.dataset.delete));
      });
    } catch (err) {
      list.innerHTML = `<p class="subtle">${UI.escape(err.message)}</p>`;
    }
  }

  async function deleteTournament(t) {
    const parts = [];
    if (t.sessionCount) parts.push(`${t.sessionCount} session${t.sessionCount === 1 ? '' : 's'} and their results`);
    if (t.memberCount) parts.push(`${t.memberCount} player membership${t.memberCount === 1 ? '' : 's'}`);
    const ok = await UI.confirm({
      title: `Delete ${t.name}?`,
      message: `${parts.length ? `This permanently deletes ${parts.join(' and ')}. ` : ''}Any live game in it ends for everyone. This can't be undone.`,
      confirmText: 'Delete tournament',
      danger: true,
      requireText: 'delete',
    });
    if (!ok) return;
    try {
      await UI.api(`/api/tournaments/${encodeURIComponent(t.id)}`, { method: 'DELETE' });
      UI.toast(`Deleted ${t.name}`, 'success');
      loadTournaments();
      loadRunning();
    } catch (err) {
      UI.toast(err.message, 'error');
    }
  }

  function newTournament() {
    UI.openDialog({
      labelledBy: 'newTitle',
      render(el, close) {
        el.innerHTML = `
          <h2 class="dialog-title" id="newTitle">New tournament</h2>
          <p class="dialog-body">You'll get a code to share. Players who join can play its sessions and see their scores add up.</p>
          <form class="stack mt-4" novalidate>
            <div class="field"><label class="label" for="ntName">Name</label>
              <input class="input" id="ntName" maxlength="80" placeholder="e.g. Friday league, spring term"></div>
            <div class="field"><label class="label" for="ntDesc">Description <span class="subtle">(optional)</span></label>
              <textarea class="textarea" id="ntDesc" maxlength="300" style="min-height: 80px;"></textarea></div>
            <div class="dialog-actions">
              <button type="button" class="btn btn-secondary" data-cancel>Cancel</button>
              <button type="submit" class="btn btn-primary">Create tournament</button>
            </div>
          </form>`;
        el.querySelector('[data-cancel]').onclick = () => close();
        el.querySelector('#ntName').setAttribute('autofocus', '');
        el.querySelector('form').onsubmit = async e => {
          e.preventDefault();
          const name = el.querySelector('#ntName').value.trim();
          if (name.length < 2) {
            el.querySelector('#ntName').setAttribute('aria-invalid', 'true');
            return UI.toast('Give the tournament a name.', 'error');
          }
          try {
            const t = await UI.busy(el.querySelector('[type=submit]'), () => UI.api('/api/tournaments', {
              method: 'POST', body: { name, description: el.querySelector('#ntDesc').value },
            }));
            close();
            location.href = `/tournament.html?id=${encodeURIComponent(t.id)}`;
          } catch (err) {
            UI.toast(err.message, 'error');
          }
        };
      },
    });
  }

  // ---------------------------------------------------------------------------
  // Connection & attaching to a game
  // ---------------------------------------------------------------------------
  function ensureConnection() {
    if (connection) return connection;
    connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/game', { accessTokenFactory: () => (Session.get() || {}).token || '' })
      .withAutomaticReconnect([0, 1000, 2000, 5000, 10000, 15000])
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    connection.on('RoomState', onRoomState);
    connection.on('PlayerJoined', onPlayerJoined);
    connection.on('PlayerLeft', onPlayerLeft);
    connection.on('QuestionCountdown', onQuestionCountdown);
    connection.on('QuestionStarted', onQuestionStarted);
    connection.on('TimerTick', onTimerTick);
    connection.on('AnswerReceived', onAnswerReceived);
    connection.on('RoundCompleted', onRoundCompleted);
    connection.on('LeaderboardUpdate', onLeaderboardUpdate);
    connection.on('GameEnded', onGameEnded);
    connection.on('SessionClosed', () => {});
    connection.on('ErrorNotification', onError);

    connection.onreconnecting(() => { if (inGameView()) UI.toast('Connection lost. Reconnecting…'); });
    connection.onreconnected(() => {
      if (pin && inGameView()) {
        connection.invoke('HostJoin', pin).catch(() => {});
        UI.toast('Reconnected', 'success');
      }
    });
    return connection;
  }

  async function attach(targetPin) {
    pin = targetPin;
    attaching = true;
    const conn = ensureConnection();
    try {
      if (conn.state === signalR.HubConnectionState.Disconnected) {
        await conn.start();
      }
      await conn.invoke('HostJoin', pin);
    } catch (err) {
      console.error(err);
      attaching = false;
      UI.toast("Couldn't connect to the game server.", 'error');
      showSetup();
    }
  }

  function leaveGame() {
    if (connection && connection.state !== signalR.HubConnectionState.Disconnected) {
      connection.stop().catch(() => {});
    }
    pin = '';
    state = '';
    room = null;
    players = [];
  }

  async function backFromGame() {
    if (gameNeedsHost()) {
      const ok = await UI.confirm({
        title: 'Leave the host screen?',
        message: room && room.autoAdvance
          ? "The game keeps running on its own. You can reopen it from Your running games."
          : "The game keeps running, but it won't move on until you come back. You can reopen it from Your running games.",
        confirmText: 'Leave',
        cancelText: 'Stay',
      });
      if (!ok) return;
    }
    returnFromGame();
  }

  function returnFromGame() {
    const tournamentGame = room && room.sessionType === 'Tournament' ? room.tournamentId : null;
    leaveGame();
    if (tournamentGame) location.href = `/tournament.html?id=${encodeURIComponent(tournamentGame)}`;
    else showSetup();
  }

  function onError(message) {
    if (attaching) {
      attaching = false;
      UI.toast(message, 'error');
      leaveGame();
      showSetup();
      return;
    }
    UI.toast(message, 'error');
  }

  // ---------------------------------------------------------------------------
  // Game HUD
  // ---------------------------------------------------------------------------
  function joinLink(p) {
    return `${window.location.origin}/player.html?pin=${encodeURIComponent(p)}`;
  }

  function renderQr(containerId, p, size) {
    const el = $(containerId);
    if (!el || typeof QRCode === 'undefined') return;
    el.innerHTML = '';
    new QRCode(el, { text: joinLink(p), width: size, height: size, colorDark: '#000000', colorLight: '#ffffff', correctLevel: QRCode.CorrectLevel.M });
  }

  const isLastQuestion = () => room && question && question.questionNumber >= room.totalQuestions;

  function updateHud() {
    $('hudPlayers').lastElementChild.textContent = `${players.length} player${players.length === 1 ? '' : 's'}`;
    const primary = $('hudPrimary');
    const end = $('hudEnd');
    primary.hidden = false;
    primary.disabled = false;
    end.textContent = 'End game';

    switch (state) {
      case 'Lobby':
        primary.textContent = 'Start game';
        break;
      case 'QuestionCountdown':
        primary.textContent = 'Starting…';
        primary.disabled = true;
        break;
      case 'QuestionActive':
        primary.textContent = 'End question';
        break;
      case 'AnswerReveal':
      case 'RoundLeaderboard':
        primary.textContent = isLastQuestion() ? 'Show final results' : 'Next question';
        break;
      case 'GameEnded':
        primary.hidden = true;
        end.textContent = 'Close game';
        break;
    }
  }

  async function onPrimary() {
    if (window.sounds) window.sounds.init();
    try {
      if (state === 'Lobby') {
        if (players.length === 0) {
          const ok = await UI.confirm({
            title: 'Nobody has joined yet',
            message: 'Start anyway? Players can still join, but they will miss any questions that have already been asked.',
            confirmText: 'Start anyway',
          });
          if (!ok) return;
        }
        await connection.invoke('StartQuiz', pin);
      } else if (state === 'QuestionActive') {
        if (answeredCount < players.length) {
          const ok = await UI.confirm({
            title: 'End this question now?',
            message: `${answeredCount} of ${players.length} ${players.length === 1 ? 'player has' : 'players have'} answered. Anyone who hasn't answered gets no points for this question.`,
            confirmText: 'End question',
          });
          if (!ok || state !== 'QuestionActive') return;
        }
        await connection.invoke('EndQuestion', pin);
      } else if (state === 'AnswerReveal' || state === 'RoundLeaderboard') {
        $('hudPrimary').disabled = true;
        await connection.invoke('AdvanceQuestion', pin);
      }
    } catch (err) {
      console.error(err);
      UI.toast("That didn't go through. Check your connection and try again.", 'error');
      updateHud();
    }
  }

  /** Ends (closes) a game after confirmation. Returns true if it was closed. */
  async function endGame(targetPin, targetState, playerCount) {
    const finished = targetState === 'GameEnded';
    const ok = await UI.confirm(finished
      ? {
          title: 'Close this game?',
          message: 'The PIN will stop working and the game will disappear from your list. Final scores are already saved.',
          confirmText: 'Close game',
        }
      : {
          title: 'End this game for everyone?',
          message: `${playerCount === 1 ? 'The player in this game will be sent out. ' : playerCount ? `All ${playerCount} players will be sent out of the game. ` : ''}Scores from a game that hasn't finished are not added to the standings. This can't be undone.`,
          confirmText: 'End game',
          danger: true,
        });
    if (!ok) return false;
    try {
      await UI.api(`/api/sessions/${encodeURIComponent(targetPin)}`, { method: 'DELETE' });
      UI.toast(finished ? 'Game closed' : 'Game ended', 'success');
      return true;
    } catch (err) {
      UI.toast(err.message, 'error');
      return false;
    }
  }

  async function endCurrentGame() {
    if (await endGame(pin, state, players.length)) {
      returnFromGame();
    }
  }

  function openQrDialog() {
    UI.openDialog({
      labelledBy: 'qrTitle',
      render(el, close) {
        el.innerHTML = `
          <div class="dialog-head">
            <h2 class="dialog-title" id="qrTitle">Scan to join</h2>
            <button class="btn btn-ghost btn-icon btn-sm" data-close aria-label="Close">${UI.icon('x')}</button>
          </div>
          <div class="text-center">
            <div class="qr-box" id="qrDialogCode"></div>
            <p class="muted mt-4">or go to <strong>${UI.escape(window.location.host)}</strong> and enter</p>
            <div class="pin mt-2" style="font-size: 3rem;">${UI.escape(pin)}</div>
          </div>
          <div class="dialog-actions"><button class="btn btn-secondary" data-copy>${UI.icon('link')}Copy join link</button></div>`;
        el.querySelector('[data-close]').onclick = () => close();
        el.querySelector('[data-copy]').onclick = () => UI.copyText(joinLink(pin), 'Join link copied');
        renderQr('qrDialogCode', pin, 260);
      },
    });
  }

  // ---------------------------------------------------------------------------
  // Players
  // ---------------------------------------------------------------------------
  function renderPlayers() {
    $('lobbyCount').textContent = players.length;
    const list = $('lobbyPlayers');
    if (!players.length) {
      list.innerHTML = '<div class="empty" style="width: 100%;"><p>Waiting for players to join…</p></div>';
    } else {
      list.innerHTML = players.map(p => `
        <span class="chip">${UI.escape(p.fullName)}
          <button type="button" class="chip-remove" data-kick="${UI.escape(p.playerId)}" aria-label="Remove ${UI.escape(p.fullName)}">${UI.icon('x')}</button>
        </span>`).join('');
      list.querySelectorAll('[data-kick]').forEach(b => { b.onclick = () => kick(b.dataset.kick); });
    }
    updateHud();
  }

  async function kick(playerId) {
    const p = players.find(x => x.playerId === playerId);
    const name = p ? p.fullName : 'this player';
    const ok = await UI.confirm({
      title: `Remove ${name}?`,
      message: 'They will be sent out of the game. They can rejoin with the PIN unless you end the game.',
      confirmText: 'Remove',
      danger: true,
    });
    if (!ok) return;
    try {
      await connection.invoke('KickPlayer', pin, playerId);
      players = players.filter(x => x.playerId !== playerId);
      renderPlayers();
      UI.toast(`${name} removed`);
    } catch (_) {
      UI.toast(`Couldn't remove ${name}.`, 'error');
    }
  }

  // ---------------------------------------------------------------------------
  // Hub events
  // ---------------------------------------------------------------------------
  function onRoomState(r) {
    attaching = false;
    room = r;
    pin = r.pin;
    state = r.state;
    players = r.allPlayers || [];
    setUrl({ pin });
    showView('viewGame');

    $('hudPin').textContent = r.pin;
    $('lobbyPin').textContent = r.pin;
    $('joinUrl').textContent = window.location.host;
    $('hudTitle').textContent = r.title;
    $('hudSub').textContent = r.sessionType === 'Tournament'
      ? `${r.tournamentName} · ${r.totalQuestions} questions · players sign in to play`
      : `${r.totalQuestions} questions`;
    $('afterGame').textContent = r.sessionType === 'Tournament' ? 'Back to tournament' : 'Back to games';
    renderQr('lobbyQr', r.pin, 200);
    renderPlayers();

    const stageFor = {
      Lobby: 'stageLobby', QuestionCountdown: 'stageCountdown', QuestionActive: 'stageQuestion',
      AnswerReveal: 'stageReveal', RoundLeaderboard: 'stageStandings', GameEnded: 'stagePodium',
    };
    showStage(stageFor[r.state] || 'stageLobby');
    if (r.state === 'AnswerReveal' || r.state === 'RoundLeaderboard') {
      question = { questionNumber: r.currentQuestionIndex + 1 };
      $('standingsList').innerHTML = '<p class="muted">Standings will show after the next question.</p>';
      $('revealText').textContent = 'Waiting for the next question';
      $('revealAnswers').innerHTML = '';
    }
    if (r.state === 'GameEnded') {
      $('podium').innerHTML = '';
      $('finalTable').innerHTML = '<tr><td colspan="3" class="empty-row">This game has finished. Results are in the standings.</td></tr>';
    }
    updateHud();
  }

  function onPlayerJoined(data) {
    if (data.allPlayers) players = data.allPlayers;
    renderPlayers();
    if (window.sounds) window.sounds.click();
  }

  function onPlayerLeft(data) {
    players = players.filter(p => p.playerId !== data.playerId);
    renderPlayers();
  }

  function onQuestionCountdown(data) {
    state = 'QuestionCountdown';
    question = { questionNumber: data.questionIndex };
    showStage('stageCountdown');
    $('countdownLabel').textContent = `Question ${data.questionIndex} of ${data.totalQuestions}`;
    let n = data.countdownSeconds;
    $('countdownNumber').textContent = n;
    if (window.sounds) window.sounds.tick();
    clearInterval(countdownTimer);
    countdownTimer = setInterval(() => {
      n--;
      if (n > 0) {
        $('countdownNumber').textContent = n;
        if (window.sounds) window.sounds.tick();
      } else {
        clearInterval(countdownTimer);
      }
    }, 1000);
    updateHud();
  }

  function setTimer(remaining) {
    const t = $('questionTimer');
    t.querySelector('span').textContent = remaining;
    t.style.setProperty('--p', Math.max(0, Math.min(1, remaining / (questionTotal || 1))));
    t.classList.toggle('is-urgent', remaining <= 5);
  }

  function renderAnswers(container, choices, { correctIndex = -1, counts = null } = {}) {
    container.innerHTML = choices.map((c, i) => {
      const revealed = correctIndex >= 0;
      const isCorrect = i === correctIndex;
      const cls = revealed ? (isCorrect ? 'is-correct' : 'is-dimmed') : '';
      const count = counts ? `<span class="answer-count">${counts[i] || 0}</span>` : '';
      const mark = isCorrect ? `<span class="answer-mark">${UI.icon('check')}</span>` : '';
      return `<div class="answer answer-${i % 6} ${cls}">${UI.shape(i)}<span class="answer-text">${UI.escape(c)}</span>${count}${mark}</div>`;
    }).join('');
  }

  function onQuestionStarted(data) {
    clearInterval(countdownTimer);
    state = 'QuestionActive';
    question = data;
    questionTotal = data.timeLimit;
    answeredCount = data.answeredCount || 0;
    showStage('stageQuestion');

    $('questionLabel').textContent = `Question ${data.questionNumber} of ${data.totalQuestions}`;
    $('questionText').textContent = data.text;
    $('answeredCount').textContent = `${answeredCount} of ${players.length} answered`;
    setTimer(data.timeLimit);
    renderAnswers($('questionAnswers'), data.choices);
    updateHud();
  }

  function onTimerTick(data) {
    if (state !== 'QuestionActive') return;
    setTimer(data.remainingSeconds);
    if (window.sounds) data.remainingSeconds <= 5 ? window.sounds.hurryTick() : window.sounds.tick();
  }

  function onAnswerReceived(data) {
    answeredCount = data.totalAnswers;
    $('answeredCount').textContent = `${data.totalAnswers} of ${data.totalPlayers} answered`;
  }

  function onRoundCompleted(data) {
    state = 'AnswerReveal';
    showStage('stageReveal');
    const total = data.totalAnswers || 0;
    const correct = (data.stats || [])[data.correctIndex] || 0;
    $('revealLabel').textContent = question ? `Question ${question.questionNumber} of ${question.totalQuestions}` : '';
    $('revealStats').textContent = total ? `${correct} of ${total} got it right` : 'No one answered';
    $('revealText').textContent = question ? question.text : '';
    if (question && question.choices) {
      renderAnswers($('revealAnswers'), question.choices, { correctIndex: data.correctIndex, counts: data.stats });
    }
    if (window.sounds) window.sounds.correct();
    updateHud();
  }

  function onLeaderboardUpdate(data) {
    state = 'RoundLeaderboard';
    question = Object.assign({}, question, { questionNumber: data.questionNumber, totalQuestions: data.totalQuestions });
    showStage('stageStandings');
    $('standingsLabel').textContent = `After question ${data.questionNumber} of ${data.totalQuestions}`;
    const list = $('standingsList');
    const top = data.topPlayers || [];
    list.innerHTML = top.length ? top.map((p, i) => `
      <div class="standing" style="animation-delay: ${i * 40}ms">
        ${UI.rankBadge(p.rank || i + 1)}
        <div class="standing-name">${UI.escape(p.fullName)}
          ${p.streak > 1 ? `<div class="standing-meta">${UI.icon('flame')} ${p.streak} in a row</div>` : ''}
        </div>
        ${p.pointsGained > 0 ? `<span class="standing-gain">+${UI.formatNumber(p.pointsGained)}</span>` : ''}
        <span class="score">${UI.formatNumber(p.score)}</span>
      </div>`).join('') : '<p class="muted">No scores yet.</p>';
    updateHud();
  }

  function onGameEnded(data) {
    state = 'GameEnded';
    showStage('stagePodium');
    const podium = data.podium || [];
    const order = [1, 0, 2];
    $('podium').innerHTML = order.map(i => {
      const p = podium[i];
      return `
        <div class="podium-place p${i + 1} ${p ? '' : 'is-empty'}">
          <div class="podium-name">${p ? UI.escape(p.fullName) : ''}</div>
          <div class="podium-score">${p ? UI.formatNumber(p.score) + ' pts' : ''}</div>
          <div class="podium-block">${i + 1}</div>
        </div>`;
    }).join('');
    const all = data.allPlayers || [];
    $('finalTable').innerHTML = all.length ? all.map(p => `
      <tr><td>${UI.rankBadge(p.rank)}</td><td><div class="player-cell">${UI.avatar(p.fullName)}${UI.escape(p.fullName)}</div></td><td class="right score">${UI.formatNumber(p.score)}</td></tr>`).join('')
      : '<tr><td colspan="3" class="empty-row">No one played.</td></tr>';
    if (window.sounds) window.sounds.podium();
    updateHud();
  }

  async function exportResults(kind) {
    const button = kind === 'csv' ? $('exportCsv') : $('exportExcel');
    try {
      await UI.busy(button, () => UI.download(`/api/sessions/${encodeURIComponent(pin)}/export/${kind}`, `Groove_${pin}.${kind === 'csv' ? 'csv' : 'xls'}`));
    } catch (err) {
      UI.toast(err.message, 'error');
    }
  }

  // ---------------------------------------------------------------------------
  // Boot
  // ---------------------------------------------------------------------------
  document.addEventListener('DOMContentLoaded', () => {
    $('gateSignIn').onclick = () => openSignIn('signin');
    $('gateSignUp').onclick = () => openSignIn('signup');
    $('upgradeButton').onclick = becomeHost;

    $('newTournament').onclick = newTournament;
    $('setupForm').addEventListener('submit', createGame);
    $('refreshRunning').onclick = loadRunning;

    $('hudPrimary').onclick = onPrimary;
    $('hudEnd').onclick = endCurrentGame;
    $('hudQr').onclick = openQrDialog;
    $('copyLink').onclick = () => UI.copyText(joinLink(pin), 'Join link copied');
    $('exportCsv').onclick = () => exportResults('csv');
    $('exportExcel').onclick = () => exportResults('excel');
    $('afterGame').onclick = backFromGame;

    document.querySelectorAll('[data-guard-nav]').forEach(a => {
      a.addEventListener('click', async e => {
        if (!gameNeedsHost()) return;
        e.preventDefault();
        const ok = await UI.confirm({
          title: 'Leave the host screen?',
          message: "The game keeps running, but it won't move on until you come back. You can reopen it from Your running games.",
          confirmText: 'Leave',
          cancelText: 'Stay',
        });
        if (ok) window.location.href = a.href;
      });
    });

    window.addEventListener('beforeunload', e => {
      if (gameNeedsHost()) {
        e.preventDefault();
        e.returnValue = '';
      }
    });

    Session.onChange(() => {
      if (!Session.canHost()) {
        if (inGameView()) leaveGame();
        route();
      } else if (inGameView()) {
        renderAccount();
      } else {
        route();
      }
    });
    route();
  });
})();
