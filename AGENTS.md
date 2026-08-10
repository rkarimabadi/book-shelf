# BookStore Solution - Development Guide

## Project Structure

```
BookStore/
├── BookStore.Api/              # ASP.NET Core Web API + host for the WASM UI (single endpoint serves both)
├── BookStore.Core/             # Domain Layer (DDD)
├── BookStore.Application/      # Application Layer
├── BookStore.Infrastructure/   # Infrastructure Layer
├── BookStore.Contracts/        # Shared DTOs (API + UI)
├── BookStore.UI/               # Blazor WebAssembly client (referenced by BookStore.Api)
└── BookStore.sln               # Solution file
```

## Architecture Overview

### Clean Architecture with DDD

The solution follows Clean Architecture principles with Domain-Driven Design:

- **Domain Layer (Core)**: Pure business logic, no external dependencies
- **Application Layer**: Use cases and orchestration
- **Infrastructure Layer**: Database, external services, file storage
- **API Layer**: RESTful endpoints, controllers

## Domain Layer Guidelines

### Core Principles

1. **Domain Purity**
   - No dependencies on databases, web frameworks, or ORMs
   - No Data Annotations (e.g., [Key], [Table], [Required])
   - Pure POCO models

2. **Rich Domain Model**
   - Avoid anemic models (only getters/setters)
   - Encapsulate state with private setters
   - Change state only through public business methods

3. **Result Pattern**
   - Use `ErrorOr<T>` for all operations that can fail
   - No business exceptions - use descriptive errors
   - Errors: `Error.Validation()`, `Error.NotFound()`, `Error.Conflict()`, etc.

4. **Domain Events**
   - Events stored in `_domainEvents` list
   - Never publish events from domain layer
   - Infrastructure handles event processing via Outbox pattern

5. **Aggregate Roots**
   - Only Aggregate Roots expose entity collections
   - Children managed by parent methods
   - Maintain transaction boundaries

## Key Domain Entities

### User (Aggregate Root)
- `User.cs` - Main user entity with authentication logic
- `UserRole.cs` - User roles (User, Admin)
- `RefreshToken.cs` - Token management
- `UserDomainEvents.cs` - All domain events

### Authentication Service
- `IAuthenticationService.cs` - Authentication contract
- `AuthenticationService.cs` - Authentication implementation

### Repositories
- `IUserRepository.cs` - User data access contract

### Book (Aggregate Root)
- `Book.cs` - Book entity with content management logic (Create/UpdateDetails/Delete)
- `BookDomainEvents.cs` - Book domain events (created, updated, deleted)
- `BookErrors.cs` - Book error catalog

### Repositories
- `IBookRepository.cs` - Book data access contract

### File Storage
- `IFileStorage.cs` (Application contract) - Save/Delete/GetFullPath for uploaded files
- `LocalFileStorage.cs` + `LocalFileStorageOptions` (Infrastructure) - writes to `wwwroot/uploads/` (subdirs `covers/`, `books/`), configured via the `FileStorage` section (`RootPath`/`BaseUrl`, defaults `wwwroot/uploads`/`/uploads`)
- `SaveAsync` returns web-relative paths (`uploads/books/<name>`, BaseUrl + subdir + unique name); `GetFullPath` strips the `BaseUrl` prefix before combining with `RootPath`
- Book delete/replace cleans up uploaded files (best-effort via `FileCleanup`, logs a warning on failure — never fails the request after the DB commit)


## Technology Stack

### Backend
- .NET 9
- C# 13 (SDK default - no `LangVersion` pin in csproj files)
- ASP.NET Core 9 Web API
- Entity Framework Core 9 (for Infrastructure)
- SQLite (development database)

