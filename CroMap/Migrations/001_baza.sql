CREATE TABLE users (
    id SERIAL PRIMARY KEY,
    first_name VARCHAR(100) NOT NULL,
    last_name VARCHAR(100) NOT NULL,
    email VARCHAR(255) UNIQUE,
    phone VARCHAR(20) UNIQUE,
    password_hash TEXT NOT NULL,
    birth_date DATE NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

DELETE FROM users
WHERE id IN (2, 3, 4, 5, 6, 8, 10, 11, 12, 13);

ALTER TABLE users ADD COLUMN language VARCHAR(5) NOT NULL DEFAULT 'hr';
ALTER TABLE users ADD COLUMN IF NOT EXISTS language VARCHAR(5) DEFAULT 'hr';
select from  users;

-- Videos
CREATE TRIGGER trigger_track_video_activity
AFTER INSERT ON videos
FOR EACH ROW EXECUTE FUNCTION track_post_activity();

CREATE TRIGGER trigger_ensure_daily_activity_video
AFTER INSERT ON videos
FOR EACH ROW EXECUTE FUNCTION ensure_daily_activity_exists();

-- Stories
CREATE TRIGGER trigger_track_story_activity
AFTER INSERT ON stories
FOR EACH ROW EXECUTE FUNCTION track_post_activity();

CREATE TRIGGER trigger_ensure_daily_activity_story
AFTER INSERT ON stories
FOR EACH ROW EXECUTE FUNCTION ensure_daily_activity_exists();

-- Likes i comments (bonus)
CREATE TRIGGER trigger_ensure_daily_activity_likes
AFTER INSERT ON likes
FOR EACH ROW EXECUTE FUNCTION ensure_daily_activity_exists();

CREATE TRIGGER trigger_ensure_daily_activity_comments
AFTER INSERT ON comments
FOR EACH ROW EXECUTE FUNCTION ensure_daily_activity_exists();


SELECT email, password_hash FROM users LIMIT 5;
select from  users;
DELETE FROM users;

ALTER TABLE users
ADD CONSTRAINT email_or_phone_required
CHECK (email IS NOT NULL OR phone IS NOT NULL);

SELECT * FROM users;

SELECT * FROM videos;

SELECT * FROM likes;

select * from comments;

select * from messages;

select * from shares;

select * from wishlist_videos;

ALTER TABLE users ADD COLUMN username VARCHAR(20);

ALTER TABLE users ADD COLUMN is_admin BOOLEAN DEFAULT FALSE;

ALTER TABLE users
ALTER COLUMN username SET NOT NULL;

UPDATE users
SET username = 'lanavana'
WHERE id = 5;

CREATE TABLE videos (
    id SERIAL PRIMARY KEY,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    title VARCHAR(255) NOT NULL,
    location VARCHAR(255) NOT NULL,          -- OBAVEZNO: npr. 'Kafić Centar', 'Restoran Zona'
    additional_description TEXT,              -- OPCIONALNO: 'Odlična atmosfera, preporučam!'
    file_path TEXT NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE likes (
    id SERIAL PRIMARY KEY,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    video_id INTEGER NOT NULL REFERENCES videos(id) ON DELETE CASCADE,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(user_id, video_id)   -- sprečava duple lajkove
);

CREATE TABLE comments (
    id SERIAL PRIMARY KEY,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    video_id INTEGER NOT NULL REFERENCES videos(id) ON DELETE CASCADE,
    content TEXT NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE saved_videos (
    id SERIAL PRIMARY KEY,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    video_id INTEGER NOT NULL REFERENCES videos(id) ON DELETE CASCADE,
    saved_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(user_id, video_id)   -- jedan korisnik može spremiti isti video samo jednom
);

CREATE TABLE messages (
    id SERIAL PRIMARY KEY,
    sender_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    receiver_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    content TEXT NOT NULL,
    is_read BOOLEAN DEFAULT FALSE,
    sent_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

ALTER TABLE "messages" ADD COLUMN "MediaType" VARCHAR(10), ADD COLUMN "MediaUrl" VARCHAR(500);

CREATE TABLE shares (
    id SERIAL PRIMARY KEY,
    video_id INTEGER NOT NULL REFERENCES videos(id) ON DELETE CASCADE,
    shared_by_user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    shared_to_user_id INTEGER REFERENCES users(id) ON DELETE SET NULL, -- može biti NULL ako se dijeli izvan aplikacije
    shared_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE wishlist_videos (
    id SERIAL PRIMARY KEY,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    video_id INTEGER NOT NULL REFERENCES videos(id) ON DELETE CASCADE,
    added_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    notes TEXT,   -- npr. "Želim probati pizzu", "Super izgleda za izlazak"
    UNIQUE(user_id, video_id)
); 

UPDATE videos
SET 
    title = 'Novi naslov videa',
    location = 'Nova lokacija',
    additional_description = 'Ažurirani opis',
    file_path = 'https://www.w3schools.com/html/mov_bbb.mp4'
WHERE 
    id = 1;  -- ID videa koji želiš ažurirati



CREATE TABLE IF NOT EXISTS user_profiles (
    id SERIAL PRIMARY KEY,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    avatar TEXT,
    is_public BOOLEAN DEFAULT TRUE,
    screen_time_limit_minutes INTEGER,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(user_id)
);

ALTER TABLE user_profiles
ADD COLUMN show_username BOOLEAN DEFAULT TRUE;


CREATE TABLE IF NOT EXISTS follows (
    id SERIAL PRIMARY KEY,
    follower_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    followed_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(follower_id, followed_id)
);

-- Kreiraj stories tablicu
CREATE TABLE IF NOT EXISTS stories (
    id SERIAL PRIMARY KEY,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    media_url TEXT NOT NULL,
    media_type VARCHAR(20) NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    expires_at TIMESTAMP DEFAULT (CURRENT_TIMESTAMP + INTERVAL '24 hours')
);

CREATE TABLE IF NOT EXISTS story_views (
    id SERIAL PRIMARY KEY,
    story_id INTEGER NOT NULL REFERENCES stories(id) ON DELETE CASCADE,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    viewed_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(story_id, user_id)
);

-- Kreiraj golden_friends tablicu
CREATE TABLE IF NOT EXISTS golden_friends (
    id SERIAL PRIMARY KEY,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    friend_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(user_id, friend_id)
);

ALTER TABLE wishlist_videos ADD COLUMN IF NOT EXISTS is_going BOOLEAN;

-- Kreiraj indekse
CREATE INDEX IF NOT EXISTS idx_follows_follower_id ON follows(follower_id);
CREATE INDEX IF NOT EXISTS idx_follows_followed_id ON follows(followed_id);
CREATE INDEX IF NOT EXISTS idx_stories_user_id ON stories(user_id);
CREATE INDEX IF NOT EXISTS idx_stories_expires_at ON stories(expires_at);
CREATE INDEX IF NOT EXISTS idx_story_views_story_id ON story_views(story_id);
CREATE INDEX IF NOT EXISTS idx_golden_friends_user_id ON golden_friends(user_id);
CREATE INDEX IF NOT EXISTS idx_wishlist_videos_user_id ON wishlist_videos(user_id);
CREATE INDEX IF NOT EXISTS idx_saved_videos_user_id ON saved_videos(user_id);
CREATE INDEX IF NOT EXISTS idx_user_profiles_user_id ON user_profiles(user_id);

CREATE TABLE blocks (
    id SERIAL PRIMARY KEY,
    user_id INT NOT NULL,
    blocked_user_id INT NOT NULL,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),

    CONSTRAINT unique_block UNIQUE(user_id, blocked_user_id)
);

CREATE TABLE activity_logs
(
    id SERIAL PRIMARY KEY,
    user_id INT NOT NULL,
    date DATE NOT NULL,

    likes INT DEFAULT 0,
    comments INT DEFAULT 0,
    posts INT DEFAULT 0,

    session_minutes INT DEFAULT 0
);

-- Dodaj kolonu za praćenje pratitelja
ALTER TABLE activity_logs ADD COLUMN IF NOT EXISTS followers_count INT DEFAULT 0;

-- Dodaj unique constraint za sprječavanje duplikata po danu
ALTER TABLE activity_logs ADD CONSTRAINT unique_user_date UNIQUE (user_id, date);
-- Ažuriraj sve video zapise s novom IP adresom
UPDATE videos 
SET file_path = REPLACE(file_path, 'http://10.225.64.205:7089', 'http://10.236.214.205:7089')
WHERE file_path LIKE '%10.225.64.205%';

-- Ažuriraj sve video poruke koje imaju staru IP adresu
UPDATE messages 
SET content = REPLACE(content, 'http://10.225.64.205:7089', 'http://10.236.214.205:7089')
WHERE content LIKE '%__CROMAP_VIDEO__%';

-- Ako imaš story tablicu
UPDATE stories 
SET media_url = REPLACE(media_url, 'http://10.225.64.205:7089', 'http://10.236.214.205:7089')
WHERE media_url LIKE '%10.225.64.205%';


-- Ažuriraj avatar URL-ove u user_profiles tablici
UPDATE user_profiles 
SET avatar = REPLACE(avatar, 'http://10.225.64.205:7089', 'http://10.236.214.205:7089')
WHERE avatar LIKE '%10.225.64.205%';

SELECT * FROM activity_logs ORDER BY date DESC;

-- Funkcija koja osigurava da postoji dnevni unos za korisnika
CREATE OR REPLACE FUNCTION ensure_daily_activity_exists()
RETURNS TRIGGER AS $$
BEGIN
    -- Provjeri da li postoji unos za današnji dan
    IF NOT EXISTS (
        SELECT 1 FROM activity_logs 
        WHERE user_id = NEW.user_id AND date = CURRENT_DATE
    ) THEN
        -- Ako ne postoji, kreiraj novi unos
        INSERT INTO activity_logs (user_id, date, likes, comments, posts, session_minutes, followers_count)
        VALUES (
            NEW.user_id, 
            CURRENT_DATE, 
            0,  -- likes
            0,  -- comments  
            0,  -- posts
            0,  -- session_minutes
            (SELECT COUNT(*) FROM follows WHERE followed_id = NEW.user_id)  -- followers_count
        );
    END IF;
    
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;


-- Trigger koji se aktivira prije INSERT ili UPDATE
CREATE TRIGGER trigger_ensure_daily_activity
    BEFORE INSERT OR UPDATE ON activity_logs
    FOR EACH ROW
    EXECUTE FUNCTION ensure_daily_activity_exists();


-- Funkcija za ažuriranje followers_count kada se promijeni follow
CREATE OR REPLACE FUNCTION update_followers_count()
RETURNS TRIGGER AS $$
BEGIN
    IF TG_OP = 'INSERT' THEN
        -- Kada se doda novi pratitelj
        UPDATE activity_logs 
        SET followers_count = followers_count + 1
        WHERE user_id = NEW.followed_id AND date = CURRENT_DATE;
        
        -- Ako ne postoji unos za današnji dan, kreiraj ga
        IF NOT FOUND THEN
            INSERT INTO activity_logs (user_id, date, likes, comments, posts, session_minutes, followers_count)
            VALUES (NEW.followed_id, CURRENT_DATE, 0, 0, 0, 0, 1);
        END IF;
        
    ELSIF TG_OP = 'DELETE' THEN
        -- Kada se ukloni pratitelj
        UPDATE activity_logs 
        SET followers_count = followers_count - 1
        WHERE user_id = OLD.followed_id AND date = CURRENT_DATE;
    END IF;
    
    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

-- Trigger na follows tablici
CREATE TRIGGER trigger_update_followers_count
    AFTER INSERT OR DELETE ON follows
    FOR EACH ROW
    EXECUTE FUNCTION update_followers_count();


-- Funkcija za automatsko bilježenje lajkova
CREATE OR REPLACE FUNCTION track_like_activity()
RETURNS TRIGGER AS $$
BEGIN
    IF TG_OP = 'INSERT' THEN
        -- Kada se doda lajk
        INSERT INTO activity_logs (user_id, date, likes, comments, posts, session_minutes, followers_count)
        VALUES (NEW.user_id, CURRENT_DATE, 1, 0, 0, 0, 
            (SELECT COUNT(*) FROM follows WHERE followed_id = NEW.user_id))
        ON CONFLICT (user_id, date) 
        DO UPDATE SET likes = activity_logs.likes + 1;
        
    ELSIF TG_OP = 'DELETE' THEN
        -- Kada se ukloni lajk
        UPDATE activity_logs 
        SET likes = likes - 1
        WHERE user_id = OLD.user_id AND date = CURRENT_DATE;
    END IF;
    
    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

-- Trigger na likes tablici
CREATE TRIGGER trigger_track_likes
    AFTER INSERT OR DELETE ON likes
    FOR EACH ROW
    EXECUTE FUNCTION track_like_activity();


-- Funkcija za automatsko bilježenje komentara
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
        SET comments = comments - 1
        WHERE user_id = OLD.user_id AND date = CURRENT_DATE;
    END IF;
    
    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

-- Trigger na comments tablici
CREATE TRIGGER trigger_track_comments
    AFTER INSERT OR DELETE ON comments
    FOR EACH ROW
    EXECUTE FUNCTION track_comment_activity();


-- Funkcija za automatsko bilježenje objava
CREATE OR REPLACE FUNCTION track_post_activity()
RETURNS TRIGGER AS $$
BEGIN
    IF TG_OP = 'INSERT' THEN
        INSERT INTO activity_logs (user_id, date, likes, comments, posts, session_minutes, followers_count)
        VALUES (NEW.user_id, CURRENT_DATE, 0, 0, 1, 0, 
            (SELECT COUNT(*) FROM follows WHERE followed_id = NEW.user_id))
        ON CONFLICT (user_id, date) 
        DO UPDATE SET posts = activity_logs.posts + 1;
        
    ELSIF TG_OP = 'DELETE' THEN
        UPDATE activity_logs 
        SET posts = posts - 1
        WHERE user_id = OLD.user_id AND date = CURRENT_DATE;
    END IF;
    
    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

-- Trigger na videos tablici
CREATE TRIGGER trigger_track_posts
    AFTER INSERT OR DELETE ON videos
    FOR EACH ROW
    EXECUTE FUNCTION track_post_activity();

-- Provjeri da su svi dodani triggeri uklonjeni (trebali bi ostati samo RI_ConstraintTrigger)
SELECT 
    tgname AS trigger_name,
    tgrelid::regclass AS table_name
FROM pg_trigger
WHERE tgname NOT LIKE 'RI_ConstraintTrigger%'
ORDER BY tgname;


ALTER TABLE videos ADD COLUMN media_type VARCHAR(20) DEFAULT 'video';

-- Provjeri postoji li kolona
SELECT column_name, data_type 
FROM information_schema.columns 
WHERE table_name = 'videos' AND column_name = 'media_type';

-- Ako ne postoji, dodaj je
ALTER TABLE videos ADD COLUMN media_type VARCHAR(20) DEFAULT 'video';

-- Ažuriraj postojeće redove (slike su one u /images/ folderu)
UPDATE videos SET media_type = 'image' WHERE file_path LIKE '%/images/%';
UPDATE videos SET media_type = 'video' WHERE media_type IS NULL;


SELECT id, file_path, media_type FROM videos;

-- Postavi media_type na 'image' za sve zapise koji imaju 'images' u file_path
UPDATE videos 
SET media_type = 'image' 
WHERE file_path LIKE '%/images/%';

-- Provjeri rezultat
SELECT id, file_path, media_type FROM videos;


-- Tablica za story lajkove
CREATE TABLE IF NOT EXISTS story_likes (
    id SERIAL PRIMARY KEY,
    story_id INTEGER NOT NULL REFERENCES stories(id) ON DELETE CASCADE,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    reaction_type VARCHAR(50) DEFAULT 'like',
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(story_id, user_id)
);

-- Tablica za story komentare
CREATE TABLE IF NOT EXISTS story_comments (
    id SERIAL PRIMARY KEY,
    story_id INTEGER NOT NULL REFERENCES stories(id) ON DELETE CASCADE,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    text TEXT NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Tablica za reakcije na komentare
CREATE TABLE IF NOT EXISTS story_comment_reactions (
    id SERIAL PRIMARY KEY,
    comment_id INTEGER NOT NULL REFERENCES story_comments(id) ON DELETE CASCADE,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    reaction_type VARCHAR(50) NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(comment_id, user_id)
);

-- Tablica za preglede storyja (već postoji, ali dodajemo indekse)
CREATE INDEX IF NOT EXISTS idx_story_views_story_id ON story_views(story_id);
CREATE INDEX IF NOT EXISTS idx_story_views_user_id ON story_views(user_id);

-- Indeksi za nove tablice
CREATE INDEX IF NOT EXISTS idx_story_likes_story_id ON story_likes(story_id);
CREATE INDEX IF NOT EXISTS idx_story_comments_story_id ON story_comments(story_id);
CREATE INDEX IF NOT EXISTS idx_story_comment_reactions_comment_id ON story_comment_reactions(comment_id);

-- Automatsko brisanje isteklih storyjeva (opcionalno - PostgreSQL event trigger)
-- Može se koristiti cron job ili pg_cron ekstenzija


CREATE TABLE IF NOT EXISTS story_views (
    id SERIAL PRIMARY KEY,
    story_id INTEGER NOT NULL REFERENCES stories(id) ON DELETE CASCADE,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    viewed_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(story_id, user_id)
);

CREATE INDEX IF NOT EXISTS idx_story_views_story_id ON story_views(story_id);
CREATE INDEX IF NOT EXISTS idx_story_views_user_id ON story_views(user_id);


-- Provjeri sve story_views zapise
SELECT * FROM story_views;

-- Provjeri zapise za konkretan story (npr. id=7)
SELECT * FROM story_views WHERE story_id = 7;

-- Provjeri join s korisnicima
SELECT 
    u.id,
    u.first_name,
    u.last_name,
    sv.story_id,
    sv.viewed_at
FROM story_views sv
JOIN users u ON sv.user_id = u.id
WHERE sv.story_id = 7;


-- Ručno dodaj viewer za story 7 (korisnik 8 - Maja)
INSERT INTO story_views (story_id, user_id, viewed_at)
VALUES (7, 8, NOW())
ON CONFLICT (story_id, user_id) DO NOTHING;

-- Provjeri da li je dodano
SELECT * FROM story_views WHERE story_id = 7;


-- Provjeri constraint
SELECT constraint_name, constraint_type 
FROM information_schema.table_constraints 
WHERE table_name = 'story_views';

-- Ako treba, obriši i ponovno kreiraj
ALTER TABLE story_views DROP CONSTRAINT IF EXISTS story_views_story_id_user_id_key;
ALTER TABLE story_views ADD CONSTRAINT story_views_story_id_user_id_key UNIQUE(story_id, user_id);



-- Provjeri aktivne storyje
SELECT 
    id, 
    user_id, 
    expires_at, 
    NOW() as current_time,
    CASE WHEN expires_at > NOW() THEN 'Active' ELSE 'Expired' END as status
FROM stories 
WHERE user_id = 1  -- ili koji god korisnik
ORDER BY created_at DESC;



-- Provjeri da li korisnik 8 ima aktivni story
SELECT 
    id, 
    user_id, 
    expires_at, 
    NOW() as current_time,
    CASE WHEN expires_at > NOW() THEN 'Active' ELSE 'Expired' END as status
FROM stories 
WHERE user_id = 8;


-- Tabela za grupe
CREATE TABLE IF NOT EXISTS activity_groups (
    id TEXT PRIMARY KEY,
    creator_name TEXT NOT NULL,
    activity TEXT NOT NULL,
    description TEXT,
    latitude DOUBLE PRECISION NOT NULL,
    longitude DOUBLE PRECISION NOT NULL,
    location_name TEXT NOT NULL,
    max_people INTEGER NOT NULL,
    members TEXT,
    created_at TIMESTAMP NOT NULL,
    expires_at TIMESTAMP NOT NULL
);

-- Tabela za poruke
CREATE TABLE IF NOT EXISTS group_messages (
    id TEXT PRIMARY KEY,
    group_id TEXT NOT NULL,
    user_name TEXT NOT NULL,
    text TEXT NOT NULL,
    created_at TIMESTAMP NOT NULL,
    FOREIGN KEY (group_id) REFERENCES activity_groups(id) ON DELETE CASCADE
);

-- Indeksi za brže pretrage
CREATE INDEX IF NOT EXISTS idx_groups_expires_at ON activity_groups(expires_at);
CREATE INDEX IF NOT EXISTS idx_messages_group_id ON group_messages(group_id);

-- Pretvori members iz stringa u niz (ako treba)
UPDATE activity_groups SET members = '{}' WHERE members IS NULL;



CREATE TABLE IF NOT EXISTS password_reset_tokens (
    id SERIAL PRIMARY KEY,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    token VARCHAR(10) NOT NULL UNIQUE,
    expires_at TIMESTAMP NOT NULL,
    created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE TABLE plan_ratings (
    id SERIAL PRIMARY KEY,
    user_name VARCHAR(100) NOT NULL,
    destination VARCHAR(200) NOT NULL,
    rating INTEGER NOT NULL CHECK (rating BETWEEN 1 AND 5),
    created_at TIMESTAMP DEFAULT NOW()
);

ALTER TABLE activity_groups ADD COLUMN IF NOT EXISTS creator_user_id INTEGER;
ALTER TABLE group_messages ADD COLUMN IF NOT EXISTS user_id INTEGER;

CREATE TABLE support_reports (
    id SERIAL PRIMARY KEY,
    type VARCHAR(20) NOT NULL,
    message TEXT NOT NULL,
    user_name VARCHAR(100),
    username VARCHAR(100),
    created_at TIMESTAMP DEFAULT NOW()
);

UPDATE videos SET file_path = REPLACE(file_path, 'http://', 'https://');

SELECT id, file_path, media_type FROM videos ORDER BY created_at DESC LIMIT 5;

UPDATE videos 
SET media_type = 'image' 
WHERE file_path LIKE '%.jpeg' OR file_path LIKE '%.jpg' OR file_path LIKE '%.png';




-- Obriši sve duplikate i stare triggere
DROP TRIGGER IF EXISTS trigger_track_comments ON comments;
DROP TRIGGER IF EXISTS trigger_ensure_daily_activity_comments ON comments;
DROP TRIGGER IF EXISTS trigger_track_likes ON likes;
DROP TRIGGER IF EXISTS trigger_ensure_daily_activity_likes ON likes;
DROP TRIGGER IF EXISTS trigger_track_story_activity ON stories;
DROP TRIGGER IF EXISTS trigger_ensure_daily_activity_story ON stories;
DROP TRIGGER IF EXISTS trigger_track_video_activity ON videos;
DROP TRIGGER IF EXISTS trigger_ensure_daily_activity_video ON videos;
DROP TRIGGER IF EXISTS trigger_track_posts ON videos;
DROP TRIGGER IF EXISTS trigger_update_followers_count ON follows;

-- Postavi čisto, bez duplikata
CREATE TRIGGER trigger_track_video_activity
AFTER INSERT ON videos
FOR EACH ROW EXECUTE FUNCTION track_post_activity();

CREATE TRIGGER trigger_ensure_daily_activity_video
AFTER INSERT ON videos
FOR EACH ROW EXECUTE FUNCTION ensure_daily_activity_exists();

CREATE TRIGGER trigger_track_story_activity
AFTER INSERT ON stories
FOR EACH ROW EXECUTE FUNCTION track_post_activity();

CREATE TRIGGER trigger_ensure_daily_activity_story
AFTER INSERT ON stories
FOR EACH ROW EXECUTE FUNCTION ensure_daily_activity_exists();

CREATE TRIGGER trigger_ensure_daily_activity_likes
AFTER INSERT ON likes
FOR EACH ROW EXECUTE FUNCTION ensure_daily_activity_exists();

CREATE TRIGGER trigger_ensure_daily_activity_comments
AFTER INSERT ON comments
FOR EACH ROW EXECUTE FUNCTION ensure_daily_activity_exists();

CREATE TRIGGER trigger_update_followers_count
AFTER INSERT ON follows
FOR EACH ROW EXECUTE FUNCTION ensure_daily_activity_exists();


DROP TRIGGER IF EXISTS trigger_update_followers_count ON follows;

ALTER TABLE users ALTER COLUMN password_hash DROP NOT NULL;
ALTER TABLE users ADD COLUMN google_id VARCHAR(255) UNIQUE;
ALTER TABLE users ADD COLUMN auth_provider VARCHAR(20) DEFAULT 'local';

ALTER TABLE users ALTER COLUMN birth_date DROP NOT NULL;

-- ─── Follow requests (potvrda praćenja za privatne profile) ──────────────────
-- Kad je ciljani korisnik privatan (user_profiles.is_public = false), klik na
-- "Prati" više ne upisuje odmah u "follows" nego stvara zahtjev ovdje; tek
-- kad ciljani korisnik prihvati, red se briše odavde i pravi "follows" zapis
-- se kreira (vidi FollowRepository.AcceptFollowRequestAsync).
CREATE TABLE IF NOT EXISTS follow_requests (
    id SERIAL PRIMARY KEY,
    requester_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    target_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(requester_id, target_id)
);

CREATE INDEX IF NOT EXISTS idx_follow_requests_requester_id ON follow_requests(requester_id);
CREATE INDEX IF NOT EXISTS idx_follow_requests_target_id ON follow_requests(target_id);

-- ─── Popravak: "čišćenje triggera" gore (oko retka 643-685) je greškom
-- izbrisalo i pratitelje i lajkove/komentare iz activity_logs praćenja ──
-- Redoslijed gore je bio: DROP ispravnog trigger_update_followers_count →
-- ponovno stvoren ali POGREŠNO vezan na ensure_daily_activity_exists()
-- (koja samo osigurava da red postoji za danas, ne zbraja ništa) → pa čak
-- i TAJ pogrešan trigger ponovno obrisan — na kraju NIJEDAN trigger ne
-- ažurira activity_logs.followers_count kad netko zaprati/otprati. Isto
-- "čišćenje" je obrisalo trigger_track_likes i trigger_track_comments
-- (koji stvarno zbrajaju likes/comments) i vratilo samo njihove
-- "ensure_exists" parnjake, koji ne zbrajaju ništa. Rezultat: aktivnost
-- (dnevna/tjedna/mjesečna) je uvijek pokazivala 0 pratitelja, i lajkovi/
-- komentari se više uopće nisu brojali u activity_logs.
DROP TRIGGER IF EXISTS trigger_update_followers_count ON follows;
CREATE TRIGGER trigger_update_followers_count
    AFTER INSERT OR DELETE ON follows
    FOR EACH ROW
    EXECUTE FUNCTION update_followers_count();

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