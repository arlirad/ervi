namespace Arlirad.Ervi.Mediator.Interfaces;

public interface IMediator
{
    ValueTask<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct);
    ValueTask Publish<TNotification>(TNotification request, CancellationToken ct) where TNotification : INotification;
    ValueTask FlushHandler(Type handler);
}