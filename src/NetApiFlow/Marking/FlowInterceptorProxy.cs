using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace NetApiFlow.Marking;

internal class FlowInterceptorProxy : DispatchProxy
{
    private static readonly MethodInfo WrapTaskGenericMethod = typeof(FlowInterceptorProxy)
        .GetMethod(nameof(WrapTaskGeneric), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo WrapValueTaskGenericMethod = typeof(FlowInterceptorProxy)
        .GetMethod(nameof(WrapValueTaskGeneric), BindingFlags.NonPublic | BindingFlags.Static)!;

    private object _target = null!;
    private string _component = null!;
    private ComponentType _componentType;

    internal object Target => _target;

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

        // Captured so Activity.Current can be restored once the synchronous part of this call
        // returns, instead of staying pinned to this step until it completes asynchronously later
        // (which would make unrelated concurrent calls parent themselves under this step by mistake).
        var callerActivity = Activity.Current;
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
            var wrapped = WrapTask((Task)result!, step);
            Activity.Current = callerActivity;
            return wrapped;
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var elementType = returnType.GetGenericArguments()[0];
            var wrapped = WrapTaskGenericMethod.MakeGenericMethod(elementType).Invoke(null, [result, step]);
            Activity.Current = callerActivity;
            return wrapped;
        }

        if (returnType == typeof(ValueTask))
        {
#pragma warning disable CA2012 // The boxed ValueTask from reflection is passed straight through to WrapValueTask, which consumes it exactly once via AsTask().
            var wrapped = WrapValueTask((ValueTask)result!, step);
#pragma warning restore CA2012
            Activity.Current = callerActivity;
            return wrapped;
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var elementType = returnType.GetGenericArguments()[0];
            var wrapped = WrapValueTaskGenericMethod.MakeGenericMethod(elementType).Invoke(null, [result, step]);
            Activity.Current = callerActivity;
            return wrapped;
        }

        step.Dispose();
        return result;
    }

    private static async Task WrapTask(Task task, FlowStep step)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        finally
        {
            CloseSpan(task, step);
        }
    }

    private static async Task<T> WrapTaskGeneric<T>(Task<T> task, FlowStep step)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        finally
        {
            CloseSpan(task, step);
        }
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
        else if (task.IsCanceled)
        {
            step.MarkError(CancellationOf(task));
        }

        step.Dispose();
    }

    private static OperationCanceledException CancellationOf(Task canceled)
    {
        try
        {
            canceled.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException cancellation)
        {
            return cancellation;
        }

        return new TaskCanceledException(canceled);
    }
}
