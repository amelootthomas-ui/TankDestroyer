using LIOM.Bot.Objects;
using TankDestroyer.API;

namespace LIOM.Bot.Services.Location;

public interface IRelativeLocationCalculator
{
    public RelativeLocation Calculate(ITank tank, ITank enemyTank);
}