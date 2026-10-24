using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace FitMatrix.Api.Services;

public sealed class BrowserRenderingOptions
{
    public const string SectionName = "BrowserRendering";

    public bool Enabled { get; set; } = true;

    // Windows dev machines already have Edge, so no browser download is needed. Elsewhere Playwright's own
    // Chromium is used when it has been installed (playwright install chromium); otherwise rendering switches off.
    public string? Channel { get; set; } = OperatingSystem.IsWindows() ? "msedge" : null;

    public int TimeoutSeconds { get; set; } = 45;
}

public sealed record RenderedPage(Uri FinalUri, string Html);

// Opens pages in a headless browser for manufacturers that load their specifications with JavaScript.
// One page renders at a time; the browser is started on first use and reused for the whole harvest.
public sealed partial class RenderedPageService(
    IOptions<BrowserRenderingOptions> options,
    ILogger<RenderedPageService> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private IPlaywright? playwright;
    private IBrowser? browser;
    private bool unavailable;

    public bool IsAvailable => options.Value.Enabled && !unavailable;

    public async Task<RenderedPage?> RenderAsync(Uri url, CancellationToken cancellationToken)
    {
        if (!IsAvailable) return null;
        await gate.WaitAsync(cancellationToken);
        try
        {
            var activeBrowser = await GetBrowserAsync();
            if (activeBrowser is null) return null;

            await using var context = await activeBrowser.NewContextAsync(new BrowserNewContextOptions
            {
                Locale = "en-US",
                ViewportSize = new ViewportSize { Width = 1366, Height = 900 }
            });
            var page = await context.NewPageAsync();
            var timeout = options.Value.TimeoutSeconds * 1000f;
            var response = await page.GotoAsync(url.AbsoluteUri, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = timeout });
            if (response?.Status == (int)HttpStatusCode.TooManyRequests)
                throw new HttpRequestException("Manufacturer site is rate limiting requests.", null, HttpStatusCode.TooManyRequests);
            if (response is null || response.Status >= 400) return null;

            await WaitQuietlyAsync(page);
            await OpenSpecificationSectionsAsync(page);

            var html = await page.ContentAsync();
            // Visible text keeps "Width: 105 cm" style lines that frameworks split across many elements.
            var text = await page.EvaluateAsync<string>("() => document.body ? document.body.innerText : ''");
            return new RenderedPage(new Uri(page.Url), $"{html}\n<pre data-fitmatrix-rendered-text>\n{WebUtility.HtmlEncode(text)}\n</pre>");
        }
        catch (PlaywrightException exception)
        {
            logger.LogInformation(exception, "Rendered page could not be read: {Url}", url);
            return null;
        }
        catch (TimeoutException exception)
        {
            logger.LogInformation(exception, "Rendered page timed out: {Url}", url);
            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<IBrowser?> GetBrowserAsync()
    {
        if (browser is { IsConnected: true }) return browser;
        try
        {
            playwright ??= await Playwright.CreateAsync();
            browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Channel = options.Value.Channel
            });
            return browser;
        }
        catch (PlaywrightException exception)
        {
            unavailable = true;
            logger.LogWarning(exception, "Headless browser is unavailable; JavaScript-rendered sources are skipped.");
            return null;
        }
    }

    private static async Task WaitQuietlyAsync(IPage page)
    {
        try
        {
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            // Analytics and chat widgets keep some pages busy forever; the product data is usually there by now.
        }
    }

    // Many stores hide measurements behind a "Specifications" or "Measurements" tab or drawer.
    private static async Task OpenSpecificationSectionsAsync(IPage page)
    {
        var toggles = page.Locator("button, summary, [role=button], [role=tab], a[href^='#']")
            .Filter(new LocatorFilterOptions { HasTextRegex = SpecificationToggleRegex() });
        var count = Math.Min(await toggles.CountAsync(), 4);
        for (var index = 0; index < count; index++)
        {
            try
            {
                var toggle = toggles.Nth(index);
                if (!await toggle.IsVisibleAsync()) continue;
                await toggle.ClickAsync(new LocatorClickOptions { Timeout = 3_000 });
                await page.WaitForTimeoutAsync(800);
            }
            catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
            {
                // A toggle that cannot be clicked is not fatal; the rest of the page is still read.
            }
        }
    }

    [GeneratedRegex(@"^\s*((see|view)\s+)?(all\s+)?(tech(nical)?\s+)?spec(ification)?s|^\s*(dimensions?(\s*/\s*weights?)?|measurements|product\s+(size|details|dimensions))\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex SpecificationToggleRegex();

    public async ValueTask DisposeAsync()
    {
        if (browser is not null) await browser.DisposeAsync();
        playwright?.Dispose();
        gate.Dispose();
    }
}
