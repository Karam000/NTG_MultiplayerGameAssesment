# NTG Multiplayer Technical Assessment

Server-authoritative multiplayer prototype built with **Unity 6000.6.0f1**, **Netcode for GameObjects (NGO) 2.x**,
**Unity Transport (UTP/UDP)**, **Unity Multiplayer Services (Sessions)** for the lobby, a **dedicated headless server**,
and **Edgegap** for dedicated-server hosting. Android/iOS mobile client target (landscape).

> **No credentials in this repo.** The Edgegap API token lives only in Unity **Secret Manager** (cloud-side) and the
> Edgegap plugin"s local settings. Never commit it to the project, README, or video.

---

## 1. What the game is

Two teams (max 8 players) in one arena:

- **Objectives (cubes)** — carry them to the green zone and channel 1 s to complete them
  ("Available → Carried → InteractionInProgress → Completed"). **Condition A**: first team to complete 3 objectives wins.
- **One shared ball** — one possessor at a time; carrying it slows you (server-enforced 0.6× speed).
  Throw it to eliminate enemy players. **Condition B**: eliminate the entire enemy team to win.
- Eliminated players are grayed out and spectate until the match ends.

Match flow: "WaitingForPlayers → Starting (3 s) → Playing → Finished", fully server-controlled.

**Controls:** WASD/arrows = move · **E** = pickup / drop · **Space** = throw · Mobile: virtual joystick + INTERACT / THROW buttons.

---

## 2. Project layout

| Path                     | Contents                                                                                                                               |
| ------------------------ | -------------------------------------------------------------------------------------------------------------------------------------- |
| "Assets/Scenes"          | "ClientMenu" (sign-in + find match), "Lobby" (session room, offline), "Game" (networked match), "ServerEntry" (server-only boot scene) |
| "Assets/Scripts/Runtime" | All gameplay/network code (see §8 for the networking map)                                                                              |
| "Assets/Prefabs"         | "NetworkManager", "UICanvas", "PlayerCapsule", "MatchManager", "Objective", "Ball"                                                     |
| "Assets/CloudCode~/"     | Source of the Cloud Code module "NTGServerAllocator" (the "~" folder is excluded from game compilation)                                |
| "Assets/Spec"            | Original assessment brief                                                                                                              |

Lobby/matchmaking is based on Unity"s MPS (quick join; no room codes).
The UGS session "host" is only the **lobby leader** (can start the match) — **all gameplay runs on the dedicated server; no client ever acts as network host.**

---

## 3. Requirements

- Unity **6000.6.0f1** with modules: **Linux Dedicated Server Build Support**, **Android Build Support**
- Packages (already in "Packages/manifest.json"): "com.unity.netcode.gameobjects", "com.unity.transport",
  "com.unity.services.multiplayer", "com.unity.services.cloudcode", "com.unity.multiplayer.playmode", "com.unity.inputsystem"
- Edgegap Unity plugin: "https://github.com/edgegap/edgegap-unity-plugin.git"
- Docker Desktop (for the server container image)
- A linked **Unity Cloud (UGS) project**: _Edit → Project Settings → Services_ (required for Sessions/Cloud Code)

---

## 4. Dedicated server build — configuration & startup

**Scenes in the server build:** "ServerEntry" (scene 0), "Game".
Client builds use: "ClientMenu" (scene 0), "Lobby", "Game".

- The server auto-starts on launch ("ServerStartup"): binds **"0.0.0.0"**, listens on **UDP 7777**, then loads "Game".
- Port override priority: "-port <n>" CLI arg → "PORT" env var → default "7777".
- Max 8 clients (connection approval rejects with "Server full").
- Build it via **Build Profiles → Dedicated Server** (Linux for Edgegap, Windows for local testing).

Run a local (Windows) server:

"""powershell
.\{ServerBuildName}.exe -logFile server.log
"""

Expected "server.log" lines (startup verification):

"""
[SERVER] Listening on 0.0.0.0:7777 (udp)
[SERVER] MatchManager spawned
[SERVER] Spawned player object for client 1, team 0
"""

---

## 5. Edgegap deployment

**One-time setup** (via the Edgegap Unity plugin — _Tools → Edgegap Hosting_):

1. Sign in with your Edgegap token (stored by the plugin locally — not in the repo).
2. **Build Server** (Linux dedicated-server profile).
3. **Containerize with Docker** (Docker Desktop must be running).
4. **Upload image & create App Version** → on the dashboard, set port **7777 / UDP**.
   Note the **application name** and **version name** — they must match the constants in
   "Assets/CloudCode~/ServerAllocatorModule.cs" ("EdgegapAppName", "EdgegapAppVersion").

**Per-match deployments are created on demand by Cloud Code** (not manually):

- Module "NTGServerAllocator" (deploy it via _Services → Deployment_ window):
  - "Request" — "POST /v2/deployments" → returns "request_id"
  - "Status" — "GET /v1/status/{request_id}" → returns "running", "fqdn", "public_ip", **external port**
  - "Stop" — "DELETE /v1/stop/{request_id}" — called automatically when the match finishes
