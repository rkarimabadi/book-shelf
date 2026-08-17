# BookStore Solution - Development Guide

> **Product name:** the public app is branded **«خانه کتاب»** (House of Books). `BookStore.*` remains the internal solution/project prefix. The brand string lives in the browser tab title (`wwwroot/index.html`), the `Home` hero, and the header logo `alt` (`MainLayout.razor`) — keep those four sites in sync when renaming (there are exactly four).

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
- `PasswordResetToken.cs` - Password-reset tokens (SHA-256 hashed, single-use, 1h expiry)
- `UserDomainEvents.cs` - All domain events

### Authentication Service
- `IAuthenticationService.cs` - Authentication contract
- `AuthenticationService.cs` - Authentication implementation

### Repositories
- `IUserRepository.cs` - User data access contract

### Book (Aggregate Root)
- `Book.cs` - Book entity with content management logic (Create/UpdateDetails/Delete/Activate/Deactivate)
- `BookDomainEvents.cs` - Book domain events (created, updated, deleted, deactivated, activated)
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
- **Structure**: `Pages/` (Home, Login, Register, ForgotPassword, ResetPassword, GoogleCallback), `Layout/` (MainLayout + scoped CSS, Persian RTL), `Services/` (`AuthenticationService` + `IAuthenticationService`, `AuthStateProvider`, `ClientStorageService` + `IClientStorageService`, `AuthenticatedHttpClientHandler`, `ProblemDetailsParser` shared helper, `PersianDateFormatter` — see the Persian Solar Calendar Dates Status section below), `Features/` (feature-driven: `Books/` and `Admin/` each with `Pages/` + `Components/` + `Services/`; `Library/` with `Pages/` only, reusing Books' components/services), `Shared/Components/` (LoadingSpinner, EmptyState, ErrorNotice, ConfirmDialog, AccessDenied), `wwwroot/` (index.html, css/app.css, vendored libs in `lib/` — Bootstrap 5.3.3 + Bootstrap Icons 1.13.1, versions pinned in `lib/README.md`; see the Typography & Fonts (Peyda) section below)
- **Auth flow**: login/register post to the API, then `AuthStateProvider.SignInAsync` persists `auth_token`/`refresh_token` in localStorage (`IJSRuntime`) and raises `NotifyAuthenticationStateChanged`. `AuthStateProvider.ParseToken` decodes the JWT payload (base64url) and builds `ClaimsPrincipal` (email/role/given_name/family_name); expired tokens → anonymous.
- `MainLayout` uses `<AuthorizeView>` (needs `AddAuthorizationCore` + `AddCascadingAuthenticationState` in `Program.cs`) to switch between "ورود/ثبتنام" links and the logged-in user + logout button. Nav links: `کتابخانه` for every authenticated user (inside `<AuthorizeView>`), and admin-only `مدیریت` inside `<AuthorizeView Roles="Admin">`.
- `App.razor` uses `<AuthorizeRouteView>` (not plain `RouteView`) with a `<NotAuthorized>` template → `AccessDenied` component: anonymous users are redirected to `/login?returnUrl=...`; authenticated non-admins see a Persian access-denied message. Admin pages carry `@attribute [Authorize(Roles = "Admin")]`.
- PWA capabilities (planned)

### Typography & Fonts (Peyda)

- **Single source of truth:** `--font-fa` (the PeydaWeb stack) in `wwwroot/css/app.css` `:root`; applied to `html, body` and — via the Bootstrap contract below — to every Bootstrap component.
- **Self-hosted loading:** Peyda v4.1 ships in `wwwroot/fonts/Peyda/01-Standard/WebFonts/` (commercial fontiran.com license). The `@font-face` rules live directly in `app.css` — the vendor's `fontiran.css`/`style.css` are **not** loaded (the vendor file also has a `Semibold`/`SemiBold` filename-case bug; our embedded faces use the correct casing).
- **Only 3 weights declared** — `400` Regular (body), `600` SemiBold (buttons/sub-headings), `700` Bold (titles) — the only weights the UI uses. Browsers download a face only when rendered text needs that weight, so unused faces would be dead CSS; **if a new weight is ever needed, add its `@font-face` back** (woff2 files for 100–900 all exist in the folder).
- **`font-display: swap`** on every face (no invisible-text flash) + `<link rel="preload" as="font" type="font/woff2" crossorigin>` for `PeydaWeb-Regular.woff2` in `index.html` — the font is fetched in parallel with CSS/JS during document parse, so the swap window is effectively eliminated (verified via CDP: initiator `link`, ~13 ms after navigation).
- **Bootstrap font contract** (Bootstrap 5.3.3 sets its own stacks via CSS variables; `app.css` `:root` overrides them to `var(--font-fa)`):
  - `--bs-font-sans-serif` — read **directly** by `.tooltip`/`.popover`, the only components that would otherwise render in a system font;
  - `--bs-body-font-family` — read by `body`;
  - `--bs-btn-font-family` — read by `.btn` (empty by default in 5.3.3 so buttons inherit; the override makes it explicit);
  - everything else (`.form-control`, `.form-select`, `.card`, `.modal`, `.dropdown-menu`, `.navbar`, …) sets no `font-family` and inherits; monospace (`code`/`kbd`/`pre`, `--bs-font-monospace`) is intentionally left untouched.
- **Upgrade safety:** this contract is the first thing to re-verify after any Bootstrap/font upgrade — see `wwwroot/lib/README.md` (the authoritative version manifest for the vendored `lib/` assets) and pitfall 16.

### Security
- Custom auth stack (no ASP.NET Core Identity): `User` aggregate + `PasswordHasher` (PBKDF2) + `JwtTokenGenerator` (JWT access tokens)
- Rotating refresh tokens (7-day expiry, revoked on refresh/logout) persisted in the `RefreshTokens` table
- Password reset: hashed single-use `PasswordResetToken` rows + `SmtpEmailSender` (SMTP via `SmtpSettings`; log-fallback when `Host` is empty)
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
  - `IDateTimeProvider`, `IJwtTokenGenerator`, `IPasswordHasher`, `IFileStorage`, `IEmailSender`, `IUnitOfWork` (injected into every handler to persist changes after business logic)
- Implementations live in Infrastructure

### Security
- `Roles` and `Policies` static classes, both defined in `Common/Security/Roles.cs` (`Roles.Admin`/`Roles.User`; `Policies.RequireAdminRole`/`Policies.RequireUserRole`). `RequireUserRole` = "registered account" and admits both `User` and `Admin` (admins can use the library, download, and call `/me`)
- Authorization is enforced via `[Authorize(Policy = ...)]` attributes on controllers (no auth pipeline behavior yet)

### Registration
- `DependencyInjection.AddApplication()` registers MediatR, FluentValidation validators, and `ValidationBehavior`
- Currently implemented: `Features/Authentication/` (Register, Login, RefreshToken, Logout, ForgotPassword, ResetPassword commands), `Features/Books/` (Create/Update/Delete/SetBookStatus commands, Get/GetBooks queries with `IncludeInactive`), `Features/Library/` (Add/Remove commands, GetUserLibrary query)

## Authentication Status

- **Domain (Core):** `User`, `RefreshToken`, `AuthenticationService` complete
  - `IAuthenticationService.RefreshToken` returns `(User User, string RefreshToken)` (rotates token and returns the new one)
  - Custom guard `GuardClauseExtensions.ExpiresInPast` in `Core/Domain/Common/`
- **Application:** Register/Login/RefreshToken/Logout commands with validators + handlers, `ValidationBehavior`, DI registration — complete and compiling
- **Infrastructure:** EF Core 9 + SQLite (`BookStoreDbContext`, Fluent API configurations), `UserRepository`, `BookRepository`, `UnitOfWork`, `JwtTokenGenerator`, `PasswordHasher` (PBKDF2), `DateTimeProvider`, `JwtSettings` (Options + validation), `LocalFileStorage`, Outbox pattern (`PublishDomainEventsInterceptor` + `ProcessOutboxMessagesJob` via Quartz.NET every 30 s, publishing through MediatR `DomainEventNotification`), `AddInfrastructure()` DI registration — complete and compiling
- **API:** AuthController (register/login/refresh/logout/me), JWT validation with `MapInboundClaims = false`, `ApiController` ProblemDetails base — complete and verified
- Refresh-token failures are uniformly `401` (Unauthorized): unknown token → `User.RefreshTokenNotFound`, plus expired/revoked variants; garbage token no longer returns 404
- **UI (Blazor WebAssembly):** `Login`/`Register` pages (Persian, client-side field validation, `returnUrl` support), `Home` page, `MainLayout` with `<AuthorizeView>`; `AuthenticationService` + `AuthStateProvider` (JWT parse from localStorage, sign-in/out, refresh-token rotation handshake) — complete and compiles; DI wiring verified (see pitfall 7)

## Google OAuth Login Status (ورود/ثبت‌نام با گوگل)

- **Requirement:** users can register/login with their Google account in addition to email+password
- **Flow:** UI button (`ورود با گوگل` / `ثبت‌نام با گوگل`) → `GET /api/auth/google-login` (`Challenge` on the `Google` scheme, `RedirectUri` = `/api/auth/google-finalize?returnUrl=…`) → Google consent → Google redirects to `/api/auth/google-callback` (the OAuth handler's registered `CallbackPath` — the handler validates `state`, exchanges the code, signs in the `BookStore.GoogleExternal` cookie and **redirects to the `RedirectUri`, i.e. `/api/auth/google-finalize`**) → `GET /api/auth/google-finalize` reads the verified identity from the external cookie (clears it), exchanges it for the app's own JWT pair via the `ExternalLoginCommand` → redirects to `/auth/callback?access_token=…&refresh_token=…&returnUrl=…` → `GoogleCallback.razor` persists the tokens (`AuthStateProvider.StoreTokensAsync` — no state notification) and **full-reloads** (`forceLoad: true`) to returnUrl; boot-time auth reads localStorage and builds the authenticated shell. ⚠️ Do NOT switch this to SPA navigation + `SignInAsync` — raising the auth notification while on the bounce page makes `AuthorizeRouteView` re-create the page (double navigation) and leaves the layout with a stale anonymous header (verified via CDP); the full reload is deliberate
- ⚠️ **RedirectUri must NOT be the OAuth `CallbackPath`:** `GoogleLogin` must point its `AuthenticationProperties.RedirectUri` at the separate `google-finalize` action, never at `google-callback`. If it points at the callback path, the OAuth handler intercepts the *post-auth* redirect too and fails with `The oauth state was missing or invalid` (no `state` on the second leg) → `OnRemoteFailure` → `/login?google_error=1` — this was the exact production failure behind the original `google_error=1` (proven by the `GoogleOAuth` log line showing `Query: ?returnUrl=%2F` with no `code`/`state`). The `redirect_uri` sent *to Google* stays `google-callback` (that is the registered URI); only the in-app continuation path changed. Verified locally: challenge Location shows `redirect_uri=…/api/auth/google-callback` while the debug log shows `redirectUri=…/api/auth/google-finalize?returnUrl=%2F`
- **Optional — gated by config:** requires `GoogleOAuth:ClientId` + `GoogleOAuth:ClientSecret` (appsettings; env vars `GoogleOAuth__ClientId` etc. override). Until both are set the schemes are NOT registered, `GET /api/auth/google-status` returns `false`, `/api/auth/google-login` returns 404, and the UI hides the button entirely
- **Account linking:** a Google sign-in looks the user up by email. New email → auto-provisions a `User` (role `User`, active, `HasPassword=false`) whose password is a PBKDF2 hash of a random never-known secret → the password form can never be used against it, but email-based password reset still works and the change-password page offers «تعیین رمز عبور» (set a password without proving a current one). Existing email (password-registered or Google-created) → same account, tokens issued; admins keep their role
- **Domain (Core):** `IAuthenticationService.LoginExternalUser(email)` — looks up by email, rejects inactive users (`UserInactive`), records the login and issues a fresh refresh token (mirrors `LoginUser` minus the password comparison)
- **Application:** `Features/Authentication/Commands/ExternalLogin/` (command + validator + handler) — find-or-create + JWT issuance, same shape as the Login/Register handlers
- **API:** the four endpoints live in `AuthController` (`google-status`/`google-login`/`google-callback`/`google-finalize`). `google-callback` is shadowed by the OAuth handler while the scheme is registered (the controller action is effectively dead — kept only as a defensive 404 when the scheme is off); `google-finalize` is the real continuation (reads the external cookie, issues JWTs). `returnUrl` is validated (`GoogleOAuthDefaults.SafeReturnUrl` — same-origin relative only) to prevent open redirects; the OAuth handler's built-in `state` parameter protects the callback against CSRF. Constants (scheme/cookie names, callback path) live in `BookStore.Api/Common/GoogleOAuthDefaults.cs`
- **UI:** `Login.razor`/`Register.razor` render the Google button + «یا» divider only when enabled (`IsGoogleEnabledAsync` → `GET api/auth/google-status`); `Pages/GoogleCallback.razor` (`/auth/callback`) consumes the token URL; `.google-button`/`.auth-divider` styles in `app.css`
- **Setup (Google Cloud Console):** create an OAuth 2.0 Client ID (Web application) at https://console.cloud.google.com/apis/credentials and register the redirect URI `https://<host>/api/auth/google-callback` (+ `http://localhost:PORT/api/auth/google-callback` for dev); paste Client ID/Secret into `GoogleOAuth`. **Never commit real credentials** — use `appsettings.Production.json` (gitignored) or env vars. Behind a TLS-terminating proxy, the `redirect_uri` must still be built from the public https origin: the app enables `UseForwardedHeaders` (see the Forwarded Headers bullet in Production Deployment) so `Request.Scheme`/`Host` are proxy-corrected automatically; only if the proxy cannot forward `X-Forwarded-*` headers (or you want to skip the middleware) keep the manual `GoogleOAuth:BaseUrl` fallback (mirrors the `PasswordReset:BaseUrl` precedent)
- **Known limitation:** the fresh JWT pair rides the callback URL query string (standard SPA pattern; the full reload leaves it in the address bar until the user navigates, and it may appear in server access logs). Full E2E requires real Google credentials; verified without them: build clean, status gating, challenge redirect to accounts.google.com (correct registered `redirect_uri`), post-auth redirect targets `google-finalize`, callback error path → `/login?google_error=1`, and the signed-JWT bounce-page flow (stores tokens → full reload → authenticated header)

## Book Content Management Status

- **Domain (Core):** `Book` aggregate complete (Create/UpdateDetails/Delete, domain events)
- **Application:** `Features/Books/` — CreateBook/UpdateBook/DeleteBook/SetBookStatus commands + validators + handlers, GetBook/GetBooks queries (with `IncludeInactive` for admins); Delete/Update handlers delete uploaded files (best-effort via `FileCleanup`)
- **Infrastructure:** `BookRepository`, `BookConfiguration`, `LocalFileStorage` (wwwroot/uploads, subdirs covers/books, `FileStorage` config section), `AddBooks` + `AddBookIsActive` migrations
- **API:** `BooksController` — GET public list/detail; POST/PUT/DELETE protected by `RequireAdminRole` policy; multipart upload (cover image + book file; Create rejects a missing book file with 400 `Book.FileRequired`)
- Verified by manual smoke test: CRUD + auth checks (403 for non-admin, 404 after delete), file cleanup on update/delete
- **Activate/Deactivate (SH-05):** `Book.IsActive` (default true) + `Deactivate()`/`Activate()` (+ `BookDeactivatedEvent`/`BookActivatedEvent`). Public list/detail/download treat inactive books as not-found: `GetBooksQuery`/`GetBookQuery` gained `IncludeInactive`, honored only for admins (`User.IsInRole(Roles.Admin)` — non-admins never see hidden books). Users' libraries hide inactive books via the `b.IsActive` join filter in `GetLibraryBooksAsync` (**LibraryEntry rows are kept, so reactivation restores them** — reversible by design). `AddBookToLibraryCommand` rejects inactive books (400 `Book.Inactive`). Admin endpoint: `PATCH /api/books/{id}/status` (`RequireAdminRole`, body `{ isActive }`) -> `SetBookStatusCommand`. `BookResponse.IsActive` added (all construction sites updated). UI: toggle button (فعال‌سازی/غیرفعال‌سازی) + «غیرفعال» badge + dimmed rows on `/admin`; the admin list uses `api/books?includeInactive=true` (admin service, no public cache); `AdminBookEdit` loads inactive books (`GetBookAsync(id, includeInactive: true)`).
- ⚠️ **Migration trap:** EF generated `AddBookIsActive` with `defaultValue: false`, which would have silently deactivated every existing book on deploy — hand-edited to `defaultValue: true`. Verify defaults whenever EF adds a new non-null column.

## User Library Status

- **Domain (Core):** `LibraryEntry` entity + `User.AddToLibrary` / `User.RemoveFromLibrary` (guards: duplicate add → `Conflict`, missing book → `NotFound`), `BookAddedToLibraryEvent` / `BookRemovedFromLibraryEvent`
- **Application:** `Features/Library/` — AddBookToLibrary/RemoveBookFromLibrary commands, GetUserLibrary query
- **Infrastructure:** `LibraryEntryConfiguration` (FK cascade on User + Book), `AddLibraryEntries` migration, `UserRepository.GetLibraryBooksAsync` (join returns book + AddedAt, ordered newest-first), `IsBookInLibraryAsync`
- **API:** `LibraryController` — `GET /api/library`, `POST /api/library` (body `{ bookId }`), `DELETE /api/library/{bookId}`; all `[Authorize(Policy = Policies.RequireUserRole)]`, userId from JWT `sub` claim
- Verified by manual smoke test: 401 anonymous, 409 duplicate add, 404 missing book, per-user isolation, remove → 404 on re-remove

## User Library UI Status

- **Page:** `Features/Library/Pages/MyLibrary.razor` (`/library`, `[Authorize]` + `AuthorizeRouteView` — anonymous users are redirected to `/login?returnUrl=%2Flibrary`); responsive card grid reusing `BookCard`, plus skeleton loading, `EmptyState` (with CTA to /books) and `ErrorNotice` with retry
- **Data:** reuses `IBookService.GetMyLibraryAsync()` (`GET /api/library`); maps `LibraryBookResponse` → `BookResponse` for `BookCard` (maps `AddedAt` into both `CreatedAt`/`UpdatedAt` — harmless since the card never renders dates, see comment in the page)
- **Remove:** `MyLibrary.razor` has a ✕ overlay button on each card + `ConfirmDialog` (calls `IBookService.RemoveFromLibraryAsync` → `DELETE /api/library/{id}`, removes the card from the grid and shows a Persian success notice; the `EmptyState` returns when the last book is removed); `BookDetails.razor` replaced the disabled «در کتابخانهٔ شماست» button with an active «حذف از کتابخانه» button + `ConfirmDialog`, flipping back to «افزودن به کتابخانه» after removal. Both redirect to login with `returnUrl` on 401; `BookService.ReadProblemMessageAsync` maps `User.BookNotInLibrary` (404) to a friendly Persian message
- **Nav:** «کتابخانه» link in `MainLayout` inside `<AuthorizeView>` (visible to all authenticated users; the «مدیریت» link stays admin-only)
- Verified via API smoke test (build 0 warnings / 0 errors): DELETE → 204, library empties, re-remove → 404 with `User.BookNotInLibrary`, anonymous → 401; add/remove round-trip on the details page; empty state for a fresh user

## Password Reset Status (SH-04 - بازیابی رمز عبور)

- **Domain (Core):** `PasswordResetToken` entity (SHA-256-hashed token, 1h expiry, single-use `MarkUsed`); `User.AddPasswordResetToken` / `InvalidatePasswordResetTokens` (a new request invalidates all older links) / `ResetPassword` (validates token, sets the new hash, revokes ALL refresh tokens -> forces re-login; rejects inactive users); events `PasswordResetRequestedEvent` / `PasswordResetCompletedEvent`
- **Application:** `Features/Authentication/Commands/ForgotPassword/` + `ResetPassword/` (command + validator + handler each); `IEmailSender` contract in `Application/Common/Interfaces/`
- **Infrastructure:** `SmtpEmailSender` (`System.Net.Mail.SmtpClient`, zero new packages) driven by the `SmtpSettings` config section; **log-fallback**: when `SmtpSettings:Host` is empty the email (incl. the reset link) is written to the server log instead of sent - dev convenience, but in **Production you MUST set a real SMTP host or reset emails are never delivered**; `AddPasswordResetTokens` migration; `UserRepository.GetByPasswordResetToken` + fixup loop covers the new child collection (pitfall 1)
- **API:** `POST /api/auth/forgot-password` (always 204 - no user enumeration; reset link base = `Request.Scheme://Request.Host/reset-password` — proxy-corrected by `UseForwardedHeaders`, overridable via `PasswordReset:BaseUrl` as a fallback) - `POST /api/auth/reset-password` (204 on success; 400 expired/used, 404 invalid token)
- **UI:** `ForgotPassword.razor` (`/forgot-password`, generic success message) + `ResetPassword.razor` (`/reset-password?email=&token=` - new password + confirm), forgot-password link on `Login.razor`; auth-card styling + `.auth-success`/`.auth-links`/`.auth-button-link` in `app.css`
- Verified E2E with a fake SMTP sink: forgot -> 204, email captured with the link, reset -> 204, login with new password -> 200, old password -> 401, token replay -> 400, unknown-email forgot -> 204, re-forgot invalidates the previous token

## Change Password Status (SH-08 - تغییر رمز عبور)

- **Domain (Core):** `User.ChangePassword(newPasswordHash)` — rejects inactive users, sets the new hash, revokes ALL refresh tokens (forces re-login) and fires `PasswordChangedEvent`. The current-password verification lives in the handler because PBKDF2 hashes are salted (pitfall 2) — the domain cannot re-verify a plaintext password
- **Google-created accounts (set-password mode):** `User.HasPassword` (bool, column default `true`) marks whether the account has a usable password. Google-created accounts start `false` (`ExternalLoginCommandHandler` passes `hasPassword: false` through `RegisterUser` → `User.Create`); `ChangePassword`/`ResetPassword` flip it back to `true`. When `HasPassword == false` the change-password flow **skips the current-password check** so the user can SET a password without proving one — after which change-password behaves normally and email+password login works alongside Google
- **Application:** `Features/Authentication/Commands/ChangePassword/` (command + validator + handler): handler loads the user via `IAuthenticationService.GetUserByEmail`, verifies the current password with `IPasswordHasher.VerifyPassword(current, storedHash)` **only when `user.HasPassword`** (mismatch → 400 `User.InvalidCurrentPassword`), hashes the new password, calls `IAuthenticationService.ChangePassword(email, newHash)` and persists via `IUnitOfWork`. `CurrentPassword` is nullable in the command/request; the validator no longer requires it
- **API:** `POST /api/auth/change-password` (`[Authorize(Policy = Policies.RequireUserRole)]`) — identity taken from the JWT `email` claim, not the body; 204 on success; 401 if the claim is missing. `GET /api/auth/me` now also returns `hasPassword` (loaded from the DB; falls back to `true` if the user row can't be fetched) so the UI knows which mode to render
- **UI:** `ChangePassword.razor` (`/change-password`, `[Authorize]` → anonymous users hit `/login`): on init calls `AuthService.HasPasswordAsync()` (→ `/api/auth/me`); passwordless (Google-created) accounts hide the «رمز عبور فعلی» field, get the title «تعیین رمز عبور» and an explanatory subtitle, and submit with a null current password; otherwise current/new/confirm fields with client-side validation, mapping the wrong-current-password error to «رمز عبور فعلی اشتباه است.» After success the page signs the user out locally (`LogoutAsync` + redirect to `/login`) since the server revoked every refresh token. «تغییر رمز» nav link added to `MainLayout` inside the `<AuthorizeView>` (all authenticated users)
- **Migration:** `AddUserHasPassword` — new non-null `HasPassword` column with **default `true`** (hand-verified; EF would have generated `false` — the migration-trap pitfall — which would have silently made every existing password account skip verification). ⚠️ Existing rows all get `true` — Google-created accounts that predate this deploy are indistinguishable from password accounts (their hash is just a hash), so they keep the old behavior (must use email reset) until the column is flipped: `UPDATE Users SET HasPassword=0 WHERE <email>` for any known pre-existing Google accounts
- Verified via API smoke test: anonymous → 401 · password account: `/me` → `hasPassword:true` · wrong current → 400 `User.InvalidCurrentPassword` · Google-style account (`HasPassword=0` in DB): `/me` → `hasPassword:false` · set-password with `currentPassword:null` → 204 · login with the new password → 200 (proves it was really set) · old password → 401 · DB flag flipped back to `true` after the set

## Admin User Management Status (SH-07 - مدیریت کاربران)

- **Domain (Core):** `User.ChangeRole(role)` (same-role → 409 `User.RoleAlreadySet`, invalid → 400 `User.InvalidRole`, fires `UserRoleChangedEvent`); `User.Deactivate()` now also revokes every refresh token so a blocked account loses its sessions. Errors: `User.NotFound` (by id), `User.CannotModifySelf` (409). Removed the unused `DeactivateUser(email)`/`ActivateUser(email)` service methods — handlers call `IUserRepository` directly (same pattern as `SetBookStatusCommand`)
- **Application:** `Features/Users/` — `Queries/GetUsers/` (list all, newest-first) + `Commands/SetUserStatus/`, `Commands/SetUserRole/`, `Commands/DeleteUser/` (command + validator + handler each). Handlers: load via `IUserRepository.GetByIdAsync`, block any mutation of the caller's own account (`CannotModifySelf`), persist via `IUnitOfWork`; delete relies on FK cascade for refresh/password-reset tokens and library entries (books untouched)
- **Infrastructure:** `UserRepository.GetUsersAsync` (AsNoTracking, ordered by `CreatedAt` desc) — `GetByIdAsync`/`Delete` already existed
- **API:** `UsersController` (`/api/users`, every endpoint `[Authorize(Policy = Policies.RequireAdminRole)]`): `GET` list → `UserResponse` · `PATCH {id}/status` (body `{ isActive }`) → 204 · `PATCH {id}/role` (body `{ role }`, invalid → 400) → 204 · `DELETE {id}` → 204. Current admin id from JWT `sub` claim (missing → 401)
- **UI:** `Features/Admin/Pages/AdminUsers.razor` (`/admin/users`, `[Authorize(Roles = "Admin")]`): rows with avatar initials, name/email/join date, role badge + ارتقا/تبدیل button, مسدود/فعال toggle, حذف with `ConfirmDialog`; the admin's own row shows «حساب شما» with no actions. `IAdminUserService`/`AdminUserService` (401 → login redirect, 403 → Persian denial, Persian mapping for `User.NotFound`/`CannotModifySelf`/`RoleAlreadySet`); «کاربران» nav link added for admins. `AuthStateProvider.ParseToken` now also emits the `sub` claim as `ClaimTypes.NameIdentifier` (used to detect the self-row)
- No migration required (no schema change)
- Verified via API smoke test: anon list 401 · non-admin list 403 · admin list 200 · deactivate 204 (+ blocked user's login → 401, `IsActive=false` in list) · reactivate 204 · role change 204 · same role again 409 · self status/role/delete all 409 · missing user 404 · invalid role 400 · delete 204 (user vanishes from list) · DB check: zero orphaned `RefreshToken`/`LibraryEntry` rows after delete (cascade works)

## Admin Password Reset Status (SH-10 - تغییر/بازنشانی رمز عبور توسط ادمین)

- **Requirement:** an admin can set/reset any user's password without knowing the current one; the user is forced to log in again afterwards
- **Domain (Core):** no new domain code — reuses `User.ChangePassword(newHash)` (validates activity, sets the hash, revokes ALL refresh tokens → forced re-login, raises `PasswordChangedEvent`)
- **Application:** `Features/Users/Commands/ResetUserPassword/` (command + validator + handler): blocks mutating the caller's own account (`CannotModifySelf`), loads via `IUserRepository.GetByIdAsync`, hashes the new password with `IPasswordHasher` (PBKDF2 — never compare hashes), calls `user.ChangePassword`, persists via `IUnitOfWork`; validator enforces ≥ 8 chars
- **Contracts:** `ResetUserPasswordRequest(Password)` in `BookStore.Contracts/Users/UserContracts.cs`
- **API:** `PATCH /api/users/{id}/password` (`[Authorize(Policy = Policies.RequireAdminRole)]`) — body `{ password }`, 204 on success; 401 missing claim · 400 short password (FluentValidation) · 404 unknown user · 409 self-reset (`CannotModifySelf`) · 401 inactive user (`User.Inactive`)
- **UI:** `AdminUserPasswordDialog` component (`Features/Admin/Components/`) — modal with new password + confirm, client-side validation (required, ≥ 8 chars, match), busy spinner; `AdminUsers.razor` gains a «تغییر رمز» (`bi-key`) action per row (hidden for the admin's own row), success notice «…تغییر کرد و از همهٔ دستگاهها خارج شد», errors shown inside the dialog; `IAdminUserService.ResetUserPasswordAsync` → `PATCH`; `MapPersian` now matches both error codes AND English descriptions (ProblemDetails title surfaces the description, e.g. `User.Inactive` / «is inactive» → «حساب کاربر غیرفعال است…»)
- No migration required (no schema change)

## Responsive Navigation Status (SH-09 - ناوبری واکنش‌گرا)

- **Requirement:** the header/nav must work on desktop, tablet, and mobile — there is NO hamburger drawer; on mobile the nav condenses to icons only
- **Three-tier navigation** (all icons are Bootstrap Icons, vendored in `wwwroot/lib/bootstrap-icons/`):
  - **Visitors & users share the top header:** primary nav `خانه` (`bi-house`) + `کتاب‌ها` (`bi-book`), plus `کتابخانه` (`bi-bookmark`) only inside `<AuthorizeView>` (authenticated users). The opposite side of the header shows `ورود`/`ثبت‌نام` (`bi-box-arrow-in-left`/`bi-person-plus`) for anonymous visitors, or the **user pill** for authenticated users
  - **Authenticated users:** clicking the pill toggles a dropdown (`role="menu"`, `aria-haspopup`/`aria-expanded`) with `تغییر رمز` (`bi-shield-lock`) and `خروج` (`bi-box-arrow-right`). A transparent full-screen `.user-menu-overlay` (z-100) closes the dropdown on outside click; the dropdown itself sits above it (z-101) and works identically on every screen size
  - **Admins only:** a second strip below the header (`AuthorizeView Roles="Admin"`) with `مدیریت کتاب‌ها` (`bi-collection`, `/admin`) and `مدیریت کاربران` (`bi-people`, `/admin/users`) — visible on all screens, unchanged on mobile. The books tab's active state is computed in `AdminBooksClass` (active on `/admin`, `/admin/add`, `/admin/edit` but NOT `/admin/users`, since `NavLink` can't express "prefix minus one path"; the users tab uses `Match="NavLinkMatch.Prefix"`)
- **Mobile (`≤1023.98px`):** home/books/library labels are visually hidden (clip pattern, still announced by screen readers) so only icons remain; ورود/ثبت‌نام keep their labels (just more compact); the user pill drops the name (`.user-menu-name` `display:none` — safe because the trigger's `aria-label` is built from the name) keeping avatar + chevron; tap targets stay at 44px
- Accessibility: `aria-label` (incl. user name) + `aria-expanded` on the user trigger, `role="menu"`/`role="menuitem"` on the dropdown
- No server/API changes. Verified: full solution build clean (0 warnings / 0 errors)

## Toast Notifications Status (SH-06 - پیام‌های عملیاتی)

- **Requirement:** operation messages ("با موفقیت به کتابخانه اضافه شد", "خطا در ثبت‌نام", …) appear as non-blocking slide-down notifications instead of inline page banners
- **Service:** `Services/ToastService.cs` — scoped in-memory bus with `ShowInfo`/`ShowSuccess`/`ShowError`, a `Changed` event, and `Dismiss(id)`. Success/info toasts auto-dismiss after 4.5 s; **error toasts are sticky** (persist until manually closed via the ✕ button) so the user can't miss a failure. Toast IDs are `Guid`-keyed
- **Host:** `Shared/Components/ToastHost.razor` + `.razor.css` — fixed-position stack bottom-start (RTL-aware via `inset-inline-end`), z-index 1100 (above the user-menu overlay's 101), slide-in animation (disabled under `prefers-reduced-motion`), `aria-live="polite"` container, `role="status"`/`role="alert"` per toast, close buttons. Styled with the app design tokens (`--background`/`--label`/`--separator`, `--shadow-md`, colored 4px accent bar per variant: blue/green/red) so it follows light/dark mode automatically. Mounted once in `MainLayout` (after `<main>`), so it survives page navigation; the service instance lives in the DI container alongside the layout
- **Wiring (all previous inline `_notice` banners replaced with toasts):** `BookDetails` (add/remove/download/copy-link/share), `MyLibrary` (remove), `AdminBooks` (activate/deactivate/delete), `AdminUsers` (block/reactivate/role change/reset password/delete — the password dialog keeps its own inline error), `AdminBookAdd`/`AdminBookEdit` (fire the success toast right before navigating back to `/admin`), and the auth pages `Login`/`Register`/`ForgotPassword`/`ResetPassword`/`ChangePassword` (operation errors now toast; **field-validation errors stay inline** next to their inputs; full-page `Sent`/`Done` success states and the broken reset-link state — which renders `ErrorNotice` instead of the form — stay inline since they're page states, not operations). Auth-transition welcomes: `Login` → «خوش آمدید، {FirstName}!», `Register` → «ثبت‌نام با موفقیت انجام شد. خوش آمدید، {FirstName}!» (both fire before `NavigateTo`; the layout-hosted toasts survive navigation — same pattern as AdminBookAdd/Edit), and `MainLayout.HandleLogout` → «با موفقیت خارج شدید.» (`RegisterAsync` now returns `AuthenticationResponse` to carry the name; `ChangePassword`'s internal `LogoutAsync` intentionally shows no logout toast since its Done panel explains the forced re-login)
- Cleanup: dead `.details-notice`/`.library-notice`/`.admin-notice`/`.auth-error` CSS removed along with the `_notice`/`ErrorMessage` page state
- No server/API changes. Verified: full solution build clean (0 warnings / 0 errors)

## Public Books UI Status

- **Pages:** `Features/Books/Pages/BooksList.razor` (`/books` — responsive card grid, skeleton loading, EmptyState/ErrorNotice states), `BookDetails.razor` (`/books/{id:guid}` — cover, title, author, description, دانلود button, auth-aware افزودن به کتابخانه)
- **Components:** `BookCard` (presentational, hover lift), `BookCover` (img with graceful placeholder fallback), shared `LoadingSpinner`/`EmptyState`/`ErrorNotice` in `Shared/Components/`; all styled via CSS isolation + design tokens
- **Services:** `IBookService`/`BookService` (books list with 30 s client cache, detail, library-membership check, add-to-library), `IClientStorageService`/`ClientStorageService` (localStorage behind an interface), `AuthenticatedHttpClientHandler` (attaches the stored JWT as a Bearer header; each HttpClient owns its private handler chain)
- **Share (CH-04):** «کپی لینک» button on `BookDetails.razor` copies the current book URL via `window.bookstoreCopyText` (Clipboard API with a hidden-textarea `execCommand` fallback for non-secure contexts) and shows «لینک کتاب کپی شد.»; when the browser supports the Web Share API a «اشتراک‌گذاری» button also appears (`window.bookstoreShare` → native share sheet with title + URL; resolves false on cancel so no error noise). JS helpers live in `index.html` next to `bookstoreDownload`.
- **Details-page auth flow:** anonymous click on افزودن → stores pending bookId in localStorage → redirects to `/login?returnUrl=/books/{id}` → after login/register returns to the book → auto-adds and shows a success notice; 409 duplicate is mapped to a friendly Persian message; if already in library the button is disabled («در کتابخانهٔ شماست»); a 401 after a fresh login shows the error instead of looping back to login
- **DI:** `AddScoped<IClientStorageService>` + HttpClient factory (with auth handler) + `AddScoped<IBookService>` in `BookStore.UI/Program.cs`
- Verified via browser smoke test: list + details render, anonymous→login→auto-add E2E, 409/401 handled gracefully

## Books Pagination Status (SH-03 - صفحه‌بندی)

- **Requirement:** when the catalog grows, the public books list shows a few pages instead of loading everything at once
- **Server — paged list endpoint:** `GET /api/books` now accepts `page` (1-based) + `pageSize` (clamped 1–500 — the ceiling exists so the admin catalog's single "all rows" page of 500 is never silently truncated; the public UI never asks for more than 12, default 12) and returns a `PagedBooksResponse` envelope `{ items, page, pageSize, totalCount, totalPages }`. `GetBooksQuery` gained `Page`/`PageSize` and returns `GetBooksResult` with the **clamped effective** page/pageSize and `totalPages = max(1, ceil(total/pageSize))` computed in the handler (single place that knows the bounds). `IBookRepository.GetAllAsync`/`GetActiveAsync` were replaced by a single `GetPageAsync(page, pageSize, includeInactive, ct)` returning `(Items, TotalCount)` — EF runs `CountAsync` + `Skip`/`Take` in one round-trip pair; public (active-only) and admin (`includeInactive`) queries share it
- **Admin list:** `AdminBookService.GetBooksAsync` now parses the envelope with `?includeInactive=true&pageSize=500` (admin page still shows the full catalog)
- **UI (`BooksList.razor`):** 12 books per page; subtitle shows the range («نمایش ۱۳ تا ۲۴ از ۴۷ کتاب») or «مجموعهٔ N کتاب» on a single page; a pager below the grid renders قبلی/بعدی + numbered pages with `…` ellipses (first + last page always visible, ±1 window around the current page) — active page highlighted, disabled at the ends, 44px tap targets (compact on ≤480px), `aria-label="صفحه‌بندی"` + `aria-current="page"`. Page switches show the skeleton briefly, then scroll to top (`scrollTo(0,0)`). If the requested page no longer exists (books deleted), `LoadAsync` clamps to the last page and reloads
- **Client cache:** `BookService` now caches per `(page, pageSize)` key (30 s TTL each) instead of the whole list
- No migration needed. Verified via API smoke test: page1 12/14 items + totalPages 2 · page2 → 2 items (newest-first order) · page3 → empty page without error · `pageSize=999` → clamped (was 50, now 500) · `pageSize=5` → 3 pages · `page=0` → clamped to 1 · admin `includeInactive=true` → 15 (incl. inactive) · anonymous flag ignored → 14 · **regression check with 60 seeded books**: admin `pageSize=500` returns all 60 (the original 50-cap would have silently truncated the admin list), `pageSize=999` clamps to 500, public default returns 12/60. Full solution builds 0 warnings / 0 errors

## Book Categories Status (CH-01 - دسته‌بندی)

- **Requirement:** books carry genre labels (رمان، علمی، تاریخی، …) and the public list can be filtered by them
- **Domain:** `BookCategories` (Core) holds the fixed catalog — values are the **Persian labels themselves** (رمان/علمی/تاریخی/فلسفه/شعر و ادبیات/کودک و نوجوان/مذهبی/متفرقه), so the stored value, API payload and UI labels are the same string (no code→label mapping anywhere). `Book.Category` (required) is validated in `Create`/`UpdateDetails` via `BookCategories.IsValid` → `BookErrors.Validation.InvalidCategory` (400)
- **Data:** `AddBookCategory` migration adds a `Category` TEXT column with `defaultValue: "متفرقه"` (General) so pre-existing books land in «متفرقه»; `BookConfiguration` maps it `IsRequired().HasMaxLength(50).HasDefaultValue(BookCategories.General)`
- **Application:** `CreateBookCommand`/`UpdateBookCommand` gain `Category` (validators: required + `Must(IsValid)`); `GetBooksQuery` gains `Category`; new `GetBookCategoriesQuery` returns the catalog
- **API:** `GET /api/books/categories` → `List<string>`; `GET /api/books?category=…` filters (works with pagination); Create/Update accept `category`; all `BookResponse` sites + `LibraryBookResponse` carry `Category`
- **UI:** `BookForm` gains a required دسته‌بندی `<select>` (fetched from the API, prefilled in edit, «انتخاب کنید» placeholder + validation); `BooksList.razor` shows filter chips («همه» + categories) that refetch server-side (resets to page 1, cache key includes category, subtitle shows «… در دستهٔ «X»»); `BookCard` shows a `bi-tag` category pill; `BookDetails` shows a category pill; `AdminBooks` rows show a category badge; `MyLibrary` maps `LibraryBookResponse.Category` through. `AdminBookService` maps the new category validation errors to Persian
- No schema risk (string default verified in the migration). Verified via API smoke test: categories endpoint returns 8 · create with valid Persian category → 201 (stored correctly) · invalid/missing category → 400 · filter رمان/علمی/تاریخی returns only matching books · filter + pagination (pageSize=2 → 2 items/total 3/2 pages) · library response carries category. Full solution builds 0 warnings / 0 errors

## Books Search Status (SH-02 - جستجو)

- **Requirement:** a search box on the books list page to find books by title
- **Server:** `GET /api/books?search=…` — substring match on title (`EF.Functions.Like("%…%")`), composes with the existing `category` filter and `page`/`pageSize` pagination. `GetBooksQuery` gained `Search`; `IBookRepository.GetPageAsync` gained a `search` param (applied with the category filter before `CountAsync`/`Skip`/`Take`, so `totalCount`/`totalPages` reflect the filtered set)
- **UI (`BooksList.razor`):** a search box above the grid with a `bi-search` icon, a clear (✕) button, and a **debounced** `oninput` (500 ms) so typing doesn't hammer the API. Typing resets to page 1 and refetches server-side; the subtitle shows the result context («… برای «X»»); the component `@implements IDisposable` to cancel the pending debounce on dispose. Cache key in `BookService` includes `search`
- No migration needed. Verified via API smoke test: search=رمان → 2 (رمان عاشقانه، رمان تاریخی) · search=تاریخ → 2 (رمان تاریخی، تاریخ ایران) · search=کتاب → 1 (کتاب علمی نجوم) · search+category combined → 2 (only رمان titles in رمان category) · search+pagination → clamped to last page · empty search → all. Full solution builds 0 warnings / 0 errors

## Personal Book Notes Status (CH-03 - یادداشت‌نویسی شخصی)

- **Requirement:** a logged-in user can keep a private note about a book on its details page (e.g. «چند صفحه از این را خوانده‌ام»)
- **Domain:** `BookNote` aggregate (Core) — `UserId` + `BookId` + `Note` (max 1000) + `UpdatedAt`; `Create`/`Update` validate length → `BookNoteErrors.TooLong` (400). One note per user per book (unique index). `IBookNoteRepository` contract in Core
- **Data:** `AddBookNotes` migration creates `BookNotes` with a unique (UserId, BookId) index, FK cascade on both User and Book (deleting a book or user removes its notes), `Note` TEXT max 1000; `BookNoteConfiguration` + `DbSet<BookNote>` + `BookNoteRepository` + DI registration
- **Application:** `Features/Notes/` — `GetBookNoteQuery(userId, bookId)` → the note text ("" when none); `SaveBookNoteCommand(userId, bookId, note)` upserts (create if none, update if exists) and treats a blank note as **clear** (deletes the row; deleting an absent note is a no-op). Saving for a non-existent book → 404 `Book.NotFound`; validator caps length
- **API:** `BookNotesController` — `GET/PUT /api/books/{bookId:guid}/note`, both `[Authorize(Policy = Policies.RequireUserRole)]`, userId from the JWT `sub` claim (missing → 401). GET → `BookNoteResponse(note)`; PUT → 204
- **UI:** `BookService.GetBookNoteAsync`/`SaveBookNoteAsync` (+ `SaveNoteResult`); `BookDetails.razor` shows a «یادداشت خصوصی» panel (authenticated users only) — textarea (maxlength 1000, live `N/1000` counter), «ذخیرهٔ یادداشت» button with busy state, loads the existing note on init, toasts on save, 401 → login redirect. Anonymous visitors never see the panel
- No schema risk (unique index + cascades verified in the migration). Verified via API smoke test: anon GET 401 · GET empty `{"note":""}` · PUT 204 · GET returns the saved Persian note · another user's GET is empty (per-user isolation) · PUT for a non-existent book 404 · PUT blank clears (GET → empty). Full solution builds 0 warnings / 0 errors

## Download Gating Status

- **Requirement (MVP — مسیرهای شرطی):** unregistered viewers must not download book files; an anonymous click on «دانلود کتاب» redirects to login and auto-downloads after successful auth — mirrors the add-to-library flow
- **Server — protected endpoint:** `GET /api/books/{id}/download` (`[Authorize(Policy = Policies.RequireUserRole)]` — any registered account, incl. admins) in `BooksController`: fetches the book, resolves the file via `IFileStorage.GetFullPath` **rooted with `IWebHostEnvironment.ContentRootPath`** (see pitfall 13), 404 `Book.FileMissing` when absent, returns `PhysicalFile` as `application/epub+zip` with `Content-Disposition: attachment`
- **Server — static protection:** `Program.cs` — `UseStaticFiles()` moved **after** `UseAuthentication()`; a `UseWhen("/uploads/books")` branch denies anonymous requests with 401 (covers at `/uploads/covers` stay public)
- **UI:** `BookDetails.razor` — anonymous sees a «دانلود کتاب» button that stores `pending_download_book` in localStorage and redirects to `/login?returnUrl=/books/{id}`; after login `ProcessPendingDownloadAsync` (same round-trip/abandonment guards as the add flow) auto-triggers the download; authenticated users download via `BookService.DownloadBookAsync` (fetch bytes over the authenticated HttpClient) and `window.bookstoreDownload` JS helper in `index.html` saves the blob; the display filename strips the server's `<32-hex>_` prefix from `FilePath` (`DisplayFileName`)
- Verified via API smoke test: anon direct file 401, anon endpoint 401, authed endpoint 200 (correct content-type/filename), authed direct access to a known static type (`.txt`) 200 while anon is 401

## Admin Content Management UI Status

- **Pages:** `Features/Admin/Pages/AdminBooks.razor` (`/admin` — book list with cover thumb + status toggle (فعال‌سازی/غیرفعال‌سازی) + «غیرفعال» badge and dimmed rows for deactivated books, ویرایش/حذف actions, empty/loading/error states, ConfirmDialog on delete + success notice), `AdminBookAdd.razor` (`/admin/add`), `AdminBookEdit.razor` (`/admin/edit/{id:guid}`)
- **Components:** `BookForm` (shared add/edit form: title/author/description + cover & EPUB `InputFile` pickers with C#-only cover preview via base64 data-URL — no JS interop; client-side validation: title/author required, EPUB required on create, file-size caps 5 MB cover / 25 MB book; edit mode prefills and keeps existing files when no new ones are chosen), shared `ConfirmDialog` (pure-CSS modal) and `AccessDenied`
- **Services:** `IAdminBookService`/`AdminBookService` — multipart POST/PUT (`title`/`author`/`description`/`coverImage`/`file` field names matching `BooksController`; safe ASCII upload names since the server renames anyway) + GET all books incl. deactivated (`api/books?includeInactive=true`, no public cache) + `PATCH api/books/{id}/status` (activate/deactivate) + DELETE; maps ProblemDetails to Persian messages via shared `ProblemDetailsParser` (also used by `BookService` and `AuthenticationService`)
- **Auth:** pages guarded with `[Authorize(Roles = "Admin")]` + `AuthorizeRouteView` (see Frontend section); 401 → redirect to login with returnUrl; 403 → friendly Persian message
- **DI:** `AddScoped<IAdminBookService, AdminBookService>()` in `BookStore.UI/Program.cs` (uses the same shared HttpClient with the JWT bearer handler)
- Verified via browser + API smoke test: admin-only nav, anonymous `/admin` → `/login?returnUrl=%2Fadmin`, add form validation, edit prefill + save (title-only update keeps files), delete with confirm, and API contract 201/200/401/403/204

## Persian (Farsi) Localization Status

- **Requirement:** every message and notification shown to the user must be in Farsi — no English leaks in toasts, dialogs, errors, or system UI
- **Server stays English (by design):** the API returns English ProblemDetails (`title` = English description, `detail` = error code) as the single source of truth for error codes; the UI maps them to Farsi at the edge
- **Client translation layers:** `AuthenticationService.MapPersian` (25+ mappings covering every FluentValidation message + ErrorOr description the auth endpoints emit — invalid credentials, email exists/not-found, inactive account, reset-token invalid/expired/used, refresh-token errors, field validation), `BookService.ReadProblemMessageAsync` (note-too-long, book not-found, inactive, file-missing, library errors), `AdminBookService.MapPersian` (Book.Inactive, category errors, …), `AdminUserService.MapPersian` (User.NotFound/CannotModifySelf/RoleAlreadySet/Inactive, password-length). Each mapper matches **both error codes AND English descriptions** (ProblemDetails `title` surfaces the description); order specific → generic so precedence is safe
- **System UI translated:** `index.html` `#blazor-error-ui` («خطای غیرمنتظره‌ای رخ داد. / بارگذاری مجدد») and the `App.razor` 404 page («صفحه پیدا نشد / صفحه‌ای که دنبال آن هستید پیدا نشد.»)
- **Convention:** all page text, dialogs, buttons, empty states, pager labels, and shared components (`LoadingSpinner`, `EmptyState`, `ErrorNotice`, `ConfirmDialog`, `Pager`, `ToastHost`, `AccessDenied`) are Farsi; field-validation errors stay inline next to their inputs while operation errors go to toasts
- **Adding a new service call:** route every user-facing error through the service's `MapPersian`/`ReadProblemMessageAsync` — never let a raw English ProblemDetails `title` reach a toast
- Verified by a full UI sweep + live API sampling of server error strings: build clean (0 warnings / 0 errors)

## Persian Solar Calendar Dates Status

- **Requirement:** every date the user sees must render in the Persian Solar (Jalali) calendar — «۱۴۰۵/۰۵/۲۰», not «2026/08/11»
- **Helper:** `BookStore.UI/Services/PersianDateFormatter.cs` — static `Format(DateTime)` using the built-in `System.Globalization.PersianCalendar` (zero new packages, works in Blazor WASM): converts UTC → browser local time, renders `yyyy/MM/dd` in Jalali, then converts digits to Persian (`۰`–`۹`). ⚠️ Custom numeric formats do NOT apply `fa-IR`'s `NativeDigits` — the digit conversion is explicit
- **Usage:** the only two date displays in the app are the admin rows (`AdminUserRow` «عضویت: …», `AdminBookRow` creation date); both call `PersianDateFormatter.Format`. The helper is imported app-wide via `_Imports.razor` (`@using BookStore.UI.Services`)
- **Convention:** use `PersianDateFormatter.Format(...)` for any future date display; dates remain `DateTime` (UTC) in the API/domain — conversion happens only at render
- Accuracy verified against known anchors (2026-03-21 → ۱۴۰۵/۰۱/۰۱ · 2026-08-11 → ۱۴۰۵/۰۵/۲۰ · 1979-02-11 → ۱۳۵۷/۱۱/۲۲); a full sweep confirmed zero remaining Gregorian/date-format `ToString` calls in the UI

## Production Deployment (IIS / Shared Hosting)

- **Publish:** `dotnet publish BookStore.Api -c Release -o <deploy-folder>` — the WASM client's static assets (incl. `_framework`) are copied into the API's `wwwroot`, so the single-folder output is fully self-contained (see pitfall 5). The output includes `web.config` for the ASP.NET Core Module; in IIS just point the site's physical path at the deploy folder.
- **Runtime:** shared hosts without .NET 9 need a self-contained publish: `dotnet publish BookStore.Api -c Release -r win-x64 --self-contained true -o <deploy-folder>`. Otherwise the host must have the .NET 9 Hosting Bundle installed.
- **SQLite DB:** on first start the app runs `Database.Migrate()` and creates `bookstore.db` in the site folder (next to `web.config`) thanks to the path anchoring in `Program.cs` (pitfall 14). To keep existing data, copy the dev `BookStore.Api/bookstore.db` into the deploy folder before first start (site stopped).
- **Uploaded files:** uploads live under the site's `wwwroot/uploads/` (`covers/` public, `books/` gated via the auth branch + protected download endpoint). Copy the dev `BookStore.Api/wwwroot/uploads/` contents across to keep existing covers/books; the app creates the folders automatically.
- **Permissions:** the IIS AppPool identity needs write access (Modify) to the site folder — required for SQLite (DB file + journal) and `wwwroot/uploads`. Most shared-hosting control panels grant the site's AppPool write access to the user's site folder by default.
- **Secrets:** `BookStore.Api/appsettings.Production.json` holds a randomly generated `JwtSettings:Secret` (≥ 32 chars; validated at startup via `ValidateOnStart`) and is loaded automatically when `ASPNETCORE_ENVIRONMENT=Production` (the default under IIS). The file is **gitignored** — it must be copied to the server as part of the deploy (it is not in the repo), otherwise Production silently falls back to the dev secret in `appsettings.json`. For per-server control, override it with the environment variable `JwtSettings__Secret` (add `<environmentVariables>` to the deployed `web.config`) — env vars take precedence over the file. Rotate the secret by replacing the value (changing it invalidates previously issued JWTs).
- **HTTPS:** make sure the IIS site has an HTTPS binding (shared hosts usually terminate TLS there); `UseHttpsRedirection()` then no-ops.
- **Forwarded headers (TLS-terminating proxy):** `Program.cs` registers `UseForwardedHeaders` as the **first** middleware with `XForwardedFor | XForwardedProto | XForwardedHost` so `Request.Scheme`/`Host`/client-IP reflect the public https URL the browser sees — which is what keeps the Google OAuth `redirect_uri` and password-reset links correct behind the proxy (no hard-coded base-URL overrides needed). Trust is **loopback-only by default**; a remote proxy's IP/CIDR must be listed in the `ForwardedHeaders:KnownProxies`/`KnownNetworks` arrays (in `appsettings.json` or env vars like `ForwardedHeaders__KnownProxies__0=1.2.3.4`) or its `X-Forwarded-*` headers are ignored. Never clear the default trust (open-proxy spoofing). Unparseable `KnownProxies`/`KnownNetworks` entries are skipped silently at startup (no warning logged) — double-check the config if forwarded headers appear to be ignored. IIS ARR on the same host is already covered by the loopback default. The `GoogleOAuth:BaseUrl`/`PasswordReset:BaseUrl` overrides remain as a manual fallback when the proxy can't forward headers.
- **SMTP (password reset):** fill in `SmtpSettings:Host`/`Port`/`Username`/`Password`/`From` in the deployed `appsettings.Production.json` (or web.config env vars, e.g. `SmtpSettings__Host`) using your hosting provider's mail server - otherwise reset emails are only logged, never sent. Reset links use https automatically via the forwarded-headers setup above (manual fallback: `PasswordReset:BaseUrl`).
- **Post-deploy sanity check:** `/` serves the WASM app · `/api/books` returns the list · anonymous cover request = 200 · anonymous request to a book file under `/uploads/books/` = 401 · admin upload + download round-trips.
- **IIS blocks DELETE/PUT/PATCH with 405 by default:** shared hosts often enable the WebDAV module (or set `denyUnlistedVerbs`) which intercepts non-GET/POST verbs and returns "405 - HTTP verb used to access this page is not allowed" before the request reaches the app. `BookStore.Api/web.config` (in source, merged into every publish) removes `WebDAVModule`/`WebDAV` and explicitly allows `GET,HEAD,POST,PUT,PATCH,DELETE,OPTIONS` under `<requestFiltering><verbs>`. If the site still 405s, the server-level config wins over the app's web.config — ask the host to disable WebDAV for the site. Redeploying the zip (or just replacing the deployed `web.config`) applies the fix; IIS recycles the app pool on the change automatically.

## Bootstrap Admin Seeding (SeedAdmin)

- **Requirement:** the app needs a known admin account on every fresh deployment without manual DB edits
- **Mechanism:** `Program.cs` runs an idempotent `SeedAdminUser` right after `Database.Migrate()` in the startup scope. It reads `SeedAdmin:Email`/`SeedAdmin:Password` (plus optional `FirstName`/`LastName`) from configuration — **only when configured**. It creates the account (via `User.Create` + the production `PasswordHasher`, role `Admin`) only if no user has that email; it never updates/clobbers an existing row, so it is harmless on every publish
- **Config lives in `appsettings.Production.json`** (gitignored — the admin password must never be committed; env-var override: `SeedAdmin__Email`/`SeedAdmin__Password`). Development stays unaffected (no `SeedAdmin` section in `appsettings.json`)
- **Security note:** the seeded password sits in plaintext in the deployed `appsettings.Production.json`. If that file leaks, the admin account is exposed — rotate the password afterwards via the admin panel (SH-08) or by replacing the value and re-seeding

## Known Pitfalls (learned via smoke tests)

0. **Ephemeral DataProtection keys break auth cookies on shared hosts.** IIS app pools run without a user profile on shared hosting (Plesk, etc.), so ASP.NET Core DataProtection falls back to **in-memory keys** (`EphemeralXmlRepository` warning at startup) that die with the process. Any app-pool recycle between the Google challenge and its callback (or between issuing and validating any protected cookie) makes the cookie unreadable → `Correlation failed` → `/login?google_error=1`. Fix (in `Program.cs`): `AddDataProtection().SetApplicationName("BookStore").PersistKeysToFileSystem(<site>/keys)` — the `keys` folder is created on first run and must be fully writable by the app-pool identity.
   - ⚠️ **Plesk grants WRITE but not DELETE:** the key store rotates files via temp+delete, so `keys/` needs create/write/delete for the AppPool identity. If delete is denied, the FIRST Google challenge 500s with `UnauthorizedAccessException …\keys\….tmp … is denied` at `FileSystemXmlRepository.StoreElementCore` (only DP-touching endpoints break; JWT login/status keep working). Fix on the host: Plesk File Manager → `keys` → Permissions → grant the Web User **Modify** (or ask the host for Modify on the site folder). `Program.cs` now probes create+delete at startup and falls back to ephemeral keys with a clear `[DataProtection]` console warning instead of 500ing, so a permissions misconfiguration degrades to `google_error` rather than a site-wide 500.
   - Verify after deploy: startup log shows no `EphemeralXmlRepository` warning, no `[DataProtection] … not fully writable` line, and a `keys/key-*.xml` file exists.
   - Related: `stdoutLogEnabled="true"` is currently committed in `web.config` for production diagnostics (writes to the site's `\logs\` folder, which must exist) — flip it back to `false` once the OAuth issue is resolved if log files are unwanted.

1. **EF treats client-generated Guid keys as existing rows.** `Entity.Id = Guid.NewGuid()` is non-default, so when a new child (e.g. `RefreshToken`) is added to a tracked aggregate, EF marks it `Modified` → `UPDATE` affects 0 rows → `DbUpdateConcurrencyException`. Fix: in `UserRepository.Update`, snapshot tracked children with `AutoDetectChangesEnabled = false` first, then set untracked ones to `EntityState.Added`.
2. **PBKDF2 hashes are salted; never compare hash strings directly.** `HashPassword` returns a different string each call, so `user.PasswordHash != passwordHash` always fails. Verify via `IPasswordHasher.VerifyPassword(password, storedHash)` in the handler, then pass the stored hash to the domain service.
3. **JWT inbound claims are remapped by default.** `JwtSecurityTokenHandler` maps `sub`/`email` to `ClaimTypes.*`, so `FindFirstValue(JwtRegisteredClaimNames.Sub)` returns null. Fix: `options.MapInboundClaims = false` on the JwtBearer options.
4. **EF Core 9 `PendingModelChangesWarning` at runtime.** If `Database.Migrate()` throws "model has pending changes" even though `dotnet ef migrations has-pending-model-changes` says clean, the migration snapshot is stale — run `dotnet ef migrations remove` then `dotnet ef migrations add <Name>` again.
5. **Static web assets from the referenced UI project only load in Development.** `WebHost.ConfigureWebDefaults` calls `StaticWebAssetsLoader.UseStaticWebAssets` only when `IsDevelopment()`. Running the API with `--no-launch-profile` (no `ASPNETCORE_ENVIRONMENT=Development`) yields `Production` → `/` returns 404 and the WASM app is not served. The launch profiles set Development, so normal `dotnet run` works. For Production, deploy the `dotnet publish` output (assets are copied into `wwwroot/`).
6. **`LocalFileStorage` relative paths include the `BaseUrl` prefix; strip it before resolving to disk.** `SaveAsync` returns `uploads/books/<name>` (BaseUrl + subdir + name), so `GetFullPath` must strip the `BaseUrl` segment before `Path.Combine` with `RootPath` — otherwise it yields `wwwroot/uploads/uploads/...` and `DeleteAsync` silently no-ops. This bit the file cleanup on book delete/update.
7. **Blazor DI: register `AuthStateProvider` under both the concrete and base types.** `MainLayout` injects the concrete `AuthStateProvider` while the auth pipeline resolves `AuthenticationStateProvider`. `AddScoped<AuthenticationStateProvider, AuthStateProvider>()` alone fails at render time ("no registered service of type 'BookStore.UI.Services.AuthStateProvider'"). Fix: `AddScoped<AuthStateProvider>()` + `AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<AuthStateProvider>())`.
8. **Tokens in localStorage are raw strings, not JSON.** `AuthStateProvider` stores `auth_token`/`refresh_token` as plain strings, so `ClientStorageService.GetItemAsync<string>` must catch `JsonException` and return the raw value — otherwise any read of a stored token crashes the request (hit when the JWT bearer handler broke all API calls once a token existed).
9. **A `DelegatingHandler` handed to `new HttpClient(handler)` needs `InnerHandler` assigned** (e.g. `InnerHandler = new HttpClientHandler()`), otherwise every request throws `InvalidOperationException: The inner handler has not been assigned.` Also: don't register one handler instance and share it across several HttpClients — each HttpClient should own its private chain (create it in the HttpClient factory).
10. **Login `returnUrl` values must start with `/`.** `Login.razor` only honors `ReturnUrl` when it `StartsWith('/')`, so building one from `NavigationManager.ToBaseRelativePath` (which yields `admin`, no leading slash) silently sends the user home after login. Fix: prefix `"/"` before escaping (`Uri.EscapeDataString("/" + relativePath.TrimStart('/'))`).
11. **Blazor reuses a page instance when navigating between two URLs that match the same route template** (e.g. `/admin/edit/A` → `/admin/edit/B`), so `OnInitializedAsync` never re-runs and the page shows stale data. Fix: reload in `OnParametersSetAsync` keyed on a last-seen id (`if (Id != _loadedId) { _loadedId = Id; ... }`); child components with prefill state need the same id-guard (see `BookForm._prefilledForId`).
12. **Static files cannot serve `.epub` (or any extension missing from `FileExtensionContentTypeProvider`).** `StaticFileMiddleware` returns 404 for unknown types because `ServeUnknownFileTypes` defaults to `false` — so the original `<a href="/uploads/books/...">` download link never actually worked. The protected download endpoint is therefore the only delivery path for book files; the `/uploads/books` auth-branch still matters for known types (e.g. `.txt` book files).
13. **`PhysicalFile` requires a rooted path.** `IFileStorage.GetFullPath` returns a web-relative path (`wwwroot/uploads/...`); combine it with `IWebHostEnvironment.ContentRootPath` (`Path.GetFullPath(relPath, contentRoot)`) before passing it to `PhysicalFile`, or the request throws `NotSupportedException: path was not rooted`.
14. **Relative SQLite/upload paths break under IIS.** IIS apps do NOT run with the site folder as the working directory (in-process hosting runs inside `w3wp.exe`, whose CWD is `C:\Windows\System32\inetsrv`), so `Data Source=bookstore.db` and `FileStorage:RootPath=wwwroot/uploads` would resolve against the wrong directory — the DB would be created in `System32` (access denied) and uploads would be written outside the site. Fix: `Program.cs` anchors both config values to `ContentRootPath` at startup (`Path.GetFullPath(value, builder.Environment.ContentRootPath)`) before `AddInfrastructure` reads them. IIS sets the content root to the site's physical path, so the DB lands next to `web.config` and uploads land in the site's `wwwroot/uploads`. Verified by a Production-mode smoke test: published app run with a foreign working directory created the DB and wrote uploads under the content root only.

15. **IIS returns 405 for DELETE/PUT/PATCH verbs.** With the ASP.NET Core Module, IIS itself can reject non-GET/POST verbs before they reach Kestrel — the classic cause is the **WebDAV module** (common on shared hosts) or Request Filtering `denyUnlistedVerbs`. The symptom is the plain IIS error page "405 - HTTP verb used to access this page is not allowed" (not a ProblemDetails body). Fix: `BookStore.Api/web.config` removes `WebDAVModule`/`WebDAV` and whitelists the API verbs under `<security><requestFiltering><verbs>`; the SDK merges this source web.config into every publish. App-level routing can also return 405 (wrong verb for a matching path) — that's normal ASP.NET behavior and returns a JSON ProblemDetails body instead.

16. **Bootstrap upgrades silently reset component fonts to the system stack.** Bootstrap 5.3.3 sets `font-family` via CSS variables — `body` reads `--bs-body-font-family`, `.btn` reads `--bs-btn-font-family`, and `.tooltip`/`.popover` read `--bs-font-sans-serif` directly. A future Bootstrap version that renames these variables or hardcodes `font-family` in new components would make the app fall back to system fonts **without any build error**. Fix: after upgrading Bootstrap (see `wwwroot/lib/README.md`), re-verify `css/app.css` `:root` still overrides `--bs-font-sans-serif`, `--bs-body-font-family`, and `--bs-btn-font-family` to `var(--font-fa)`, then browser-check computed `font-family` on `.tooltip`, `.popover`, `.btn`, and `.form-control` — all must resolve to a stack starting with `PeydaWeb`. (Verified against Bootstrap 5.3.3: only those three variables exist; everything else inherits.)

17. **Razor's email heuristic turns a glued `@Identifier` into literal text.** In `.razor` markup, an `@` immediately preceded by a letter/digit and followed by an identifier (e.g. `کتاب@FilterSuffix`) is treated as an email address — the compiler emits `@FilterSuffix` as a plain-text string literal and the build stays 0 errors. Only a whitespace-preceded `@` transitions to code, which is why `مجموعهٔ @_totalCount` evaluated while the glued `کتاب@FilterSuffix` rendered verbatim ("مجموعهٔ 2 کتاب@FilterSuffix"). Fix: force the code transition with explicit parentheses — `کتاب@(FilterSuffix)`. Persian text is especially prone to this since words end in letters with no natural whitespace before the code. Verification: scan the compiled assembly for the literal — it lives in the #US heap as UTF-16 (`[System.Text.Encoding]::Unicode.GetString($bytes).IndexOf('@FilterSuffix')`); an ASCII scan only finds the metadata getter name `get_FilterSuffix` and is misleading (UI assembly: `BookStore.UI/bin/Debug/net9.0/BookStore.UI.dll`).

## MVP Status (feature codes)

Features are tracked in `book/MVP Scope Document.md` with stable **codes** for prompt references: `MH-*` (MUST), `SH-*` (SHOULD), `CH-*` (COULD), `WH-*` (WON'T) — e.g. «implement SH-02». Build is clean (0 warnings / 0 errors).

- **MUST HAVE — 11/11 done (MH-01..MH-12):** ثبت‌نام/ورود/خروج (`Register.razor`, `Login.razor`, `MainLayout.razor`) · Home + books list + details with دانلود/افزودن به کتابخانه (`Home.razor`, `BooksList.razor`, `BookDetails.razor`) · افزودن به کتابخانه + لیست کتاب‌های کاربر (`BookDetails.razor`, `MyLibrary.razor`) · افزودن/ویرایش/حذف کتاب (ادمین) (`AdminBooks.razor`, `AdminBookAdd.razor`, `AdminBookEdit.razor`) · هدایت هوشمند ناشناس — anonymous → login → auto-add / auto-download (`BookDetails.razor` pending flows)
- **SHOULD HAVE — 10/10 done:** `SH-01` حذف از کتابخانه · `SH-02` جستجو · `SH-03` صفحه‌بندی · `SH-04` بازیابی رمز عبور · `SH-05` فعال/غیرفعال کردن کتاب · `SH-06` پیام‌های عملیاتی (Toast) · `SH-07` مدیریت کاربران · `SH-08` تغییر رمز عبور · `SH-09` ناوبری واکنش‌گرا · `SH-10` تغییر/بازنشانی رمز عبور توسط ادمین
- **COULD HAVE — 3/5 done:** `CH-01` دسته‌بندی · `CH-03` یادداشت شخصی · `CH-04` اشتراک‌گذاری لینک کتاب — remaining: `CH-02` تعداد دفعات اضافه‌شده، `CH-05` آمار بازدید
- **EXTRA (no feature code):** ورود/ثبت‌نام با گوگل (Google OAuth, see its status section) · رابط تمام‌فارسی (Farsi Localization Status) · تاریخ شمسی (Persian Solar Calendar Dates Status)

## Next Steps (remaining features)

SHOULD HAVE: none remaining (10/10 complete)
COULD HAVE: `CH-02` نمایش تعداد دفعات اضافه‌شده · `CH-05` آمار بازدید

### Migrations

- Design-time factory `BookStoreDbContextFactory` in `Infrastructure/Persistence/` (SQLite `Data Source=bookstore.db`)
- Run from solution root:
  - `dotnet ef migrations add <Name> --project BookStore.Infrastructure --startup-project BookStore.Infrastructure`
  - `dotnet ef database update --project BookStore.Infrastructure --startup-project BookStore.Infrastructure`
- `bookstore.db` is created in `BookStore.Api/` at runtime (dev only, not for source control)
- The design-time factory (`--startup-project BookStore.Infrastructure`) creates a separate throwaway DB next to it — delete it after use; only the `BookStore.Api/` instance is needed
- EF Core pinned to 9.x (v10 targets net10.0); tool: `dotnet-ef` 9.0.17 (global); SDK 9.0.304