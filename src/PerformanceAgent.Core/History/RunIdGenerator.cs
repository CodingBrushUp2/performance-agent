using System.Security.Cryptography;
using System.Globalization;

namespace PerformanceAgent.Core.History;

public sealed class RunIdGenerator
{
    public string Create(DateTimeOffset timestamp)
    {
        Span<byte> random = stackalloc byte[8];
        RandomNumberGenerator.Fill(random);
        var utcTimestamp = timestamp.UtcDateTime.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture);
        return $"run-{utcTimestamp}-{Convert.ToHexString(random).ToLowerInvariant()}";
    }
}
