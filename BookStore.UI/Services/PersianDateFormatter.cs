using System.Globalization;

namespace BookStore.UI.Services;

/// <summary>
/// Formats DateTime values as Persian Solar (Jalali) calendar dates with Persian digits,
/// e.g. «۱۴۰۴/۰۵/۲۱». The server stores all timestamps in UTC, so callers pass the raw
/// UTC value and this converts to the browser's local timezone before formatting.
/// Uses the built-in <see cref="PersianCalendar"/> (no external package, works in Blazor WASM).
/// </summary>
public static class PersianDateFormatter
{
    private static readonly PersianCalendar Calendar = new();

    /// <summary>
    /// Returns a local-time Jalali date as "yyyy/MM/dd" with Persian digits
    /// (e.g. 1404/05/21 → «۱۴۰۴/۰۵/۲۱»).
    /// </summary>
    public static string Format(DateTime value)
    {
        var local = value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;

        // Note: custom numeric formats ("00") do NOT apply the culture's NativeDigits, so
        // the digits are converted explicitly rather than relying on the fa-IR culture.
        return ToPersianDigits(
            $"{Calendar.GetYear(local):0000}/{Calendar.GetMonth(local):00}/{Calendar.GetDayOfMonth(local):00}");
    }

    private static string ToPersianDigits(string value)
    {
        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            // ۰ (U+06F0) … ۹ (U+06F9) are the Persian-Indic digits.
            chars[i] = chars[i] is >= '0' and <= '9'
                ? (char)('۰' + (chars[i] - '0'))
                : chars[i];
        }

        return new string(chars);
    }
}
