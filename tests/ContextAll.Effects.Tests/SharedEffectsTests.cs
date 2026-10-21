using System.Collections.Immutable;
using ContextAll.Core;
using ContextAll.Effects;
using LanguageExt;

namespace ContextAll.Effects.Tests;

public sealed class SharedEffectsTests
{
    [Fact]
    public async Task Dispatch_awaits_the_delegate_and_does_not_run_it_before_dispatch()
    {
        var calls = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource<Fin<Unit>>(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<Effect, CancellationToken, ValueTask<Fin<Unit>>> perform = async (_, _) =>
        {
            calls++;
            started.SetResult();
            return await gate.Task.ConfigureAwait(false);
        };
        var transition = new Transition(
            new DecisionState(1),
            ImmutableArray.Create<Effect>(new RecordedCount(1)));

        Assert.Equal(0, calls);

        var pending = SharedEffects.Run(transition, perform, CancellationToken.None).AsTask();
        var entered = await Task.WhenAny(started.Task, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.Same(started.Task, entered);
        Assert.Equal(1, calls);
        Assert.False(pending.IsCompleted);

        gate.SetResult(Fin.Succ(default(Unit)));
        var outcome = await pending;
        Assert.True(outcome.IsSucc);
    }
}
