namespace InstallSentinel.Services;
using InstallSentinel.Services.Logging;

using InstallSentinel.Models;
using InstallSentinel.Services.Interfaces;
using InstallSentinel.Common;
using InstallSentinel.Common.Helpers;
using InstallSentinel.Configuration;
using System.Text.Json;
using Microsoft.Extensions.Options;

public sealed class VirusTotalService : IVirusTotalService
{
    private readonly HttpClient _httpClient;
    private readonly VirusTotalSettings _settings;
    private readonly SemaphoreSlim _rateLimiter = new(1, 1);
    private readonly AgentLogger _agentLogger;

    public VirusTotalService(HttpClient httpClient, IOptions<AppConfig> config, AgentLogger agentLogger)
    {
        _agentLogger = agentLogger;
        _httpClient = httpClient;
        _settings = config.Value.VirusTotal;

        _httpClient.BaseAddress = new Uri(_settings.BaseUrl);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(Constants.VirusTotal.UserAgent);
        _httpClient.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);

        if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("x-apikey", _settings.ApiKey);
        }
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.ApiKey) && _settings.Enabled;

    public async Task<VirusTotalReport?> ScanFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            return null;

        if (!File.Exists(filePath))
            throw new FileNotFoundException("File not found", filePath);

        var sha256 = await HashUtils.ComputeSha256Async(filePath, cancellationToken);
        return await ScanHashAsync(sha256, cancellationToken);
    }

    public async Task<VirusTotalReport?> ScanHashAsync(string sha256, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            return null;

        if (string.IsNullOrWhiteSpace(sha256) || sha256.Length != 64)
            throw new ArgumentException("Invalid SHA256 hash", nameof(sha256));

        await _rateLimiter.WaitAsync(cancellationToken);
        try
        {
            var url = $"/files/{sha256}";
            _agentLogger.Info("VT", $"Scanning hash: {sha256[..16]}...");
            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new VirusTotalReport
                {
                    Sha256 = sha256,
                    Resource = sha256,
                    Positives = 0,
                    Total = 0,
                    ScanDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                    Permalink = $"https://www.virustotal.com/gui/file/{sha256}",
                    DetailedResults = new Dictionary<string, object> { { "error", "File not found in VirusTotal database" } }
                };
            }

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseVirusTotalResponse(json, sha256);
        }
        finally
        {
            _rateLimiter.Release();
        }
    }

    private static VirusTotalReport ParseVirusTotalResponse(string json, string sha256)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var data = root.GetProperty("data");
        var attributes = data.GetProperty("attributes");

        var stats = attributes.GetProperty("last_analysis_stats");
        var positives = stats.GetProperty("malicious").GetInt32() + stats.GetProperty("suspicious").GetInt32();
        var total = stats.EnumerateObject().Sum(p => p.Value.GetInt32());

        var scanDate = DateTimeOffset.FromUnixTimeSeconds(attributes.GetProperty("last_analysis_date").GetInt64()).ToString("yyyy-MM-dd HH:mm:ss");
        var permalink = $"https://www.virustotal.com/gui/file/{sha256}";

        var detailedResults = new Dictionary<string, object>();
        if (attributes.TryGetProperty("last_analysis_results", out var results))
        {
            foreach (var engine in results.EnumerateObject())
            {
                detailedResults[engine.Name] = engine.Value.Clone();
            }
        }

        return new VirusTotalReport
        {
            Sha256 = sha256,
            Resource = sha256,
            Positives = positives,
            Total = total,
            ScanDate = scanDate,
            Permalink = permalink,
            DetailedResults = detailedResults
        };
    }
}