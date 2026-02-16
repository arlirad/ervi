using Arlirad.Ervi.Mediator.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Arlirad.Ervi.Mediator;

public static class DependencyInjection
{
    public static void AddMediator(this IServiceCollection services, Type type)
    {
        ReflectionMediator.RegisterHandlers(services, type);
        services.AddScoped<IMediator>(sp => new ReflectionMediator(sp));
    }
}