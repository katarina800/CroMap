-- ============================================================================
--  MIGRACIJA: DOGAĐAJI NA OBJAVAMA I PODRUČJA KORISNIKA
-- ----------------------------------------------------------------------------
--  Tri stvari koje idu zajedno jer sve tri trebaju iste stupce na objavi:
--
--  1) DOGAĐAJ — objava može biti najava (koncert u kafiću, izlet), s
--     datumom i vremenom početka. Ne pogađa se iz teksta: autor to sam
--     označi pri objavi, pa je podatak točan i nad njim se može raditi
--     ("počinje za 2 dana", podsjetnik dan ranije).
--
--  2) KOORDINATE OBJAVE — dosad je objava imala samo tekstualnu lokaciju
--     ("Virovitičko-podravska županija, 33523"), koja se ni s čim ne može
--     usporediti: "Čađavica" i "Virovitičko-podravska županija" su isto
--     mjesto, a kao tekst se ne podudaraju. S koordinatama se blizina
--     računa, a ne pogađa.
--
--  3) PODRUČJA KORISNIKA — gdje se korisnik zaista zadržava. Ne prati se
--     lokacija u pozadini (baterija, posebna dozvola na Play Storeu,
--     privatnost) nego se broje trenuci kad aplikaciju ionako koristi s
--     poznatom lokacijom. Točke se zaokružuju na ćeliju od ~11 km, pa se
--     ne pamti gdje je netko bio nego u kojem je kraju.
--
--  Skripta je idempotentna (IF NOT EXISTS) — smije se pokrenuti više puta.
--
--  Pokretanje:
--      psql "<Render External Database URL>" -f migracija_dogadaji_i_podrucja.sql
-- ============================================================================

BEGIN;

-- ─── 1) Događaji ─────────────────────────────────────────────────────────────
ALTER TABLE videos ADD COLUMN IF NOT EXISTS is_event BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE videos ADD COLUMN IF NOT EXISTS event_start_at TIMESTAMP;

-- Feed nadolazećih događaja traži "is_event i početak u budućnosti".
CREATE INDEX IF NOT EXISTS idx_videos_event_start
    ON videos(event_start_at) WHERE is_event = TRUE;

-- ─── 2) Koordinate objave ────────────────────────────────────────────────────
ALTER TABLE videos ADD COLUMN IF NOT EXISTS latitude  DOUBLE PRECISION;
ALTER TABLE videos ADD COLUMN IF NOT EXISTS longitude DOUBLE PRECISION;

-- Grubo sužavanje po pravokutniku prije računanja prave udaljenosti.
CREATE INDEX IF NOT EXISTS idx_videos_coords
    ON videos(latitude, longitude) WHERE latitude IS NOT NULL;

-- ─── 3) Područja korisnika ───────────────────────────────────────────────────
-- Jedan red = jedan kraj u kojem se korisnik pojavljuje. weight raste sa
-- svakim javljanjem; najveći weight je "kod kuće". Selidba se vidi sama od
-- sebe: novo područje raste, staro ostaje na starom broju i s vremenom
-- ispadne iz vrha.
CREATE TABLE IF NOT EXISTS user_areas (
    user_id    INTEGER          NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    -- Zaokruženo na 1 decimalu (~11 km) — kraj, ne točna lokacija.
    cell_lat   DOUBLE PRECISION NOT NULL,
    cell_lon   DOUBLE PRECISION NOT NULL,
    weight     INTEGER          NOT NULL DEFAULT 0,
    last_seen  TIMESTAMP        NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (user_id, cell_lat, cell_lon)
);

CREATE INDEX IF NOT EXISTS idx_user_areas_user_weight
    ON user_areas(user_id, weight DESC);

-- ─── Postavke: svjetske aktivnosti ───────────────────────────────────────────
-- Korisnik može isključiti sadržaj izvan svojih krajeva. Podrazumijevano je
-- uključeno da aplikacija na početku ne izgleda prazno.
ALTER TABLE notification_preferences
    ADD COLUMN IF NOT EXISTS global_enabled BOOLEAN NOT NULL DEFAULT TRUE;

COMMIT;

-- ─── Provjera nakon pokretanja ──────────────────────────────────────────────
-- SELECT id, is_event, event_start_at, latitude, longitude FROM videos LIMIT 5;
-- SELECT COUNT(*) FROM user_areas;
-- SELECT global_enabled FROM notification_preferences LIMIT 1;
