namespace PanoramaVote.Core;

using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PanoramaVote.Core.Managers;
using PanoramaVote.Shared;
using Sharp.Shared;

public sealed class ModsharpPlugin : IModSharpModule
{
    public string DisplayName   => "PanoramaVote Core";
    public string DisplayAuthor => "yappershq";

    private readonly IServiceProvider        _provider;
    private readonly ILogger<ModsharpPlugin> _logger;

    public ModsharpPlugin(
        ISharedSystem   sharedSystem,
        string?         dllPath,
        string?         sharpPath,
        Version?        version,
        IConfiguration? coreConfiguration,
        bool            hotReload)
    {
        ArgumentNullException.ThrowIfNull(sharedSystem);

        var loggerFactory = sharedSystem.GetLoggerFactory();

        _ = new InterfaceBridge(sharedSystem, loggerFactory);

        var services = new ServiceCollection();

        services.AddSingleton(sharedSystem);
        services.AddSingleton(loggerFactory);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSingleton(InterfaceBridge.Instance);

        services.AddModules();

        _provider = services.BuildServiceProvider();
        _logger   = _provider.GetRequiredService<ILogger<ModsharpPlugin>>();
    }

    public bool Init()
    {
        foreach (var module in _provider.GetServices<IModule>())
        {
            try
            {
                module.Init();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PanoramaVote] Error in Init for {Module}", module.GetType().Name);
            }
        }

        return true;
    }

    public void OnLibraryConnected(string name) { }
    public void OnLibraryDisconnect(string name) { }

    public void PostInit()
    {
        foreach (var module in _provider.GetServices<IModule>())
        {
            try
            {
                module.OnPostInit();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PanoramaVote] Error in OnPostInit for {Module}", module.GetType().Name);
            }
        }

        // Publish the service interface in PostInit so consumers can resolve it in OnAllModulesLoaded.
        var manager = _provider.GetRequiredService<PanoramaVoteManager>();
        InterfaceBridge.Instance.SharpModuleManager.RegisterSharpModuleInterface<IPanoramaVoteService>(
            this, IPanoramaVoteService.Identity, manager);
    }

    public void OnAllModulesLoaded()
    {
        foreach (var module in _provider.GetServices<IModule>())
        {
            try
            {
                module.OnAllSharpModulesLoaded();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PanoramaVote] Error in OnAllModulesLoaded for {Module}", module.GetType().Name);
            }
        }

        _logger.LogInformation("[PanoramaVote] Core loaded successfully");
    }

    public void Shutdown()
    {
        foreach (var module in _provider.GetServices<IModule>())
        {
            try
            {
                module.Shutdown();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PanoramaVote] Error in Shutdown for {Module}", module.GetType().Name);
            }
        }

        if (_provider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
