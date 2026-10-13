using System.Text;
using System.Threading.RateLimiting;
using CroMap.Data;
using CroMap.Repositories;
using CroMap.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Kestrel's default request-body limit is ~28.6 MB (30_000_000 bytes) —
// far below what a real phone video needs. A 30-60s clip at typical
// camera bitrates is easily 60-150 MB, so uploads over that size were
// being rejected/reset before the request even reached the controller,
// showing up in the app as a generic "upload failed" error. Raised to
// 300 MB, matching the video/image size caps already enforced in code.
const long MaxUploadBytes = 300L * 1024 * 1024;
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = MaxUploadBytes;
});
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = MaxUploadBytes;
});

builder.Configuration["R2:AccessKeyId"] = Environment.GetEnvironmentVariable("R2_ACCESS_KEY_ID");
builder.Configuration["R2:SecretAccessKey"] = Environment.GetEnvironmentVariable("R2_SECRET_ACCESS_KEY");
builder.Configuration["R2:Endpoint"] = Environment.GetEnvironmentVariable("R2_ENDPOINT");
builder.Configuration["R2:BucketName"] = Environment.GetEnvironmentVariable("R2_BUCKET_NAME");
builder.Configuration["R2:PublicUrl"] = Environment.GetEnvironmentVariable("R2_PUBLIC_URL");

Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

// JWT Authentication
//
// Ključ za potpisivanje tokena MORA doći iz okoline (Jwt__Key na Renderu ili
// user-secrets lokalno). Dosad je u appsettings.json stajala konkretna
// vrijednost, a ta je datoteka u gitu — tko god vidi repozitorij mogao je
// sam potpisati valjan token za bilo koji korisnički id, uključujući admina,
// i time preuzeti svaki račun. Ako ključ nije postavljen ili je prekratak,
// aplikacija se namjerno NE pokreće: tiho vraćanje na ugrađenu vrijednost je
// gore od pada, jer izgleda kao da sve radi.
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException(
        "Jwt:Key nije postavljen ili je kraći od 32 bajta. Postavi varijablu " +
        "okoline Jwt__Key (HS256 zahtijeva ključ od barem 256 bita).");
}
var key = Encoding.UTF8.GetBytes(jwtKey);

// Isto vrijedi za bazu: bez connection stringa aplikacija ne može ništa, pa je
// bolje da padne pri pokretanju (vidljivo u logu deploya) nego da svaki zahtjev
// puca na prvom pristupu bazi.
if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("DefaultConnection")))
{
    throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection nije postavljen. Postavi varijablu " +
        "okoline ConnectionStrings__DefaultConnection.");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        // Potpis i rok ne bi odbili token izdan prije reseta lozinke. Zato se uz
        // svaki zahtjev provjerava da verzija sesije u tokenu ("tv") još odgovara
        // onoj u bazi — reset lozinke je povećava i time odjavljuje sve uređaje.
        // Token bez "tv" (izdan prije ove izmjene) vrijedi kao verzija 0.
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async ctx =>
            {
                var idClaim = ctx.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (!int.TryParse(idClaim, out var userId))
                {
                    ctx.Fail("Neispravan token");
                    return;
                }

                var tokenVersion = int.TryParse(ctx.Principal!.FindFirst("tv")?.Value, out var tv) ? tv : 0;
                var current = await ctx.HttpContext.RequestServices
                    .GetRequiredService<TokenVersionService>().GetAsync(userId);

                // current == null: račun je obrisan.
                if (current == null || current.Value != tokenVersion)
                    ctx.Fail("Sesija je poništena");
            }
        };
    });

// Services
builder.Services.AddSingleton<DatabaseConnection>();
builder.Services.AddScoped<UserRepository>();
builder.Services.AddScoped<IVideoRepository, VideoRepository>();
builder.Services.AddScoped<IMessageRepository, MessageRepository>();
builder.Services.AddScoped<IWishlistRepository, WishlistRepository>();
builder.Services.AddScoped<IProfileRepository, ProfileRepository>();
builder.Services.AddScoped<IStoryRepository, StoryRepository>();
builder.Services.AddScoped<IFollowRepository, FollowRepository>();
builder.Services.AddScoped<ISavedVideoRepository, SavedVideoRepository>();
builder.Services.AddScoped<IWishlistRepository, WishlistRepository>();
builder.Services.AddScoped<IGoldenFriendRepository, GoldenFriendRepository>();
builder.Services.AddScoped<IBlockRepository, BlockRepository>();
builder.Services.AddScoped<IActivityRepository, ActivityRepository>();
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddScoped<IUserAreaRepository, UserAreaRepository>();
builder.Services.AddScoped<MediaRepository>();
builder.Services.AddScoped<AdminRepository>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IEmailServiceWithInlineImages, EmailService>();
builder.Services.AddScoped<PasswordResetRepository>();
builder.Services.AddScoped<EmailVerificationRepository>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<TokenVersionService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();  // ← Bez security definicije

