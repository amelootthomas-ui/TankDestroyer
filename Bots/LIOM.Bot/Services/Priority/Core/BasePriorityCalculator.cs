using LIOM.Bot.Objects;
using TankDestroyer.API;

namespace LIOM.Bot.Services.Priority.Core;

public class BasePriorityCalculator : IPriorityCalculator
{
    internal List<IBullet> Bullets = [];
    internal List<ITank> EnemyTanks = [];
    internal ITank? Tank { get; set; }
    internal Objects.Location? Location { get; set; }
    internal Objects.Location? CurrentLocation { get; set; }
    internal LiomDirection Direction { get; set; }

    private int PriorityDistance(double originalDistance, double distance)
    {
        var distancePriority = 1;

        if (originalDistance > distance)
        {
            distancePriority = 2;
        }
        else
        {
            distancePriority = -2;
        }

        var absoluteDistance = Math.Abs(distance);

        return (int)(distancePriority * absoluteDistance);
    }

    public virtual double Calculate(ITurnContext turnContext, LiomDirection direction)
    {
        Tank = turnContext.Tank;
        EnemyTanks = turnContext.GetTanks().Where(t => Tank.OwnerId != t.OwnerId).ToList();
        Bullets = turnContext.GetBullets().ToList();
        Direction = direction;

        var x = Tank!.X + (direction switch
        {
            LiomDirection.East => 1,
            LiomDirection.West => -1,
            _ => 0
        });

        var y = Tank!.Y + (direction switch
        {
            LiomDirection.South => 1,
            LiomDirection.North => -1,
            _ => 0
        });

        if (y < 0 || x < 0 || y > turnContext.GetMapHeight() || x > turnContext.GetMapWidth())
        {
            throw new ArgumentOutOfRangeException(nameof(Direction),"Direction would place tank out of bounds");
        }

        Location = new Objects.Location(x, y);
        CurrentLocation = new Objects.Location(Tank!.X, Tank!.Y);

        return 0;
    }

    public virtual int Order() => 0;

    public virtual double Calculate(ITurnContext turnContext, LiomDirection direction,
        Dictionary<int, Objects.Location> locationHistory) => Calculate(turnContext, direction);
}