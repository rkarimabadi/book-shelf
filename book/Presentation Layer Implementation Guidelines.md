### سند دستورالعمل پیاده‌سازی لایه ارائه (Presentation Layer Implementation Guidelines)
**هدف:** مدیریت درخواست‌های HTTP، اعتبارسنجی ساختاری، هدایت درخواست‌ها به لایه Application و بازگرداندن پاسخ‌های استاندارد (مانند Problem Details) بدون درگیری با منطق اصلی سیستم.
در توسعه کنترلرها (Controllers) و پیکربندی پروژه API، قوانین زیر را به دقت رعایت کنید:
#### ۱. جداسازی پروژه قراردادها (Contracts Project)
 * **پروژه مستقل:** تعریف مدل‌های ورودی و خروجی (Requests & Responses) نباید در داخل پروژه API یا Application باشد. یک پروژه مجزا به نام Contracts (یا Shared) ایجاد کنید.
 * **رکوردهای سی‌شارپ:** از record به جای class برای تعریف Requestها و Responseها در این پروژه استفاده کنید تا Immutability (تغییرناپذیری) حفظ شود.
 * **مزیت:** کلاینت‌ها (مثل فرانت‌اند یا سرویس‌های دیگر) می‌توانند این پروژه را بدون نیاز به کشیدن وابستگی‌های سنگینِ کل سیستم، مستقیماً به عنوان پکیج مرجع (Reference) استفاده کنند.
 * پروژه باید از نوع Blazor Hosted Webassembly باشد تا UI بتواند بصورت یکپارچه از یکجا سرویس داده شود.
#### ۲. پکیج‌ها و ابزارهای استاندارد لایه
 * استفاده از **Mapster** (یا ابزارهای مشابه مثل AutoMapper) برای تبدیل اشیاء (Mapping).
 * استفاده از **MediatR** برای ارسال Command/Query به لایه Application.
 * استفاده از استانداردهای توکار ASP.NET Core برای بازگرداندن **ProblemDetails**.
#### ۳. کنترلرهای به‌شدت لاغر (Thin Controllers)
کنترلرها در این معماری تنها **سه وظیفه** بر عهده دارند و هرگز نباید از این مرز فراتر بروند:
 1. **دریافت درخواست (Request):** گرفتن داده‌ها از بدنه (Body)، مسیر (Route) یا پارامترها (Query).
 2. **تبدیل و ارسال (Map & Send):** تبدیل شیء Request (از پروژه Contracts) به شیء Command/Query (مربوط به لایه Application) با استفاده از Mapper و ارسال آن توسط MediatR.
 3. **بازگرداندن پاسخ (Return Response):** دریافت نتیجه از MediatR (که از نوع ErrorOr است) و تبدیل آن به وضعیت HTTP مناسب (مثل 200 OK، 201 Created یا یک پیام خطای استاندارد).
#### ۴. استانداردسازی خطاها با الگوی Problem Details (RFC 7807)
 * **کلاس پایه کنترلر (ApiController Base):** یک کنترلر پایه (Base Controller) بنویسید که همه کنترلرهای دیگر از آن ارث‌بری کنند.
 * **متد Problem مرکزی:** در این کلاس پایه، یک متد یکپارچه بنویسید که لیست خطاهای صادر شده از ErrorOr را دریافت کرده و آن‌ها را به فرمت استاندارد ProblemDetails در ASP.NET Core تبدیل کند.
 * **ممنوعیت بازگشت مستقیم ارور متنی:** هیچ‌گاه نباید یک استرینگ ساده یا یک JSON غیرساختاریافته به عنوان خطا به کلاینت بازگردانده شود.
#### ۵. نگاشت وضعیت‌های HTTP (Status Codes Mapping)
متدِ پردازشِ خطای شما باید انواع خطاهای صادر شده از لایه Domain یا Application را به درستی به Status Codeهای مربوطه در پروتکل HTTP نگاشت کند:
 * ErrorType.Validation ➡️ **400 Bad Request** (مثال: فرمت ایمیل اشتباه است)
 * ErrorType.Unauthorized ➡️ **401 Unauthorized** (مثال: توکن ارسال نشده است)
 * ErrorType.Forbidden ➡️ **403 Forbidden** (مثال: کاربر نقش ادمین ندارد)
 * ErrorType.NotFound ➡️ **404 Not Found** (مثال: کاربری با این ID یافت نشد)
 * ErrorType.Conflict ➡️ **409 Conflict** (مثال: ایمیل تکراری است)
 * خطای سفارشی TooManyRequests (Error.Custom با نوع 429) ➡️ **429 Too Many Requests** (مثال: درخواست کد تازه زودتر از ۲۴ ساعت)
 * **Metadata خطا:** مقادیر Error.Metadata را کنار code به Extensions همان ProblemDetails اضافه کنید تا کلاینت بتواند دقیق واکنش نشان دهد؛ مثلاً زمان مجاز درخواست بعدی را نمایش دهد.
