const express = require("express");
const crypto = require("crypto");
const fs = require("fs");
const path = require("path");
const dgram = require("dgram");
const { spawn } = require("child_process");
const { Pool } = require("pg");

const app = express();
app.use(express.json({ limit: "1mb" }));

// ---- Config ----
const PORT = Number(process.env.REGISTRY_PORT || 8080);
const SERVER_BIN = process.env.PERIBIND_SERVER_BIN || "/opt/peribind-server/PeribindServer";
const SERVER_CWD = process.env.PERIBIND_SERVER_CWD || "/opt/peribind-server";
const SERVER_PUBLIC_IP = process.env.PERIBIND_SERVER_PUBLIC_IP || "209.38.222.103";
const LOG_DIR = process.env.PERIBIND_SERVER_LOG_DIR || "/opt/peribind-server/logs";
const PORT_MIN = Number(process.env.PERIBIND_SERVER_PORT_MIN || 7777);
const PORT_MAX = Number(process.env.PERIBIND_SERVER_PORT_MAX || 7877);
const REGISTRY_SELF_URL =
  process.env.PERIBIND_MATCH_REGISTRY_URL || `http://127.0.0.1:${PORT}`;
const TTL_MS = Number(process.env.MATCH_TTL_MS || 6 * 60 * 60 * 1000); // 6h safety TTL
const END_GRACE_MS = Number(process.env.MATCH_END_GRACE_MS || 30 * 1000);
const LOBBY_STALE_MS = Number(process.env.LOBBY_STALE_MS || 30 * 60 * 1000);
const RELEASE_ADMIN_TOKEN = process.env.PERIBIND_RELEASE_ADMIN_TOKEN || "";
const RELEASE_DEFAULT_CHANNEL = process.env.PERIBIND_RELEASE_DEFAULT_CHANNEL || "stable";
const RELEASE_DEFAULT_PLATFORM = process.env.PERIBIND_RELEASE_DEFAULT_PLATFORM || "win64";
const INTERNAL_API_TOKEN = process.env.PERIBIND_INTERNAL_API_TOKEN || "";
const TRUST_PROXY = String(process.env.PERIBIND_TRUST_PROXY || "1").trim() !== "0";
const RATE_LIMIT_WINDOW_MS = Number(process.env.PERIBIND_RATE_LIMIT_WINDOW_MS || 10_000);
const RATE_LIMIT_MAX_GLOBAL = Number(process.env.PERIBIND_RATE_LIMIT_MAX_GLOBAL || 150);
const RATE_LIMIT_MAX_LOBBY = Number(process.env.PERIBIND_RATE_LIMIT_MAX_LOBBY || 80);
const RATE_LIMIT_MAX_MATCH = Number(process.env.PERIBIND_RATE_LIMIT_MAX_MATCH || 60);
const RATE_LIMIT_MAX_RELEASE = Number(process.env.PERIBIND_RATE_LIMIT_MAX_RELEASE || 20);

const pool = new Pool(buildPgConfig());

fs.mkdirSync(LOG_DIR, { recursive: true });

if (TRUST_PROXY) {
  app.set("trust proxy", 1);
}

// Runtime-only child handles for processes spawned by this registry process.
const processes = new Map(); // matchId -> ChildProcess
const reservedPorts = new Set(); // temporary reservation during spawn/allocation

// Serialize create flow to avoid concurrent port double-assignment.
let createQueue = Promise.resolve();

function withCreateLock(fn) {
  const run = createQueue.then(fn, fn);
  createQueue = run.then(() => undefined, () => undefined);
  return run;
}

function buildPgConfig() {
  if (process.env.DATABASE_URL) {
    const cfg = { connectionString: process.env.DATABASE_URL };
    if (process.env.PGSSL === "true") {
      cfg.ssl = { rejectUnauthorized: false };
    }
    return cfg;
  }

  return {
    host: process.env.PGHOST || "127.0.0.1",
    port: Number(process.env.PGPORT || 5432),
    database: process.env.PGDATABASE || "peribind",
    user: process.env.PGUSER || "peribind",
    password: process.env.PGPASSWORD || ""
  };
}

function now() {
  return Date.now();
}

function logEvent(level, event, fields = {}) {
  const line = JSON.stringify({
    ts: new Date().toISOString(),
    level,
    event,
    ...fields
  });

  if (level === "error") {
    console.error(line);
    return;
  }

  if (level === "warn") {
    console.warn(line);
    return;
  }

  console.log(line);
}

function buildMatchPayload(row) {
  const players = Array.isArray(row.players) ? row.players.map((p) => String(p)) : [];
  const connectedPlayers = Array.isArray(row.connected_players)
    ? row.connected_players.map((p) => String(p))
    : [];

  return {
    matchId: row.match_id,
    lobbyId: row.lobby_id || "",
    serverIp: row.server_ip,
    serverPort: Number(row.server_port),
    players,
    connectedPlayers,
    expiresAt: new Date(row.expires_at).getTime()
  };
}

function isPidAlive(pid) {
  if (!pid || pid <= 0) return false;
  try {
    process.kill(pid, 0);
    return true;
  } catch (_) {
    return false;
  }
}

function isUdpPortFree(port, host = "0.0.0.0") {
  return new Promise((resolve) => {
    const sock = dgram.createSocket("udp4");
    let done = false;

    const finish = (ok) => {
      if (done) return;
      done = true;
      try { sock.close(); } catch (_) {}
      resolve(ok);
    };

    sock.once("error", () => finish(false));
    sock.bind(port, host, () => finish(true));
  });
}

async function initDb() {
  await pool.query(`
    CREATE TABLE IF NOT EXISTS matches (
      match_id TEXT PRIMARY KEY,
      lobby_id TEXT UNIQUE,
      server_ip TEXT NOT NULL,
      server_port INTEGER NOT NULL,
      players JSONB NOT NULL DEFAULT '[]'::jsonb,
      expires_at TIMESTAMPTZ NOT NULL,
      pid INTEGER NOT NULL DEFAULT 0,
      created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
      ended_at TIMESTAMPTZ NULL,
      terminating BOOLEAN NOT NULL DEFAULT FALSE
    );
  `);

  await pool.query(`
    CREATE INDEX IF NOT EXISTS idx_matches_lobby_id ON matches(lobby_id);
  `);
  await pool.query(`
    CREATE INDEX IF NOT EXISTS idx_matches_expires_at ON matches(expires_at);
  `);
  await pool.query(`
    CREATE INDEX IF NOT EXISTS idx_matches_ended_at ON matches(ended_at);
  `);

  await pool.query(`
    CREATE TABLE IF NOT EXISTS player_last_match (
      player_id TEXT PRIMARY KEY,
      match_id TEXT NOT NULL REFERENCES matches(match_id) ON DELETE CASCADE,
      updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
    );
  `);
  await pool.query(`
    CREATE INDEX IF NOT EXISTS idx_player_last_match_match_id ON player_last_match(match_id);
  `);

  await pool.query(`
    CREATE TABLE IF NOT EXISTS match_presence (
      match_id TEXT NOT NULL REFERENCES matches(match_id) ON DELETE CASCADE,
      player_id TEXT NOT NULL,
      connected BOOLEAN NOT NULL DEFAULT FALSE,
      updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
      PRIMARY KEY(match_id, player_id)
    );
  `);
  await pool.query(`
    CREATE INDEX IF NOT EXISTS idx_match_presence_match_connected
    ON match_presence(match_id, connected);
  `);

  await pool.query(`
    CREATE TABLE IF NOT EXISTS players (
      ugs_player_id TEXT PRIMARY KEY,
      username TEXT NOT NULL,
      display_name TEXT NOT NULL DEFAULT '',
      created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
      last_seen_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
    );
  `);
  await pool.query(`
    CREATE INDEX IF NOT EXISTS idx_players_last_seen_at ON players(last_seen_at);
  `);
  await pool.query(`
    CREATE INDEX IF NOT EXISTS idx_players_username ON players(username);
  `);

  await pool.query(`
    CREATE TABLE IF NOT EXISTS match_results (
      match_id TEXT PRIMARY KEY,
      winner_player_id TEXT NOT NULL DEFAULT '',
      was_surrendered BOOLEAN NOT NULL DEFAULT FALSE,
      surrendering_player_id TEXT NOT NULL DEFAULT '',
      completed_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
      meta JSONB NOT NULL DEFAULT '{}'::jsonb
    );
  `);
  await pool.query(`
    CREATE INDEX IF NOT EXISTS idx_match_results_completed_at
    ON match_results(completed_at DESC);
  `);

  await pool.query(`
    CREATE TABLE IF NOT EXISTS match_result_players (
      match_id TEXT NOT NULL REFERENCES match_results(match_id) ON DELETE CASCADE,
      player_id TEXT NOT NULL,
      player_slot INTEGER NOT NULL DEFAULT -1,
      score INTEGER NOT NULL DEFAULT 0,
      result TEXT NOT NULL,
      created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
      PRIMARY KEY(match_id, player_id)
    );
  `);
  await pool.query(`
    CREATE INDEX IF NOT EXISTS idx_match_result_players_player_created
    ON match_result_players(player_id, created_at DESC);
  `);

  await pool.query(`
    CREATE TABLE IF NOT EXISTS player_stats (
      player_id TEXT PRIMARY KEY,
      games_played INTEGER NOT NULL DEFAULT 0,
      wins INTEGER NOT NULL DEFAULT 0,
      losses INTEGER NOT NULL DEFAULT 0,
      draws INTEGER NOT NULL DEFAULT 0,
      surrenders INTEGER NOT NULL DEFAULT 0,
      rank_points INTEGER NOT NULL DEFAULT 0,
      score_total INTEGER NOT NULL DEFAULT 0,
      last_result TEXT NOT NULL DEFAULT '',
      last_match_id TEXT NOT NULL DEFAULT '',
      last_match_at TIMESTAMPTZ NULL,
      updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
    );
  `);
  await pool.query(`
    CREATE INDEX IF NOT EXISTS idx_player_stats_rank_points
    ON player_stats(rank_points DESC, wins DESC, games_played DESC);
  `);

  await pool.query(`
    CREATE TABLE IF NOT EXISTS lobbies (
      lobby_id TEXT PRIMARY KEY,
      lobby_code TEXT NOT NULL UNIQUE,
      name TEXT NOT NULL,
      max_players INTEGER NOT NULL,
      host_player_id TEXT NOT NULL,
      data JSONB NOT NULL DEFAULT '{}'::jsonb,
      created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
      updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
      closed_at TIMESTAMPTZ NULL
    );
  `);
  await pool.query(`
    CREATE INDEX IF NOT EXISTS idx_lobbies_closed_updated
    ON lobbies(closed_at, updated_at DESC);
  `);
  await pool.query(`
    CREATE INDEX IF NOT EXISTS idx_lobbies_code
    ON lobbies(lobby_code);
  `);

  await pool.query(`
    CREATE TABLE IF NOT EXISTS lobby_players (
      lobby_id TEXT NOT NULL REFERENCES lobbies(lobby_id) ON DELETE CASCADE,
      player_id TEXT NOT NULL,
      ready BOOLEAN NOT NULL DEFAULT FALSE,
      joined_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
      updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
      PRIMARY KEY(lobby_id, player_id)
    );
  `);
  await pool.query(`
    CREATE INDEX IF NOT EXISTS idx_lobby_players_lobby
    ON lobby_players(lobby_id, joined_at ASC);
  `);

  await pool.query(`
    CREATE TABLE IF NOT EXISTS client_releases (
      channel TEXT NOT NULL,
      platform TEXT NOT NULL,
      version TEXT NOT NULL,
      min_supported_version TEXT NOT NULL,
      download_url TEXT NOT NULL,
      sha256 TEXT NOT NULL DEFAULT '',
      notes_url TEXT NOT NULL DEFAULT '',
      size_bytes BIGINT NOT NULL DEFAULT 0,
      created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
      is_active BOOLEAN NOT NULL DEFAULT TRUE,
      PRIMARY KEY(channel, platform, version)
    );
  `);
  await pool.query(`
    CREATE INDEX IF NOT EXISTS idx_client_releases_track_active
    ON client_releases(channel, platform, is_active, created_at DESC);
  `);
}

