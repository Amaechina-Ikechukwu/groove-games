// Groove Admin Deck Logic - Secure Auth & Vector Icon System
let currentParsedQuiz = null;

// Modern SVG Icons (Replacing Cartoony Emojis)
const ICONS = {
  trash: `<svg class="icon-svg" viewBox="0 0 24 24"><polyline points="3 6 5 6 21 6"></polyline><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path></svg>`,
  check: `<svg class="icon-svg" style="color: var(--neon-lime);" viewBox="0 0 24 24"><polyline points="20 6 9 17 4 12"></polyline></svg>`,
  cross: `<svg class="icon-svg" style="color: var(--tile-red);" viewBox="0 0 24 24"><line x1="18" y1="6" x2="6" y2="18"></line><line x1="6" y1="6" x2="18" y2="18"></line></svg>`,
  flame: `<svg class="icon-svg" style="color: #FFA502;" viewBox="0 0 24 24"><path d="M8.5 14.5A2.5 2.5 0 0 0 11 12c0-1.38-.5-2-1-3-1.072-2.143-.224-4.054 2-6 .5 2.5 2 4.9 4 6.5 2 1.6 3 3.5 3 5.5a7 7 0 1 1-14 0c0-1.153.433-2.294 1-3a2.5 2.5 0 0 0 2.5 2.5z"></path></svg>`,
  zap: `<svg class="icon-svg" viewBox="0 0 24 24"><polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"></polygon></svg>`,
  play: `<svg class="icon-svg" viewBox="0 0 24 24"><polygon points="5 3 19 12 5 21 5 3"></polygon></svg>`,
  lock: `<svg class="icon-svg" viewBox="0 0 24 24"><rect x="3" y="11" width="18" height="11" rx="2" ry="2"></rect><path d="M7 11V7a5 5 0 0 1 10 0v4"></path></svg>`,
  shield: `<svg class="icon-svg" viewBox="0 0 24 24"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"></path></svg>`,
  save: `<svg class="icon-svg" viewBox="0 0 24 24"><path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"></path><polyline points="17 21 17 13 7 13 7 21"></polyline><polyline points="7 3 7 8 15 8"></polyline></svg>`
};

const SAMPLES = {
  1: `Q1: What does API stand for?
A) Application Programming Interface *
B) Applied Protocol Integration
C) Advanced Programmer Interaction
D) Automated Processing Interface
Time: 15s
Points: 1000

Q2: SignalR uses WebSockets as its primary transport when supported.
[x] True
[ ] False
Time: 15s
Points: 1000`,

  2: `Q1: Which collection is thread-safe in .NET without locks?
A) ConcurrentDictionary *
B) List<T>
C) Dictionary<TKey, TValue>
D) HashSet<T>
Time: 20s
Points: 1000

Q2: What is the default port for secure HTTPS traffic?
A) 80
B) 443 *
C) 8080
D) 22
Time: 15s
Points: 1000

Q3: Which protocol does gRPC utilize for high-throughput binary streaming?
A) HTTP/1.0
B) HTTP/2 *
C) Telnet
D) FTP
Time: 20s
Points: 1000`,

  3: `Q1: ASP.NET Core natively compiles and runs in Linux Docker containers.
[x] True
[ ] False
Time: 10s
Points: 1000

Q2: PostgreSQL supports JSONB columns for lightning fast document queries.
[x] True
[ ] False
Time: 10s
Points: 1000

Q3: SignalR supports automatic reconnection out of the box with backoff retry.
[x] True
[ ] False
Time: 12s
Points: 1000`
};

document.addEventListener('DOMContentLoaded', () => {
  checkAdminAuth();
  insertTemplate(1);
});

