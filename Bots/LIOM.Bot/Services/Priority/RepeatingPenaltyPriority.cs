using System.Reflection.Metadata.Ecma335;
using LIOM.Bot.Objects;
using LIOM.Bot.Services.Priority.Core;
using TankDestroyer.API;

namespace LIOM.Bot.Services.Priority;

public class RepeatingPenaltyPriority(int historyAmount) : BasePriorityCalculator, IPriorityCalculator
{
    private readonly int _historyAmount = historyAmount;

    public override int Order() => 30;

    public override double Calculate(ITurnContext turnContext, LiomDirection direction, Dictionary<int, Objects.Location> locationHistory)
    {
        base.Calculate(turnContext, direction, locationHistory);
        
        var last = locationHistory.OrderByDescending(lh => lh.Key)
            .Take(_historyAmount)
            .Select(l=>l.Value)
            .ToList();

        var penalty = -2;
        
        var locationAmount =  last.Count(l=>l.Equals(Location));
        
        return penalty * locationAmount;
    }
}