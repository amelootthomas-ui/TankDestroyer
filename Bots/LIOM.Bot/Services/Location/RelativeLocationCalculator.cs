using LIOM.Bot.Objects;
using TankDestroyer.API;

namespace LIOM.Bot.Services.Location;

public class RelativeLocationCalculator: IRelativeLocationCalculator
{
    public RelativeLocation Calculate(ITank tank, ITank enemyTank)
    {
        return new RelativeLocation(tank.X - enemyTank.X, tank.Y - enemyTank.Y);
    }
}