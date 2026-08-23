# BookStore — High-Level Architecture Analysis

**Date:** 2026-08-15 · **Scope:** whole solution (BookStore.sln) · **Based on:** current source, not the design docs

---

## 1. Executive Summary

BookStore is a **digital library / e-book sharing web application** (Persian RTL UI) built as a single-deployment **hosted Blazor WebAssembly** app on **.NET 9**. The backend follows **Clean Architecture with Domain-Driven Design (DDD)** across five projects; the frontend is a feature-organized Blazor WASM client that is referenced by — and served from — the same API host, giving one origin, one publish folder, and no CORS.

The architecture's defining decisions:

| Decision | Choice |
|---|---|
| Backend style | Clean Architecture + DDD (rich domain model, aggregates) |
| CQRS-ish orchestration | MediatR (commands/queries + handlers), vertical feature folders |
| Error handling | `ErrorOr<T>` result pattern everywhere — no business exceptions |
| Validation | FluentValidation run automatically by a pipeline behavior |
| Persistence | EF Core 9 + SQLite, repository + UnitOfWork |
| Cross-cutting events | Domain events → Outbox table → Quartz job → MediatR publish (30 s) |
| Auth | Custom stack (no ASP.NET Identity): PBKDF2 hasher + JWT + rotating refresh tokens |
| File storage | Local disk under `wwwroot/uploads/` (`covers/` public, `books/` gated) |
| Frontend | Blazor WebAssembly, hosted model, feature-first folders, scoped CSS |

**Maturity assessment:** MVP is functionally complete (11/11 MUST, 6/9 SHOULD, 1/5 COULD per `book/MVP Scope Document.md`), the build is clean (0 warnings / 0 errors), and each feature was verified via manual API smoke tests. The codebase is notably disciplined — the layering rules in AGENTS.md are consistently honored in the source.

---

## 2. Solution Structure & Project References

```
BookStore.sln
├── BookStore.Core            (domain — zero external deps beyond ErrorOr/GuardClauses)
├── BookStore.Application     (use cases — MediatR commands/queries, validators, interfaces)
├── BookStore.Contracts       (shared DTOs — records consumed by API + UI)
├── BookStore.Infrastructure  (EF Core, SQLite, JWT, SMTP, file storage, Outbox, Quartz)
├── BookStore.Api             (ASP.NET Core Web API host + serves the WASM client)
└── BookStore.UI              (Blazor WebAssembly client)
```

Reference graph (dependency direction points to what a project may use):

```
BookStore.Contracts   (leaf — referenced by Api + UI)
        ▲
BookStore.Core        (leaf — referenced by Application + Infrastructure)
        ▲
BookStore.Application (→ Core, Contracts*)
        ▲
BookStore.Infrastructure (→ Application, Core)
        ▲
BookStore.Api         (→ Infrastructure, Application, Contracts, UI)
BookStore.UI          (→ Contracts only)
```

- `BookStore.Core` depends only on `ErrorOr` + `Ardalis.GuardClauses` — pure domain, no EF/web/ORM.
- `BookStore.Application` references Core only; all external needs (clock, JWT, hashing, email, storage, persistence) are **interfaces defined in `Application/Common/Interfaces/`** and implemented in Infrastructure (dependency inversion).
- `BookStore.Infrastructure` references Application + Core; it hosts EF Core, migrations, JWT/SMTP/storage implementations, and background jobs.
- `BookStore.Contracts` is a standalone DTO library shared by the API and the WASM client — it decouples the UI from the API's internals.
- `BookStore.UI` is a `Microsoft.NET.Sdk.BlazorWebAssembly` project referenced by the API (`Microsoft.AspNetCore.Components.WebAssembly.Server`), so `dotnet publish BookStore.Api` produces one self-contained folder with the WASM assets under `wwwroot/_framework/`.

**Notable:** dependencies flow in one direction; there is no upward or sideways reference violation visible in the source. This is the textbook shape of Clean Architecture done with project boundaries instead of folders.

---

## 3. Layer-by-Layer Analysis

### 3.1 BookStore.Core — Domain Layer

Two aggregate roots and supporting entities:

```
AggregateRoot
├── User            → RefreshToken[], PasswordResetToken[], LibraryEntry[]
└── Book            (leaf — content + status only)
```

