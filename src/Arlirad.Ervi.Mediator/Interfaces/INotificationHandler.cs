namespace Arlirad.Ervi.Mediator.Interfaces;

public interface INotificationHandler<in TNotification> where TNotification : INotification
{
    ValueTask Handle(TNotification notification, CancellationToken ct);
}