using LIOM.Bot.Objects;
using LIOM.Bot.Services.Location;
using TankDestroyer.API;

namespace LIOM.Bot.Services.Tanks;

public class TankService(int myTankId, IList<ITank> tanks, IRelativeLocationCalculator relativeLocationCalculator)
    : ITankService
{
    private IList<ITank> _tanks = tanks;
    private readonly int _myTankId = myTankId;
    private readonly IRelativeLocationCalculator _relativeLocationCalculator = relativeLocationCalculator;

    public IList<ITank> GetAllTanks(bool isEnemy = true)
    {
        if (isEnemy)
            return _tanks.Where(t => t.OwnerId != _myTankId).ToList();

        return _tanks;
    }

    public ITank? GetTankById(int id) => _tanks.FirstOrDefault(t => t.OwnerId == id);


    public ITank? GetTankByLocation(int x, int y, bool isEnemy = true)
    {
        var filteredTanks = GetAllTanks(isEnemy);
        return filteredTanks.FirstOrDefault(tank => tank.X == x && tank.Y == y);
    }

    public ITank? FindClosestTank()
    {
        var closestId = 0;
        var closestDistance = int.MaxValue;

        var enemyTanks = GetAllTanks();
        var ownTank = GetTankById(_myTankId);

        var locations = new Dictionary<int, RelativeLocation>();

        foreach (var enemyTank in enemyTanks)
        {
            locations[enemyTank.OwnerId] = _relativeLocationCalculator.Calculate(ownTank!, enemyTank);
        }

        foreach (var keyValuePair in locations)
        {
            var distance = CalculateDistance(keyValuePair.Value.RelativeX, keyValuePair.Value.RelativeY);
            var isDestroyed = GetTankById(keyValuePair.Key)?.Destroyed ?? true;

            if (distance >= closestDistance || isDestroyed) continue;

            closestId = keyValuePair.Key;
            closestDistance = distance;
        }

        return GetTankById(closestId);
    }

    public void UpdateTanks(IEnumerable<ITank> tanks)
    {
        _tanks = tanks.ToList();
    }

    private int CalculateDistance(int x, int y)
    {
        return Math.Abs(x) + Math.Abs(y);
    }
}