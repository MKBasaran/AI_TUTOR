# .NET plugin loader

[This plugin loader uses `AssemblyLoadContext` to dynamically load assemblies.](https://learn.microsoft.com/en-us/dotnet/core/dependency-loading/understanding-assemblyloadcontext)

The loader will attempt to load all `.dll` assembly files in the specified plugin directory. It will then use reflection to find all types that extend `EDMOPlugin`.

## Format

.NET plugins is simply a library that contains classes that extend `EDMOPlugin`, and the plugin can opt in to events by overriding the corresponding methods.

Plugins developed in this format can reference external .NET libraries. This

## Dependency resolution

If a plugin references an external library, the runtime will attempt to load the external library using the following _relevant_ procedures

1. If the library is also referenced by the host program. Then the dependency is transitively resolved.
2. If the library is in the plugin folder, it'll be loaded.

If the runtime fails to load the external library (because the above procedure failed), then the host runtime will throw an exception _**upon first use**_.

> If an external library is referenced, but no methods/types from the library is every used, then there is no issues whatsoever.

