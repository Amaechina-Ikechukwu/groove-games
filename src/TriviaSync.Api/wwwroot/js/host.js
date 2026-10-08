// Groove Host Stadium Logic - Tournament & Multi-Session Edition
let connection = null;
let currentPin = '';
let currentHostId = 'host@groove.live';
let currentSessionData = null;
let currentQuestionData = null;
let activeTournamentSessions = [];

const CHOICE_GLYPHS = ['▲', '◆', '●', '■', '⬡', '★'];

document.addEventListener('DOMContentLoaded', () => {
  // Check stored host identity
  const storedHost = sessionStorage.getItem('groove_host_email') || sessionStorage.getItem('triviasync_host_email');
  if (storedHost) {
    currentHostId = storedHost;
    document.getElementById('hostEmailDisplay').textContent = currentHostId;
  }

  loadSavedQuizzes();

  const params = new URLSearchParams(window.location.search);
  const pinParam = params.get('pin');
  if (pinParam) {
    currentPin = pinParam.trim().toUpperCase();
    initSignalRAndAttach();
  }
});

function toggleSessionMode() {
  const isMulti = document.getElementById('modeMulti').checked;
  document.getElementById('multiSessionFields').style.display = isMulti ? 'block' : 'none';
}

function openHostAuthModal() {
  document.getElementById('hostAuthModal').classList.add('active');
}

function closeHostAuthModal() {
  document.getElementById('hostAuthModal').classList.remove('active');
}

async function handleHostLogin(e) {
  e.preventDefault();
  const email = document.getElementById('modalHostEmailInput').value.trim();
  const password = document.getElementById('modalHostPasswordInput').value.trim();

  try {
    const res = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password })
    });

    if (res.ok) {
      const auth = await res.json();
      currentHostId = auth.email;
      sessionStorage.setItem('groove_host_email', auth.email);
      sessionStorage.setItem('groove_host_token', auth.token);
      document.getElementById('hostEmailDisplay').textContent = auth.email;
      closeHostAuthModal();
      alert(`Signed in as ${auth.role}: ${auth.displayName}`);
    } else {
      alert('Login failed. Please check credentials.');
    }
  } catch (err) {
    console.error(err);
    alert('Network error during authentication.');
  }
}

async function loadSavedQuizzes() {
  try {
    const res = await fetch('/api/quizzes');
    if (res.ok) {
      const quizzes = await res.json();
      const select = document.getElementById('quizSelectDropdown');
      select.innerHTML = '';

      if (quizzes.length === 0) {
        select.innerHTML = '<option value="">No quizzes found. Create one in Admin portal.</option>';
        return;
      }

      quizzes.forEach(q => {
        const opt = document.createElement('option');
        opt.value = q.id;
        opt.textContent = `${q.title} (${q.questions.length} Questions)`;
        select.appendChild(opt);
      });
    }
  } catch (err) {
    console.error('Error loading quizzes:', err);
  }
}

function connectExistingPin() {
  const pin = document.getElementById('existingPinInput').value.trim().toUpperCase();
  if (pin) {
    currentPin = pin;
    initSignalRAndAttach();
  }
}

async function launchNewSession() {
  const select = document.getElementById('quizSelectDropdown');
  const quizId = select.value;
  const isMulti = document.getElementById('modeMulti').checked;
  const sessionCount = parseInt(document.getElementById('multiSessionCountSelect').value) || 2;
  const tournamentName = document.getElementById('tournamentNameInput').value.trim();
  const autoAdvance = document.getElementById('autoAdvanceCheck').checked;

  if (!quizId) {
    alert('Please select a quiz to launch.');
    return;
  }

  try {
    const payload = {
      quizId: quizId,
      hostId: currentHostId,
      autoAdvance: autoAdvance,
      sessionType: isMulti ? 'MultiSession' : 'Single',
      sessionCount: isMulti ? sessionCount : 1,
      tournamentName: tournamentName
    };

    const res = await fetch('/api/sessions', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });

    if (res.ok) {
      const data = await res.json();
      console.log('Session creation response:', data);

      if (data.sessionType === 'MultiSession' && data.sessions && data.sessions.length > 1) {
        // Multi-Session Tournament created!
        activeTournamentSessions = data.sessions;
        renderTournamentCodesDeck(data);
      } else {
        // Single session
        currentPin = data.pin;
        initSignalRAndAttach();
      }
    } else {
      const err = await res.json();
      alert(err.message || 'Failed to create room.');
    }
  } catch (e) {
    console.error(e);
    alert('Network error while launching arena.');
  }
}