function normalizeTrackValue(value, fallback) {
  const normalized = String(value || fallback || "").trim().toLowerCase();
  return normalized.length > 0 ? normalized : fallback;
}

function validateVersion(value) {
  // Keep it simple and compatible with Unity Application.version values.
  return typeof value === "string" && /^[0-9A-Za-z._-]{1,32}$/.test(value.trim());
}

function validatePlayerId(value) {
  return typeof value === "string" && /^[A-Za-z0-9_-]{6,64}$/.test(value.trim());
}

function normalizePlayerText(value, maxLen = 64) {
  return String(value || "").trim().slice(0, maxLen);
}

function readAdminToken(req) {
  const fromHeader = req.get("x-admin-token");
  if (fromHeader && fromHeader.trim().length > 0) return fromHeader.trim();

  const auth = req.get("authorization");
  if (auth && auth.startsWith("Bearer ")) return auth.substring(7).trim();

  return "";
}

function readInternalToken(req) {
  const fromHeader = req.get("x-internal-token");
  if (fromHeader && fromHeader.trim().length > 0) return fromHeader.trim();

  return "";
}

function requireAdminToken(req, res) {
  if (!RELEASE_ADMIN_TOKEN) {
    res.status(503).json({ error: "release admin token is not configured" });
    return false;
  }

  const token = readAdminToken(req);
  if (!token || token !== RELEASE_ADMIN_TOKEN) {
    res.status(401).json({ error: "unauthorized" });
    return false;
  }

  return true;
}

function requireInternalToken(req, res) {
  if (!INTERNAL_API_TOKEN) {
    return true;
  }

  const token = readInternalToken(req);
  if (!token || token !== INTERNAL_API_TOKEN) {
    logEvent("warn", "internal_token_rejected", {
      method: req.method,
      path: req.path,
      ip: req.ip || ""
    });
    res.status(401).json({ error: "unauthorized" });
    return false;
  }

  return true;
}

function createRateLimiter({ windowMs, max, scope }) {
  const buckets = new Map();

  setInterval(() => {
    const cutoff = now() - windowMs * 2;
    for (const [key, bucket] of buckets) {
      if (bucket.resetAt <= cutoff) {
        buckets.delete(key);
      }
    }
  }, Math.max(5_000, windowMs)).unref();

  return (req, res, next) => {
    const ip = req.ip || req.socket?.remoteAddress || "unknown";
    const route = req.path || req.originalUrl || "/";
    const key = `${ip}|${scope}|${route}`;
    const current = now();
    let bucket = buckets.get(key);
    if (!bucket || bucket.resetAt <= current) {
      bucket = { count: 0, resetAt: current + windowMs };
      buckets.set(key, bucket);
    }

    bucket.count += 1;
    if (bucket.count > max) {
      const retryAfterSeconds = Math.max(1, Math.ceil((bucket.resetAt - current) / 1000));
      res.setHeader("Retry-After", String(retryAfterSeconds));
      logEvent("warn", "rate_limited", {
        scope,
        ip,
        route,
        count: bucket.count,
        max
      });
      return res.status(429).json({ error: "rate_limited" });
    }

    next();
  };
}

function mapReleaseRow(row) {
  return {
    channel: row.channel,
    platform: row.platform,
    version: row.version,
    minSupportedVersion: row.min_supported_version,
    downloadUrl: row.download_url,
    sha256: row.sha256 || "",
    notesUrl: row.notes_url || "",
    sizeBytes: Number(row.size_bytes || 0),
    publishedAt: row.created_at
  };
}

function mapPlayerRow(row) {
  return {
    ugsPlayerId: row.ugs_player_id,
    username: row.username,
    displayName: row.display_name,
    createdAt: row.created_at,
    lastSeenAt: row.last_seen_at
  };
}

function normalizeResultValue(value) {
  const safe = String(value || "").trim().toLowerCase();
  if (safe === "win" || safe === "loss" || safe === "draw") {
    return safe;
  }
  return "draw";
}

function mapPlayerStatsRow(row) {
  return {
    playerId: row.player_id,
    gamesPlayed: Number(row.games_played || 0),
    wins: Number(row.wins || 0),
    losses: Number(row.losses || 0),
    draws: Number(row.draws || 0),
    surrenders: Number(row.surrenders || 0),
    rankPoints: Number(row.rank_points || 0),
    scoreTotal: Number(row.score_total || 0),
    lastResult: row.last_result || "",
    lastMatchId: row.last_match_id || "",
    lastMatchAt: row.last_match_at || null,
    updatedAt: row.updated_at
  };
}

function mapMatchHistoryRow(row) {
  return {
    matchId: row.match_id,
    completedAt: row.completed_at,
    wasSurrendered: !!row.was_surrendered,
    winnerPlayerId: row.winner_player_id || "",
    surrenderingPlayerId: row.surrendering_player_id || "",
    playerId: row.player_id,
    playerSlot: Number(row.player_slot || -1),
    score: Number(row.score || 0),
    result: normalizeResultValue(row.result),
    opponents: Array.isArray(row.opponents) ? row.opponents : []
  };
}

function mapMatchResultRow(row) {
  return {
    matchId: row.match_id,
    winnerPlayerId: row.winner_player_id || "",
    wasSurrendered: !!row.was_surrendered,
    surrenderingPlayerId: row.surrendering_player_id || "",
    completedAt: row.completed_at,
    players: Array.isArray(row.players) ? row.players : []
  };
}

const LOBBY_CODE_CHARS = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

function normalizeLobbyText(value, maxLen = 64) {
  return String(value || "").trim().slice(0, maxLen);
}

function parseLobbyData(data) {
  const safe = data && typeof data === "object" ? data : {};
  const serverPort = Number.parseInt(String(safe.serverPort || "0"), 10);
  return {
    map: normalizeLobbyText(safe.map || "", 32),
    mode: normalizeLobbyText(safe.mode || "", 32),
    region: normalizeLobbyText(safe.region || "", 32),
    serverIp: normalizeLobbyText(safe.serverIp || "", 64),
    serverPort: Number.isFinite(serverPort) ? serverPort : 0,
    matchId: normalizeLobbyText(safe.matchId || "", 64)
  };
}

