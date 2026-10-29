using BookStore.Api.Common;
using BookStore.Application.Common.Security;
using BookStore.Application.Features.AiSettings.Commands.TestAiConnection;
using BookStore.Application.Features.AiSettings.Commands.UpdateAiSettings;
using BookStore.Application.Features.AiSettings.Queries.GetAiSettings;
using BookStore.Contracts.Translation;
using BookStore.Core.Domain.Translation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers;

/// <summary>AI provider settings of the translation tool. The API key is write-only: only a masked hint is returned.</summary>
[Route("api/ai-settings")]
[Authorize(Policy = Policies.RequireAdminRole)]
public sealed class AiSettingsController : ApiController
{
    private readonly ISender _sender;

    public AiSettingsController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetAiSettingsQuery(), cancellationToken);

        return result.Match(
            value => Ok(value.Settings is null
                ? new AiSettingsResponse(
                    false,
                    TranslationValues.ProviderOpenAi,
                    TranslationValues.DefaultOpenAiEndpoint,
                    string.Empty,
                    false,
                    null,
                    false,
                    AiProviderSettings.DefaultParallelRequests,
                    AiProviderSettings.DefaultChunkWords,
                    null)
                : new AiSettingsResponse(
                    true,
                    value.Settings.Provider.ToWire(),
                    value.Settings.Endpoint,
                    value.Settings.Model,
                    value.Settings.HasApiKey && !value.ApiKeyUnreadable,
                    value.ApiKeyHint,
                    value.ApiKeyUnreadable,
                    value.Settings.ParallelRequests,
                    value.Settings.ChunkWords,
                    value.Settings.UpdatedAt)),
            errors => Problem(errors));
    }

    [HttpPut]
    public async Task<IActionResult> Update(UpdateAiSettingsRequest request, CancellationToken cancellationToken)
    {
        var provider = TranslationMappings.ParseProvider(request.Provider);
        if (provider is null)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "AI provider is invalid.", detail: "AiSettings.InvalidProvider");
        }

        var result = await _sender.Send(new UpdateAiSettingsCommand(
            provider.Value,
            request.Endpoint ?? string.Empty,
            request.Model ?? string.Empty,
            request.ApiKey,
            request.ClearApiKey,
            request.ParallelRequests,
            request.ChunkWords), cancellationToken);

        return result.Match(_ => NoContent(), errors => Problem(errors));
    }

    [HttpPost("test")]
    public async Task<IActionResult> Test(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new TestAiConnectionCommand(), cancellationToken);

        if (result.IsError && result.FirstError.Code.StartsWith("Ai.", StringComparison.Ordinal))
        {
            // The provider's 401/404 must not look like the admin's own session expiring.
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: result.FirstError.Description, detail: result.FirstError.Code);
        }

        return result.Match(reply => Ok(new AiConnectionTestResponse(reply)), errors => Problem(errors));
    }
}
