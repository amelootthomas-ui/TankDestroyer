using LIOM.Bot.Objects;
using TankDestroyer.API;

namespace LIOM.Bot.Services.Tanks;

public interface INextMovementService
{
    public Objects.Location GetNextLocation(ITank tank, LiomDirection direction);
}