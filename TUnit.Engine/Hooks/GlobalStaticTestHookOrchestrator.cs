﻿using Microsoft.Testing.Platform.Extensions.TestFramework;
using TUnit.Core;
using TUnit.Core.Exceptions;
using TUnit.Engine.Helpers;
using TUnit.Engine.Services;

namespace TUnit.Engine.Hooks;

#if !DEBUG
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
#endif
public class GlobalStaticTestHookOrchestrator(HookMessagePublisher hookMessagePublisher)
{
    private static readonly List<(string Name, StaticHookMethod HookMethod, Func<TestContext, Task> Action)> SetUps = [];
    private static readonly List<(string Name, StaticHookMethod HookMethod, Func<TestContext, Task> Action)> CleanUps = [];

    private static readonly List<(string Name, StaticHookMethod HookMethod, LazyHook<ExecuteRequestContext, HookMessagePublisher> Action)> ClassSetUps = [];
    private static readonly List<(string Name, StaticHookMethod HookMethod, Func<ClassHookContext, Task> Action)> ClassCleanUps = [];
    
    private static readonly List<(string Name, StaticHookMethod HookMethod, LazyHook<ExecuteRequestContext, HookMessagePublisher> Action)> AssemblySetUps = [];
    private static readonly List<(string Name, StaticHookMethod HookMethod, Func<AssemblyHookContext, Task> Action)> AssemblyCleanUps = [];
    
    private static readonly List<(string Name, StaticHookMethod HookMethod, Func<BeforeTestDiscoveryContext, Task> Action)> BeforeTestDiscovery = [];
    private static readonly List<(string Name, StaticHookMethod HookMethod, Func<TestDiscoveryContext, Task> Action)> AfterTestDiscovery = [];
    
    private static readonly List<(string Name, StaticHookMethod HookMethod, Func<TestSessionContext, Task> Action)> BeforeTestSession = [];
    private static readonly List<(string Name, StaticHookMethod HookMethod, Func<TestSessionContext, Task> Action)> AfterTestSession = [];

    public async Task DiscoverHooks(ExecuteRequestContext context)
    {
        foreach (var (name, hookMethod, _) in ClassSetUps)
        {
            await hookMessagePublisher.Discover(context, $"Before Class: {name}", hookMethod);
        }
        
        foreach (var (name, hookMethod, _) in ClassCleanUps)
        {
            await hookMessagePublisher.Discover(context, $"After Class: {name}", hookMethod);
        }
        
        foreach (var (name, hookMethod, _) in AssemblySetUps)
        {
            await hookMessagePublisher.Discover(context, $"Before Assembly: {name}", hookMethod);
        }
        
        foreach (var (name, hookMethod, _) in AssemblyCleanUps)
        {
            await hookMessagePublisher.Discover(context, $"After Assembly: {name}", hookMethod);
        }
    }
    
    public static void RegisterBeforeHook(StaticHookMethod<TestContext> staticMethod)
    {
        SetUps.Add((staticMethod.Name, staticMethod, async context =>
        {
            var timeout = staticMethod.Timeout;

            try
            {
                await RunHelpers.RunWithTimeoutAsync(token => HookExecutorProvider.GetHookExecutor(staticMethod, context.InternalDiscoveredTest).ExecuteBeforeTestHook(staticMethod.MethodInfo, context, () => staticMethod.Body(context, token)), timeout);
            }
            catch (Exception e)
            {
                throw new BeforeTestException($"Error executing BeforeEvery(Test) method: {staticMethod.Name}", e);
            }
        }));
    }

    public static void RegisterAfterHook(StaticHookMethod<TestContext> staticMethod)
    {
        CleanUps.Add((staticMethod.Name, staticMethod, async context =>
        {
            var timeout = staticMethod.Timeout;

            try
            {
                await RunHelpers.RunWithTimeoutAsync(token => HookExecutorProvider.GetHookExecutor(staticMethod, context.InternalDiscoveredTest).ExecuteAfterTestHook(staticMethod.MethodInfo, context, () => staticMethod.Body(context, token)), timeout);
            }
            catch (Exception e)
            {
                throw new AfterTestException($"Error executing AfterEvery(Test) method: {staticMethod.Name}", e);
            }
        }));
    }

    internal static async Task ExecuteBeforeHooks(DiscoveredTest discoveredTest)
    {
        foreach (var setUp in SetUps.OrderBy(x => x.HookMethod.Order))
        {
            await Timings.Record("Global Static Test Hook Set Up: " + setUp.Name, discoveredTest.TestContext, 
                () => setUp.Action(discoveredTest.TestContext));
        }
    }

