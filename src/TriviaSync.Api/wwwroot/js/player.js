// TriviaSync Player Client Logic - Esports Edition
let connection = null;
let currentPin = '';
let currentFullName = '';
let currentIdentifier = '';
let currentHostId = '';
let currentQuestionIndex = -1;
let hasAnsweredCurrentQuestion = false;

// Geometric gaming glyphs
const TILE_CONFIGS = [
  { shape: '▲', label: 'Crimson Triangle' },
  { shape: '◆', label: 'Cyan Diamond' },
  { shape: '●', label: 'Amber Circle' },
  { shape: '■', label: 'Lime Square' },
  { shape: '⬡', label: 'Purple Hexagon' },
  { shape: '★', label: 'Orange Star' }
];

function checkPlayerAuth() {
  const token = sessionStorage.getItem('groove_player_token');
  const name = sessionStorage.getItem('groove_player_name');
  const email = sessionStorage.getItem('groove_player_email');
  const nameDisplay = document.getElementById('playerDisplayName');
  const logoutBtn = document.getElementById('playerLogoutBtn');

  if (token && name) {
    currentFullName = name;
    currentIdentifier = email || '';
    if (nameDisplay) nameDisplay.textContent = name;
    if (logoutBtn) logoutBtn.style.display = 'inline-block';

    const joinName = document.getElementById('joinName');
    const joinId = document.getElementById('joinIdentifier');
    if (joinName) joinName.value = name;
    if (joinId && email) joinId.value = email;

    // Show PIN entry stage
    showStage('stageJoin');
    return true;
  } else {
    if (nameDisplay) nameDisplay.textContent = 'GUEST';
    if (logoutBtn) logoutBtn.style.display = 'none';
    showStage('stageAuth');
    return false;
  }
}

function switchPlayerAuthTab(tab) {
  const loginBtn = document.getElementById('tabPlayerLoginBtn');
  const regBtn = document.getElementById('tabPlayerRegisterBtn');
  const loginForm = document.getElementById('playerLoginForm');
  const regForm = document.getElementById('playerRegisterForm');
  const alertBox = document.getElementById('playerAuthAlert');
  if (alertBox) alertBox.style.display = 'none';

  if (tab === 'login') {
    loginBtn.classList.add('active');
    regBtn.classList.remove('active');
    loginForm.style.display = 'block';
    regForm.style.display = 'none';
  } else {
    loginBtn.classList.remove('active');
    regBtn.classList.add('active');
    loginForm.style.display = 'none';
    regForm.style.display = 'block';
  }
}

function setPlayerDemo(email, password) {
  switchPlayerAuthTab('login');
  document.getElementById('playerLoginEmail').value = email;
  document.getElementById('playerLoginPassword').value = password;
}

async function handlePlayerLogin(e) {
  e.preventDefault();
  const email = document.getElementById('playerLoginEmail').value.trim();
  const password = document.getElementById('playerLoginPassword').value.trim();
  const alertBox = document.getElementById('playerAuthAlert');
  if (alertBox) alertBox.style.display = 'none';

  try {
    const res = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password })
    });

    if (res.ok) {
      const auth = await res.json();
      sessionStorage.setItem('groove_player_token', auth.token);
      sessionStorage.setItem('groove_player_email', auth.email);
      sessionStorage.setItem('groove_player_name', auth.displayName || email.split('@')[0]);
      checkPlayerAuth();
    } else {
      const err = await res.json().catch(() => ({}));
      if (alertBox) {
        alertBox.textContent = err.message || 'Login failed. Please check credentials.';
        alertBox.style.display = 'block';
      }
    }
  } catch (err) {
    console.error(err);
    if (alertBox) {
      alertBox.textContent = 'Network error during player login.';
      alertBox.style.display = 'block';
    }
  }
}

async function handlePlayerRegister(e) {
  e.preventDefault();
  const fullName = document.getElementById('playerRegisterName').value.trim();
  const email = document.getElementById('playerRegisterEmail').value.trim();
  const password = document.getElementById('playerRegisterPassword').value.trim();
  const alertBox = document.getElementById('playerAuthAlert');
  if (alertBox) alertBox.style.display = 'none';

  if (!fullName || !email || !password) {
    if (alertBox) {
      alertBox.textContent = 'All fields are required.';
      alertBox.style.display = 'block';
    }
    return;
  }

  try {
    const res = await fetch('/api/auth/register', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ fullName, email, password, role: 'Player' })
    });

    if (res.ok) {
      const auth = await res.json();
      sessionStorage.setItem('groove_player_token', auth.token);
      sessionStorage.setItem('groove_player_email', auth.email);
      sessionStorage.setItem('groove_player_name', auth.displayName || fullName);
      checkPlayerAuth();
    } else {
      const err = await res.json().catch(() => ({}));
      if (alertBox) {
        alertBox.textContent = err.message || 'Registration failed. Try a different email.';
        alertBox.style.display = 'block';
      }
    }
  } catch (err) {
    console.error(err);
    if (alertBox) {
      alertBox.textContent = 'Network error during registration.';
      alertBox.style.display = 'block';
    }
  }
}

