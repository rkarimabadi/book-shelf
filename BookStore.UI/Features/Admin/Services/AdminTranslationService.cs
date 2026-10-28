using System.Net;
using System.Net.Http.Json;
using BookStore.Contracts.Translation;
using BookStore.UI.Services;
using Microsoft.JSInterop;

namespace BookStore.UI.Features.Admin.Services;

public sealed class AdminTranslationService : IAdminTranslationService
{
    private const string Offline = "ارتباط با سرور برقرار نشد؛ دوباره تلاش کنید.";
    private const string SessionExpired = "نشست شما منقضی شده است؛ لطفاً دوباره وارد شوید.";
    private const string NoPermission = "شما مجوز لازم برای این عملیات را ندارید.";

    private readonly HttpClient _http;
    private readonly IJSRuntime _js;

    public AdminTranslationService(HttpClient http, IJSRuntime js)
    {
        _http = http;
        _js = js;
    }

    public Task<AdminTranslationResult<IReadOnlyList<TranslationJobResponse>>> GetJobsAsync(CancellationToken cancellationToken = default) =>
        SendAsync<IReadOnlyList<TranslationJobResponse>>(
            () => _http.GetAsync("api/translations", cancellationToken),
            async r => await r.Content.ReadFromJsonAsync<List<TranslationJobResponse>>(cancellationToken) ?? []);

    public Task<AdminTranslationResult<TranslationJobResponse>> CreateJobAsync(TranslationJobSubmit form, CancellationToken cancellationToken = default) =>
        SendAsync(
            () =>
            {
                var content = new MultipartFormDataContent
                {
                    { new StringContent(form.Title ?? string.Empty), "title" },
                    { new StringContent(form.Author ?? string.Empty), "author" },
                    { new StringContent(form.Mode), "mode" },
                    { new StringContent(form.Instructions ?? string.Empty), "instructions" },
                    {
                        new StreamContent(form.BookFile.OpenReadStream(TranslationValues.EpubMaxBytes, cancellationToken)),
                        "file", SafeFileName(form.BookFile.Name)
                    }
                };

                if (form.CoverFile is not null)
                {
                    content.Add(new StreamContent(form.CoverFile.OpenReadStream(TranslationValues.CoverMaxBytes, cancellationToken)),
                        "coverImage", "cover" + Path.GetExtension(form.CoverFile.Name));
                }

                return _http.PostAsync("api/translations", content, cancellationToken);
            },
            async r => (await r.Content.ReadFromJsonAsync<TranslationJobResponse>(cancellationToken))!);

    public Task<AdminTranslationResult<bool>> CancelJobAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync(() => _http.PostAsync($"api/translations/{id}/cancel", null, cancellationToken), _ => Task.FromResult(true));

    public Task<AdminTranslationResult<bool>> RetryJobAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync(() => _http.PostAsync($"api/translations/{id}/retry", null, cancellationToken), _ => Task.FromResult(true));

    public Task<AdminTranslationResult<bool>> DeleteJobAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync(() => _http.DeleteAsync($"api/translations/{id}", cancellationToken), _ => Task.FromResult(true));

    public Task<AdminTranslationResult<bool>> DownloadAsync(TranslationJobResponse job, CancellationToken cancellationToken = default) =>
        SendAsync(
            () => _http.GetAsync($"api/translations/{job.Id}/download", cancellationToken),
            async r =>
            {
                var bytes = await r.Content.ReadAsByteArrayAsync(cancellationToken);
                var name = Path.GetFileNameWithoutExtension(job.OriginalFileName) + "-fa.epub";
                await _js.InvokeVoidAsync("bookstoreDownload", bytes, name);
                return true;
            });

    public Task<AdminTranslationResult<AiSettingsResponse>> GetSettingsAsync(CancellationToken cancellationToken = default) =>
        SendAsync(
            () => _http.GetAsync("api/ai-settings", cancellationToken),
            async r => (await r.Content.ReadFromJsonAsync<AiSettingsResponse>(cancellationToken))!);

