# RULES.md — BookStore Project Rules (non-negotiable)

This file lists the **hard rules** that every change to this codebase must obey.
They are binding for all contributors, including AI coding assistants.
`AGENTS.md` is the full reference guide (structure, status, pitfalls); when in doubt,
read the relevant section there. Rules are grouped by layer and numbered (`R-01`…)
so they can be referenced precisely (e.g. «violates R-12»).

---
- **R-00** - Always answer in English.
## 1. Architecture & Dependency Direction

- **R-01** — Strict Clean Architecture with DDD. Dependency direction is one-way:
  `Api → Application / Infrastructure`, `Infrastructure → Application (interfaces) / Core`,
  `Core → nothing external`.
- **R-02** — The **Core (Domain) layer must have zero external dependencies**:
  no EF Core, no ASP.NET, no data annotations (`[Key]`, `[Table]`, `[Required]`), no NuGet
  packages. Pure POCO domain models only.
- **R-03** — Shared DTOs live in `BookStore.Contracts`, referenced by both API and UI.
  Never reference `BookStore.Core` from the UI, and never reference Infrastructure from Application.
- **R-04** — No new projects or NuGet packages without justification. Verify the package is
  appropriate and check how the project already solves the problem before adding anything.
- **R-05** — Application code is organized in feature-driven vertical slices:
  `Features/<Feature>/Commands/<Name>/` and `Features/<Feature>/Queries/<Name>/`.
- **R-06** — API contracts exposed to the UI are `POST`/`PUT`/`PATCH`/`DELETE` + `GET`;
  responses use `ProblemDetails` (controllers inherit the `ApiController` base).

## 2. Domain Layer

- **R-07** — Rich domain model: state is encapsulated with **private setters** and can only
  change through public **business methods** (`Book.Deactivate()`, `User.ResetPassword(...)`).
  No anemic getters/setters-only entities.
- **R-08** — Every operation that can fail returns **`ErrorOr<T>`** (from the `ErrorOr`
  package). **Never throw exceptions for business-logic failures.** Use descriptive error
  catalogs per aggregate (e.g. `BookErrors`, `UserErrors`).
- **R-09** — Domain events are appended to the entity's `_domainEvents` list and are
  **never published from the domain layer**. Infrastructure publishes them via the Outbox
  pattern (`PublishDomainEventsInterceptor` + `ProcessOutboxMessagesJob`).
- **R-10** — Only **aggregate roots** expose entity collections; children are created and
  managed exclusively through parent methods (never by reaching into a child collection
  from outside the aggregate).
- **R-11** — All dates are **UTC**. Email addresses are normalized to **lowercase**.
- **R-12** — Use `Guard.Against` (custom guards in `Core/Domain/Common/GuardClauseExtensions.cs`)
  for input validation inside domain factories/methods.
- **R-13** — New child entities of a tracked aggregate (e.g. `RefreshToken`,
  `PasswordResetToken`) must be persisted with the **tracked-children snapshot + fixup loop**
  pattern in the repository (`AutoDetectChangesEnabled = false` first, then untracked children
  set to `EntityState.Added`) — see AGENTS.md pitfall 1. Never rely on default change tracking
  for new children.

## 3. Application Layer

- **R-14** — One file per feature: Command/Query + its FluentValidation Validator + Handler
  together. Commands/queries implement `IRequest<ErrorOr<T>>` and have exactly one handler.
- **R-15** — Validation happens **only** in FluentValidation validators, executed automatically
  by `ValidationBehavior`. **Never validate inside handlers.**
- **R-16** — Handlers contain **no business logic** — they orchestrate domain services,
  repositories, and external contracts, then persist via `IUnitOfWork`.
- **R-17** — External needs (clock, tokens, hashing, file storage, email, persistence) are
  contracts in `Application/Common/Interfaces/` (`IDateTimeProvider`, `IJwtTokenGenerator`,
  `IPasswordHasher`, `IFileStorage`, `IEmailSender`, `IUnitOfWork`); implementations live in
  Infrastructure. Repository and domain-service contracts live in **Core**.
- **R-18** — Every handler that mutates state must call `IUnitOfWork.SaveChangesAsync` before
  returning success.

## 4. API & Security

- **R-19** — Authorization uses **policy attributes** — `[Authorize(Policy = Policies.RequireAdminRole)]`
  / `Policies.RequireUserRole` — never `[Authorize]` alone for admin-only endpoints.
  `RequireUserRole` = "registered account" (admins are also users).
- **R-20** — JWT handling keeps `MapInboundClaims = false`; read claims via
  `JwtRegisteredClaimNames` (`sub`, `email`). Never rely on remapped `ClaimTypes.*` names.
- **R-21** — PBKDF2 password hashes are salted: **never compare hash strings directly**.
  Verify with `IPasswordHasher.VerifyPassword(password, storedHash)`, then pass the stored
  hash to the domain service.
- **R-22** — Stored secret tokens (refresh tokens, password-reset tokens) are hashed
  (SHA-256) so a DB leak cannot be replayed; they are single-use and carry an expiry.
