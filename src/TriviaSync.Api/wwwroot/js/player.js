// Groove player client
(function () {
  'use strict';

  const CURRENT_GAME_KEY = 'groove.player.game';
  const PROFILE_KEY = 'groove.player.profile';

  const $ = id => document.getElementById(id);

  let connection = null;
  let game = null;           // { pin, name, identifier } once joined
  let hostId = '';
  let questionIndex = -1;
  let answered = false;
  let timeLimit = 20;
  let countdownTimer = null;
  let stage = 'stageJoin';
  let joining = false;

  // ---------------------------------------------------------------------------
  // Stage handling
  // ---------------------------------------------------------------------------
  function show(id) {
    stage = id;
    document.querySelectorAll('.stage').forEach(el => el.classList.toggle('is-active', el.id === id));
    document.body.classList.toggle('hide-tabbar', !['stageJoin', 'stageEnded', 'stageSummary'].includes(id));
    window.scrollTo(0, 0);
  }

  const inGame = () => !!game && stage !== 'stageJoin' && stage !== 'stageEnded' && stage !== 'stageSummary';

  function setPlayerBar(name, score, streak) {
    $('playerBar').hidden = !name;
    if (name) $('barName').textContent = name;
    if (score !== undefined) $('barScore').textContent = `${UI.formatNumber(score)} pts`;
    if (streak !== undefined) {
      $('barStreak').hidden = streak < 2;
      $('barStreak').innerHTML = `${UI.icon('flame')} ${streak}`;
    }
  }

  function saveGame() {
    try { sessionStorage.setItem(CURRENT_GAME_KEY, JSON.stringify(game)); } catch (_) { /* ignore */ }
  }

  function forgetGame() {
    game = null;
    try { sessionStorage.removeItem(CURRENT_GAME_KEY); } catch (_) { /* ignore */ }
    setPlayerBar(null);
  }

  // ---------------------------------------------------------------------------
  // Join form
  // ---------------------------------------------------------------------------
  function showJoinError(message, field) {
    const box = $('joinError');
    box.innerHTML = `${UI.icon('alert')}<span>${UI.escape(message)}</span>`;
    box.hidden = false;
    if (field) {
      field.setAttribute('aria-invalid', 'true');
      field.focus();
    }
  }

  function clearJoinError() {
    $('joinError').hidden = true;
    ['joinPin', 'joinName'].forEach(id => $(id).removeAttribute('aria-invalid'));
  }

  function renderAccountLine() {
    const line = $('accountLine');
    const s = Session.get();
    if (s) {
      line.innerHTML = `Signed in as <strong>${UI.escape(s.displayName || s.email)}</strong>. <a href="#" data-signout>Sign out</a>`;
      line.querySelector('[data-signout]').onclick = async e => {
        e.preventDefault();
        await UI.signOut();
        renderAccountLine();
      };
    } else {
      line.innerHTML = `No account needed. <a href="#" data-signin>Sign in</a> to fill in your details automatically.`;
      line.querySelector('[data-signin]').onclick = async e => {
        e.preventDefault();
        const session = await UI.signIn({ title: 'Sign in', description: 'Optional. We\'ll fill in your name and email for you.', signupRole: 'Player' });
        if (session) {
          prefillFromSession(true);
          renderAccountLine();
        }
      };
    }
  }

  function prefillFromSession(overwrite) {
    const s = Session.get();
    let profile = {};
    try { profile = JSON.parse(localStorage.getItem(PROFILE_KEY) || '{}'); } catch (_) { /* ignore */ }
    const name = (s && s.displayName) || profile.name || '';
    const identifier = (s && s.email) || profile.identifier || '';
    if (overwrite || !$('joinName').value) $('joinName').value = name;
    if (overwrite || !$('joinIdentifier').value) $('joinIdentifier').value = identifier;
  }

  let connectedToken = null;

  async function ensureConnected() {
    const token = (Session.get() || {}).token || '';
    // The hub reads the token once, at connect time. Reconnect if the player signed in or out since.
    if (connection.state === signalR.HubConnectionState.Connected && connectedToken !== token) {
      await connection.stop();
    }
    if (connection.state === signalR.HubConnectionState.Connected) return;
    if (connection.state === signalR.HubConnectionState.Disconnected) {
      connectedToken = token;
      await connection.start();
    } else {
      // Connecting / reconnecting: wait briefly.
      await new Promise((resolve, reject) => {
        const started = Date.now();
        const t = setInterval(() => {
          if (connection.state === signalR.HubConnectionState.Connected) { clearInterval(t); resolve(); }
          else if (Date.now() - started > 8000) { clearInterval(t); reject(new Error('timeout')); }
        }, 200);
      });
    }
  }

  async function join(pin, name, identifier) {
    joining = true;
    game = { pin, name, identifier };
    await ensureConnected();
    await connection.invoke('JoinRoom', pin, name, identifier, 'global');
  }

  async function handleJoinSubmit(e) {
    e.preventDefault();
    clearJoinError();
    if (window.sounds) window.sounds.init();

    const pinInput = $('joinPin');
    const nameInput = $('joinName');
    const pin = pinInput.value.trim().toUpperCase();
    const name = nameInput.value.trim().replace(/\s+/g, ' ');
    const identifier = $('joinIdentifier').value.trim();

    if (pin.length !== 6) return showJoinError('Enter the 6-digit PIN from the host\'s screen.', pinInput);
    if (name.length < 2) return showJoinError('Enter your name (at least 2 characters).', nameInput);

    try { localStorage.setItem(PROFILE_KEY, JSON.stringify({ name, identifier })); } catch (_) { /* ignore */ }

    try {
      await UI.busy($('joinSubmit'), () => join(pin, name, identifier));
    } catch (err) {
      console.error(err);
      joining = false;
      game = null;
      showJoinError("Couldn't connect to the game server. Check your connection and try again.");
    }
  }

  // ---------------------------------------------------------------------------
  // Leaving
  // ---------------------------------------------------------------------------
  async function confirmLeave() {
    if (!inGame()) return true;
    const ok = await UI.confirm({
      title: 'Leave this game?',
      message: 'You can rejoin with the same PIN and name while the game is still running. Your points so far are kept.',
      confirmText: 'Leave game',
      cancelText: 'Stay',
      danger: true,
    });
    if (ok) {
      forgetGame();
      try { await connection.stop(); } catch (_) { /* ignore */ }
    }
    return ok;
  }

  function goToJoin() {
    forgetGame();
    clearJoinError();
    show('stageJoin');
    if (connection.state === signalR.HubConnectionState.Disconnected) {
      ensureConnected().catch(() => {});
    }
  }

  // ---------------------------------------------------------------------------
  // Standings dialog
  // ---------------------------------------------------------------------------
  async function openStandings() {
    UI.openDialog({
      wide: true,
      labelledBy: 'standingsTitle',
      render(el, close) {
        el.innerHTML = `
          <div class="dialog-head">
            <div>
              <h2 class="dialog-title" id="standingsTitle">Standings</h2>
              <p class="dialog-body">Total points across every game run by this host.</p>
            </div>
            <button class="btn btn-ghost btn-icon btn-sm" data-close aria-label="Close">${UI.icon('x')}</button>
          </div>
          <div class="table-wrap">
            <table class="table">
              <thead><tr><th>#</th><th>Player</th><th class="right">Points</th><th class="right hide-sm">Games</th><th class="right">Accuracy</th></tr></thead>
              <tbody><tr><td colspan="5" class="empty-row">Loading…</td></tr></tbody>
            </table>
          </div>
          <div class="dialog-actions"><a class="btn btn-secondary" href="/leaderboard.html" target="_blank" rel="noopener">Open full standings</a></div>`;
        el.querySelector('[data-close]').onclick = () => close();

        const tbody = el.querySelector('tbody');
        const url = hostId ? `/api/leaderboard?hostId=${encodeURIComponent(hostId)}` : '/api/leaderboard';
        UI.api(url, { auth: false }).then(players => {
          if (!players.length) {
            tbody.innerHTML = '<tr><td colspan="5" class="empty-row">No scores yet. They appear after the first game finishes.</td></tr>';
            return;
          }
          const me = game && game.name.toLowerCase();
          tbody.innerHTML = players.map((p, i) => `
            <tr class="${me && p.fullName.toLowerCase() === me ? 'is-me' : ''}">
              <td>${UI.rankBadge(i + 1)}</td>
              <td><div class="player-cell">${UI.avatar(p.fullName)}${UI.escape(p.fullName)}${me && p.fullName.toLowerCase() === me ? ' <span class="badge badge-accent">You</span>' : ''}</div></td>
              <td class="right score">${UI.formatNumber(p.totalPointsAllTime)}</td>
              <td class="right hide-sm num">${p.quizzesPlayed || 0}</td>
              <td class="right num">${Math.round(p.accuracyPercentage || 0)}%</td>
            </tr>`).join('');
        }).catch(err => {
          tbody.innerHTML = `<tr><td colspan="5" class="empty-row">${UI.escape(err.message)}</td></tr>`;
        });
      },
    });
  }

  // ---------------------------------------------------------------------------
  // Hub events
  // ---------------------------------------------------------------------------
  function onRoomState(state) {
    joining = false;
    hostId = state.hostId || '';
    const p = state.player;
    game.name = p.fullName;
    saveGame();

    setPlayerBar(p.fullName, p.score, p.streak);
    $('lobbyName').textContent = p.fullName;
    $('lobbyGame').textContent = state.sessionType === 'Tournament'
      ? `${state.tournamentName} · ${state.title} · counts toward the tournament`
      : state.totalSessions > 1
      ? `${state.tournamentName.replace(/ - Session \d+$/, '')} · Round ${state.sessionNumber} of ${state.totalSessions}`
      : state.title;

    switch (state.state) {
      case 'Lobby': show('stageLobby'); break;
      case 'QuestionCountdown': show('stageCountdown'); break;
      case 'GameEnded': show('stageSummary'); break;
      case 'QuestionActive': break; // QuestionStarted follows immediately
      default:
        // Joined between questions: wait on the result screen until the next question.
        $('resultIcon').className = 'result-icon';
        $('resultIcon').innerHTML = UI.icon('info');
        $('resultTitle').textContent = 'Waiting for the next question';
        $('resultPoints').textContent = '';
        $('resultRank').textContent = p.rank ? `#${p.rank}` : '–';
        $('resultTotal').textContent = UI.formatNumber(p.score);
        $('resultStreak').hidden = true;
        show('stageResult');
    }
  }

  function onQuestionCountdown(data) {
    answered = false;
    show('stageCountdown');
    $('countdownLabel').textContent = `Question ${data.questionIndex} of ${data.totalQuestions}`;
    let count = data.countdownSeconds;
    const el = $('countdownNumber');
    el.textContent = count;
    if (window.sounds) window.sounds.tick();
    clearInterval(countdownTimer);
    countdownTimer = setInterval(() => {
      count--;
      if (count > 0) {
        el.textContent = count;
        if (window.sounds) window.sounds.tick();
      } else {
        clearInterval(countdownTimer);
      }
    }, 1000);
  }

  function setTimer(remaining) {
    const t = $('questionTimer');
    t.querySelector('span').textContent = remaining;
    t.style.setProperty('--p', Math.max(0, Math.min(1, remaining / (timeLimit || 1))));
    t.classList.toggle('is-urgent', remaining <= 5);
  }

  function onQuestionStarted(data) {
    clearInterval(countdownTimer);
    questionIndex = data.index;
    answered = !!data.alreadyAnswered;
    timeLimit = data.timeLimit;

    if (answered) {
      show('stageSubmitted');
      return;
    }

    $('questionLabel').textContent = `Question ${data.questionNumber} of ${data.totalQuestions}`;
    $('questionText').textContent = data.text || '';
    setTimer(data.timeLimit);

    const grid = $('answerGrid');
    grid.innerHTML = data.choices.map((choice, i) => `
      <button type="button" class="answer answer-${i % 6}" data-index="${i}" aria-label="${UI.SHAPES[i % 6].name}: ${UI.escape(choice)}">
        ${UI.shape(i)}<span class="answer-text">${UI.escape(choice)}</span>
      </button>`).join('');
    grid.querySelectorAll('.answer').forEach(btn => {
      btn.onclick = () => submitAnswer(Number(btn.dataset.index));
    });
    show('stageQuestion');
  }

  function onTimerTick(data) {
    if (stage !== 'stageQuestion') return;
    setTimer(data.remainingSeconds);
    if (window.sounds) data.remainingSeconds <= 5 ? window.sounds.hurryTick() : window.sounds.tick();
  }

  async function submitAnswer(choice) {
    if (answered) return;
    answered = true;
    if (window.sounds) window.sounds.click();

    document.querySelectorAll('#answerGrid .answer').forEach((b, i) => {
      b.disabled = true;
      b.classList.add(i === choice ? 'is-selected' : 'is-dimmed');
    });

    try {
      await connection.invoke('SubmitAnswer', game.pin, questionIndex, choice);
      setTimeout(() => { if (stage === 'stageQuestion') show('stageSubmitted'); }, 300);
    } catch (err) {
      console.error(err);
      answered = false;
      document.querySelectorAll('#answerGrid .answer').forEach(b => {
        b.disabled = false;
        b.classList.remove('is-selected', 'is-dimmed');
      });
      UI.toast("Your answer didn't go through. Tap it again.", 'error');
    }
  }

  function onPlayerRoundResult(r) {
    const icon = $('resultIcon');
    icon.className = `result-icon ${r.isCorrect ? 'is-correct' : 'is-wrong'}`;
    icon.innerHTML = UI.icon(r.isCorrect ? 'check' : 'x');
    $('resultTitle').textContent = r.isCorrect ? 'Correct' : (answered ? 'Not quite' : "Time's up");
    $('resultPoints').textContent = `+${UI.formatNumber(r.pointsEarned)}`;
    $('resultPoints').style.color = r.isCorrect ? 'var(--success)' : 'var(--text-3)';
    $('resultRank').textContent = `#${r.rank}`;
    $('resultTotal').textContent = UI.formatNumber(r.totalScore);

    const streak = $('resultStreak');
    streak.hidden = r.streak < 2;
    streak.innerHTML = `${UI.icon('flame')} ${r.streak} in a row`;

    setPlayerBar(game.name, r.totalScore, r.streak);
    show('stageResult');
    if (window.sounds) r.isCorrect ? window.sounds.correct() : window.sounds.wrong();
  }

  function onLeaderboardUpdate(data) {
    const me = (data.topPlayers || []).find(p => game && p.fullName === game.name);
    if (me) {
      $('resultRank').textContent = `#${me.rank}`;
      setPlayerBar(game.name, me.score);
    }
  }

  function onGameEnded(data) {
    const all = data.allPlayers || [];
    const me = all.find(p => game && p.fullName === game.name);
    if (me) {
      $('summaryRank').textContent = `#${me.rank}`;
      $('summaryScore').textContent = UI.formatNumber(me.score);
      $('summaryText').textContent = me.rank === 1
        ? `You won! First place out of ${all.length}.`
        : `You finished ${ordinal(me.rank)} out of ${all.length}.`;
    }
    try { sessionStorage.removeItem(CURRENT_GAME_KEY); } catch (_) { /* ignore */ }
    show('stageSummary');
    if (window.sounds) window.sounds.podium();
  }

  function ordinal(n) {
    const s = ['th', 'st', 'nd', 'rd'], v = n % 100;
    return n + (s[(v - 20) % 10] || s[v] || s[0]);
  }

  function endWith(title, text) {
    forgetGame();
    $('endedTitle').textContent = title;
    $('endedText').textContent = text;
    show('stageEnded');
  }

  function onError(message) {
    if (joining || stage === 'stageJoin') {
      joining = false;
      const wasReconnect = stage !== 'stageJoin';
      forgetGame();
      if (wasReconnect) {
        endWith('This game has ended', message);
      } else {
        showJoinError(message, $('joinPin'));
      }
      return;
    }
    UI.toast(message, 'error');
  }

  /** Tournament games need an account so the score lands in the tournament standings. */
  async function onSignInRequired(data) {
    const pending = game;
    joining = false;
    forgetGame();
    show('stageJoin');
    const session = await UI.signIn({
      title: 'Sign in to play',
      description: `This game is part of the tournament “${data.tournamentName}”. Sign in or create an account so your score counts. You'll be added to the tournament.`,
      startWith: 'signin',
    });
    if (!session || !pending) return;
    renderAccountLine();
    try {
      // Reconnect so the hub sees the new token, then join again.
      await connection.stop();
      await UI.busy($('joinSubmit'), () => join(pending.pin, session.displayName || pending.name, session.email));
    } catch (_) {
      showJoinError("Couldn't connect to the game server. Check your connection and try again.");
    }
  }

  // ---------------------------------------------------------------------------
  // Boot
  // ---------------------------------------------------------------------------
  function initConnection() {
    connection = new signalR.HubConnectionBuilder()
      // Signed-in players send their token so tournament games can credit their account.
      .withUrl('/hubs/game', { accessTokenFactory: () => (Session.get() || {}).token || '' })
      .withAutomaticReconnect([0, 1000, 2000, 5000, 10000, 15000])
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    connection.on('RoomState', onRoomState);
    connection.on('QuestionCountdown', onQuestionCountdown);
    connection.on('QuestionStarted', onQuestionStarted);
    connection.on('TimerTick', onTimerTick);
    connection.on('PlayerRoundResult', onPlayerRoundResult);
    connection.on('RoundCompleted', () => {});
    connection.on('LeaderboardUpdate', onLeaderboardUpdate);
    connection.on('GameEnded', onGameEnded);
    connection.on('PlayerKicked', d => endWith('You were removed from the game', d.message || 'The host removed you from this game.'));
    connection.on('SessionClosed', () => endWith('The host ended this game', "Thanks for playing. Games that end early don't count toward the standings."));
    connection.on('ErrorNotification', onError);
    connection.on('SignInRequired', onSignInRequired);

    connection.onreconnecting(() => { if (inGame()) UI.toast('Connection lost. Reconnecting…'); });
    connection.onreconnected(() => {
      if (game) connection.invoke('JoinRoom', game.pin, game.name, game.identifier || '', 'global').catch(() => {});
    });
    connection.onclose(() => { if (inGame()) UI.toast('Disconnected from the game.', 'error'); });
  }

  document.addEventListener('DOMContentLoaded', () => {
    initConnection();

    const pinInput = $('joinPin');
    pinInput.addEventListener('input', () => {
      pinInput.value = pinInput.value.replace(/[^0-9a-z]/gi, '').toUpperCase().slice(0, 6);
      pinInput.removeAttribute('aria-invalid');
    });
    $('joinName').addEventListener('input', () => $('joinName').removeAttribute('aria-invalid'));
    $('joinForm').addEventListener('submit', handleJoinSubmit);

    document.querySelectorAll('[data-action="standings"]').forEach(b => { b.onclick = openStandings; });
    document.querySelectorAll('[data-action="rejoin"]').forEach(b => { b.onclick = goToJoin; });
    document.querySelectorAll('[data-action="leave"]').forEach(b => {
      b.onclick = async () => { if (await confirmLeave()) goToJoin(); };
    });

    $('brandLink').addEventListener('click', async e => {
      if (!inGame()) return;
      e.preventDefault();
      if (await confirmLeave()) window.location.href = '/';
    });

    window.addEventListener('beforeunload', e => {
      if (stage === 'stageQuestion' && !answered) {
        e.preventDefault();
        e.returnValue = '';
      }
    });

    prefillFromSession(false);
    renderAccountLine();

    const params = new URLSearchParams(window.location.search);
    const pinParam = (params.get('pin') || '').trim().toUpperCase();
    if (pinParam) pinInput.value = pinParam.slice(0, 6);

    // Same tab, page reloaded mid-game: rejoin automatically.
    let saved = null;
    try { saved = JSON.parse(sessionStorage.getItem(CURRENT_GAME_KEY) || 'null'); } catch (_) { /* ignore */ }
    if (saved && saved.pin && saved.name && (!pinParam || pinParam === saved.pin)) {
      join(saved.pin, saved.name, saved.identifier || '').catch(() => {
        joining = false;
        forgetGame();
      });
    } else {
      ensureConnected().catch(() => {});
      (pinParam ? $('joinName') : pinInput).focus();
    }
  });
})();