    public Task<AdminTranslationResult<bool>> SaveSettingsAsync(UpdateAiSettingsRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(() => _http.PutAsJsonAsync("api/ai-settings", request, cancellationToken), _ => Task.FromResult(true));

    public Task<AdminTranslationResult<string>> TestConnectionAsync(CancellationToken cancellationToken = default) =>
        SendAsync(
            () => _http.PostAsync("api/ai-settings/test", null, cancellationToken),
            async r => (await r.Content.ReadFromJsonAsync<AiConnectionTestResponse>(cancellationToken))!.Reply);

    // The server stores the upload under its own name; a safe ASCII name avoids Content-Disposition issues.
    private static string SafeFileName(string? name)
    {
        var baseName = new string(Path.GetFileNameWithoutExtension(name ?? "book")
            .Where(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_')
            .ToArray());
        return (baseName.Length == 0 ? "book" : baseName) + ".epub";
    }

    private static async Task<AdminTranslationResult<T>> SendAsync<T>(
        Func<Task<HttpResponseMessage>> send,
        Func<HttpResponseMessage, Task<T>> read)
    {
        try
        {
            using var response = await send();
            if (response.IsSuccessStatusCode)
            {
                return new AdminTranslationResult<T>(true, await read(response));
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return new AdminTranslationResult<T>(false, Message: SessionExpired, Unauthorized: true);
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return new AdminTranslationResult<T>(false, Message: NoPermission, Forbidden: true);
            }

            var body = await response.Content.ReadAsStringAsync();
            return new AdminTranslationResult<T>(false, Message: MapPersian(ProblemDetailsParser.ReadMessage(body)));
        }
        catch (Exception)
        {
            return new AdminTranslationResult<T>(false, Message: Offline);
        }
    }

    private static string MapPersian(string? message) => message switch
    {
        null => "عملیات ناموفق بود؛ دوباره تلاش کنید.",
        _ when message.Contains("AiSettings.NotConfigured") || message.Contains("settings are not configured") =>
            "ابتدا تنظیمات سرویس هوش مصنوعی را ذخیره کنید.",
        _ when message.Contains("ApiKeyMissing") || message.Contains("API key is missing") =>
            "کلید API ذخیره‌شده قابل خواندن نیست؛ آن را دوباره در تنظیمات وارد کنید.",
        _ when message.Contains("Endpoint must be") || message.Contains("InvalidEndpoint") || message.Contains("Endpoint is") =>
            "آدرس سرویس باید یک نشانی کامل http یا https باشد.",
        _ when message.Contains("Model is") || message.Contains("InvalidModel") => "نام مدل الزامی است.",
        _ when message.Contains("Parallel requests") => "تعداد درخواست‌های هم‌زمان باید بین ۱ تا ۸ باشد.",
        _ when message.Contains("Chunk size") => "اندازهٔ هر بخش باید بین ۲۰۰ تا ۲۰۰۰ کلمه باشد.",
        _ when message.Contains("AI provider is invalid") => "نوع سرویس‌دهنده معتبر نیست.",
        _ when message.Contains("rejected the API key") => "سرویس هوش مصنوعی کلید API را نپذیرفت.",
        _ when message.Contains("endpoint or model was not found") => "آدرس سرویس یا نام مدل پیدا نشد.",
        _ when message.Contains("refused the request") => "سرویس هوش مصنوعی درخواست را رد کرد: " + message,
        _ when message.Contains("AI provider") => "پاسخی از سرویس هوش مصنوعی دریافت نشد: " + message,
        _ when message.Contains("Book file is required") || message.Contains("FileRequired") => "فایل EPUB کتاب را انتخاب کنید.",
        _ when message.Contains("must be an EPUB") => "فقط فایل EPUB پذیرفته می‌شود.",
        _ when message.Contains("too large") => "حجم فایل بیش از حد مجاز است (کتاب ۲۵ و جلد ۵ مگابایت).",
        _ when message.Contains("not a valid EPUB") => "این فایل یک کتاب EPUB معتبر نیست.",
        _ when message.Contains("DRM") => "این کتاب قفل DRM دارد و قابل ترجمه نیست؛ نسخهٔ بدون DRM را بارگذاری کنید.",
        _ when message.Contains("contains no text") => "در این کتاب متنی برای ترجمه پیدا نشد.",
        _ when message.Contains("Cover image must be") => "تصویر جلد باید JPG، PNG، WEBP یا GIF باشد.",
        _ when message.Contains("Title must not exceed") => "عنوان نباید بیشتر از ۲۰۰ کاراکتر باشد.",
        _ when message.Contains("Author must not exceed") => "نام نویسنده نباید بیشتر از ۱۰۰ کاراکتر باشد.",
        _ when message.Contains("Instructions must not exceed") => "توضیحات نباید بیشتر از ۲۰۰۰ کاراکتر باشد.",
        _ when message.Contains("mode is invalid") => "نوع کار معتبر نیست.",
        _ when message.Contains("can be cancelled") => "فقط کار در صف یا در حال اجرا را می‌توان متوقف کرد.",
        _ when message.Contains("can be retried") => "فقط کار ناموفق یا متوقف‌شده را می‌توان دوباره اجرا کرد.",
        _ when message.Contains("Cancel the translation job") => "ابتدا کار را متوقف کنید، سپس حذفش کنید.",
        _ when message.Contains("not ready yet") => "کتاب ترجمه‌شده هنوز آماده نیست.",
        _ when message.Contains("file is missing") => "فایل کتاب ترجمه‌شده پیدا نشد.",
        _ when message.Contains("was not found") => "کار ترجمه پیدا نشد.",
        _ => "عملیات ناموفق بود؛ دوباره تلاش کنید."
    };

    /// <summary>Persian headline for a failed job's stored "Code: message" error.</summary>
    public static string DescribeJobError(string? error)
    {
        var code = error?.Split(':', 2)[0].Trim() ?? string.Empty;
        return code switch
        {
            "Ai.Unauthorized" => "سرویس هوش مصنوعی کلید API را نپذیرفت؛ تنظیمات را بررسی کنید.",
            "Ai.NotFound" => "آدرس سرویس یا نام مدل پیدا نشد؛ تنظیمات را بررسی کنید.",
            "Ai.BadRequest" => "سرویس هوش مصنوعی درخواست را رد کرد (مثلاً اعتبار حساب تمام شده یا مدل نامعتبر است).",
            "Ai.Truncated" => "پاسخ مدل ناقص ماند؛ اندازهٔ بخش‌ها را در تنظیمات کوچک‌تر کنید.",
            "Ai.Failed" => "ارتباط با سرویس هوش مصنوعی برقرار نشد.",
            "AiSettings.NotConfigured" => "تنظیمات سرویس هوش مصنوعی ذخیره نشده است.",
            "AiSettings.ApiKeyMissing" => "کلید API ذخیره‌شده قابل خواندن نیست؛ آن را دوباره وارد کنید.",
            "TranslationJob.InvalidEpub" => "فایل EPUB معتبر نیست.",
            "TranslationJob.DrmProtected" => "کتاب قفل DRM دارد.",
            "TranslationJob.SourceMissing" => "فایل بارگذاری‌شده پیدا نشد.",
            "TranslationJob.NoText" => "در کتاب متنی برای ترجمه پیدا نشد.",
            _ => "ترجمه با خطا متوقف شد."
        };
    }
}