function mapLobbyRow(row) {
  const playersRaw = Array.isArray(row.players) ? row.players : [];
  const players = playersRaw.map((p) => ({
    id: normalizePlayerText(p.id || p.player_id || "", 64),
    ready: !!p.ready
  }));
  const data = parseLobbyData(row.data);

  return {
    id: row.lobby_id,
    lobbyCode: row.lobby_code,
    name: row.name,
    maxPlayers: Number(row.max_players),
    hostId: row.host_player_id,
    map: data.map,
    mode: data.mode,
    region: data.region,
    serverIp: data.serverIp,
    serverPort: data.serverPort,
    matchId: data.matchId,
    players,
    createdAt: row.created_at,
    updatedAt: row.updated_at
  };
}

async function generateLobbyCode(client) {
  for (let attempt = 0; attempt < 12; attempt++) {
    let code = "";
    for (let i = 0; i < 6; i++) {
      const idx = Math.floor(Math.random() * LOBBY_CODE_CHARS.length);
      code += LOBBY_CODE_CHARS[idx];
    }

    const exists = await client.query(
      `SELECT 1 FROM lobbies WHERE lobby_code = $1 AND closed_at IS NULL LIMIT 1`,
      [code]
    );
    if (exists.rowCount === 0) {
      return code;
    }
  }

  throw new Error("failed to allocate lobby code");
}

function buildLobbyDataJson(currentData, updates = {}) {
  const base = parseLobbyData(currentData);
  if (Object.prototype.hasOwnProperty.call(updates, "map")) {
    base.map = normalizeLobbyText(updates.map, 32);
  }
  if (Object.prototype.hasOwnProperty.call(updates, "mode")) {
    base.mode = normalizeLobbyText(updates.mode, 32);
  }
  if (Object.prototype.hasOwnProperty.call(updates, "region")) {
    base.region = normalizeLobbyText(updates.region, 32);
  }
  if (Object.prototype.hasOwnProperty.call(updates, "serverIp")) {
    base.serverIp = normalizeLobbyText(updates.serverIp, 64);
  }
  if (Object.prototype.hasOwnProperty.call(updates, "serverPort")) {
    const nextPort = Number.parseInt(String(updates.serverPort || "0"), 10);
    base.serverPort = Number.isFinite(nextPort) ? nextPort : 0;
  }
  if (Object.prototype.hasOwnProperty.call(updates, "matchId")) {
    base.matchId = normalizeLobbyText(updates.matchId, 64);
  }
  return base;
}

const SELECT_LOBBY_WITH_PLAYERS_SQL = `
  SELECT
    l.lobby_id,
    l.lobby_code,
    l.name,
    l.max_players,
    l.host_player_id,
    l.data,
    l.created_at,
    l.updated_at,
    COALESCE(
      jsonb_agg(
        jsonb_build_object(
          'id', lp.player_id,
          'ready', lp.ready,
          'joinedAt', lp.joined_at
        ) ORDER BY lp.joined_at
      ) FILTER (WHERE lp.player_id IS NOT NULL),
      '[]'::jsonb
    ) AS players
  FROM lobbies l
  LEFT JOIN lobby_players lp ON lp.lobby_id = l.lobby_id
  WHERE l.lobby_id = $1
    AND l.closed_at IS NULL
  GROUP BY l.lobby_id
  LIMIT 1
`;

async function getLobbyById(lobbyId, client = pool) {
  const result = await client.query(SELECT_LOBBY_WITH_PLAYERS_SQL, [lobbyId]);
  return result.rows[0] || null;
}

async function closeLobbyIfEmpty(lobbyId, client) {
  const countResult = await client.query(
    `SELECT COUNT(*)::INT AS player_count FROM lobby_players WHERE lobby_id = $1`,
    [lobbyId]
  );
  const playerCount = Number(countResult.rows[0]?.player_count || 0);
  if (playerCount > 0) {
    return false;
  }

  await client.query(
    `UPDATE lobbies
     SET closed_at = NOW(),
         updated_at = NOW()
     WHERE lobby_id = $1
       AND closed_at IS NULL`,
    [lobbyId]
  );
  return true;
}

async function closeLobbyByMatchId(matchId, reason = "match_closed") {
  if (!matchId) {
    return;
  }

  const row = await pool.query(
    `SELECT lobby_id
     FROM lobbies
     WHERE closed_at IS NULL
       AND COALESCE(data->>'matchId', '') = $1
     LIMIT 1`,
    [matchId]
  );

  const lobbyId = row.rows[0]?.lobby_id;
  if (!lobbyId) {
    return;
  }

  await closeLobbyById(lobbyId, reason);
}

async function getMatchById(matchId, client = pool) {
  const result = await client.query(
    `SELECT
       m.*,
       COALESCE(
         (
           SELECT jsonb_agg(mp.player_id ORDER BY mp.player_id)
           FROM match_presence mp
           WHERE mp.match_id = m.match_id
             AND mp.connected = TRUE
         ),
         '[]'::jsonb
       ) AS connected_players
     FROM matches m
     WHERE m.match_id = $1
     LIMIT 1`,
    [matchId]
  );
  return result.rows[0] || null;
}

async function getActiveMatchByLobby(lobbyId, client = pool) {
  const result = await client.query(
    `SELECT
       m.*,
       COALESCE(
         (
           SELECT jsonb_agg(mp.player_id ORDER BY mp.player_id)
           FROM match_presence mp
           WHERE mp.match_id = m.match_id
             AND mp.connected = TRUE
         ),
         '[]'::jsonb
       ) AS connected_players
     FROM matches m
     WHERE m.lobby_id = $1
       AND m.ended_at IS NULL
     LIMIT 1`,
    [lobbyId]
  );
  return result.rows[0] || null;
}

async function getActiveMatchByPlayer(playerId, client = pool) {
  const result = await client.query(
    `SELECT
       m.match_id,
       m.lobby_id,
       COALESCE(mp.connected, FALSE) AS is_connected
     FROM player_last_match pl
     JOIN matches m ON m.match_id = pl.match_id
     LEFT JOIN match_presence mp
       ON mp.match_id = m.match_id
      AND mp.player_id = pl.player_id
     WHERE pl.player_id = $1
       AND m.ended_at IS NULL
     LIMIT 1`,
    [playerId]
  );
  return result.rows[0] || null;
}

async function clearPlayerLastMapping(matchId) {
  await pool.query(`DELETE FROM player_last_match WHERE match_id = $1`, [matchId]);
}

async function closeLobbyById(lobbyId, reason = "closed") {
  if (!lobbyId) {
    return;
  }

  await pool.query(
    `UPDATE lobbies
     SET closed_at = NOW(),
         updated_at = NOW()
     WHERE lobby_id = $1
       AND closed_at IS NULL`,
    [lobbyId]
  );

  await pool.query(`DELETE FROM lobby_players WHERE lobby_id = $1`, [lobbyId]);
  logEvent("info", "lobby_closed", { lobbyId, reason });
}

async function upsertPlayerLastMapping(players, matchId) {
  for (const playerId of players) {
    await pool.query(
      `INSERT INTO player_last_match(player_id, match_id, updated_at)
       VALUES ($1, $2, NOW())
       ON CONFLICT (player_id)
       DO UPDATE SET match_id = EXCLUDED.match_id, updated_at = NOW()`,
      [playerId, matchId]
    );
  }
}

async function upsertMatchPresence(players, matchId, connected = false) {
  for (const playerId of players) {
    await pool.query(
      `INSERT INTO match_presence(match_id, player_id, connected, updated_at)
       VALUES ($1, $2, $3, NOW())
       ON CONFLICT (match_id, player_id)
       DO UPDATE SET connected = EXCLUDED.connected, updated_at = NOW()`,
      [matchId, playerId, !!connected]
    );
  }
}

async function setMatchPresence(matchId, playerId, connected, client = pool) {
  await client.query(
    `INSERT INTO match_presence(match_id, player_id, connected, updated_at)
     VALUES ($1, $2, $3, NOW())
     ON CONFLICT (match_id, player_id)
     DO UPDATE SET connected = EXCLUDED.connected, updated_at = NOW()`,
    [matchId, playerId, !!connected]
  );
}

function getResultForPlayer(playerId, winnerPlayerId) {
  if (!winnerPlayerId) {
    return "draw";
  }

  return playerId === winnerPlayerId ? "win" : "loss";
}

function getRankPointsForResult(result) {
  if (result === "win") return 3;
  if (result === "draw") return 1;
  return 0;
}

