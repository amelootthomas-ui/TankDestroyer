using TankDestroyer.API;

namespace TankYou.Bot;

internal static class Danger
{
    // turns until bullet hits, 0 if it will hit this turn, -1 if it won't hit
    public static int IsDangerous(ITurnContext context, (int x, int y) pos, int lookahead = 10)
    {
        var turn = 0;

        while (true)
        {
            var hits = context.GetBullets().Any(bullet => Locator.WillCollide(pos, bullet, context, turn));
            if (hits)
                return turn;

            if (turn > lookahead)
                return -1;

            turn++;
        }
    }
}
internal static class PathFinder
{
    const int MAX_TIME = 50;

    public static (int x, int y) GetOrbitGoal(
        ITurnContext context,
        (int x, int y) start,
        ITank enemy,
        int minDist = 7,
        int maxDist = 12)
    {
        var candidates = GetPreferredPositions(context, enemy, minDist, maxDist)
            .Where(p => p != start)
            .ToList();

        if (candidates.Count == 0)
            return (enemy.X, enemy.Y);

        // thanks chatgpt for the angle math :)
        double currentAngle = Math.Atan2(start.y - enemy.Y, start.x - enemy.X);
        double targetAngle = currentAngle + (Math.PI / 2);

        return candidates
            .OrderBy(p =>
            {
                double angle = Math.Atan2(p.y - enemy.Y, p.x - enemy.X);
                double diff = Math.Abs(angle - targetAngle);
                // normalize to [0, PI]
                if (diff > Math.PI) diff = (2 * Math.PI) - diff;
                return diff;
            })
            .First();
    }
    public static List<(int x, int y)> FindPath(
    ITurnContext context,
    (int x, int y) start,
    (int x, int y) goal)
    {
        var openSet = new PriorityQueue<(int x, int y, int t), int>();
        openSet.Enqueue((start.x, start.y, 0), 0);
        var closed = new HashSet<(int x, int y, int t)>();
        var cameFrom = new Dictionary<(int, int, int), (int, int, int)>();
        var gScore = new Dictionary<(int, int, int), int>
        {
            [(start.x, start.y, 0)] = 0
        };

        while (openSet.Count > 0)
        {
            var current = openSet.Dequeue();
            var (cx, cy, ct) = current;
            if (closed.Contains(current))
            {
                continue;
            }

            closed.Add(current);
            if (ct > MAX_TIME)
            {
                continue;
            }

            if ((cx, cy) == goal)
            {
                return ReconstructPath(cameFrom, current);
            }

            foreach (var neighbor in GetNeighbors(context, (cx, cy)))
            {
                var next = (neighbor.x, neighbor.y, t: ct + 1);
                if (next.t > MAX_TIME)
                {
                    continue;
                }

                var cost = GetTileCost(context, neighbor, ct + 1, (cx, cy));

                var tentativeG = gScore[current] + cost;

                if (!gScore.ContainsKey(next) || tentativeG < gScore[next])
                {
                    cameFrom[next] = current;
                    gScore[next] = tentativeG;

                    var f = tentativeG + Heuristic(neighbor, goal);
                    openSet.Enqueue(next, f);
                }
            }
        }

        return new();
    }

    private static List<(int x, int y)> ReconstructPath(
    Dictionary<(int x, int y, int t), (int x, int y, int t)> cameFrom,
    (int x, int y, int t) current)
    {
        var path = new List<(int x, int y)>();

        while (true)
        {
            path.Add((current.x, current.y));

            if (!cameFrom.TryGetValue(current, out var prev))
                break;

            current = prev;
        }

        path.Reverse();

        return path;
    }

    private static int Heuristic((int x, int y) a, (int x, int y) b)
    {
        // Manhattan distance
        return Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y);
    }

    private static IEnumerable<(int x, int y)> GetNeighbors(
        ITurnContext context,
        (int x, int y) pos)
    {
        var candidates = new (int x, int y)[]
        {
            (pos.x, pos.y - 1),
            (pos.x + 1, pos.y),
            (pos.x, pos.y + 1),
            (pos.x - 1, pos.y)
        };

        foreach (var c in candidates)
        {
            if (IsWalkable(context, c.x, c.y))
                yield return c;
        }

        // yield return pos; // Allow waiting in place
    }
    private static int GetTileCost(ITurnContext context, (int x, int y) pos, int arrivalTime, (int x, int y)? previousPosition = null)
    {
        var tile = context.GetTile(pos.x, pos.y).TileType;

        if (tile == TileType.Water)
            return int.MaxValue;

        int baseCost = 1;
        // if (pos == previousPosition)
        //     baseCost += 10; // lazy bastard

        var dangerTurn = Danger.IsDangerous(context, pos, lookahead: 10);

        if (dangerTurn == -1)
            return baseCost;

        if (dangerTurn == arrivalTime)
            return 1000;

        var delta = dangerTurn - arrivalTime;
        if (delta >= 0 && delta < 3)
            return baseCost + (10 * (3 - delta));

        return baseCost;
    }

    private static bool IsWalkable(ITurnContext context, int x, int y)
    {
        if (x < 0 || y < 0 || x >= context.GetMapWidth() || y >= context.GetMapHeight())
            return false;

        return context.GetTile(x, y).TileType != TileType.Water && context.GetTanks().Any(t => t.X != x && t.Y != y);
    }

    private static List<(int x, int y)> GetPreferredPositions(
    ITurnContext context,
    ITank enemy,
    int minDist = 7,
    int maxDist = 12)
    {
        var result = new List<(int x, int y)>();

        int width = context.GetMapWidth();
        int height = context.GetMapHeight();

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                var dx = x - enemy.X;
                var dy = y - enemy.Y;

                var dist = Math.Sqrt((dx * dx) + (dy * dy));

                if (dist >= minDist && dist <= maxDist && context.GetTile(x, y).TileType != TileType.Water)
                {
                    result.Add((x, y));
                }
            }
        }

        return result;
    }
}
