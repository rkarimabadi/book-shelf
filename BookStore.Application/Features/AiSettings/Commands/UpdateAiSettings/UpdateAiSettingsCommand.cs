using BookStore.Application.Common.Interfaces;
using BookStore.Core.Domain.Translation;
using ErrorOr;
using FluentValidation;
using MediatR;

namespace BookStore.Application.Features.AiSettings.Commands.UpdateAiSettings;

/// <param name="ApiKey">A new key, or null/empty to keep the stored one.</param>
/// <param name="ClearApiKey">Removes the stored key (ignored when a new key is given).</param>
public record UpdateAiSettingsCommand(
    AiProviderType Provider,
    string Endpoint,
    string Model,
    string? ApiKey,
    bool ClearApiKey,
    int ParallelRequests,
    int ChunkWords) : IRequest<ErrorOr<Success>>;

public class UpdateAiSettingsCommandValidator : AbstractValidator<UpdateAiSettingsCommand>
{
    public UpdateAiSettingsCommandValidator()
    {
        RuleFor(x => x.Provider)
            .IsInEnum().WithMessage("AI provider is invalid.");

        RuleFor(x => x.Endpoint)
            .NotEmpty().WithMessage("Endpoint is required.")
            .MaximumLength(AiProviderSettings.MaxEndpointLength).WithMessage("Endpoint is too long.");

        RuleFor(x => x.Model)
            .NotEmpty().WithMessage("Model is required.")
            .MaximumLength(AiProviderSettings.MaxModelLength).WithMessage("Model is too long.");

        RuleFor(x => x.ApiKey)
            .MaximumLength(1000).WithMessage("API key is too long.");

        RuleFor(x => x.ParallelRequests)
            .InclusiveBetween(AiProviderSettings.MinParallelRequests, AiProviderSettings.MaxParallelRequests)
            .WithMessage($"Parallel requests must be between {AiProviderSettings.MinParallelRequests} and {AiProviderSettings.MaxParallelRequests}.");

        RuleFor(x => x.ChunkWords)
            .InclusiveBetween(AiProviderSettings.MinChunkWords, AiProviderSettings.MaxChunkWords)
            .WithMessage($"Chunk size must be between {AiProviderSettings.MinChunkWords} and {AiProviderSettings.MaxChunkWords} words.");
    }
}

public class UpdateAiSettingsCommandHandler : IRequestHandler<UpdateAiSettingsCommand, ErrorOr<Success>>
{
    private readonly IAiProviderSettingsRepository _settingsRepository;
    private readonly ISecretProtector _secretProtector;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateAiSettingsCommandHandler(
        IAiProviderSettingsRepository settingsRepository,
        ISecretProtector secretProtector,
        IUnitOfWork unitOfWork)
    {
        _settingsRepository = settingsRepository;
        _secretProtector = secretProtector;
        _unitOfWork = unitOfWork;
    }

    public async Task<ErrorOr<Success>> Handle(UpdateAiSettingsCommand command, CancellationToken cancellationToken)
    {
        var newKey = string.IsNullOrWhiteSpace(command.ApiKey) ? null : command.ApiKey.Trim();
        var protectedKey = newKey is null ? null : _secretProtector.Protect(newKey);

        var settings = await _settingsRepository.GetAsync(cancellationToken);
        if (settings is null)
        {
            var created = AiProviderSettings.Create(
                command.Provider, command.Endpoint, command.Model,
                command.ParallelRequests, command.ChunkWords, protectedKey);
            if (created.IsError)
            {
                return created.Errors;
            }

            _settingsRepository.Add(created.Value);
        }
        else
        {
            var updated = settings.Update(
                command.Provider, command.Endpoint, command.Model,
                command.ParallelRequests, command.ChunkWords, protectedKey);
            if (updated.IsError)
            {
                return updated.Errors;
            }

            if (newKey is null && command.ClearApiKey)
            {
                settings.ClearApiKey();
            }

            _settingsRepository.Update(settings);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success;
    }
}
