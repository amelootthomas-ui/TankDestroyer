using TankDestroyer.API;

namespace JorenS.Bot;

[Bot("Dodger", "Joren", "62FC03")]
public class DodgeBot : IPlayerBot
{
    private static readonly Random _random = new();
    private static readonly Coordinate[] _directions =
    [
        new Coordinate(0, -1),
        new Coordinate(0, 1),
        new Coordinate(-1, 0),
        new Coordinate(1, 0),
        new Coordinate(0, 0)
    ];

    public void DoTurn(ITurnContext context)
    {
        DodgeBulletOrMove(context);
        RotateRandomly(context);
        context.Fire();
    }

    private static void DodgeBulletOrMove(ITurnContext context)
    {
        var myPosition = new Coordinate(context.Tank.X, context.Tank.Y);
        var dangerTiles = GetDangerTiles(context);

        var inBulletDanger = IsInLineOfFire(myPosition, context);
        var inTankDanger = IsInTankLineOfFire(myPosition, context);
        var mustEscape = inBulletDanger || inTankDanger;

        var candidates = _directions
            .Select(d => new Coordinate(myPosition.X + d.X, myPosition.Y + d.Y))
            .Where(pos => IsInsideMap(context, pos))
            .Where(pos => IsWalkable(context, pos))
            .ToList();

        var safeMoves = candidates
            .Where(pos =>
                !IsInLineOfFire(pos, context) &&
                !IsInTankLineOfFire(pos, context))
            .OrderByDescending(pos => DistanceToClosestDanger(pos, dangerTiles))
            .ToList();

        Coordinate chosen;
        if (safeMoves.Count > 0)
        {
            chosen = safeMoves.First();
        }
        else if (mustEscape && candidates.Count > 0)
        {
            chosen = candidates
                .OrderBy(pos =>
                    (IsInLineOfFire(pos, context) || IsInTankLineOfFire(pos, context)) ? 1 : 0)
                .ThenByDescending(pos => DistanceToClosestDanger(pos, dangerTiles))
                .First();
        }
        else if (candidates.Count > 0)
        {
            chosen = candidates[_random.Next(candidates.Count)];
        }
        else
        {
            return;
        }

        MoveTo(context, myPosition, chosen);
    }

    private static HashSet<Coordinate> GetDangerTiles(ITurnContext context)
    {
        var dangerTiles = new HashSet<Coordinate>();
        var tanks = context.GetTanks()
            .Select(t => new Coordinate(t.X, t.Y))
            .ToHashSet();

        foreach (var bullet in context.GetBullets())
        {
            var (dx, dy) = GetBulletDelta(bullet.Direction);
            var bulletPosition = new Coordinate(bullet.X, bullet.Y);

            for (var i = 0; i < 6; i++)
            {
                bulletPosition = new Coordinate(bulletPosition.X + dx, bulletPosition.Y + dy);
                if (!IsInsideMap(context, bulletPosition))
                {
                    break;
                }

                dangerTiles.Add(bulletPosition);

                var tile = context.GetTile(bulletPosition.X, bulletPosition.Y);
                if (tile.TileType is TileType.Tree or TileType.Building)
                {
                    break;
                }

                if (tanks.Contains(bulletPosition))
                {
                    break;
                }
            }
        }

        return dangerTiles;
    }

    private static (int dx, int dy) GetBulletDelta(TurretDirection direction) => direction switch
    {
        TurretDirection.North => (0, -1),
        TurretDirection.South => (0, 1),
        TurretDirection.West => (-1, 0),
        TurretDirection.East => (1, 0),

        TurretDirection.NorthEast => (1, -1),
        TurretDirection.NorthWest => (-1, -1),
        TurretDirection.SouthEast => (1, 1),
        TurretDirection.SouthWest => (-1, 1),

        _ => (0, 0)
    };

    private static bool IsWalkable(ITurnContext context, Coordinate pos)
    {
        var tile = context.GetTile(pos.X, pos.Y);
        if (tile.TileType == TileType.Water)
        {
            return false;
        }

        foreach (var tank in context.GetTanks())
        {
            if (tank.X == pos.X && tank.Y == pos.Y)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsInsideMap(ITurnContext context, Coordinate pos)
        => pos.X >= 0
        && pos.Y >= 0
        && pos.X < context.GetMapWidth()
        && pos.Y < context.GetMapHeight();

    private static bool IsInLineOfFire(Coordinate pos, ITurnContext context)
    {
        var tanks = context.GetTanks()
            .Select(t => new Coordinate(t.X, t.Y))
            .ToHashSet();

        foreach (var bullet in context.GetBullets())
        {
            var (dx, dy) = GetBulletDelta(bullet.Direction);
            var current = new Coordinate(bullet.X, bullet.Y);

            for (var i = 1; i <= 6; i++)
            {
                current = new Coordinate(current.X + dx, current.Y + dy);

                if (!IsInsideMap(context, current))
                {
                    break;
                }

                if (current == pos)
                {
                    return true;
                }

                var tile = context.GetTile(current.X, current.Y);
                if (tile.TileType is TileType.Tree or TileType.Building)
                {
                    break;
                }

                if (tanks.Contains(current))
                {
                    break;
                }
            }
        }

        return false;
    }

    private static bool IsInTankLineOfFire(Coordinate pos, ITurnContext context)
    {
        var myTank = context.Tank;

        foreach (var tank in context.GetTanks())
        {
            if (tank.X == myTank.X && tank.Y == myTank.Y)
            {
                continue;
            }

            var dx = pos.X - tank.X;
            var dy = pos.Y - tank.Y;

            var stepX = Math.Sign(dx);
            var stepY = Math.Sign(dy);

            if (dx != 0 
                && dy != 0 
                && Math.Abs(dx) != Math.Abs(dy))
            {
                continue;
            }

            if (dx == 0 
                && dy == 0)
            {
                continue;
            }

            var current = new Coordinate(tank.X, tank.Y);
            while (true)
            {
                current = new Coordinate(current.X + stepX, current.Y + stepY);
                if (!IsInsideMap(context, current))
                {
                    break;
                }

                if (current == pos)
                {
                    return true;
                }

                var tile = context.GetTile(current.X, current.Y);
                if (tile.TileType is TileType.Tree or TileType.Building)
                {
                    break;
                }
            }
        }

        return false;
    }

    private static int DistanceToClosestDanger(Coordinate pos, HashSet<Coordinate> dangerTiles)
    {
        var min = int.MaxValue;

        foreach (var d in dangerTiles)
        {
            var dist = Math.Abs(pos.X - d.X) + Math.Abs(pos.Y - d.Y);
            if (dist < min)
            {
                min = dist;
            }
        }

        return min == int.MaxValue
            ? 0
            : min;
    }

    private static void MoveTo(ITurnContext context, Coordinate from, Coordinate to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;

        Direction direction;
        switch (dx, dy)
        {
            case (1, _):
                direction = Direction.West;
                break;
            case (-1, _):
                direction = Direction.East;
                break;
            case (_, 1):
                direction = Direction.North;
                break;
            case (_, -1):
                direction = Direction.South;
                break;
            default:
                return;
        }

        context.MoveTank(direction);
    }

    private static void RotateRandomly(ITurnContext context)
    {
        var directions = Enum.GetValues<TurretDirection>();
        context.RotateTurret(directions[_random.Next(0, directions.Length)]);
    }
}