// ============================================================================
// ADMIN AUTHENTICATION GUARD
// ============================================================================
function checkAdminAuth() {
  const token = sessionStorage.getItem('groove_admin_token');
  const email = sessionStorage.getItem('groove_admin_email') || 'admin@groove.live';

  const authGate = document.getElementById('adminAuthGate');
  const mainContent = document.getElementById('adminMainContent');
  const userBadge = document.getElementById('adminUserBadge');
  const emailDisplay = document.getElementById('adminEmailDisplay');

  if (token) {
    authGate.style.display = 'none';
    mainContent.style.display = 'block';
    userBadge.style.display = 'inline-flex';
    emailDisplay.textContent = email;

    loadSavedQuizzesList();
    loadPersistentLeaderboard();
    loadUsersList();
  } else {
    authGate.style.display = 'flex';
    mainContent.style.display = 'none';
    userBadge.style.display = 'none';
  }
}

function setAdminCreds(email, password) {
  const emailInput = document.getElementById('adminLoginEmail');
  const passInput = document.getElementById('adminLoginPassword');
  if (emailInput) emailInput.value = email;
  if (passInput) passInput.value = password;
}

async function handleAdminLogin(e) {
  e.preventDefault();
  const email = document.getElementById('adminLoginEmail').value.trim();
  const password = document.getElementById('adminLoginPassword').value.trim();
  const alertBox = document.getElementById('adminAuthAlert');
  alertBox.style.display = 'none';

  try {
    const res = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password, portal: 'Admin', requestedRole: 'Admin' })
    });

    if (res.ok) {
      const auth = await res.json();
      if (auth.role !== 'Admin' && auth.role !== 'SuperAdmin') {
        alertBox.textContent = `Access denied. Account '${auth.email}' has role '${auth.role}', but Administrator privilege is required.`;
        alertBox.style.display = 'block';
        return;
      }

      sessionStorage.setItem('groove_admin_token', auth.token);
      sessionStorage.setItem('groove_admin_email', auth.email);
      checkAdminAuth();
    } else {
      const err = await res.json().catch(() => ({}));
      alertBox.textContent = err.message || 'Authentication failed. Please verify your Administrator credentials.';
      alertBox.style.display = 'block';
    }
  } catch (err) {
    console.error('Admin login error:', err);
    alertBox.textContent = 'Network error while attempting authentication.';
    alertBox.style.display = 'block';
  }
}

function adminLogout() {
  sessionStorage.removeItem('groove_admin_token');
  sessionStorage.removeItem('groove_admin_email');
  checkAdminAuth();
}

function getAuthHeaders() {
  const token = sessionStorage.getItem('groove_admin_token');
  const headers = { 'Content-Type': 'application/json' };
  if (token) {
    headers['Authorization'] = `Bearer ${token}`;
  }
  return headers;
}

// ============================================================================
// TABS & TEMPLATES
// ============================================================================
function switchAdminTab(tabId) {
  document.querySelectorAll('.tab-btn').forEach(btn => btn.classList.remove('active'));
  document.querySelectorAll('.tab-content').forEach(c => c.classList.remove('active'));

  event.currentTarget.classList.add('active');
  const target = document.getElementById(tabId);
  if (target) target.classList.add('active');

  if (tabId === 'tabSaved') loadSavedQuizzesList();
  if (tabId === 'tabLeaderboard') loadPersistentLeaderboard();
  if (tabId === 'tabRoles') loadUsersList();
}

function insertTemplate(num) {
  const text = SAMPLES[num] || '';
  document.getElementById('rawQuizTextInput').value = text;
}

function clearRawText() {
  document.getElementById('rawQuizTextInput').value = '';
  document.getElementById('parsedCardsContainer').innerHTML = `
    <div style="color: var(--text-muted); padding: 3rem 1rem; text-align: center; font-size: 1.1rem;">
      Paste quiz text on the left and click "PARSE WITH ENGINE" to preview questions.
    </div>
  `;
  const saveBtn = document.getElementById('btnSaveToDb');
  if (saveBtn) saveBtn.style.display = 'none';
  document.getElementById('parsedCount').textContent = '0';
  document.getElementById('parseStatusAlert').style.display = 'none';
}

