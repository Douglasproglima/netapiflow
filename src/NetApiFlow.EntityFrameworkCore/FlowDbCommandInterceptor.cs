using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace NetApiFlow.EntityFrameworkCore;

/// <summary>
/// Wraps every SQL command execution in a <see cref="ComponentType.Database"/> step. The operation
/// is recorded as "{CommandType} {MainTable}" (e.g. <c>"SELECT Orders"</c>); the full command text
/// is only attached, without parameter values, when <see cref="NetApiFlowOptions.CaptureSqlText"/>
/// is enabled. Parameter values are never recorded, under any configuration.
/// </summary>
public sealed partial class FlowDbCommandInterceptor(IOptions<NetApiFlowOptions> options) : DbCommandInterceptor
{
    private readonly ConcurrentDictionary<Guid, FlowStep> _steps = new();

    /// <inheritdoc />
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        StartStep(command, eventData);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        StartStep(command, eventData);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        CompleteStep(eventData);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        CompleteStep(eventData);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        StartStep(command, eventData);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        StartStep(command, eventData);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        CompleteStep(eventData);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        CompleteStep(eventData);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        StartStep(command, eventData);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        StartStep(command, eventData);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        CompleteStep(eventData);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
    {
        CompleteStep(eventData);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) => FailStep(eventData);

    /// <inheritdoc />
    public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        FailStep(eventData);
        return Task.CompletedTask;
    }

    private void StartStep(DbCommand command, CommandEventData eventData)
    {
        var step = NetApiFlow.Step("EntityFrameworkCore", DescribeCommand(command.CommandText), ComponentType.Database);
        if (options.Value.CaptureSqlText)
        {
            step.Tag("sql", command.CommandText);
        }

        _steps[eventData.CommandId] = step;
    }

    private void CompleteStep(CommandExecutedEventData eventData)
    {
        if (_steps.TryRemove(eventData.CommandId, out var step))
        {
            step.Dispose();
        }
    }

    private void FailStep(CommandErrorEventData eventData)
    {
        if (_steps.TryRemove(eventData.CommandId, out var step))
        {
            step.MarkError(eventData.Exception);
            step.Dispose();
        }
    }

    /// <summary>Derives "{CommandType} {MainTable}" from the command text, never the parameter values.</summary>
    internal static string DescribeCommand(string commandText)
    {
        var typeMatch = CommandTypeRegex().Match(commandText);
        var commandType = typeMatch.Success ? typeMatch.Groups[1].Value.ToUpperInvariant() : "SQL";

        var tableMatch = MainTableRegex().Match(commandText);
        var table = tableMatch.Success ? tableMatch.Groups[1].Value : "Unknown";

        return $"{commandType} {table}";
    }

    [GeneratedRegex(@"^\s*(SELECT|INSERT|UPDATE|DELETE)\b", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex CommandTypeRegex();

    [GeneratedRegex(@"\b(?:FROM|INTO|UPDATE)\s+[`""\[]?(\w+)[`""\]]?", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex MainTableRegex();
}
