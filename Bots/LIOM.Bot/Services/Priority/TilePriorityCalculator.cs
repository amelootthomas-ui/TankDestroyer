using LIOM.Bot.Objects;
using LIOM.Bot.Services.Location;
using LIOM.Bot.Services.Priority.Core;
using LIOM.Bot.Services.Tanks;
using TankDestroyer.API;

namespace LIOM.Bot.Services.Priority;

public class TilePriorityCalculator(IRelativeLocationCalculator relativeLocationCalculator, ITankService tankService, int minimalDistance)
    : BasePriorityCalculator, IPriorityCalculator
{
    private readonly IRelativeLocationCalculator _relativeLocationCalculator = relativeLocationCalculator;
    private readonly ITankService _tankService = tankService;
    private readonly int _minimalDistance = minimalDistance;

    public override double Calculate(ITurnContext turnContext, LiomDirection direction)
    {
        base.Calculate(turnContext, direction);
        
        var tile = turnContext.GetTile(Location!.X, Location!.Y);

        var score = 0;
        var penaltyScoreMinimum = 5;
        var penaltyScoreMaximum = 10;

        var random = new Random();
        var closestTank = _tankService.FindClosestTank();
        if (closestTank != null)
        {
           var relative = _relativeLocationCalculator.Calculate(Tank!, closestTank);
           if (Math.Abs(relative.RelativeX) < _minimalDistance || Math.Abs(relative.RelativeY) > _minimalDistance)
           {
               score -= Math.Abs(relative.RelativeX * relative.RelativeY * random.Next(penaltyScoreMinimum, penaltyScoreMaximum));
           }
        }
        
        score += tile.TileType switch
        {
            TileType.Water => -1000,
            TileType.Tree => 5,
            TileType.Building => 15,
            _ => 0
        };
        
        
        
        return score;
    }
   

    public override int Order() => 10;
}