// ============================================================================
// PARSER & PREVIEW
// ============================================================================
async function runParser() {
  const rawText = document.getElementById('rawQuizTextInput').value;
  const title = document.getElementById('quizTitleInput').value.trim() || 'Untitled Quiz';
  const alertBox = document.getElementById('parseStatusAlert');

  if (!rawText.trim()) {
    alert('Please enter or paste quiz questions first.');
    return;
  }

  try {
    const res = await fetch('/api/quizzes/parse', {
      method: 'POST',
      headers: getAuthHeaders(),
      body: JSON.stringify({ rawText, title })
    });

    const data = await res.json();
    alertBox.style.display = 'block';

    if (data.success) {
      alertBox.style.background = 'rgba(0, 230, 118, 0.15)';
      alertBox.style.border = '1px solid var(--neon-lime)';
      alertBox.style.color = 'var(--neon-lime)';
      alertBox.innerHTML = `
        <div style="display: flex; align-items: center; gap: 0.5rem;">
          ${ICONS.check}
          <span>Parsed <strong>${data.quiz.questions.length}</strong> questions successfully!</span>
        </div>`;

      currentParsedQuiz = data.quiz;
      renderParsedPreview(currentParsedQuiz);
      const saveBtn = document.getElementById('btnSaveToDb');
      if (saveBtn) saveBtn.style.display = 'inline-flex';
    } else {
      alertBox.style.background = 'rgba(255, 42, 85, 0.15)';
      alertBox.style.border = '1px solid var(--tile-red)';
      alertBox.style.color = 'var(--tile-red)';
      alertBox.innerHTML = `
        <div style="display: flex; align-items: center; gap: 0.5rem; margin-bottom: 0.4rem;">
          ${ICONS.cross}
          <strong>Errors parsing questions:</strong>
        </div>` + (data.errors || []).map(e => `• ${e}`).join('<br>');
    }
  } catch (err) {
    console.error('Parser request failed:', err);
    alert('Error connecting to backend parser service.');
  }
}

function renderParsedPreview(quiz) {
  const container = document.getElementById('parsedCardsContainer');
  container.innerHTML = '';
  document.getElementById('parsedCount').textContent = quiz.questions.length;

  quiz.questions.forEach((q, qIdx) => {
    const card = document.createElement('div');
    card.className = 'parsed-q-card';
    card.id = `parsedCard_${qIdx}`;

    let choicesHtml = '';
    q.choices.forEach((choice, cIdx) => {
      const isCorrect = (cIdx === q.CorrectIndex || cIdx === q.correctIndex);
      choicesHtml += `
        <div class="parsed-choice-item ${isCorrect ? 'is-correct' : ''}">
          <input type="radio" name="correctChoice_${qIdx}" ${isCorrect ? 'checked' : ''} onchange="setCorrectChoice(${qIdx}, ${cIdx})" style="accent-color: var(--neon-lime); cursor: pointer;">
          <input type="text" class="form-input-cyber" value="${escapeHtml(choice)}" oninput="updateChoiceText(${qIdx}, ${cIdx}, this.value)" style="padding: 0.4rem 0.75rem; font-size: 0.95rem;">
          <button onclick="deleteChoice(${qIdx}, ${cIdx})" class="btn-cyber btn-dark" style="padding: 0.25rem 0.5rem; font-size: 0.8rem; color: var(--tile-red);" title="Delete choice">
            ${ICONS.cross}
          </button>
        </div>
      `;
    });

    card.innerHTML = `
      <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 0.75rem;">
        <span class="hud-pill" style="font-size: 0.85rem;">Q${qIdx + 1}</span>
        <div style="display: flex; gap: 0.5rem; align-items: center;">
          <label style="font-size: 0.8rem; color: var(--text-muted);">Time(s):</label>
          <input type="number" value="${q.timeLimitSeconds || 20}" onchange="updateTimeLimit(${qIdx}, this.value)" class="form-input-cyber" style="width: 70px; padding: 0.25rem 0.5rem; font-size: 0.85rem;">
          <label style="font-size: 0.8rem; color: var(--text-muted); margin-left: 0.25rem;">Pts:</label>
          <input type="number" value="${q.points || 1000}" onchange="updatePoints(${qIdx}, this.value)" class="form-input-cyber" style="width: 80px; padding: 0.25rem 0.5rem; font-size: 0.85rem;">
          <button onclick="deleteQuestion(${qIdx})" class="btn-cyber btn-dark" style="padding: 0.35rem 0.6rem; color: var(--tile-red);" title="Delete Question">
            ${ICONS.trash}
          </button>
        </div>
      </div>

      <input type="text" class="form-input-cyber" value="${escapeHtml(q.text)}" oninput="updateQuestionText(${qIdx}, this.value)" style="margin-bottom: 0.85rem; font-weight: 700;">

      <div id="choicesList_${qIdx}">
        ${choicesHtml}
      </div>

      <div style="margin-top: 0.75rem; text-align: right;">
        <button onclick="addChoice(${qIdx})" class="btn-cyber btn-dark" style="font-size: 0.8rem; padding: 0.35rem 0.75rem;">+ Add Choice</button>
      </div>
    `;

    container.appendChild(card);
  });
}