function playerLogout() {
  sessionStorage.removeItem('groove_player_token');
  sessionStorage.removeItem('groove_player_name');
  sessionStorage.removeItem('groove_player_email');
  sessionStorage.removeItem('groove_pin');
  if (connection && connection.state === signalR.HubConnectionState.Connected) {
    connection.stop();
  }
  checkPlayerAuth();
}

document.addEventListener('DOMContentLoaded', () => {
  // Check URL params for quick join
  const params = new URLSearchParams(window.location.search);
  const pinParam = params.get('pin');
  if (pinParam) {
    document.getElementById('joinPin').value = pinParam.trim().toUpperCase();
  }

  checkPlayerAuth();
  initSignalR();
});

function initSignalR() {
  connection = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/game')
    .withAutomaticReconnect([0, 1000, 2000, 5000, 10000])
    .configureLogging(signalR.LogLevel.Information)
    .build();

  // Setup Hub Event Listeners
  connection.on('RoomState', onRoomState);
  connection.on('QuestionCountdown', onQuestionCountdown);
  connection.on('QuestionStarted', onQuestionStarted);
  connection.on('TimerTick', onTimerTick);
  connection.on('PlayerRoundResult', onPlayerRoundResult);
  connection.on('RoundCompleted', onRoundCompleted);
  connection.on('LeaderboardUpdate', onLeaderboardUpdate);
  connection.on('GameEnded', onGameEnded);
  connection.on('PlayerKicked', onPlayerKicked);
  connection.on('ErrorNotification', onErrorNotification);

  connection.onreconnected(() => {
    console.log('Reconnected to SignalR. Re-authenticating in room...');
    if (currentPin && currentFullName) {
      connection.invoke('JoinRoom', currentPin, currentFullName, currentIdentifier, 'global');
    }
  });

  connection.start().catch(err => {
    console.error('SignalR start error:', err);
  });
}

function showStage(stageId) {
  document.querySelectorAll('.stage-wrapper').forEach(el => el.classList.remove('active'));
  const target = document.getElementById(stageId);
  if (target) target.classList.add('active');
}

async function handleJoin(e) {
  e.preventDefault();
  if (window.sounds) window.sounds.init();

  currentPin = document.getElementById('joinPin').value.trim().toUpperCase();
  currentFullName = document.getElementById('joinName').value.trim();
  currentIdentifier = document.getElementById('joinIdentifier').value.trim();

  const errorDiv = document.getElementById('joinErrorMessage');
  errorDiv.style.display = 'none';

  if (!currentPin || !currentFullName) {
    errorDiv.textContent = 'ROOM PIN AND FULL NAME ARE REQUIRED.';
    errorDiv.style.display = 'block';
    return;
  }

  try {
    if (connection.state !== signalR.HubConnectionState.Connected) {
      await connection.start();
    }

    sessionStorage.setItem('groove_pin', currentPin);
    sessionStorage.setItem('groove_name', currentFullName);
    sessionStorage.setItem('groove_id', currentIdentifier);

    await connection.invoke('JoinRoom', currentPin, currentFullName, currentIdentifier, 'global');
  } catch (err) {
    console.error(err);
    errorDiv.textContent = 'UNABLE TO ENTER ARENA. PLEASE VERIFY PIN.';
    errorDiv.style.display = 'block';
  }
}

function onRoomState(state) {
  console.log('Room State:', state);
  currentHostId = state.hostId || '';
  document.getElementById('playerDisplayName').textContent = state.player.fullName.toUpperCase();
  document.getElementById('lobbyPlayerName').textContent = state.player.fullName;
  document.getElementById('playerScoreDisplay').style.display = 'flex';
  document.getElementById('playerScoreValue').textContent = state.player.score;

  if (state.player.streak > 0) {
    document.getElementById('streakBadge').style.display = 'inline-flex';
    document.getElementById('streakCount').textContent = state.player.streak;
  }

  if (state.state === 'Lobby') {
    showStage('stageLobby');
  } else if (state.state === 'QuestionCountdown') {
    showStage('stageCountdown');
  } else if (state.state === 'GameEnded') {
    showStage('stageSummary');
  }
}

