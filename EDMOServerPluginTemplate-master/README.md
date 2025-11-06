# EDMO Plugin Template

This repository contains the C# and Python templates that can be used to make plugins/extensions for `ServerVNext`.

## C# or Python?

Each language has its own pros and cons, and which type of plugin you develop ultimately depends on the needs of the plugins. 

In general, C#/.NET plugins tend to be more performant, flexible, and has compile time checks; Python plugins are able to use the vast ecosystem of Python libraries, especially AI/ML/Data related libraries.

| Feature | C#/.NET | Python |
|  ---  | ---   | ---  |
|Background/Multiple threads| ✅ Supported out of the box |⁉️ Limited by GIL. Multiprocessing may be allowed. |
|Libraries| ✅ Obtainable via Nuget | ✅ Obtainable through Pip|
|Compile time checks| ✅ Out of the box | ❌ Relies on runtime checks|
|Project structuring| ✅ Typical .NET project structure | ❌ Limited to a single `.py` file per plugin|
|Forward compatibility| ✅ Supported, unless API changes | ✅ Supported, unless API changes | 

If possible, creating your plugin in C# is highly recommended if you require high plugin performance, and you do not require any Python only libraries. Python plugins are more susceptible to performance issues, but has more mature libraries.

> Note: Many Python libraries have C# bindings available on Nuget, such libraries include Torch, Numpy, OpenCV. Consider using these libraries in your C# plugin instead.

## Usage guide

### C# #

Most of the repository is set up to allow for development of an EDMO plugin in C#. The only file that isn't required is `EDMOPythonPlugin.py`.

#### Steps
##### Install .NET SDK
The .NET SDK is available for all major platforms and can be downloaded from [here](https://dotnet.microsoft.com/en-us/download)

##### Clone `ServerVNext`
`ServerVNext` contains the abstract class that is inherited by all `EDMOPlugins`. This allows for compile time check on correctness based on the target version. 

```sh
git clone https://github.com/TeamEDMO/ServerVNext
```

##### Add reference to the project
This project already has a project reference to `ServerVNext`, with the assumption that the repository is cloned to the parent directory of this project.

##### Create your plugin
> For the best experience developing for .NET, we recommend using [Visual Studio](https://visualstudio.microsoft.com/downloads/), [Jetbrains Rider](https://www.jetbrains.com/rider/), or [Visual Studio Code](https://code.visualstudio.com/).

Using the template code file `SampleEDMOPlugin.cs` as a base, you can start making your plugin. You may create helper classes as needed, as `ServerVNext` will only recognise classes that derives from `EDMOPlugin`. You may assign any valid class name to your plugin class.

> Note: You may include multiple classes that derives from `EDMOPlugin`, all of them will be recognised.

You may also choose to include packages/libraries from Nuget to enhance plugin functionality.

> Tip: You may include a `main()` method within your code base to test your plugins' functionality

##### Deploying your plugin
In order to deploy your plugin, one only needs to copy the build output to the `Plugins` folder of `ServerVNext`. The build output is typically in the form of `{YOUR_PLUGIN_NAME}.dll`, and can be found in folder (typically `./bin/[Debug|Release]/net9.0`). You will also need to copy any dependency DLLs that can be found in the output. 

> Note: You do not need to copy `ServerVNext.dll` to the plugins folder as that dependency is implicitly provided by `ServerVNext` itself.


### Python
#### Developing the plugin
`EDMOPythonPlugin.py` is the only file needed to create a plugin. It contains a class with many blank methods. Each of the methods map identically to their C# counterparts. 

One can (and should) remove unused methods from their plugin definition, as it avoids needless interop calls from C# which comes with some overhead.

Each `.py` file can only contain a single plugin class, and the class must be named `EDMOPythonPlugin`, but there is no limit to how many `.py` files that can be loaded. The `.py` files can be renamed to avoid conflicts and to describe itself.

Each `.py` file should be self contained, and _must not_ reference any other python file in the project directory. All helper functions and classes must be defined in the same file. However, plugins may utilise any Python library already installed into the global Python environment of the host system of `ServerVNext`.

#### Deploying the plugin
Simply copy the `.py` file for your plugin to the `Plugins` folder of `ServerVNext`. `ServerVNext` will automagically initialise a Python runtime in order to run the plugins.