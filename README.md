# ⚡ Groove — Real-Time Multiplayer Esports Quiz Platform

> **Kahoot-style rapid-fire multiplayer quiz platform powered by .NET 8, SignalR, and PostgreSQL 16 with Entity Framework Core, dressed in a high-voltage neon chartreuse & obsidian cyber aesthetic.**

---

## 🌟 Key Features

* **Sub-Second Real-Time Synchronization:** SignalR WebSockets powering countdown clocks, buzzer answers, and live leaderboard shifts.
* **Streamo-Inspired Cyber UI:** Pitch-black matte carbon backdrop, electric neon lime (`#D4FF00`) accents, asymmetric cards, PlayStation-inspired geometric glyphs (`▲`, `◆`, `●`, `■`), and a signature curved bottom navigation tray.
* **Multi-Session Tournament Deck:** Launch single matches or multi-session tournaments. Each session is assigned its own unique 6-character access code (PIN), binding all players to that Host.
* **Persistent Tournament Leaderboard:** Deduplicates participant identity across multi-day events and sessions. Tracks cumulative points, total games, accuracy %, and winning streaks.
* **All-In-One Self-Contained Container:** .NET 8 application and PostgreSQL run together inside a single Docker container. Automatic table provisioning via `EnsureCreated()`. No Firebase or external cloud lock-in.
* **Dokploy & Docker Ready:** Turnkey deployment on any VPS running Dokploy or standard Docker Compose. Automatic Let's Encrypt SSL and zero-config WebSockets via Dokploy's Traefik proxy.
* **Smart Bulk Text Quiz Parser:** Paste raw Markdown, semi-structured plain text, or JSON. Tokenizes questions, choices (2 to 6 options), answer keys (`*`, `[x]`, `Answer: A`), time limits (`Time: 15s`), and points (`Points: 1000`).
* **Zero-Latency Web Audio Synthesizer:** Built-in procedural sound effects (countdown tick, hurry-up beep, correct fanfare, buzzer, podium fanfare) with zero external media dependencies.
* **1-Click Excel & CSV Exports:** Instant downloads of live match breakdowns and global tournament standings.

---

## 🏗️ Architecture & Tech Stack

```
   ┌─────────────────────────────────────────────────────────┐
   │             Clients (HTML5 / Modern JS / CSS)           │
   │   - Host Display Screen: Stadium Projector View         │
   │   - Player Screen: Mobile-First Cyber Buzzer            │
   │   - Admin Deck: Bulk Text Parser & Persistent Rankings  │
   └───────────────▲─────────────────────────▲───────────────┘
                   │ HTTP / REST             │ SignalR WebSockets
                   ▼                         ▼
   ┌─────────────────────────────────────────────────────────┐
   │            ASP.NET Core Web API & SignalR               │
   │  - GameEngineService (Timers, Scoring calculation)      │
   │  - QuizParserEngine (Regex Tokenizer & Validator)       │
   │  - Hub: QuizHub (/hubs/game)                            │
   │  - PostgresDataService (EF Core + In-Memory Fallback)   │
   │  - ExportService (UTF-8 BOM CSV & Excel XML)            │
   └───────────────────────────────▲─────────────────────────┘
                                   │ Npgsql / EF Core
                                   ▼
   ┌─────────────────────────────────────────────────────────┐
   │                     PostgreSQL 16                       │
   │   - quizzes (Quiz Banks & Questions JSON)               │
   │   - game_sessions (Match States & PINs)                 │
   │   - players (Persistent Profiles & Host Bindings)       │
   │   - round_audits (Round Answer Telemetry)               │
   └─────────────────────────────────────────────────────────┘
```

* **Backend:** .NET 8 (C#) Web API + ASP.NET Core SignalR
* **Persistence:** PostgreSQL 16 + Entity Framework Core (`Npgsql.EntityFrameworkCore.PostgreSQL`)
* **Frontend:** Vanilla JavaScript (ES6+), official `@microsoft/signalr` client, procedural Web Audio API synthesizer
* **Deployment:** Dokploy / Docker Compose with Traefik Reverse Proxy

---

## 🚀 Dokploy VPS Deployment

Deploying to Dokploy takes less than 3 minutes. See [DOKPLOY_DEPLOYMENT.md](DOKPLOY_DEPLOYMENT.md) for full instructions:

1. In Dokploy, create a **Project** (`Groove`).
2. Add an **Application Service** pointing to your Git repository with `Dockerfile`.
3. In the **Domains** tab, point your domain (e.g. `groove.yourdomain.com`) to port `5000` with HTTPS enabled.
4. Add a persistent volume mount for `/var/lib/postgresql/data`.
5. Click **Deploy**. Dokploy builds the container, initializes the database, and provisions Let's Encrypt SSL automatically.

---

## 💻 Running Locally in Docker

```bash
GROOVE_JWT_KEY="$(openssl rand -base64 48)" GROOVE_ADMIN_EMAIL=you@example.com GROOVE_ADMIN_PASSWORD=choose-a-strong-password docker compose up --build
```

Access the applications immediately:
* **Home Portal:** [http://localhost:5000](http://localhost:5000)
* **Participant Mobile Buzzer:** [http://localhost:5000/player.html](http://localhost:5000/player.html)
* **Host Stadium Display:** [http://localhost:5000/host.html](http://localhost:5000/host.html)
* **Admin Control Deck:** [http://localhost:5000/admin.html](http://localhost:5000/admin.html)
* **Swagger API Explorer:** [http://localhost:5000/swagger](http://localhost:5000/swagger)

---

## 🧪 Running Unit Tests

```bash
dotnet test
```

The tests cover the parser, scoring, reconnects, persistence, and auth/RBAC rules (no self-assigned roles, hashed passwords, session ownership).

---

## 🎮 Tournaments

**Roles**
- **Admins** see and manage everything: every host's tournaments, every running game, all quizzes, scores and people.
- **Hosts** create tournaments and manage their own: sessions, players and results.
- **Players** join tournaments with a code, play their sessions, and see their history on **Your tournaments** (`/tournaments.html`).

**Flow**
1. A host creates a tournament on `/host.html` and shares its 6-character join code (or the invite link).
2. Players sign in and join with the code.
3. The host adds sessions, each one of two kinds:
   - **Live game**: the host clicks *Run live* and players join with a PIN. Signed-in players who join with the PIN are added to the tournament automatically.
   - **Self-paced**: the host opens it until a deadline (like Kahoot's assign mode). Members play on their own time with one attempt each. The server times every question, so refreshing doesn't reset the clock.
4. Scores from every session add up in the tournament standings. **Standings are public** at `/tournament.html?id=…` and on `/leaderboard.html`. Player emails are only shown to the tournament's host and admins.

**Quick games** are one-off live games with no tournament. Anyone with the PIN can play without an account, and scores go on the separate Quick games leaderboard. Tournament scores are kept per tournament and never added into it.
