using System.Collections.Immutable;
using LanguageExt;

namespace ContextAll.Core;

public sealed record DecisionState(int Count);

public abstract record DecisionInput;

public sealed record AcceptCount : DecisionInput;

public sealed record RefuseCount : DecisionInput;

public abstract record Effect;

public sealed record RecordedCount(int Count) : Effect;

public sealed record Transition(DecisionState State, ImmutableArray<Effect> Effects)
{
    public bool Equals(Transition? other) =>
        other is not null
        && State == other.State
        && Effects.SequenceEqual(other.Effects);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(State);
        foreach (var effect in Effects)
        {
            hash.Add(effect);
        }

        return hash.ToHashCode();
    }
}

public static class Decision
{
    public static Fin<Transition> Decide(DecisionState state, DecisionInput input) =>
        input switch
        {
            RefuseCount => Fin.Fail<Transition>("refused"),
            AcceptCount => Fin.Succ(Accepted(state)),
            _ => Fin.Fail<Transition>("refused"),
        };

    static Transition Accepted(DecisionState state)
    {
        var next = state.Count + 1;
        return new Transition(
            new DecisionState(next),
            ImmutableArray.Create<Effect>(new RecordedCount(next)));
    }
}
