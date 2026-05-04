using LIOM.Bot.Objects;
using TankDestroyer.API;

namespace LIOM.Bot.Services.Priority.Core;

public interface IPriorityBase
{
    public double Calculate(ITurnContext turnContext, LiomDirection direction);
}