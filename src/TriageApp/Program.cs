using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.EntityFrameworkCore;
using TriageApp.Components;
using TriageApp.Data;
using TriageApp.Services;

// Load .env before anything else so all services can read the variables.
EnvLoader.Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();

// --- Microsoft auth (enabled only when credentials are present in .env) ---
var msClientId = Environment.GetEnvironmentVariable("MICROSOFT_CLIENT_ID");
var msClientSecret = Environment.GetEnvironmentVariable("MICROSOFT_CLIENT_SECRET");
var authEnabled = !string.IsNullOrWhiteSpace(msClientId) && !string.IsNullOrWhiteSpace(msClientSecret);

if (authEnabled)
{
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = MicrosoftAccountDefaults.AuthenticationScheme;
    })
    .AddCookie(options => options.LoginPath = "/login")
    .AddMicrosoftAccount(options =>
    {
        options.ClientId = msClientId!;
        options.ClientSecret = msClientSecret!;
    });
}
builder.Services.AddAuthorization();

// --- Application services ---
builder.Services.AddHttpClient();
builder.Services.AddScoped<IJiraService, JiraService>();
builder.Services.AddSingleton<ArchitectureClassifier>();
builder.Services.AddSingleton<SchemaService>();
builder.Services.AddSingleton<QuizEngine>();

// --- Database (SQL Server, with in-memory demo fallback) ---
var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")
    ?? @"Server=localhost\SQLEXPRESS;Database=TriageApp;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

var dbAvailable = await SqlServerChecker.TryConnectAsync(connectionString);

if (dbAvailable)
{
    builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
    builder.Services.AddScoped<ITriageStore, DbTriageStore>();
    builder.Services.AddScoped<IQuizStore, DbQuizStore>();
}
else
{
    builder.Services.AddSingleton<ITriageStore, InMemoryTriageStore>();
    builder.Services.AddSingleton<IQuizStore, InMemoryQuizStore>();
}

builder.Services.AddSingleton(new AppInfo
{
    AuthEnabled = authEnabled,
    DbAvailable = dbAvailable,
    JiraConfigured = JiraConfiguredFromEnv()
});

var app = builder.Build();

if (dbAvailable)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.UseStaticFiles();

if (authEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapGet("/login", (HttpContext ctx) =>
        Results.Challenge(new AuthenticationProperties { RedirectUri = "/" }));

    app.MapGet("/logout", (HttpContext ctx) =>
        Results.SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            [CookieAuthenticationDefaults.AuthenticationScheme]));
}

app.UseAntiforgery();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static bool JiraConfiguredFromEnv() =>
    new[] { "JIRA_BASE_URL", "JIRA_USER_EMAIL", "JIRA_API_TOKEN", "JIRA_PROJECT_KEY" }
        .All(v => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(v)));
