-- ============================================================================
--  POPRAVAK: TABLICA notifications NEMA SVE STUPCE
-- ----------------------------------------------------------------------------
--  Tablica "notifications" postojala je u bazi PRIJE migracije obavijesti, u
--  drugom obliku. Migracija ju je stvarala s "CREATE TABLE IF NOT EXISTS", a
--  taj uvjet gleda SAMO ime tablice, ne i njezine stupce — pa je naredba tiho
--  preskočena i tablica je ostala stara.
--
--  Posljedica: svaki upis obavijesti puca na "column actor_user_id does not
--  exist". Taj je upis u poslužitelju omotan u try/catch (da neuspjela
--  obavijest ne sruši objavljivanje), pa se greška vidjela samo u logu, a u
--  aplikaciji je izgledalo kao da obavijesti naprosto ne stižu.
--
--  Ova skripta dodaje stupce koji nedostaju, bez diranja postojećih podataka.
--  Smije se pokrenuti više puta — drugi put ne mijenja ništa.
--
--  Pokretanje:
--      psql "<Render External Database URL>" -f popravak_tablice_obavijesti.sql
-- ============================================================================

-- Što tablica ima SADA (pokreni prvo, ne mijenja ništa):
--   SELECT column_name, data_type, is_nullable
--   FROM information_schema.columns
--   WHERE table_name = 'notifications'
--   ORDER BY ordinal_position;

BEGIN;

-- Tko je izazvao obavijest. Smije biti prazno (korisnik je u međuvremenu
-- obrisan), zato bez NOT NULL.
ALTER TABLE notifications
    ADD COLUMN IF NOT EXISTS actor_user_id INTEGER REFERENCES users(id) ON DELETE SET NULL;

-- Vrsta obavijesti. Postojeći redovi dobivaju zadanu vrijednost.
ALTER TABLE notifications
    ADD COLUMN IF NOT EXISTS type VARCHAR(40) NOT NULL DEFAULT 'new_activity';

ALTER TABLE notifications
    ADD COLUMN IF NOT EXISTS title VARCHAR(200) NOT NULL DEFAULT '';

ALTER TABLE notifications
    ADD COLUMN IF NOT EXISTS body TEXT NOT NULL DEFAULT '';

-- Kategorija zbog koje je obavijest poslana ("mountain", "cafe"…).
ALTER TABLE notifications
    ADD COLUMN IF NOT EXISTS category VARCHAR(40);

ALTER TABLE notifications
    ADD COLUMN IF NOT EXISTS video_id INTEGER REFERENCES videos(id) ON DELETE CASCADE;

ALTER TABLE notifications
    ADD COLUMN IF NOT EXISTS is_read BOOLEAN NOT NULL DEFAULT FALSE;

ALTER TABLE notifications
    ADD COLUMN IF NOT EXISTS created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP;

-- Indeksi iz izvorne migracije — i oni su preskočeni zajedno s tablicom.
CREATE INDEX IF NOT EXISTS idx_notifications_user_created
    ON notifications (user_id, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_notifications_user_unread
    ON notifications (user_id)
    WHERE is_read = FALSE;

COMMIT;

-- ─── Provjera nakon pokretanja ──────────────────────────────────────────────
-- Svih devet stupaca mora biti na popisu:
--   SELECT column_name FROM information_schema.columns
--   WHERE table_name = 'notifications' ORDER BY ordinal_position;
--
-- Nakon toga napravi NOVU objavu s drugog računa i provjeri:
--   SELECT id, user_id, actor_user_id, category, title, created_at
--   FROM notifications ORDER BY created_at DESC LIMIT 5;
