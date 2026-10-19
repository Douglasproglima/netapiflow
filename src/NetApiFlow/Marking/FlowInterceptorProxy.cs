using System.Reflection;
using System.Runtime.ExceptionServices;

namespace NetApiFlow.Marking;

/// <summary>
/// Wraps an interface implementation so every method call becomes a tracked step. Closes the span
/// when the underlying operation actually finishes, not when the method call returns (risco R-01).
/// </summary>
internal class FlowInterceptorProxy : DispatchProxy
{
    private static readonly MethodInfo WrapTaskGenericMethod = typeof(FlowInterceptorProxy)
        .GetMethod(nameof(WrapTaskGeneric), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo WrapValueTaskGenericMethod = typeof(FlowInterceptorProxy)
        .GetMethod(nameof(WrapValueTaskGeneric), BindingFlags.NonPublic | BindingFlags.Static)!;

    private object _target = null!;
    private string _component = null!;
    private ComponentType _componentType;

    /// <summary>Creates a proxy of <typeparamref name="TInterface"/> that tracks every call against <paramref name="target"/>.</summary>
    public static TInterface Create<TInterface, TImplementation>(TImplementation target, ComponentType componentType)
        where TInterface : class
        where TImplementation : notnull, TInterface
    {
        object proxyObject = Create<TInterface, FlowInterceptorProxy>()!;
        var proxy = (FlowInterceptorProxy)proxyObject;
        proxy._target = target;
        proxy._component = typeof(TImplementation).Name;
        proxy._componentType = componentType;
        return (TInterface)proxyObject;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        var step = NetApiFlow.Step(_component, targetMethod.Name, _componentType);

        object? result;
        try
        {
            result = targetMethod.Invoke(_target, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            step.MarkError(ex.InnerException);
            step.Dispose();
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }

        var returnType = targetMethod.ReturnType;

        if (returnType == typeof(void))
        {
            step.Dispose();
            return null;
        }

        if (returnType == typeof(Task))
        {
            return WrapTask((Task)result!, step);
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var elementType = returnType.GetGenericArguments()[0];
            return WrapTaskGenericMethod.MakeGenericMethod(elementType).Invoke(null, [result, step]);
        }

        if (returnType == typeof(ValueTask))
        {
#pragma warning disable CA2012 // The boxed ValueTask from reflection is passed straight through to WrapValueTask, which consumes it exactly once via AsTask().
            return WrapValueTask((ValueTask)result!, step);
#pragma warning restore CA2012
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var elementType = returnType.GetGenericArguments()[0];
            return WrapValueTaskGenericMethod.MakeGenericMethod(elementType).Invoke(null, [result, step]);
        }

        step.Dispose();
        return result;
    }

    /// <summary>
    /// Returns a continuation of <paramref name="task"/> that closes the span and only then
    /// re-exposes the original outcome, so anyone awaiting the returned task always observes the
    /// span as already closed (no race between this continuation and the caller's own await).
    /// </summary>
    private static Task WrapTask(Task task, FlowStep step)
    {
        return task.ContinueWith(
            static (completed, state) =>
            {
                CloseSpan(completed, (FlowStep)state!);
                completed.GetAwaiter().GetResult();
            },
            step,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static Task<T> WrapTaskGeneric<T>(Task<T> task, FlowStep step)
    {
        return task.ContinueWith(
            static (completed, state) =>
            {
                CloseSpan(completed, (FlowStep)state!);
                return completed.GetAwaiter().GetResult();
            },
            step,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static ValueTask WrapValueTask(ValueTask valueTask, FlowStep step)
    {
        // A ValueTask can only be consumed once, so it is always materialized into a Task here
        // (via AsTask()) rather than branching on IsCompleted and reusing the original struct.
        var task = valueTask.AsTask();
        return new ValueTask(WrapTask(task, step));
    }

    private static ValueTask<T> WrapValueTaskGeneric<T>(ValueTask<T> valueTask, FlowStep step)
    {
        var task = valueTask.AsTask();
        return new ValueTask<T>(WrapTaskGeneric(task, step));
    }

    private static void CloseSpan(Task task, FlowStep step)
    {
        if (task.IsFaulted && task.Exception is not null)
        {
            step.MarkError(task.Exception.GetBaseException());
        }

        step.Dispose();
    }
}
