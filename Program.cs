using System.Threading.RateLimiting;
using Diviny.Services;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// O Render fica na frente do site: lê o IP e o protocolo reais do visitante
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

// Limites por visitante (protegem a cota gratuita da IA, a do Scryfall e o processador)
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    o.AddPolicy("analise", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "anonimo",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(10),
            QueueLimit = 0
        }));

    o.AddPolicy("chat", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "anonimo",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(5),
            QueueLimit = 0
        }));

    o.AddPolicy("colecoes", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "anonimo",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(5),
            QueueLimit = 0
        }));

    o.AddPolicy("simulacao", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "anonimo",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 6,
            Window = TimeSpan.FromMinutes(10),
            QueueLimit = 0
        }));
});

builder.Services.AddHttpClient<ScryfallService>(c =>
{
    c.BaseAddress = new Uri("https://api.scryfall.com/");
    c.DefaultRequestHeaders.UserAgent.ParseAdd("Diviny/1.0");
    c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});

builder.Services.AddHttpClient<CollectionService>(c =>
{
    c.BaseAddress = new Uri("https://api.scryfall.com/");
    c.DefaultRequestHeaders.UserAgent.ParseAdd("Diviny/1.0");
    c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    c.Timeout = TimeSpan.FromSeconds(60);
});

builder.Services.AddHttpClient<CommanderSpellbookService>(c =>
{
    c.BaseAddress = new Uri("https://backend.commanderspellbook.com/");
    c.DefaultRequestHeaders.UserAgent.ParseAdd("Diviny/1.0");
    c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    c.Timeout = TimeSpan.FromSeconds(20);
});

builder.Services.AddHttpClient<AiService>(c =>
{
    c.BaseAddress = new Uri("https://api.groq.com/openai/");
    c.Timeout = TimeSpan.FromSeconds(180);
});

builder.Services.AddScoped<DeckService>();
builder.Services.AddScoped<DeckStatsService>();
builder.Services.AddScoped<CardClassifierService>();
builder.Services.AddScoped<DeckAnalysisService>();
builder.Services.AddScoped<ResultCacheService>();
builder.Services.AddScoped<DeckBuilderService>();
builder.Services.AddScoped<RulesService>();
builder.Services.AddScoped<RulesChatService>();
builder.Services.AddScoped<SimulationService>();

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
else
{
    // Na nuvem o Render já cuida do HTTPS
    app.UseHttpsRedirection();
}

app.UseRouting();
app.UseRateLimiter();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();