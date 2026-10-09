// Shared UI primitives: icons, dialogs, toasts, session, API helpers, sign-in dialog.
(function () {
  'use strict';

  // ---------------------------------------------------------------------------
  // Icons & answer shapes
  // ---------------------------------------------------------------------------
  const ICON_PATHS = {
    x: '<path d="M18 6 6 18M6 6l12 12"/>',
    check: '<path d="M20 6 9 17l-5-5"/>',
    alert: '<path d="M12 9v4m0 4h.01M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z"/>',
    info: '<circle cx="12" cy="12" r="10"/><path d="M12 16v-4m0-4h.01"/>',
    logout: '<path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4M16 17l5-5-5-5M21 12H9"/>',
    trash: '<path d="M3 6h18M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>',
    download: '<path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4M7 10l5 5 5-5M12 15V3"/>',
    copy: '<rect x="9" y="9" width="13" height="13" rx="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/>',
    qr: '<rect x="3" y="3" width="7" height="7" rx="1"/><rect x="14" y="3" width="7" height="7" rx="1"/><rect x="3" y="14" width="7" height="7" rx="1"/><path d="M14 14h3v3h-3zM20 14v.01M14 20h.01M17 17h3v3h-3z"/>',
    users: '<path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75"/>',
    play: '<path d="M6 4l14 8-14 8z"/>',
    next: '<path d="M5 12h14M12 5l7 7-7 7"/>',
    back: '<path d="M15 18l-6-6 6-6"/>',
    stop: '<rect x="5" y="5" width="14" height="14" rx="2"/>',
    refresh: '<path d="M21 12a9 9 0 1 1-2.64-6.36L21 8M21 3v5h-5"/>',
    plus: '<path d="M12 5v14M5 12h14"/>',
    help: '<circle cx="12" cy="12" r="10"/><path d="M9.1 9a3 3 0 0 1 5.8 1c0 2-3 3-3 3M12 17h.01"/>',
    trophy: '<path d="M6 9H4.5a2.5 2.5 0 0 1 0-5H6M18 9h1.5a2.5 2.5 0 0 0 0-5H18M4 22h16M10 14.66V17c0 .55-.45 1-1 1H7v4M14 14.66V17c0 .55.45 1 1 1h2v4M18 2H6v7a6 6 0 0 0 12 0V2z"/>',
    flame: '<path d="M8.5 14.5A2.5 2.5 0 0 0 11 12c0-1.38-.5-2-1-3-1.07-2.14-.22-4.05 2-6 .5 2.5 2 4.9 4 6.5 2 1.6 3 3.5 3 5.5a7 7 0 1 1-14 0c0-1.15.43-2.29 1-3a2.5 2.5 0 0 0 2.5 2.5z"/>',
    user: '<path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/>',
    link: '<path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71"/>',
    edit: '<path d="M12 20h9M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4z"/>',
    home: '<path d="M3 10.5 12 3l9 7.5V20a1 1 0 0 1-1 1h-5v-6H9v6H4a1 1 0 0 1-1-1z"/>',
    gamepad: '<rect x="2" y="6" width="20" height="12" rx="4"/><path d="M6 12h4M8 10v4M15 11h.01M18 13h.01"/>',
    screen: '<rect x="2" y="3" width="20" height="14" rx="2"/><path d="M8 21h8M12 17v4"/>',
  };

  function icon(name, cls = '') {
    return `<svg class="icon ${cls}" viewBox="0 0 24 24" aria-hidden="true">${ICON_PATHS[name] || ''}</svg>`;
  }

  // Answer shapes: answers are told apart by shape as well as color (color-blind safe).
  const SHAPES = [
    { name: 'Triangle', path: '<path d="M12 3 22 20H2z"/>' },
    { name: 'Diamond', path: '<path d="M12 2 22 12 12 22 2 12z"/>' },
    { name: 'Circle', path: '<circle cx="12" cy="12" r="10"/>' },
    { name: 'Square', path: '<rect x="3" y="3" width="18" height="18" rx="2"/>' },
    { name: 'Hexagon', path: '<path d="M7 3h10l5 9-5 9H7l-5-9z"/>' },
    { name: 'Star', path: '<path d="m12 2 3.1 6.3 6.9 1-5 4.9 1.2 6.8L12 17.8 5.8 21l1.2-6.8-5-4.9 6.9-1z"/>' },
  ];

  function shape(index) {
    const s = SHAPES[index % SHAPES.length];
    return `<svg class="shape" viewBox="0 0 24 24" aria-hidden="true">${s.path}</svg>`;
  }

  function escape(value) {
    return String(value ?? '')
      .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
  }

  function initials(name) {
    return String(name || '?').trim().split(/\s+/).map(p => p[0]).slice(0, 2).join('').toUpperCase();
  }

  const AVATAR_COLORS = ['var(--a0)', 'var(--a1)', 'var(--a3)', 'var(--a4)', 'var(--a5)', '#5B6B7A'];
  function avatar(name) {
    let h = 0;
    for (const ch of String(name)) h = (h * 31 + ch.charCodeAt(0)) >>> 0;
    return `<span class="avatar" style="background:${AVATAR_COLORS[h % AVATAR_COLORS.length]}">${escape(initials(name))}</span>`;
  }

  function rankBadge(rank) {
    return `<span class="rank ${rank <= 3 ? 'rank-' + rank : ''}">${rank}</span>`;
  }

  function formatNumber(n) {
    return Number(n || 0).toLocaleString();
  }

  /**
   * A date and time as a <time> element: "9 Oct 2026, 10:20" in the viewer's time zone,
   * with the exact UTC time on hover. Returns an HTML string (the value is escaped).
   */
  function stamp(value) {
    const d = value ? new Date(value) : null;
    if (!d || isNaN(d)) return '';
    const text = d.toLocaleString([], { day: 'numeric', month: 'short', year: 'numeric', hour: 'numeric', minute: '2-digit' });
    return `<time datetime="${d.toISOString()}" title="${escape(d.toUTCString())}">${escape(text)}</time>`;
  }

  function formatDateTime(value) {
    return new Date(value).toLocaleString([], { weekday: 'short', month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' });
  }

  /** "in 3 days", "in 2 hours", "5 minutes ago". */
  function relativeTime(value) {
    const diff = new Date(value).getTime() - Date.now();
    const abs = Math.abs(diff);
    const units = [['day', 86400000], ['hour', 3600000], ['minute', 60000]];
    for (const [unit, ms] of units) {
      if (abs >= ms || unit === 'minute') {
        const n = Math.max(1, Math.round(abs / ms));
        const label = `${n} ${unit}${n === 1 ? '' : 's'}`;
        return diff >= 0 ? `in ${label}` : `${label} ago`;
      }
    }
    return '';
  }

  /** Value for a datetime-local input, in the viewer's local time. */
  function toLocalInput(date) {
    const d = new Date(date);
    const pad = n => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
  }

  // ---------------------------------------------------------------------------
  // Toasts
  // ---------------------------------------------------------------------------
  function toast(message, type = 'info', duration = 3200) {
    let host = document.querySelector('.toasts');
    if (!host) {
      host = document.createElement('div');
      host.className = 'toasts';
      host.setAttribute('role', 'status');
      host.setAttribute('aria-live', 'polite');
      document.body.appendChild(host);
    }
    const el = document.createElement('div');
    el.className = `toast toast-${type}`;
    const iconName = type === 'success' ? 'check' : type === 'error' ? 'alert' : 'info';
    el.innerHTML = `${icon(iconName)}<span>${escape(message)}</span>`;
    host.appendChild(el);
    setTimeout(() => {
      el.classList.add('is-leaving');
      setTimeout(() => el.remove(), 200);
    }, duration);
  }

  // ---------------------------------------------------------------------------
  // Dialogs
  // ---------------------------------------------------------------------------
  const FOCUSABLE = 'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

  /**
   * Opens a modal dialog. `render(dialogEl, close)` fills it. Returns { el, close, done }
   * where `done` resolves with whatever value close() receives (undefined on Escape/backdrop).
   */
  function openDialog({ render, wide = false, dismissible = true, labelledBy } = {}) {
    const previouslyFocused = document.activeElement;
    const backdrop = document.createElement('div');
    backdrop.className = 'dialog-backdrop';
    const dialog = document.createElement('div');
    dialog.className = 'dialog' + (wide ? ' dialog-wide' : '');
    dialog.setAttribute('role', 'dialog');
    dialog.setAttribute('aria-modal', 'true');
    if (labelledBy) dialog.setAttribute('aria-labelledby', labelledBy);
    backdrop.appendChild(dialog);

    let resolveDone;
    const done = new Promise(r => { resolveDone = r; });
    let closed = false;

    function close(value) {
      if (closed) return;
      closed = true;
      document.removeEventListener('keydown', onKey, true);
      backdrop.remove();
      document.body.style.overflow = '';
      if (previouslyFocused && previouslyFocused.focus) previouslyFocused.focus();
      resolveDone(value);
    }

    function onKey(e) {
      if (e.key === 'Escape' && dismissible) {
        e.stopPropagation();
        close(undefined);
      } else if (e.key === 'Tab') {
        const items = [...dialog.querySelectorAll(FOCUSABLE)].filter(el => el.offsetParent !== null);
        if (!items.length) return;
        const first = items[0], last = items[items.length - 1];
        if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
        else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
      }
    }

    if (dismissible) {
      backdrop.addEventListener('mousedown', e => { if (e.target === backdrop) close(undefined); });
    }
    document.addEventListener('keydown', onKey, true);
    document.body.appendChild(backdrop);
    document.body.style.overflow = 'hidden';

    render(dialog, close);

    const autofocus = dialog.querySelector('[autofocus]') || dialog.querySelector(FOCUSABLE);
    if (autofocus) setTimeout(() => autofocus.focus(), 0);

    return { el: dialog, close, done };
  }

  let dialogSeq = 0;

  /**
   * Confirmation dialog. Resolves true only on explicit confirmation.
   * Options: title, message (HTML-escaped), html (trusted HTML), confirmText, cancelText,
   * danger (red confirm button, focus starts on Cancel), requireText (user must type it).
   */
  function confirm(opts) {
    const {
      title = 'Are you sure?', message = '', html = '', confirmText = 'Confirm', cancelText = 'Cancel',
      danger = false, requireText = '',
    } = opts || {};
    const id = `dlg-title-${++dialogSeq}`;

    const { done } = openDialog({
      labelledBy: id,
      render(el, close) {
        el.innerHTML = `
          <h2 class="dialog-title" id="${id}">${escape(title)}</h2>
          <div class="dialog-body">${html || (message ? `<p>${escape(message)}</p>` : '')}</div>
          ${requireText ? `
            <div class="field mt-4">
              <label class="label" for="${id}-input">Type <strong>${escape(requireText)}</strong> to confirm</label>
              <input class="input" id="${id}-input" autocomplete="off" spellcheck="false">
            </div>` : ''}
          <div class="dialog-actions">
            <button type="button" class="btn btn-secondary" data-cancel ${danger && !requireText ? 'autofocus' : ''}>${escape(cancelText)}</button>
            <button type="button" class="btn ${danger ? 'btn-danger' : 'btn-primary'}" data-confirm ${!danger && !requireText ? 'autofocus' : ''} ${requireText ? 'disabled' : ''}>${escape(confirmText)}</button>
          </div>`;
        const confirmBtn = el.querySelector('[data-confirm]');
        el.querySelector('[data-cancel]').onclick = () => close(false);
        confirmBtn.onclick = () => close(true);
        if (requireText) {
          const input = el.querySelector('input');
          input.setAttribute('autofocus', '');
          input.addEventListener('input', () => {
            confirmBtn.disabled = input.value.trim().toLowerCase() !== requireText.toLowerCase();
          });
          input.addEventListener('keydown', e => { if (e.key === 'Enter' && !confirmBtn.disabled) close(true); });
        }
      },
    });
    return done.then(v => v === true);
  }

  /** Informational dialog with a single button. */
  function notice({ title, message = '', html = '', buttonText = 'OK', dismissible = true }) {
    const id = `dlg-title-${++dialogSeq}`;
    const { done } = openDialog({
      labelledBy: id,
      dismissible,
      render(el, close) {
        el.innerHTML = `
          <h2 class="dialog-title" id="${id}">${escape(title)}</h2>
          <div class="dialog-body">${html || `<p>${escape(message)}</p>`}</div>
          <div class="dialog-actions"><button type="button" class="btn btn-primary" autofocus>${escape(buttonText)}</button></div>`;
        el.querySelector('button').onclick = () => close(true);
      },
    });
    return done;
  }

  // ---------------------------------------------------------------------------
  // Session (one shared sign-in across pages)
  // ---------------------------------------------------------------------------
  const SESSION_KEY = 'groove.session';
  // Clean up keys from older builds that kept a separate login per page.
  ['groove_admin_token', 'groove_admin_email', 'groove_host_token', 'groove_host_email',
   'groove_player_token', 'groove_player_email', 'groove_player_name'].forEach(k => {
    try { sessionStorage.removeItem(k); } catch (_) { /* storage unavailable */ }
  });

  function decodeExpiry(token) {
    try {
      const payload = JSON.parse(atob(token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')));
      return payload.exp ? payload.exp * 1000 : 0;
    } catch (_) {
      return 0;
    }
  }

  const listeners = new Set();
  const Session = {
    get() {
      try {
        const s = JSON.parse(localStorage.getItem(SESSION_KEY) || 'null');
        if (s && s.token && decodeExpiry(s.token) > Date.now()) return s;
        if (s) localStorage.removeItem(SESSION_KEY);
      } catch (_) { /* storage unavailable or corrupt */ }
      return null;
    },
    set(auth) {
      const s = { token: auth.token, email: auth.email, displayName: auth.displayName, role: auth.role };
      try { localStorage.setItem(SESSION_KEY, JSON.stringify(s)); } catch (_) { /* ignore */ }
      listeners.forEach(fn => fn(s));
      return s;
    },
    clear() {
      try { localStorage.removeItem(SESSION_KEY); } catch (_) { /* ignore */ }
      listeners.forEach(fn => fn(null));
    },
    onChange(fn) { listeners.add(fn); },
    isAdmin(s = Session.get()) { return !!s && (s.role === 'Admin' || s.role === 'SuperAdmin'); },
    canHost(s = Session.get()) { return !!s && (s.role === 'Host' || Session.isAdmin(s)); },
  };

  // Other tabs signing in/out should be reflected here too.
  window.addEventListener('storage', e => {
    if (e.key === SESSION_KEY) listeners.forEach(fn => fn(Session.get()));
  });

  // ---------------------------------------------------------------------------
  // API
  // ---------------------------------------------------------------------------
  class ApiError extends Error {
    constructor(message, status) { super(message); this.status = status; }
  }

  async function api(url, { method = 'GET', body, auth = true } = {}) {
    const headers = {};
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    const session = auth ? Session.get() : null;
    if (session) headers.Authorization = `Bearer ${session.token}`;

    let res;
    try {
      res = await fetch(url, { method, headers, body: body !== undefined ? JSON.stringify(body) : undefined });
    } catch (_) {
      throw new ApiError("Can't reach the server. Check your connection and try again.", 0);
    }

    if (res.status === 401 && session) {
      Session.clear();
      throw new ApiError('Your session has expired. Sign in again.', 401);
    }

    const text = await res.text();
    let data = null;
    try { data = text ? JSON.parse(text) : null; } catch (_) { data = null; }

    if (!res.ok) {
      const fallback = res.status === 403 ? "You don't have permission to do that."
        : res.status === 404 ? 'Not found.'
        : res.status === 429 ? 'Too many attempts. Wait a minute and try again.'
        : 'Something went wrong. Please try again.';
      throw new ApiError((data && data.message) || fallback, res.status);
    }
    return data;
  }

  /** Downloads a file from an authenticated endpoint. */
  async function download(url, fallbackName) {
    const session = Session.get();
    const res = await fetch(url, { headers: session ? { Authorization: `Bearer ${session.token}` } : {} });
    if (!res.ok) {
      throw new ApiError(res.status === 403 || res.status === 401 ? "You don't have permission to export this." : 'Export failed.', res.status);
    }
    const blob = await res.blob();
    const disposition = res.headers.get('Content-Disposition') || '';
    const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = match ? decodeURIComponent(match[1]) : fallbackName;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(a.href), 1000);
  }

  /** Puts a button into a loading state while `fn` runs. */
  async function busy(button, fn) {
    if (!button) return fn();
    button.classList.add('is-loading');
    button.disabled = true;
    try {
      return await fn();
    } finally {
      button.classList.remove('is-loading');
      button.disabled = false;
    }
  }

  async function copyText(text, successMessage = 'Copied') {
    try {
      await navigator.clipboard.writeText(text);
      toast(successMessage, 'success');
    } catch (_) {
      notice({ title: 'Copy this', html: `<input class="input" readonly value="${escape(text)}" onfocus="this.select()">` });
    }
  }

  // ---------------------------------------------------------------------------
  // Sign-in / sign-up dialog
  // ---------------------------------------------------------------------------
  /**
   * Opens the account dialog. Resolves with the session on success, or null if dismissed.
   * Options: title, description, signupRole ('Player' | 'Host'), allowSignup, startWith ('signin' | 'signup').
   */
  function signIn(opts = {}) {
    const {
      title = 'Sign in', description = '', signupRole = 'Player', allowSignup = true, startWith = 'signin',
    } = opts;
    const id = `dlg-title-${++dialogSeq}`;
    let mode = allowSignup ? startWith : 'signin';

    const { done } = openDialog({
      labelledBy: id,
      render(el, close) {
        el.innerHTML = `
          <div class="dialog-head">
            <div>
              <h2 class="dialog-title" id="${id}">${escape(title)}</h2>
              ${description ? `<p class="dialog-body">${escape(description)}</p>` : ''}
            </div>
            <button type="button" class="btn btn-ghost btn-icon btn-sm" data-close aria-label="Close">${icon('x')}</button>
          </div>
          ${allowSignup ? `
            <div class="segmented block mb-4" role="tablist">
              <button type="button" role="tab" data-mode="signin">Sign in</button>
              <button type="button" role="tab" data-mode="signup">Create account</button>
            </div>` : ''}
          <form class="stack" novalidate>
            <div class="form-error" hidden role="alert"></div>
            <div class="field" data-signup-only>
              <label class="label" for="${id}-name">Your name</label>
              <input class="input" id="${id}-name" name="name" autocomplete="name" maxlength="60">
            </div>
            <div class="field">
              <label class="label" for="${id}-email">Email</label>
              <input class="input" id="${id}-email" name="email" type="email" autocomplete="email" required>
            </div>
            <div class="field">
              <label class="label" for="${id}-password">Password</label>
              <input class="input" id="${id}-password" name="password" type="password" required>
              <span class="hint" data-signup-only>At least 8 characters.</span>
            </div>
            <button type="submit" class="btn btn-primary btn-block btn-lg mt-2"></button>
          </form>`;

        const form = el.querySelector('form');
        const errorBox = el.querySelector('.form-error');
        const submit = form.querySelector('[type=submit]');
        const nameInput = form.elements.name;
        const emailInput = form.elements.email;
        const passwordInput = form.elements.password;

        function setMode(next) {
          mode = next;
          el.querySelectorAll('[data-mode]').forEach(b => b.setAttribute('aria-selected', String(b.dataset.mode === mode)));
          el.querySelectorAll('[data-signup-only]').forEach(n => { n.hidden = mode !== 'signup'; });
          passwordInput.autocomplete = mode === 'signup' ? 'new-password' : 'current-password';
          submit.textContent = mode === 'signup'
            ? (signupRole === 'Host' ? 'Create host account' : 'Create account')
            : 'Sign in';
          errorBox.hidden = true;
          [nameInput, emailInput, passwordInput].forEach(i => i.removeAttribute('aria-invalid'));
          const first = mode === 'signup' ? nameInput : emailInput;
          [nameInput, emailInput].forEach(i => i.removeAttribute('autofocus'));
          first.setAttribute('autofocus', '');
          first.focus();
        }

        function fail(message, input) {
          errorBox.innerHTML = `${icon('alert')}<span>${escape(message)}</span>`;
          errorBox.hidden = false;
          if (input) { input.setAttribute('aria-invalid', 'true'); input.focus(); }
        }

        el.querySelector('[data-close]').onclick = () => close(null);
        el.querySelectorAll('[data-mode]').forEach(b => { b.onclick = () => setMode(b.dataset.mode); });

        form.addEventListener('submit', async e => {
          e.preventDefault();
          errorBox.hidden = true;
          [nameInput, emailInput, passwordInput].forEach(i => i.removeAttribute('aria-invalid'));

          const email = emailInput.value.trim();
          const password = passwordInput.value;
          const name = nameInput.value.trim();

          if (mode === 'signup' && name.length < 2) return fail('Enter your name.', nameInput);
          if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) return fail('Enter a valid email address.', emailInput);
          if (!password) return fail('Enter your password.', passwordInput);
          if (mode === 'signup' && password.length < 8) return fail('Password must be at least 8 characters.', passwordInput);

          try {
            const auth = await busy(submit, () => mode === 'signup'
              ? api('/api/auth/register', { method: 'POST', auth: false, body: { fullName: name, email, password, role: signupRole } })
              : api('/api/auth/login', { method: 'POST', auth: false, body: { email, password } }));
            close(Session.set(auth));
          } catch (err) {
            fail(err.message);
          }
        });

        setMode(mode);
      },
    });
    return done.then(v => v || null);
  }

  /** Confirms and signs out. Pass `warning` to explain what will be interrupted. */
  async function signOut({ warning } = {}) {
    if (warning) {
      const ok = await confirm({ title: 'Sign out?', message: warning, confirmText: 'Sign out', danger: true });
      if (!ok) return false;
    }
    Session.clear();
    toast('Signed out');
    return true;
  }

  /** Renders the signed-in user + sign-in/out control into a container. */
  function renderAccount(container, { onSignIn, onSignOut } = {}) {
    const s = Session.get();
    document.querySelectorAll('[data-admin-only]').forEach(n => { n.hidden = !Session.isAdmin(s); });
    if (!container) return;
    if (s) {
      container.innerHTML = `
        <span class="account-name" title="${escape(s.email)}">${escape(s.displayName || s.email)} · ${escape(s.role)}</span>
        <button type="button" class="btn btn-ghost btn-sm" data-signout>${icon('logout')}<span>Sign out</span></button>`;
      container.querySelector('[data-signout]').onclick = () => onSignOut ? onSignOut() : signOut();
    } else {
      container.innerHTML = `<button type="button" class="btn btn-secondary btn-sm" data-signin>Sign in</button>`;
      container.querySelector('[data-signin]').onclick = () => onSignIn ? onSignIn() : signIn();
    }
  }

  /** Replaces <span data-icon="name"> placeholders in static markup with inline SVG icons. */
  function hydrateIcons(root = document) {
    root.querySelectorAll('[data-icon]').forEach(el => { el.outerHTML = icon(el.dataset.icon, el.className); });
  }
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => hydrateIcons());
  } else {
    hydrateIcons();
  }

  window.UI = {
    hydrateIcons,
    icon, shape, SHAPES, escape, initials, avatar, rankBadge, formatNumber, formatDateTime, stamp, relativeTime, toLocalInput,
    toast, openDialog, confirm, notice,
    api, ApiError, download, busy, copyText,
    signIn, signOut, renderAccount,
  };
  window.Session = Session;
})();
