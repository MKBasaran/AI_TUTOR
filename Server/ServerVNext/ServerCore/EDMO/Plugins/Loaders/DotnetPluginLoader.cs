using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using ServerCore.Logging;

namespace ServerCore.EDMO.Plugins.Loaders;

/// <summary>
/// A plugin loader implementation that loads managed .NET plugins.
/// <br/>
/// A .NET plugin is takes the form of a .NET library DLL.
/// <br/>
/// Each library may contain one or more types that extend <see cref="EDMOPlugin"/>.
/// <br/>
/// A plugin library may also reference external libraries, which will be resolved by the runtime by searching the same directory or via the hosts dependency list.
/// Runtime errors may occur if the dependency could not be resolved.
/// </summary>
/// <seealso cref="EDMOPlugin"/>
public class DotnetPluginLoader : IPluginLoader
{
    /// <summary>
    /// The singleton instance shared across the whole program.
    /// <br/>
    /// <br/>
    /// <inheritdoc cref="DotnetPluginLoader"/>
    /// </summary>
    /// <remarks>
    /// This instance must be initialised on every execution of the program.
    /// </remarks>
    public static readonly DotnetPluginLoader INSTANCE = new();

    private bool isInitialised;
    private AssemblyLoadContext assemblyLoadContext = null!;

    private List<(Type type, int priority)> pluginTypes = [];

    private DotnetPluginLoader() { }

    /// <inheritdoc/>
    public void Initialise(DirectoryInfo pluginDirectory)
    {
        if (isInitialised)
            return;

        if (!pluginDirectory.Exists)
            return;

        StandardLogs.RUNTIME_LOGGER.Log("Initialising Dotnet plugin loader.");
        string[] allPlugins = [..pluginDirectory.EnumerateFiles().Select(f => f.FullName)];
        FileInfo[] dllFiles = [.. pluginDirectory.EnumerateFiles("*.dll")];

        if (dllFiles.Length == 0)
        {
            StandardLogs.RUNTIME_LOGGER.Log("No DLLs found in plugin directory.");
            return;
        }

        assemblyLoadContext = new AssemblyLoadContext("ManagedPlugins");
        assemblyLoadContext.Resolving += resolvePluginDependencyAssembly;

        foreach (var dllFile in dllFiles)
        {
            try
            {
                StandardLogs.RUNTIME_LOGGER.Log($"Found {dllFile.Name} in plugins folder.");
                var assembly = assemblyLoadContext.LoadFromAssemblyPath(dllFile.FullName);

                int dllPriority = Array.IndexOf(allPlugins, dllFile.FullName);

                bool foundTypes = false;

                foreach (Type type in assembly.GetTypes())
                {
                    if (!typeof(EDMOPlugin).IsAssignableFrom(type)) continue;

                    StandardLogs.RUNTIME_LOGGER.Log($"\t{type.Name} found in loaded assembly.");

                    pluginTypes.Add((type, dllPriority));
                    foundTypes = true;
                }

                if (!foundTypes)
                    StandardLogs.RUNTIME_LOGGER.Log($"\tDLL file contains no types inheriting {nameof(EDMOPlugin)}");
            }
            catch(BadImageFormatException)
            {
                StandardLogs.RUNTIME_LOGGER.Log($"\t{dllFile.Name} is not a valid .NET assembly.");
            }
        }

        pluginTypes = [..pluginTypes.OrderBy(p => p.priority)];

        isInitialised = true;
    }

    /// <inheritdoc/>
    public IEnumerable<EDMOPlugin> CreatePluginsFor(EDMOSession session)
    {
        foreach ((var type, int priority) in pluginTypes)
        {
            var pluginInstance = (EDMOPlugin)Activator.CreateInstance(type, session)!;
            pluginInstance.Priority = priority;

            yield return pluginInstance;
        }
    }

    private Assembly? resolvePluginDependencyAssembly(AssemblyLoadContext? ctx, AssemblyName asmName)
    {
        // the requesting assembly may be located out of the executable's base directory, thus requiring manual resolving of its dependencies.
        // this attempts resolving the ruleset dependencies on game core and framework assemblies by returning assemblies with the same assembly name
        // already loaded in the AppDomain.
        var domainAssembly = AssemblyLoadContext.Default.Assemblies
            // Given name is always going to be equally-or-more qualified than the assembly name.
            .Where(a =>
            {
                string? name = a.GetName().Name;
                if (name == null)
                    return false;

                return asmName.Name!.Contains(name, StringComparison.Ordinal);
            }).MaxBy(a => a.GetName().Version);

        return domainAssembly ?? null;
    }
}
