// Self-paced tournament play. The server times every question; this page only displays the clock.
(function () {
  'use strict';

  const $ = id => document.getElementById(id);
  const sessionId = new URLSearchParams(location.search).get('session') || '';

  let info = null;          // intro details
  let state = null;         // latest PlayState from the server
  let question = null;      // question currently on screen
  let choices = [];         // its choices, kept for the feedback screen
  let deadlineAt = 0;       // local time the on-screen clock hits zero
  let clock = null;
  let submitting = false;
  let stage = 'stageLoading';

  function show(id) {
    stage = id;
    document.querySelectorAll('.stage').forEach(s => s.classList.toggle('is-active', s.id === id));
    window.scrollTo(0, 0);
  }

  function message(title, text, actions = '') {
    clearInterval(clock);
    $('msgTitle').textContent = title;
    $('msgText').textContent = text;
    $('msgActions').innerHTML = actions;
    show('stageMessage');
  }

  function standingsLink() {
    return `/tournament.html?id=${encodeURIComponent((info || state).tournamentId)}`;
  }

  function setScore(score) {
    $('barScore').hidden = false;
    $('barScore').textContent = `${UI.formatNumber(score)} pts`;
  }

  // ---------------------------------------------------------------------------
  // Loading & starting
  // ---------------------------------------------------------------------------
  async function load() {
    if (!sessionId) return message('Session not found', 'This link is missing the session.', '<a class="btn btn-primary" href="/tournaments.html">Your tournaments</a>');
    if (!Session.get()) {
      message('Sign in to play', 'This session is part of a tournament, so your score is saved to your account.',
        '<button class="btn btn-primary" data-signin>Sign in</button>');
      document.querySelector('[data-signin]').onclick = () => UI.signIn();
      return;
    }

    try {
      info = await UI.api(`/api/play/${encodeURIComponent(sessionId)}`);
    } catch (err) {
      if (err.status === 403) {
        return message('You\'re not in this tournament', 'Ask the host for the tournament code, then join it from Your tournaments.',
          '<a class="btn btn-primary" href="/tournaments.html">Join with a code</a>');
      }
      return message('Session not found', err.message, '<a class="btn btn-primary" href="/tournaments.html">Your tournaments</a>');
    }

    document.title = `${info.title} · Groove`;
    if (info.mode !== 'SelfPaced') {
      return message('This one is played live', 'Your host runs this session on a shared screen. Join with the PIN they show you.',
        '<a class="btn btn-primary" href="/player.html">Enter a PIN</a>');
    }
    if (info.done) return showDone(info);
    if (info.status === 'Draft') {
      return message('Not open yet', 'Your host hasn\'t opened this session. Check back later.', `<a class="btn btn-secondary" href="${standingsLink()}">Back to tournament</a>`);
    }
    if (info.status === 'Closed') {
      return message('This session has closed', info.started ? `You scored ${UI.formatNumber(info.score)} points before it closed.` : 'The deadline has passed.',
        `<a class="btn btn-primary" href="${standingsLink()}">See standings</a>`);
    }

    $('introTournament').textContent = info.tournamentName;
    $('introTitle').textContent = info.title;
    $('introMeta').textContent = `${info.totalQuestions} questions · closes ${UI.relativeTime(info.closesAt)} (${UI.formatDateTime(info.closesAt)})`;
    $('startButton').textContent = info.started ? `Continue (question ${info.answered + 1} of ${info.totalQuestions})` : 'Start';
    if (info.started) setScore(info.score);
    show('stageIntro');
  }

  async function next(button) {
    if (window.sounds) window.sounds.init();
    try {
      state = await UI.busy(button, () => UI.api(`/api/play/${encodeURIComponent(sessionId)}/start`, { method: 'POST' }));
      render();
    } catch (err) {
      handleError(err);
    }
  }

  function handleError(err) {
    if (err.status === 409) {
      return message('This session has closed', `${err.message}`, `<a class="btn btn-primary" href="${standingsLink()}">See standings</a>`);
    }
    UI.toast(err.message, 'error');
  }

  function render() {
    setScore(state.score);
    if (state.done) return showDone(state);
    if (!state.question) {
      return message('This session has closed', 'Your points so far still count.', `<a class="btn btn-primary" href="${standingsLink()}">See standings</a>`);
    }
    showQuestion(state.question);
  }

  // ---------------------------------------------------------------------------
  // Question
  // ---------------------------------------------------------------------------
  function showQuestion(q) {
    question = q;
    choices = q.choices;
    submitting = false;
    $('progressBar').style.width = `${(q.index / state.totalQuestions) * 100}%`;
    $('questionLabel').textContent = `Question ${q.number} of ${state.totalQuestions}`;
    $('questionText').textContent = q.text;
    $('answerGrid').innerHTML = q.choices.map((c, i) => `
      <button type="button" class="answer answer-${i % 6}" data-index="${i}" aria-label="${UI.SHAPES[i % 6].name}: ${UI.escape(c)}">
        ${UI.shape(i)}<span class="answer-text">${UI.escape(c)}</span>
      </button>`).join('');
    $('answerGrid').querySelectorAll('.answer').forEach(b => { b.onclick = () => answer(Number(b.dataset.index)); });

    deadlineAt = Date.now() + q.remaining * 1000;
    clearInterval(clock);
    let lastWhole = null;
    const tick = () => {
      const remaining = Math.max(0, (deadlineAt - Date.now()) / 1000);
      const whole = Math.ceil(remaining);
      const t = $('questionTimer');
      t.querySelector('span').textContent = whole;
      t.style.setProperty('--p', remaining / q.timeLimit);
      t.classList.toggle('is-urgent', whole <= 5);
      if (whole !== lastWhole && whole > 0 && window.sounds) whole <= 5 ? window.sounds.hurryTick() : null;
      lastWhole = whole;
      if (remaining <= 0) {
        clearInterval(clock);
        answer(-1);
      }
    };
    tick();
    clock = setInterval(tick, 100);
    show('stageQuestion');
  }

  async function answer(choice) {
    if (submitting || !question) return;
    submitting = true;
    clearInterval(clock);
    if (window.sounds && choice >= 0) window.sounds.click();
    $('answerGrid').querySelectorAll('.answer').forEach((b, i) => {
      b.disabled = true;
      b.classList.add(i === choice ? 'is-selected' : 'is-dimmed');
    });

    try {
      const res = await UI.api(`/api/play/${encodeURIComponent(sessionId)}/answer`, {
        method: 'POST', body: { questionIndex: question.index, choiceIndex: choice },
      });
      state = res.state;
      if (!res.result) {
        // Already answered elsewhere (another tab or a retry): just carry on.
        return state.done ? showDone(state) : next();
      }
      showFeedback(res.result, choice);
    } catch (err) {
      submitting = false;
      if (err.status === 409) return handleError(err);
      UI.toast("Your answer didn't go through. Tap it again.", 'error');
      $('answerGrid').querySelectorAll('.answer').forEach(b => { b.disabled = false; b.classList.remove('is-selected', 'is-dimmed'); });
      // Restart the visible clock; the server still holds the real start time.
      showQuestion(Object.assign({}, question, { remaining: Math.max(0, (deadlineAt - Date.now()) / 1000) }));
    }
  }

  function showFeedback(result, choice) {
    setScore(state.score);
    const icon = $('fbIcon');
    icon.className = `result-icon ${result.isCorrect ? 'is-correct' : 'is-wrong'}`;
    icon.innerHTML = UI.icon(result.isCorrect ? 'check' : 'x');
    $('fbTitle').textContent = result.isCorrect ? 'Correct' : result.timedOut ? "Time's up" : 'Not quite';
    $('fbPoints').textContent = `+${UI.formatNumber(result.pointsEarned)}`;
    $('fbPoints').style.color = result.isCorrect ? 'var(--success)' : 'var(--text-3)';
    $('fbAnswer').innerHTML = result.isCorrect ? '' : `
      <p class="subtle mb-2">The answer was</p>
      <div class="answer answer-${result.correctIndex % 6}" style="min-height: 64px;">${UI.shape(result.correctIndex)}<span class="answer-text">${UI.escape(choices[result.correctIndex])}</span></div>`;
    $('nextButton').textContent = state.done ? 'See your result' : 'Next question';
    show('stageFeedback');
    $('nextButton').focus();
    if (window.sounds) result.isCorrect ? window.sounds.correct() : window.sounds.wrong();
  }

  function showDone(s) {
    clearInterval(clock);
    setScore(s.score);
    $('doneScore').textContent = UI.formatNumber(s.score);
    $('doneCorrect').textContent = `${s.correctCount}/${s.totalQuestions}`;
    $('doneText').textContent = `You finished ${s.title}.`;
    $('doneStandings').href = standingsLink();
    show('stageDone');
    if (window.sounds) window.sounds.podium();
  }

  // ---------------------------------------------------------------------------
  // Boot
  // ---------------------------------------------------------------------------
  document.addEventListener('DOMContentLoaded', () => {
    $('startButton').onclick = e => next(e.currentTarget);
    $('nextButton').onclick = e => (state && state.done ? showDone(state) : next(e.currentTarget));

    const inProgress = () => stage === 'stageQuestion' || stage === 'stageFeedback';
    $('brandLink').addEventListener('click', async e => {
      if (!inProgress()) return;
      e.preventDefault();
      const ok = await UI.confirm({
        title: 'Leave this session?',
        message: 'You can come back before the deadline and pick up where you left off. The clock on this question keeps running while you are away.',
        confirmText: 'Leave',
        cancelText: 'Keep playing',
        danger: true,
      });
      if (ok) location.href = '/';
    });
    window.addEventListener('beforeunload', e => {
      if (stage === 'stageQuestion' && !submitting) {
        e.preventDefault();
        e.returnValue = '';
      }
    });

    Session.onChange(load);
    load();
  });
})();