**Patterns honored (per AGENTS.md):**
- **Rich domain model** — state changes only via business methods (`Login()`, `ChangePassword()`, `AddToLibrary()`, `ResetPassword()`, `Deactivate()`, `UpdateDetails()`…). Private setters throughout.
- **Result pattern** — factories and methods return `ErrorOr<T>`; errors are typed (`Error.Validation/NotFound/Conflict/Unauthorized`) with stable codes (`User.BookNotInLibrary`, `Book.FileRequired`, …). No exceptions for business rules.
- **Domain events** — each state change raises an event (`UserCreatedEvent`, `BookDeactivatedEvent`, …) collected in the aggregate's `_domainEvents` list. The domain never publishes them itself.
- **Encapsulated collections** — children are `private readonly List<T>` exposed as `IReadOnlyCollection`, mutated only through aggregate methods (e.g. `AddRefreshToken`, `AddToLibrary`).
- **Input guards** — `Ardalis.GuardClauses` + a custom `GuardClauseExtensions.ExpiresInPast`.

**Domain services:** `AuthenticationService` (per-user auth workflows) with contract `IAuthenticationService` — both live in Core because authentication is domain logic here (custom auth stack, not Identity).

**Observation:** `User` carries three child collections (refresh tokens, reset tokens, library entries) — a large aggregate. It works because library/books are separate aggregates referenced by id only (no EF navigation into `Book` from `User`), keeping transaction boundaries clean.

### 3.2 BookStore.Application — Application Layer

**Vertical slices (feature folders):** `Features/Authentication/`, `Features/Books/`, `Features/Library/`, `Features/Users/` — each command/query is one file containing the request + FluentValidation validator + handler.

**CQRS-lite via MediatR:**
- Commands/queries are `IRequest<ErrorOr<T>>`, each with exactly one handler.
- `ValidationBehavior<TRequest, TResponse>` (an `IPipelineBehavior`) runs validators automatically — handlers never validate inline.
- Handlers orchestrate: load aggregate via repository → call domain method → persist via `IUnitOfWork` → return result. No business logic in handlers (e.g. `CreateBookCommandHandler` just calls `Book.Create`, adds, saves).

**Dependency inversion contracts** (`Common/Interfaces/`): `IDateTimeProvider`, `IJwtTokenGenerator`, `IPasswordHasher`, `IFileStorage`, `IEmailSender`, `IUnitOfWork` — all implemented in Infrastructure.

**Security metadata** (`Common/Security/Roles.cs`): `Roles.Admin`/`Roles.User` and `Policies.RequireAdminRole`/`Policies.RequireUserRole`. Notably `RequireUserRole` = "registered account" and admits both `User` and `Admin` — a deliberate modeling choice (admins can also use the library and download books).

**Observation:** one file per feature keeps a feature's surface in one place, but some files mix three responsibilities (record + validator + handler). Acceptable at this scale; a strict reviewer might split them.

### 3.3 BookStore.Infrastructure — Infrastructure Layer

**Persistence (EF Core 9 + SQLite):**
- `BookStoreDbContext` + Fluent API configurations per entity (`UserConfiguration`, `BookConfiguration`, `RefreshTokenConfiguration`, `PasswordResetTokenConfiguration`, `LibraryEntryConfiguration`, `OutboxMessageConfiguration`).
- Repositories: `UserRepository`, `BookRepository` implementing Core contracts; `UnitOfWork` wraps `SaveChangesAsync` (single DB context → one transaction per use case).
- Migrations: InitialCreate → AddBooks → AddLibraryEntries → AddPasswordResetTokens → AddBookIsActive (5 total).
- Design-time factory (`BookStoreDbContextFactory`) for `dotnet ef` commands.

**Authentication implementations:** `PasswordHasher` (PBKDF2), `JwtTokenGenerator`, `JwtSettings` (Options + `ValidateOnStart` — secret ≥ 32 chars).

**Email:** `SmtpEmailSender` (System.Net.Mail, zero new packages) with a **log-fallback** when `SmtpSettings:Host` is empty — dev convenience that must be configured in production.

**File storage:** `LocalFileStorage` writes to `wwwroot/uploads/{covers|books}`; returns web-relative paths (`uploads/books/<name>`); `GetFullPath` strips the BaseUrl prefix before resolving to disk (documented pitfall 6).

