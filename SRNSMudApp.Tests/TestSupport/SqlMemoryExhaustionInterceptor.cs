using System.Data.Common;
using System.Reflection;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using SRNSMudApp.Data;

namespace SRNSMudApp.Tests.TestSupport;

/// <summary>
///     SQL Server のワークスペースメモリ枯渇例外
///     (SqlException 701: 'There is insufficient system memory in resource pool 'default' to run this query.')
///     をシミュレートする EF Core コマンドインターセプター。
///     AsSplitQuery や多数の Include を含む重いクエリで発生するエラーシナリオをテストするために使用する。
/// </summary>
public sealed class SqlMemoryExhaustionInterceptor : DbCommandInterceptor
{
    private readonly Func<DbCommand, bool> _predicate;

    /// <summary>
    ///     コマンドテキストに特定の部分文字列が含まれる場合に例外を発生させるコンストラクタ。
    ///     targetPattern が null または空の場合はすべての Reader コマンドに対して例外を発生させる。
    /// </summary>
    /// <param name="targetPattern">捕捉対象とする SQL コマンドテキストの部分文字列（大文字小文字を区別しない）。</param>
    public SqlMemoryExhaustionInterceptor(string? targetPattern = null)
    {
        if (string.IsNullOrWhiteSpace(targetPattern))
        {
            _predicate = _ => true;
        }
        else
        {
            _predicate = cmd => cmd.CommandText.Contains(targetPattern, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    ///     カスタム述語に基づいて例外を発生させるコンストラクタ。
    /// </summary>
    /// <param name="predicate">コマンドを検証して例外を発生させるかどうかを判定する述語デリゲート。</param>
    public SqlMemoryExhaustionInterceptor(Func<DbCommand, bool> predicate)
    {
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (_predicate(command))
        {
            throw CreateSqlException(701, "There is insufficient system memory in resource pool 'default' to run this query.");
        }

        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    /// <summary>
    ///     インターセプターおよび SplitQuery を構成した DbContextOptions を生成するヘルパー。
    /// </summary>
    /// <param name="connectionString">データベース接続文字列。</param>
    /// <param name="interceptor">登録するコマンドインターセプター。</param>
    /// <returns>構成済みの <see cref="DbContextOptions{ApplicationDbContext}"/>。</returns>
    public static DbContextOptions<ApplicationDbContext> CreateOptions(
        string connectionString,
        DbCommandInterceptor interceptor)
    {
        return new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString, sqlOptions =>
            {
                sqlOptions.UseHierarchyId();
                sqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                sqlOptions.CommandTimeout(300);
            })
            .AddInterceptors(interceptor)
            .Options;
    }

    /// <summary>
    ///     Microsoft.Data.SqlClient.SqlException はパブリックコンストラクタを持たないため、
    ///     リフレクションを用いて指定のエラー番号とメッセージを持つ SqlException を生成する。
    /// </summary>
    /// <param name="number">SQL エラー番号 (例: 701)。</param>
    /// <param name="message">SQL エラーメッセージ。</param>
    /// <returns>生成された <see cref="SqlException"/> インスタンス。</returns>
    public static SqlException CreateSqlException(int number, string message)
    {
        var errorCollectionConstructor = typeof(SqlErrorCollection)
            .GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
        var errorCollection = (SqlErrorCollection)errorCollectionConstructor!.Invoke(null);

        var errorConstructors = typeof(SqlError)
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance);
        var errorConstructor = errorConstructors.OrderByDescending(c => c.GetParameters().Length).First();
        var errorParams = errorConstructor.GetParameters();
        var args = new object?[errorParams.Length];
        for (var i = 0; i < errorParams.Length; i++)
        {
            var pType = errorParams[i].ParameterType;
            if (pType == typeof(int))
            {
                args[i] = (errorParams[i].Name == "infoNumber" || i == 0) ? number : 1;
            }
            else if (pType == typeof(byte))
            {
                args[i] = (byte)17;
            }
            else if (pType == typeof(string))
            {
                args[i] = (errorParams[i].Name == "errorMessage" || i == 4) ? message : "mock";
            }
            else if (pType == typeof(uint))
            {
                args[i] = (uint)0;
            }
            else if (pType == typeof(Exception))
            {
                args[i] = null;
            }
            else if (pType == typeof(Guid))
            {
                args[i] = Guid.Empty;
            }
            else
            {
                args[i] = null;
            }
        }

        var error = (SqlError)errorConstructor.Invoke(args);

        var addMethod = typeof(SqlErrorCollection)
            .GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance, null, [typeof(SqlError)], null);
        addMethod!.Invoke(errorCollection, [error]);

        var createExceptionMethod = typeof(SqlException)
            .GetMethod("CreateException", BindingFlags.NonPublic | BindingFlags.Static, null, [typeof(SqlErrorCollection), typeof(string)], null);
        if (createExceptionMethod != null)
        {
            return (SqlException)createExceptionMethod.Invoke(null, [errorCollection, "16.0.1000"])!;
        }

        var exceptionConstructors = typeof(SqlException)
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance);
        var exceptionConstructor = exceptionConstructors.OrderByDescending(c => c.GetParameters().Length).First();
        var exArgs = new object?[exceptionConstructor.GetParameters().Length];
        exArgs[0] = message;
        exArgs[1] = errorCollection;
        return (SqlException)exceptionConstructor.Invoke(exArgs);
    }
}