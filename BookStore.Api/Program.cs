using System.Text;
using BookStore.Application;
using BookStore.Application.Common.Security;
using BookStore.Infrastructure;
using BookStore.Infrastructure.Authentication;
using BookStore.Infrastructure.Persistence;
using BookStore.Infrastructure.Storage;
using Mapster;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddMapster();

var jwtSettings = builder.Configuration
    .GetSection(JwtSettings.SectionName)
    .Get<JwtSettings>()
    ?? throw new InvalidOperationException($"'{JwtSettings.SectionName}' configuration section is missing.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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
}

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
