using TankDestroyer.API;

namespace TankYou.Bot;

internal class MapCache
{
    public static MapCache Instance { get; } = new();

    public void UpdateMap(ITurnContext turnContext)
    {
    }
}
