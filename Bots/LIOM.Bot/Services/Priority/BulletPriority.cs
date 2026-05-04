using System.Diagnostics;
using LIOM.Bot.Extensions;
using LIOM.Bot.Objects;
using LIOM.Bot.Services.Priority.Core;
using TankDestroyer.API;

namespace LIOM.Bot.Services.Priority;

public class BulletPriority : BasePriorityCalculator, IPriorityCalculator
{
    public override int Order() => 50;

    public override double Calculate(ITurnContext turnContext, LiomDirection direction,
        Dictionary<int, Objects.Location> locationHistory)
    {
        base.Calculate(turnContext, direction, locationHistory);
        var height = turnContext.GetMapHeight();
        var width = turnContext.GetMapWidth();


        var directionY = Direction switch
        {
            LiomDirection.North => -1,
            LiomDirection.South => 1,
            _ => 0,
        };
        var directionX = Direction switch
        {
            LiomDirection.East => 1,
            LiomDirection.West => -1,
            _ => 0,
        };

        var nextLocation = new Objects.Location(Tank!.X+directionX,Tank!.Y+ directionY);
        var total = 0;

        foreach (var bullet in Bullets)
        {
            var trajectory = GetTrajectory(bullet, height, width);
            if (trajectory.Any(t=>t.X ==nextLocation.X && t.Y==nextLocation.Y))
            {
                total -= 200;
            } 
        }

        return 0;
    }

    private IList<Objects.Location> GetTrajectory(IBullet bullet, int height, int width)
    {
        var direction = bullet.Direction.Transform();
        var x = bullet.X;
        var y = bullet.Y;

        var modifierX = direction switch
        {
            LiomDirection.East => 1,
            LiomDirection.West => -1,
            LiomDirection.NorthEast => 1,
            LiomDirection.SouthEast => 1,
            LiomDirection.NorthWest => -1,
            LiomDirection.SouthWest => -1,
            _ => 0
        };

        var modifierY = direction switch
        {
            LiomDirection.North => -1,
            LiomDirection.South => 1,
            LiomDirection.NorthEast => -1,
            LiomDirection.SouthEast => 1,
            LiomDirection.NorthWest => -1,
            LiomDirection.SouthWest => 1,
            _ => 0
        };

        var locations = new List<Objects.Location>();
        var canGenerateLocation = true;

        while (canGenerateLocation)
        {
            x += modifierX;
            y += modifierY;

            if (x >= width || y >= height || x < 0 || y < 0)
            {
                canGenerateLocation = false;
                continue;
            }

            var location = new Objects.Location(x, y);
            locations.Add(location);
        }

        return locations;
    }
}