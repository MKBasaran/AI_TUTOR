using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Python.Runtime;
using ServerCore.Logging;

namespace ServerCore.EDMO.Plugins.Loaders;

/// <summary>
/// A plugin loader implementation that loads Python script files.
/// <br/>
/// A python plugin is a self-contained <c>*.py</c> file, containing a single class <c>EDMOPythonPlugin</c>.
/// Python plugins are be wrapped in a wrapper C# class <see cref="EDMOPythonPlugin"/> that performs introspection of the python type to determine functionality.
///
/// <br/>
/// Python plugins may import external libraries that are installed in the global Python environment.
/// If a Python plugin imports a library not available in the global Python environment, then the plugin will be removed from the plugin list.
/// </summary>
///
/// <remarks>
/// <para>
/// By default, the plugin loader attempts to use Python 3.12 on the host system, automatically falling back to the appropriate library path.
/// <list type="table">
///     <listheader>
///         <term>OS</term>
///         <description> Plugin library string</description>
///     </listheader>
///     <item>
///         <term>Windows</term>
///         <description>python312.dll</description>
///     </item>
///     <item>
///         <term>Linux</term>
///         <description>libpython3.12.so</description>
///     </item>
///     <item>
///         <term>MacOS</term>
///         <description>libpython3.12.dylib</description>
///     </item>
/// </list>
///
/// One can override the loaded library by including a file <c>pythonDLL</c> in the plugin directory containing a path to the target library.
/// Alternatively, one can also set the <c>PYTHONNET_PYDLL</c> environment variable to achieve the same effect.
/// If both methods are used, the <c>pythonDLL</c> file takes precedence.
/// </para>
///
/// </remarks>
/// <seealso cref="EDMOPythonPlugin"/>
public class PythonPluginLoader : IPluginLoader
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
    public static readonly PythonPluginLoader INSTANCE = new();

    private bool isInitialised;

    private static readonly string default_python_lib = null!;

    static PythonPluginLoader()
    {
        // Check the OS platform
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            default_python_lib = "python312.dll";
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            default_python_lib = "libpython3.12.so";
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            default_python_lib = "libpython3.12.dylib";
    }

    private PythonPluginLoader()
    {
    }

    private List<(dynamic type, int priority)> plugins = [];
    private readonly List<PyModule> pluginScopes = [];

    /// <inheritdoc/>
    public void Initialise(DirectoryInfo pluginDirectory)
    {
        // Let's initialise python
        if (isInitialised)
            return;

        if (!pluginDirectory.Exists)
            return;

        StandardLogs.RUNTIME_LOGGER.Log("Initialising Python plugin loader.");

        string[] allFiles = [..pluginDirectory.EnumerateFiles().Select(f => f.FullName)];
        FileInfo[] pythonFiles = [.. pluginDirectory.EnumerateFiles("*.py")];
        if (pythonFiles.Length == 0)
        {
            StandardLogs.RUNTIME_LOGGER.Log("No \".py\" files found in plugin directory.");
            return;
        }

        if (!PythonEngine.IsInitialized)
        {
            StandardLogs.RUNTIME_LOGGER.Log("Initialising Python Engine");
            DirectoryInfo venvDir = new DirectoryInfo($"{pluginDirectory}/.venv");

            FileInfo pythonDeclarationFile = new FileInfo($"{pluginDirectory}/pythonDLL");
            string? pyDLLEnvVar = Environment.GetEnvironmentVariable("PYTHONNET_PYDLL");
            string dllstring = string.IsNullOrEmpty(pyDLLEnvVar) ? default_python_lib : pyDLLEnvVar;
            if (pythonDeclarationFile.Exists)
                dllstring = File.ReadAllText(pythonDeclarationFile.FullName).Trim();

            StandardLogs.RUNTIME_LOGGER.Log($"Target Python library: {dllstring}");

            Runtime.PythonDLL = dllstring;

            /* Investigate VENV bs later
            if (venvDir.Exists)
            {
                StandardLogs.Runtime.Log("Found .venv directory in plugin path, using it.");
                string path = (Environment.GetEnvironmentVariable("PATH") ?? "").TrimEnd(';');
                path = string.IsNullOrEmpty(path) ? venvDir.FullName : $"{path};{venvDir.FullName}";
                Environment.SetEnvironmentVariable("PATH", path, EnvironmentVariableTarget.Process);
                Environment.SetEnvironmentVariable("PYTHONHOME", venvDir.FullName, EnvironmentVariableTarget.Process);

                string sitePackages = Directory.EnumerateDirectories(venvDir.FullName, "site-packages",
                    SearchOption.AllDirectories).FirstOrDefault(venvDir.FullName);

                string libDir = new DirectoryInfo(sitePackages).Parent.FullName;

                string PythonPath = $"{sitePackages};{libDir}";
                Environment.SetEnvironmentVariable("PYTHONPATH", PythonPath, EnvironmentVariableTarget.Process);

                PythonEngine.PythonPath += PythonPath;
                PythonEngine.PythonHome = venvDir.FullName;

            }*/

            try
            {
                PythonEngine.Initialize();
                PythonEngine.BeginAllowThreads();
            }
            catch (Exception e)
            {
                StandardLogs.RUNTIME_LOGGER.Log(e.StackTrace);
                return;
            }
        }

        // Find any candidate python files in the root of plugins, and cache their class constructors
        foreach (var file in pythonFiles)
        {
            int priority = Array.IndexOf(allFiles, file.FullName);
            StandardLogs.RUNTIME_LOGGER.Log($"Found {file.Name} in plugins folder.");
            string code = File.ReadAllText(file.FullName);
            using var gil = Py.GIL();

            var compiledCode = PythonEngine.Compile(code);
            var pScope = Py.CreateScope();

            try
            {
                pScope.Execute(compiledCode);

                PyObject classCtor = pScope.Get("EDMOPythonPlugin");

                plugins.Add((classCtor, priority));
                pluginScopes.Add(pScope);
            }
            catch
            {
                StandardLogs.RUNTIME_LOGGER.Log("\tProblem executing plugin definition, not including in plugin list.",
                    LogLevel.Error);
            }
        }

        isInitialised = true;
        plugins = [.. plugins.OrderBy(p => p.priority)];
    }

    /// <inheritdoc/>
    public IEnumerable<EDMOPlugin> CreatePluginsFor(EDMOSession session)
    {
        for (int i = 0; i < plugins.Count; ++i)
        {
            var pluginEntry = plugins[i];

            dynamic pluginCtor = plugins[i].type;
            EDMOPythonPlugin? plugin;
            try
            {
                plugin = new EDMOPythonPlugin(session, pluginCtor);
            }
            catch
            {
                StandardLogs.RUNTIME_LOGGER.Log(
                    "Failed to initialise Python plugin. Python plugin will not be included in this session.",
                    LogLevel.Error);
                continue;
            }

            plugin.Priority = pluginEntry.priority;
            yield return plugin;
        }
    }
}
