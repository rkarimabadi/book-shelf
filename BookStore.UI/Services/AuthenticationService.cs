using System.Net.Http.Json;
using BookStore.Contracts.Authentication;

namespace BookStore.UI.Services;

/// <summary>Login succeeded password-wise but the address was never confirmed; the UI opens the code page.</summary>
public sealed class EmailNotConfirmedException : Exception
{
    public EmailNotConfirmedException(string email)
        : base("ایمیل شما هنوز تأیید نشده است؛ کد تأیید را وارد کنید.")
    {
        Email = email;
    }

    public string Email { get; }
}

public interface IAuthenticationService
{
    /// <summary>Creates an unconfirmed account and mails the code. No session yet.</summary>
    Task<RegisterResponse> RegisterAsync(RegisterRequest request);

    /// <summary>Confirms the address with the mailed code and signs the user in.</summary>
    Task<AuthenticationResponse> ConfirmEmailAsync(string email, string code);

    /// <summary>Mails a fresh code; returns the seconds until another may be requested.</summary>
    Task<int> ResendEmailVerificationAsync(string email);
    Task<AuthenticationResponse> LoginAsync(LoginRequest request);
    Task LogoutAsync(string refreshToken);
    Task ForgotPasswordAsync(string email);
    Task ResetPasswordAsync(string email, string token, string newPassword);
    Task ChangePasswordAsync(string? currentPassword, string newPassword);
    Task<bool> IsGoogleEnabledAsync();
    Task<bool> HasPasswordAsync();
}

public class AuthenticationService : IAuthenticationService
{
    private readonly HttpClient _httpClient;
    private readonly AuthStateProvider _authStateProvider;
    private readonly string _authUrl = "api/auth";

    public AuthenticationService(HttpClient httpClient, AuthStateProvider authStateProvider)
    {
        _httpClient = httpClient;
        _authStateProvider = authStateProvider;
    }

    public async Task<RegisterResponse> RegisterAsync(RegisterRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync($"{_authUrl}/register", request);
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception(await ReadErrorAsync(response));
        }

