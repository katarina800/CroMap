-- ----------------------------------------------------------------------------
-- Reset lozinke: kodovi se spremaju kao HMAC hash (64 znaka), ne kao 6 znamenki.
--
-- 1) token je bio VARCHAR(10) UNIQUE: dva korisnika koji bi istovremeno dobili
--    isti 6-znamenkasti kod izazvali bi grešku pri INSERT-u, pa kod nikome ne
--    bi stigao (zahtjev završi s 500 prije slanja e-maila). Hash uključuje
--    user_id, pa jedinstvenost više nije potrebna.
-- 2) Postojeći kodovi su u čistom obliku i s novim kodom se ne bi podudarali —
--    brišu se (vrijede najviše 1 sat, korisnik traži novi).
-- Pokrenuti PRIJE deploya nove verzije API-ja.
-- ----------------------------------------------------------------------------
BEGIN;

DELETE FROM password_reset_tokens;

ALTER TABLE password_reset_tokens
    DROP CONSTRAINT IF EXISTS password_reset_tokens_token_key;

ALTER TABLE password_reset_tokens
    ALTER COLUMN token TYPE VARCHAR(64);

ALTER TABLE password_reset_tokens
    ADD COLUMN IF NOT EXISTS attempts INTEGER NOT NULL DEFAULT 0;

-- Jedan aktivan kod po korisniku
CREATE UNIQUE INDEX IF NOT EXISTS password_reset_tokens_user_id_key
    ON password_reset_tokens (user_id);

COMMIT;
