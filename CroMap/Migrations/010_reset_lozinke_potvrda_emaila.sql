-- ----------------------------------------------------------------------------
-- Reset lozinke, potvrda e-maila i odjava sa svih uređaja.
-- Sigurno za ponovno pokretanje (sve je IF NOT EXISTS / provjereno).
-- Pokreće ga DatabaseMigrator pri dizanju (u vlastitoj transakciji, pa ovdje nema BEGIN/COMMIT).
-- ----------------------------------------------------------------------------

-- ============================================================================
-- 1) Reset lozinke: kodovi se spremaju kao HMAC hash (64 znaka), ne kao
--    6 znamenki.
--    token je bio VARCHAR(10) UNIQUE: dva korisnika koji bi istovremeno dobili
--    isti 6-znamenkasti kod izazvali bi grešku pri INSERT-u pa kod nikome ne bi
--    stigao. Hash uključuje user_id, pa jedinstvenost više nije potrebna.
--    Postojeći kodovi su u čistom obliku i s novim se ne bi podudarali —
--    brišu se (vrijede najviše 1 sat, korisnik traži novi).
-- ============================================================================
DELETE FROM password_reset_tokens
WHERE length(token) BETWEEN 1 AND 10;  -- samo stari zapisi (čisti kod)

ALTER TABLE password_reset_tokens
    DROP CONSTRAINT IF EXISTS password_reset_tokens_token_key;

ALTER TABLE password_reset_tokens
    ALTER COLUMN token TYPE VARCHAR(64);

ALTER TABLE password_reset_tokens
    ADD COLUMN IF NOT EXISTS attempts INTEGER NOT NULL DEFAULT 0;

-- Jednokratna dozvola izdana nakon što poslužitelj provjeri kod (korak 2).
ALTER TABLE password_reset_tokens
    ADD COLUMN IF NOT EXISTS ticket VARCHAR(64);
ALTER TABLE password_reset_tokens
    ADD COLUMN IF NOT EXISTS ticket_expires_at TIMESTAMP;

-- Jedan aktivan kod po korisniku (potrebno za upsert)
CREATE UNIQUE INDEX IF NOT EXISTS password_reset_tokens_user_id_key
    ON password_reset_tokens (user_id);

-- ============================================================================
-- 2) Odjava sa svih uređaja: svaki JWT nosi token_version; reset lozinke ga
--    povećava pa stariji tokeni prestaju vrijediti. Postojeći tokeni (bez "tv")
--    vrijede kao verzija 0 — nitko nije odjavljen dok ne napravi reset.
-- ============================================================================
ALTER TABLE users
    ADD COLUMN IF NOT EXISTS token_version INTEGER NOT NULL DEFAULT 0;

-- ============================================================================
-- 3) Potvrda e-maila pri registraciji.
--    Svi POSTOJEĆI računi se označavaju kao potvrđeni (inače bi se nitko ne bi
--    mogao prijaviti). To se događa samo pri prvom dodavanju stupca, pa
--    ponovno pokretanje skripte ne "potvrđuje" kasnije nepotvrđene račune.
-- ============================================================================
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'users' AND column_name = 'email_verified'
    ) THEN
        ALTER TABLE users ADD COLUMN email_verified BOOLEAN NOT NULL DEFAULT FALSE;
        UPDATE users SET email_verified = TRUE;
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS email_verification_codes (
    user_id    INTEGER PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
    token      VARCHAR(64) NOT NULL,          -- HMAC hash koda
    expires_at TIMESTAMP   NOT NULL,
    created_at TIMESTAMP   NOT NULL DEFAULT NOW(),
    attempts   INTEGER     NOT NULL DEFAULT 0
);
