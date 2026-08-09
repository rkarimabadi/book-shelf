### سند پیاده‌سازی قابلیت احراز هویت (Authentication Feature Implementation)

**هدف:** مستندسازی کامل مراحل طراحی، پیاده‌سازی، و رفع اشکال قابلیت احراز هویت (Register / Login / RefreshToken / Logout) در راه‌حل BookStore بر پایه معماری تمیز (Clean Architecture) و طراحی مبتنی بر دامنه (DDD).

---

## ۱. نمای کلی معماری

قابلیت احراز هویت در چهار لایه مجزا پیاده‌سازی شده است و جریان داده همواره از بیرون به درون (API ← Application ← Core) و سپس در جهت مخالف بازمی‌گردد:

| لایه | مسئولیت |
|------|----------|
| `BookStore.Core` | مدل دامنه غنی: موجودیت `User`، توکن تازه‌سازی، سرویس دامنه احراز هویت، رویدادهای دامنه |
| `BookStore.Application` | فرمان‌ها (Commands)، اعتبارسنجی FluentValidation، رفتار Pipeline، قراردادها (Interfaces) |
| `BookStore.Infrastructure` | EF Core + SQLite، JWT، هش رمز PBKDF2، الگوی Outbox با Quartz، مخازن |
| `BookStore.Api` | کنترلرهای REST، اعتبارسنجی JWT، نگاشت Mapster، ساختار خطای ProblemDetails |

---

## ۲. لایه دامنه (Core)

### ۲.۱. موجودیت User (Aggregate Root)

- **کپسوله‌سازی وضعیت:** تمام پراپرتی‌ها `private set` یا `private init` هستند؛ هیچ راهی برای تغییر مستقیم وضعیت از بیرون وجود ندارد.
- **Factory Method:** ساخت نمونه فقط از طریق `User.Create(...)` با خروجی `ErrorOr<User>` انجام می‌شود؛ ایمیل به حروف کوچک نرمال می‌شود.
- **متدهای بیزینسی:** `Login()`، `AddRefreshToken()`، `RevokeRefreshToken()`، `UpdateProfile()`، `Activate()` و `Deactivate()` — همه وضعیت موجودیت را با حفظ قوانین ناوردا تغییر می‌دهند.
- **رویدادهای دامنه:** هر تغییر معنادار یک رویداد به `_domainEvents` اضافه می‌کند (مانند `UserCreatedEvent`، `UserLoggedInEvent`، `RefreshTokenAddedEvent`).
- **مجموعه توکن‌ها:** لیست `RefreshToken`ها در فیلد خصوصی `_refreshTokens` نگهداری می‌شود و فقط به صورت `IReadOnlyCollection` در معرض دید قرار می‌گیرد (مدیریت فرزندان منحصراً توسط Aggregate Root).

### ۲.۲. موجودیت RefreshToken

- از `Entity` پایه ارث می‌برد و کلید آن یک `Guid` ساخته‌شده در سمت کلاینت است (مهم: این جزئیات در بخش ۵.۱ منشأ یک باگ شد).
- متد `Revoke()` وضعیت توکن را باطل می‌کند و `IsExpired()` / `IsValid()` قوانین اعتبار را برقرار می‌کنند.

### ۲.۳. سرویس دامنه AuthenticationService

- **قرارداد `IAuthenticationService`** و پیاده‌سازی `AuthenticationService` هر دو در لایه دامنه قرار دارند.
- `RegisterUser(email, passwordHash, ...)`: بررسی تکراری‌بودن ایمیل، ساخت User از طریق Factory و ثبت در مخزن.
- `LoginUser(email, passwordHash)`: بازیابی کاربر، بررسی فعال‌بودن، به‌روزرسانی `LastLoginAt`، تولید و ثبت توکن تازه‌سازی.
- `RefreshToken(refreshToken)`: چرخش توکن (باطل‌کردن توکن قبلی + ثبت توکن جدید).
- `LogoutUser(refreshToken)`: باطل‌کردن توکن.
- **نکته مهم:** مقایسه رمز عبور در این لایه صرفاً مقایسه رشته‌ای هش است؛ این طراحی، منشأ باگ دوم بود (بخش ۵.۲).

