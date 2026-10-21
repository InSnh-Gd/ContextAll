using ContextAll.Core;
using LanguageExt;

namespace ContextAll.Effects;

public static class SharedEffects
{
    public static int PayloadCap() => CoreRules.MaxPayloadBytes;

    public static async ValueTask<Fin<Unit>> Run(
        Transition transition,
        Func<Effect, CancellationToken, ValueTask<Fin<Unit>>> perform,
        CancellationToken cancellationToken)
    {
        foreach (var effect in transition.Effects)
        {
            var outcome = await perform(effect, cancellationToken).ConfigureAwait(false);
            if (outcome.IsFail)
            {
                return outcome;
            }
        }

        return Fin.Succ(default(Unit));
    }
}

