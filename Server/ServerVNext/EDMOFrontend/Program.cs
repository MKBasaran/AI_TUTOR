using System.Globalization;
using EDMOFrontend.Components;
using ServerCore.EDMO;
using ServerCore.EDMO.Plugins.Loaders;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
// Safety to ensure that the server always find the webroot, regardless of the method of execution.
Directory.SetCurrentDirectory(AppContext.BaseDirectory);

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://*:8080");

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
Console.WriteLine(Environment.CurrentDirectory);

DirectoryInfo pluginDir = new($"{AppContext.BaseDirectory}/Plugins/");

// Unconditionally create the plugin directory
// Allows operators to know immediately where to put plugins
Directory.CreateDirectory(pluginDir.FullName);

CompositePluginLoader pluginLoader = new()
{
    PluginLoaders = [DotnetPluginLoader.INSTANCE, PythonPluginLoader.INSTANCE]
};


pluginLoader.Initialise(pluginDir);

EDMOSessionManager manager = new EDMOSessionManager()
{
    SessionPluginLoader = pluginLoader
};

manager.Start();

builder.Services
    .AddSingleton(manager)
    .AddSingleton<LocalisationProvider>()
    .AddScoped<LocalisationContext>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAntiforgery();
app.UseStaticFiles();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
