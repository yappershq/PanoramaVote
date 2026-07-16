namespace PanoramaVote.Core;

using Microsoft.Extensions.DependencyInjection;
using PanoramaVote.Core.Managers;

internal static class ModuleDependencyInjection
{
    internal static IServiceCollection AddModules(this IServiceCollection services)
    {
        // Single manager is both the lifecycle module and the published service implementation.
        services.AddSingleton<PanoramaVoteManager>();
        services.AddSingleton<IModule>(sp => sp.GetRequiredService<PanoramaVoteManager>());

        return services;
    }
}