    internal static async Task ExecuteAfterHooks(DiscoveredTest discoveredTest, List<Exception> cleanUpExceptions)
    {
        foreach (var cleanUp in CleanUps.OrderBy(x => x.HookMethod.Order))
        {
            await Timings.Record("Global Static Test Hook Clean Up: " + cleanUp.Name, discoveredTest.TestContext,
                () => RunHelpers.RunSafelyAsync(async () => await cleanUp.Action(discoveredTest.TestContext), cleanUpExceptions));
        }
    }
    
    public static void RegisterBeforeHook(StaticHookMethod<ClassHookContext> staticMethod)
    {
        ClassSetUps.Add((staticMethod.Name, staticMethod, new LazyHook<ExecuteRequestContext, HookMessagePublisher>(async (executeRequestContext, hookPublisher) =>
        {
            var timeout = staticMethod.Timeout;

            var classHookContext = ClassHookOrchestrator.GetClassHookContext(staticMethod.ClassType);

            try
            {
                await hookPublisher.Push(executeRequestContext, $"Before Class: {staticMethod.Name}",
                    staticMethod, () =>
                        RunHelpers.RunWithTimeoutAsync(
                            token => staticMethod.HookExecutor.ExecuteBeforeClassHook(staticMethod.MethodInfo,
                                classHookContext,
                                () => staticMethod.Body(classHookContext, token)), timeout)
                );
            }
            catch (Exception e)
            {
                throw new BeforeClassException($"Error executing Before(Class) method: {staticMethod.Name}", e);
            }
        })));
    }

    public static void RegisterAfterHook(StaticHookMethod<ClassHookContext> staticMethod)
    {
        ClassCleanUps.Add((staticMethod.Name, staticMethod, async context =>
        {
            var timeout = staticMethod.Timeout;

            try
            {
                await RunHelpers.RunWithTimeoutAsync(token => staticMethod.HookExecutor.ExecuteAfterClassHook(staticMethod.MethodInfo, context, () => staticMethod.Body(context, token)), timeout);
            }
            catch (Exception e)
            {
                throw new AfterClassException($"Error executing After(Class) method: {staticMethod.Name}", e);
            }
        }));
    }

    internal async Task ExecuteBeforeHooks(ExecuteRequestContext executeRequestContext, ClassHookContext context)
    {
        foreach (var setUp in ClassSetUps.OrderBy(x => x.HookMethod.Order))
        {
            await setUp.Action.Value(executeRequestContext, hookMessagePublisher);
        }
    }

    internal async Task ExecuteAfterHooks(ExecuteRequestContext executeRequestContext, ClassHookContext context, List<Exception> cleanUpExceptions)
    {
        foreach (var cleanUp in ClassCleanUps.OrderBy(x => x.HookMethod.Order))
        {
            await hookMessagePublisher.Push(executeRequestContext, $"After Class: {cleanUp.Name}", cleanUp.HookMethod, () => RunHelpers.RunSafelyAsync(() => cleanUp.Action(context), cleanUpExceptions));
        }
    }
    
    public static void RegisterBeforeHook(StaticHookMethod<AssemblyHookContext> staticMethod)
    {
        AssemblySetUps.Add((staticMethod.Name, staticMethod, new LazyHook<ExecuteRequestContext, HookMessagePublisher>(async (executeRequestContext, hookPublisher) =>
        {
            var timeout = staticMethod.Timeout;
            var assemblyHookContext = AssemblyHookOrchestrator.GetAssemblyHookContext(staticMethod.Assembly);

            try
            {
                await hookPublisher.Push(executeRequestContext, $"Before Assembly: {staticMethod.Name}",
                    staticMethod, () => 
                        RunHelpers.RunWithTimeoutAsync(
                            token => staticMethod.HookExecutor.ExecuteBeforeAssemblyHook(staticMethod.MethodInfo,
                                assemblyHookContext,
                                () => staticMethod.Body(assemblyHookContext, token)), timeout)
                );
            }
            catch (Exception e)
            {
                throw new BeforeAssemblyException($"Error executing Before(Assembly) method: {staticMethod.Name}",
                    e);
            }
        })));
    }

    public static void RegisterAfterHook(StaticHookMethod<AssemblyHookContext> staticMethod)
    {
        AssemblyCleanUps.Add((staticMethod.Name, staticMethod, async context =>
        {
            var timeout = staticMethod.Timeout;

            try
            {
                await RunHelpers.RunWithTimeoutAsync(token => staticMethod.HookExecutor.ExecuteAfterAssemblyHook(staticMethod.MethodInfo, context, () => staticMethod.Body(context, token)), timeout);
            }
            catch (Exception e)
            {
                throw new AfterAssemblyException($"Error executing After(Assembly) method: {staticMethod.Name}", e);
            }
        }));
    }

