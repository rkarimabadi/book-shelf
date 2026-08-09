# E-Station — High-Level Technology Overview

## ۱. چارچوب کلی (Platform & Runtime)
- **.NET 9 به‌عنوان Runtime اصلی برای تمام بخش‌های سیستم (Backend, Web, Mobile).
- **C# 12** زبان برنامه‌نویسی برای همه پروژه‌ها.
- هدف‌گذاری **Cross-platform** برای سرویس‌ها و کتابخانه‌ها، با استقرار نهایی روی **Windows Server** (هاست اشتراکی).

## ۲. Backend (Web API)
- **ASP.NET Core 9 Web API** – ارائه RESTful endpoints با فرمت JSON.
- **Entity Framework Core 9 – ORM برای دسترسی به پایگاه داده، با پشتیبانی از Migrations.
- **NetTopologySuite** – کتابخانه فضایی (Spatial) برای ذخیره و کوئری موقعیت‌های جغرافیایی ایستگاه‌ها.
- **Swashbuckle / Swagger** – مستندسازی تعاملی API در محیط توسعه.

## ۳. Frontend Web (PWA)
- **Blazor WebAssembly (WASM)** – فریمورک SPA سمت کلاینت با اجرای دات‌نت در مرورگر.
- **Progressive Web App (PWA)** – از طریق Service Worker و Web App Manifest برای نصب‌پذیری و قابلیت‌های آفلاین پایه.
- **JavaScript Interop** – برای استفاده از APIهای مرورگر مانند Geolocation و کتابخانه‌های نقشه (Leaflet).

## ۴. Frontend Mobile
- **.NET MAUI Blazor Hybrid** – اجرای کامپوننت‌های Blazor درون یک WebView بومی در اندروید و ویندوز (و iOS در آینده).
- **دسترسی به قابلیت‌های دستگاه** (مانند GPS) از طریق APIهای بومی MAUI (`Geolocation.Default`).

## ۵. مؤلفه‌های مشترک UI
- **Razor Class Library (RCL)** – پروژه اشتراکی شامل صفحات و کامپوننت‌های قابل استفاده مجدد در Blazor WASM و MAUI.
- **کتابخانه نقشه: Leaflet.js** (از طریق JS interop در WASM و اجرا در WebView در MAUI) برای نمایش موقعیت ایستگاه‌ها.

## ۶. پایگاه داده (Database)
- **SQL Server** نسخه‌ای که توسط هاست اشتراکی ارائه می‌شود (احتمالاً SQL Server 2019 یا 2022 Express/Standard).
- **No Spatial Data Type `geography`** برای ذخیره مختصات جغرافیایی.
- پشتیبان جایگزین: **PostgreSQL + PostGIS** در صورتی که هاست اجازه نصب بدهد (EF Core provider مربوطه).

## ۷. ثبت وقایع (Logging)
- **Serilog** به‌عنوان کتابخانه اصلی لاگ‌گیری.
- **Serilog.Sinks.File** – ذخیره‌سازی لاگ‌ها در فایل‌های چرخشی (Rolling) با محدودیت حجم و تعداد فایل.
- **Serilog.Sinks.MSSqlServer** – گزینه جایگزین برای نوشتن لاگ مستقیماً در پایگاه داده، در صورت بروز محدودیت نوشتن فایل روی هاست.

## ۸. احراز هویت و امنیت
- **ASP.NET Core Identity** برای مدیریت کاربران، رمز عبور و نقش‌ها.
- **JWT (JSON Web Token)** برای احراز هویت API.
- **Refresh Token** برای افزایش امنیت و عدم نیاز به ورود مکرر.
- **HTTPS** (TLS) اجباری در محیط Production.

## ۹. ذخیره‌سازی سمت کلاینت
- **Blazor WebAssembly:** `ProtectedLocalStorage` برای نگهداری توکن JWT در مرورگر.
- **MAUI:** `SecureStorage` برای ذخیره‌سازی امن توکن روی دستگاه.

## ۱۰. پردازش‌های پس‌زمینه و زمان‌بندی‌شده
- **IHostedService** درون فرآیند ASP.NET Core برای اجرای وظایف دوره‌ای ساده (مثلاً بازبینی وضعیت ایستگاه‌های رأی‌خورده).
- در صورت محدودیت ریسایکل شدن Application Pool روی هاست اشتراکی، وظایف بحرانی در لحظه (همراه با درخواست کاربر) انجام می‌شوند.

## ۱۱. ملاحظات میزبانی (Hosting Constraints)
- **هاست اشتراکی ویندوز** (با IIS و Application Pool) به مدت حداقل یک سال.
- **بدون Redis یا Distributed Cache** – فقط `IMemoryCache` درون‌حافظه‌ای.
- **بدون سرویس‌های پس‌زمینه خارجی** – همه چیز داخل پروسه اصلی اجرا می‌شود.
- **انتشار و استقرار ساده:** پوشه `publish` شامل API و فایل‌های استاتیک Blazor WASM از طریق FTP یا کنترل‌پنل هاست.

## ۱۲. ابزارهای توسعه و تست
- **Visual Studio 2022 / VS Code** به همراه extensions مربوط به .NET MAUI و Blazor.
- **.NET CLI** برای ساخت، اجرا و انتشار.
- **Testcontainers** یا **LocalDB** برای تست‌های یکپارچگی پایگاه داده.
- **Lighthouse** برای بررسی استانداردهای PWA.