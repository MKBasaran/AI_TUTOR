using System.Globalization;
using System.Text.Json.Serialization;
using EDMOFrontend.Components;
using EDMOFrontend.Services;
using ServerCore.EDMO;
using ServerCore.EDMO.Plugins.Loaders;
using ServerCore.Tutor;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
// Safety to ensure that the server always find the webroot, regardless of the method of execution.
Directory.SetCurrentDirectory(AppContext.BaseDirectory);

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://*:8080");

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

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

EDMOSessionManager manager = new()
{
    SessionPluginLoader = pluginLoader
};

manager.Start();

var tutorOptions = builder.Configuration.GetSection("Tutor").Get<TutorOptions>() ?? new TutorOptions();
tutorOptions.ResolveStuckDetectorPath(builder.Environment.ContentRootPath);

builder.Services
    .AddSingleton(manager)
    .AddSingleton(tutorOptions)
    .AddSingleton<IStuckDetector>(_ => new PythonStuckDetector(tutorOptions.StuckDetectorPath, tutorOptions.StuckWindow))
    .AddSingleton<HintGenerator>()
    .AddSingleton<ITrialLogger, TutorTrialLogger>()
    .AddSingleton<IStuckReportLogger, TutorStuckReportLogger>()
    .AddSingleton<TutorSessionManager>()
    .AddHttpClient()
    .AddScoped<TutorManager>()
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

app.MapPost("/api/trials", (TutorSessionManager tutor, TrialSubmission submission) =>
    tutor.SubmitTrial(submission));

app.MapGet("/api/tutor/status", (TutorSessionManager tutor, string session_id, string? leg_id) =>
    tutor.GetStatus(session_id, leg_id));

app.MapPost("/api/tutor/hint", (TutorSessionManager tutor, TutorHintRequest request) =>
    tutor.RequestHint(request.SessionId, request.LegId));

app.MapPost("/api/tutor/report-stuck", (TutorSessionManager tutor, TutorStuckReportRequest request) =>
    tutor.ReportStuck(request));

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
