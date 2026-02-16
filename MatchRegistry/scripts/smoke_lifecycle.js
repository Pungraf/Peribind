#!/usr/bin/env node

/**
 * Match Registry smoke test for full lobby lifecycle:
 * create -> list -> join -> ready -> match/create -> lobby/server-info -> leave -> close
 *
 * Usage:
 *   node scripts/smoke_lifecycle.js
 *   REGISTRY_URL=http://127.0.0.1:8080 PERIBIND_INTERNAL_API_TOKEN=... node scripts/smoke_lifecycle.js
 */

const REGISTRY_URL = (process.env.REGISTRY_URL || "http://127.0.0.1:8080").replace(/\/+$/, "");
const INTERNAL_TOKEN = process.env.PERIBIND_INTERNAL_API_TOKEN || "";
const TIMEOUT_MS = Number(process.env.SMOKE_TIMEOUT_MS || 15000);

function log(step, data = null) {
  if (data == null) {
    console.log(`[smoke] ${step}`);
    return;
  }

  console.log(`[smoke] ${step}: ${JSON.stringify(data)}`);
}

async function request(method, path, body, { allowNotFound = false } = {}) {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), TIMEOUT_MS);
  try {
    const headers = {};
    if (body != null) {
      headers["Content-Type"] = "application/json";
    }
    if (INTERNAL_TOKEN) {
      headers["x-internal-token"] = INTERNAL_TOKEN;
    }

    const res = await fetch(`${REGISTRY_URL}${path}`, {
      method,
      headers,
      body: body != null ? JSON.stringify(body) : undefined,
      signal: controller.signal
    });

    const text = await res.text();
    let json = null;
    if (text && text.trim().length > 0) {
      try {
        json = JSON.parse(text);
      } catch {
        json = { raw: text };
      }
    }

    if (allowNotFound && res.status === 404) {
      return { status: res.status, json };
    }

    if (!res.ok) {
      const message = json && json.error ? json.error : text || res.statusText;
      throw new Error(`${method} ${path} failed (${res.status}): ${message}`);
    }

    return { status: res.status, json };
  } finally {
    clearTimeout(timeout);
  }
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

function buildPlayerId(prefix) {
  const suffix = Math.random().toString(16).slice(2, 10);
  return `${prefix}_${Date.now().toString(36)}_${suffix}`.slice(0, 48);
}

async function run() {
  const hostPlayer = buildPlayerId("SMOKE_HOST");
  const guestPlayer = buildPlayerId("SMOKE_GUEST");
  log("start", { registry: REGISTRY_URL, hostPlayer, guestPlayer });

  const created = await request("POST", "/lobby/create", {
    playerId: hostPlayer,
    name: "SmokeLobby",
    maxPlayers: 2,
    map: "",
    mode: "",
    region: ""
  });
  const lobby = created.json;
  assert(lobby && lobby.id, "lobby/create returned no lobby id");
  log("lobby/create", { lobbyId: lobby.id, code: lobby.lobbyCode });

  const listed = await request("GET", "/lobby/list");
  const listedLobby = (listed.json?.results || []).find((x) => x.id === lobby.id);
  assert(!!listedLobby, "lobby/list did not include created lobby");
  log("lobby/list ok");

  const joined = await request("POST", "/lobby/join", {
    lobbyId: lobby.id,
    playerId: guestPlayer
  });
  assert((joined.json?.players || []).length === 2, "guest join did not produce 2 players");
  log("lobby/join ok");

  await request("POST", "/lobby/player-ready", {
    lobbyId: lobby.id,
    playerId: hostPlayer,
    isReady: true
  });
  await request("POST", "/lobby/player-ready", {
    lobbyId: lobby.id,
    playerId: guestPlayer,
    isReady: true
  });
  log("lobby/player-ready ok");

  const matchCreated = await request("POST", "/match/create", {
    lobbyId: lobby.id,
    players: [hostPlayer, guestPlayer],
    map: "",
    mode: "",
    region: ""
  });
  const match = matchCreated.json;
  assert(match && match.matchId, "match/create returned no matchId");
  log("match/create", { matchId: match.matchId, port: match.serverPort });

  await request("POST", "/lobby/server-info", {
    lobbyId: lobby.id,
    playerId: hostPlayer,
    serverIp: match.serverIp,
    serverPort: match.serverPort,
    matchId: match.matchId
  });
  log("lobby/server-info ok");

  const fetchedLobby = await request("GET", `/lobby/${encodeURIComponent(lobby.id)}`);
  assert(fetchedLobby.json?.matchId === match.matchId, "lobby does not contain matchId after server-info");
  log("lobby/get ok");

  await request("POST", "/lobby/leave", { lobbyId: lobby.id, playerId: guestPlayer });
  await request("POST", "/lobby/leave", { lobbyId: lobby.id, playerId: hostPlayer });
  log("lobby/leave ok");

  const closedLobby = await request("GET", `/lobby/${encodeURIComponent(lobby.id)}`, null, { allowNotFound: true });
  assert(closedLobby.status === 404, "lobby still exists after all players left");
  log("lobby closed");

  const resultWrite = await request("POST", "/match/result", {
    matchId: match.matchId,
    winnerPlayerId: hostPlayer,
    wasSurrendered: false,
    surrenderingPlayerId: "",
    players: [
      { playerId: hostPlayer, playerSlot: 0, score: 10 },
      { playerId: guestPlayer, playerSlot: 1, score: 14 }
    ]
  });
  assert(resultWrite.json?.ok === true, "match/result did not return ok");
  log("match/result ok", { duplicate: !!resultWrite.json?.duplicate });

  const profile = await request("GET", `/player/${encodeURIComponent(hostPlayer)}/profile`);
  assert(profile.json?.stats?.gamesPlayed >= 1, "player profile stats were not updated");
  log("player/profile ok");

  const history = await request("GET", `/player/${encodeURIComponent(hostPlayer)}/matches?limit=5`);
  assert(Array.isArray(history.json?.results) && history.json.results.length >= 1, "player history missing");
  log("player/matches ok");

  const leaderboard = await request("GET", "/leaderboard?limit=10");
  assert(Array.isArray(leaderboard.json?.leaderboard), "leaderboard response invalid");
  log("leaderboard ok");

  await request("POST", "/match/end", { matchId: match.matchId });
  log("match/end ok");

  const afterEnd = await request("GET", `/match/${encodeURIComponent(match.matchId)}`, null, { allowNotFound: true });
  assert(afterEnd.status === 404, "match still exists after match/end");
  log("match closed");

  log("success");
}

run().catch((err) => {
  console.error(`[smoke] failed: ${err.message || err}`);
  process.exit(1);
});