function renderTournamentCodesDeck(data) {
  document.getElementById('hostStageSelect').classList.remove('active');
  document.getElementById('tournamentCodesDeck').style.display = 'block';
  document.getElementById('deckTournamentName').textContent = data.tournamentName || 'TOURNAMENT SESSIONS';

  const container = document.getElementById('tournamentSessionsList');
  container.innerHTML = '';

  data.sessions.forEach((s, idx) => {
    const card = document.createElement('div');
    card.className = 'cyber-card';
    card.style.padding = '1.75rem';
    card.innerHTML = `
      <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 0.75rem;">
        <span class="hud-pill score-pill" style="font-size: 0.8rem;">SESSION ${s.sessionNumber} OF ${s.totalSessions}</span>
        <span class="live-pill" style="padding: 0.2rem 0.5rem; font-size: 0.75rem;">READY</span>
      </div>

      <h3 style="font-family: var(--font-display); font-size: 1.3rem; font-weight: 900; text-transform: uppercase; margin-bottom: 1rem;">
        ${escapeHtml(s.quizTitle)}
      </h3>

      <div style="background: var(--bg-surface-elevated); border: 2px solid var(--neon-lime); border-radius: var(--radius-button); padding: 1rem; text-align: center; margin-bottom: 1.25rem;">
        <div style="font-size: 0.8rem; color: var(--text-muted); font-family: var(--font-display); text-transform: uppercase;">CONTENDER ACCESS CODE</div>
        <div style="font-family: var(--font-display); font-size: 2.2rem; font-weight: 900; color: var(--neon-lime); letter-spacing: 4px;">
          ${s.pin}
        </div>
      </div>

      <div style="display: flex; gap: 0.5rem;">
        <button onclick="launchSessionFromDeck('${s.pin}')" class="btn-cyber btn-lime" style="flex: 1; font-size: 0.95rem; padding: 0.75rem;">
          ▶ LAUNCH THIS ARENA
        </button>
        <button onclick="copyPinCode('${s.pin}')" class="btn-cyber btn-dark" style="padding: 0.75rem 1rem;" title="Copy Code">
          📋
        </button>
      </div>
    `;
    container.appendChild(card);
  });
}

function copyPinCode(pin) {
  navigator.clipboard.writeText(pin);
  alert(`Access code ${pin} copied to clipboard!`);
}

function launchSessionFromDeck(pin) {
  currentPin = pin;
  document.getElementById('tournamentCodesDeck').style.display = 'none';
  initSignalRAndAttach();
}

function returnToLobbyOrNextSession() {
  if (activeTournamentSessions.length > 0) {
    document.getElementById('hostGameContainer').style.display = 'none';
    document.getElementById('tournamentCodesDeck').style.display = 'block';
  } else {
    window.location.href = '/host.html';
  }
}

function initSignalRAndAttach() {
  connection = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/game')
    .withAutomaticReconnect([0, 1000, 2000, 5000, 10000])
    .configureLogging(signalR.LogLevel.Information)
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
  connection.on('ErrorNotification', msg => alert(msg));

  connection.start().then(() => {
    console.log('Host connected to SignalR. Attaching as host for PIN:', currentPin);
    connection.invoke('HostJoin', currentPin);
  }).catch(err => {
    console.error('SignalR host start error:', err);
    alert('Failed to connect to game hub. Check PIN.');
  });
}

function showHostStage(stageId) {
  document.getElementById('hostStageSelect').classList.remove('active');
  document.getElementById('tournamentCodesDeck').style.display = 'none';
  document.getElementById('hostGameContainer').style.display = 'flex';
  document.querySelectorAll('.host-stage').forEach(el => el.classList.remove('active'));
  const target = document.getElementById(stageId);
  if (target) target.classList.add('active');
}

