-- Additive migration: no changes to collected telemetry or its retention.
CREATE TABLE IF NOT EXISTS public_download_stats (
  id INTEGER PRIMARY KEY CHECK (id = 1),
  count INTEGER NOT NULL CHECK (count >= 0),
  updated_at TEXT NOT NULL
);