        return await response.Content.ReadFromJsonAsync<RegisterResponse>()
            ?? throw new Exception("پاسخ نامعتبر از سرور.");
    }

    public async Task<AuthenticationResponse> ConfirmEmailAsync(string email, string code)
    {
        var response = await _httpClient.PostAsJsonAsync($"{_authUrl}/confirm-email", new ConfirmEmailRequest(email, code));
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception(await ReadErrorAsync(response));
        }

        var result = await ReadResultAsync(response);
        await _authStateProvider.SignInAsync(result);
        return result;
    }

    public async Task<int> ResendEmailVerificationAsync(string email)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"{_authUrl}/resend-email-verification",
            new ResendEmailVerificationRequest(email));
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception(await ReadErrorAsync(response));
        }

        var result = await response.Content.ReadFromJsonAsync<ResendEmailVerificationResponse>();
        return result?.ResendAfterSeconds ?? 0;
    }

    public async Task<AuthenticationResponse> LoginAsync(LoginRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync($"{_authUrl}/login", request);
        if (!response.IsSuccessStatusCode)
        {
            // ProblemDetails.detail carries the error code; 403 + this code = unconfirmed address.
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                var body = await response.Content.ReadAsStringAsync();
                if (body.Contains("User.EmailNotConfirmed", StringComparison.Ordinal))
                {
                    throw new EmailNotConfirmedException(request.Email);
                }

                throw new Exception(ReadMessage(body, response));
            }

            throw new Exception(await ReadErrorAsync(response));
        }

        var result = await ReadResultAsync(response);
        await _authStateProvider.SignInAsync(result);
        return result;
    }

    public async Task LogoutAsync(string refreshToken)
    {
        try
        {
            await _httpClient.PostAsJsonAsync($"{_authUrl}/logout", new LogoutRequest(refreshToken));
        }
        catch
        {
            // Local sign-out must still proceed even if the server call fails.
        }
        finally
        {
            await _authStateProvider.SignOutAsync();
        }
    }

    public async Task ForgotPasswordAsync(string email)
    {
        var response = await _httpClient.PostAsJsonAsync($"{_authUrl}/forgot-password", new ForgotPasswordRequest(email));
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception(await ReadErrorAsync(response));
        }
    }

    public async Task ResetPasswordAsync(string email, string token, string newPassword)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"{_authUrl}/reset-password",
            new ResetPasswordRequest(email, token, newPassword));
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception(await ReadErrorAsync(response));
        }
    }

    public async Task ChangePasswordAsync(string? currentPassword, string newPassword)
    {
        // currentPassword is null for Google-created accounts (they SET a password without
        // proving a current one; the server skips verification when HasPassword == false).
        var response = await _httpClient.PostAsJsonAsync(
            $"{_authUrl}/change-password",
            new ChangePasswordRequest(currentPassword, newPassword));
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception(await ReadErrorAsync(response));
        }
    }

    public async Task<bool> IsGoogleEnabledAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<bool>($"{_authUrl}/google-status");
        }
        catch
        {
            // If the status endpoint is unreachable, don't surface an error — just hide the button.
            return false;
        }
    }

    public async Task<bool> HasPasswordAsync()
    {
        // Google-created accounts have no usable password yet; the change-password page uses
        // this to hide the current-password field and label the action «تعیین رمز عبور».
        try
        {
            var me = await _httpClient.GetFromJsonAsync<MeResponse>($"{_authUrl}/me");
            return me?.HasPassword ?? true;
        }
        catch
        {
            // Unknown state → assume the account has a password (the safer UI default).
            return true;
        }
    }

    private static async Task<AuthenticationResponse> ReadResultAsync(HttpResponseMessage response)
    {
        var result = await response.Content.ReadFromJsonAsync<AuthenticationResponse>();
        return result ?? throw new Exception("Invalid authentication response.");
    }

    private sealed record MeResponse(bool HasPassword);

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response) =>
        ReadMessage(await response.Content.ReadAsStringAsync(), response);

    private static string ReadMessage(string content, HttpResponseMessage response)
    {
        var message = ProblemDetailsParser.ReadMessage(content);
        return message is null ? $"درخواست ناموفق بود ({response.StatusCode})." : MapPersian(message);
    }

    // The server surfaces validation and business errors in English (FluentValidation messages
    // and ErrorOr descriptions); translate everything a user can hit on the auth pages.
    private static string MapPersian(string message)
    {
        // FluentValidation messages (surfaced as field errors in ValidationProblemDetails).
        return message switch
        {
            "Email is required." => "ایمیل را وارد کنید.",
            "A valid email address is required." => "ایمیل معتبر نیست.",
            "Password is required." => "رمز عبور را وارد کنید.",
            "Password must be at least 8 characters long." => "رمز عبور باید حداقل ۸ کاراکتر باشد.",
            "First name is required." => "نام را وارد کنید.",
            "First name must not exceed 100 characters." => "نام نباید بیشتر از ۱۰۰ کاراکتر باشد.",
            "Last name is required." => "نام خانوادگی را وارد کنید.",
            "Last name must not exceed 100 characters." => "نام خانوادگی نباید بیشتر از ۱۰۰ کاراکتر باشد.",
            "Current password is required." => "رمز عبور فعلی را وارد کنید.",
            "New password is required." => "رمز عبور جدید را وارد کنید.",
            "Reset token is required." => "لینک بازیابی معتبر نیست.",
            "Refresh token is required." => "نشست شما منقضی شده است؛ لطفاً دوباره وارد شوید.",
            "Email is not valid." => "ایمیل معتبر نیست.",
            "Invalid email or password." => "ایمیل یا رمز عبور اشتباه است.",
            "User.InvalidCredentials" => "ایمیل یا رمز عبور اشتباه است.",
            "Current password is incorrect." => "رمز عبور فعلی اشتباه است.",
            "User.InvalidCurrentPassword" => "رمز عبور فعلی اشتباه است.",
            "Password reset token is invalid." => "لینک بازیابی معتبر نیست.",
            "User.ResetTokenNotFound" => "لینک بازیابی معتبر نیست.",
            "Password reset token has expired." => "لینک بازیابی منقضی شده است؛ دوباره لینک جدیدی دریافت کنید.",
            "User.ResetTokenExpired" => "لینک بازیابی منقضی شده است؛ دوباره لینک جدیدی دریافت کنید.",
            "Password reset token has already been used." => "این لینک بازیابی قبلاً استفاده شده است؛ لینک جدیدی دریافت کنید.",
            "User.ResetTokenUsed" => "این لینک بازیابی قبلاً استفاده شده است؛ لینک جدیدی دریافت کنید.",
            "User account is inactive." => "حساب شما غیرفعال است؛ با پشتیبانی تماس بگیرید.",
            "UserInactive" => "حساب شما غیرفعال است؛ با پشتیبانی تماس بگیرید.",
            "Refresh token has expired." => "نشست شما منقضی شده است؛ لطفاً دوباره وارد شوید.",
            "Refresh token has been revoked." => "نشست شما منقضی شده است؛ لطفاً دوباره وارد شوید.",
            "Verification code is required." => "کد تأیید را وارد کنید.",
            "Verification code is incorrect." => "کد تأیید درست نیست.",
            "User.InvalidVerificationCode" => "کد تأیید درست نیست.",
            "Verification code has expired." => "این کد دیگر معتبر نیست؛ کد تازه بخواهید.",
            "User.VerificationCodeExpired" => "این کد دیگر معتبر نیست؛ کد تازه بخواهید.",
            "Email address is not confirmed." => "ایمیل شما هنوز تأیید نشده است.",
            "User.EmailNotConfirmed" => "ایمیل شما هنوز تأیید نشده است.",
            "Email address is already confirmed." => "ایمیل شما قبلاً تأیید شده است؛ وارد شوید.",
            "User.EmailAlreadyConfirmed" => "ایمیل شما قبلاً تأیید شده است؛ وارد شوید.",
            "Refresh token not found or already revoked." => "نشست شما منقضی شده است؛ لطفاً دوباره وارد شوید.",
            "User.RefreshTokenExpired" => "نشست شما منقضی شده است؛ لطفاً دوباره وارد شوید.",
            "User.RefreshTokenRevoked" => "نشست شما منقضی شده است؛ لطفاً دوباره وارد شوید.",
            "User.RefreshTokenNotFound" => "نشست شما منقضی شده است؛ لطفاً دوباره وارد شوید.",
            "RefreshTokenNotFound" => "نشست شما منقضی شده است؛ لطفاً دوباره وارد شوید.",
            _ => MapPersianContaining(message)
        };
    }

    private static string MapPersianContaining(string message)
    {
        if (message.StartsWith("User with email", StringComparison.Ordinal)
            && message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            return "کاربری با این ایمیل قبلاً ثبت شده است؛ وارد شوید.";
        }

        if (message.StartsWith("User with email", StringComparison.Ordinal)
            && message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return "کاربری با این ایمیل پیدا نشد.";
        }

        if (message.Contains("is inactive", StringComparison.OrdinalIgnoreCase))
        {
            return "حساب شما غیرفعال است؛ با پشتیبانی تماس بگیرید.";
        }

        return message;
    }
}
