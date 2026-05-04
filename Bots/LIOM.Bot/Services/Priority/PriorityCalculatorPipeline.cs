using LIOM.Bot.Objects;
using LIOM.Bot.Services.Priority.Core;
using TankDestroyer.API;

namespace LIOM.Bot.Services.Priority;

public class PriorityCalculatorPipeline : IPriorityBase
{
    private readonly List<IPriorityCalculator> _calculators = [];
    private readonly Dictionary<int, Objects.Location> _previousLocations = new();
    internal void Add(IPriorityCalculator calculator) => _calculators.Add(calculator);
    internal void AddHistory(int turn, Objects.Location location) => _previousLocations.Add(turn, location);

    public double Calculate(ITurnContext turnContext, LiomDirection direction)
    {
        var total = 0.0;

        foreach (var priorityCalculator in _calculators.OrderBy(c => c.Order()).ToList())
        {
            try
            {
                var value = priorityCalculator.Calculate(turnContext, direction,_previousLocations);
                total += value;
            }
            catch (Exception e)
            {
                total -= 1000;
            }
        }
        
        return total;
    }
}