#### ۶. تزریق وابستگی در کنترلر (Dependency Injection)
 * در سازنده (Constructor) کنترلرها، فقط و فقط اینترفیس‌های ISender (مربوط به MediatR) و IMapper (مربوط به Mapster) را تزریق کنید.
 * **تزریق سرویس‌ها و ریپازیتوری‌ها ممنوع است:** لایه Presentation به هیچ وجه نباید مستقیماً با پایگاه داده یا سرویس‌های زیرساختی تعامل داشته باشد.
#### ۷. امنیت و احراز هویت در سطح Endpoint
 * **صفت‌های مجوزی (Authorization Attributes):** از صفت‌های [Authorize] روی کنترلرها یا اکشن‌ها استفاده کنید.
 * **سیاست‌های مبتنی بر مجوز (Policy-based Auth):** در سناریوهای پیچیده‌تر، صفت‌های سفارشی (Custom Attributes) بنویسید که مستقیماً به Permissionهای تعریف شده در لایه Application یا Domain متصل شوند (مثلاً [HasPermission(Permissions.Subscription.Create)]).
 * **Policy پیش‌فرض و استثناها:** [Authorize] ساده تأیید ایمیل را هم می‌خواهد (بند ۱۰ سند Infrastructure). Endpointهایی که حساب تأییدنشده به آن‌ها نیاز دارد (وضعیت، ارسال و تأیید کد، و اطلاعات کاربر جاری) با [Authorize(Policy = AuthPolicies.AllowUnconfirmedEmail)] علامت می‌خورند.
 * **پاسخ روشن برای حساب تأییدنشده:** یک IAuthorizationMiddlewareResultHandler بنویسید که وقتی کاربر واردشده فقط به‌خاطر تأییدنشدن ایمیل رد می‌شود، به‌جای یک 403 خالی، ProblemDetails با code مشخص (مثلاً User.EmailNotConfirmed) برگرداند تا کلاینت بداند کاربر را به صفحه تأیید ببرد. بقیه حالت‌ها را به AuthorizationMiddlewareResultHandler پیش‌فرض بسپارید.
 * **Endpointهای حساب کاربری:**
    * `POST /api/auth/register` و `POST /api/auth/login`: توکن را در کوکی HttpOnly می‌گذارند و پروفایل کاربر را همراه فیلد EmailConfirmed برمی‌گردانند.
    * `GET /api/auth/email-verification`، `POST /api/auth/email-verification/send` و `POST /api/auth/email-verification/confirm`: وضعیت تأیید، ارسال دوباره کد (اگر زود باشد 429) و تأیید کد.
    * `POST /api/auth/password/forgot`: همیشه **202 Accepted**، چه حسابی با آن ایمیل باشد چه نباشد.
    * `POST /api/auth/password/reset`: گذرواژه تازه را با کد تعیین می‌کند و مثل ورود، کوکی نشست را می‌گذارد.
 * **Endpointهای ورود با گوگل (Google SSO):** دو اکشن در AuthController این جریان را پیش می‌برند و هر دو وقتی ورود با گوگل فعال نیست 404 برمی‌گردانند (پیاده‌سازی فنی در بند ۸ سند Infrastructure):
    * `GET /api/auth/google-login?returnUrl=…`: یک Challenge روی Scheme گوگل می‌سازد که RedirectUri آن اکشن google-finalize است.
    * `GET /api/auth/google-finalize`: هویت را از کوکی موقت گوگل می‌خواند و بلافاصله آن کوکی را پاک می‌کند، ExternalLoginCommand را با MediatR می‌فرستد، JWT حاصل را در همان کوکی HttpOnly نشست می‌گذارد و با LocalRedirect به returnUrl برمی‌گرداند. توکن هرگز در URL قرار نمی‌گیرد.
 * **جلوگیری از Open Redirect:** returnUrl فقط به‌صورت مسیر نسبی داخل همین سایت پذیرفته می‌شود (بدون `//`، `\` و `:`)؛ هر مقدار دیگری به `/` تبدیل می‌شود.
 * **خطاهای این جریان ProblemDetails نیستند:** این دو Endpoint با ناوبری کامل مرورگر باز می‌شوند، نه با HttpClient. برای همین خطا به‌صورت Redirect به `/login?google_error=unverified` (ایمیل تأییدنشده) یا `/login?google_error=failed` (سایر خطاها) برگردانده می‌شود. این تنها استثنای بند ۴ است.
 * **کنترلر همچنان لاغر است:** خواندن و پاک کردن کوکی موقت با HttpContext.AuthenticateAsync و SignOutAsync کار سطح HTTP است و مجاز است، اما پیدا کردن یا ساختن کاربر فقط از طریق ExternalLoginCommand انجام می‌شود و سرویس دیگری به کنترلر تزریق نمی‌شود.