function onRoomState(state) {
  console.log('Host Room State:', state);
  currentSessionData = state;

  document.getElementById('stadiumPinValue').textContent = state.pin;
  document.getElementById('stadiumQuizTitle').textContent = 
    state.tournamentName ? `${state.tournamentName} - ${state.title}` : state.title;
  document.getElementById('stadiumPlayerCount').textContent = state.connectedPlayerCount || 0;

  renderLobbyPlayers(state.allPlayers || []);

  if (state.state === 'Lobby') {
    showHostStage('hostStageLobby');
    setHudButton('START QUIZ', () => startQuizFromHost());
  } else if (state.state === 'QuestionActive') {
    showHostStage('hostStageQuestion');
    setHudButton('SKIP QUESTION', () => advanceNextQuestionFromHost());
  } else if (state.state === 'AnswerReveal') {
    showHostStage('hostStageReveal');
    setHudButton('VIEW STANDINGS', () => advanceNextQuestionFromHost());
  } else if (state.state === 'RoundLeaderboard') {
    showHostStage('hostStageLeaderboard');
    setHudButton('NEXT QUESTION', () => advanceNextQuestionFromHost());
  } else if (state.state === 'GameEnded') {
    showHostStage('hostStagePodium');
  }
}

function setHudButton(text, handler) {
  const btn = document.getElementById('btnHostAction');
  btn.textContent = text;
  btn.onclick = handler;
}

function renderLobbyPlayers(players) {
  const container = document.getElementById('lobbyContendersGrid');
  container.innerHTML = '';

  document.getElementById('stadiumPlayerCount').textContent = players.length;

  if (players.length === 0) {
    container.innerHTML = '<div style="color: var(--text-muted); font-size: 1.2rem; padding: 2rem 0;">Waiting for contenders to enter access code...</div>';
    return;
  }

  players.forEach(p => {
    const chip = document.createElement('div');
    chip.className = 'player-avatar-chip';
    chip.id = `chip_${p.playerId}`;
    chip.innerHTML = `
      <span>👤</span>
      <span>${escapeHtml(p.fullName)}</span>
      <span class="kick-btn" onclick="kickPlayer('${p.playerId}')" title="Kick player">✕</span>
    `;
    container.appendChild(chip);
  });
}

function onPlayerJoined(data) {
  console.log('Player Joined:', data);
  document.getElementById('stadiumPlayerCount').textContent = data.totalCount;

  if (data.allPlayers) {
    renderLobbyPlayers(data.allPlayers);
  }

  if (window.sounds) window.sounds.click();
}

function onPlayerLeft(data) {
  console.log('Player Left:', data);
  document.getElementById('stadiumPlayerCount').textContent = data.totalCount;
  const chip = document.getElementById(`chip_${data.playerId}`);
  if (chip) chip.remove();
}

function startQuizFromHost() {
  if (window.sounds) window.sounds.init();
  connection.invoke('StartQuiz', currentPin);
}

function advanceNextQuestionFromHost() {
  if (window.sounds) window.sounds.init();
  connection.invoke('AdvanceQuestion', currentPin);
}

function kickPlayer(playerId) {
  if (confirm('Remove this player from the game session?')) {
    connection.invoke('KickPlayer', currentPin, playerId);
  }
}

function onQuestionCountdown(data) {
  console.log('Host Countdown:', data);
  showHostStage('hostStageCountdown');

  document.getElementById('hostCountdownSubtext').textContent = `Question ${data.questionIndex} of ${data.totalQuestions}`;
  let count = data.countdownSeconds;
  const digitEl = document.getElementById('hostCountdownDigit');
  digitEl.textContent = count;

  if (window.sounds) window.sounds.tick();

  const interval = setInterval(() => {
    count--;
    if (count > 0) {
      digitEl.textContent = count;
      if (window.sounds) window.sounds.tick();
    } else {
      clearInterval(interval);
    }
  }, 1000);
}