    internal async Task ExecuteBeforeHooks(ExecuteRequestContext executeRequestContext, AssemblyHookContext context)
    {
        foreach (var setUp in AssemblySetUps.OrderBy(x => x.HookMethod.Order))
        {
            await setUp.Action.Value(executeRequestContext, hookMessagePublisher);
        }
    }

    internal async Task ExecuteAfterHooks(ExecuteRequestContext executeRequestContext, AssemblyHookContext context,
        List<Exception> cleanUpExceptions)
    {
        foreach (var cleanUp in AssemblyCleanUps.OrderBy(x => x.HookMethod.Order))
        {
            await hookMessagePublisher.Push(executeRequestContext, $"After Assembly: {cleanUp.Name}", cleanUp.HookMethod, () => RunHelpers.RunSafelyAsync(() => cleanUp.Action(context), cleanUpExceptions));
        }
    }
    
    public static void RegisterBeforeHook(StaticHookMethod<BeforeTestDiscoveryContext> staticMethod)
    {
        BeforeTestDiscovery.Add((staticMethod.Name, staticMethod, async context =>
        {
            var timeout = staticMethod.Timeout;

            try
            {
                await RunHelpers.RunWithTimeoutAsync(token => staticMethod.HookExecutor.ExecuteBeforeTestDiscoveryHook(staticMethod.MethodInfo, () => staticMethod.Body(context, token)), timeout);
            }
            catch (Exception e)
            {
                throw new BeforeTestDiscoveryException($"Error executing Before(TestDiscovery) method: {staticMethod.Name}", e);
            }
        }));
    }

    public static void RegisterAfterHook(StaticHookMethod<TestDiscoveryContext> staticMethod)
    {
        AfterTestDiscovery.Add((staticMethod.Name, staticMethod, async context =>
        {
            var timeout = staticMethod.Timeout;

            try
            {
                await RunHelpers.RunWithTimeoutAsync(token => staticMethod.HookExecutor.ExecuteAfterTestDiscoveryHook(staticMethod.MethodInfo, context, () => staticMethod.Body(context, token)), timeout);
            }
            catch (Exception e)
            {
                throw new AfterTestDiscoveryException($"Error executing After(TestDiscovery) method: {staticMethod.Name}", e);
            }
        }));
    }

    public static async Task ExecuteBeforeHooks(BeforeTestDiscoveryContext context)
    {
        foreach (var setUp in BeforeTestDiscovery.OrderBy(x => x.HookMethod.Order))
        {
            await setUp.Action(context);
        }
    }

    public static async Task ExecuteAfterHooks(TestDiscoveryContext context)
    {
        List<Exception> exceptions = []; 
        
        foreach (var cleanUp in AfterTestDiscovery.OrderBy(x => x.HookMethod.Order))
        {
            await RunHelpers.RunSafelyAsync(() => cleanUp.Action(context), exceptions);
        }
        
        ExceptionsHelper.ThrowIfAny(exceptions);
    }
    
    public static void RegisterBeforeHook(StaticHookMethod<TestSessionContext> staticMethod)
    {
        BeforeTestSession.Add((staticMethod.Name, staticMethod, async context =>
        {
            var timeout = staticMethod.Timeout;

            try
            {
                await RunHelpers.RunWithTimeoutAsync(token => staticMethod.HookExecutor.ExecuteBeforeTestSessionHook(staticMethod.MethodInfo, context, () => staticMethod.Body(context, token)), timeout);
            }
            catch (Exception e)
            {
                throw new BeforeTestSessionException($"Error executing Before(TestSession) method: {staticMethod.Name}", e);
            }
        }));
    }

    public static void RegisterAfterHook(StaticHookMethod<TestSessionContext> staticMethod)
    {
        AfterTestSession.Add((staticMethod.Name, staticMethod, async context =>
        {
            var timeout = staticMethod.Timeout;

            try
            {
                await RunHelpers.RunWithTimeoutAsync(token => staticMethod.HookExecutor.ExecuteAfterTestSessionHook(staticMethod.MethodInfo, context, () => staticMethod.Body(context, token)), timeout);
            }
            catch (Exception e)
            {
                throw new AfterTestSessionException($"Error executing After(TestSession) method: {staticMethod.Name}", e);
            }
        }));
    }

    public static async Task ExecuteBeforeHooks(TestSessionContext context)
    {
        foreach (var setUp in BeforeTestSession.OrderBy(x => x.HookMethod.Order))
        {
            await setUp.Action(context);
        }
    }

    public static async Task ExecuteAfterHooks(TestSessionContext context)
    {
        List<Exception> exceptions = []; 

        foreach (var cleanUp in AfterTestSession.OrderBy(x => x.HookMethod.Order))
        {
            await RunHelpers.RunSafelyAsync(() => cleanUp.Action(context), exceptions);
        }
        
        ExceptionsHelper.ThrowIfAny(exceptions);
    }
}