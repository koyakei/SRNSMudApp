#region

using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

using MudBlazor.Services;

#endregion

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddMudServices();

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthenticationStateDeserialization();

// API クライアントサービスの登録
builder.Services.AddHttpClient<SRNSMudApp.Client.Services.IItemListApiClient, SRNSMudApp.Client.Services.ItemListApiClient>(client =>
{
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
});
builder.Services.AddScoped<SRNSMudApp.Client.Services.ITagApiClient>(sp => sp.GetRequiredService<SRNSMudApp.Client.Services.IItemListApiClient>());
builder.Services.AddScoped<SRNSMudApp.Client.Services.ITagProposalApiClient>(sp => sp.GetRequiredService<SRNSMudApp.Client.Services.IItemListApiClient>());
builder.Services.AddScoped<SRNSMudApp.Client.Services.ITagLedgerApiClient>(sp => sp.GetRequiredService<SRNSMudApp.Client.Services.IItemListApiClient>());
builder.Services.AddScoped<SRNSMudApp.Client.Services.ITaggingRequestApiClient>(sp => sp.GetRequiredService<SRNSMudApp.Client.Services.IItemListApiClient>());
builder.Services.AddTransient<SRNSMudApp.Client.Services.IDoubleSubmissionGuard, SRNSMudApp.Client.Services.DoubleSubmissionGuard>();

await builder.Build().RunAsync();