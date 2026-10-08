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

document.addEventListener('DOMContentLoaded', () => {
  // Check URL params for quick join
  const params = new URLSearchParams(window.location.search);
  const pinParam = params.get('pin');
  if (pinParam) {
    document.getElementById('joinPin').value = pinParam.trim().toUpperCase();
  }

  // Restore stored session if exists
  const savedPin = sessionStorage.getItem('groove_pin') || sessionStorage.getItem('triviasync_pin');
  const savedName = sessionStorage.getItem('groove_name') || sessionStorage.getItem('triviasync_name');
  const savedId = sessionStorage.getItem('groove_id') || sessionStorage.getItem('triviasync_id');

  if (savedPin && savedName) {
    document.getElementById('joinPin').value = savedPin;
    document.getElementById('joinName').value = savedName;
    if (savedId) document.getElementById('joinIdentifier').value = savedId;
  }

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
    streakMsg.textContent = `🔥 WINNING STREAK: ${result.streak}`;
    streakMsg.style.display = 'inline-flex';
  } else {
    document.getElementById('streakBadge').style.display = 'none';
    streakMsg.style.display = 'none';
  }

  if (result.isCorrect) {
    iconEl.textContent = '✅';
    headingEl.textContent = 'CORRECT!';
    headingEl.style.color = 'var(--neon-lime)';
    pointsEl.textContent = `+${result.pointsEarned} PTS`;
    pointsEl.style.color = 'var(--neon-lime)';
    if (window.sounds) window.sounds.correct();
  } else {
    iconEl.textContent = '❌';
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
        tr.className = 'row-card';
        if (isMe) {
          tr.style.border = '2px solid var(--neon-lime)';
          tr.style.background = 'rgba(212, 255, 0, 0.12)';
        }
        tr.innerHTML = `
          <td><strong style="color: ${idx === 0 ? 'var(--neon-lime)' : '#fff'};">#${idx + 1}</strong></td>
          <td><strong style="font-family:var(--font-display); ${isMe ? 'color:var(--neon-lime);' : ''}">${escapeHtml(p.fullName)} ${isMe ? '(You)' : ''}</strong></td>
          <td><strong style="color:var(--neon-lime);">${p.totalPointsAllTime.toLocaleString()}</strong></td>
          <td>${p.quizzesPlayed}</td>
          <td><span style="color:#2ecc71;">${p.accuracyPercentage}%</span></td>
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
