namespace BookStore.UI.Services;

public enum ToastType
{
    Info,
    Success,
    Error
}

public sealed class ToastMessage
{
    public Guid Id { get; } = Guid.NewGuid();

    public ToastType Type { get; init; }

    public string Text { get; init; } = string.Empty;

    /// <summary>Errors persist until manually dismissed so the user never misses them.</summary>
    public bool Sticky => Type == ToastType.Error;
}

/// <summary>
/// Lightweight in-memory toast/notification bus for the whole WASM app.
/// Pages fire Show* and the <c>ToastHost</c> mounted in <c>MainLayout</c> renders them.
/// </summary>
public sealed class ToastService
{
    private const int AutoDismissMs = 4500;

    private readonly List<ToastMessage> _toasts = new();

    public event Action? Changed;

    public IReadOnlyList<ToastMessage> Toasts => _toasts;

    public void ShowInfo(string text) => Show(new ToastMessage { Type = ToastType.Info, Text = text });

    public void ShowSuccess(string text) => Show(new ToastMessage { Type = ToastType.Success, Text = text });

    public void ShowError(string text) => Show(new ToastMessage { Type = ToastType.Error, Text = text });

    public void Dismiss(Guid id)
    {
        var index = _toasts.FindIndex(t => t.Id == id);
        if (index < 0)
        {
            return;
        }

        _toasts.RemoveAt(index);
        Changed?.Invoke();
    }

    private void Show(ToastMessage toast)
    {
        _toasts.Add(toast);
        Changed?.Invoke();

        if (!toast.Sticky)
        {
            _ = AutoDismissAsync(toast.Id);
        }
    }

    private async Task AutoDismissAsync(Guid id)
    {
        try
        {
            await Task.Delay(AutoDismissMs);
            Dismiss(id);
        }
        catch (Exception)
        {
            // Fire-and-forget dismissal; a cancelled/failed timer must never crash the app.
        }
    }
}