async function upsertPlayerStatsForResult(client, entry) {
  const safeResult = normalizeResultValue(entry.result);
  const wins = safeResult === "win" ? 1 : 0;
  const losses = safeResult === "loss" ? 1 : 0;
  const draws = safeResult === "draw" ? 1 : 0;
  const surrenders = entry.wasSurrendered && entry.playerId === entry.surrenderingPlayerId ? 1 : 0;
  const rankPoints = getRankPointsForResult(safeResult);

  await client.query(
    `INSERT INTO player_stats(
       player_id,
       games_played,
       wins,
       losses,
       draws,
       surrenders,
       rank_points,
       score_total,
       last_result,
       last_match_id,
       last_match_at,
       updated_at
     ) VALUES (
       $1,
       1,
       $2,
       $3,
       $4,
       $5,
       $6,
       $7,
       $8,
       $9,
       NOW(),
       NOW()
     )
     ON CONFLICT (player_id)
     DO UPDATE SET
       games_played = player_stats.games_played + 1,
       wins = player_stats.wins + EXCLUDED.wins,
       losses = player_stats.losses + EXCLUDED.losses,
       draws = player_stats.draws + EXCLUDED.draws,
       surrenders = player_stats.surrenders + EXCLUDED.surrenders,
       rank_points = player_stats.rank_points + EXCLUDED.rank_points,
       score_total = player_stats.score_total + EXCLUDED.score_total,
       last_result = EXCLUDED.last_result,
       last_match_id = EXCLUDED.last_match_id,
       last_match_at = NOW(),
       updated_at = NOW()`,
    [
      entry.playerId,
      wins,
      losses,
      draws,
      surrenders,
      rankPoints,
      Number(entry.score || 0),
      safeResult,
      entry.matchId
    ]
  );
}

async function getMatchResultById(matchId, client = pool) {
  const result = await client.query(
    `SELECT
       mr.match_id,
       mr.winner_player_id,
       mr.was_surrendered,
       mr.surrendering_player_id,
       mr.completed_at,
       COALESCE(
         jsonb_agg(
           jsonb_build_object(
             'playerId', mrp.player_id,
             'playerSlot', mrp.player_slot,
             'score', mrp.score,
             'result', mrp.result
           )
           ORDER BY mrp.player_slot ASC, mrp.player_id ASC
         ) FILTER (WHERE mrp.player_id IS NOT NULL),
         '[]'::jsonb
       ) AS players
     FROM match_results mr
     LEFT JOIN match_result_players mrp ON mrp.match_id = mr.match_id
     WHERE mr.match_id = $1
     GROUP BY mr.match_id
     LIMIT 1`,
    [matchId]
  );

  return result.rows[0] || null;
}

async function markMatchEnded(matchId, reason = "ended") {
  const updated = await pool.query(
    `UPDATE matches
     SET ended_at = COALESCE(ended_at, NOW()),
         expires_at = NOW() + ($2::TEXT || ' milliseconds')::INTERVAL
     WHERE match_id = $1
     RETURNING *`,
    [matchId, END_GRACE_MS]
  );
  const row = updated.rows[0];
  if (!row) return null;

  await clearPlayerLastMapping(matchId);
  logEvent("info", "match_marked_ended", { matchId, reason });
  return row;
}

async function cleanupMatch(matchId, reason = "cleanup") {
  const deleted = await pool.query(`DELETE FROM matches WHERE match_id = $1 RETURNING *`, [matchId]);
  const row = deleted.rows[0];
  if (!row) return;

  await closeLobbyById(row.lobby_id, `match_${reason}`);
  await clearPlayerLastMapping(matchId);
  reservedPorts.delete(Number(row.server_port));
  processes.delete(matchId);

  logEvent("info", "match_removed", { matchId, reason });
}

async function terminateMatch(matchId, signal = "SIGTERM") {
  const row = await getMatchById(matchId);
  if (!row) return;
  if (row.terminating) return;

  await pool.query(`UPDATE matches SET terminating = TRUE WHERE match_id = $1`, [matchId]);

  const pid = Number(row.pid || 0);
  if (pid <= 0) return;
  try {
    process.kill(pid, signal);
  } catch (_) {}
}

async function allocatePort() {
  const usedResult = await pool.query(`SELECT server_port FROM matches WHERE ended_at IS NULL`);
  const inUse = new Set(usedResult.rows.map((r) => Number(r.server_port)));

  for (let p = PORT_MIN; p <= PORT_MAX; p++) {
    if (inUse.has(p) || reservedPorts.has(p)) continue;
    if (await isUdpPortFree(p)) return p;
  }
  return null;
}

async function createMatch({ lobbyId, players = [], map = "", mode = "", region = "" }) {
  return withCreateLock(async () => {
    if (!lobbyId) throw new Error("missing lobbyId");
    if (!Array.isArray(players) || players.length === 0) throw new Error("missing players");

    const existing = await getActiveMatchByLobby(lobbyId);
    if (existing) {
      return existing;
    }

    const serverPort = await allocatePort();
    if (!serverPort) throw new Error("no free server port");

    reservedPorts.add(serverPort);

    const matchId = crypto.randomUUID().replace(/-/g, "");
    const logFile = path.join(LOG_DIR, `${matchId}.log`);

    const env = {
      ...process.env,
      PERIBIND_MATCH_ID: matchId,
      PERIBIND_SERVER_PORT: String(serverPort),
      PERIBIND_MATCH_REGISTRY_URL: REGISTRY_SELF_URL,
      PERIBIND_MATCH_MAP: String(map || ""),
      PERIBIND_MATCH_MODE: String(mode || ""),
      PERIBIND_MATCH_REGION: String(region || "")
    };

    let child;
    try {
      child = spawn(
        SERVER_BIN,
        [
          "-port", String(serverPort),
          "-matchId", matchId,
          "-logFile", logFile
        ],
        {
          cwd: SERVER_CWD,
          env,
          stdio: "ignore"
        }
      );
    } catch (e) {
      reservedPorts.delete(serverPort);
      throw new Error(`spawn failed: ${e.message || e}`);
    }

    if (!child.pid) {
      reservedPorts.delete(serverPort);
      throw new Error("failed to spawn server process");
    }

    const insert = await pool.query(
      `INSERT INTO matches(
         match_id, lobby_id, server_ip, server_port, players,
         expires_at, pid, created_at, ended_at, terminating
       ) VALUES (
         $1, $2, $3, $4, $5::jsonb,
         NOW() + ($6::TEXT || ' milliseconds')::INTERVAL, $7, NOW(), NULL, FALSE
       )
       RETURNING *`,
      [matchId, lobbyId, SERVER_PUBLIC_IP, serverPort, JSON.stringify(players), TTL_MS, child.pid]
    );

    await upsertPlayerLastMapping(players, matchId);
    await upsertMatchPresence(players, matchId, false);

    // Port is now tracked in DB row; temp reservation no longer needed.
    reservedPorts.delete(serverPort);
    processes.set(matchId, child);

    child.on("error", async (err) => {
      logEvent("error", "match_process_error", {
        matchId,
        pid: child.pid || 0,
        message: err.message || String(err)
      });
      await cleanupMatch(matchId, "process_error");
    });

    child.on("exit", async (code, signal) => {
      logEvent("info", "match_process_exit", {
        matchId,
        pid: child.pid || 0,
        code: Number.isFinite(code) ? code : null,
        signal: signal || ""
      });
      await cleanupMatch(matchId, "process_exit");
    });

    const row = insert.rows[0];
    logEvent("info", "match_created", {
      matchId,
      lobbyId,
      pid: child.pid || 0,
      serverIp: SERVER_PUBLIC_IP,
      serverPort
    });
    return row;
  });
}

async function lifecycleSweep() {
  const result = await pool.query(`SELECT * FROM matches`);
  const t = now();

  for (const row of result.rows) {
    const matchId = row.match_id;
    const expiresAt = new Date(row.expires_at).getTime();
    const endedAt = row.ended_at ? new Date(row.ended_at).getTime() : 0;
    const terminating = !!row.terminating;
    const pid = Number(row.pid || 0);

    if (!terminating && expiresAt <= t) {
      logEvent("warn", "match_ttl_reached", { matchId, pid });
      await terminateMatch(matchId, "SIGTERM");
      setTimeout(() => {
        terminateMatch(matchId, "SIGKILL").catch(() => {});
      }, 5000);
      continue;
    }

    if (!terminating && endedAt > 0) {
      logEvent("warn", "match_ended_but_alive", { matchId, pid });
      await terminateMatch(matchId, "SIGTERM");
      setTimeout(() => {
        terminateMatch(matchId, "SIGKILL").catch(() => {});
      }, 5000);
      continue;
    }

    if (terminating && !isPidAlive(pid)) {
      await cleanupMatch(matchId, "pid_not_alive");
    }
  }
}

async function lobbyLifecycleSweep() {
  const lobbies = await pool.query(
    `SELECT
       l.lobby_id,
       l.updated_at,
       COALESCE(l.data->>'matchId', '') AS match_id,
       COUNT(lp.player_id)::INT AS player_count
     FROM lobbies l
     LEFT JOIN lobby_players lp ON lp.lobby_id = l.lobby_id
     WHERE l.closed_at IS NULL
     GROUP BY l.lobby_id, l.updated_at, l.data`
  );

  for (const row of lobbies.rows) {
    const lobbyId = row.lobby_id;
    const matchId = String(row.match_id || "").trim();
    const playerCount = Number(row.player_count || 0);
    const updatedAtMs = new Date(row.updated_at).getTime();
    const isStale = Number.isFinite(updatedAtMs) && (now() - updatedAtMs) >= LOBBY_STALE_MS;

    if (playerCount <= 0) {
      await closeLobbyById(lobbyId, "empty_lobby_sweep");
      continue;
    }

    if (!matchId) {
      if (isStale) {
        await closeLobbyById(lobbyId, "stale_lobby_sweep");
      }
      continue;
    }

    const match = await pool.query(
      `SELECT 1
       FROM matches
       WHERE match_id = $1
         AND ended_at IS NULL
       LIMIT 1`,
      [matchId]
    );

    if (match.rowCount === 0) {
      await closeLobbyById(lobbyId, "orphan_match_sweep");
    }
  }
}

