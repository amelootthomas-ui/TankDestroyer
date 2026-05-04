using LIOM.Bot.Objects;

namespace LIOM.Bot.Services.Turret;

public class TurretService() : ITurretService
{
    public LiomDirection CalculateDirection(RelativeLocation relativeLocation)
    {
        var degree = CalculateDegrees(relativeLocation.RelativeX, relativeLocation.RelativeY);
        return GetTurretDirection(degree);
    }

   private static double CalculateDegrees(int x, int y)
    {
        var radians = Math.Atan2(y, x);
        var degrees = radians * (180.0 / Math.PI);
        degrees = 90 - degrees;
        return (degrees + 360) % 360;
    }

    private static LiomDirection GetTurretDirection(double degrees)
    {
        return degrees switch
        {
            < 22.5 => LiomDirection.North,
            < 67.5 => LiomDirection.NorthEast,
            < 112.5 => LiomDirection.East,
            < 157.5 => LiomDirection.SouthEast,
            < 202.5 => LiomDirection.South,
            < 247.5 => LiomDirection.SouthWest,
            < 292.5 => LiomDirection.West,
            < 337.5 => LiomDirection.NorthWest,
            _ => LiomDirection.North
        };
    }
}