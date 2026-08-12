using System.Net;
using System.Text;
using BookStore.Api.Common;
using BookStore.Application;
using BookStore.Application.Common.Interfaces;
using BookStore.Application.Common.Security;
using BookStore.Core.Domain.Users;
using BookStore.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using BookStore.Infrastructure.Authentication;
using BookStore.Infrastructure.Persistence;
using BookStore.Infrastructure.Storage;
using Mapster;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// IIS-hosted apps do NOT run with the site folder as the working directory (in-process
// hosting runs inside w3wp.exe), so relative paths like "Data Source=bookstore.db" or
// "wwwroot/uploads" would resolve against an unexpected directory and break the app or
// the file storage. Anchor both to the content root (the deployed site folder) up front.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (!string.IsNullOrWhiteSpace(connectionString))
{
    var sqliteConnection = new SqliteConnectionStringBuilder(connectionString);
    var dataSource = sqliteConnection.DataSource;
    if (!string.IsNullOrWhiteSpace(dataSource)
        && !Path.IsPathRooted(dataSource)
        && !dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
    {
        sqliteConnection.DataSource = Path.GetFullPath(dataSource, builder.Environment.ContentRootPath);
        builder.Configuration["ConnectionStrings:DefaultConnection"] = sqliteConnection.ToString();
    }
}

var fileStorageRoot = builder.Configuration[$"{LocalFileStorageOptions.SectionName}:RootPath"];
if (!string.IsNullOrWhiteSpace(fileStorageRoot) && !Path.IsPathRooted(fileStorageRoot))
{
    builder.Configuration[$"{LocalFileStorageOptions.SectionName}:RootPath"] =
        Path.GetFullPath(fileStorageRoot, builder.Environment.ContentRootPath);
}

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Persist DataProtection keys to a folder in the site so auth cookies (incl. the OAuth
// correlation + external cookies and the JWT-less sign-in cookie) survive app-pool
// recycles. Shared hosts run the app pool without a user profile, so the default falls
// back to ephemeral in-memory keys that die with the process (EphemeralXmlRepository
// warning on the production host) — that breaks the Google round trip whenever IIS
// recycles between the challenge and the callback.
//
// Shared hosting (Plesk) sometimes grants the app pool WRITE but NOT DELETE, which makes
// the key repository's temp-file rotation throw UnauthorizedAccessException on the first
// challenge -> 500 on /api/auth/google-login. Probe BOTH create and delete at startup
// and fall back to ephemeral keys (with a clear log line) instead of taking down the
// endpoint. Fix on the host: grant the AppPool/Web-User identity 'Modify' on the folder.
var dataProtectionKeysPath = Path.Combine(builder.Environment.ContentRootPath, "keys");
var dataProtectionKeysWritable = false;
try
{
    Directory.CreateDirectory(dataProtectionKeysPath);
    var probe = Path.Combine(dataProtectionKeysPath, $".probe-{Guid.NewGuid():N}");
    File.WriteAllText(probe, "probe");
    File.Delete(probe); // delete is the operation Plesk often denies
    dataProtectionKeysWritable = true;
}
catch (Exception ex)
{
    Console.Error.WriteLine(
        $"[DataProtection] keys folder '{dataProtectionKeysPath}' is not fully writable " +
        $"(create/write OK, delete failed: {ex.Message}). Falling back to EPHEMERAL in-memory " +
        "keys — OAuth/auth cookies will NOT survive app-pool recycles. Grant the AppPool " +
        "identity 'Modify' on the folder (Plesk: File Manager → keys → Permissions → Web User).");
}

var dataProtectionBuilder = builder.Services.AddDataProtection().SetApplicationName("BookStore");
if (dataProtectionKeysWritable)
{
    dataProtectionBuilder.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
}

// Trust X-Forwarded-* headers from the reverse proxy (IIS ARR, nginx, ...) so that
// Request.Scheme/Request.Host reflect the public URL the browser sees — keeping the
// Google OAuth redirect_uri and password-reset links correct behind a TLS-terminating
// proxy without hard-coded base-URL overrides. Loopback is trusted by default; add
// remote proxy IPs/CIDR networks under "ForwardedHeaders" in configuration. Never
// clear the default trust: that would let any client spoof the forwarded headers.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedHost;

    foreach (var proxy in builder.Configuration
                 .GetSection("ForwardedHeaders:KnownProxies")
                 .Get<string[]>() ?? [])
    {
        if (IPAddress.TryParse(proxy, out var address))
        {
            options.KnownProxies.Add(address);
        }
    }

    foreach (var network in builder.Configuration
                 .GetSection("ForwardedHeaders:KnownNetworks")
                 .Get<string[]>() ?? [])
    {
        // Microsoft.AspNetCore.HttpOverrides.IPNetwork (not System.Net.IPNetwork) is the
        // type KnownNetworks expects; Parse handles "<address>/<prefixLength>" (IPv4 + IPv6).
        // Unparseable entries are skipped silently — check the config if headers are ignored.
        try
        {
            options.KnownNetworks.Add(Microsoft.AspNetCore.HttpOverrides.IPNetwork.Parse(network));
        }
        catch (FormatException)
        {
        }
    }
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddMapster();

var jwtSettings = builder.Configuration
    .GetSection(JwtSettings.SectionName)
    .Get<JwtSettings>()
    ?? throw new InvalidOperationException($"'{JwtSettings.SectionName}' configuration section is missing.");

var authenticationBuilder = builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
            RoleClaimType = "role"
        };
    });

