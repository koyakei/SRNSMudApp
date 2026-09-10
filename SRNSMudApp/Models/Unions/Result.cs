using System.Diagnostics.CodeAnalysis;

namespace SRNSMudApp.Models.Unions;

public record Success<T>(T Value);
public record Failure(string ErrorMessage);

[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Union type handled by C# compiler")]
public readonly union Result<T>(Success<T>, Failure);

/// <summary>
///     Result 型の生成を補助する静的ファクトリクラス。
/// </summary>
public static class Result
{
    public static Result<bool> Ok() => new Success<bool>(true);

    public static Result<T> Ok<T>(T value) => new Success<T>(value);

    public static Result<bool> Fail(string errorMessage) => new Failure(errorMessage);

    public static Result<T> Fail<T>(string errorMessage) => new Failure(errorMessage);
}