**Outbox pattern:**
- `PublishDomainEventsInterceptor` (SaveChangesInterceptor) drains aggregate `_domainEvents` into the `OutboxMessages` table in the same transaction as the business change (atomicity).
- `ProcessOutboxMessagesJob` (Quartz, every 30 s, batch of 20, `[DisallowConcurrentExecution]`) deserializes and publishes via MediatR `DomainEventNotification`. Failures are recorded on the row (`Error` column), not lost — the message stays unprocessed and is retried.

**Observation:** the Outbox is the one piece of "enterprise-grade" machinery in an otherwise lean stack — a good fit for future multi-service growth, though currently the only real consumer is email/notification-style side effects. The 30 s window is fine for these workloads.

### 3.4 BookStore.Api — Presentation / Host

- **Program.cs:** path anchoring for SQLite + upload root under IIS (pitfall 14), `AddApplication()` + `AddInfrastructure()`, controllers, Swagger (dev only), JWT bearer (with `MapInboundClaims = false`, `RoleClaimType = "role"`), two authorization policies, static files + WASM fallback.
- **Static file gating:** `UseStaticFiles()` runs **after** `UseAuthentication()`; a `UseWhen("/uploads/books")` branch denies anonymous access (401) while `/uploads/covers` stays public. The download of book files goes through the authenticated endpoint instead (see below).
- **Controllers (thin):** `ApiController` base maps `ErrorOr` errors to ProblemDetails (`Problem(errors)`). Controllers do request mapping (Mapster), send commands, and map results to Contract DTOs. They contain no business logic.
  - `AuthController` — register/login/refresh/logout/forgot/reset/change-password/me (public + policy-protected).
  - `BooksController` — CRUD (admin-only writes), list/detail (public), **`GET /{id}/download`** (policy-protected, returns `PhysicalFile` rooted at ContentRootPath — pitfall 13). Multipart upload handled here (file saved via `IFileStorage` before the command).
  - `LibraryController` — add/remove/list, user identity from JWT `sub`.
  - `UsersController` — admin user management (list/status/role/delete), self-modification blocked with 409.
- **Policies enforced on endpoints** (`RequireAdminRole` on writes; `RequireUserRole` on library/download/me). No authorization pipeline behavior — attribute-level only.

### 3.5 BookStore.Contracts — Shared DTOs

Immutable records mirroring API requests/responses (`RegisterRequest`, `LoginRequest`, `BookResponse`, `AuthenticationResponse`, `UserResponse`, `LibraryBookResponse`, …). Referenced by both API and UI so the client can bind without any API project reference.

### 3.6 BookStore.UI — Blazor WebAssembly Client

**Structure:** feature-first folders mirroring the backend features (`Features/Books/`, `Features/Admin/`, `Features/Library/`), plus `Pages/`, `Layout/`, `Shared/Components/`, `Services/`.

**Pages:**
- Public: Home, Login, Register, ForgotPassword, ResetPassword, ChangePassword, BooksList, BookDetails.
- Library: MyLibrary (authenticated).
- Admin: AdminBooks (`/admin`), AdminBookAdd, AdminBookEdit, AdminUsers — all `[Authorize(Roles = "Admin")]`.

**Auth plumbing (custom, no Identity):**
- `AuthStateProvider` — persists `auth_token`/`refresh_token` in localStorage (raw strings, not JSON — pitfall 8), decodes the JWT payload (base64url) into a `ClaimsPrincipal`, signs in/out, raises `NotifyAuthenticationStateChanged`.
- `AuthenticatedHttpClientHandler` — a `DelegatingHandler` attaching the stored JWT as a Bearer header; each HttpClient owns its private handler chain (pitfall 9).
- `MainLayout` uses `<AuthorizeView>`; `App.razor` uses `<AuthorizeRouteView>` with a `<NotAuthorized>` template redirecting anonymous users to `/login?returnUrl=...` and showing an access-denied message for authenticated non-admins.
- DI registers `AuthStateProvider` under both concrete and base types (pitfall 7).

**Services:** `AuthenticationService`, `BookService` (30 s client cache), `AdminBookService`/`AdminUserService` (multipart + status + role ops), `ClientStorageService`, and a shared `ProblemDetailsParser` mapping API errors to friendly Persian messages.

**UX patterns worth noting:** skeleton loading, EmptyState/ErrorNotice/ConfirmDialog shared components, pending-action flows (anonymous → login → auto-add to library / auto-download), RTL scoped CSS, responsive hamburger nav (SH-09).

---

## 4. Request Flow (End-to-End Example)

