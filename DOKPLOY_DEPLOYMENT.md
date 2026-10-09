# 🚀 Groove — Dokploy VPS Deployment Guide (All-In-One Container)

Groove runs as a **single, self-contained Docker container** that packages:
- **.NET 8 Web API** (Game Engine, Scorer, Auth, Parsers)
- **ASP.NET Core SignalR** (Sub-second real-time buzzer & sync)
- **PostgreSQL Database** (Embedded and running internally inside the container on `localhost:5432`)
- **Streamo Neon/Obsidian Cyber UI** (Zero external CDN or cloud dependencies)

There is **no Firebase, no separate database container to orchestrate, and no external cloud lock-in**.

---

## 🏗️ Architecture Inside the Single Container

```
 🌐 Internet (Players & Hosts)
       │ HTTPS / WSS
       ▼
 ┌───────────── Dokploy Traefik Reverse Proxy (Auto SSL) ─────────────┐
 │   Routes: https://groove.yourdomain.com ──> groove:5000           │
 │   SignalR: wss://groove.yourdomain.com/hubs/game (WebSockets)     │
 └───────────────────────────────┬───────────────────────────────────┘
                                 │ Port 5000
                                 ▼
 ┌───────────────────────────────────────────────────────────────────┐
 │                 GROOVE DOCKER CONTAINER                           │
 │                                                                   │
 │   ┌───────────────────────────────┐                               │
 │   │      Groove .NET 8 Web API    │                               │
 │   │   - SignalR QuizHub           │                               │
 │   │   - Streamo Cyber UI          │                               │
 │   └───────────────┬───────────────┘                               │
 │                   │ localhost:5432 (Internal loopback)            │
 │                   ▼                                               │
 │   ┌───────────────────────────────┐                               │
 │   │      PostgreSQL Server        │                               │
 │   │   - Database: 'groove'        │                               │
 │   │   - Auto init & migrations    │                               │
 │   └───────────────┬───────────────┘                               │
 └───────────────────┼───────────────────────────────────────────────┘
                     │ Persistent Volume Mount
                     ▼
        /var/lib/postgresql/data (On VPS Host)
```

---

## 🛠️ Step-by-Step Deployment on Dokploy

### Step 1: Push Code to Your Git Repository
```bash
git init
git add .
git commit -m "feat: Groove all-in-one container with PostgreSQL"
git branch -M main
git remote add origin https://github.com/YOUR_USERNAME/groove.git
git push -u origin main
```

---

### Step 2: Create Service in Dokploy (Single Container Application)

1. Open your **Dokploy Dashboard**.
2. Click **Projects** -> **Create Project** -> Name: `Groove`.
3. Inside your project, click **Create Service** -> Select **Application**.
4. Configure the Application:
   - **Name:** `groove`
   - **Source:** Select **GitHub** / **GitLab** / **Git** and choose your repository.
   - **Branch:** `main`
   - **Build Type:** **Dockerfile** (Dokploy will automatically detect `Dockerfile` in the root).

---

### Step 3: Configure Volumes (For Database Persistence)
To ensure your PostgreSQL database persists across container rebuilds:
1. In your Dokploy Application, go to the **Volumes** or **Mounts** tab.
2. Add a persistent volume mount:
   - **Host Path:** `/var/lib/dokploy/volumes/groove_db` (or a named volume: `groove_data`)
   - **Container Path:** `/var/lib/postgresql/data`

---

### Step 4: Configure Domains & SSL
1. Go to the **Domains** tab in your Dokploy Application.
2. Click **Add Domain**:
   - **Host:** `groove.yourdomain.com` *(Ensure your domain's DNS A Record points to your VPS IP)*
   - **Port:** `5000`
   - **HTTPS:** Check **Enable HTTPS** *(Dokploy automatically generates and auto-renews a free Let's Encrypt SSL certificate)*
3. Traefik automatically proxies both HTTP and WebSocket connections (`/hubs/game`) seamlessly.

---

### Step 5: Click Deploy!
1. Click **Deploy**.
2. Watch the deployment logs in Dokploy:
   - The container builds the .NET 8 application.
   - Container startup runs `entrypoint.sh`:
     - Initializes PostgreSQL cluster in `/var/lib/postgresql/data`.
     - Creates user `postgres` and database `groove`.
     - Starts PostgreSQL daemon in the background.
     - Launches the .NET 8 Web API.
     - EF Core verifies tables (`quizzes`, `game_sessions`, `players`, `round_audits`).
   - Dokploy's healthcheck monitors `http://localhost:5000/health`.

Your app is now live with zero additional setup!

---

## 💻 Alternative: Running via Docker Compose

If you prefer using `docker compose` directly on your VPS terminal:

```bash
docker compose up -d --build
```

To view logs:
```bash
docker compose logs -f groove
```

---

## 🎮 Accessing Groove
- **Home / Player Portal:** `https://groove.yourdomain.com`
- **Host Stadium Display:** `https://groove.yourdomain.com/host.html`
  - Hosts create their own account from this page. Existing player accounts can switch to hosting there too.
- **Admin Control Deck:** `https://groove.yourdomain.com/admin.html`
  - Sign in with the admin account from `Admin__Email` / `Admin__Password` (see below), then promote other people from the **People** tab.

## 🔐 Required environment variables

Set these in Dokploy's **Environment** tab before the first deploy:

| Variable | Purpose |
| --- | --- |
| `Jwt__Key` | Secret used to sign sign-in tokens. Use a long random value (32+ characters), e.g. `openssl rand -base64 48`. If it's missing, the app generates a random key on each start and everyone is signed out on restart. |
| `Admin__Email` | Email of the first admin account. Created on startup if it doesn't exist. |
| `Admin__Password` | Password for that account. You can remove both variables after the first start. |

There are no built-in default accounts in production. The demo accounts (`admin@groove.live` etc.) exist only when `ASPNETCORE_ENVIRONMENT=Development`.

> **Upgrading from an older version:** accounts used to be stored in memory with plaintext passwords and were lost on every restart. They now live in the `users` table with hashed passwords. Nobody has an account after upgrading, so set the admin variables above, then hosts sign up again.
- **Health Check:** `https://groove.yourdomain.com/health`
