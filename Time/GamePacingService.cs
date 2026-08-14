using ProjectEve.Core.Time;

namespace ProjectEve.Time;

/// <summary>
/// Game UX pacing. The NPC can take realistic in-world minutes/hours while
/// the human player waits only seconds. This service NEVER changes game time.
/// </summary>
public sealed class GamePacingService : IGamePacingService
{
    private readonly object _rngLock = new();
    private readonly Random _rng = new();

    public TimeSpan ToRealDelay(TimeSpan simulatedDelay, GamePacingContext context)
    {
        var minutes = Math.Max(0, simulatedDelay.TotalMinutes);

        if (context.Urgent)
            return TimeSpan.FromSeconds(RandomBetween(1.0, 4.0));

        if (context.ActiveInteraction)
        {
            if (minutes <= 5) return TimeSpan.FromSeconds(RandomBetween(1.0, 3.0));
            if (minutes <= 20) return TimeSpan.FromSeconds(RandomBetween(3.0, 6.0));
            if (minutes <= 60) return TimeSpan.FromSeconds(RandomBetween(6.0, 12.0));
            if (minutes <= 120) return TimeSpan.FromSeconds(RandomBetween(10.0, 20.0));
            return TimeSpan.FromSeconds(RandomBetween(18.0, 30.0));
        }

        if (minutes <= 5) return TimeSpan.FromSeconds(RandomBetween(2.0, 5.0));
        if (minutes <= 20) return TimeSpan.FromSeconds(RandomBetween(5.0, 10.0));
        if (minutes <= 60) return TimeSpan.FromSeconds(RandomBetween(10.0, 20.0));
        if (minutes <= 120) return TimeSpan.FromSeconds(RandomBetween(15.0, 35.0));
        return TimeSpan.FromSeconds(RandomBetween(30.0, 60.0));
    }

    private double RandomBetween(double min, double max)
    {
        lock (_rngLock)
            return min + (_rng.NextDouble() * (max - min));
    }
}
