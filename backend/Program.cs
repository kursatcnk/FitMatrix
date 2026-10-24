using FitMatrix.Api.Data;
using FitMatrix.Api.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddDbContext<FitMatrixDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("FitMatrix")));
builder.Services.AddScoped<CatalogImportService>();
builder.Services.Configure<IcecatOptions>(builder.Configuration.GetSection(IcecatOptions.SectionName));
builder.Services.AddHttpClient<IcecatProductLookupService>(client =>
{
    client.BaseAddress = new Uri("https://data.icecat.biz/");
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddHttpClient<AccountFreeProductLookupService>(client =>
{
    client.BaseAddress = new Uri("https://api.upcitemdb.com/");
    client.Timeout = TimeSpan.FromSeconds(25);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("FitMatrix/0.1 product-dimension-planner");
});
builder.Services.AddHttpClient<OpenWebProductLookupService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(25);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 FitMatrix/0.1");
    client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.8");
});
builder.Services.AddHttpClient<ProductGuruLookupService>(client =>
{
    client.BaseAddress = new Uri("https://myproduct.guru/");
    client.Timeout = TimeSpan.FromSeconds(25);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) FitMatrix/0.1");
    client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.8");
});
builder.Services.AddScoped<ProductDiscoveryService>();
builder.Services.AddScoped<DiscoveryCatalogWriter>();
builder.Services.AddHttpClient<ManufacturerSitemapReader>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) FitMatrix/0.1 catalog-sitemap-reader");
});
builder.Services.AddSingleton<CatalogHarvestService>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("product-search", limiter =>
    {
        limiter.PermitLimit = 20;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 2;
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});
builder.Services.AddCors(options =>
{
    options.AddPolicy("FitMatrixFrontend", policy =>
    {
        policy
            .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseCors("FitMatrixFrontend");
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<FitMatrixDbContext>();
    await dbContext.Database.MigrateAsync();
    await ExpandedCatalogSeed.SeedAsync(dbContext);
    await dbContext.ProductLookupCaches
        .Where(entry => !entry.NormalizedQuery.StartsWith(ProductDiscoveryService.CacheVersion) ||
                        entry.ExpiresAt < DateTimeOffset.UtcNow.AddDays(-1))
        .ExecuteDeleteAsync();
}

app.Run();