- Secret: create **"EDGEGAP_API_TOKEN"** in **Unity Dashboard → Secret Manager** and grant Cloud Code access.
- The lobby **leader** picks the server mode in the lobby dropdown: **Edgegap (cloud)** or **Local server (LAN)**.

**Port model (important):** the server listens on internal **UDP 7777**; Edgegap assigns a **random external port** per
deployment. Clients always connect to "fqdn (or public_ip) : external_port", never to 7777 and never to a hardcoded address.
The connection info is published to the session by the leader at match start; all clients read it at runtime.

**Verify startup / diagnose:** Edgegap dashboard → Deployments → your deployment → status "Ready" + logs
(look for "[SERVER] Listening on 0.0.0.0:7777"). If the deployment is Ready but unreachable: check the app-version port
mapping is "7777/UDP", and that clients use the **external** port from "Status".

---

## 6. Connecting clients (incl. mobile)

Clients never hardcode an endpoint:

1. Open the app → anonymous UGS sign-in → **FIND MATCH** (quick join into the shared session).
2. Lobby: non-leaders press **READY**; leader presses **START MATCH** (and picks cloud/local in the dropdown).
3. On start, the leader"s client allocates the server (Cloud Code) and publishes "ip:port" to the session;
   **every client auto-connects** and NGO scene-sync pulls everyone into "Game".

**Android install:** build with the **Android** build profile (scenes "ClientMenu, Lobby, Game", landscape), copy the APK
to the device, install, run. Requires internet (UGS + Edgegap). For **local-mode** testing against a PC-hosted server,
keep the phone on the same Wi-Fi and allow UDP 7777 through the PC firewall.

- **Test device used:** Xiaomi Redmi Note 9s

---

## 7. Reproducing the 3-client test

1. Start a deployment (leader picks _Edgegap_ in the lobby dropdown) **or** run a local server (leader picks _Local_).
2. Client A (any device) → FIND MATCH → becomes leader.
3. Clients B and C → FIND MATCH → land in the same lobby → press **READY**.
4. Leader presses **START MATCH** → all clients connect and spawn (blue/red teams, arrow above your own player).
5. Verify: movement sync · cube pickup (try both clients grabbing the same cube simultaneously) · carry + 1 s channel ·
   ball pickup/throw/hit · elimination · disconnect a carrier (object drops) · match end banner → **BACK TO LOBBY**.

---

## 8. Networking architecture (short)

- **Transport:** Unity Transport over **UDP**. Server binds "0.0.0.0:7777"; Edgegap maps a random external port.
- **Authority:** the dedicated server owns all gameplay outcomes. Clients only _request_ (RPCs); the server validates
  (state, possession, distance, cooldown, elimination, match state) and replicates results via "NetworkVariable"s.
- **Movement:** client sends **input state** (never position) 20 Hz unreliable → server integrates at fixed step →
  broadcasts position+seq 20 Hz unreliable. Owner does **prediction + soft reconciliation** (hard snap on large desync);
  remote clients render a ~100 ms interpolation buffer. Speed cheats are impossible: position only ever changes server-side.
- **Objective state:** server-side state machine on each object; persistent state replicated with "NetworkVariable"s.
  The pickup race is resolved by server-side state check at request time — first valid request wins, losers are rejected.
- **Timed interaction:** server coroutine (1 s), cancelled server-side if the carrier moves away / is eliminated / disconnects.
  The client progress bar is cosmetic (driven by the replicated end-time).
- **Events:** one-shot effects (announcements) via "ClientRpc"; persistent state always via "NetworkVariable".
- **Ownership vs possession:** NGO "NetworkObject" ownership (who may write) stays with the **server** for world objects;
  _gameplay possession_ (who carries an item) is a separate server-validated "NetworkVariable<ulong>" — never client-claimed.
- **Ball/hits:** client sends only a throw _direction_; server validates possession/state/cooldown/direction, simulates
  the arc, and does the hit detection itself (proximity check vs authoritative positions). A client can never declare a hit.
- **Mobile efficiency:** unreliable delivery for high-frequency messages, no per-frame RPCs, eliminated/frozen clients
  stop input traffic entirely, kinematic movement (no physics cost), legacy uGUI + Joystick Pack touch controls.
- **Lobby/session:** UGS Sessions (quick join, roster, ready flags, leader via session host). The session carries the
  server endpoint as a property; the leader clears it when the match ends.

---

## 9. Known limitations & troubleshooting

- A finished match does not reset on the _same_ server process — every match gets a **fresh** deployment (cloud mode)
  or needs a server restart (local mode).
- If the leader leaves mid-match, the deployment may idle until Edgegap"s max-duration auto-stop.
- **Client can"t connect:** confirm deployment is "Ready"; use the **external** port (not 7777); for local mode, allow
  UDP 7777 in the Windows firewall and ensure the phone shares the PC"s Wi-Fi.
- **"Starting server…" fails:** check Cloud Code logs (dashboard) — usually a missing/wrong "EDGEGAP_API_TOKEN" secret,
  or "EdgegapAppName/EdgegapAppVersion" not matching the uploaded app.
- **UGS sign-in fails:** project not linked (_Project Settings → Services_), or no internet.
- Deployments cost free-tier capacity while running — stop unused deployments (dashboard, or finish the match to auto-stop).
