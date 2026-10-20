-- Push obavijesti: Expo push tokeni uređaja.
--
-- Jedan korisnik može imati više uređaja, a jedan token pripada samo jednom
-- korisniku (kad se na istom telefonu prijavi netko drugi, token prelazi na
-- njega — vidi upsert u PushTokenRepository). Redovi se brišu zajedno s
-- računom (ON DELETE CASCADE) i kad Expo javi da uređaj više ne postoji.
--
-- Pokreni jednom na produkcijskoj bazi PRIJE deploya nove verzije poslužitelja.

BEGIN;

CREATE TABLE IF NOT EXISTS push_tokens (
    id           SERIAL PRIMARY KEY,
    user_id      INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    token        TEXT NOT NULL UNIQUE,
    platform     VARCHAR(20),
    created_at   TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    last_seen_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_push_tokens_user ON push_tokens(user_id);

COMMIT;
