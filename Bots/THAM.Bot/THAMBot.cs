using System;
using System.Collections.Generic;
using System.Linq;
using TankDestroyer.API;

namespace THAM.Bot;

[Bot("Thomas", "Former Antmaster", "ECF207")]
public class THAMBot : IPlayerBot
{
    private bool _initialized;
    private int _mapWidth;
    private int _mapHeight;

    private Dictionary<int, (int x, int y)> _enemyLastPos = new();
    private Dictionary<int, (int x, int y)> _enemyPredictedPos = new();  // sla vorige voorspelling op om volgende beurt te vergelijken
    private Dictionary<int, int> _enemyMissCounter = new();

    private HashSet<(int x, int y)> _treeSet = new();
    private HashSet<(int x, int y)> _buildingSet = new();
    private HashSet<(int x, int y)> _waterSet = new();

    private (int x, int y)? _lastPos;
    private Direction? _lastMoveDir;
    private Random _rng = new();

    private static readonly Direction[] AllDirections =
        { Direction.North, Direction.West, Direction.South, Direction.East };

    public void DoTurn(ITurnContext turnContext)
    {
        if (turnContext == null) return;

        var mapAvailable = TryHasMap(turnContext);
        if (mapAvailable) EnsureInitialized(turnContext);

        var current = turnContext.Tank;

        // Anti-stuck — vergelijk positie aan het begin van de beurt
        bool stuck = _lastPos.HasValue && _lastPos.Value == (current.X, current.Y);
        _lastPos = (current.X, current.Y);

        var enemies = turnContext.GetTanks()
            .Where(t => t.OwnerId != current.OwnerId && !t.Destroyed)
            .ToList();

        // DOEL: Zwakste vijand eerst, bij gelijke gezondheid beslis op afstand
        var target = enemies
            .OrderBy(t => t.Health)
            .ThenBy(t => Math.Abs(t.X - current.X) + Math.Abs(t.Y - current.Y))
            .FirstOrDefault();

        var bullets = turnContext.GetBullets();

        // --- BEWEGING ---
        Direction? moveDir = null;

        if (stuck)
        {
            // Probeer een pad naar de vijand te vinden in plaats van willekeurig ontsnappen
            if (target != null)
            {
                var pathDir = FindPathToClearShot(turnContext, target) ?? FindPathMoveTowards(turnContext, target.X, target.Y);
                if (pathDir.HasValue)
                {
                    moveDir = pathDir;
                }
            }

            // Terugval als pad niet gevonden wordt
            if (!moveDir.HasValue)
            {
                moveDir = FindAnySafeDirection(turnContext, forceExclude: _lastMoveDir)
                       ?? FindAnySafeDirection(turnContext);
            }
        }
        else
        {
            // BEWEGING: Richtingen scoren op kogelgevaar + lokbonus + nabijheid van vijanden
            moveDir =
                FindBestMoveTowardsClearViewEnemy(turnContext, enemies, bullets)
                ?? FindSafestDirection(turnContext, enemies, bullets)
                ?? (target != null ? FindPathToClearShot(turnContext, target) : null)
                ?? (target != null ? FindPathMoveTowards(turnContext, target.X, target.Y) : null);
        }

        if (moveDir.HasValue)
        {
            turnContext.MoveTank(moveDir.Value);
            _lastMoveDir = moveDir.Value;
        }

        // --- ROTATIE ---
        if (target != null)
        {
            var myNextPos = moveDir.HasValue
                ? GetNextPosition(current.X, current.Y, moveDir.Value)
                : (x: current.X, y: current.Y);

            // Vergelijk voorspelling van vorige beurt met waar de vijand werkelijk eindigde
            if (_enemyPredictedPos.TryGetValue(target.OwnerId, out var lastPredicted))
            {
                bool predictionWasCorrect = lastPredicted == (target.X, target.Y);
                _enemyMissCounter[target.OwnerId] = predictionWasCorrect
                    ? 0
                    : _enemyMissCounter.GetValueOrDefault(target.OwnerId) + 1;
            }

            int misses = _enemyMissCounter.GetValueOrDefault(target.OwnerId);
            (int x, int y) predicted = misses >= 3
                ? (target.X, target.Y)          // prediction unreliable — aim at current pos
                : PredictEnemyPosition(target);

            // Sla op wat we deze beurt voorspelden om volgende beurt te controleren
            _enemyPredictedPos[target.OwnerId] = predicted;

            var dx = predicted.x - myNextPos.x;
            var dy = predicted.y - myNextPos.y;

            turnContext.RotateTurret(GetApproximateDirection(dx, dy));
        }

        // --- VUUR --- (geen straf, altijd vuren)
        turnContext.Fire();
    }

