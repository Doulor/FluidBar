-- D1 schema for the FluidBar anonymous usage data collector.
-- Applied with: npx wrangler d1 execute fluidbar_telemetry --remote --file=./schema.sql

CREATE TABLE IF NOT EXISTS batches (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  received_at TEXT NOT NULL,
  install_hash TEXT NOT NULL,
  version TEXT NOT NULL,
  channel TEXT NOT NULL,
  self_contained INTEGER NOT NULL,
  os TEXT NOT NULL,
  arch TEXT NOT NULL,
  locale TEXT NOT NULL,
  run_count INTEGER NOT NULL,
  dropped INTEGER NOT NULL DEFAULT 0,
  payload TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS batches_received_at ON batches (received_at);
CREATE INDEX IF NOT EXISTS batches_install_hash ON batches (install_hash);
CREATE INDEX IF NOT EXISTS batches_version ON batches (version);
