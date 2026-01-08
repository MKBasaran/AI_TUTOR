# ServerVNext

ServerVNext is a robust, reliable, and modular server designed to run EDMO study sessions.

## Running the server
If you are just looking to run the server, grab the latest release for the hosting platform

| Windows 10+ ([x64](https://github.com/TeamEDMO/ServerVNext/releases/latest/download/EDMOFrontend_win-x64.zip), [arm64](https://github.com/TeamEDMO/ServerVNext/releases/latest/download/EDMOFrontend_win-arm64.zip)) | macOS 12+ ([Intel](https://github.com/TeamEDMO/ServerVNext/releases/latest/download/EDMOFrontend_osx-x64.zip), [Apple Silicon](https://github.com/TeamEDMO/ServerVNext/releases/latest/download/EDMOFrontend_osx-arm64.zip)) | Linux ([x64](https://github.com/TeamEDMO/ServerVNext/releases/latest/download/EDMOFrontend_linux-x64.zip), [arm64](https://github.com/TeamEDMO/ServerVNext/releases/latest/download/EDMOFrontend_linux-arm64.zip)) |
| -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |


Once downloaded and extracted, all you have to do is to run the frontend executable.

* Windows - Simply launch `EDMOFrontend.exe` using the file explorer or through the terminal
* Linux / MacOS - Launch `EDMOFrontend` via a file manager or terminal

> The server runs .NET 9 and ASP.NET core. The releases provided by this repo are self-contained, and do not require the user to install the runtimes.

Once the server is launched, clients can connect to it freely via the IP addess of the hosting computer. 

> By default the IP is `<host computer ip>:5000`

### Retrieving session data
Session data is stored in the `{Executable directory}/Logs/` directory. 

The directory layout of the logs folder is as follows

```
Logs/ # The general log folder
	{Server start date/time}/ # Folder for a server run
		Sessions/
			{Session Start Date}/
				{Robot identifier}/
					{Session Start time}/ # Marks a single session
						imu.log
						oscillator{i}.log
						user{i}.log
						session.log
		runtime.log
```

Each session log folder may also contain extra files if plugins are used.

> [There is a plugin to mirror session logs to another device on the network when a session ends.](https://github.com/TeamEDMO/FileSyncPlugin)


### Setting up plugins
The server provides support for arbitrary session plugins. They can be installed into the `Plugins/` directory located in the executable directory.

> The folder may not be present when downloading a release. One can manually create the plugins folder. The folder is also created when the server starts running.

Plugins can be provided as a .NET plugin, or a Python script. They can be dropped directly into the plugins folder.

For more specific information, [refer to the documentation on plugins](./ServerCore/docs/docs/EDMO/Plugins/overview.md).

#### .NET plugins
.NET plugins work out of the box, simply place the plugin DLL along with their dependency libraries into the plugins folder.

#### Python plugins
Python plugins require Python to be installed on the host system. By default, Python 3.12 is expected to be present on the system.

Python plugins are expected to be self-contained, and not reference other Python files in the plugins directory. They may import Python libraries, but they must be installed into the global python environment.

For more specific information, [refer to the PythonPluginLoader documentation](./ServerCore/docs/docs/EDMO/Plugins/loaders/pythonPluginLoader.md).

## Debugging and Developing

Some prerequisites are required before attempting to debug or develop:

* A desktop platform with the .NET 9 SDK or higher installed.
* An IDE with support for C#, providing auto completion and syntax highlighting. I recommend using [Visual Studio 2022](https://visualstudio.microsoft.com/), [Visual Studio Code](https://code.visualstudio.com/), or [Jetbrains Rider](https://www.jetbrains.com/rider/)
* The [node.js package manager](https://docs.npmjs.com/downloading-and-installing-node-js-and-npm) is installed
    + This is used to acquire node packages used by the frontend. And is automatically invoked during the build process.

### Downloading the source code

Clone the repository:

```sh
git clone https://github.com/TeamEDMO/ServerVNext
cd ServerVNext
```

To update the source code to the latest commit, run the following command inside the osu directory:

```sh
git pull
```

### Building

The solution contains 3 projects:

* `ServerCore` - The core library that contains functionality to interface with embedded devices, along with session management functionality.
* `EDMOFrontend` - The main executable that is used during the EDMO study programmes. Powered by ASP.NET Core and Blazor.
* `ServerCore.Tests` - Some testing on core functionality provided by `ServerCore`. This also contains some tests aimed at validating implementation compliance of an EDMO robot.

Your IDE should provide launch configuration for every non-library project.

You can also build each project individually using [`dotnet build`](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-build)

```sh
dotnet build `ServerCore`
```

or

```sh
dotnet build `EDMOFrontend
```

#### Building plugins

[Refer to the EDMO plugin template repository](https://github.com/TeamEDMO/EDMOServerPluginTemplate)

## Licence

ServerVNext is licensed under the [MIT licence](https://opensource.org/licenses/MIT). Please see the licence file for more information. tl;dr you can do whatever you want as long as you include the original copyright and license notice in any copy of the software/source.

Do take note that project dependencies may not share the same license.