function onQuestionCountdown(data) {
  console.log('Countdown:', data);
  showStage('stageCountdown');
  hasAnsweredCurrentQuestion = false;

  document.getElementById('countdownQuestionInfo').textContent = `Question ${data.questionIndex} of ${data.totalQuestions}`;
  let count = data.countdownSeconds;
  const numEl = document.getElementById('countdownNumber');
  numEl.textContent = count;

  if (window.sounds) window.sounds.tick();

  const interval = setInterval(() => {
    count--;
    if (count > 0) {
      numEl.textContent = count;
      if (window.sounds) window.sounds.tick();
    } else {
      clearInterval(interval);
    }
  }, 1000);
}

function onQuestionStarted(data) {
  console.log('Question Started:', data);
  currentQuestionIndex = data.index;
  hasAnsweredCurrentQuestion = data.alreadyAnswered || false;

  if (hasAnsweredCurrentQuestion) {
    showStage('stageSubmitted');
    return;
  }

  showStage('stageQuestion');
  document.getElementById('playerQuestionNum').textContent = `Q${data.questionNumber} of ${data.totalQuestions}`;
  
  const timerBadge = document.getElementById('playerTimerBadge');
  timerBadge.textContent = data.timeLimit;
  timerBadge.classList.remove('hurry');

  // Render 4 (or 2-6) Asymmetric Buzzer buttons
  const grid = document.getElementById('playerChoicesGrid');
  grid.innerHTML = '';

  data.choices.forEach((choiceText, index) => {
    const config = TILE_CONFIGS[index % TILE_CONFIGS.length];
    const btn = document.createElement('button');
    btn.className = `buzzer-btn buzzer-${index % 4}`;
    btn.innerHTML = `
      <div class="buzzer-glyph">${config.shape}</div>
      <div class="buzzer-label">${escapeHtml(choiceText)}</div>
    `;

    btn.onclick = () => submitAnswer(index);
    grid.appendChild(btn);
  });
}

function onTimerTick(data) {
  const badge = document.getElementById('playerTimerBadge');
  if (badge) {
    badge.textContent = data.remainingSeconds;
    if (data.remainingSeconds <= 5) {
      badge.classList.add('hurry');
      if (window.sounds) window.sounds.hurryTick();
    } else {
      if (window.sounds) window.sounds.tick();
    }
  }
}

function submitAnswer(choiceIndex) {
  if (hasAnsweredCurrentQuestion) return;
  hasAnsweredCurrentQuestion = true;

  if (window.sounds) window.sounds.click();

  // Dim non-selected buttons, highlight selected
  const buttons = document.querySelectorAll('.buzzer-btn');
  buttons.forEach((b, idx) => {
    if (idx === choiceIndex) {
      b.classList.add('selected');
    } else {
      b.classList.add('dimmed');
    }
  });

  connection.invoke('SubmitAnswer', currentPin, currentQuestionIndex, choiceIndex)
    .then(() => {
      setTimeout(() => {
        showStage('stageSubmitted');
      }, 250);
    })
    .catch(err => {
      console.error('Answer submit error:', err);
    });
}

function onPlayerRoundResult(result) {
  console.log('Player Round Result:', result);
  showStage('stageResult');

  const iconEl = document.getElementById('resultIcon');
  const headingEl = document.getElementById('resultHeading');
  const pointsEl = document.getElementById('resultPointsEarned');
  const scoreEl = document.getElementById('resultTotalScore');
  const rankEl = document.getElementById('resultRankText');
  const streakMsg = document.getElementById('resultStreakMsg');

  document.getElementById('playerScoreValue').textContent = result.totalScore;
  scoreEl.textContent = `${result.totalScore} PTS`;
  rankEl.textContent = `#${result.rank}`;

  if (result.streak > 0) {
    document.getElementById('streakBadge').style.display = 'inline-flex';
    document.getElementById('streakCount').textContent = result.streak;
    streakMsg.innerHTML = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="#FFA502" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="width: 14px; height: 14px; margin-right: 0.35rem;"><path d="M8.5 14.5A2.5 2.5 0 0 0 11 12c0-1.38-.5-2-1-3-1.072-2.143-.224-4.054 2-6 .5 2.5 2 4.9 4 6.5 2 1.6 3 3.5 3 5.5a7 7 0 1 1-14 0c0-1.153.433-2.294 1-3a2.5 2.5 0 0 0 2.5 2.5z"></path></svg> <span>WINNING STREAK: ' + result.streak + '</span>';
    streakMsg.style.display = 'inline-flex';
  } else {
    document.getElementById('streakBadge').style.display = 'none';
    streakMsg.style.display = 'none';
  }

  if (result.isCorrect) {
    iconEl.innerHTML = '<svg width="56" height="56" viewBox="0 0 24 24" fill="none" stroke="var(--neon-lime)" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" style="width: 56px; height: 56px;"><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"></path><polyline points="22 4 12 14.01 9 11.01"></polyline></svg>';
    headingEl.textContent = 'CORRECT!';
    headingEl.style.color = 'var(--neon-lime)';
    pointsEl.textContent = `+${result.pointsEarned} PTS`;
    pointsEl.style.color = 'var(--neon-lime)';
    if (window.sounds) window.sounds.correct();
  } else {
    iconEl.innerHTML = '<svg width="56" height="56" viewBox="0 0 24 24" fill="none" stroke="var(--tile-red)" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" style="width: 56px; height: 56px;"><circle cx="12" cy="12" r="10"></circle><line x1="15" y1="9" x2="9" y2="15"></line><line x1="9" y1="9" x2="15" y2="15"></line></svg>';
    headingEl.textContent = 'INCORRECT';
    headingEl.style.color = 'var(--tile-red)';
    pointsEl.textContent = '+0 PTS';
    pointsEl.style.color = 'var(--tile-red)';
    if (window.sounds) window.sounds.wrong();
  }
}

