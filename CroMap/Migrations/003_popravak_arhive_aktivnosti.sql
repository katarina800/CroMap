-- ============================================================================
--  POPRAVAK ARHIVE AKTIVNOSTI (lajkovi i komentari pokazuju 0)
-- ----------------------------------------------------------------------------
--  Zašto je potrebno:
--    Brojači u tablici activity_logs pune se okidačima (triggerima) na
--    tablicama likes i comments. Jedna ranija migracija u baza.sql
--    ("čišćenje duplih trigera", redci 644-653) obrisala je
--    trigger_track_likes i trigger_track_comments, a natrag je stvorila samo
--    trigger_ensure_daily_activity_* — a oni SAMO osiguravaju da redak za
--    današnji dan postoji, ne povećavaju brojače. Od tada lajkovi i
--    komentari nikad ne dođu do activity_logs, pa arhiva pokazuje 0 iako
--    korisnik jest lajkao i komentirao. (Objave rade jer njihov okidač
--    nije obrisan — zato "Objave" pokazuje točan broj, a ostalo ne.)
--
--  Skripta je idempotentna: smije se pokrenuti više puta.
--  Pokreće se JEDNOM nad produkcijskom bazom, npr.:
--      psql "<Render External Database URL>" -f popravak_arhive_aktivnosti.sql
-- ============================================================================

BEGIN;

-- ─── 0. Sigurnosna provjera: ON CONFLICT (user_id, date) traži ovaj indeks ──
CREATE UNIQUE INDEX IF NOT EXISTS activity_logs_user_date_uidx
    ON activity_logs (user_id, date);

-- ─── 1. Vrati funkcije koje broje lajkove i komentare ───────────────────────
CREATE OR REPLACE FUNCTION track_like_activity()
RETURNS TRIGGER AS $$
BEGIN
    IF TG_OP = 'INSERT' THEN
        INSERT INTO activity_logs (user_id, date, likes, comments, posts, session_minutes, followers_count)
        VALUES (NEW.user_id, CURRENT_DATE, 1, 0, 0, 0,
            (SELECT COUNT(*) FROM follows WHERE followed_id = NEW.user_id))
        ON CONFLICT (user_id, date)
        DO UPDATE SET likes = activity_logs.likes + 1;

    ELSIF TG_OP = 'DELETE' THEN
        -- GREATEST(...,0) da odjava lajka ne odvuče brojač u minus ako je
        -- lajk star (dodan prije nego je redak za taj dan uopće postojao).
        UPDATE activity_logs
        SET likes = GREATEST(likes - 1, 0)
        WHERE user_id = OLD.user_id AND date = CURRENT_DATE;
    END IF;

    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION track_comment_activity()
RETURNS TRIGGER AS $$
BEGIN
    IF TG_OP = 'INSERT' THEN
        INSERT INTO activity_logs (user_id, date, likes, comments, posts, session_minutes, followers_count)
        VALUES (NEW.user_id, CURRENT_DATE, 0, 1, 0, 0,
            (SELECT COUNT(*) FROM follows WHERE followed_id = NEW.user_id))
        ON CONFLICT (user_id, date)
        DO UPDATE SET comments = activity_logs.comments + 1;

    ELSIF TG_OP = 'DELETE' THEN
        UPDATE activity_logs
        SET comments = GREATEST(comments - 1, 0)
        WHERE user_id = OLD.user_id AND date = CURRENT_DATE;
    END IF;

    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

-- ─── 2. Vrati same okidače ──────────────────────────────────────────────────
DROP TRIGGER IF EXISTS trigger_track_likes ON likes;
CREATE TRIGGER trigger_track_likes
    AFTER INSERT OR DELETE ON likes
    FOR EACH ROW
    EXECUTE FUNCTION track_like_activity();

DROP TRIGGER IF EXISTS trigger_track_comments ON comments;
CREATE TRIGGER trigger_track_comments
    AFTER INSERT OR DELETE ON comments
    FOR EACH ROW
    EXECUTE FUNCTION track_comment_activity();

-- ─── 3. Nadoknadi ono što je propušteno dok okidača nije bilo ───────────────
--  Brojači se NE zbrajaju s postojećima nego se postavljaju na stvarno
--  stanje izbrojano iz izvornih tablica — tako je rezultat isti bez obzira
--  koliko je puta skripta pokrenuta i je li neki okidač u međuvremenu radio.
INSERT INTO activity_logs (user_id, date, likes, comments, posts, session_minutes, followers_count)
SELECT
    s.user_id,
    s.day,
    SUM(s.likes)::int,
    SUM(s.comments)::int,
    SUM(s.posts)::int,
    0,
    (SELECT COUNT(*) FROM follows f WHERE f.followed_id = s.user_id)::int
FROM (
    SELECT user_id, created_at::date AS day, COUNT(*) AS likes, 0 AS comments, 0 AS posts
    FROM likes GROUP BY 1, 2
    UNION ALL
    SELECT user_id, created_at::date, 0, COUNT(*), 0
    FROM comments GROUP BY 1, 2
    UNION ALL
    SELECT user_id, created_at::date, 0, 0, COUNT(*)
    FROM videos GROUP BY 1, 2
) s
GROUP BY s.user_id, s.day
ON CONFLICT (user_id, date) DO UPDATE
SET likes    = EXCLUDED.likes,
    comments = EXCLUDED.comments,
    posts    = EXCLUDED.posts;

COMMIT;

-- ─── Provjera nakon pokretanja (zamijeni <ID> svojim korisničkim ID-em) ─────
-- SELECT date, likes, comments, posts, session_minutes, followers_count
-- FROM activity_logs WHERE user_id = <ID> ORDER BY date DESC LIMIT 7;