### ۲.۴. Guard Clauses سفارشی

- `Guard.Against.ExpiresInPast(expiresAt, ...)` به عنوان افزونه سفارشی برای اعتبارسنجی تاریخ انقضا در `Core/Domain/Common/GuardClauseExtensions.cs` اضافه شد.

---

## ۳. لایه اپلیکیشن (Application)

### ۳.۱. فرمان‌ها (Commands)

هر قابلیت یک پوشه اختصاصی (Vertical Slice) دارد و در یک فایل واحد شامل سه بخش است:

| فرمان | ساختار فایل |
|-------|-------------|
| Register | `RegisterCommand` + Validator + Handler |
| Login | `LoginCommand` + Validator + Handler |
| RefreshToken | `RefreshTokenCommand` + Validator + Handler |
| Logout | `LogoutCommand` + Validator + Handler |

- هر فرمان `IRequest<ErrorOr<T>>` را پیاده می‌کند و دقیقاً یک Handler دارد.
- Handlerها منطق بیزینسی ندارند؛ فقط سرویس‌های دامنه را فراخوانی و هماهنگ می‌کنند و در پایان `_unitOfWork.SaveChangesAsync` را اجرا می‌کنند.

### ۳.۲. رفتار Pipeline (ValidationBehavior)

- اعتبارسنجی با `AbstractValidator<T>` (FluentValidation) انجام می‌شود و به صورت خودکار از طریق `IPipelineBehavior` مدیاتور اجرا می‌گردد؛ هرگز داخل Handler اعتبارسنجی نمی‌شود.
- خروجی آن `Error.Validation(PropertyName, ErrorMessage)` است تا در لایه API به `400 Bad Request` با جزئیات فیلدها تبدیل شود.

### ۳.۳. قراردادها (Dependency Inversion)

وابستگی‌های بیرونی به صورت اینترفیس در `Application/Common/Interfaces/` تعریف شده‌اند و پیاده‌سازی آن‌ها در Infrastructure انجام می‌شود:

- `IPasswordHasher` — هش و تأیید رمز (پیاده‌سازی PBKDF2)
- `IJwtTokenGenerator` — تولید توکن JWT
- `IUserRepository` — دسترسی به داده کاربر
- `IUnitOfWork` — مرز تراکنش واحد (Single SaveChanges)
- `IDateTimeProvider` — تأمین زمان UTC (ساختگی‌پذیر برای تست)

### ۳.۴. ثبت وابستگی‌ها

`DependencyInjection.AddApplication()` ثبت‌نام مدیاتور، اعتبارسنج‌های FluentValidation و `ValidationBehavior` را انجام می‌دهد.

---

## ۴. لایه زیرساخت (Infrastructure)

### ۴.۱. داده و EF Core

- `BookStoreDbContext` با Fluent API (نه Data Annotation) در `Persistence/Configurations/` پیکربندی شده است.
- SQLite با `Data Source=bookstore.db`؛ فایل دیتابیس زمان اجرا در پوشه `BookStore.Api/` ساخته می‌شود (فایل‌های `-shm` و `-wal` متعلق به همان نمونه هستند).
- نگاشت رابطه `User ↔ RefreshToken` به صورت یک‌به‌چند با `DeleteBehavior.Cascade` و کلید خارجی پنهان `UserId`.

### ۴.۲. مخزن UserRepository

- متدهای `GetById` / `GetByEmail` / `GetByRefreshToken` (با `Include(RefreshTokens)`) و `Add` / `Update` / `Delete`.
- متد `Update` پس از اصلاح باگ (بخش ۵.۱) وظیفه تعیین وضعیت صحیح توکن‌های جدید را نیز بر عهده دارد.

### ۴.۳. احراز هویت و امنیت

