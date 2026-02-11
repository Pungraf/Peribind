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

const pool = new Pool(buildPgConfig());

fs.mkdirSync(LOG_DIR, { recursive: true });

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

function buildMatchPayload(row) {
  return {
    matchId: row.match_id,
    lobbyId: row.lobby_id || "",
    serverIp: row.server_ip,
    serverPort: Number(row.server_port),
    players: Array.isArray(row.players) ? row.players : [],
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
}

async function getMatchById(matchId) {
  const result = await pool.query(`SELECT * FROM matches WHERE match_id = $1`, [matchId]);
  return result.rows[0] || null;
}

async function getActiveMatchByLobby(lobbyId) {
  const result = await pool.query(
    `SELECT * FROM matches WHERE lobby_id = $1 AND ended_at IS NULL LIMIT 1`,
    [lobbyId]
  );
  return result.rows[0] || null;
}

async function clearPlayerLastMapping(matchId) {
  await pool.query(`DELETE FROM player_last_match WHERE match_id = $1`, [matchId]);
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
  console.log(`[match] marked ended ${matchId} (${reason})`);
  return row;
}

async function cleanupMatch(matchId, reason = "cleanup") {
  const deleted = await pool.query(`DELETE FROM matches WHERE match_id = $1 RETURNING *`, [matchId]);
  const row = deleted.rows[0];
  if (!row) return;

  await clearPlayerLastMapping(matchId);
  reservedPorts.delete(Number(row.server_port));
  processes.delete(matchId);

  console.log(`[match] removed ${matchId} (${reason})`);
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

    // Port is now tracked in DB row; temp reservation no longer needed.
    reservedPorts.delete(serverPort);
    processes.set(matchId, child);

    child.on("error", async (err) => {
      console.log(`[match] process error ${matchId} pid=${child.pid}: ${err.message}`);
      await cleanupMatch(matchId, "process_error");
    });

    child.on("exit", async (code, signal) => {
      console.log(`[match] process exit ${matchId} pid=${child.pid} code=${code} signal=${signal}`);
      await cleanupMatch(matchId, "process_exit");
    });

    const row = insert.rows[0];
    console.log(`[match] created ${matchId} lobby=${lobbyId} pid=${child.pid} ${SERVER_PUBLIC_IP}:${serverPort}`);
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
      console.log(`[match] ttl reached ${matchId}, sending SIGTERM pid=${pid}`);
      await terminateMatch(matchId, "SIGTERM");
      setTimeout(() => {
        terminateMatch(matchId, "SIGKILL").catch(() => {});
      }, 5000);
      continue;
    }

    if (!terminating && endedAt > 0) {
      console.log(`[match] ended-but-alive ${matchId}, sending SIGTERM pid=${pid}`);
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

// ---- API ----
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
    return res.json({ ok: true });
  } catch (e) {
    return res.status(500).json({ error: e.message || "register_failed" });
  }
});

app.get("/match/:id", async (req, res) => {
  const match = await getMatchById(req.params.id);
  if (!match || match.ended_at) return res.status(404).json({ error: "not found" });
  res.json(buildMatchPayload(match));
});

app.post("/match/end", async (req, res) => {
  const { matchId } = req.body || {};
  if (!matchId) return res.status(400).json({ error: "missing matchId" });

  const row = await getMatchById(matchId);
  if (!row) return res.json({ ok: true });

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
    `SELECT m.*
     FROM player_last_match pl
     JOIN matches m ON m.match_id = pl.match_id
     WHERE pl.player_id = $1 AND m.ended_at IS NULL
     LIMIT 1`,
    [playerId]
  );

  const row = result.rows[0];
  if (!row) return res.status(404).json({ error: "not found" });
  res.json(buildMatchPayload(row));
});

app.get("/health", async (_req, res) => {
  const counts = await pool.query(
    `SELECT
       COUNT(*)::INT AS matches_total,
       COUNT(*) FILTER (WHERE ended_at IS NULL)::INT AS matches_active
     FROM matches`
  );
  const players = await pool.query(`SELECT COUNT(*)::INT AS players_with_last_match FROM player_last_match`);
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
    playersWithLastMatch: players.rows[0].players_with_last_match,
    activePorts: ports.rows.map((r) => Number(r.server_port)),
    reservedPorts: reservedPorts.size,
    portRange: [PORT_MIN, PORT_MAX]
  });
});

async function main() {
  await initDb();

  setInterval(() => {
    lifecycleSweep().catch((err) => {
      console.error("[lifecycle] sweep failed:", err.message || err);
    });
  }, 15_000);

  app.listen(PORT, () => {
    console.log(`Match allocator on ${PORT}`);
  });
}

main().catch((err) => {
  console.error("[startup] failed:", err.message || err);
  process.exit(1);
});