function onQuestionStarted(data) {
  console.log('Host Question Started:', data);
  currentQuestionData = data;
  showHostStage('hostStageQuestion');
  setHudButton('SKIP QUESTION', () => advanceNextQuestionFromHost());

  document.getElementById('hostQuestionTracker').textContent = `QUESTION ${data.questionNumber} OF ${data.totalQuestions}`;
  document.getElementById('hostQuestionText').textContent = data.text;

  const timerEl = document.getElementById('hostTimerRing');
  timerEl.textContent = data.timeLimit;
  timerEl.classList.remove('hurry');

  const countBadge = document.getElementById('hostAnswersReceivedBadge');
  countBadge.textContent = `0 / ${document.getElementById('stadiumPlayerCount').textContent} ANSWERED`;

  // Render Choices
  const container = document.getElementById('hostChoicesContainer');
  container.innerHTML = '';

  data.choices.forEach((choiceText, idx) => {
    const card = document.createElement('div');
    card.className = `host-choice-card host-choice-${idx % 4}`;
    card.id = `choiceCard_${idx}`;
    card.innerHTML = `
      <div class="choice-glyph">${CHOICE_GLYPHS[idx % CHOICE_GLYPHS.length]}</div>
      <div class="choice-text">${escapeHtml(choiceText)}</div>
    `;
    container.appendChild(card);
  });
}

function onTimerTick(data) {
  const timerEl = document.getElementById('hostTimerRing');
  if (timerEl) {
    timerEl.textContent = data.remainingSeconds;
    if (data.remainingSeconds <= 5) {
      timerEl.classList.add('hurry');
      if (window.sounds) window.sounds.hurryTick();
    } else {
      if (window.sounds) window.sounds.tick();
    }
  }
}

function onAnswerReceived(data) {
  const countBadge = document.getElementById('hostAnswersReceivedBadge');
  if (countBadge) {
    countBadge.textContent = `${data.totalAnswers} / ${data.totalPlayers} ANSWERED`;
  }
}

function onRoundCompleted(data) {
  console.log('Host Round Completed:', data);
  showHostStage('hostStageReveal');
  setHudButton('NEXT ➔', () => advanceNextQuestionFromHost());

  if (window.sounds) window.sounds.correct();

  document.getElementById('revealQuestionText').textContent = currentQuestionData ? currentQuestionData.text : '';

  const container = document.getElementById('revealChoicesContainer');
  container.innerHTML = '';

  if (currentQuestionData && currentQuestionData.choices) {
    currentQuestionData.choices.forEach((choiceText, idx) => {
      const isCorrect = (idx === data.correctIndex);
      const card = document.createElement('div');
      card.className = `host-choice-card host-choice-${idx % 4} ${isCorrect ? 'revealed-correct' : 'dimmed-wrong'}`;
      card.innerHTML = `
        <div class="choice-glyph">${isCorrect ? '✔' : CHOICE_GLYPHS[idx % CHOICE_GLYPHS.length]}</div>
        <div class="choice-text">${escapeHtml(choiceText)} ${isCorrect ? '<strong style="color:var(--neon-lime);">[CORRECT]</strong>' : ''}</div>
      `;
      container.appendChild(card);
    });
  }

  // Draw response distribution bars
  const barsContainer = document.getElementById('distributionBars');
  barsContainer.innerHTML = '';

  const maxVotes = Math.max(1, ...(data.stats || [1]));

  (data.stats || []).forEach((count, idx) => {
    const col = document.createElement('div');
    col.className = 'dist-col';

    const heightPct = Math.round((count / maxVotes) * 100);

    col.innerHTML = `
      <div class="dist-bar host-choice-${idx % 4}" style="height: ${Math.max(15, heightPct)}%;">
        ${count}
      </div>
      <div class="dist-label" style="color: #fff;">
        ${CHOICE_GLYPHS[idx % CHOICE_GLYPHS.length]}
      </div>
    `;
    barsContainer.appendChild(col);
  });
}

function onLeaderboardUpdate(data) {
  console.log('Host Leaderboard Update:', data);
  showHostStage('hostStageLeaderboard');
  setHudButton('NEXT QUESTION ➔', () => advanceNextQuestionFromHost());

  const list = document.getElementById('hostLeaderboardList');
  list.innerHTML = '';

  if (data.topPlayers) {
    data.topPlayers.forEach((p, idx) => {
      const card = document.createElement('div');
      card.className = `round-rank-card rank-${idx + 1}`;
      card.innerHTML = `
        <div style="display: flex; align-items: center; gap: 1.25rem;">
          <div class="rank-number">${idx + 1}</div>
          <div>
            <div style="font-family: var(--font-display); font-size: 1.35rem; font-weight: 900; text-transform: uppercase;">
              ${escapeHtml(p.fullName)}
            </div>
            ${p.streak > 1 ? `<div style="color: #FFA502; font-size: 0.85rem; font-weight: 700;">🔥 Streak: ${p.streak} in a row</div>` : ''}
          </div>
        </div>
        <div style="text-align: right;">
          <div style="font-family: var(--font-display); font-size: 1.6rem; font-weight: 900; color: var(--neon-lime);">
            ${p.score.toLocaleString()} PTS
          </div>
          ${p.pointsGained > 0 ? `<div style="color: var(--neon-lime); font-size: 0.9rem; font-weight: 800;">+${p.pointsGained}</div>` : ''}
        </div>
      `;
      list.appendChild(card);
    });
  }
}

