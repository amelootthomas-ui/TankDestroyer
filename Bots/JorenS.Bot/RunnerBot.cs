using TankDestroyer.API;

namespace JorenS.Bot;

[Bot("Runner", "Joren", "2403FC")]
public class RunnerBot : IPlayerBot
{
    private readonly Random _random = new();

    public void DoTurn(ITurnContext context)
    {
        MoveToSafestTile(context);
        RotateRandomly(context);
        context.Fire();
    }

    private static void MoveToSafestTile(ITurnContext context)
    {
        var start = new Coordinate(context.Tank.X, context.Tank.Y);

        var bestTile = FindSafestTile(context, start);
        if (bestTile == start)
        {
            return;
        }

        var path = FindPath(context, start, bestTile);
        if (path == null || path.Count == 0)
        {
            return;
        }

        var next = path[0];

        var dx = next.X - start.X;
        var dy = next.Y - start.Y;

        var direction = GetDirectionFromDelta(dx, dy);
        context.MoveTank(direction);
    }

    private static Coordinate FindSafestTile(ITurnContext context, Coordinate start)
    {
        var queue = new Queue<Coordinate>();
        var visited = new HashSet<Coordinate>();

        queue.Enqueue(start);
        visited.Add(start);

        var directions = GetDirections();
        var enemies = context.GetTanks()
            .Where(t => t.OwnerId != context.Tank.OwnerId)
            .Select(t => new Coordinate(t.X, t.Y))
            .ToList();

        var bestTile = start;
        var bestScore = float.MinValue;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            var tile = context.GetTile(current.X, current.Y);
            var cover = GetCoverMultiplier(tile.TileType);

            var minDistance = int.MaxValue;

            foreach (var enemy in enemies)
            {
                var dist = Math.Abs(current.X - enemy.X) +
                           Math.Abs(current.Y - enemy.Y);

                minDistance = Math.Min(minDistance, dist);
            }

            var score = minDistance * (1f / cover);
            if (score > bestScore)
            {
                bestScore = score;
                bestTile = current;
            }

            foreach (var (dx, dy) in directions)
            {
                var next = new Coordinate(current.X + dx, current.Y + dy);
                if (visited.Contains(next)
                    || IsInvalidCoordinate(context, next)
                    || context.GetTile(next.X, next.Y).TileType == TileType.Water)
                {
                    continue;
                }

                if (context.GetTanks().Any(t =>
                    t.OwnerId != context.Tank.OwnerId &&
                    t.X == next.X &&
                    t.Y == next.Y))
                {
                    continue;
                }

                visited.Add(next);
                queue.Enqueue(next);
            }
        }

        return bestTile;
    }

    private static List<Coordinate> FindPath(ITurnContext context, Coordinate start, Coordinate target)
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

            occupied.Add(new Coordinate(tank.X, tank.Y));
        }

        queue.Enqueue(start);
        cameFrom[start] = start;

        var directions = GetDirections();

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
                if (IsInvalidCoordinate(context, next)
                    || cameFrom.ContainsKey(next)
                    || context.GetTile(next.Y, next.X).TileType == TileType.Water
                    || occupied.Contains(next))
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

    private static (int dx, int dy)[] GetDirections() =>
    [
        (0, -1),
        (0, 1),
        (-1, 0),
        (1, 0)
    ];

    private static float GetCoverMultiplier(TileType tile) => tile switch
    {
        TileType.Tree => 0.25f,
        TileType.Building => 0.5f,
        _ => 0.75f
    };

    private static bool IsInvalidCoordinate(ITurnContext context, Coordinate coordinate)
        => coordinate.X < 0
        || coordinate.Y < 0
        || coordinate.X >= context.GetMapWidth()
        || coordinate.Y >= context.GetMapHeight();

    private static Direction GetDirectionFromDelta(int dx, int dy) => (dx, dy) switch
    {
        (1, _) => Direction.West,
        (-1, _) => Direction.East,
        (_, 1) => Direction.North,
        (_, -1) => Direction.South,
        _ => throw new Exception("Invalid movement delta"),
    };

    private void RotateRandomly(ITurnContext context)
    {
        var directions = Enum.GetValues<TurretDirection>();
        context.RotateTurret(directions[_random.Next(0, directions.Length)]);
    }
}