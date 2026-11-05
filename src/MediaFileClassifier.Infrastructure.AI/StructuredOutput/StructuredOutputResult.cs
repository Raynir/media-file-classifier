namespace MediaFileClassifier.Infrastructure.AI.StructuredOutput;

/// <summary>
/// Result of parsing a model's structured answer.
/// </summary>
/// <typeparam name="T">Type of the value.</typeparam>
/// <param name="Value">The value, if the answer was usable.</param>
/// <param name="Error">Why the answer can't be used, if it can't.</param>
public sealed record StructuredOutputResult<T>(T? Value, string? Error)
    where T : class
{
    public bool IsSuccess => Value is not null;

    public static StructuredOutputResult<T> Success(T value) => new(value, null);

    public static StructuredOutputResult<T> Failure(string error) => new(null, error);
}