    // =============================================================
    // Waardeert elke begaanbare richting op basis van:
    //   - kogelgevaar (vermijd geraakt worden)
    //   - lokbonus   (geef voorkeur aan posities met dekking om in te duiken)
    //   - afstand tot een vijand waarop we een vrije schootlijn hebben (dichterbij = beter)
    // Geeft alleen een richting terug als minstens één vijand een vrije vuurlijn heeft.
    // =============================================================
    private Direction? FindBestMoveTowardsClearViewEnemy(
        ITurnContext turnContext, List<ITank> enemies, IBullet[] bullets)
    {
        if (!enemies.Any()) return null;

        var current = turnContext.Tank;

        var scored = AllDirections
            .Select(d =>
            {
                var next = GetNextPosition(current.X, current.Y, d);
                if (!CanMoveTo(turnContext, next.x, next.y))
                    return (dir: d, score: int.MaxValue, hasLof: false);

                int danger = EvaluateBulletDanger(turnContext, next.x, next.y, bullets);
                if (danger > 300)   // too hot — skip
                    return (dir: d, score: int.MaxValue, hasLof: false);

                // FIX BAIT: now actually used in scoring
                int baitBonus = IsGoodBaitPosition(turnContext, next.x, next.y, enemies) ? -200 : 0;

                bool hasLof = enemies.Any(e =>
                    HasClearLineOfFireFrom(turnContext, next.x, next.y, e.X, e.Y));

                int distScore = enemies
                    .Where(e => HasClearLineOfFireFrom(turnContext, next.x, next.y, e.X, e.Y))
                    .Select(e => Math.Abs(e.X - next.x) + Math.Abs(e.Y - next.y))
                    .DefaultIfEmpty(int.MaxValue / 2)
                    .Min();

                int score = danger + baitBonus + distScore * 10;
                return (dir: d, score, hasLof);
            })
            .Where(s => s.score < int.MaxValue && s.hasLof)
            .OrderBy(s => s.score)
            .ToList();

        return scored.Any() ? scored[0].dir : null;
    }

    // =============================================================
    // Kies het begaanbare vakje met de laagste kogelgevaar-score,
    // met voorkeur voor lokposities en bewegen richting vijanden.
    // =============================================================
    private Direction? FindSafestDirection(
        ITurnContext turnContext, List<ITank> enemies, IBullet[] bullets)
    {
        var current = turnContext.Tank;

        return AllDirections
            .Select(d =>
            {
                var next = GetNextPosition(current.X, current.Y, d);
                if (!CanMoveTo(turnContext, next.x, next.y))
                    return (dir: d, score: int.MaxValue);

                int danger = EvaluateBulletDanger(turnContext, next.x, next.y, bullets);
                int baitBonus = IsGoodBaitPosition(turnContext, next.x, next.y, enemies) ? -200 : 0;
                int dist = enemies
                    .Select(e => Math.Abs(e.X - next.x) + Math.Abs(e.Y - next.y))
                    .DefaultIfEmpty(0)
                    .Min();

                return (dir: d, score: danger + baitBonus + dist * 5);
            })
            .Where(s => s.score < int.MaxValue)
            .OrderBy(s => s.score)
            .Select(s => (Direction?)s.dir)
            .FirstOrDefault();
    }

