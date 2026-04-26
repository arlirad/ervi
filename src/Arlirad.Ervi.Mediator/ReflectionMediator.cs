using System.Collections.Concurrent;
using System.Reflection;
using Arlirad.Ervi.Mediator.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Arlirad.Ervi.Mediator;

public class ReflectionMediator(IServiceProvider serviceProvider) : IMediator
{
    public async ValueTask<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct)
    {
        var requestType = request.GetType();
        var handlerType = typeof(IRequestHandler<,>).MakeGenericType(requestType, typeof(TResponse));

        await using var scope = serviceProvider.CreateAsyncScope();
        var instance = scope.ServiceProvider.GetRequiredService(handlerType);

        var method = handlerType.GetMethod(nameof(IRequestHandler<IRequest<TResponse>, TResponse>.Handle))
            ?? throw new InvalidOperationException($"Handle method not found on {handlerType.FullName}");

        return await (ValueTask<TResponse>)method.Invoke(instance, [request, ct])!;
    }

    public async ValueTask Publish<TNotification>(TNotification notification, CancellationToken ct)
        where TNotification : INotification
    {
        var notificationType = notification.GetType();
        var handlerType = typeof(INotificationHandler<>).MakeGenericType(notificationType);

        await using var scope = serviceProvider.CreateAsyncScope();
        var instances = scope.ServiceProvider.GetServices(handlerType);

        var method = handlerType.GetMethod(nameof(INotificationHandler<INotification>.Handle))
            ?? throw new InvalidOperationException($"Handle method not found on {handlerType.FullName}");

        foreach (var instance in instances)
        {
            await (ValueTask)method.Invoke(instance, [notification, ct])!;
        }
    }

    internal static void RegisterHandlers(IServiceCollection services, Type root)
    {
        var assembly = root.Assembly;
        var types = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false });

        foreach (var type in types)
        {
            var interfaces = type.GetInterfaces();

            foreach (var iface in interfaces)
            {
                if (!iface.IsGenericType)
                    continue;

                var genericTypeDefinition = iface.GetGenericTypeDefinition();
                if (genericTypeDefinition == typeof(IRequestHandler<,>)
                    || genericTypeDefinition == typeof(INotificationHandler<>))
                    services.AddScoped(iface, type);
            }
        }
    }
}