using TankDestroyer.API;

namespace LIOM.Bot.Services.Tanks;

public interface ITankService
{
    IList<ITank> GetAllTanks(bool isEnemy = true);
    ITank? GetTankById(int id);
    ITank? GetTankByLocation(int x, int y, bool isEnemy = true);
    ITank? FindClosestTank();
    void UpdateTanks(IEnumerable<ITank> tanks);
}