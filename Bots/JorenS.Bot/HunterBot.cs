using TankDestroyer.API;

namespace JorenS.Bot;

[Bot("Hunter", "Joren", "CC0404")]
public class HunterBot : IPlayerBot
{
    private readonly Random _random = new();
    private ITank? _tankToChase = null;

    public void DoTurn(ITurnContext context)
    {
        SetTankToChase(context);
        MoveTowardsTankToChase(context);
        RotateToDirectionOfTankToChase(context);
        context.Fire();
    }

    private void SetTankToChase(ITurnContext context)
    {
        if (_tankToChase is not null
           && !_tankToChase.Destroyed)
        {
            return;
        }

        var otherTanks = context.GetTanks()
            .Where(v => v.OwnerId != context.Tank.OwnerId && !v.Destroyed)
            .ToArray();

        _tankToChase = otherTanks[_random.Next(0, otherTanks.Length)];
    }

    private void MoveTowardsTankToChase(ITurnContext context)
    {
        if (_tankToChase is null)
        {
            return;
        }

        var start = new Coordinate(context.Tank.X, context.Tank.Y);
        var target = new Coordinate(_tankToChase.X, _tankToChase.Y);

        var path = FindPath(context, start, target);
        if (path.Count == 0)
        {
            return;
        }

        var next = path[0];

        var dx = next.X - start.X;
        var dy = next.Y - start.Y;

        var direction = GetDirectionFromDelta(dx, dy);

        context.MoveTank(direction);
    }

    private List<Coordinate> FindPath(ITurnContext context, Coordinate start, Coordinate target)
    {
        var queue = new Queue<Coordinate>();
        var cameFrom = new Dictionary<Coordinate, Coordinate>();

        var occupied = new HashSet<Coordinate>();

        foreach (var tank in context.GetTanks())
        {
            if (tank.OwnerId == context.Tank.OwnerId)
            {
                continue;
            }

            if (tank.OwnerId == _tankToChase!.OwnerId)
            {
                continue;
            }

            occupied.Add(new Coordinate(tank.X, tank.Y));
        }

        queue.Enqueue(start);
        cameFrom[start] = start;

        var directions = new (int dx, int dy)[]
        {
            (0, -1),
            (0, 1),
            (-1, 0),
            (1, 0)
        };

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == target)
            {
                break;
            }

            foreach (var (dx, dy) in directions)
            {
                var next = new Coordinate(current.X + dx, current.Y + dy);
                if (next.X < 0 
                    || next.Y < 0 
                    || next.X >= context.GetMapWidth() 
                    || next.Y >= context.GetMapHeight())
                {
                    continue;
                }

                if (cameFrom.ContainsKey(next))
                {
                    continue;
                }

                var tile = context.GetTile(next.Y, next.X);
                if (tile.TileType == TileType.Water)
                {
                    continue;
                }

                if (occupied.Contains(next))
                {
                    continue;
                }

                queue.Enqueue(next);
                cameFrom[next] = current;
            }
        }

        var path = new List<Coordinate>();
        var cur = target;

        while (cur != start)
        {
            path.Add(cur);
            cur = cameFrom[cur];
        }

        path.Reverse();
        return path;
    }

    private static Direction GetDirectionFromDelta(int dx, int dy) => (dx, dy) switch
    {
        (1, _) => Direction.West,
        (-1, _) => Direction.East,
        (_, 1) => Direction.North,
        (_, -1) => Direction.South,
        _ => throw new Exception("Invalid movement delta"),
    };

    private void RotateToDirectionOfTankToChase(ITurnContext context)
    {
        if (_tankToChase is null)
        {
            return;
        }

        var xDifference = _tankToChase.X - context.Tank.X;
        var yDifference = _tankToChase.Y - context.Tank.Y;
        var direction = (xDifference, yDifference) switch
        {
            (0, < 0) => TurretDirection.South,
            ( > 0, < 0) => TurretDirection.SouthWest,
            ( < 0, < 0) => TurretDirection.SouthEast,

            (0, > 0) => TurretDirection.North,
            ( > 0, > 0) => TurretDirection.NorthWest,
            ( < 0, > 0) => TurretDirection.NorthEast,

            ( > 0, 0) => TurretDirection.West,
            ( < 0, 0) => TurretDirection.East,

            _ => TurretDirection.South,
        };

        context.RotateTurret(direction);
    }
}