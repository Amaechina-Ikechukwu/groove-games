// Live updates: the server pushes "something changed" signals and pages re-fetch what they show.
// Needs signalr.min.js, ui.js. Usage:
//   Live.subscribe('user', 'public');          // topics the server allows for this person
//   Live.on('sessions', () => reloadSessions());
//   Live.onReconnect(() => reloadEverything()); // after a dropped connection, in case signals were missed
(function () {
  'use strict';

  const handlers = {};            // kind -> Set of callbacks
  const reconnectHandlers = new Set();
  const wanted = new Set();       // topics to (re)subscribe to
  const timers = {};
  let conn = null;
  let starting = null;
  let usedToken = null;
  let intentionalStop = false;
  let warned = false;

  const currentToken = () => ((window.Session && Session.get()) || {}).token || '';

  // Several signals of one kind in a row (a burst of answers) run the handlers once.
  function dispatch(kind, group) {
    clearTimeout(timers[kind]);
    timers[kind] = setTimeout(() => {
      (handlers[kind] ? [...handlers[kind]] : []).forEach(fn => {
        try { fn(group); } catch (err) { console.error('Live handler failed', err); }
      });
    }, 150);
  }

  async function resubscribe() {
    for (const topic of wanted) {
      try { await conn.invoke('Subscribe', topic); } catch (_) { /* not allowed, or the connection dropped again */ }
    }
  }

  async function connect() {
    usedToken = currentToken();
    const c = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/game', { accessTokenFactory: () => currentToken() })
      .withAutomaticReconnect([0, 1000, 2000, 5000, 10000, 15000, 30000])
      .configureLogging(signalR.LogLevel.Error)
      .build();
    conn = c;

    c.on('Changed', (group, kind) => dispatch(kind, group));
    c.onreconnecting(() => {
      if (!warned && window.UI) { warned = true; UI.toast('Connection lost. Reconnecting…'); }
    });
    c.onreconnected(async () => {
      await resubscribe();
      if (warned && window.UI) UI.toast('Back online', 'success');
      warned = false;
      reconnectHandlers.forEach(fn => { try { fn(); } catch (err) { console.error(err); } });
    });
    c.onclose(() => {
      // Automatic retries ran out. Keep trying quietly unless we closed it on purpose.
      if (!intentionalStop && conn === c) setTimeout(() => ensure(true), 5000);
    });

    await c.start();
    await resubscribe();
  }

  function ensure(restart) {
    if (starting && !restart) return starting;
    starting = (async () => {
      if (typeof signalR === 'undefined') return;
      if (conn) {
        intentionalStop = true;
        try { await conn.stop(); } catch (_) { /* already closed */ }
        intentionalStop = false;
        conn = null;
      }
      try {
        await connect();
      } catch (_) {
        conn = null;
        starting = null;
        setTimeout(() => ensure(), 5000);
      }
    })();
    return starting;
  }

  // Signing in or out changes what this connection may subscribe to.
  if (window.Session) {
    Session.onChange(() => { if (conn && currentToken() !== usedToken) ensure(true); });
  }

  window.Live = {
    /** Runs fn when the server says `kind` changed. Returns a function that removes the handler. */
    on(kind, fn) {
      (handlers[kind] = handlers[kind] || new Set()).add(fn);
      return () => handlers[kind].delete(fn);
    },

    /** Runs fn after the connection came back from a drop, so the page can re-fetch anything it missed. */
    onReconnect(fn) {
      reconnectHandlers.add(fn);
      return () => reconnectHandlers.delete(fn);
    },

    async subscribe(...topics) {
      topics.forEach(t => wanted.add(t));
      await ensure();
      if (conn && conn.state === signalR.HubConnectionState.Connected) {
        await Promise.all(topics.map(t => conn.invoke('Subscribe', t).catch(() => false)));
      }
    },

    async unsubscribe(...topics) {
      topics.forEach(t => wanted.delete(t));
      if (conn && conn.state === signalR.HubConnectionState.Connected) {
        await Promise.all(topics.map(t => conn.invoke('Unsubscribe', t).catch(() => {})));
      }
    },
  };
})();
