using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using PerformanceAgent.Core.Analysis;
using Xunit;

namespace PerformanceAgent.Cli.IntegrationTests;

/// <summary>
/// The real local UI (Kestrel on loopback, with all middleware) started in-process through
/// <see cref="LocalWebUi.StartAsync"/>. Provider resolution is counted so tests can prove when AI was (not) involved.
/// </summary>
internal sealed partial class UiServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClientHandler _handler;
    private readonly List<(string Provider, string? Model)> _resolutions = [];

    private UiServer(WebApplication app)
    {
        _app = app;
        Address = app.Urls.First();
        _handler = new HttpClientHandler { UseCookies = true, CookieContainer = new CookieContainer(), AllowAutoRedirect = false, UseProxy = false };
        Client = new HttpClient(_handler) { BaseAddress = new Uri(Address) };
    }

    public string Address { get; }
    public HttpClient Client { get; }

    /// <summary>Every (ai.provider, ai.model) pair passed to provider resolution.</summary>
    public IReadOnlyList<(string Provider, string? Model)> Resolutions => _resolutions;

    /// <param name="createProvider">Fake provider factory, or null for the production <see cref="AnalysisProviderFactory"/>.</param>
    public static async Task<UiServer> StartAsync(TestWorkspace workspace, Func<string, string?, IPerformanceAnalysisProvider>? createProvider)
    {
        UiServer? server = null;
        var factory = createProvider ?? AnalysisProviderFactory.Create;
        var app = await LocalWebUi.StartAsync(workspace.Storage, (provider, model) =>
        {
            server!._resolutions.Add((provider, model));
            return factory(provider, model);
        }, CancellationToken.None);
        server = new UiServer(app);
        return server;
    }

    public async Task<(HttpStatusCode Status, string Body, HttpResponseMessage Response)> SendAsync(HttpRequestMessage request)
    {
        var response = await Client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(), response);
    }

    public Task<(HttpStatusCode Status, string Body, HttpResponseMessage Response)> GetAsync(string path) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, path));

    /// <summary>Opens Run Details (sets the antiforgery cookie) and returns its form token.</summary>
    public async Task<string> TokenFromDetailsAsync(string runId)
    {
        var (status, body, _) = await GetAsync($"/runs/{Uri.EscapeDataString(runId)}");
        Assert.Equal(HttpStatusCode.OK, status);
        return WebUtility.HtmlDecode(TokenPattern().Match(body).Groups[1].Value);
    }

    public Task<(HttpStatusCode Status, string Body, HttpResponseMessage Response)> PostAnalyzeAsync(
        string runId, string? token, IDictionary<string, string>? headers = null)
    {
        var fields = new List<KeyValuePair<string?, string?>>();
        if (token is not null)
            fields.Add(new("__RequestVerificationToken", token));
        var request = new HttpRequestMessage(HttpMethod.Post, $"/runs/{Uri.EscapeDataString(runId)}/analyze")
        {
            Content = new FormUrlEncodedContent(fields),
        };
        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
        {
            if (name == "Host") request.Headers.Host = value;
            else request.Headers.TryAddWithoutValidation(name, value);
        }
        return SendAsync(request);
    }

    /// <summary>Opens Run Details, then clicks Analyze exactly as a browser form submission would.</summary>
    public async Task<(HttpStatusCode Status, string Body, HttpResponseMessage Response)> AnalyzeAsync(string runId) =>
        await PostAnalyzeAsync(runId, await TokenFromDetailsAsync(runId));

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        _handler.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();
}

/// <summary>Sets or clears an environment variable for one test and restores it afterwards.</summary>
internal sealed class EnvironmentVariableScope : IDisposable
{
    private readonly string _name;
    private readonly string? _previous;

    public EnvironmentVariableScope(string name, string? value)
    {
        _name = name;
        _previous = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
}

/// <summary>Tests that change process environment variables must not run alongside other tests.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection
{
    public const string Name = "Process environment";
}