function updateQuestionText(qIdx, text) {
  if (currentParsedQuiz && currentParsedQuiz.questions[qIdx]) {
    currentParsedQuiz.questions[qIdx].text = text;
  }
}

function updateChoiceText(qIdx, cIdx, text) {
  if (currentParsedQuiz && currentParsedQuiz.questions[qIdx]) {
    currentParsedQuiz.questions[qIdx].choices[cIdx] = text;
  }
}

function setCorrectChoice(qIdx, cIdx) {
  if (currentParsedQuiz && currentParsedQuiz.questions[qIdx]) {
    currentParsedQuiz.questions[qIdx].correctIndex = cIdx;
    renderParsedPreview(currentParsedQuiz);
  }
}

function updateTimeLimit(qIdx, val) {
  if (currentParsedQuiz && currentParsedQuiz.questions[qIdx]) {
    currentParsedQuiz.questions[qIdx].timeLimitSeconds = parseInt(val) || 20;
  }
}

function updatePoints(qIdx, val) {
  if (currentParsedQuiz && currentParsedQuiz.questions[qIdx]) {
    currentParsedQuiz.questions[qIdx].points = parseInt(val) || 1000;
  }
}

function addChoice(qIdx) {
  if (currentParsedQuiz && currentParsedQuiz.questions[qIdx]) {
    if (currentParsedQuiz.questions[qIdx].choices.length >= 6) {
      alert('Max 6 choices supported.');
      return;
    }
    currentParsedQuiz.questions[qIdx].choices.push(`Choice ${currentParsedQuiz.questions[qIdx].choices.length + 1}`);
    renderParsedPreview(currentParsedQuiz);
  }
}

function deleteChoice(qIdx, cIdx) {
  if (currentParsedQuiz && currentParsedQuiz.questions[qIdx]) {
    if (currentParsedQuiz.questions[qIdx].choices.length <= 2) {
      alert('Must have at least 2 choices.');
      return;
    }
    currentParsedQuiz.questions[qIdx].choices.splice(cIdx, 1);
    if (currentParsedQuiz.questions[qIdx].correctIndex >= currentParsedQuiz.questions[qIdx].choices.length) {
      currentParsedQuiz.questions[qIdx].correctIndex = 0;
    }
    renderParsedPreview(currentParsedQuiz);
  }
}

function deleteQuestion(qIdx) {
  if (currentParsedQuiz && currentParsedQuiz.questions) {
    currentParsedQuiz.questions.splice(qIdx, 1);
    renderParsedPreview(currentParsedQuiz);
  }
}

