using System.Collections.Generic;
using System.IO;

namespace ServerCore.EDMO.Plugins.Loaders;

/// <summary>
/// Provides functionality to find plugins used to extend the functionality of an <see cref="EDMOSession"/>.
/// </summary>
public interface IPluginLoader
{
    /// <summary>
    /// Initialises this plugin loader. This will search for plugin definitions in the specified plugin directory, and may involve initialising a plugin runtime.
    /// </summary>
    /// <param name="pluginDirectory">The directory where the plugins are located.</param>
    /// <remarks>
    /// It is safe to initialise the same plugin loader instance multiple times, subsequent attempts will result in a no-op.
    /// </remarks>
    void Initialise(DirectoryInfo pluginDirectory);

    /// <summary>
    /// Construct plugins, injecting the <see cref="EDMOSession"/> into the plugins.
    /// </summary>
    /// <param name="session">The session provided to</param>
    /// <returns>An enumerable containing an ordered sequence of <see cref="EDMOPlugin"/>s.</returns>
    /// <remarks>
    /// If this plugin loader is not initialised properly, then this will return an empty enumerable.
    /// </remarks>
    IEnumerable<EDMOPlugin> CreatePluginsFor(EDMOSession session);
}
