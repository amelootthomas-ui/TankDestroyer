using LIOM.Bot.Objects;
using TankDestroyer.API;

namespace LIOM.Bot.Extensions;

public static class DirectionExtensions
{
    public static LiomDirection Transform(this Direction direction)
    {
        return direction switch
        {
            Direction.North => LiomDirection.South,
            Direction.East => LiomDirection.West,
            Direction.South => LiomDirection.North,
            Direction.West => LiomDirection.East,
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
        };
    }
    
    public static LiomDirection Transform(this TurretDirection direction)
    {
        return direction switch
        {
            TurretDirection.North => LiomDirection.South,
            TurretDirection.NorthEast => LiomDirection.SouthWest,
            TurretDirection.East => LiomDirection.West,
            TurretDirection.SouthEast => LiomDirection.NorthWest,
            TurretDirection.South => LiomDirection.North,
            TurretDirection.SouthWest => LiomDirection.NorthEast,
            TurretDirection.West => LiomDirection.East,
            TurretDirection.NorthWest => LiomDirection.SouthEast,
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
        };
    }
    
    public static Direction ToDirection(this LiomDirection direction)
    {
        return direction switch
        {
            LiomDirection.South => Direction.North,
            LiomDirection.West => Direction.East,
            LiomDirection.North => Direction.South,
            LiomDirection.East => Direction.West,
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
        };
    }

    public static TurretDirection ToTurretDirection(this LiomDirection direction)
    {
        return direction switch
        {
            LiomDirection.South     => TurretDirection.North,
            LiomDirection.SouthWest => TurretDirection.NorthEast,
            LiomDirection.West      => TurretDirection.East,
            LiomDirection.NorthWest => TurretDirection.SouthEast,
            LiomDirection.North     => TurretDirection.South,
            LiomDirection.NorthEast => TurretDirection.SouthWest,
            LiomDirection.East      => TurretDirection.West,
            LiomDirection.SouthEast => TurretDirection.NorthWest,
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
        };
    }
}