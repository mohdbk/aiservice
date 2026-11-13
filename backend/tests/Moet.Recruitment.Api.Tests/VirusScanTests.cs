using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Infrastructure.Files;

namespace Moet.Recruitment.Api.Tests;

/// <summary>Runs only when CLAMAV_HOST points at a ClamAV daemon (CI starts one).</summary>
public sealed class ClamFactAttribute : FactAttribute
{
    public ClamFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CLAMAV_HOST"))) Skip = "Set CLAMAV_HOST to run the virus scanner test.";
    }
}

public class VirusScanTests
{
    static ClamAvScanner Scanner(string? host) => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["CLAMAV_HOST"] = host }).Build(),
        NullLogger<ClamAvScanner>.Instance);

    [Fact]
    public async Task Off_without_a_host()
    {
        var s = Scanner(null);
        Assert.Equal("off", await s.StateAsync());
        Assert.Equal(ScanOutcome.Off, (await s.ScanAsync("%PDF-1.4"u8.ToArray())).Outcome);
    }

    [ClamFact]
    public async Task Finds_the_test_virus_and_passes_a_clean_file()
    {
        var s = Scanner(Environment.GetEnvironmentVariable("CLAMAV_HOST"));
        Assert.Equal("ok", await s.StateAsync());
        // The standard EICAR test file (harmless), put together here so the source file itself is not flagged.
        var eicar = System.Text.Encoding.ASCII.GetBytes("X5O!P%@AP[4\\PZX54(P^)7CC)7}$" + "EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*");
        var (outcome, detail) = await s.ScanAsync(eicar);
        Assert.Equal(ScanOutcome.Infected, outcome);
        Assert.Contains("Eicar", detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ScanOutcome.Clean, (await s.ScanAsync(new byte[200_000])).Outcome);
        await Assert.ThrowsAsync<Moet.Recruitment.Domain.Common.DomainException>(() => ((IVirusScanner)s).CheckAsync(eicar));
    }
}
