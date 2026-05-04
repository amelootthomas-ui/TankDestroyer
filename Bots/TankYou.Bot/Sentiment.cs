namespace TankYou.Bot;

internal enum Sentiment
{
    // Flees from combat, prioritizing safety above all else
    Run,
    // Engages in combat, but only when it is safe to do so
    Cautious,
    // Engages in combat, even if it is not always safe to do so
    Brave,
    // Don't move, snipe from afar, and hope for the best
    Wait,
}
