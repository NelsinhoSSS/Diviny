using Diviny.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

builder.Services.AddHttpClient<ScryfallService>(c =>
{
    c.BaseAddress = new Uri("https://api.scryfall.com/");
    c.DefaultRequestHeaders.UserAgent.ParseAdd("Diviny/1.0");
    c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
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

builder.Services.AddScoped<ResultCacheService>();
builder.Services.AddScoped<DeckService>();
builder.Services.AddScoped<DeckStatsService>();
builder.Services.AddScoped<CardClassifierService>();
builder.Services.AddScoped<DeckAnalysisService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();