// ============================================================================
// PERSISTENCE & CRUD
// ============================================================================
async function saveParsedQuizToDatabase() {
  if (!currentParsedQuiz || currentParsedQuiz.questions.length === 0) {
    alert('No questions to save.');
    return;
  }

  currentParsedQuiz.title = document.getElementById('quizTitleInput').value.trim() || 'Untitled Quiz';

  try {
    const res = await fetch('/api/quizzes', {
      method: 'POST',
      headers: getAuthHeaders(),
      body: JSON.stringify(currentParsedQuiz)
    });

    if (res.ok) {
      alert('Quiz successfully committed to PostgreSQL database!');
      loadSavedQuizzesList();
      // Switch to saved quizzes tab
      document.querySelectorAll('.tab-btn')[1].click();
    } else {
      const err = await res.json();
      alert(err.message || 'Error saving quiz.');
    }
  } catch (err) {
    console.error(err);
    alert('Network error while committing quiz.');
  }
}

async function loadSavedQuizzesList() {
  try {
    const res = await fetch('/api/quizzes', { headers: getAuthHeaders() });
    if (res.ok) {
      const quizzes = await res.json();
      document.getElementById('savedQuizCountBadge').textContent = quizzes.length;

      const grid = document.getElementById('savedQuizzesGrid');
      grid.innerHTML = '';

      if (quizzes.length === 0) {
        grid.innerHTML = '<div style="color: var(--text-muted); font-size: 1.2rem; padding: 2rem;">No quizzes found. Ingest one using the parser tab.</div>';
        return;
      }

      quizzes.forEach(q => {
        const card = document.createElement('div');
        card.className = 'cyber-card';
        card.style.padding = '1.75rem';
        card.innerHTML = `
          <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 0.75rem;">
            <span class="hud-pill" style="font-size: 0.8rem;">${q.questions.length} QUESTIONS</span>
            <span style="color: var(--text-muted); font-size: 0.85rem;">${new Date(q.createdAt).toLocaleDateString()}</span>
          </div>

          <h3 style="font-family: var(--font-display); font-size: 1.4rem; font-weight: 900; text-transform: uppercase; margin-bottom: 0.5rem;">
            ${escapeHtml(q.title)}
          </h3>
          <p style="color: var(--text-secondary); font-size: 0.9rem; margin-bottom: 1.5rem;">
            Created by: ${escapeHtml(q.createdBy || 'Admin')}
          </p>

          <div style="display: flex; gap: 0.75rem;">
            <button onclick="launchRoomDirectly('${q.id}')" class="btn-cyber btn-lime" style="flex: 1; font-size: 0.9rem; padding: 0.65rem 1rem; display: inline-flex; align-items: center; justify-content: center; gap: 0.4rem;">
              ${ICONS.play} LAUNCH ARENA
            </button>
            <button onclick="deleteQuizBank('${q.id}')" class="btn-cyber btn-dark" style="color: var(--tile-red); padding: 0.65rem 1rem;" title="Delete Quiz">
              ${ICONS.trash}
            </button>
          </div>
        `;
        grid.appendChild(card);
      });
    }
  } catch (err) {
    console.error('Error loading saved quizzes:', err);
  }
}

async function launchRoomDirectly(quizId) {
  try {
    const res = await fetch('/api/sessions', {
      method: 'POST',
      headers: getAuthHeaders(),
      body: JSON.stringify({
        quizId: quizId,
        hostId: 'admin_launcher',
        autoAdvance: false
      })
    });

    if (res.ok) {
      const data = await res.json();
      window.location.href = `/host.html?pin=${encodeURIComponent(data.pin)}`;
    } else {
      alert('Failed to launch room.');
    }
  } catch (e) {
    console.error(e);
  }
}

async function deleteQuizBank(quizId) {
  if (confirm('Permanently delete this quiz bank?')) {
    try {
      const res = await fetch(`/api/quizzes/${quizId}`, {
        method: 'DELETE',
        headers: getAuthHeaders()
      });
      if (res.ok) {
        loadSavedQuizzesList();
      }
    } catch (e) {
      console.error(e);
    }
  }
}

