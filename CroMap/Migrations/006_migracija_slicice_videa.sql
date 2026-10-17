-- ============================================================================
--  MIGRACIJA: SLIČICE (THUMBNAILS) ZA VIDEO OBJAVE
-- ----------------------------------------------------------------------------
--  U profilu ("Moje" i "Spremljeno") video se dosad prikazivao samo kao ikona
--  kamere — nije se vidjelo što je na njemu. Slike su imale sličicu jer je
--  sama datoteka slika; videi nisu imali ništa jer sličica nigdje nije
--  postojala.
--
--  Sličicu sada radi telefon pri objavi (prvi kadar videa) i šalje je uz
--  video, a ovdje se pamti gdje je spremljena. Stupac je prazan za sve
--  postojeće videe — oni i dalje prikazuju ikonu kamere, dok se ne objave
--  ponovno.
--
--  Skripta je idempotentna (IF NOT EXISTS) — smije se pokrenuti više puta.
--
--  Pokretanje:
--      psql "<Render External Database URL>" -f migracija_slicice_videa.sql
-- ============================================================================

BEGIN;

ALTER TABLE videos ADD COLUMN IF NOT EXISTS thumbnail_path TEXT NOT NULL DEFAULT '';

COMMIT;

-- ─── Provjera nakon pokretanja ──────────────────────────────────────────────
-- SELECT id, media_type, thumbnail_path FROM videos ORDER BY id DESC LIMIT 5;
