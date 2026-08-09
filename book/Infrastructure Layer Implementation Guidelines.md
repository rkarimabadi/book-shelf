### سند دستورالعمل پیاده‌سازی لایه زیرساخت (Infrastructure Layer Implementation Guidelines)
**هدف:** پیاده‌سازی جزئیات فنی، ارتباط با پایگاه داده، مدیریت سرویس‌های خارجی و پیاده‌سازی مکانیزم‌های احراز هویت بدون نشت (Leak) این وابستگی‌ها به لایه‌های مرکزی.
لطفاً در توسعه کلاس‌ها و ساختار این لایه، قوانین زیر را به دقت رعایت کنید:
#### ۱. پکیج‌های استاندارد لایه
 * استفاده از فریم‌ورک **Entity Framework Core** (و پروایدرهای آن مانند SQL Server) برای ارتباط با پایگاه داده.
 * استفاده از پکیج **Quartz.NET** (یا ابزارهای مشابه) برای مدیریت Background Jobها و پردازش رویدادها.
 * استفاده از **Microsoft.AspNetCore.Authentication.JwtBearer** برای تولید و مدیریت توکن‌های امنیتی.
 * استفاده از **Microsoft.Extensions.Options** برای پیاده‌سازی الگوی Options در مدیریت تنظیمات (Configurations).
#### ۲. پیکربندی پایگاه داده با EF Core (بدون تغییر دامنه)
 * **استفاده از Fluent API:** مپ کردن موجودیت‌های دامنه (Domain Entities) به جداول دیتابیس باید صرفاً از طریق کلاس‌هایی که IEntityTypeConfiguration<T> را پیاده‌سازی می‌کنند، انجام شود.
 * **مپ کردن فیلدهای Private:** از آنجا که Propertyهای دامنه دارای private set هستند و ممکن است فیلدهای کاملاً خصوصی (private readonly) داشته باشند، EF Core باید به گونه‌ای کانفیگ شود که بتواند این فیلدها را بخواند و بنویسد (استفاده از Property(x => x.MyProperty).HasField("_myProperty")).
 * **استفاده از Value Converters:** برای ذخیره آبجکت‌های مقدار (Value Objects) و شناسه‌های قوی (Strongly Typed IDs) در دیتابیس، حتماً از Value Converterهای EF Core استفاده کنید تا انواع پیچیده دامنه به انواع ساده دیتابیسی (مثل Guid یا string) تبدیل شوند.
#### ۳. پیاده‌سازی الگوی Repository
 * این لایه موظف است اینترفیس‌های Repository تعریف شده در لایه Application (مانند IUserRepository) را پیاده‌سازی کند.
 * **پرهیز از Generic Repository:** معمولاً از پیاده‌سازی یک Repository عمومی و بسیار بزرگ (Generic) بپرهیزید. هر Aggregate Root باید Repository مختص به خود را داشته باشد که متدهای معنا‌دار بر اساس Use Caseها در آن پیاده‌سازی شده‌اند.
 * عملیات ذخیره‌سازی نهایی (SaveChanges) معمولاً به عنوان بخشی از یک Unit of Work یا در سطح هندلرها فراخوانی می‌شود، نه مستقیماً درون متدهای Repository (Add یا Update).
#### ۴. ثبات نهایی (Eventual Consistency) و رویدادهای دامنه
 * **استفاده از Interceptorها:** برای مدیریت Domain Eventها (که در لایه دامنه تولید شده‌اند)، یک کلاس SaveChangesInterceptor از EF Core پیاده‌سازی کنید. این Interceptor پیش از ذخیره تغییرات در دیتابیس، تمام رویدادها را از موجودیت‌ها استخراج می‌کند.
 * **الگوی Outbox (Outbox Pattern):** رویدادهای استخراج شده نباید فوراً اجرا شوند. آن‌ها را به فرمت JSON سریالایز کرده و در جدولی به نام OutboxMessages ذخیره کنید (تا همراه با تغییرات اصلی در یک تراکنش دیتابیسی ذخیره شوند).
 * **پردازش پس‌زمینه:** یک Background Job با استفاده از Quartz.NET بنویسید که هر چند ثانیه یک‌بار پیام‌های پردازش نشده Outbox را خوانده و آن‌ها را توسط MediatR (به عنوان Notification) در سیستم منتشر (Publish) کند.
#### ۵. احراز هویت و زمان (Authentication & Clock)
 * **تولید توکن (Token Generation):** کلاسی مانند JwtTokenGenerator بسازید که اینترفیس IJwtTokenGenerator (از لایه Application) را پیاده‌سازی کند و وظیفه تولید توکن‌های JWT را بر عهده داشته باشد.
 * **پروایدر زمان (DateTime Provider):** هرگز در لایه‌های مرکزی از DateTime.Now یا DateTime.UtcNow استفاده نکنید. یک اینترفیس IDateTimeProvider در لایه Application تعریف کرده و در این لایه با کلاسی که DateTime.UtcNow را برمی‌گرداند، آن را پیاده‌سازی کنید (این کار تست‌نویسی را بسیار آسان می‌کند).
#### ۶. مدیریت تنظیمات (Options Pattern)
 * برای خواندن مقادیر از appsettings.json (مانند Connection Strings، تنظیمات JWT یا تنظیمات ایمیل)، کلاس‌های پیکربندی (مثلاً JwtSettings) ایجاد کنید.
 * با استفاده از IOptions<T> یا متد Bind، این تنظیمات را در زمان راه‌اندازی سیستم (Startup) اعتبارسنجی (Validate) کرده و به سرویس‌ها تزریق کنید.
#### ۷. توسعه متدهای تزریق وابستگی (Dependency Injection Registration)
 * کلاس Program.cs باید تمیز بماند. یک کلاس استاتیک به نام DependencyInjection (یا InfrastructureServiceRegistration) در این لایه ایجاد کنید.
 * یک متد اکستنشن (Extension Method) مانند AddInfrastructure(this IServiceCollection services, IConfiguration config) بنویسید و تمام ریپازیتوری‌ها، تنظیمات دیتابیس، کلاینت‌های HTTP خارجی و سرویس‌های احراز هویت را در این متد رجیستر کنید.
**چک‌لیست تایید کد (Code Review Checklist):**
 * [ ] آیا تمام کانفیگ‌های دیتابیس با استفاده از Fluent API نوشته شده و کلاس‌های دامنه دست‌نخورده باقی مانده‌اند؟
 * [ ] آیا اینترفیس‌های لایه Application به‌درستی و بدون استفاده از منطق بیزینسی (Business Logic) پیاده‌سازی شده‌اند؟
 * [ ] آیا رویدادهای دامنه (Domain Events) از طریق Interceptorها و مکانیزم Outbox ذخیره و پردازش می‌شوند؟
 * [ ] آیا ارتباط با سرویس‌های شخص ثالث (Third-party) به‌درستی کپسوله شده و در صورت بروز خطا لاگ می‌شود؟
 * [ ] آیا ثبت وابستگی‌های این لایه (DI) در یک Extension Method اختصاصی انجام شده است؟
 * [ ] آیا مقادیر تنظیمات (Settings) از طریق الگوی Options به کلاس‌ها تزریق شده است؟
