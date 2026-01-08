using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ServerCore.EDMO.Plugins.Loaders;

/// <summary>
/// A plugin loader that serves as a wrapper for multiple plugin loaders
/// <br/>
/// <inheritdoc cref="IPluginLoader"/>
/// </summary>
public class CompositePluginLoader : IPluginLoader
{
    /// <summary>
    /// The list of plugin loaders that are part of this composite loader.
    /// </summary>
    public required IPluginLoader[] PluginLoaders { get; init; }

    /// <inheritdoc/>
    public void Initialise(DirectoryInfo pluginDirectory)
    {
        foreach (var pluginLoader in PluginLoaders)
            pluginLoader.Initialise(pluginDirectory);
    }

    /// <inheritdoc/>
    public IEnumerable<EDMOPlugin> CreatePluginsFor(EDMOSession session) =>
        PluginLoaders.SelectMany(l => l.CreatePluginsFor(session)).OrderBy(p => p.Priority);
}
