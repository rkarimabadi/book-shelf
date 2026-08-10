using BookStore.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace BookStore.Application.Common;

internal static class FileCleanup
{
    public static async Task DeleteIfExistsAsync(
        IFileStorage fileStorage,
        ILogger logger,
        string? relativePath,
        string context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return;
        }

        try
        {
            await fileStorage.DeleteAsync(relativePath, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete uploaded file '{RelativePath}' ({Context}).", relativePath, context);
        }
    }
}
