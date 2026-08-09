# BookStore Solution - Development Guide

## Project Structure

```
BookStore/
├── BookStore.Api/              # ASP.NET Core Web API
├── BookStore.Core/             # Domain Layer (DDD)
├── BookStore.Application/      # Application Layer
├── BookStore.Infrastructure/   # Infrastructure Layer
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
- `LocalFileStorage.cs` (Infrastructure) - writes to `wwwroot/uploads/` (subdirs `covers/`, `books/`), configured via `FileStorage` section


## Technology Stack

### Backend
- .NET 9
- C# 12
- ASP.NET Core 9 Web API
- Entity Framework Core 9 (for Infrastructure)
- SQLite (development database)

### Frontend (Future)
- Blazor WebAssembly
- PWA capabilities

### Security
- ASP.NET Core Identity (for user management)
- JWT tokens
- Refresh tokens
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
- External needs (repos, email, tokens, clock) are interfaces defined in Application:
  - `IUserRepository`, `IEmailService`, `IDateTimeProvider`, `IJwtTokenGenerator`, `IPasswordHasher`
- Implementations live in Infrastructure

### Security
- Roles/Permissions/Policies defined as constants in `Common/Security/` (`Roles`, `Policies`)
- Authorization can be enforced via an `IPipelineBehavior` before handlers

### Registration
- `DependencyInjection.AddApplication()` registers MediatR, FluentValidation validators, and `ValidationBehavior`
- Currently implemented: `Features/Authentication/` (Register, Login, RefreshToken, Logout commands) and `Features/Books/` (Create/Update/Delete commands, Get/GetAll queries)

## Authentication Status (as of current session)

- **Domain (Core):** `User`, `RefreshToken`, `AuthenticationService` complete
  - `IAuthenticationService.RefreshToken` returns `(User User, string RefreshToken)` (rotates token and returns the new one)
  - Custom guard `GuardClauseExtensions.ExpiresInPast` in `Core/Domain/Common/`
- **Application:** Register/Login/RefreshToken/Logout commands with validators + handlers, `ValidationBehavior`, DI registration — complete and compiling
- **Infrastructure:** EF Core 9 + SQLite (`BookStoreDbContext`, Fluent API configurations), `UserRepository`, `JwtTokenGenerator`, `PasswordHasher` (PBKDF2), `DateTimeProvider`, `JwtSettings` (Options + validation), Outbox pattern (`PublishDomainEventsInterceptor` + `ProcessOutboxMessagesJob` via Quartz.NET), `AddInfrastructure()` DI registration — complete and compiling
- **API:** AuthController (register/login/refresh/logout/me), JWT validation with `MapInboundClaims = false`, `ApiController` ProblemDetails base — complete and verified

## Book Content Management Status

- **Domain (Core):** `Book` aggregate complete (Create/UpdateDetails/Delete, domain events)
- **Application:** `Features/Books/` — CreateBook/UpdateBook/DeleteBook commands + validators + handlers, GetBook/GetBooks queries
- **Infrastructure:** `BookRepository`, `BookConfiguration`, `LocalFileStorage` (wwwroot/uploads, subdirs covers/books, `FileStorage` config section), `AddBooks` migration
- **API:** `BooksController` — GET public list/detail; POST/PUT/DELETE protected by `RequireAdminRole` policy; multipart upload (cover image + book file)

## Next Steps

1. Build API endpoints (controllers, JWT validation) — DONE
2. Implement user library management
3. Create book management domain entities — DONE
4. Add validation and error handling (API-level) — DONE

### Migrations

- Design-time factory `BookStoreDbContextFactory` in `Infrastructure/Persistence/` (SQLite `Data Source=bookstore.db`)
- Run from solution root:
  - `dotnet ef migrations add <Name> --project BookStore.Infrastructure --startup-project BookStore.Infrastructure`
  - `dotnet ef database update --project BookStore.Infrastructure --startup-project BookStore.Infrastructure`
- `bookstore.db` is created in `BookStore.Api/` at runtime (dev only, not for source control)
- The design-time factory (`--startup-project BookStore.Infrastructure`) creates a separate throwaway DB next to it — delete it after use; only the `BookStore.Api/` instance is needed
- EF Core pinned to 9.x (v10 targets net10.0); tool: `dotnet-ef` 9.0.18 (global)