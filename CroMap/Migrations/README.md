# Migracije baze

Datoteke se izvode **redom po imenu**, pa svaka nova ide s većim brojem:
`010_opis.sql`, `011_opis.sql`, …

Pokreće ih aplikacija sama, pri dizanju (`DatabaseMigrator`, pozvan iz
`Program.cs`). Nema više lijepljenja u pgAdmin.

## Pravila

1. **Nikad ne mijenjaj migraciju koja je već primijenjena.** Pokretač pamti
   SHA-256 svake datoteke; izmjena već primijenjene migracije zaustavlja
   dizanje aplikacije s jasnom porukom. Napravi novu datoteku.
2. **`CREATE TABLE IF NOT EXISTS` ne jamči da tablica ima tražene stupce** —
   podudara se samo po imenu. Ako tablica možda već postoji u starijem
   obliku, dodaj i `ALTER TABLE … ADD COLUMN IF NOT EXISTS` za svaki stupac.
   Točno to je pustilo `notifications` bez `actor_user_id`: migracija je
   prošla bez greške, a svaki upis obavijesti je padao.
3. Svaka migracija se izvodi u vlastitoj transakciji. Ako padne, poništava se
   i aplikacija se **ne diže** — namjerno, da se ne radi nad polovičnom bazom.

## Polazište (baseline)

Prvih devet datoteka primijenjeno je ručno prije nego je ovaj pokretač
postojao. Kad pokretač prvi put naiđe na bazu koja već ima tablicu `users`, a
tablica `schema_migrations` je prazna, on zatečene migracije **samo zabilježi
kao primijenjene, bez izvođenja** (`polaziste = TRUE`).

To nije kozmetika: `007_popravak_slika_oznacenih_kao_video.sql` radi
`UPDATE videos` nad postojećim redcima, pa ponovno izvođenje ne bi bilo
bezopasno. Na praznoj bazi se, naravno, izvode sve redom.

## Što je gdje

    SELECT naziv, polaziste, primijenjeno
    FROM schema_migrations
    ORDER BY naziv;
