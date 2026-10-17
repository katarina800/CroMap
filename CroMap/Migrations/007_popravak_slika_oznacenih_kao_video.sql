-- ============================================================================
--  POPRAVAK: SLIKE ZAPISANE KAO VIDEO
-- ----------------------------------------------------------------------------
--  Gumb "Dodaj sadržaj" u profilu slao je datoteku bez polja MediaType, a
--  poslužitelj u tom slučaju uzima zadano "video". Svaka fotografija dodana
--  tim putem zapisana je dakle kao video: u profilu je dobivala ikonu
--  kamere i otvarala se u video playeru, koji s .jpg datotekom ne može
--  ništa — slika se naprosto nije vidjela.
--
--  Aplikacija sada šalje ispravan MediaType, a prikaz uz to gleda i nastavak
--  datoteke, pa se stare objave prikazuju ispravno i bez ove skripte. Ovime
--  se popravlja sam zapis u bazi, da i poslužitelj (obavijesti, sličice,
--  budući upiti) zna o čemu je riječ.
--
--  Skripta mijenja SAMO retke kojima datoteka očito jest slika. Smije se
--  pokrenuti više puta — drugi put ne mijenja ništa.
--
--  Pokretanje:
--      psql "<Render External Database URL>" -f popravak_slika_oznacenih_kao_video.sql
-- ============================================================================

BEGIN;

-- Prvo pogledaj što će se promijeniti (ne mijenja ništa):
--   SELECT id, title, media_type, file_path
--   FROM videos
--   WHERE media_type <> 'image'
--     AND split_part(file_path, '?', 1) ~* '\.(jpg|jpeg|png|gif|webp|heic|bmp)$';

UPDATE videos
SET media_type = 'image'
WHERE media_type IS DISTINCT FROM 'image'
  -- split_part odbacuje upitnik i sve iza njega, jer pohrana zna dodati
  -- parametre iza naziva datoteke.
  AND split_part(file_path, '?', 1) ~* '\.(jpg|jpeg|png|gif|webp|heic|bmp)$';

COMMIT;

-- ─── Provjera nakon pokretanja ──────────────────────────────────────────────
-- SELECT media_type, COUNT(*) FROM videos GROUP BY media_type;
-- Ne smije ostati nijedan red koji je .jpg a nije 'image':
-- SELECT COUNT(*) FROM videos
--  WHERE media_type <> 'image'
--    AND split_part(file_path, '?', 1) ~* '\.(jpg|jpeg|png|gif|webp|heic|bmp)$';
