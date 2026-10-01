using System.Globalization;
using System.Text.Json;
using PerformanceAgent.Core.Evidence;
using PerformanceAgent.Core.History;
using Xunit;

namespace PerformanceAgent.Core.Tests;

public sealed class ArchiveIdentityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"perfagent-test-{Guid.NewGuid():N}");
    private static readonly DateTimeOffset Timestamp = DateTimeOffset.Parse("2026-09-30T12:00:00Z");
    private FileRunArchive Archive => new(_root);
    private static ArchivedBenchmarkRun Run(string id, string? commit = null) => new(id, Timestamp, commit, new BenchmarkEvidence("1.0", []));

    [Theory]
    [InlineData("run-test")]
    [InlineData("legacy")]
    [InlineData("custom.id")]
    [InlineData("Measurement 1")]
    [InlineData("日本語")]
    public async Task SafeCustomIds_AreReadableAndListed(string id)
    {
        await Archive.AppendAsync(Run(id));
        Assert.Equal(id, (await Archive.ReadAsync(id)).RunId);
        Assert.Equal(id, Assert.Single(await Archive.ListAsync()).RunId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("../escape")]
    [InlineData("..\\escape")]
    [InlineData("/absolute")]
    [InlineData("C:\\absolute")]
    [InlineData("id/name")]
    [InlineData("id\\name")]
    [InlineData("id:stream")]
    [InlineData("id*")]
    [InlineData("id?")]
    [InlineData("id\n")]
    [InlineData("id\0")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("id.")]
    [InlineData("id ")]
    [InlineData("CON")]
    [InlineData("nul.txt")]
    [InlineData("LPT1")]
    public async Task UnsafeIds_AreRejectedByBothOperationsWithoutCreatingFiles(string id)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Archive.AppendAsync(Run(id)));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Archive.ReadAsync(id));
        Assert.False(Directory.Exists(_root));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"runId\":\"../escape\"}")]
    [InlineData("{\"runId\":\"different\"}")]
    public async Task InvalidStoredIdentity_FailsClearlyInReadAndList(string json)
    {
        var directory = Path.Combine(_root, "archive");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "legacy.json"), json);
        var read = await Assert.ThrowsAsync<InvalidOperationException>(() => Archive.ReadAsync("legacy"));
        var list = await Assert.ThrowsAsync<InvalidOperationException>(() => Archive.ListAsync());
        Assert.Contains("legacy.json", read.Message);
        Assert.Contains("legacy.json", list.Message);
    }

    [Fact]
    public async Task ConcurrentCollision_PublishesExactlyOneCompleteRun()
    {
        var attempts = await Task.WhenAll(Enumerable.Range(0, 12).Select(async index =>
        {
            try { await Archive.AppendAsync(Run("collision", index.ToString(CultureInfo.InvariantCulture))); return index; }
            catch (InvalidOperationException) { return -1; }
        }));
        var winner = Assert.Single(attempts, index => index >= 0);
        Assert.Equal(winner.ToString(CultureInfo.InvariantCulture), (await Archive.ReadAsync("collision")).CommitSha);
        Assert.Single(await Archive.ListAsync());
        Assert.Single(Directory.EnumerateFiles(Path.Combine(_root, "archive")));
    }

    [Fact]
    public async Task Listing_IgnoresTemporaryFilesAndOrdersEqualTimestampsById()
    {
        await Archive.AppendAsync(Run("z-custom"));
        await Archive.AppendAsync(Run("a-custom"));
        await File.WriteAllTextAsync(Path.Combine(_root, "archive", ".incomplete.tmp"), "{broken");
        Assert.Equal(new[] { "a-custom", "z-custom" }, (await Archive.ListAsync()).Select(run => run.RunId));
    }

    [Theory]
    [InlineData("ar-SA")]
    [InlineData("fa-IR")]
    public async Task GeneratedIds_UseInvariantUtcAndRoundTrip(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var id = new RunIdGenerator().Create(Timestamp.ToOffset(TimeSpan.FromHours(3)));
            Assert.Matches("^run-20260930T120000000Z-[0-9a-f]{16}$", id);
            await Archive.AppendAsync(Run(id));
            Assert.Equal(id, Assert.Single(await Archive.ListAsync()).RunId);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
