using System.Text.Json;

namespace PerformanceAgent.Core.Budgets;

public sealed class JsonPerformanceBudgetReader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public PerformanceBudget Read(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Performance budget JSON cannot be empty.", nameof(json));

        PerformanceBudget budget;
        try
        {
            budget = JsonSerializer.Deserialize<PerformanceBudget>(json, Options)
                ?? throw new ArgumentException("Performance budget JSON must contain an object.", nameof(json));
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Performance budget JSON is invalid.", nameof(json), exception);
        }

        Validate(budget);
        return budget;
    }

    private static void Validate(PerformanceBudget budget)
    {
        ValidateThreshold(budget.MaxMeanRegressionPercent, nameof(budget.MaxMeanRegressionPercent));
        ValidateThreshold(budget.MaxAllocationRegressionPercent, nameof(budget.MaxAllocationRegressionPercent));

        if (budget.MaxMeanRegressionPercent is null && budget.MaxAllocationRegressionPercent is null)
            throw new ArgumentException("At least one performance budget threshold must be configured.");
    }

    private static void ValidateThreshold(double? value, string name)
    {
        if (value is not null && (!double.IsFinite(value.Value) || value.Value < 0))
            throw new ArgumentOutOfRangeException(name, "Performance budget thresholds must be finite, non-negative percentages.");
    }
}
