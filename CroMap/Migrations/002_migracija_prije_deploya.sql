-- ============================================================================
--  MIGRACIJA KOJU TREBA POKRENUTI PRIJE (ili zajedno s) DEPLOYEM BACKENDA
-- ----------------------------------------------------------------------------
--  Nova verzija backenda uvodi zahtjeve za praćenje privatnih profila: klik na
--  "Prati" kod privatnog korisnika više ne upisuje odmah u "follows" nego
--  stvara zahtjev u tablici follow_requests, koji ciljani korisnik prihvaća
--  ili odbija (FollowRepository: RequestFollowAsync / AcceptFollowRequestAsync
--  / DeclineFollowRequestAsync).
--
--  Ako se backend objavi bez ove tablice, praćenje privatnog profila i popis
--  zahtjeva vraćat će grešku 500 jer tablica ne postoji.
--
--  Skripta je idempotentna (sve je IF NOT EXISTS) — smije se pokrenuti više
--  puta i sigurno je pokrenuti je i prije samog deploya.
--
--  Pokretanje:
--      psql "<Render External Database URL>" -f migracija_prije_deploya.sql
-- ============================================================================

BEGIN;

CREATE TABLE IF NOT EXISTS follow_requests (
    id SERIAL PRIMARY KEY,
    requester_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    target_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(requester_id, target_id)
);

CREATE INDEX IF NOT EXISTS idx_follow_requests_requester_id
    ON follow_requests(requester_id);
CREATE INDEX IF NOT EXISTS idx_follow_requests_target_id
    ON follow_requests(target_id);

-- ----------------------------------------------------------------------------
--  Brojač pogrešnih pokušaja za kod za resetiranje lozinke.
--
--  Kod je šesteroznamenkast i dosad se provjeravao bez ikakvog ograničenja
--  broja pokušaja, pa se prostor od 900.000 kombinacija mogao pretraživati sve
--  dok se ne pogodi. Nakon 5 promašaja kod se sada poništava i mora se
--  zatražiti novi (PasswordResetRepository.ValidateTokenAsync).
-- ----------------------------------------------------------------------------
ALTER TABLE password_reset_tokens
    ADD COLUMN IF NOT EXISTS attempts INTEGER NOT NULL DEFAULT 0;

COMMIT;

-- ─── Provjera nakon pokretanja ──────────────────────────────────────────────
-- SELECT COUNT(*) FROM follow_requests;   -- mora vratiti broj (0), ne grešku
