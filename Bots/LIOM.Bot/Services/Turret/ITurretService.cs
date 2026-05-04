using LIOM.Bot.Objects;

namespace LIOM.Bot.Services.Turret;

public interface ITurretService
{
    public LiomDirection CalculateDirection(RelativeLocation relativeLocation);
}