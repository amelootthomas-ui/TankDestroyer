using LIOM.Bot.Objects;
using LIOM.Bot.Services.Priority.Core;
using TankDestroyer.API;

namespace LIOM.Bot.Services.Priority;

public class PriorityDistanceCalculator : BasePriorityCalculator, IPriorityCalculator
{

    public override double Calculate(ITurnContext turnContext, LiomDirection direction,
        Dictionary<int, Objects.Location> locationHistory)
    {
        var weight = 10;
        base.Calculate(turnContext, direction, locationHistory);

        var numberOfEnemies = EnemyTanks.Where(IsCloser).ToList().Count();

        
        return numberOfEnemies * weight;
    }

    public override int Order() => 20;

    private bool IsCloser(ITank tank)
    {
        var differenceCurrentLocationX = CurrentLocation!.X - tank.X;
        var differenceCurrentLocationY = CurrentLocation!.Y - tank.Y;
        var differenceOtherLocationX = Location!.X - tank.X;
        var differenceOtherLocationY = Location!.Y - tank.Y;
        
        var isVertical = Direction is LiomDirection.South or LiomDirection.North;
        var isHorizonal = Direction is LiomDirection.East or LiomDirection.West;

        return !tank.Destroyed
               && (
                   (isHorizonal && IsCloserX(differenceCurrentLocationX, differenceOtherLocationX))
                   ||
                   (isVertical && IsCloserY(differenceCurrentLocationY, differenceOtherLocationY))
               );
    }

    private bool IsCloserX(int x1, int x2)
    {
        var differenceLocationX = Direction switch
        {
            LiomDirection.East => x1 - x2,
            LiomDirection.West => x2 - x1,
            _ => 0
        };

        return differenceLocationX == 1;
    }

    private bool IsCloserY(int y1, int y2)
    {
        var differenceLocationY = Direction switch
        {
            LiomDirection.South => y2 - y1,
            LiomDirection.North => y1 - y2,
            _ => 0
        };

        return differenceLocationY == 1;
    }
}