async function loadPersistentLeaderboard() {
  const search = document.getElementById('leaderboardSearchInput')?.value || '';
  const org = document.getElementById('leaderboardOrgSelect')?.value || '';

  try {
    const res = await fetch(`/api/leaderboard?search=${encodeURIComponent(search)}&organizationId=${encodeURIComponent(org)}`, {
      headers: getAuthHeaders()
    });
    if (res.ok) {
      const players = await res.json();
      const tbody = document.getElementById('leaderboardTableBody');
      tbody.innerHTML = '';

      if (players.length === 0) {
        tbody.innerHTML = '<tr><td colspan="9" style="text-align: center; color: var(--text-muted); padding: 2rem;">No players registered yet.</td></tr>';
        return;
      }

      players.forEach((p, idx) => {
        const tr = document.createElement('tr');
        tr.className = 'row-card';

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
              <div class="contender-name-text">${escapeHtml(p.fullName)}</div>
            </div>
          </td>
          <td><span class="hud-pill" style="font-size: 0.75rem;">${escapeHtml(p.organizationId || 'global')}</span></td>
          <td style="color: var(--text-muted); font-size: 0.85rem;">${escapeHtml(p.identifier || '—')}</td>
          <td><span class="score-cyber">${(p.totalPointsAllTime || 0).toLocaleString()} PTS</span></td>
          <td style="font-weight: 700; color: #fff;">${p.quizzesPlayed || 0}</td>
          <td><span class="accuracy-pill ${accClass}">${acc}%</span></td>
          <td><span class="streak-chip">${ICONS.flame} ${p.highestStreak || 0}</span></td>
          <td style="color: var(--text-muted); font-size: 0.85rem;">${new Date(p.lastActive).toLocaleDateString()}</td>
        `;
        tbody.appendChild(tr);
      });
    }
  } catch (err) {
    console.error('Error loading leaderboard:', err);
  }
}

async function resetLeaderboardConfirm() {
  if (confirm('CAUTION: Are you sure you want to clear the persistent cumulative leaderboard? This cannot be undone.')) {
    try {
      const res = await fetch('/api/leaderboard/reset', {
        method: 'POST',
        headers: getAuthHeaders()
      });
      if (res.ok) {
        alert('Leaderboard reset successfully.');
        loadPersistentLeaderboard();
      }
    } catch (e) {
      console.error(e);
    }
  }
}

async function loadUsersList() {
  try {
    const res = await fetch('/api/auth/users', { headers: getAuthHeaders() });
    if (res.ok) {
      const users = await res.json();
      const container = document.getElementById('usersListContainer');
      container.innerHTML = '';

      users.forEach(u => {
        const card = document.createElement('div');
        card.style.background = 'var(--bg-surface-elevated)';
        card.style.border = '1px solid var(--border-cyber)';
        card.style.borderRadius = 'var(--radius-button)';
        card.style.padding = '1rem 1.25rem';
        card.style.marginBottom = '0.75rem';
        card.style.display = 'flex';
        card.style.justifyContent = 'space-between';
        card.style.alignItems = 'center';

        card.innerHTML = `
          <div>
            <div style="font-family: var(--font-display); font-weight: 800; font-size: 1.1rem;">
              ${escapeHtml(u.displayName || u.email)}
            </div>
            <div style="color: var(--text-muted); font-size: 0.85rem;">
              ${escapeHtml(u.email)}
            </div>
          </div>
          <div>
            <span class="hud-pill score-pill" style="font-size: 0.85rem;">
              ${escapeHtml(u.role)}
            </span>
          </div>
        `;
        container.appendChild(card);
      });
    }
  } catch (err) {
    console.error('Error loading users:', err);
  }
}

async function handleRoleAssign() {
  const email = document.getElementById('assignEmailInput').value.trim();
  const role = document.getElementById('assignRoleSelect').value;

  if (!email) {
    alert('Please enter user email.');
    return;
  }

  try {
    const res = await fetch('/api/auth/assign-role', {
      method: 'POST',
      headers: getAuthHeaders(),
      body: JSON.stringify({ email, role })
    });

    if (res.ok) {
      alert(`Role '${role}' assigned to ${email}`);
      loadUsersList();
    } else {
      alert('Failed to assign role.');
    }
  } catch (e) {
    console.error(e);
  }
}

function escapeHtml(str) {
  if (!str) return '';
  return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}