// Optional Google OAuth login. Only registered when GoogleOAuth:ClientId/ClientSecret are
// configured; otherwise the feature is off and the UI hides the Google button (the
// api/auth/google-status endpoint reports the state to the client).
if (GoogleOAuthDefaults.IsConfigured(builder.Configuration))
{
    authenticationBuilder
        .AddCookie(GoogleOAuthDefaults.SignInScheme, options =>
        {
            options.Cookie.Name = "BookStore.GoogleExternal";
            // Lax keeps the temporary external cookie off cross-site POSTs while still
            // flowing through the Google redirect round trip (both are top-level navigations).
            options.Cookie.SameSite = SameSiteMode.Lax;
        })
        .AddGoogle(options =>
        {
            options.ClientId = builder.Configuration[$"{GoogleOAuthDefaults.SectionName}:ClientId"]!;
            options.ClientSecret = builder.Configuration[$"{GoogleOAuthDefaults.SectionName}:ClientSecret"]!;
            options.SignInScheme = GoogleOAuthDefaults.SignInScheme;
            options.CallbackPath = GoogleOAuthDefaults.CallbackPath;

            // The OAuth handler intercepts the callback path and validates the state
            // parameter itself. A missing/invalid state (direct hit, expired link) or a
            // Google "access_denied" (user cancelled) would otherwise throw -> 500; turn
            // every remote failure into a redirect the SPA can show as an error toast.
            // The underlying exception is logged so real failures (redirect_uri mismatch,
            // correlation/state cookie lost, token-exchange error, ...) are diagnosable.
            options.Events.OnRemoteFailure = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("GoogleOAuth");

                logger.LogError(context.Failure,
                    "Google OAuth remote failure. Query: {Query}",
                    context.Request.QueryString.HasValue
                        ? context.Request.QueryString.Value
                        : "(none)");

                context.Response.Redirect("/login?google_error=1");
                context.HandleResponse();
                return Task.CompletedTask;
            };
        });
}

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Policies.RequireAdminRole, policy => policy.RequireRole(Roles.Admin));
    // "Registered account" policy: any user role. Admins are also users (they can use the
    // library, download books, and call /me), so the policy admits both User and Admin.
    options.AddPolicy(Policies.RequireUserRole, policy => policy.RequireRole(Roles.User, Roles.Admin));
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<BookStoreDbContext>();
    dbContext.Database.Migrate();

    // Optional bootstrap admin (auto-seed on deploy): creates the account only when
    // SeedAdmin:Email + SeedAdmin:Password are configured AND no user has that email —
    // idempotent, never overwrites existing data, harmless on every publish. Disabled
    // unless configured (see appsettings.Production.json / SeedAdmin env vars).
    SeedAdminUser(scope.ServiceProvider);
}

// First middleware so every downstream component (https redirection, auth, URL building)
// sees the proxy-corrected scheme/host/client-IP.
app.UseForwardedHeaders();

app.UseHttpsRedirection();

app.UseBlazorFrameworkFiles();

app.UseAuthentication();

// Book files are served through the authenticated download endpoint; deny anonymous direct access.
app.UseWhen(context => context.Request.Path.StartsWithSegments("/uploads/books"), appBuilder =>
{
    appBuilder.Use(async (context, next) =>
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next();
    });
});

app.UseStaticFiles();

app.UseAuthorization();

app.MapControllers();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapFallbackToFile("index.html");

app.Run();

/// <summary>
/// Creates the configured bootstrap admin account on first start. Reads
/// <c>SeedAdmin:Email</c> and <c>SeedAdmin:Password</c> (Production appsettings / env
/// vars); skips entirely when not configured, and never touches an existing account
/// with the same email. Reuses the domain factory + production password hasher so the
/// row is identical to one created through the Register endpoint.
/// </summary>
static void SeedAdminUser(IServiceProvider services)
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var email = configuration["SeedAdmin:Email"]?.Trim();
    var password = configuration["SeedAdmin:Password"];
    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
    {
        return; // seeding disabled
    }

    var dbContext = services.GetRequiredService<BookStoreDbContext>();
    if (dbContext.Users.Any(u => u.Email == email))
    {
        return; // already exists — never clobber
    }

    var hasher = services.GetRequiredService<IPasswordHasher>();
    var created = User.Create(
        email,
        hasher.HashPassword(password),
        configuration["SeedAdmin:FirstName"] ?? "Admin",
        configuration["SeedAdmin:LastName"] ?? "System",
        UserRole.Admin);

    if (created.IsError)
    {
        services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("SeedAdmin")
            .LogError("SeedAdmin: could not create the admin user — {Errors}.",
                string.Join(", ", created.Errors.Select(e => e.Code)));
        return;
    }

    dbContext.Users.Add(created.Value);
    dbContext.SaveChanges();
}