// CORS — u produkciji ograničeno na stvarne domene aplikacije (postavi
// AllowedOrigins u konfiguraciji/env varijabli, npr. "https://vara.app,
// https://www.vara.app"). Mobilna aplikacija (Bearer token, ne kolačići)
// CORS uopće ne provjerava, ovo štiti isključivo web/browser klijente.
var allowedOrigins = (builder.Configuration["AllowedOrigins"] ?? "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (builder.Environment.IsDevelopment() || allowedOrigins.Length == 0)
        {
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        }
        else
        {
            policy.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader();
        }
    });
});

// Rate limiting — glavna obrana od automatiziranih botova koji bi inače
// mogli neograničeno brzo pokušavati registraciju/login (brute force,
// masovno kreiranje lažnih računa). Prije ovoga nije postojalo NIKAKVO
// ograničenje broja zahtjeva.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Opći limit za sve rute, po IP adresi. Podignuto s 300 na 900/min —
    // 300 se pokazalo prenisko: sam popis razgovora (getConversations) radi
    // zaseban fetch avatara i poruka PO KONTAKTU, pa je korisnik s tridesetak
    // pratitelja/praćenih, uz osvježavanje popisa svakih par sekundi, sam
    // znao potrošiti većinu budžeta — pa su nasumične druge akcije (follow,
    // slanje poruke) dobivale 429 "previše zahtjeva", što se u appu vidjelo
    // kao slučajan "failed to send". Mali QueueLimit dodatno ublažava
    // kratke naglе udare (npr. kad se ekran tek otvori i odjednom povuče
    // više resursa) tako da par zahtjeva pričeka umjesto da odmah padne.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 900,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 10,
            }));

    // Stroži limit specifično za registraciju i prijavu — najčešća meta
    // botova (masovna registracija, brute-force lozinki)
    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 8,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

builder.Services.AddHttpClient();

builder.Services.AddScoped<CroMap.Services.IR2StorageService, CroMap.Services.R2StorageService>();



var app = builder.Build();


var wwwrootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
var avatarsPath = Path.Combine(wwwrootPath, "avatars");
var videosPath = Path.Combine(wwwrootPath, "videos");
var storiesPath = Path.Combine(wwwrootPath, "stories");


if (!Directory.Exists(wwwrootPath))
    Directory.CreateDirectory(wwwrootPath);


if (!Directory.Exists(avatarsPath))
    Directory.CreateDirectory(avatarsPath);

if (!Directory.Exists(videosPath))
    Directory.CreateDirectory(videosPath);

if (!Directory.Exists(storiesPath))
    Directory.CreateDirectory(storiesPath);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Render (i slične platforme) stavljaju aplikaciju iza reverse proxyja —
// bez ovoga bi RemoteIpAddress uvijek bio proxyjev IP, pa bi rate limiter
// gore partitionirao SVE korisnike zajedno kao jednog "klijenta" umjesto
// svakog posebno po njegovoj stvarnoj IP adresi.
var forwardedHeaderOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
};
forwardedHeaderOptions.KnownNetworks.Clear();
forwardedHeaderOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeaderOptions);

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.UseStaticFiles();


app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
        Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "avatars")),
    RequestPath = "/avatars"
});

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
        Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "videos")),
    RequestPath = "/videos"
});


app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
        Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "stories")),
    RequestPath = "/stories"
});

app.MapControllers();

// Seed admin korisnika
using (var scope = app.Services.CreateScope())
{
    var adminRepo = scope.ServiceProvider.GetRequiredService<AdminRepository>();
    await adminRepo.SeedAdminUser();
}


app.Run("http://0.0.0.0:7089");