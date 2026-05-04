using System.Text;
using TankDestroyer.API;

namespace HDJO.Bot;

[Bot("HDJO bot", "Hendrik", "F54927")]
public class HDJOBot : IPlayerBot
{
    private Random _random = new();

    public void DoTurn(ITurnContext turnContext)
    {
        var enumValues = Enum.GetValues<TurretDirection>();
        var enumDirectionValues = Enum.GetValues<Direction>();
        
        turnContext.MoveTank(enumDirectionValues[_random.Next(0, enumDirectionValues.Length)]);
        turnContext.RotateTurret(enumValues[_random.Next(0, enumValues.Length)]);
        turnContext.Fire();
    }
}