const globalRateLimit = createRateLimiter({
  windowMs: Math.max(1000, RATE_LIMIT_WINDOW_MS),
  max: Math.max(20, RATE_LIMIT_MAX_GLOBAL),
  scope: "global"
});
const lobbyRateLimit = createRateLimiter({
  windowMs: Math.max(1000, RATE_LIMIT_WINDOW_MS),
  max: Math.max(10, RATE_LIMIT_MAX_LOBBY),
  scope: "lobby"
});
const matchRateLimit = createRateLimiter({
  windowMs: Math.max(1000, RATE_LIMIT_WINDOW_MS),
  max: Math.max(10, RATE_LIMIT_MAX_MATCH),
  scope: "match"
});
const releaseRateLimit = createRateLimiter({
  windowMs: Math.max(1000, RATE_LIMIT_WINDOW_MS),
  max: Math.max(5, RATE_LIMIT_MAX_RELEASE),
  scope: "release"
});

app.use(globalRateLimit);
app.use("/lobby", lobbyRateLimit);
app.use("/match", matchRateLimit);
app.use("/release", releaseRateLimit);

// ---- API ----
app.post("/lobby/create", async (req, res) => {
  const playerId = normalizePlayerText(req.body?.playerId, 64);
  const name = normalizeLobbyText(req.body?.name || req.body?.lobbyName || "Match", 64);
  const maxPlayers = Number(req.body?.maxPlayers || 2);
  const map = normalizeLobbyText(req.body?.map || "", 32);
  const mode = normalizeLobbyText(req.body?.mode || "", 32);
  const region = normalizeLobbyText(req.body?.region || "", 32);

  if (!validatePlayerId(playerId)) {
    return res.status(400).json({ error: "invalid playerId" });
  }

  if (!Number.isInteger(maxPlayers) || maxPlayers < 2 || maxPlayers > 16) {
    return res.status(400).json({ error: "invalid maxPlayers" });
  }

  const client = await pool.connect();
  try {
    await client.query("BEGIN");

    const lobbyId = crypto.randomUUID().replace(/-/g, "");
    const lobbyCode = await generateLobbyCode(client);
    const data = buildLobbyDataJson({}, { map, mode, region, serverIp: "", serverPort: 0, matchId: "" });

    await client.query(
      `INSERT INTO lobbies(
         lobby_id, lobby_code, name, max_players, host_player_id, data, created_at, updated_at, closed_at
       ) VALUES ($1, $2, $3, $4, $5, $6::jsonb, NOW(), NOW(), NULL)`,
      [lobbyId, lobbyCode, name || "Match", maxPlayers, playerId, JSON.stringify(data)]
    );

    await client.query(
      `INSERT INTO lobby_players(lobby_id, player_id, ready, joined_at, updated_at)
       VALUES ($1, $2, FALSE, NOW(), NOW())`,
      [lobbyId, playerId]
    );

    const row = await getLobbyById(lobbyId, client);
    await client.query("COMMIT");
    return res.json(mapLobbyRow(row));
  } catch (e) {
    await client.query("ROLLBACK");
    return res.status(500).json({ error: e.message || "lobby_create_failed" });
  } finally {
    client.release();
  }
});

app.get("/lobby/list", async (req, res) => {
  const map = normalizeLobbyText(req.query?.map || "", 32);
  const includeFull = String(req.query?.includeFull || "1").trim() !== "0";
  const params = [];
  const where = ["l.closed_at IS NULL"];

  if (map) {
    params.push(map);
    where.push(`COALESCE(l.data->>'map', '') = $${params.length}`);
  }

  if (!includeFull) {
    where.push(`(SELECT COUNT(*) FROM lobby_players lp2 WHERE lp2.lobby_id = l.lobby_id) < l.max_players`);
  }

  const query = `
    SELECT
      l.lobby_id,
      l.lobby_code,
      l.name,
      l.max_players,
      l.host_player_id,
      l.data,
      l.created_at,
      l.updated_at,
      COALESCE(
        jsonb_agg(
          jsonb_build_object(
            'id', lp.player_id,
            'ready', lp.ready,
            'joinedAt', lp.joined_at
          ) ORDER BY lp.joined_at
        ) FILTER (WHERE lp.player_id IS NOT NULL),
        '[]'::jsonb
      ) AS players
    FROM lobbies l
    LEFT JOIN lobby_players lp ON lp.lobby_id = l.lobby_id
    WHERE ${where.join(" AND ")}
    GROUP BY l.lobby_id
    ORDER BY l.updated_at DESC
    LIMIT 50
  `;

  const result = await pool.query(query, params);
  return res.json({ results: result.rows.map(mapLobbyRow) });
});

app.get("/lobby/:id", async (req, res) => {
  const lobbyId = normalizeLobbyText(req.params.id, 64);
  if (!lobbyId) {
    return res.status(400).json({ error: "missing lobbyId" });
  }

  const row = await getLobbyById(lobbyId);
  if (!row) {
    return res.status(404).json({ error: "not found" });
  }

  return res.json(mapLobbyRow(row));
});

app.post("/lobby/join", async (req, res) => {
  const lobbyId = normalizeLobbyText(req.body?.lobbyId, 64);
  const playerId = normalizePlayerText(req.body?.playerId, 64);
  if (!lobbyId || !validatePlayerId(playerId)) {
    return res.status(400).json({ error: "invalid request" });
  }

  const client = await pool.connect();
  try {
    await client.query("BEGIN");
    const lobbyResult = await client.query(
      `SELECT * FROM lobbies
       WHERE lobby_id = $1
         AND closed_at IS NULL
       FOR UPDATE`,
      [lobbyId]
    );
    const lobby = lobbyResult.rows[0];
    if (!lobby) {
      await client.query("ROLLBACK");
      return res.status(404).json({ error: "not found" });
    }

    const activeMatch = await getActiveMatchByPlayer(playerId, client);
    if (activeMatch) {
      await client.query("ROLLBACK");
      if (activeMatch.lobby_id === lobbyId) {
        const isConnected = !!activeMatch.is_connected;
        return res.status(409).json({
          error: isConnected
            ? "already_in_active_match_connected"
            : "already_in_active_match_disconnected",
          matchId: activeMatch.match_id
        });
      }
      return res.status(409).json({ error: "player_busy_in_other_match", matchId: activeMatch.match_id });
    }

    const membership = await client.query(
      `SELECT 1 FROM lobby_players WHERE lobby_id = $1 AND player_id = $2 LIMIT 1`,
      [lobbyId, playerId]
    );
    const alreadyInLobby = membership.rowCount > 0;

    if (!alreadyInLobby) {
      const countResult = await client.query(
        `SELECT COUNT(*)::INT AS player_count FROM lobby_players WHERE lobby_id = $1`,
        [lobbyId]
      );
      const playerCount = Number(countResult.rows[0]?.player_count || 0);
      if (playerCount >= Number(lobby.max_players)) {
        await client.query("ROLLBACK");
        return res.status(409).json({ error: "lobby_full" });
      }

      await client.query(
        `INSERT INTO lobby_players(lobby_id, player_id, ready, joined_at, updated_at)
         VALUES ($1, $2, FALSE, NOW(), NOW())`,
        [lobbyId, playerId]
      );
    }

    await client.query(`UPDATE lobbies SET updated_at = NOW() WHERE lobby_id = $1`, [lobbyId]);
    const row = await getLobbyById(lobbyId, client);
    await client.query("COMMIT");
    return res.json(mapLobbyRow(row));
  } catch (e) {
    await client.query("ROLLBACK");
    return res.status(500).json({ error: e.message || "lobby_join_failed" });
  } finally {
    client.release();
  }
});

app.post("/lobby/join-by-code", async (req, res) => {
  const code = normalizeLobbyText(req.body?.code, 16).toUpperCase();
  const playerId = normalizePlayerText(req.body?.playerId, 64);
  if (!code || !validatePlayerId(playerId)) {
    return res.status(400).json({ error: "invalid request" });
  }

  const client = await pool.connect();
  try {
    await client.query("BEGIN");
    const lobbyResult = await client.query(
      `SELECT * FROM lobbies
       WHERE lobby_code = $1
         AND closed_at IS NULL
       FOR UPDATE`,
      [code]
    );
    const lobby = lobbyResult.rows[0];
    if (!lobby) {
      await client.query("ROLLBACK");
      return res.status(404).json({ error: "not found" });
    }

    const lobbyId = lobby.lobby_id;
    const activeMatch = await getActiveMatchByPlayer(playerId, client);
    if (activeMatch) {
      await client.query("ROLLBACK");
      if (activeMatch.lobby_id === lobbyId) {
        const isConnected = !!activeMatch.is_connected;
        return res.status(409).json({
          error: isConnected
            ? "already_in_active_match_connected"
            : "already_in_active_match_disconnected",
          matchId: activeMatch.match_id
        });
      }
      return res.status(409).json({ error: "player_busy_in_other_match", matchId: activeMatch.match_id });
    }

    const membership = await client.query(
      `SELECT 1 FROM lobby_players WHERE lobby_id = $1 AND player_id = $2 LIMIT 1`,
      [lobbyId, playerId]
    );
    const alreadyInLobby = membership.rowCount > 0;

    if (!alreadyInLobby) {
      const countResult = await client.query(
        `SELECT COUNT(*)::INT AS player_count FROM lobby_players WHERE lobby_id = $1`,
        [lobbyId]
      );
      const playerCount = Number(countResult.rows[0]?.player_count || 0);
      if (playerCount >= Number(lobby.max_players)) {
        await client.query("ROLLBACK");
        return res.status(409).json({ error: "lobby_full" });
      }

      await client.query(
        `INSERT INTO lobby_players(lobby_id, player_id, ready, joined_at, updated_at)
         VALUES ($1, $2, FALSE, NOW(), NOW())`,
        [lobbyId, playerId]
      );
    }

    await client.query(`UPDATE lobbies SET updated_at = NOW() WHERE lobby_id = $1`, [lobbyId]);
    const row = await getLobbyById(lobbyId, client);
    await client.query("COMMIT");
    return res.json(mapLobbyRow(row));
  } catch (e) {
    await client.query("ROLLBACK");
    return res.status(500).json({ error: e.message || "lobby_join_failed" });
  } finally {
    client.release();
  }
});

