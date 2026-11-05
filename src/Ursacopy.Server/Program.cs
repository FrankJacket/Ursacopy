using System.Reflection;
using Microsoft.AspNetCore.DataProtection;
using MudBlazor.Services;
using Ursacopy.Server;
using Ursacopy.Server.Components;
using Ursacopy.Server.Data;

var b = WebApplication.CreateBuilder(args);
b.Services.AddRazorComponents().AddInteractiveServerComponents();
b.Services.AddMudServices();
b.Services.AddSingleton<Db>();
b.Services.AddSingleton<Repo>();
b.Services.AddSingleton<JobRunner>();
b.Services.AddHostedService<Scheduler>();

var dataDir = Path.GetFullPath(b.Configuration["DataDir"] ?? "data");
Directory.CreateDirectory(dataDir);
// Keys kept with the data so that browser sessions survive a container update, whatever user runs the container.
b.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys"))).SetApplicationName("Ursacopy");

var app = b.Build();
app.Services.GetRequiredService<Db>().Init();
app.Services.GetRequiredService<Repo>().MarkInterruptedRuns();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapGet("/healthz", () => "ok");
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();

public static class AppInfo
{
    public static readonly string Version =
        (typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev").Split('+')[0];
}
