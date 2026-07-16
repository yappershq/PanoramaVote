namespace PanoramaVote.Core;

using Microsoft.Extensions.Logging;
using Sharp.Shared;
using Sharp.Shared.Managers;

/// <summary>
///     Captures the ModSharp managers this plugin needs off <see cref="ISharedSystem" /> once,
///     exposed through a single static instance (mirrors the house Core-plugin bootstrap).
/// </summary>
internal sealed class InterfaceBridge
{
    internal static InterfaceBridge Instance { get; private set; } = null!;

    internal IEntityManager      EntityManager      { get; }
    internal IClientManager      ClientManager      { get; }
    internal IEventManager       EventManager       { get; }
    internal IConVarManager       ConVarManager      { get; }
    internal IModSharp           ModSharp           { get; }
    internal ISharpModuleManager SharpModuleManager { get; }
    internal ILoggerFactory      LoggerFactory      { get; }

    public InterfaceBridge(ISharedSystem sharedSystem, ILoggerFactory loggerFactory)
    {
        Instance = this;

        EntityManager      = sharedSystem.GetEntityManager();
        ClientManager      = sharedSystem.GetClientManager();
        EventManager       = sharedSystem.GetEventManager();
        ConVarManager      = sharedSystem.GetConVarManager();
        ModSharp           = sharedSystem.GetModSharp();
        SharpModuleManager = sharedSystem.GetSharpModuleManager();
        LoggerFactory      = loggerFactory;
    }
}