**Admin creates a book (POST /api/books):**

```
Browser (WASM)                          API Host
─────────────────────────────────────────────────────────────
AdminBookAdd.razor
  → AdminBookService.CreateAsync (multipart, JWT header)
        │
        ▼
  BooksController.Create                     [Authorize(Policy=RequireAdminRole)]
    ├─ IFileStorage.SaveAsync → wwwroot/uploads/books/<name>
    ├─ CreateBookCommand ── MediatR ──► CreateBookCommandHandler
    │     ├─ Book.Create(...)  → domain rules, raises BookCreatedEvent
    │     ├─ _bookRepository.Add(book)
    │     └─ _unitOfWork.SaveChangesAsync
    │           ├─ EF persists Book row
    │           └─ PublishDomainEventsInterceptor writes OutboxMessage (same tx)
    └─ Problem/BookResponse (201 Created)
        │
        ▼
  [Quartz 30s] ProcessOutboxMessagesJob → MediatR publish(BookCreatedEvent)
```

**Anonymous user downloads a book:**

```
BookDetails.razor → click «دانلود کتاب»
  └─ anonymous → store pending_download_book → /login?returnUrl=/books/{id}
  └─ after login → BookService.DownloadBookAsync → GET /api/books/{id}/download
        │   [Authorize(Policy=RequireUserRole)]
        ▼
  BooksController.Download → GetBookQuery → _fileStorage.GetFullPath → PhysicalFile
  (static /uploads/books is separately 401-gated in the middleware pipeline)
```

---

## 5. Data Model (SQLite)

```
Users (Id, Email†, PasswordHash, FirstName, LastName, Role, CreatedAt, LastLoginAt, IsActive)
  ├── RefreshTokens (Id, UserId FK, Token, ExpiresAt, IsRevoked, CreatedAt)
  ├── PasswordResetTokens (Id, UserId FK, Token(SHA-256), ExpiresAt, IsUsed)
  └── LibraryEntries (Id, UserId FK, BookId FK, AddedAt)   [unique(UserId, BookId)]
Books (Id, Title, Author, Description, CoverImagePath, FilePath, IsActive, CreatedAt, UpdatedAt)
OutboxMessages (Id, Type, Content, OccurredOn, ProcessedOnUtc, Error)
```

- † Email normalized to lowercase; all dates UTC.
- `LibraryEntry` holds the book **id only** — no navigation to `Book`, so library book data is resolved by a join query (`UserRepository.GetLibraryBooksAsync` filters by `b.IsActive`, keeping entries for inactive books so reactivation restores them).
- Deletes rely on EF cascade (refresh/password-reset tokens, library entries) — verified no orphaned rows.

---

## 6. Security Model

| Aspect | Implementation |
|---|---|
| Password storage | PBKDF2 via `PasswordHasher` (salted; verification only via `VerifyPassword`) |
| Access tokens | JWT (symmetric HS*), `MapInboundClaims=false`, role in `role` claim |
| Sessions | Rotating refresh tokens (7-day), persisted, revoked on refresh/logout/password-change/deactivate |
| Password reset | SHA-256-hashed single-use tokens, 1 h expiry; new request invalidates older ones; no user enumeration (always 204) |
| Force re-login | Password change / account deactivation revoke ALL refresh tokens |
| Book downloads | Policy-protected endpoint + 401 static-file branch; covers stay public |
| Admin scope | Writes & user management are `RequireAdminRole`; self-modification blocked (409) |
| Inactive books | Hidden for non-admins at query level (list/detail/download/library) |
| Secrets | `JwtSettings` validated at startup (≥32 char secret); production overrides via gitignored `appsettings.Production.json` / env vars |

---

## 7. Deployment Topology

```
                  ┌──────────────────────────────────────────────┐
   Browser ──────►│  BookStore.Api (single process, one origin)   │
   (WASM UI)      │  ├─ /        → Blazor WASM client (wwwroot)   │
                  │  ├─ /api/*   → controllers (JSON)             │
                  │  ├─ /swagger → dev only                       │
                  │  ├─ /uploads/covers → static, public          │
                  │  ├─ /uploads/books → static, 401-gated        │
                  │  ├─ SQLite: bookstore.db (site folder)        │
                  │  └─ Quartz: Outbox processor (30 s)           │
                  └──────────────────────────────────────────────┘
```