### Frontend (hosted Blazor WebAssembly)
- `BookStore.UI` is a Blazor WebAssembly client, referenced by `BookStore.Api` (hosted model)
- One entry point / one origin: `BookStore.Api` serves both `/api/*` (controllers) and the WASM app at `/`
- `BookStore.Api/Program.cs`: `UseBlazorFrameworkFiles()` + `UseStaticFiles()` + `MapFallbackToFile("index.html")` (+ dev-only `UseWebAssemblyDebugging()`); requires `Microsoft.AspNetCore.Components.WebAssembly.Server` package
- Run with `dotnet run --project BookStore.Api` (or the `http`/`https` launch profiles) — the WASM client is served at the same URL as the API (http://localhost:5114 / https://localhost:7293; Swagger at `/swagger`)
- **Structure**: `Pages/` (Home, Login, Register), `Layout/` (MainLayout + scoped CSS, Persian RTL), `Services/` (`AuthenticationService` + `IAuthenticationService`, `AuthStateProvider`, `ClientStorageService` + `IClientStorageService`, `AuthenticatedHttpClientHandler`), `Features/Books/` (feature-driven: `Pages/`, `Components/`, `Services/`), `Shared/Components/` (LoadingSpinner, EmptyState, ErrorNotice), `wwwroot/` (index.html, css/app.css, bootstrap in lib/)
- **Auth flow**: login/register post to the API, then `AuthStateProvider.SignInAsync` persists `auth_token`/`refresh_token` in localStorage (`IJSRuntime`) and raises `NotifyAuthenticationStateChanged`. `AuthStateProvider.ParseToken` decodes the JWT payload (base64url) and builds `ClaimsPrincipal` (email/role/given_name/family_name); expired tokens → anonymous.
- `MainLayout` uses `<AuthorizeView>` (needs `AddAuthorizationCore` + `AddCascadingAuthenticationState` in `Program.cs`) to switch between "ورود/ثبتنام" links and the logged-in user + logout button.
- PWA capabilities (planned)

### Security
- Custom auth stack (no ASP.NET Core Identity): `User` aggregate + `PasswordHasher` (PBKDF2) + `JwtTokenGenerator` (JWT access tokens)
- Rotating refresh tokens (7-day expiry, revoked on refresh/logout) persisted in the `RefreshTokens` table
- HTTPS (production)

## Development Workflow

### Adding New Domain Entities

1. Create entity in `BookStore.Core/Domain/`
2. Inherit from `Entity` or `AggregateRoot`
3. Use `ErrorOr<T>` for factory methods
4. Implement business logic in methods
5. Add domain events for state changes
6. Use `Guard.Against` for input validation

### Adding Use Cases

1. Define command/query in `BookStore.Application/`
2. Implement handler with `ErrorOr<T>` return
3. Call domain services from handlers
4. No business logic in handlers

### Adding Infrastructure

1. Implement repository interfaces in `BookStore.Infrastructure/`
2. Use Entity Framework Core
3. Map domain entities to database entities
4. Handle migrations

### API Development

1. Create controllers in `BookStore.Api/Controllers/`
2. Use dependency injection
3. Return appropriate HTTP status codes
4. Validate input in controllers

## Testing Strategy

- No test projects in this solution (removed)
- Verify changes by building (`dotnet build`) and running the API smoke tests manually

## Common Patterns

### Creating Entities

```csharp
var result = User.Create(email, passwordHash, firstName, lastName);
if (result.IsError)
{
    return result.Errors;
}
var user = result.Value;
```

### Domain Events

```csharp
user.AddDomainEvent(new UserCreatedEvent(user.Id, user.Email, ...));
```

### Validation

```csharp
Guard.Against.NullOrEmpty(email, nameof(email));
Guard.Against.ExpiresInPast(expiresAt, nameof(expiresAt));
```

## Project-Specific Rules

1. Always use `ErrorOr<T>` for methods that can fail
2. Never throw exceptions for business logic failures
3. Domain layer has zero external dependencies
4. Use descriptive error codes and messages
5. All dates in UTC
6. Email addresses normalized to lowercase

## Application Layer Guidelines

### Structure & Patterns
- Feature-driven folders (vertical slicing), e.g. `Features/Authentication/Commands/Login/`
- One file per feature: Command/Query + Validator + Handler together
- Commands implement `IRequest<ErrorOr<T>>`; each has exactly one handler
- Handlers receive dependencies via constructor; no business logic in handlers

### Validation
- Validators use `AbstractValidator<T>` (FluentValidation), one per command/query
- Validation runs automatically via `ValidationBehavior<TRequest, TResponse>` (MediatR `IPipelineBehavior`); never validate inside handlers
- Pipeline behavior returns `Error.Validation(code: PropertyName, description: ErrorMessage)`

### Contracts (Dependency Inversion)
- Repository and domain-service contracts live in **Core**: `IUserRepository`, `IBookRepository`, `IAuthenticationService`
- External needs (tokens, clock, hashing, storage, persistence) are interfaces defined in `Application/Common/Interfaces/`:
  - `IDateTimeProvider`, `IJwtTokenGenerator`, `IPasswordHasher`, `IFileStorage`, `IUnitOfWork` (injected into every handler to persist changes after business logic)
- Implementations live in Infrastructure

### Security
- `Roles` and `Policies` static classes, both defined in `Common/Security/Roles.cs` (`Roles.Admin`/`Roles.User`; `Policies.RequireAdminRole`/`Policies.RequireUserRole`)
- Authorization is enforced via `[Authorize(Policy = ...)]` attributes on controllers (no auth pipeline behavior yet)

### Registration
- `DependencyInjection.AddApplication()` registers MediatR, FluentValidation validators, and `ValidationBehavior`
- Currently implemented: `Features/Authentication/` (Register, Login, RefreshToken, Logout commands), `Features/Books/` (Create/Update/Delete commands, Get/GetAll queries), `Features/Library/` (Add/Remove commands, GetUserLibrary query)

## Authentication Status (as of current session)

- **Domain (Core):** `User`, `RefreshToken`, `AuthenticationService` complete
  - `IAuthenticationService.RefreshToken` returns `(User User, string RefreshToken)` (rotates token and returns the new one)
  - Custom guard `GuardClauseExtensions.ExpiresInPast` in `Core/Domain/Common/`
- **Application:** Register/Login/RefreshToken/Logout commands with validators + handlers, `ValidationBehavior`, DI registration — complete and compiling
- **Infrastructure:** EF Core 9 + SQLite (`BookStoreDbContext`, Fluent API configurations), `UserRepository`, `BookRepository`, `UnitOfWork`, `JwtTokenGenerator`, `PasswordHasher` (PBKDF2), `DateTimeProvider`, `JwtSettings` (Options + validation), `LocalFileStorage`, Outbox pattern (`PublishDomainEventsInterceptor` + `ProcessOutboxMessagesJob` via Quartz.NET every 30 s, publishing through MediatR `DomainEventNotification`), `AddInfrastructure()` DI registration — complete and compiling
- **API:** AuthController (register/login/refresh/logout/me), JWT validation with `MapInboundClaims = false`, `ApiController` ProblemDetails base — complete and verified
- Refresh-token failures are uniformly `401` (Unauthorized): unknown token → `User.RefreshTokenNotFound`, plus expired/revoked variants; garbage token no longer returns 404
- **UI (Blazor WebAssembly):** `Login`/`Register` pages (Persian, client-side field validation, `returnUrl` support), `Home` page, `MainLayout` with `<AuthorizeView>`; `AuthenticationService` + `AuthStateProvider` (JWT parse from localStorage, sign-in/out, refresh-token rotation handshake) — complete and compiles; DI wiring verified (see pitfall 7)

## Book Content Management Status

- **Domain (Core):** `Book` aggregate complete (Create/UpdateDetails/Delete, domain events)
- **Application:** `Features/Books/` — CreateBook/UpdateBook/DeleteBook commands + validators + handlers, GetBook/GetBooks queries; Delete/Update handlers delete uploaded files (best-effort via `FileCleanup`)
- **Infrastructure:** `BookRepository`, `BookConfiguration`, `LocalFileStorage` (wwwroot/uploads, subdirs covers/books, `FileStorage` config section), `AddBooks` migration
- **API:** `BooksController` — GET public list/detail; POST/PUT/DELETE protected by `RequireAdminRole` policy; multipart upload (cover image + book file; Create rejects a missing book file with 400 `Book.FileRequired`)
- Verified by manual smoke test: CRUD + auth checks (403 for non-admin, 404 after delete), file cleanup on update/delete

## User Library Status

- **Domain (Core):** `LibraryEntry` entity + `User.AddToLibrary` / `User.RemoveFromLibrary` (guards: duplicate add → `Conflict`, missing book → `NotFound`), `BookAddedToLibraryEvent` / `BookRemovedFromLibraryEvent`
- **Application:** `Features/Library/` — AddBookToLibrary/RemoveBookFromLibrary commands, GetUserLibrary query
- **Infrastructure:** `LibraryEntryConfiguration` (FK cascade on User + Book), `AddLibraryEntries` migration, `UserRepository.GetLibraryBooksAsync` (join returns book + AddedAt, ordered newest-first), `IsBookInLibraryAsync`
- **API:** `LibraryController` — `GET /api/library`, `POST /api/library` (body `{ bookId }`), `DELETE /api/library/{bookId}`; all `[Authorize]`, userId from JWT `sub` claim
- Verified by manual smoke test: 401 anonymous, 409 duplicate add, 404 missing book, per-user isolation, remove → 404 on re-remove

## Public Books UI Status

- **Pages:** `Features/Books/Pages/BooksList.razor` (`/books` — responsive card grid, skeleton loading, EmptyState/ErrorNotice states), `BookDetails.razor` (`/books/{id:guid}` — cover, title, author, description, دانلود button, auth-aware افزودن به کتابخانه)
- **Components:** `BookCard` (presentational, hover lift), `BookCover` (img with graceful placeholder fallback), shared `LoadingSpinner`/`EmptyState`/`ErrorNotice` in `Shared/Components/`; all styled via CSS isolation + design tokens
- **Services:** `IBookService`/`BookService` (books list with 30 s client cache, detail, library-membership check, add-to-library), `IClientStorageService`/`ClientStorageService` (localStorage behind an interface), `AuthenticatedHttpClientHandler` (attaches the stored JWT as a Bearer header; each HttpClient owns its private handler chain)
- **Details-page auth flow:** anonymous click on افزودن → stores pending bookId in localStorage → redirects to `/login?returnUrl=/books/{id}` → after login/register returns to the book → auto-adds and shows a success notice; 409 duplicate is mapped to a friendly Persian message; if already in library the button is disabled («در کتابخانهٔ شماست»); a 401 after a fresh login shows the error instead of looping back to login
- **DI:** `AddScoped<IClientStorageService>` + HttpClient factory (with auth handler) + `AddScoped<IBookService>` in `BookStore.UI/Program.cs`
- Verified via browser smoke test: list + details render, anonymous→login→auto-add E2E, 409/401 handled gracefully

## Known Pitfalls (learned via smoke tests)

1. **EF treats client-generated Guid keys as existing rows.** `Entity.Id = Guid.NewGuid()` is non-default, so when a new child (e.g. `RefreshToken`) is added to a tracked aggregate, EF marks it `Modified` → `UPDATE` affects 0 rows → `DbUpdateConcurrencyException`. Fix: in `UserRepository.Update`, snapshot tracked children with `AutoDetectChangesEnabled = false` first, then set untracked ones to `EntityState.Added`.
2. **PBKDF2 hashes are salted; never compare hash strings directly.** `HashPassword` returns a different string each call, so `user.PasswordHash != passwordHash` always fails. Verify via `IPasswordHasher.VerifyPassword(password, storedHash)` in the handler, then pass the stored hash to the domain service.
3. **JWT inbound claims are remapped by default.** `JwtSecurityTokenHandler` maps `sub`/`email` to `ClaimTypes.*`, so `FindFirstValue(JwtRegisteredClaimNames.Sub)` returns null. Fix: `options.MapInboundClaims = false` on the JwtBearer options.
4. **EF Core 9 `PendingModelChangesWarning` at runtime.** If `Database.Migrate()` throws "model has pending changes" even though `dotnet ef migrations has-pending-model-changes` says clean, the migration snapshot is stale — run `dotnet ef migrations remove` then `dotnet ef migrations add <Name>` again.
5. **Static web assets from the referenced UI project only load in Development.** `WebHost.ConfigureWebDefaults` calls `StaticWebAssetsLoader.UseStaticWebAssets` only when `IsDevelopment()`. Running the API with `--no-launch-profile` (no `ASPNETCORE_ENVIRONMENT=Development`) yields `Production` → `/` returns 404 and the WASM app is not served. The launch profiles set Development, so normal `dotnet run` works. For Production, deploy the `dotnet publish` output (assets are copied into `wwwroot/`).
6. **`LocalFileStorage` relative paths include the `BaseUrl` prefix; strip it before resolving to disk.** `SaveAsync` returns `uploads/books/<name>` (BaseUrl + subdir + name), so `GetFullPath` must strip the `BaseUrl` segment before `Path.Combine` with `RootPath` — otherwise it yields `wwwroot/uploads/uploads/...` and `DeleteAsync` silently no-ops. This bit the file cleanup on book delete/update.
7. **Blazor DI: register `AuthStateProvider` under both the concrete and base types.** `MainLayout` injects the concrete `AuthStateProvider` while the auth pipeline resolves `AuthenticationStateProvider`. `AddScoped<AuthenticationStateProvider, AuthStateProvider>()` alone fails at render time ("no registered service of type 'BookStore.UI.Services.AuthStateProvider'"). Fix: `AddScoped<AuthStateProvider>()` + `AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<AuthStateProvider>())`.
8. **Tokens in localStorage are raw strings, not JSON.** `AuthStateProvider` stores `auth_token`/`refresh_token` as plain strings, so `ClientStorageService.GetItemAsync<string>` must catch `JsonException` and return the raw value — otherwise any read of a stored token crashes the request (hit when the JWT bearer handler broke all API calls once a token existed).
9. **A `DelegatingHandler` handed to `new HttpClient(handler)` needs `InnerHandler` assigned** (e.g. `InnerHandler = new HttpClientHandler()`), otherwise every request throws `InvalidOperationException: The inner handler has not been assigned.` Also: don't register one handler instance and share it across several HttpClients — each HttpClient should own its private chain (create it in the HttpClient factory).

## Next Steps

1. Build API endpoints (controllers, JWT validation) — DONE
2. Implement user library management (add to library, list user's books) — DONE
3. Create book management domain entities — DONE
4. Add validation and error handling (API-level) — DONE
5. Blazor WebAssembly UI: project + register/login pages + home page — DONE (hosted in BookStore.Api, served at `/`)
6. Public books UI (list + details + add-to-library) — DONE; admin CRUD UI (add/edit/delete books) — TODO

### Migrations

- Design-time factory `BookStoreDbContextFactory` in `Infrastructure/Persistence/` (SQLite `Data Source=bookstore.db`)
- Run from solution root:
  - `dotnet ef migrations add <Name> --project BookStore.Infrastructure --startup-project BookStore.Infrastructure`
  - `dotnet ef database update --project BookStore.Infrastructure --startup-project BookStore.Infrastructure`
- `bookstore.db` is created in `BookStore.Api/` at runtime (dev only, not for source control)
- The design-time factory (`--startup-project BookStore.Infrastructure`) creates a separate throwaway DB next to it — delete it after use; only the `BookStore.Api/` instance is needed
- EF Core pinned to 9.x (v10 targets net10.0); tool: `dotnet-ef` 9.0.17 (global); SDK 9.0.304