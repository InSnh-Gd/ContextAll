using System.Collections.Immutable;
using ContextAll.Core;
using LanguageExt;

namespace ContextAll.Core.Tests;

public sealed class DecisionTests
{
    [Fact]
    public void Repeating_a_decision_returns_the_same_result_without_running_effects()
    {
        var calls = 0;
        Func<Effect, CancellationToken, ValueTask<Fin<Unit>>> perform = (_, _) =>
        {
            calls++;
            return new ValueTask<Fin<Unit>>(Fin.Succ(default(Unit)));
        };

        var start = new DecisionState(1);
        var input = new AcceptCount();
        var first = Decision.Decide(start, input);
        var second = Decision.Decide(start, input);
        var firstTransition = ValueOf(first);
        var secondTransition = ValueOf(second);

        Assert.Equal(firstTransition, secondTransition);
        Assert.Equal(Accepted(), firstTransition);
        Assert.Equal(0, calls);
        GC.KeepAlive(perform);
    }

    [Fact]
    public void A_refused_input_is_a_failure_and_leaves_the_accepted_state()
    {
        var state = new DecisionState(4);
        var thrown = Record.Exception(() => Decision.Decide(state, new RefuseCount()));
        var result = Decision.Decide(state, new RefuseCount());

        Assert.Null(thrown);
        Assert.True(result.IsFail);
        Assert.Equal(4, state.Count);
    }

    static Transition Accepted() =>
        new(new DecisionState(2), ImmutableArray.Create<Effect>(new RecordedCount(2)));

    static Transition ValueOf(Fin<Transition> result) =>
        result.Match(
            Succ: value => value,
            Fail: error => throw new Xunit.Sdk.XunitException(error.Message));
}