- **JwtTokenGenerator:** تولید توکن HMAC-SHA256 با ادعاهای `sub`, `email`, `given_name`, `family_name`, `role`, `jti`.
- **PasswordHasher:** هش PBKDF2 با نمک تصادفی ۱۶ بایتی، ۱۰۰۰۰ تکرار و مقایسه زمان‌ثابت (`FixedTimeEquals`).
- **JwtSettings:** خواندن و اعتبارسنجی بخش پیکربندی `Jwt` (Issuer, Audience, Secret, ExpiryMinutes).

### ۴.۴. الگوی Outbox

- **PublishDomainEventsInterceptor:** قبل از ذخیره‌سازی، رویدادهای دامنه را از Aggregateها می‌خواند، پاک می‌کند و به‌صورت `OutboxMessage` به همان تراکنش اضافه می‌کند (اتمی‌بودن ثبت + رویداد).
- **ProcessOutboxMessagesJob (Quartz):** هرچند ثانیه پیام‌های پردازش‌نشده را می‌خواند، رویداد را با MediatR منتشر می‌کند و `ProcessedOnUtc` را ثبت می‌نماید.
- با `[DisallowConcurrentExecution]` از اجرای همزمان Job جلوگیری می‌شود.

### ۴.۵. ثبت وابستگی‌ها

`DependencyInjection.AddInfrastructure(configuration)` شامل EF Core، مخازن، سرویس‌های امنیتی و Quartz است.

---

## ۵. لایه API و رفع اشکال (Smoke Test)

### ۵.۱. باگ اول: `DbUpdateConcurrencyException` در ثبت‌نام

**مشاهده:** اولین فراخوانی Register با خطای 500 و پیام *"expected to affect 1 row(s), but actually affected 0 row(s)"* شکست خورد. با فعال‌سازی لاگ‌گیری SQL در EF مشخص شد آخرین دستور، `UPDATE RefreshToken ... WHERE Id = @p6` است.

**ریشه‌یابی:** کلاس پایه `Entity` مقدار `Id = Guid.NewGuid()` را در سازنده می‌دهد. وقتی یک `RefreshToken` جدید از طریق متد Aggregate به مجموعه کاربرِ ردیابی‌شده اضافه می‌شود، EF در لحظه الحاق (Graph Attach)، کلید غیرپیش‌فرض را نشانه «موجود در دیتابیس» می‌پندارد و وضعیت را `Modified` (به‌جای `Added`) در نظر می‌گیرد؛ در نتیجه به‌جای `INSERT` یک `UPDATE` صادر می‌کند که هیچ سطری را تغییر نمی‌دهد.

**راه‌حل:** در `UserRepository.Update` ابتدا با خاموش‌کردن موقت `AutoDetectChangesEnabled`، توکن‌های ازپیش‌ردیابی‌شده (بارگذاری‌شده از دیتابیس) را فهرست می‌کنیم، سپس پس از `_dbContext.Users.Update(user)`، هر توکنی که در آن فهرست نبود به‌صراحت `EntityState.Added` می‌شود تا EF آن را `INSERT` کند.

### ۵.۲. باگ دوم: ورود با 401 همیشگی

**مشاهده:** پس از اصلاح باگ اول، ثبت‌نام موفق شد اما Login همیشه `401 InvalidCredentials` برمی‌گرداند.

**ریشه‌یابی:** هش PBKDF2 هر بار نمک تصادفی جدید تولید می‌کند؛ بنابراین خروجی `HashPassword` برای یک رمز یکسان در هر بار اجرا متفاوت است. مقایسه رشته‌ای `user.PasswordHash != passwordHash` در `AuthenticationService.LoginUser` هیچ‌وقت برقرار نمی‌شد. ثبت‌نام فقط به این دلیل کار می‌کرد که همان رشته هشِ تازه‌تولیدشده را دوباره مقایسه می‌کرد!

**راه‌حل:** در `LoginCommandHandler` ابتدا کاربر از طریق `GetUserByEmail` بازیابی می‌شود و رمز با `_passwordHasher.VerifyPassword(command.Password, user.PasswordHash)` تأیید می‌گردد؛ در صورت نامعتبربودن، `InvalidCredentials` برگردانده می‌شود و سپس هشِ ذخیره‌شده به `LoginUser` ارسال می‌شود.