function onRoundCompleted(data) {
  // Handled individually via onPlayerRoundResult
}

function onLeaderboardUpdate(data) {
  console.log('Leaderboard Update:', data);
  if (data.topPlayers) {
    const me = data.topPlayers.find(p => p.fullName === currentFullName);
    if (me) {
      document.getElementById('playerScoreValue').textContent = me.score;
      document.getElementById('resultRankText').textContent = `#${me.rank}`;
    }
  }
}

function onGameEnded(data) {
  console.log('Game Ended:', data);
  showStage('stageSummary');
  if (window.sounds) window.sounds.podium();

  if (data.allPlayers) {
    const me = data.allPlayers.find(p => p.fullName === currentFullName);
    if (me) {
      document.getElementById('summaryRankBadge').textContent = `RANK #${me.rank}`;
    }
  }
}

function onPlayerKicked(data) {
  alert(data.message || 'You have been removed from the session.');
  sessionStorage.clear();
  window.location.reload();
}

function onErrorNotification(message) {
  alert(message);
}

function escapeHtml(str) {
  if (!str) return '';
  return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

async function openPlayerTournamentModal() {
  document.getElementById('playerTournamentModal').classList.add('active');
  const tbody = document.getElementById('playerCumulativeTbody');
  tbody.innerHTML = '<tr><td colspan="5" style="text-align:center; padding:1.5rem; color:var(--text-muted);">Loading standings...</td></tr>';

  try {
    const url = currentHostId ? `/api/leaderboard?hostId=${encodeURIComponent(currentHostId)}` : '/api/leaderboard';
    const res = await fetch(url);
    if (res.ok) {
      const players = await res.json();
      tbody.innerHTML = '';

      if (players.length === 0) {
        tbody.innerHTML = '<tr><td colspan="5" style="text-align:center; padding:1.5rem; color:var(--text-muted);">No records found.</td></tr>';
        return;
      }

      players.forEach((p, idx) => {
        const isMe = (p.fullName === currentFullName);
        const tr = document.createElement('tr');
        tr.className = 'row-card' + (isMe ? ' highlight-me' : '');

        const rankClass = idx === 0 ? 'rank-1' : idx === 1 ? 'rank-2' : idx === 2 ? 'rank-3' : 'rank-other';
        const rankIcon = idx === 0 
          ? `<svg width="15" height="15" viewBox="0 0 24 24" fill="currentColor" stroke="none"><polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"></polygon></svg>#1`
          : `#${idx + 1}`;

        const acc = p.accuracyPercentage || 0;
        const accClass = acc >= 80 ? 'accuracy-high' : acc >= 50 ? 'accuracy-mid' : 'accuracy-low';
        const initials = (p.fullName || 'C').split(' ').map(n => n[0]).slice(0, 2).join('').toUpperCase();

        tr.innerHTML = `
          <td><span class="rank-badge ${rankClass}">${rankIcon}</span></td>
          <td>
            <div class="contender-cell">
              <div class="contender-avatar">${initials}</div>
              <div class="contender-name-text">
                ${escapeHtml(p.fullName)} ${isMe ? '<span style="color:var(--neon-lime); font-size:0.8rem; margin-left:0.3rem;">(YOU)</span>' : ''}
              </div>
            </div>
          </td>
          <td><span class="score-cyber">${(p.totalPointsAllTime || 0).toLocaleString()} PTS</span></td>
          <td style="font-weight: 700; color: #fff;">${p.quizzesPlayed || 0}</td>
          <td><span class="accuracy-pill ${accClass}">${acc}%</span></td>
        `;
        tbody.appendChild(tr);
      });
    }
  } catch (err) {
    console.error('Error fetching standings:', err);
  }
}

function closePlayerTournamentModal() {
  document.getElementById('playerTournamentModal').classList.remove('active');
}
