namespace PanoramaVote.Core;

/// <summary>
///     Internal lifecycle contract for DI-registered modules, driven by <see cref="ModsharpPlugin" />.
///     Kept in Core (not the public .Shared) since nothing outside this plugin consumes it.
/// </summary>
internal interface IModule
{
    bool Init();
    void OnPostInit();
    void OnAllSharpModulesLoaded();
    void Shutdown();
}