app.post("/lobby/leave", async (req, res) => {
  const lobbyId = normalizeLobbyText(req.body?.lobbyId, 64);
  const playerId = normalizePlayerText(req.body?.playerId, 64);
  if (!lobbyId || !validatePlayerId(playerId)) {
    return res.status(400).json({ error: "invalid request" });
  }

  const client = await pool.connect();
  try {
    await client.query("BEGIN");
    const lobbyResult = await client.query(
      `SELECT * FROM lobbies
       WHERE lobby_id = $1
         AND closed_at IS NULL
       FOR UPDATE`,
      [lobbyId]
    );
    const lobby = lobbyResult.rows[0];
    if (!lobby) {
      await client.query("ROLLBACK");
      return res.json({ ok: true, closed: true });
    }

    await client.query(
      `DELETE FROM lobby_players
       WHERE lobby_id = $1
         AND player_id = $2`,
      [lobbyId, playerId]
    );

    const closed = await closeLobbyIfEmpty(lobbyId, client);
    if (closed) {
      await client.query("COMMIT");
      return res.json({ ok: true, closed: true });
    }

    if (lobby.host_player_id === playerId) {
      const nextHost = await client.query(
        `SELECT player_id
         FROM lobby_players
         WHERE lobby_id = $1
         ORDER BY joined_at ASC
         LIMIT 1`,
        [lobbyId]
      );
      const nextHostId = nextHost.rows[0]?.player_id || "";
      if (nextHostId) {
        await client.query(
          `UPDATE lobbies
           SET host_player_id = $2, updated_at = NOW()
           WHERE lobby_id = $1`,
          [lobbyId, nextHostId]
        );
      }
    } else {
      await client.query(`UPDATE lobbies SET updated_at = NOW() WHERE lobby_id = $1`, [lobbyId]);
    }

    const row = await getLobbyById(lobbyId, client);
    await client.query("COMMIT");
    return res.json({ ok: true, closed: false, lobby: row ? mapLobbyRow(row) : null });
  } catch (e) {
    await client.query("ROLLBACK");
    return res.status(500).json({ error: e.message || "lobby_leave_failed" });
  } finally {
    client.release();
  }
});

app.post("/lobby/player-ready", async (req, res) => {
  const lobbyId = normalizeLobbyText(req.body?.lobbyId, 64);
  const playerId = normalizePlayerText(req.body?.playerId, 64);
  const isReady = !!req.body?.isReady;
  if (!lobbyId || !validatePlayerId(playerId)) {
    return res.status(400).json({ error: "invalid request" });
  }

  const client = await pool.connect();
  try {
    await client.query("BEGIN");
    const lobbyResult = await client.query(
      `SELECT 1 FROM lobbies
       WHERE lobby_id = $1
         AND closed_at IS NULL
       FOR UPDATE`,
      [lobbyId]
    );
    if (lobbyResult.rowCount === 0) {
      await client.query("ROLLBACK");
      return res.status(404).json({ error: "not found" });
    }

    const updated = await client.query(
      `UPDATE lobby_players
       SET ready = $3,
           updated_at = NOW()
       WHERE lobby_id = $1
         AND player_id = $2`,
      [lobbyId, playerId, isReady]
    );
    if (updated.rowCount === 0) {
      await client.query("ROLLBACK");
      return res.status(403).json({ error: "player_not_in_lobby" });
    }

    await client.query(`UPDATE lobbies SET updated_at = NOW() WHERE lobby_id = $1`, [lobbyId]);
    const row = await getLobbyById(lobbyId, client);
    await client.query("COMMIT");
    return res.json(mapLobbyRow(row));
  } catch (e) {
    await client.query("ROLLBACK");
    return res.status(500).json({ error: e.message || "lobby_ready_failed" });
  } finally {
    client.release();
  }
});

app.post("/lobby/server-info", async (req, res) => {
  const lobbyId = normalizeLobbyText(req.body?.lobbyId, 64);
  const playerId = normalizePlayerText(req.body?.playerId, 64);
  const serverIp = normalizeLobbyText(req.body?.serverIp, 64);
  const serverPort = Number.parseInt(String(req.body?.serverPort || "0"), 10);
  const matchId = normalizeLobbyText(req.body?.matchId, 64);

  if (!lobbyId || !validatePlayerId(playerId) || !serverIp || !Number.isInteger(serverPort) || serverPort <= 0) {
    return res.status(400).json({ error: "invalid request" });
  }

  const client = await pool.connect();
  try {
    await client.query("BEGIN");
    const lobbyResult = await client.query(
      `SELECT * FROM lobbies
       WHERE lobby_id = $1
         AND closed_at IS NULL
       FOR UPDATE`,
      [lobbyId]
    );
    const lobby = lobbyResult.rows[0];
    if (!lobby) {
      await client.query("ROLLBACK");
      return res.status(404).json({ error: "not found" });
    }
    if (lobby.host_player_id !== playerId) {
      await client.query("ROLLBACK");
      return res.status(403).json({ error: "host_only" });
    }

    const data = buildLobbyDataJson(lobby.data, { serverIp, serverPort, matchId });
    await client.query(
      `UPDATE lobbies
       SET data = $2::jsonb,
           updated_at = NOW()
       WHERE lobby_id = $1`,
      [lobbyId, JSON.stringify(data)]
    );

    const row = await getLobbyById(lobbyId, client);
    await client.query("COMMIT");
    return res.json(mapLobbyRow(row));
  } catch (e) {
    await client.query("ROLLBACK");
    return res.status(500).json({ error: e.message || "lobby_server_info_failed" });
  } finally {
    client.release();
  }
});

app.post("/match/create", async (req, res) => {
  try {
    const { lobbyId, players, map, mode, region } = req.body || {};
    const match = await createMatch({ lobbyId, players, map, mode, region });
    res.json(buildMatchPayload(match));
  } catch (e) {
    res.status(400).json({ error: e.message || "create_failed" });
  }
});

// Backward compatibility endpoint (not used by current flow)
app.post("/match/register", async (req, res) => {
  if (!requireInternalToken(req, res)) return;

  try {
    const { matchId, serverIp, serverPort, players } = req.body || {};
    if (!matchId || !serverIp || !serverPort || !players || !Array.isArray(players)) {
      return res.status(400).json({ error: "missing fields" });
    }

    await pool.query(
      `INSERT INTO matches(
         match_id, lobby_id, server_ip, server_port, players,
         expires_at, pid, created_at, ended_at, terminating
       ) VALUES (
         $1, '', $2, $3, $4::jsonb,
         NOW() + ($5::TEXT || ' milliseconds')::INTERVAL, 0, NOW(), NULL, FALSE
       )
       ON CONFLICT (match_id) DO UPDATE
       SET server_ip = EXCLUDED.server_ip,
           server_port = EXCLUDED.server_port,
           players = EXCLUDED.players,
           expires_at = EXCLUDED.expires_at,
           ended_at = NULL,
           terminating = FALSE`,
      [matchId, serverIp, Number(serverPort), JSON.stringify(players), TTL_MS]
    );

    await upsertPlayerLastMapping(players, matchId);
    await upsertMatchPresence(players, matchId, false);
    return res.json({ ok: true });
  } catch (e) {
    return res.status(500).json({ error: e.message || "register_failed" });
  }
});

app.post("/match/presence", async (req, res) => {
  if (!requireInternalToken(req, res)) return;

  const matchId = normalizeLobbyText(req.body?.matchId, 64);
  const playerId = normalizePlayerText(req.body?.playerId, 64);
  const connected = !!req.body?.connected;
  if (!matchId || !validatePlayerId(playerId)) {
    return res.status(400).json({ error: "invalid request" });
  }

  const client = await pool.connect();
  try {
    await client.query("BEGIN");
    const match = await getMatchById(matchId, client);
    if (!match || match.ended_at) {
      await client.query("ROLLBACK");
      return res.status(404).json({ error: "not found" });
    }

    const players = Array.isArray(match.players) ? match.players.map((p) => String(p)) : [];
    if (!players.includes(playerId)) {
      await client.query("ROLLBACK");
      return res.status(403).json({ error: "not_in_match" });
    }

    await setMatchPresence(matchId, playerId, connected, client);
    await client.query("COMMIT");
    return res.json({ ok: true, matchId, playerId, connected });
  } catch (e) {
    await client.query("ROLLBACK");
    return res.status(500).json({ error: e.message || "presence_update_failed" });
  } finally {
    client.release();
  }
});

