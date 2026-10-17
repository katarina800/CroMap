-- ============================================================================
--  MIGRACIJA: RADIJUS ZA OBAVIJESTI I FEED
-- ----------------------------------------------------------------------------
--  Dosad je "Blizu mene" značilo fiksnih 50 km, a obavijesti su stizale samo
--  od ljudi koje korisnik prati. Zbog toga je odabir kategorije u postavkama
--  izgledao kao da ne radi: tko ne prati nikoga tko objavljuje planinarske
--  sadržaje, nije dobivao ništa iako je uredno odabrao "Planine".
--
--  Sada korisnik sam bira dokle seže "blizu" (do 100 km), a ista brojka
--  vrijedi i za obavijesti.
--
--  Skripta se smije pokrenuti više puta — drugi put ne mijenja ništa.
--
--  Pokretanje:
--      psql "<Render External Database URL>" -f migracija_radijus_obavijesti.sql
-- ============================================================================

BEGIN;

-- Dokle za ovog korisnika seže "blizu mene", u kilometrima.
-- 50 je dosadašnje ponašanje, pa postojeći korisnici ne osjete promjenu.
ALTER TABLE notification_preferences
    ADD COLUMN IF NOT EXISTS radius_km INTEGER NOT NULL DEFAULT 50;

-- Obavijesti se sad traže po kategoriji za SVE korisnike, ne samo za
-- pratitelje autora, pa je ovo glavni put pretraživanja.
CREATE INDEX IF NOT EXISTS idx_notif_prefs_app_enabled
    ON notification_preferences (user_id)
    WHERE app_enabled = TRUE;

-- Provjera blizine gleda krajeve korisnika.
CREATE INDEX IF NOT EXISTS idx_user_areas_user
    ON user_areas (user_id);

COMMIT;

-- ─── Provjera nakon pokretanja ──────────────────────────────────────────────
-- SELECT user_id, categories, radius_km, global_enabled
-- FROM notification_preferences LIMIT 5;