- **R-23** — No user enumeration: forgot-password returns success whether or not the account
  exists, and email-send failures are logged but still return success.
- **R-24** — Refresh-token failures (unknown/expired/revoked) uniformly return **401**.
  Rotation: each refresh returns a new token and revokes the old one; logout revokes all.
- **R-25** — Deactivated books (`IsActive = false`) are invisible to non-admins everywhere:
  public list, detail, download, and users' libraries. The `includeInactive` query flag is
  **honored only for admins** (`User.IsInRole(Roles.Admin)`).
- **R-26** — Book files are delivered only through the protected download endpoint
  (`GET /api/books/{id}/download`, `RequireUserRole`) — static serving of `.epub` is
  impossible (pitfall 12). `/uploads/books` static access is gated to authenticated users;
  `/uploads/covers` stays public.
- **R-27** — `PhysicalFile` requires a **rooted path**: combine `IFileStorage.GetFullPath`
  with `IWebHostEnvironment.ContentRootPath` (`Path.GetFullPath(relPath, contentRoot)`).
- **R-28** — Multipart upload field names must match `BooksController` exactly:
  `title`, `author`, `description`, `coverImage`, `file`.

## 5. Infrastructure

- **R-29** — Use the established EF Core 9 + SQLite stack and repository-per-aggregate pattern.
  No new ORM/database without discussion.
- **R-30** — EF migrations are generated with the documented commands only, always from the
  solution root with `--project BookStore.Infrastructure --startup-project BookStore.Infrastructure`.
  The design-time throwaway DB must be deleted after use.
- **R-31** — When EF adds a new non-null column, **verify the migration's `defaultValue`**
  (EF can default a new `bool` to `false` and silently break existing rows — see the
  `AddBookIsActive` trap in AGENTS.md).
- **R-32** — `LocalFileStorage`: `SaveAsync` returns web-relative, `BaseUrl`-prefixed paths;
  `GetFullPath`/`DeleteAsync` must strip the `BaseUrl` prefix before resolving against
  `RootPath` (pitfall 6).
- **R-33** — Uploaded-file cleanup on delete/replace is best-effort (`FileCleanup`): log a
  warning on failure, never fail the request after the DB commit.

## 6. UI (Blazor WebAssembly)

- **R-34** — UI is Persian, RTL, and styled with scoped CSS + the existing design tokens.
  Match the current design language; don't introduce a parallel style system.
- **R-35** — Feature folders contain their own `Pages/` + `Components/` + `Services/`;
  cross-feature presentational components live in `Shared/Components/`
  (LoadingSpinner, EmptyState, ErrorNotice, ConfirmDialog, AccessDenied).
- **R-36** — Register `AuthStateProvider` under **both** the concrete type and
  `AuthenticationStateProvider` in `BookStore.UI/Program.cs` (pitfall 7).
- **R-37** — Tokens in localStorage are **raw strings, not JSON** — `ClientStorageService`
  must catch `JsonException` and return the raw value (pitfall 8).
- **R-38** — Pages matched by the same route template are reused by Blazor: reload data in
  `OnParametersSetAsync` keyed on a last-seen id (`if (Id != _loadedId) ...`); child
  components with prefill state need the same id-guard (pitfall 11).
- **R-39** — Login `returnUrl` values must start with `/` (prefix `"/"` before escaping).
- **R-40** — Each `HttpClient` owns its private handler chain; a `DelegatingHandler` passed
  to `new HttpClient(...)` needs `InnerHandler` assigned (pitfall 9).

## 7. Git, Secrets & Workflow

- **R-41** — The solution must build with **0 warnings / 0 errors** before a change is done
  (`dotnet build BookStore.sln -c Release`).
- **R-42** — Secrets are never committed: `BookStore.Api/appsettings.Production.json` is
  **gitignored**; production JWT/SMTP secrets go there or in environment variables
  (e.g. `JwtSettings__Secret`). Never hardcode secrets in source or committed config.
- **R-43** — Keep the docs in sync when implementing features: `AGENTS.md` status sections,
  `book/MVP Scope Document.md` feature codes (`MH-*`/`SH-*`/`CH-*`), and this file.
- **R-44** — There are no test projects (removed by design). Verify changes by building plus
  **live API smoke tests** (temp SQLite DB + curl E2E flows, e.g. auth round-trip, add/remove
  library, deactivate/restore book, forgot/reset password).
- **R-45** — Do not run effectful commands (git push/commit, DB mutations on real data,
  global package installs) unless the user explicitly asks.

---

## Quick checklist before finishing any change

1. Does it respect the one-way dependency direction (R-01…R-03)?
2. Are failures expressed as `ErrorOr<T>` and never business exceptions (R-08)?
3. Is validation in the validator, not the handler (R-15)?
4. Are secrets outside the repo (R-42)?
5. Does the build produce 0 warnings / 0 errors (R-41)?
6. Are AGENTS.md + the MVP doc statuses updated (R-43)?
