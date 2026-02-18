using WebUI.Components;
using WebUI.Services;

var builder = WebApplication.CreateBuilder(args);

// Blazor Server with interactive components
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Singleton order tracker — survives across Blazor circuits
builder.Services.AddSingleton<OrderTracker>();

// HTTP clients for each backend service using Aspire service discovery.
// The service names ("orders-api", etc.) match the resource names in AppHost.
builder.Services.AddHttpClient("orders", client =>
{
    client.BaseAddress = new Uri("https+http://orders-api");
})
.AddServiceDiscovery();

builder.Services.AddHttpClient("payments", client =>
{
    client.BaseAddress = new Uri("https+http://payments-api");
})
.AddServiceDiscovery();

builder.Services.AddHttpClient("accounting", client =>
{
    client.BaseAddress = new Uri("https+http://accounting-api");
})
.AddServiceDiscovery();

builder.Services.AddServiceDiscovery();

builder.Services.AddScoped<PaymentPlatformClient>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    return new PaymentPlatformClient(
        factory.CreateClient("orders"),
        factory.CreateClient("payments"),
        factory.CreateClient("accounting"));
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
