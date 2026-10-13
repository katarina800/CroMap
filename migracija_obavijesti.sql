-- ============================================================================
--  MIGRACIJA: OBAVIJESTI O NOVIM AKTIVNOSTIMA
-- ----------------------------------------------------------------------------
--  Uvodi obavijesti unutar aplikacije (i po želji e-poštom) o novom sadržaju
--  koji objave korisnici koje pratite, filtrirane po kategorijama koje ste
--  sami odabrali u "Postavke obavijesti".
--
--  Dosad su te postavke živjele SAMO u memoriji telefona (AsyncStorage), pa
--  poslužitelj nije imao pojma koga što zanima, a brojač na zvonu bio je
--  trajno 0 jer ga ništa nije postavljalo.
--
--  Skripta je idempotentna (IF NOT EXISTS) — smije se pokrenuti više puta.
--
--  Pokretanje:
--      psql "<Render External Database URL>" -f migracija_obavijesti.sql
-- ============================================================================

BEGIN;

-- ─── Postavke obavijesti po korisniku ────────────────────────────────────────
-- categories je popis STABILNIH oznaka kategorija odvojenih zarezom
-- ("cafe,club,museum"), a ne prevedenih naziva — inače se ne bi mogle
-- usporediti s kategorijama objave korisnika koji piše na drugom jeziku.
CREATE TABLE IF NOT EXISTS notification_preferences (
    user_id       INTEGER PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
    app_enabled   BOOLEAN NOT NULL DEFAULT TRUE,
    email_enabled BOOLEAN NOT NULL DEFAULT FALSE,
    email         VARCHAR(254),
    categories    TEXT NOT NULL DEFAULT '',
    updated_at    TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- ─── Same obavijesti ─────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS notifications (
    id            SERIAL PRIMARY KEY,
    user_id       INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    actor_user_id INTEGER REFERENCES users(id) ON DELETE SET NULL,
    type          VARCHAR(40) NOT NULL,
    title         VARCHAR(200) NOT NULL,
    body          TEXT NOT NULL DEFAULT '',
    category      VARCHAR(40),
    video_id      INTEGER REFERENCES videos(id) ON DELETE CASCADE,
    is_read       BOOLEAN NOT NULL DEFAULT FALSE,
    created_at    TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_notifications_user_created
    ON notifications(user_id, created_at DESC);
-- Djelomični indeks: brojač nepročitanih je najčešći upit u aplikaciji.
CREATE INDEX IF NOT EXISTS idx_notifications_user_unread
    ON notifications(user_id) WHERE is_read = FALSE;

-- ─── Kategorije objave, strojno čitljive ─────────────────────────────────────
-- Dosad su kategorije postojale samo kao PREVEDENI tekst unutar opisa
-- ("📂 Kategorije: Parkovi, Planine"), pa ih poslužitelj nije mogao usporediti
-- ni s čim. Sada se uz opis sprema i popis stabilnih oznaka ("park,mountain").
ALTER TABLE videos ADD COLUMN IF NOT EXISTS categories TEXT NOT NULL DEFAULT '';
ALTER TABLE videos ADD COLUMN IF NOT EXISTS age_groups TEXT NOT NULL DEFAULT '';

COMMIT;

-- ─── Provjera nakon pokretanja ──────────────────────────────────────────────
-- SELECT COUNT(*) FROM notifications;
-- SELECT COUNT(*) FROM notification_preferences;
-- SELECT categories, age_groups FROM videos LIMIT 1;

-- 1. Did the migration actually land on THIS database?
SELECT column_name FROM information_schema.columns
WHERE table_name = 'notification_preferences' AND column_name = 'radius_km';

-- 2. What are mia's saved preferences?
SELECT u.username, p.app_enabled, p.categories, p.global_enabled, p.radius_km
FROM notification_preferences p JOIN users u ON u.id = p.user_id;

-- 3. What category did lili's post actually store?
SELECT id, user_id, title, categories, is_event, latitude, longitude, created_at
FROM videos ORDER BY created_at DESC LIMIT 3;

-- 4. Were any notifications created at all?
SELECT id, user_id, actor_user_id, category, title, created_at
FROM notifications ORDER BY created_at DESC LIMIT 5;


BEGIN;

ALTER TABLE notifications ADD COLUMN IF NOT EXISTS actor_user_id INTEGER REFERENCES users(id) ON DELETE SET NULL;
ALTER TABLE notifications ADD COLUMN IF NOT EXISTS type VARCHAR(40) NOT NULL DEFAULT 'new_activity';
ALTER TABLE notifications ADD COLUMN IF NOT EXISTS title VARCHAR(200) NOT NULL DEFAULT '';
ALTER TABLE notifications ADD COLUMN IF NOT EXISTS body TEXT NOT NULL DEFAULT '';
ALTER TABLE notifications ADD COLUMN IF NOT EXISTS category VARCHAR(40);
ALTER TABLE notifications ADD COLUMN IF NOT EXISTS video_id INTEGER REFERENCES videos(id) ON DELETE CASCADE;
ALTER TABLE notifications ADD COLUMN IF NOT EXISTS is_read BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE notifications ADD COLUMN IF NOT EXISTS created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP;

CREATE INDEX IF NOT EXISTS idx_notifications_user_created ON notifications (user_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_notifications_user_unread ON notifications (user_id) WHERE is_read = FALSE;

COMMIT;


SELECT column_name, data_type FROM information_schema.columns
WHERE table_name = 'notifications' ORDER BY ordinal_position;