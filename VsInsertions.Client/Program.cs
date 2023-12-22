using MudBlazor.Services;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using VsInsertions;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddSingleton<RepoStateManager>();
builder.Services.AddSingleton<HttpClient>(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddMudServices();

await builder.Build().RunAsync();
