using Arlirad.Ervi.Mediator.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Arlirad.Ervi.Mediator.Tests;

public class MediatorTests
{
    private static bool _useAlternateHandler;

    [SetUp]
    public void SetUp()
    {
        // reset notification call counters
        PingHandlerA.Called = 0;
        PingHandlerB.Called = 0;
        _useAlternateHandler = false;
    }

    [Test]
    public async Task Send_Request_Returns_Response_From_Handler()
    {
        var mediator = new ReflectionMediator(new ServiceCollection().BuildServiceProvider(), typeof(TestRoot));
        var sum = await mediator.Send(new AddNumbers { A = 2, B = 3 }, CancellationToken.None);
        Assert.That(sum, Is.EqualTo(5));
    }

    [Test]
    public void Send_Request_Throws_When_No_Handler_Found()
    {
        var mediator = new ReflectionMediator(new ServiceCollection().BuildServiceProvider(), typeof(TestRoot));
        // No handler for a different request type
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await mediator.Send(new UnregisteredRequest(), CancellationToken.None));
    }

    [Test]
    public void Send_Request_Respects_CancellationToken()
    {
        var mediator = new ReflectionMediator(new ServiceCollection().BuildServiceProvider(), typeof(TestRoot));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAsync<TaskCanceledException>(async () =>
            await mediator.Send(new AddNumbers { A = 1, B = 1 }, cts.Token));
    }

    [Test]
    public async Task Send_Notification_Invokes_All_Notification_Handlers()
    {
        var mediator = new ReflectionMediator(new ServiceCollection().BuildServiceProvider(), typeof(TestRoot));
        await mediator.Publish(new Pinged(), CancellationToken.None);
        Assert.That(PingHandlerA.Called, Is.EqualTo(1));
        Assert.That(PingHandlerB.Called, Is.EqualTo(1));
    }

    [Test]
    public async Task Send_Notification_With_No_Handlers_Is_NoOp()
    {
        var mediator = new ReflectionMediator(new ServiceCollection().BuildServiceProvider(), typeof(TestRoot));
        // another notification with no handlers in this assembly
        await mediator.Publish(new UnhandledNotification(), CancellationToken.None);
        Assert.Pass();
    }

    [Test]
    public void DependencyInjection_AddMediator_Registers_IMediator_Singleton()
    {
        var services = new ServiceCollection();
        services.AddMediator(typeof(TestRoot));
        var provider = services.BuildServiceProvider();

        var m1 = provider.GetRequiredService<IMediator>();
        var m2 = provider.GetRequiredService<IMediator>();

        Assert.That(m1, Is.SameAs(m2));
        Assert.That(m1, Is.InstanceOf<ReflectionMediator>());
    }

    [Test]
    public async Task FlushHandler_Invalidates_Handler_Cache_For_Given_Type()
    {
        var mediator = new ReflectionMediator(new ServiceCollection().BuildServiceProvider(), typeof(TestRoot));

        // Ensure initial behavior uses default (non-alternate)
        _useAlternateHandler = false;
        var result1 = await mediator.Send(new AddNumbers { A = 1, B = 1 }, CancellationToken.None);
        Assert.That(result1, Is.EqualTo(2));

        // Switch behavior and flush handler cache so a new handler instance is constructed
        _useAlternateHandler = true;
        await mediator.FlushHandler(typeof(AddNumbersHandler));

        var result2 = await mediator.Send(new AddNumbers { A = 1, B = 2 }, CancellationToken.None);
        Assert.That(result2, Is.EqualTo(100));
    }

    // Marker type to point mediator to this assembly for reflection
    private sealed class TestRoot
    {
    }

    // Simple Request/Response pair
    private sealed class AddNumbers : IRequest<int>
    {
        public required int A { get; init; }
        public required int B { get; init; }
    }

    private sealed class AddNumbersHandler : IRequestHandler<AddNumbers, int>
    {
        private readonly bool _alt = _useAlternateHandler;

        public ValueTask<int> Handle(AddNumbers request, CancellationToken ct)
        {
            return !ct.IsCancellationRequested
                ? ValueTask.FromResult(_alt ? 100 : request.A + request.B)
                : ValueTask.FromCanceled<int>(ct);
        }
    }

    // Notification and handlers
    private sealed class Pinged : INotification
    {
    }

    private sealed class PingHandlerA : INotificationHandler<Pinged>
    {
        public static int Called;

        public ValueTask Handle(Pinged notification, CancellationToken ct)
        {
            Interlocked.Increment(ref Called);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class PingHandlerB : INotificationHandler<Pinged>
    {
        public static int Called;

        public ValueTask Handle(Pinged notification, CancellationToken ct)
        {
            Interlocked.Increment(ref Called);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class UnregisteredRequest : IRequest<int>
    {
    }

    private sealed class UnhandledNotification : INotification
    {
    }
}