    private (int x, int y) PredictEnemyPosition(ITank enemy)
    {
        if (!_enemyLastPos.TryGetValue(enemy.OwnerId, out var last))
        {
            _enemyLastPos[enemy.OwnerId] = (enemy.X, enemy.Y);
            return (enemy.X, enemy.Y);
        }

        int dx = enemy.X - last.x;
        int dy = enemy.Y - last.y;

        var predicted = (x: enemy.X + dx, y: enemy.Y + dy);

        _enemyLastPos[enemy.OwnerId] = (enemy.X, enemy.Y);
        return predicted;
    }

    // Breadth-first search to find the first move towards a destination while
    // respecting CanMoveTo (which blocks trees/buildings/water when initialized).
    private Direction? FindPathMoveTowards(ITurnContext turnContext, int destX, int destY)
    {
        var current = turnContext.Tank;
        var start = (x: current.X, y: current.Y);
        if (start.x == destX && start.y == destY) return null;

        var queue = new Queue<(int x, int y)>();
        var cameFrom = new Dictionary<(int x, int y), (int x, int y)>();
        var visited = new HashSet<(int x, int y)>();

        queue.Enqueue(start);
        visited.Add(start);

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node.x == destX && node.y == destY) break;

            foreach (var d in AllDirections)
            {
                var next = GetNextPosition(node.x, node.y, d);
                if (visited.Contains(next)) continue;
                if (!CanMoveTo(turnContext, next.x, next.y)) continue;

                visited.Add(next);
                cameFrom[next] = node;
                queue.Enqueue(next);
            }
        }

        var dest = (x: destX, y: destY);

            // Als de exacte bestemming niet bereikt is, kies het bezochte vakje dat het dichtst bij de bestemming ligt
        if (!cameFrom.ContainsKey(dest) && !visited.Contains(dest))
        {
            (int x, int y)? best = null;
            int bestDist = int.MaxValue;
            foreach (var v in visited)
            {
                int dist = Math.Abs(v.x - dest.x) + Math.Abs(v.y - dest.y);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = v;
                }
            }
            if (best == null) return null;
            dest = best.Value;
        }

            // Bouw het pad terug naar de start op
        var path = new List<(int x, int y)>();
        var cur = dest;
        while (!cur.Equals(start))
        {
            path.Add(cur);
            if (!cameFrom.TryGetValue(cur, out var parent)) break;
            cur = parent;
        }

        if (path.Count == 0) return null;
        path.Reverse();
        var first = path[0];

        int dxStep = first.x - start.x;
        int dyStep = first.y - start.y;
        return GetDirectionFromDelta(dxStep, dyStep);
    }

    // Breadth-first search to find the first move toward any reachable tile
    // that has a clear line-of-fire to `target` (i.e. a good shooting position).
    private Direction? FindPathToClearShot(ITurnContext turnContext, ITank target)
    {
        var current = turnContext.Tank;
        var start = (x: current.X, y: current.Y);

        if (HasClearLineOfFireFrom(turnContext, start.x, start.y, target.X, target.Y))
            return null;

        var queue = new Queue<(int x, int y)>();
        var cameFrom = new Dictionary<(int x, int y), (int x, int y)>();
        var visited = new HashSet<(int x, int y)>();

        queue.Enqueue(start);
        visited.Add(start);

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();

            if (HasClearLineOfFireFrom(turnContext, node.x, node.y, target.X, target.Y))
            {
                var path = new List<(int x, int y)>();
                var cur = node;
                while (!cur.Equals(start))
                {
                    path.Add(cur);
                    if (!cameFrom.TryGetValue(cur, out var parent)) break;
                    cur = parent;
                }
                if (path.Count == 0) return null;
                path.Reverse();
                var first = path[0];
                return GetDirectionFromDelta(first.x - start.x, first.y - start.y);
            }

            foreach (var d in AllDirections)
            {
                var next = GetNextPosition(node.x, node.y, d);
                if (visited.Contains(next)) continue;
                if (!CanMoveTo(turnContext, next.x, next.y)) continue;

                visited.Add(next);
                cameFrom[next] = node;
                queue.Enqueue(next);
            }
        }

        return null;
    }

    private static Direction? GetDirectionFromDelta(int dx, int dy)
    {
        if (dx == 0 && dy == 1) return Direction.North;
        if (dx == 0 && dy == -1) return Direction.South;
        if (dx == 1 && dy == 0) return Direction.West;
        if (dx == -1 && dy == 0) return Direction.East;
        return null;
    }

    // laat ons een andere richting kiezen als we vastzitten
    private Direction? FindAnySafeDirection(ITurnContext turnContext, Direction? forceExclude = null)
    {
        var current = turnContext.Tank;

        foreach (var direction in AllDirections)
        {
            if (direction == forceExclude) continue;
            var next = GetNextPosition(current.X, current.Y, direction);
            if (CanMoveTo(turnContext, next.x, next.y))
                return direction;
        }

        return null;
    }

    private bool HasClearLineOfFireFrom(ITurnContext turnContext, int sx, int sy, int tx, int ty)
    {
        var dx = tx - sx;
        var dy = ty - sy;

        if (!IsAligned(dx, dy, out _)) return false;

        var stepX = Math.Sign(dx);
        var stepY = Math.Sign(dy);

        int steps = Math.Max(Math.Abs(dx), Math.Abs(dy));
        if (steps > 6) return false;

        for (int i = 1; i < steps; i++)
        {
            if (IsBlockingTileAt(turnContext, sx + stepX * i, sy + stepY * i))
                return false;
        }

        return true;
    }

    private bool IsAdjacentToBlockingTile(ITurnContext turnContext, int x, int y)
    {
        foreach (var (nx, ny) in new[] { (x, y + 1), (x, y - 1), (x + 1, y), (x - 1, y) })
        {
            if (IsBlockingTileAt(turnContext, nx, ny))
                return true;
        }
        return false;
    }

    private bool CanMoveTo(ITurnContext turnContext, int x, int y)
    {
        try
        {
            var width = turnContext.GetMapWidth();
            var height = turnContext.GetMapHeight();
            if (x < 0 || y < 0 || x >= width || y >= height) return false;
        }
        catch { }

        if (_initialized)
        {
            if (_treeSet.Contains((x, y)) || _buildingSet.Contains((x, y)) || _waterSet.Contains((x, y)))
                return false;
        }
        else
        {
            try
            {
                var tile = GetTile(turnContext, x, y);
                if (tile.TileType == TileType.Tree ||
                    tile.TileType == TileType.Building ||
                    tile.TileType == TileType.Water)
                    return false;
            }
            catch { }
        }

        foreach (var t in turnContext.GetTanks())
        {
            if (!t.Destroyed && t.X == x && t.Y == y)
                return false;
        }

        return true;
    }

    private bool IsBlockingTileAt(ITurnContext turnContext, int x, int y)
    {
        if (_initialized)
            return _treeSet.Contains((x, y)) || _buildingSet.Contains((x, y));

        var tile = GetTile(turnContext, x, y);
        return tile.TileType == TileType.Tree || tile.TileType == TileType.Building;
    }

    // Diagonale kogels worden nu correct gevolgd
    private int EvaluateBulletDanger(ITurnContext turnContext, int x, int y, IBullet[] bullets)
    {
        int danger = 0;

        foreach (var b in bullets)
        {
            int dirX = 0, dirY = 0;

            if (b.Direction.HasFlag(TurretDirection.North)) dirY = 1;
            if (b.Direction.HasFlag(TurretDirection.South)) dirY = -1;
            if (b.Direction.HasFlag(TurretDirection.West)) dirX = 1;
            if (b.Direction.HasFlag(TurretDirection.East)) dirX = -1;

            bool movingDiag = dirX != 0 && dirY != 0;
            if (!movingDiag)
            {
                if (dirX != 0 && y != b.Y) continue;
                if (dirY != 0 && x != b.X) continue;
            }

            for (int i = 1; i <= 6; i++)
            {
                int bx = b.X + dirX * i;
                int by = b.Y + dirY * i;

                if (IsBlockingTileAt(turnContext, bx, by)) break;

                if (bx == x && by == y)
                {
                    danger += (7 - i) * 60;
                    if (i <= 2) danger += 200;
                }
            }
        }

        return danger;
    }

    // Controleert correct of er dekking naast de ontsnappingsroute is
    private bool IsGoodBaitPosition(ITurnContext turnContext, int x, int y, List<ITank> enemies)
    {
        foreach (var e in enemies)
        {
            var dx = e.X - x;
            var dy = e.Y - y;

            if (!IsAligned(dx, dy, out _)) continue;
            if (!HasClearLineOfFireFrom(turnContext, e.X, e.Y, x, y)) continue;

            foreach (var (ex, ey) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
            {
                if (!CanMoveTo(turnContext, ex, ey)) continue;
                if (IsAdjacentToBlockingTile(turnContext, ex, ey)) return true;
            }
        }

        return false;
    }

    private TurretDirection GetApproximateDirection(int dx, int dy)
    {
        if (Math.Abs(dx) > Math.Abs(dy) * 2)
            return dx > 0 ? TurretDirection.West : TurretDirection.East;

        if (Math.Abs(dy) > Math.Abs(dx) * 2)
            return dy > 0 ? TurretDirection.North : TurretDirection.South;

        if (dx > 0)
            return dy > 0 ? TurretDirection.NorthWest : TurretDirection.SouthWest;

        return dy > 0 ? TurretDirection.NorthEast : TurretDirection.SouthEast;
    }

    private static bool IsAligned(int dx, int dy, out TurretDirection direction)
    {
        direction = TurretDirection.North;

        if (dx == 0)
        {
            direction = dy > 0 ? TurretDirection.North : TurretDirection.South;
            return true;
        }

        if (dy == 0)
        {
            direction = dx > 0 ? TurretDirection.West : TurretDirection.East;
            return true;
        }

        if (Math.Abs(dx) == Math.Abs(dy))
        {
            if (dx > 0 && dy > 0) direction = TurretDirection.NorthWest;
            else if (dx > 0 && dy < 0) direction = TurretDirection.SouthWest;
            else if (dx < 0 && dy > 0) direction = TurretDirection.NorthEast;
            else direction = TurretDirection.SouthEast;
            return true;
        }

        return false;
    }

    private static (int x, int y) GetNextPosition(int x, int y, Direction direction)
    {
        return direction switch
        {
            Direction.North => (x, y + 1),
            Direction.South => (x, y - 1),
            Direction.West => (x + 1, y),
            Direction.East => (x - 1, y),
            _ => (x, y)
        };
    }

    private static ITile GetTile(ITurnContext turnContext, int x, int y)
        => turnContext.GetTile(y, x);

    private bool TryHasMap(ITurnContext turnContext)
    {
        try { _ = turnContext.GetMapWidth(); return true; }
        catch { return false; }
    }

    private void EnsureInitialized(ITurnContext turnContext)
    {
        if (_initialized) return;

        _mapWidth = turnContext.GetMapWidth();
        _mapHeight = turnContext.GetMapHeight();

        for (int x = 0; x < _mapWidth; x++)
        {
            for (int y = 0; y < _mapHeight; y++)
            {
                var tile = GetTile(turnContext, x, y);
                var coord = (x, y);

                if (tile.TileType == TileType.Tree) _treeSet.Add(coord);
                else if (tile.TileType == TileType.Building) _buildingSet.Add(coord);
                else if (tile.TileType == TileType.Water) _waterSet.Add(coord);
            }
        }

        _initialized = true;
    }
}