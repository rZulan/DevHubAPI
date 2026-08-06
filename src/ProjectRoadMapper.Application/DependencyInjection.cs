using Microsoft.Extensions.DependencyInjection;

namespace ProjectRoadMapper.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssemblyContaining(typeof(DependencyInjection)));

        return services;
    }
}
