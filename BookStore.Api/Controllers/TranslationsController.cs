using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BookStore.Api.Common;
using BookStore.Application.Common.Security;
using BookStore.Application.Features.Translation.Commands.CancelTranslationJob;
using BookStore.Application.Features.Translation.Commands.CreateTranslationJob;
using BookStore.Application.Features.Translation.Commands.DeleteTranslationJob;
using BookStore.Application.Features.Translation.Commands.RetryTranslationJob;
using BookStore.Application.Features.Translation.Queries.GetTranslationJobs;
using BookStore.Application.Features.Translation.Queries.GetTranslationOutput;
using BookStore.Contracts.Translation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers;

/// <summary>Admin AI translation tool: upload an EPUB (+ optional cover), follow progress, download the Persian EPUB.</summary>
[Route("api/translations")]
[Authorize(Policy = Policies.RequireAdminRole)]
public sealed class TranslationsController : ApiController
{
    // EPUB (25 MB) + cover (5 MB) + multipart overhead.
    private const long MaxRequestBytes = 32 * 1024 * 1024;

    private readonly ISender _sender;

    public TranslationsController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetTranslationJobsQuery(), cancellationToken);

        return result.Match(
            jobs => Ok(jobs.Select(job => job.ToResponse()).ToList()),
            errors => Problem(errors));
    }

    [HttpPost]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    public async Task<IActionResult> Create(
        [FromForm] CreateTranslationJobRequest request,
        IFormFile? file,
        IFormFile? coverImage,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId is null)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "User id claim is missing.", detail: "User.ClaimMissing");
        }

        if (file is null || file.Length == 0)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Book file is required.", detail: "TranslationJob.FileRequired");
        }

        if (file.Length > TranslationValues.EpubMaxBytes || coverImage?.Length > TranslationValues.CoverMaxBytes)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "File is too large.", detail: "TranslationJob.FileTooLarge");
        }

        var mode = TranslationMappings.ParseMode(request.Mode);
        if (mode is null)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Translation mode is invalid.", detail: "TranslationJob.InvalidMode");
        }

        // The EPUB is inspected (zip read) before it is stored, so it must be seekable.
        await using var epub = new MemoryStream();
        await file.CopyToAsync(epub, cancellationToken);
        epub.Position = 0;

        await using var cover = coverImage is null ? null : new MemoryStream();
        if (coverImage is not null)
        {
            await coverImage.CopyToAsync(cover!, cancellationToken);
            cover!.Position = 0;
        }

        var result = await _sender.Send(new CreateTranslationJobCommand(
            userId.Value,
            file.FileName,
            epub,
            cover,
            coverImage is null ? null : Path.GetExtension(coverImage.FileName),
            request.Title,
            request.Author,
            mode.Value,
            request.Instructions), cancellationToken);

        return result.Match(
            job => StatusCode(StatusCodes.Status201Created, job.ToResponse()),
            errors => Problem(errors));
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new CancelTranslationJobCommand(id), cancellationToken);

        return result.Match(_ => NoContent(), errors => Problem(errors));
    }

    [HttpPost("{id:guid}/retry")]
    public async Task<IActionResult> Retry(Guid id, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new RetryTranslationJobCommand(id), cancellationToken);

        return result.Match(_ => NoContent(), errors => Problem(errors));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new DeleteTranslationJobCommand(id), cancellationToken);

        return result.Match(_ => NoContent(), errors => Problem(errors));
    }

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetTranslationOutputQuery(id), cancellationToken);

        return result.Match(
            output => PhysicalFile(output.FullPath, "application/epub+zip", output.DownloadName),
            errors => Problem(errors));
    }

    private Guid? GetUserId()
    {
        var userIdClaim = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(userIdClaim, out var userId) ? userId : null;
    }
}
