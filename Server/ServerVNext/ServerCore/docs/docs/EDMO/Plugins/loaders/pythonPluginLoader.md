# Python plugin loader

This plugin loader attempts to load python scripts using [Python.NET](https://pythonnet.github.io/).

This loader will attempt to load all `.py` script files in the plugin directory. It will then attempt to find the initialiser for the plugin class.

## Python runtime

By default, the loader will attempt to use Python 3.12 installed on the host system. This is platform agnostic, and the appropriate library name will be used.

|Operating System | Library string      |
|:---------------:|:-------------------:|
|Windows          |     python312.dll   |
|Linux            |   libpython3.12.so  |
|MacOS            | libpython3.12.dylib |

> Python 3.12 is what was found to be most reliable based on internal testing. Which is why it is the default fallback path.

One can override the loaded library by including a file `pythonDLL` in the plugin directory containing a path to the target library. Alternatively, one can also set the `PYTHONNET_PYDLL` environment variable to achieve the same effect. 
>  If both methods are used, the `pythonDLL` file takes precedence.

If no Python installation is found on the host system's (library) paths, then no Python plguins will be loaded.

[See also: Python.NET readme](https://github.com/pythonnet/pythonnet?tab=readme-ov-file#embedding-python-in-net)


## Format
A python plugin is a self-contained python script, containing a class named `EDMOPythonPlugin`, that implements one or more methods that can be called. 

> The naming is strict! The loader will only search for a class with that name. You may still define and use other classes.

The .NET wrapper type will perform python introspection to determine which methods are defined by the python type. This is an safety measure as the python class can't extend the .NET type, and relies on duck-typing to determine covariance. It also doubles as an optimisation measure, by determining the availability of methods, the host can avoid expensive interop calls to methods that don't exist.

The plugin is given a dynamic reference to the wrapper `EDMOPlugin` .NET type. This reference can be used to call [setter methods defined in EDMO plugins](../overview.md#control-methods).

[The template for the python plugin can be found here.](https://github.com/TeamEDMO/EDMOServerPluginTemplate)


## Dependency resolution

Ideally, plugin scripts should be self-contained whenever possible.

If the use of external library is desired, the server operator _must_ install said libraries into the global python environment via `pip install`.

If the external library is not available, then the Python runtime will eagerly throw, this will be caught during load, and will not be included in the plugin list.

