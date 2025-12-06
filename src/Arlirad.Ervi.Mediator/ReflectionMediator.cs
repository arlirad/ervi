using System.Collections.Concurrent;
using System.Reflection;
using Arlirad.Ervi.Mediator.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Arlirad.Ervi.Mediator;

public class ReflectionMediator(IServiceProvider serviceProvider, Type root) : IMediator
{
    private readonly ConcurrentDictionary<Type, List<Type>> _cachedNotificationHandlers = [];
    private readonly ConcurrentDictionary<Type, Type> _cachedRequestHandlers = [];
    private readonly Assembly _rootAssembly = root.Assembly;
    private readonly SemaphoreSlim _sem = new(1);

    public async ValueTask<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct)
    {
        var expectedInterface = typeof(IRequestHandler<,>).MakeGenericType(request.GetType(), typeof(TResponse));

        await using var scope = serviceProvider.CreateAsyncScope();

        var handler = await InstanceHandler<IRequest<TResponse>, TResponse>(scope, request.GetType());
        var handleMethod = expectedInterface.GetMethod("Handle")!;

        return await (ValueTask<TResponse>)handleMethod.Invoke(handler, [request, ct])!;
    }

    public async ValueTask Publish<TNotification>(TNotification notification, CancellationToken ct)
        where TNotification : INotification
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var handlers = await InstanceHandlers<TNotification>(scope);

        foreach (var handler in handlers)
        {
            await handler.Handle(notification, ct);
        }
    }

    public async ValueTask FlushHandler(Type handler)
    {
        await _sem.WaitAsync();

        var requestHandlerEntry = _cachedRequestHandlers.FirstOrDefault(kvp => kvp.Value == handler);
        if (requestHandlerEntry.Value is not null)
            _cachedRequestHandlers.TryRemove(requestHandlerEntry.Key, out _);

        var notificationHandlerEntry =
            _cachedNotificationHandlers.FirstOrDefault(kvp => kvp.Value.Any(v => v == handler));

        if (notificationHandlerEntry.Value is not null)
            _cachedNotificationHandlers.TryRemove(notificationHandlerEntry.Key, out _);

        _sem.Release();
    }

    private async ValueTask<object>
        InstanceHandler<TRequest, TResponse>(AsyncServiceScope scope, Type requestType)
        where TRequest : IRequest<TResponse>
    {
        var handlerType = await FindHandler(requestType, typeof(TResponse));

        return ActivatorUtilities.CreateInstance(scope.ServiceProvider,
            handlerType);
    }

    private async ValueTask<IEnumerable<INotificationHandler<TNotification>>>
        InstanceHandlers<TNotification>(AsyncServiceScope scope) where TNotification : INotification
    {
        return (await FindHandlers<TNotification>()).Select(h =>
            (INotificationHandler<TNotification>)ActivatorUtilities.CreateInstance(scope.ServiceProvider, h)
        );
    }

    private async ValueTask<Type> FindHandler(Type requestType, Type responseType)
    {
        var handlerType = typeof(IRequestHandler<,>).MakeGenericType(requestType, responseType);

        if (_cachedRequestHandlers.TryGetValue(handlerType, out var cachedType))
            return cachedType;

        await _sem.WaitAsync();

        if (_cachedRequestHandlers.TryGetValue(handlerType, out var cachedTypeLastChance))
        {
            _sem.Release();
            return cachedTypeLastChance;
        }

        var rootTypes = _rootAssembly.GetExportedTypes();
        var types = rootTypes.AsEnumerable();

        types = rootTypes.Aggregate(types, (current, type) => current.Concat(
            type.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)
        ));

        var handler = types
            .Where(t => t is { IsClass: true, IsAbstract: false, IsInterface: false })
            .FirstOrDefault(t => handlerType.IsAssignableFrom(t));

        if (handler is null)
        {
            _sem.Release();

            throw new InvalidOperationException(
                $"Failed to find a IRequestHandler<{requestType.FullName}, {responseType.FullName}>"
            );
        }

        _cachedRequestHandlers[handlerType] = handler;
        _sem.Release();

        return handler;
    }

    private async ValueTask<IEnumerable<Type>> FindHandlers<TNotification>() where TNotification : INotification
    {
        var handlerType = typeof(INotificationHandler<TNotification>);

        if (_cachedNotificationHandlers.TryGetValue(handlerType, out var cachedTypes))
            return cachedTypes;

        await _sem.WaitAsync();

        if (_cachedNotificationHandlers.TryGetValue(handlerType, out var cachedTypesLastChance))
        {
            _sem.Release();
            return cachedTypesLastChance;
        }

        var rootTypes = _rootAssembly.GetExportedTypes();
        var types = rootTypes.AsEnumerable();

        types = rootTypes.Aggregate(types, (current, type) => current.Concat(
            type.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)
        ));

        var handlers = types
            .Where(t => t is { IsClass: true, IsAbstract: false } && handlerType.IsAssignableFrom(t))
            .ToList();

        _cachedNotificationHandlers[handlerType] = handlers;
        _sem.Release();

        return handlers;
    }
}