function onGameEnded(data) {
  console.log('Host Game Ended:', data);
  showHostStage('hostStagePodium');
  setHudButton('MATCH FINISHED', () => {});

  if (window.sounds) window.sounds.podium();

  document.getElementById('btnExportCsv').href = `/api/sessions/${currentPin}/export/csv`;
  document.getElementById('btnExportExcel').href = `/api/sessions/${currentPin}/export/excel`;

  const podium = data.podium || [];

  if (podium[0]) {
    document.getElementById('podiumName1').textContent = podium[0].fullName.toUpperCase();
    document.getElementById('podiumScore1').textContent = `${podium[0].score.toLocaleString()} PTS`;
  }
  if (podium[1]) {
    document.getElementById('podiumName2').textContent = podium[1].fullName.toUpperCase();
    document.getElementById('podiumScore2').textContent = `${podium[1].score.toLocaleString()} PTS`;
  }
  if (podium[2]) {
    document.getElementById('podiumName3').textContent = podium[2].fullName.toUpperCase();
    document.getElementById('podiumScore3').textContent = `${podium[2].score.toLocaleString()} PTS`;
  }
}

// CUMULATIVE LEADERBOARD ACROSS ALL SESSIONS & GAMES
async function viewCumulativeLeaderboard() {
  document.getElementById('cumulativeLeaderboardModal').classList.add('active');
  document.getElementById('modalHostBadge').textContent = currentHostId || 'All Hosts';

  // Update export links for this host
  document.getElementById('btnExportModalCsv').href = `/api/leaderboard/export/csv?hostId=${encodeURIComponent(currentHostId)}`;
  document.getElementById('btnExportModalExcel').href = `/api/leaderboard/export/excel?hostId=${encodeURIComponent(currentHostId)}`;

  const tbody = document.getElementById('cumulativeModalTableBody');
  tbody.innerHTML = '<tr><td colspan="7" style="text-align:center; padding: 2rem; color: var(--text-muted);">Fetching tournament scores...</td></tr>';

  try {
    const res = await fetch(`/api/leaderboard?hostId=${encodeURIComponent(currentHostId)}`);
    if (res.ok) {
      const players = await res.json();
      tbody.innerHTML = '';

      if (players.length === 0) {
        tbody.innerHTML = '<tr><td colspan="7" style="text-align:center; padding: 2rem; color: var(--text-muted);">No contenders have recorded scores under this host yet.</td></tr>';
        return;
      }

      players.forEach((p, idx) => {
        const tr = document.createElement('tr');
        tr.className = 'row-card';
        tr.innerHTML = `
          <td><strong style="color: ${idx === 0 ? 'var(--neon-lime)' : '#fff'};">#${idx + 1}</strong></td>
          <td><strong style="font-family: var(--font-display);">${escapeHtml(p.fullName)}</strong></td>
          <td><strong style="font-family: var(--font-display); color: var(--neon-lime);">${p.totalPointsAllTime.toLocaleString()} PTS</strong></td>
          <td>${p.quizzesPlayed}</td>
          <td><strong style="color: #2ecc71;">${p.accuracyPercentage}%</strong></td>
          <td>🔥 ${p.highestStreak}</td>
          <td style="color: var(--text-muted); font-size: 0.85rem;">${new Date(p.lastActive).toLocaleDateString()}</td>
        `;
        tbody.appendChild(tr);
      });
    }
  } catch (err) {
    console.error('Error loading cumulative leaderboard:', err);
  }
}

function closeCumulativeModal() {
  document.getElementById('cumulativeLeaderboardModal').classList.remove('active');
}

function escapeHtml(str) {
  if (!str) return '';
  return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}