### ۵.۳. باگ سوم: ادعاهای JWT در اندپوینت محافظت‌شده null

**مشاهده:** اندپوینت `GET /api/auth/me` با توکن معتبر، مقادیر `userId`, `email`, `role` را null برمی‌گرداند (در حالی که بدون توکن به‌درستی 401 می‌داد).

**ریشه‌یابی:** `JwtSecurityTokenHandler` به‌صورت پیش‌فرض ادعاهای ورودی را نگاشت می‌کند (مثلاً `sub` به `ClaimTypes.NameIdentifier`)؛ بنابراین `FindFirstValue(JwtRegisteredClaimNames.Sub)` مقداری پیدا نمی‌کرد.

**راه‌حل:** غیرفعال‌کردن نگاشت پیش‌فرض با `options.MapInboundClaims = false` در پیکربندی JwtBearer تا ادعاها با نام اصلی خود (`sub`, `email`, `role`) در دسترس باشند.

### ۵.۴. ساختار خطای API

- کنترلر پایه `ApiController` متد `Problem(List<Error>)` را دارد که خطاهای ErrorOr را به `ProblemDetails` استاندارد RFC 9110 نگاشت می‌کند:
  - Validation → 400 · Unauthorized → 401 · Forbidden → 403 · NotFound → 404 · Conflict → 409 · سایر → 500
- اعتبارسنجی‌های چندگانه به `ValidationProblem` با جزئیات هر فیلد تبدیل می‌شوند.

### ۵.۵. پیکربندی Program.cs

- ثبت لایه‌های Application و Infrastructure، کنترلرها، Swagger، Mapster و تنظیمات JWT.
- پیکربندی `AddAuthentication(JwtBearer)` با `TokenValidationParameters` کامل (Issuer, Audience, Lifetime, SigningKey) و `RoleClaimType = "role"`.
- سیاست‌های مجوز `RequireAdminRole` و `RequireUserRole` بر اساس ثابت‌های `Roles` و `Policies`.
- اجرای خودکار `Database.Migrate()` هنگام راه‌اندازی و فعال‌سازی `UseAuthentication` / `UseAuthorization`.

---

## ۶. قراردادها (Contracts) و نگاشت

- پروژه `BookStore.Contracts` شامل درخواست‌ها و پاسخ‌های API (`RegisterRequest`, `LoginRequest`, `RefreshTokenRequest`, `LogoutRequest`, `AuthenticationResponse`) است و به هیچ لایه داخلی وابسته نیست.
- نگاشت بین Contracts و Application با **Mapster** انجام می‌شود (در `AuthController` از `IMapper`).

---

## ۷. نتیجه نهایی و سناریوهای تأییدشده

همه سناریوهای زیر به‌صورت دستی (Smoke Test) با شروع API و ارسال درخواست‌های واقعی HTTP تأیید شدند:

| سناریو | نتیجه |
|--------|-------|
| ثبت‌نام کاربر جدید (Register) | ✅ 200 + توکن و RefreshToken |
| ورود با رمز صحیح (Login) | ✅ 200 |
| ورود با رمز اشتباه | ✅ 401 |
| ثبت‌نام با ایمیل تکراری | ✅ 409 |
| چرخش توکن تازه‌سازی (Refresh) | ✅ 200 + توکن جدید |
| خروج (Logout) | ✅ 204 |
| دسترسی بدون توکن (`/me`) | ✅ 401 |
| دسترسی با توکن معتبر (`/me`) | ✅ ادعاهای `userId`, `email`, `role` |

**وضعیت نهایی:** راه‌حل با `dotnet build` بدون خطا و بدون هشدار کامپایل می‌شود. پروژه تستی در راه‌حل وجود ندارد (حذف شده) و صحت‌سنجی از طریق همین Smoke Testهای دستی انجام می‌گیرد.
