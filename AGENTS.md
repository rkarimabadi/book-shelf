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
- **Structure**: `Pages/` (Home, Login, Register, ForgotPassword, ResetPassword), `Layout/` (MainLayout + scoped CSS, Persian RTL), `Services/` (`AuthenticationService` + `IAuthenticationService`, `AuthStateProvider`, `ClientStorageService` + `IClientStorageService`, `AuthenticatedHttpClientHandler`, `ProblemDetailsParser` shared helper), `Features/` (feature-driven: `Books/` and `Admin/` each with `Pages/` + `Components/` + `Services/`; `Library/` with `Pages/` only, reusing Books' components/services), `Shared/Components/` (LoadingSpinner, EmptyState, ErrorNotice, ConfirmDialog, AccessDenied), `wwwroot/` (index.html, css/app.css, bootstrap in lib/)
- **Auth flow**: login/register post to the API, then `AuthStateProvider.SignInAsync` persists `auth_token`/`refresh_token` in localStorage (`IJSRuntime`) and raises `NotifyAuthenticationStateChanged`. `AuthStateProvider.ParseToken` decodes the JWT payload (base64url) and builds `ClaimsPrincipal` (email/role/given_name/family_name); expired tokens → anonymous.
- `MainLayout` uses `<AuthorizeView>` (needs `AddAuthorizationCore` + `AddCascadingAuthenticationState` in `Program.cs`) to switch between "ورود/ثبتنام" links and the logged-in user + logout button. Nav links: `کتابخانه` for every authenticated user (inside `<AuthorizeView>`), and admin-only `مدیریت` inside `<AuthorizeView Roles="Admin">`.
- `App.razor` uses `<AuthorizeRouteView>` (not plain `RouteView`) with a `<NotAuthorized>` template → `AccessDenied` component: anonymous users are redirected to `/login?returnUrl=...`; authenticated non-admins see a Persian access-denied message. Admin pages carry `@attribute [Authorize(Roles = "Admin")]`.
- PWA capabilities (planned)

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
- **API:** `POST /api/auth/forgot-password` (always 204 - no user enumeration; reset link base = `Request.Scheme://Request.Host/reset-password`, overridable via `PasswordReset:BaseUrl` for TLS-terminating proxies) - `POST /api/auth/reset-password` (204 on success; 400 expired/used, 404 invalid token)
- **UI:** `ForgotPassword.razor` (`/forgot-password`, generic success message) + `ResetPassword.razor` (`/reset-password?email=&token=` - new password + confirm), forgot-password link on `Login.razor`; auth-card styling + `.auth-success`/`.auth-links`/`.auth-button-link` in `app.css`
- Verified E2E with a fake SMTP sink: forgot -> 204, email captured with the link, reset -> 204, login with new password -> 200, old password -> 401, token replay -> 400, unknown-email forgot -> 204, re-forgot invalidates the previous token

## Change Password Status (SH-08 - تغییر رمز عبور)

- **Domain (Core):** `User.ChangePassword(newPasswordHash)` — rejects inactive users, sets the new hash, revokes ALL refresh tokens (forces re-login) and fires `PasswordChangedEvent`. The current-password verification lives in the handler because PBKDF2 hashes are salted (pitfall 2) — the domain cannot re-verify a plaintext password.
- **Application:** `Features/Authentication/Commands/ChangePassword/` (command + validator + handler): handler loads the user via `IAuthenticationService.GetUserByEmail`, verifies the current password with `IPasswordHasher.VerifyPassword(current, storedHash)` (mismatch → 400 `User.InvalidCurrentPassword`), hashes the new password, calls `IAuthenticationService.ChangePassword(email, newHash)` and persists via `IUnitOfWork`
- **API:** `POST /api/auth/change-password` (`[Authorize(Policy = Policies.RequireUserRole)]`) — identity taken from the JWT `email` claim, not the body; 204 on success; 401 if the claim is missing
- **UI:** `ChangePassword.razor` (`/change-password`, `[Authorize]` → anonymous users hit `/login`): current/new/confirm fields with client-side validation, maps the wrong-current-password error to «رمز عبور فعلی اشتباه است.», and after success signs the user out locally (`LogoutAsync` + redirect to `/login`) since the server revoked every refresh token. «تغییر رمز» nav link added to `MainLayout` inside the `<AuthorizeView>` (all authenticated users)
- No migration required (no schema change)
- Verified via API smoke test: anonymous → 401 · wrong current password → 400 with `User.InvalidCurrentPassword` · change → 204 · old-password login → 401 · new-password login → 200 · refresh with the pre-change token → 401 (revoked, proving forced re-login)

## Admin User Management Status (SH-07 - مدیریت کاربران)

- **Domain (Core):** `User.ChangeRole(role)` (same-role → 409 `User.RoleAlreadySet`, invalid → 400 `User.InvalidRole`, fires `UserRoleChangedEvent`); `User.Deactivate()` now also revokes every refresh token so a blocked account loses its sessions. Errors: `User.NotFound` (by id), `User.CannotModifySelf` (409). Removed the unused `DeactivateUser(email)`/`ActivateUser(email)` service methods — handlers call `IUserRepository` directly (same pattern as `SetBookStatusCommand`)
- **Application:** `Features/Users/` — `Queries/GetUsers/` (list all, newest-first) + `Commands/SetUserStatus/`, `Commands/SetUserRole/`, `Commands/DeleteUser/` (command + validator + handler each). Handlers: load via `IUserRepository.GetByIdAsync`, block any mutation of the caller's own account (`CannotModifySelf`), persist via `IUnitOfWork`; delete relies on FK cascade for refresh/password-reset tokens and library entries (books untouched)
- **Infrastructure:** `UserRepository.GetUsersAsync` (AsNoTracking, ordered by `CreatedAt` desc) — `GetByIdAsync`/`Delete` already existed
- **API:** `UsersController` (`/api/users`, every endpoint `[Authorize(Policy = Policies.RequireAdminRole)]`): `GET` list → `UserResponse` · `PATCH {id}/status` (body `{ isActive }`) → 204 · `PATCH {id}/role` (body `{ role }`, invalid → 400) → 204 · `DELETE {id}` → 204. Current admin id from JWT `sub` claim (missing → 401)
- **UI:** `Features/Admin/Pages/AdminUsers.razor` (`/admin/users`, `[Authorize(Roles = "Admin")]`): rows with avatar initials, name/email/join date, role badge + ارتقا/تبدیل button, مسدود/فعال toggle, حذف with `ConfirmDialog`; the admin's own row shows «حساب شما» with no actions. `IAdminUserService`/`AdminUserService` (401 → login redirect, 403 → Persian denial, Persian mapping for `User.NotFound`/`CannotModifySelf`/`RoleAlreadySet`); «کاربران» nav link added for admins. `AuthStateProvider.ParseToken` now also emits the `sub` claim as `ClaimTypes.NameIdentifier` (used to detect the self-row)
- No migration required (no schema change)
- Verified via API smoke test: anon list 401 · non-admin list 403 · admin list 200 · deactivate 204 (+ blocked user's login → 401, `IsActive=false` in list) · reactivate 204 · role change 204 · same role again 409 · self status/role/delete all 409 · missing user 404 · invalid role 400 · delete 204 (user vanishes from list) · DB check: zero orphaned `RefreshToken`/`LibraryEntry` rows after delete (cascade works)

## Public Books UI Status

- **Pages:** `Features/Books/Pages/BooksList.razor` (`/books` — responsive card grid, skeleton loading, EmptyState/ErrorNotice states), `BookDetails.razor` (`/books/{id:guid}` — cover, title, author, description, دانلود button, auth-aware افزودن به کتابخانه)
- **Components:** `BookCard` (presentational, hover lift), `BookCover` (img with graceful placeholder fallback), shared `LoadingSpinner`/`EmptyState`/`ErrorNotice` in `Shared/Components/`; all styled via CSS isolation + design tokens
- **Services:** `IBookService`/`BookService` (books list with 30 s client cache, detail, library-membership check, add-to-library), `IClientStorageService`/`ClientStorageService` (localStorage behind an interface), `AuthenticatedHttpClientHandler` (attaches the stored JWT as a Bearer header; each HttpClient owns its private handler chain)
- **Share (CH-04):** «کپی لینک» button on `BookDetails.razor` copies the current book URL via `window.bookstoreCopyText` (Clipboard API with a hidden-textarea `execCommand` fallback for non-secure contexts) and shows «لینک کتاب کپی شد.»; when the browser supports the Web Share API a «اشتراک‌گذاری» button also appears (`window.bookstoreShare` → native share sheet with title + URL; resolves false on cancel so no error noise). JS helpers live in `index.html` next to `bookstoreDownload`.
- **Details-page auth flow:** anonymous click on افزودن → stores pending bookId in localStorage → redirects to `/login?returnUrl=/books/{id}` → after login/register returns to the book → auto-adds and shows a success notice; 409 duplicate is mapped to a friendly Persian message; if already in library the button is disabled («در کتابخانهٔ شماست»); a 401 after a fresh login shows the error instead of looping back to login
- **DI:** `AddScoped<IClientStorageService>` + HttpClient factory (with auth handler) + `AddScoped<IBookService>` in `BookStore.UI/Program.cs`
- Verified via browser smoke test: list + details render, anonymous→login→auto-add E2E, 409/401 handled gracefully

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

## Production Deployment (IIS / Shared Hosting)

- **Publish:** `dotnet publish BookStore.Api -c Release -o <deploy-folder>` — the WASM client's static assets (incl. `_framework`) are copied into the API's `wwwroot`, so the single-folder output is fully self-contained (see pitfall 5). The output includes `web.config` for the ASP.NET Core Module; in IIS just point the site's physical path at the deploy folder.
- **Runtime:** shared hosts without .NET 9 need a self-contained publish: `dotnet publish BookStore.Api -c Release -r win-x64 --self-contained true -o <deploy-folder>`. Otherwise the host must have the .NET 9 Hosting Bundle installed.
- **SQLite DB:** on first start the app runs `Database.Migrate()` and creates `bookstore.db` in the site folder (next to `web.config`) thanks to the path anchoring in `Program.cs` (pitfall 14). To keep existing data, copy the dev `BookStore.Api/bookstore.db` into the deploy folder before first start (site stopped).
- **Uploaded files:** uploads live under the site's `wwwroot/uploads/` (`covers/` public, `books/` gated via the auth branch + protected download endpoint). Copy the dev `BookStore.Api/wwwroot/uploads/` contents across to keep existing covers/books; the app creates the folders automatically.
- **Permissions:** the IIS AppPool identity needs write access (Modify) to the site folder — required for SQLite (DB file + journal) and `wwwroot/uploads`. Most shared-hosting control panels grant the site's AppPool write access to the user's site folder by default.
- **Secrets:** `BookStore.Api/appsettings.Production.json` holds a randomly generated `JwtSettings:Secret` (≥ 32 chars; validated at startup via `ValidateOnStart`) and is loaded automatically when `ASPNETCORE_ENVIRONMENT=Production` (the default under IIS). The file is **gitignored** — it must be copied to the server as part of the deploy (it is not in the repo), otherwise Production silently falls back to the dev secret in `appsettings.json`. For per-server control, override it with the environment variable `JwtSettings__Secret` (add `<environmentVariables>` to the deployed `web.config`) — env vars take precedence over the file. Rotate the secret by replacing the value (changing it invalidates previously issued JWTs).
- **HTTPS:** make sure the IIS site has an HTTPS binding (shared hosts usually terminate TLS there); `UseHttpsRedirection()` then no-ops.
- **SMTP (password reset):** fill in `SmtpSettings:Host`/`Port`/`Username`/`Password`/`From` in the deployed `appsettings.Production.json` (or web.config env vars, e.g. `SmtpSettings__Host`) using your hosting provider's mail server - otherwise reset emails are only logged, never sent. If the host terminates TLS at a proxy, set `PasswordReset:BaseUrl` to the public https URL so reset links use https.
- **Post-deploy sanity check:** `/` serves the WASM app · `/api/books` returns the list · anonymous cover request = 200 · anonymous request to a book file under `/uploads/books/` = 401 · admin upload + download round-trips.
- **IIS blocks DELETE/PUT/PATCH with 405 by default:** shared hosts often enable the WebDAV module (or set `denyUnlistedVerbs`) which intercepts non-GET/POST verbs and returns "405 - HTTP verb used to access this page is not allowed" before the request reaches the app. `BookStore.Api/web.config` (in source, merged into every publish) removes `WebDAVModule`/`WebDAV` and explicitly allows `GET,HEAD,POST,PUT,PATCH,DELETE,OPTIONS` under `<requestFiltering><verbs>`. If the site still 405s, the server-level config wins over the app's web.config — ask the host to disable WebDAV for the site. Redeploying the zip (or just replacing the deployed `web.config`) applies the fix; IIS recycles the app pool on the change automatically.

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
10. **Login `returnUrl` values must start with `/`.** `Login.razor` only honors `ReturnUrl` when it `StartsWith('/')`, so building one from `NavigationManager.ToBaseRelativePath` (which yields `admin`, no leading slash) silently sends the user home after login. Fix: prefix `"/"` before escaping (`Uri.EscapeDataString("/" + relativePath.TrimStart('/'))`).
11. **Blazor reuses a page instance when navigating between two URLs that match the same route template** (e.g. `/admin/edit/A` → `/admin/edit/B`), so `OnInitializedAsync` never re-runs and the page shows stale data. Fix: reload in `OnParametersSetAsync` keyed on a last-seen id (`if (Id != _loadedId) { _loadedId = Id; ... }`); child components with prefill state need the same id-guard (see `BookForm._prefilledForId`).
12. **Static files cannot serve `.epub` (or any extension missing from `FileExtensionContentTypeProvider`).** `StaticFileMiddleware` returns 404 for unknown types because `ServeUnknownFileTypes` defaults to `false` — so the original `<a href="/uploads/books/...">` download link never actually worked. The protected download endpoint is therefore the only delivery path for book files; the `/uploads/books` auth-branch still matters for known types (e.g. `.txt` book files).
13. **`PhysicalFile` requires a rooted path.** `IFileStorage.GetFullPath` returns a web-relative path (`wwwroot/uploads/...`); combine it with `IWebHostEnvironment.ContentRootPath` (`Path.GetFullPath(relPath, contentRoot)`) before passing it to `PhysicalFile`, or the request throws `NotSupportedException: path was not rooted`.
14. **Relative SQLite/upload paths break under IIS.** IIS apps do NOT run with the site folder as the working directory (in-process hosting runs inside `w3wp.exe`, whose CWD is `C:\Windows\System32\inetsrv`), so `Data Source=bookstore.db` and `FileStorage:RootPath=wwwroot/uploads` would resolve against the wrong directory — the DB would be created in `System32` (access denied) and uploads would be written outside the site. Fix: `Program.cs` anchors both config values to `ContentRootPath` at startup (`Path.GetFullPath(value, builder.Environment.ContentRootPath)`) before `AddInfrastructure` reads them. IIS sets the content root to the site's physical path, so the DB lands next to `web.config` and uploads land in the site's `wwwroot/uploads`. Verified by a Production-mode smoke test: published app run with a foreign working directory created the DB and wrote uploads under the content root only.

15. **IIS returns 405 for DELETE/PUT/PATCH verbs.** With the ASP.NET Core Module, IIS itself can reject non-GET/POST verbs before they reach Kestrel — the classic cause is the **WebDAV module** (common on shared hosts) or Request Filtering `denyUnlistedVerbs`. The symptom is the plain IIS error page "405 - HTTP verb used to access this page is not allowed" (not a ProblemDetails body). Fix: `BookStore.Api/web.config` removes `WebDAVModule`/`WebDAV` and whitelists the API verbs under `<security><requestFiltering><verbs>`; the SDK merges this source web.config into every publish. App-level routing can also return 405 (wrong verb for a matching path) — that's normal ASP.NET behavior and returns a JSON ProblemDetails body instead.

## MVP Status (feature codes)

Features are tracked in `book/MVP Scope Document.md` with stable **codes** for prompt references: `MH-*` (MUST), `SH-*` (SHOULD), `CH-*` (COULD), `WH-*` (WON'T) — e.g. «implement SH-02». Build is clean (0 warnings / 0 errors).

- **MUST HAVE — 11/11 done (MH-01..MH-12):** ثبت‌نام/ورود/خروج (`Register.razor`, `Login.razor`, `MainLayout.razor`) · Home + books list + details with دانلود/افزودن به کتابخانه (`Home.razor`, `BooksList.razor`, `BookDetails.razor`) · افزودن به کتابخانه + لیست کتاب‌های کاربر (`BookDetails.razor`, `MyLibrary.razor`) · افزودن/ویرایش/حذف کتاب (ادمین) (`AdminBooks.razor`, `AdminBookAdd.razor`, `AdminBookEdit.razor`) · هدایت هوشمند ناشناس — anonymous → login → auto-add / auto-download (`BookDetails.razor` pending flows)
- **SHOULD HAVE — 5/8 done:** `SH-01` حذف از کتابخانه · `SH-04` بازیابی رمز عبور · `SH-05` فعال/غیرفعال کردن کتاب · `SH-07` مدیریت کاربران · `SH-08` تغییر رمز عبور — remaining: `SH-02` جستجو، `SH-03` صفحه‌بندی، `SH-06` Toast/Notification
- **COULD HAVE — 1/5 done:** `CH-04` اشتراک‌گذاری لینک کتاب — remaining: `CH-01` دسته‌بندی، `CH-02` تعداد دفعات اضافه‌شده، `CH-03` یادداشت شخصی، `CH-05` آمار بازدید

## Next Steps (remaining features)

SHOULD HAVE: `SH-02` جستجوی ساده (title search box on `/books`, client-side filter) · `SH-03` صفحه‌بندی (pagination for the books list) · `SH-06` پیام‌های عملیاتی (Toast/Notification component)
COULD HAVE: `CH-01` دسته‌بندی (دسته/ژانر) · `CH-02` نمایش تعداد دفعات اضافه‌شده · `CH-03` یادداشت‌نویسی شخصی · `CH-05` آمار بازدید

### Migrations

- Design-time factory `BookStoreDbContextFactory` in `Infrastructure/Persistence/` (SQLite `Data Source=bookstore.db`)
- Run from solution root:
  - `dotnet ef migrations add <Name> --project BookStore.Infrastructure --startup-project BookStore.Infrastructure`
  - `dotnet ef database update --project BookStore.Infrastructure --startup-project BookStore.Infrastructure`
- `bookstore.db` is created in `BookStore.Api/` at runtime (dev only, not for source control)
- The design-time factory (`--startup-project BookStore.Infrastructure`) creates a separate throwaway DB next to it — delete it after use; only the `BookStore.Api/` instance is needed
- EF Core pinned to 9.x (v10 targets net10.0); tool: `dotnet-ef` 9.0.17 (global); SDK 9.0.304