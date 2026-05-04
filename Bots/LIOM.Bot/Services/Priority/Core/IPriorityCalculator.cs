using LIOM.Bot.Objects;
using TankDestroyer.API;

namespace LIOM.Bot.Services.Priority.Core;

internal interface IPriorityCalculator: IPriorityBase
{
    public int Order();
    public double Calculate(ITurnContext turnContext, LiomDirection direction, Dictionary<int, Objects.Location> locationHistory);
}