- **Single publish** — `dotnet publish BookStore.Api -c Release` produces a self-contained folder (WASM assets copied into `wwwroot/`).
- **IIS/shared hosting** — documented path anchoring for SQLite + uploads, web.config WebDAV verb fix, AppPool write permissions, HTTPS termination, SMTP + `PasswordReset:BaseUrl` for TLS-terminating proxies.
- **Zero external services** — no Redis, no message broker, no object storage; the Outbox processor is in-process.

---

## 8. Strengths & Trade-offs

### Strengths
- **Layering discipline is real** — Core has no EF/web/ORM references; Application defines its own contracts; Infrastructure implements them. Verified by project-reference inspection.
- **Uniform error handling** — `ErrorOr<T>` + ProblemDetails + `ProblemDetailsParser` gives one consistent error contract across every layer and both clients.
- **Transactional outbox done right** — events are durable and atomic with the write; the interceptor/job pairing is a textbook implementation.
- **Security-conscious defaults** — custom auth stack with rotating refresh tokens, forced re-login on credential changes, no user enumeration, hidden inactive content, validated JWT config.
- **Sensible authz model** — `RequireUserRole` as "any registered account" elegantly lets admins use the library too.
- **Thin controllers, rich domain, orchestration-only handlers** — each layer plays its role.
- **Single-origin hosting** avoids CORS, cookie/CSRF concerns (Bearer tokens only), and multi-app deployment complexity.

### Trade-offs / risks
1. **SQLite** — fine for a single-instance MVP, but concurrent writes and multi-instance deploys (IIS web farm) would break it; the Outbox already points toward a future where a real RDBMS + broker is wanted.
2. **Refresh tokens in localStorage** — XSS-exposed (unlike httpOnly cookies). Acceptable for WASM MVP; a hardened option is SameSite cookies or a service worker.
3. **No test projects** — every feature was verified by manual smoke tests. As the domain grows, the domain layer (pure, dependency-free) is the cheapest place to add unit tests.
4. **Auth claims from JWT** — role/status are only as fresh as the token; deactivating a user kills refresh tokens (forces re-login) but an already-issued short-lived access token remains valid until expiry (no token revocation list).
5. **Outbox content in one DB** — the 30 s polling adds a small latency ceiling for event-driven side effects; no dead-letter/retry-exponential mechanism beyond recording the error and retrying forever.
6. **Large `User` aggregate** — three child collections; fine today, but if library/refresh-token volume grows, splitting into separate aggregates with domain services may be needed.
7. **Big-bang migrations** — 5 migrations in a day suggests active schema evolution; the hand-edited `AddBookIsActive` default (`true` vs EF's `false`) is a reminder to review every generated migration.

---

## 9. Recommendations (highest-value next)

1. **Add domain-layer unit tests** — Core has zero dependencies; test `User`/`Book` business rules + `AuthenticationService` with a fake clock/hasher. Biggest confidence-per-effort win.
2. **Evaluate cookie-based refresh tokens or token-versioning** (`SecurityStamp`-style) to bound the XSS/revocation gaps in §8.4.
3. **Introduce a swappable `IFileStorage` boundary already present** — if the book files grow, an S3-compatible implementation slots in without touching handlers (the interface already exists).
4. **Consider SQL Server/PostgreSQL provider** when concurrency or multi-instance hosting is needed — EF configs are provider-agnostic today.
5. **Outbox improvement:** add a max-delivery-attempts/poison-queue policy so a permanently failing event doesn't stay unprocessed forever.

---

## Appendix — Project Dependency Map (as-built)

```
┌──────────────┐    ┌──────────────┐
│ Core (leaf)  │    │ Contracts    │
│  users/books │    │  (leaf DTOs) │
└──────┬───────┘    └──┬─────┬─────┘
       │               │     │
       ▼               │     ▼
┌──────────────┐       │  ┌─────────┐
│ Application  │───────┘  │ UI (WASM)│
│ (MediatR,    │          └────┬────┘
│  validators, │               │ (referenced by Api)
│  interfaces) │               │
└──────┬───────┘               │
       │                       │
       ▼                       │
┌──────────────┐  ┌────────────▼──┐
│Infrastructure│◄─┤ Api (host)    │
│ EF/SQLite/   │  │ controllers,  │
│ JWT/SMTP/    │  │ JWT, static,  │
│ storage/     │  │ WASM serving  │
│ outbox/quartz│  └───────────────┘
└──────────────┘
```