app.post("/match/result", async (req, res) => {
  if (!requireInternalToken(req, res)) return;

  const matchId = normalizeLobbyText(req.body?.matchId, 64);
  const winnerPlayerId = normalizePlayerText(req.body?.winnerPlayerId, 64);
  const wasSurrendered = !!req.body?.wasSurrendered;
  const surrenderingPlayerId = normalizePlayerText(req.body?.surrenderingPlayerId, 64);
  const rawPlayers = Array.isArray(req.body?.players) ? req.body.players : [];

  if (!matchId) {
    return res.status(400).json({ error: "missing matchId" });
  }

  if (rawPlayers.length === 0) {
    return res.status(400).json({ error: "missing players" });
  }

  const players = [];
  const seenPlayerIds = new Set();
  for (const item of rawPlayers) {
    const playerId = normalizePlayerText(item?.playerId, 64);
    if (!validatePlayerId(playerId)) {
      return res.status(400).json({ error: "invalid playerId" });
    }
    if (seenPlayerIds.has(playerId)) {
      return res.status(400).json({ error: "duplicate playerId" });
    }
    seenPlayerIds.add(playerId);

    const playerSlotRaw = Number.parseInt(String(item?.playerSlot ?? "-1"), 10);
    const playerSlot = Number.isFinite(playerSlotRaw) ? playerSlotRaw : -1;
    const scoreRaw = Number.parseInt(String(item?.score ?? "0"), 10);
    const score = Number.isFinite(scoreRaw) ? scoreRaw : 0;
    players.push({ playerId, playerSlot, score });
  }

  if (winnerPlayerId && !seenPlayerIds.has(winnerPlayerId)) {
    return res.status(400).json({ error: "winner_not_in_players" });
  }

  if (wasSurrendered && surrenderingPlayerId && !seenPlayerIds.has(surrenderingPlayerId)) {
    return res.status(400).json({ error: "surrendering_player_not_in_players" });
  }

  const client = await pool.connect();
  try {
    await client.query("BEGIN");

    const existing = await getMatchResultById(matchId, client);
    if (existing) {
      await client.query("COMMIT");
      return res.json({
        ok: true,
        duplicate: true,
        result: mapMatchResultRow(existing)
      });
    }

    const inserted = await client.query(
      `INSERT INTO match_results(
         match_id,
         winner_player_id,
         was_surrendered,
         surrendering_player_id,
         completed_at,
         meta
       ) VALUES (
         $1,
         $2,
         $3,
         $4,
         NOW(),
         $5::jsonb
       )
       ON CONFLICT (match_id) DO NOTHING
       RETURNING match_id`,
      [
        matchId,
        winnerPlayerId || "",
        wasSurrendered,
        surrenderingPlayerId || "",
        JSON.stringify({})
      ]
    );

    if (inserted.rowCount === 0) {
      const duplicate = await getMatchResultById(matchId, client);
      await client.query("COMMIT");
      return res.json({
        ok: true,
        duplicate: true,
        result: duplicate ? mapMatchResultRow(duplicate) : null
      });
    }

    for (const player of players) {
      const result = getResultForPlayer(player.playerId, winnerPlayerId);
      await client.query(
        `INSERT INTO match_result_players(
           match_id,
           player_id,
           player_slot,
           score,
           result,
           created_at
         ) VALUES (
           $1,
           $2,
           $3,
           $4,
           $5,
           NOW()
         )`,
        [matchId, player.playerId, player.playerSlot, player.score, result]
      );

      await upsertPlayerStatsForResult(client, {
        matchId,
        playerId: player.playerId,
        score: player.score,
        result,
        wasSurrendered,
        surrenderingPlayerId: surrenderingPlayerId || ""
      });
    }

    const saved = await getMatchResultById(matchId, client);
    await client.query("COMMIT");
    return res.json({
      ok: true,
      duplicate: false,
      result: saved ? mapMatchResultRow(saved) : null
    });
  } catch (e) {
    await client.query("ROLLBACK");
    return res.status(500).json({ error: e.message || "result_write_failed" });
  } finally {
    client.release();
  }
});

app.get("/match/:id", async (req, res) => {
  const match = await getMatchById(req.params.id);
  if (!match || match.ended_at) return res.status(404).json({ error: "not found" });
  res.json(buildMatchPayload(match));
});

app.post("/match/end", async (req, res) => {
  if (!requireInternalToken(req, res)) return;

  const { matchId } = req.body || {};
  if (!matchId) return res.status(400).json({ error: "missing matchId" });

  const row = await getMatchById(matchId);
  if (!row) return res.json({ ok: true });

  await closeLobbyById(row.lobby_id, "match_end");
  await closeLobbyByMatchId(matchId, "match_end_match_id");
  await markMatchEnded(matchId, "match_end");
  await terminateMatch(matchId, "SIGTERM");
  setTimeout(() => {
    terminateMatch(matchId, "SIGKILL").catch(() => {});
  }, 5000);

  res.json({ ok: true });
});

app.get("/player/:playerId/last", async (req, res) => {
  const playerId = req.params.playerId;
  const result = await pool.query(
    `SELECT m.match_id
     FROM player_last_match pl
     JOIN matches m ON m.match_id = pl.match_id
     WHERE pl.player_id = $1 AND m.ended_at IS NULL
     LIMIT 1`,
    [playerId]
  );

  const matchId = result.rows[0]?.match_id;
  if (!matchId) return res.status(404).json({ error: "not found" });

  const row = await getMatchById(matchId);
  if (!row || row.ended_at) return res.status(404).json({ error: "not found" });
  res.json(buildMatchPayload(row));
});

app.post("/player/upsert", async (req, res) => {
  try {
    const ugsPlayerId = normalizePlayerText(req.body?.ugsPlayerId, 64);
    const username = normalizePlayerText(req.body?.username, 64);
    const displayName = normalizePlayerText(req.body?.displayName, 64);

    if (!validatePlayerId(ugsPlayerId)) {
      return res.status(400).json({ error: "invalid ugsPlayerId" });
    }

    if (!username) {
      return res.status(400).json({ error: "missing username" });
    }

    const upsert = await pool.query(
      `INSERT INTO players(ugs_player_id, username, display_name, created_at, last_seen_at)
       VALUES ($1, $2, $3, NOW(), NOW())
       ON CONFLICT (ugs_player_id)
       DO UPDATE SET
         username = EXCLUDED.username,
         display_name = CASE
           WHEN EXCLUDED.display_name = '' THEN players.display_name
           ELSE EXCLUDED.display_name
         END,
         last_seen_at = NOW()
       RETURNING *`,
      [ugsPlayerId, username, displayName]
    );

    return res.json(mapPlayerRow(upsert.rows[0]));
  } catch (e) {
    return res.status(500).json({ error: e.message || "player_upsert_failed" });
  }
});

app.get("/player/:playerId", async (req, res) => {
  const playerId = normalizePlayerText(req.params.playerId, 64);
  if (!validatePlayerId(playerId)) {
    return res.status(400).json({ error: "invalid playerId" });
  }

  const result = await pool.query(
    `SELECT * FROM players WHERE ugs_player_id = $1 LIMIT 1`,
    [playerId]
  );

  const row = result.rows[0];
  if (!row) {
    return res.status(404).json({ error: "not found" });
  }

  return res.json(mapPlayerRow(row));
});

app.get("/player/:playerId/profile", async (req, res) => {
  const playerId = normalizePlayerText(req.params.playerId, 64);
  if (!validatePlayerId(playerId)) {
    return res.status(400).json({ error: "invalid playerId" });
  }

  const playerResult = await pool.query(
    `SELECT * FROM players WHERE ugs_player_id = $1 LIMIT 1`,
    [playerId]
  );
  const statsResult = await pool.query(
    `SELECT * FROM player_stats WHERE player_id = $1 LIMIT 1`,
    [playerId]
  );

  const playerRow = playerResult.rows[0] || null;
  const statsRow = statsResult.rows[0] || null;
  if (!playerRow && !statsRow) {
    return res.status(404).json({ error: "not found" });
  }

  return res.json({
    player: playerRow
      ? mapPlayerRow(playerRow)
      : {
          ugsPlayerId: playerId,
          username: playerId,
          displayName: playerId,
          createdAt: null,
          lastSeenAt: null
        },
    stats: statsRow
      ? mapPlayerStatsRow(statsRow)
      : {
          playerId,
          gamesPlayed: 0,
          wins: 0,
          losses: 0,
          draws: 0,
          surrenders: 0,
          rankPoints: 0,
          scoreTotal: 0,
          lastResult: "",
          lastMatchId: "",
          lastMatchAt: null,
          updatedAt: null
        }
  });
});

