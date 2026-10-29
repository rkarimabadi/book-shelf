using BookStore.Application.Common.Interfaces;
using BookStore.Application.Features.AiSettings.Common;
using ErrorOr;
using MediatR;

namespace BookStore.Application.Features.AiSettings.Commands.TestAiConnection;

/// <summary>Sends a tiny prompt with the saved settings; returns the start of the model's reply.</summary>
public record TestAiConnectionCommand : IRequest<ErrorOr<string>>;

public class TestAiConnectionCommandHandler : IRequestHandler<TestAiConnectionCommand, ErrorOr<string>>
{
    private const int MaxReplyLength = 200;

    private readonly AiConnectionResolver _resolver;
    private readonly IAiTextGenerator _generator;

    public TestAiConnectionCommandHandler(AiConnectionResolver resolver, IAiTextGenerator generator)
    {
        _resolver = resolver;
        _generator = generator;
    }

    public async Task<ErrorOr<string>> Handle(TestAiConnectionCommand command, CancellationToken cancellationToken)
    {
        var resolved = await _resolver.ResolveAsync(cancellationToken);
        if (resolved.IsError)
        {
            return resolved.Errors;
        }

        var reply = await _generator.GenerateAsync(
            resolved.Value.Connection,
            "You are a connection test. Answer in Persian with one short sentence.",
            "Say hello to the bookstore admin in Persian.",
            cancellationToken);

        if (reply.IsError)
        {
            return reply.Errors;
        }

        var text = reply.Value.Trim();
        return text.Length > MaxReplyLength ? text[..MaxReplyLength] + "…" : text;
    }
}
