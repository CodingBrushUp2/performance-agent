using System.Security.Cryptography;

namespace PerformanceAgent.Core.History;

public sealed class RunIdGenerator
{
    public string Create(DateTimeOffset timestamp)
    {
        Span<byte> random = stackalloc byte[8];
        RandomNumberGenerator.Fill(random);
        return $"run-{timestamp.UtcDateTime:yyyyMMddTHHmmssfffZ}-{Convert.ToHexString(random).ToLowerInvariant()}";
    }
}