app.get("/player/:playerId/matches", async (req, res) => {
  const playerId = normalizePlayerText(req.params.playerId, 64);
  if (!validatePlayerId(playerId)) {
    return res.status(400).json({ error: "invalid playerId" });
  }

  const limitRaw = Number.parseInt(String(req.query?.limit || "20"), 10);
  const limit = Math.max(1, Math.min(100, Number.isFinite(limitRaw) ? limitRaw : 20));
  const result = await pool.query(
    `SELECT
       mr.match_id,
       mr.completed_at,
       mr.was_surrendered,
       mr.winner_player_id,
       mr.surrendering_player_id,
       mrp.player_id,
       mrp.player_slot,
       mrp.score,
       mrp.result,
       COALESCE(
         (
           SELECT jsonb_agg(
                    jsonb_build_object(
                      'playerId', op.player_id,
                      'playerSlot', op.player_slot,
                      'score', op.score,
                      'result', op.result,
                      'displayName', COALESCE(NULLIF(pp.display_name, ''), COALESCE(pp.username, op.player_id)),
                      'username', COALESCE(pp.username, op.player_id)
                    )
                    ORDER BY op.player_slot ASC, op.player_id ASC
                  )
           FROM match_result_players op
           LEFT JOIN players pp ON pp.ugs_player_id = op.player_id
           WHERE op.match_id = mr.match_id
             AND op.player_id <> $1
         ),
         '[]'::jsonb
       ) AS opponents
     FROM match_result_players mrp
     JOIN match_results mr ON mr.match_id = mrp.match_id
     WHERE mrp.player_id = $1
     ORDER BY mr.completed_at DESC
     LIMIT $2`,
    [playerId, limit]
  );

  return res.json({
    playerId,
    results: result.rows.map(mapMatchHistoryRow)
  });
});

app.get("/leaderboard", async (req, res) => {
  const limitRaw = Number.parseInt(String(req.query?.limit || "50"), 10);
  const limit = Math.max(1, Math.min(200, Number.isFinite(limitRaw) ? limitRaw : 50));

  const result = await pool.query(
    `SELECT
       ps.player_id,
       p.username AS username,
       COALESCE(NULLIF(p.display_name, ''), p.username) AS display_name,
       ps.games_played,
       ps.wins,
       ps.losses,
       ps.draws,
       ps.surrenders,
       ps.rank_points,
       ps.score_total,
       ps.last_result,
       ps.last_match_id,
       ps.last_match_at,
       ps.updated_at
     FROM player_stats ps
     JOIN players p ON p.ugs_player_id = ps.player_id
     WHERE ps.games_played > 0
     ORDER BY ps.rank_points DESC, ps.wins DESC, ps.games_played DESC, ps.updated_at DESC
     LIMIT $1`,
    [limit]
  );

  return res.json({
    leaderboard: result.rows.map((row, index) => ({
      rank: index + 1,
      playerId: row.player_id,
      username: row.username,
      displayName: row.display_name,
      stats: mapPlayerStatsRow(row)
    }))
  });
});

app.get("/health", async (_req, res) => {
  const counts = await pool.query(
    `SELECT
       COUNT(*)::INT AS matches_total,
       COUNT(*) FILTER (WHERE ended_at IS NULL)::INT AS matches_active
     FROM matches`
  );
  const players = await pool.query(`SELECT COUNT(*)::INT AS players_with_last_match FROM player_last_match`);
  const profilePlayers = await pool.query(`SELECT COUNT(*)::INT AS players_total FROM players`);
  const resultCounts = await pool.query(
    `SELECT
       COUNT(*)::INT AS match_results_total,
       (COUNT(*) FILTER (WHERE was_surrendered = TRUE))::INT AS surrendered_total
     FROM match_results`
  );
  const statsCounts = await pool.query(`SELECT COUNT(*)::INT AS player_stats_total FROM player_stats`);
  const lobbyCounts = await pool.query(
    `SELECT
       COUNT(*)::INT AS lobbies_total,
       COUNT(*) FILTER (WHERE closed_at IS NULL)::INT AS lobbies_active
     FROM lobbies`
  );
  const ports = await pool.query(
    `SELECT server_port
     FROM matches
     WHERE ended_at IS NULL
     ORDER BY server_port ASC`
  );

  res.json({
    ok: true,
    matches: counts.rows[0].matches_active,
    matchesTotal: counts.rows[0].matches_total,
    lobbies: lobbyCounts.rows[0].lobbies_active,
    lobbiesTotal: lobbyCounts.rows[0].lobbies_total,
    playersWithLastMatch: players.rows[0].players_with_last_match,
    playersTotal: profilePlayers.rows[0].players_total,
    matchResultsTotal: resultCounts.rows[0].match_results_total,
    surrenderedResultsTotal: resultCounts.rows[0].surrendered_total,
    playerStatsTotal: statsCounts.rows[0].player_stats_total,
    activePorts: ports.rows.map((r) => Number(r.server_port)),
    reservedPorts: reservedPorts.size,
    portRange: [PORT_MIN, PORT_MAX],
    releaseDefaults: {
      channel: RELEASE_DEFAULT_CHANNEL,
      platform: RELEASE_DEFAULT_PLATFORM
    }
  });
});

app.get("/release/latest", async (req, res) => {
  const channel = normalizeTrackValue(req.query.channel, RELEASE_DEFAULT_CHANNEL);
  const platform = normalizeTrackValue(req.query.platform, RELEASE_DEFAULT_PLATFORM);

  const result = await pool.query(
    `SELECT *
     FROM client_releases
     WHERE channel = $1
       AND platform = $2
       AND is_active = TRUE
     ORDER BY created_at DESC
     LIMIT 1`,
    [channel, platform]
  );

  const row = result.rows[0];
  if (!row) {
    return res.status(404).json({ error: "not found" });
  }

  return res.json(mapReleaseRow(row));
});

app.post("/release/publish", async (req, res) => {
  if (!requireAdminToken(req, res)) return;

  const channel = normalizeTrackValue(req.body?.channel, RELEASE_DEFAULT_CHANNEL);
  const platform = normalizeTrackValue(req.body?.platform, RELEASE_DEFAULT_PLATFORM);
  const version = String(req.body?.version || "").trim();
  const minSupportedVersion = String(req.body?.minSupportedVersion || "").trim();
  const downloadUrl = String(req.body?.downloadUrl || "").trim();
  const sha256 = String(req.body?.sha256 || "").trim().toLowerCase();
  const notesUrl = String(req.body?.notesUrl || "").trim();
  const sizeBytes = Number(req.body?.sizeBytes || 0);

  if (!validateVersion(version)) {
    return res.status(400).json({ error: "invalid version" });
  }

  if (!validateVersion(minSupportedVersion)) {
    return res.status(400).json({ error: "invalid minSupportedVersion" });
  }

  if (!downloadUrl || !/^https?:\/\//i.test(downloadUrl)) {
    return res.status(400).json({ error: "invalid downloadUrl" });
  }

  if (sha256 && !/^[a-f0-9]{64}$/.test(sha256)) {
    return res.status(400).json({ error: "invalid sha256" });
  }

  if (!Number.isFinite(sizeBytes) || sizeBytes < 0) {
    return res.status(400).json({ error: "invalid sizeBytes" });
  }

  await pool.query(
    `UPDATE client_releases
     SET is_active = FALSE
     WHERE channel = $1
       AND platform = $2
       AND is_active = TRUE`,
    [channel, platform]
  );

  const upsert = await pool.query(
    `INSERT INTO client_releases(
       channel, platform, version, min_supported_version,
       download_url, sha256, notes_url, size_bytes, created_at, is_active
     ) VALUES (
       $1, $2, $3, $4,
       $5, $6, $7, $8, NOW(), TRUE
     )
     ON CONFLICT(channel, platform, version)
     DO UPDATE SET
       min_supported_version = EXCLUDED.min_supported_version,
       download_url = EXCLUDED.download_url,
       sha256 = EXCLUDED.sha256,
       notes_url = EXCLUDED.notes_url,
       size_bytes = EXCLUDED.size_bytes,
       created_at = NOW(),
       is_active = TRUE
     RETURNING *`,
    [channel, platform, version, minSupportedVersion, downloadUrl, sha256, notesUrl, Math.floor(sizeBytes)]
  );

  return res.json(mapReleaseRow(upsert.rows[0]));
});

async function main() {
  await initDb();

  setInterval(() => {
    lifecycleSweep().catch((err) => {
      logEvent("error", "lifecycle_sweep_failed", { message: err.message || String(err) });
    });
  }, 15_000);

  setInterval(() => {
    lobbyLifecycleSweep().catch((err) => {
      logEvent("error", "lobby_lifecycle_sweep_failed", { message: err.message || String(err) });
    });
  }, 15_000);

  app.listen(PORT, () => {
    logEvent("info", "registry_started", {
      port: PORT,
      trustProxy: TRUST_PROXY,
      internalTokenConfigured: INTERNAL_API_TOKEN.length > 0,
      releaseAdminTokenConfigured: RELEASE_ADMIN_TOKEN.length > 0
    });
    if (!INTERNAL_API_TOKEN) {
      logEvent("warn", "internal_token_not_configured", {
        message: "Sensitive match endpoints are not protected."
      });
    }
  });
}

main().catch((err) => {
  logEvent("error", "registry_start_failed", { message: err.message || String(err) });
  process.exit(1);
});
