using System.Text.Json;

var evidence = new
{
    schemaVersion = "1.0",
    measurements = new[]
    {
        new
        {
            name = "Sample.Sum",
            meanNanoseconds = 42.5,
            allocatedBytesPerOperation = 0L
        }
    }
};

Console.WriteLine(JsonSerializer.Serialize(evidence));
