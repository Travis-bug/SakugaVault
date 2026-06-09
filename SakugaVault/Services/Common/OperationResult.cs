namespace SakugaVault.Services.Common;

/// <summary>
/// Small service-layer result wrapper.
/// This lets application services report success or failure without pushing transport-specific details into the controller.
/// </summary>
public sealed record OperationResult<T>(
    bool Succeeded,
    T? Value,
    string? ErrorCode,
    string? ErrorMessage,
    TimeSpan? RetryAfter = null)
{
    public static OperationResult<T> Success(T value) => new(true, value, null, null);

    public static OperationResult<T> Failure(string errorCode, string errorMessage, TimeSpan? retryAfter = null) =>
        new(false, default, errorCode, errorMessage, retryAfter);
}
