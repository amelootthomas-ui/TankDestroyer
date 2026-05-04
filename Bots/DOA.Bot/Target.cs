using TankDestroyer.API;

namespace DOA.Bot;

public record Target
{
    public ITank Tank { get; init; }

    public double Distance { get; init; }
    public TurretDirection[] Directions { get; init; }

    public Target((int Y, int X) myPosition, ITank target)
    {
        Tank = target;

        var diffY = myPosition.Y - target.Y;
        var diffX = myPosition.X - target.X;

        Distance = Math.Sqrt(Math.Pow(diffY, 2) + Math.Pow(diffX, 2));

        // note that East and West are flipped :(
        if (myPosition.Y == target.Y)
        {
            Directions = myPosition.X > target.X ? [TurretDirection.West] : [TurretDirection.East];
        }
        else if (myPosition.X == target.X)
        {
            Directions = myPosition.Y > target.Y ? [TurretDirection.South] : [TurretDirection.North];
        }
        else
        { 
            var angleInDegrees = Math.Round((Math.Atan2(diffY, diffX) * 180 / Math.PI + 360) % 360);

            Directions = angleInDegrees switch
            {
                315          => [TurretDirection.SouthWest],
                > 315 - 22.5 => [TurretDirection.SouthWest, TurretDirection.SouthWest],
                270          => [TurretDirection.South],
                > 270 - 22.5 => [TurretDirection.South, TurretDirection.South],
                225          => [TurretDirection.SouthEast],
                > 225 - 22.5 => [TurretDirection.SouthEast, TurretDirection.SouthEast],
                180          => [TurretDirection.East],
                > 180 - 22.5 => [TurretDirection.East, TurretDirection.East],
                135          => [TurretDirection.NorthEast],
                > 135 - 22.5 => [TurretDirection.NorthEast, TurretDirection.NorthEast],
                 90          => [TurretDirection.North],
                >  90 - 22.5 => [TurretDirection.North, TurretDirection.North],
                 45          => [TurretDirection.NorthWest],
                >  45 - 22.5 => [TurretDirection.NorthWest, TurretDirection.NorthWest],
                  0          => [TurretDirection.West],
                _            => [TurretDirection.West, TurretDirection.West]
            };
        }
    }
}