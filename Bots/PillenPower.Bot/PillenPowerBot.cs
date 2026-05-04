using System;
using System.Collections.Generic;
using System.Linq;
using TankDestroyer.API;

namespace PillenPower.Bot;

[Bot("PillenPower Bot", "Toekomstige Medicatie team", "4A32A8")]
public class PillenPowerBot : IPlayerBot
{
    private Random _random = new();
    private Direction? _lastMove;
    private bool _peekLeftNext = true;
    private readonly Queue<(int, int)> _recentPositions = new();

    private const int StallHistoryLength = 16;
    private const int StallUniqueThreshold = 3;

    private static readonly TurretDirection[] _allDirections = {
        TurretDirection.North, TurretDirection.NorthEast,
        TurretDirection.East,  TurretDirection.SouthEast,
        TurretDirection.South, TurretDirection.SouthWest,
        TurretDirection.West,  TurretDirection.NorthWest,
    };

    public void DoTurn(ITurnContext turnContext)
    {
        ITank myTank = turnContext.Tank;
        var enemyTanks = turnContext.GetTanks()
                                    .Where(t => t.OwnerId != myTank.OwnerId && !t.Destroyed)
                                    .ToList();
        int enemyCount = enemyTanks.Count;

        // Track position history to detect camping deadlocks
        _recentPositions.Enqueue((myTank.X, myTank.Y));
        if (_recentPositions.Count > StallHistoryLength)
            _recentPositions.Dequeue();
        bool isStalling = _recentPositions.Count >= StallHistoryLength
            && _recentPositions.Distinct().Count() <= StallUniqueThreshold;
        var visitedTiles = new HashSet<(int, int)>(_recentPositions);

        // 1. Target Selection — prioritize wounded enemies (one hit can finish them)
        ITank? target = enemyTanks
            .OrderBy(t => Distance(myTank.X, myTank.Y, t.X, t.Y) - (100 - t.Health) * 0.5)
            .FirstOrDefault();

        // 2. Compute true path distance to target (BFS) to avoid water
        int[,]? distMap = null;
        if (target != null)
            distMap = GetDistanceMap(turnContext, target);

        // 3. Tactical Movement (Hunt & Slay)
        Direction? moveDirection = GetTacticalMove(turnContext, myTank, target, distMap, enemyCount, isStalling, visitedTiles);
        int finalX = myTank.X;
        int finalY = myTank.Y;
        if (moveDirection.HasValue)
        {
            turnContext.MoveTank(moveDirection.Value);
            _lastMove = moveDirection.Value;
            switch (moveDirection.Value)
            {
                case Direction.North: finalY++; break;
                case Direction.South: finalY--; break;
                case Direction.East:  finalX--; break;
                case Direction.West:  finalX++; break;
            }
        }

        // 4. Combat (Rotate & Fire)
        if (target != null)
        {
            TurretDirection targetDir = GetTurretDirectionToTarget(finalX, finalY, target.X, target.Y, myTank.TurretDirection);
            turnContext.RotateTurret(targetDir);
            // FIX: bullets explode immediately at i=0 when fired from a tree tile — skip fire
            bool finallyInTree = turnContext.GetTile(finalY, finalX).TileType == TileType.Tree;
            if (!finallyInTree)
                turnContext.Fire();
        }
        else
        {
            turnContext.RotateTurret(GetRandomTurretDirection());
            if (_random.Next(0, 5) == 0) turnContext.Fire();
        }
    }

    private int[,] GetDistanceMap(ITurnContext ctx, ITank target)
    {
        int width  = ctx.GetMapWidth();
        int height = ctx.GetMapHeight();
        int[,] dist = new int[width, height];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                dist[x, y] = 999;

        var q = new Queue<(int x, int y)>();
        q.Enqueue((target.X, target.Y));
        dist[target.X, target.Y] = 0;

        int[] dx = { 0, 0, -1, 1 };
        int[] dy = { 1, -1, 0, 0 };

        while (q.Count > 0)
        {
            var curr = q.Dequeue();
            for (int i = 0; i < 4; i++)
            {
                int nx = curr.x + dx[i];
                int ny = curr.y + dy[i];
                if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                {
                    // FIX: GetTile(y, x) — first param is row (y), second is column (x)
                    if (ctx.GetTile(ny, nx).TileType != TileType.Water && dist[nx, ny] == 999)
                    {
                        dist[nx, ny] = dist[curr.x, curr.y] + 1;
                        q.Enqueue((nx, ny));
                    }
                }
            }
        }
        return dist;
    }

    private Direction? GetTacticalMove(ITurnContext turnContext, ITank myTank, ITank? target,
        int[,]? distMap, int enemyCount, bool isStalling, HashSet<(int, int)> visitedTiles)
    {
        GetDangerZones(turnContext, myTank, out var realDanger, out var predictedDanger);
        bool currentlyInTree = turnContext.GetTile(myTank.Y, myTank.X).TileType == TileType.Tree;

        double bestScore = double.MinValue;
        Direction bestDir = Direction.North;
        bool foundValid = false;

        foreach (Direction dir in Enum.GetValues(typeof(Direction)))
        {
            if (!IsMoveValid(turnContext, myTank, dir, out int nx, out int ny)) continue;

            double score = CalculateTileScore(turnContext, nx, ny, target, realDanger, predictedDanger,
                distMap, myTank, enemyCount, isStalling, visitedTiles);
            if (score > bestScore)
            {
                bestScore = score;
                bestDir   = dir;
                foundValid = true;
            }
        }

        // FIX: use noise-free score for stay/move comparison so the threshold is deterministic
        double currentTileScore = CalculateTileScore(turnContext, myTank.X, myTank.Y, target, realDanger, predictedDanger,
            distMap, myTank, enemyCount, isStalling, visitedTiles, noNoise: true);

        if (!foundValid) return null;

        bool currentlyDangerous = realDanger.Contains((myTank.X, myTank.Y))
            || (!isStalling && predictedDanger.Contains((myTank.X, myTank.Y)));

        if (currentlyDangerous || bestScore > currentTileScore)
        {
            if (currentlyInTree)
                _peekLeftNext = !_peekLeftNext;
            return bestDir;
        }

        return null;
    }

    private double CalculateTileScore(ITurnContext ctx, int x, int y, ITank? target,
        HashSet<(int, int)> realDanger, HashSet<(int, int)> predictedDanger,
        int[,]? distMap, ITank myTank, int enemyCount,
        bool isStalling, HashSet<(int, int)> visitedTiles, bool noNoise = false)
    {
        double score = 0;

        // 1. Safety — real bullets always critical; predicted danger relaxed when stalling to break deadlocks
        if (realDanger.Contains((x, y)))
            score -= 10000;
        else if (predictedDanger.Contains((x, y)))
            score -= isStalling ? 2000 : 10000;

        // 2. Cover — FIX: GetTile(y, x)
        var tileType = ctx.GetTile(y, x).TileType;
        bool currentlyInTree = ctx.GetTile(ctx.Tank.Y, ctx.Tank.X).TileType == TileType.Tree;

        if (tileType == TileType.Building)
        {
            score += 1200;
            if (enemyCount >= 2) score += 400;
        }
        else if (tileType == TileType.Tree)
        {
            bool hasPeekSpot = HasAdjacentFirePosition(ctx, x, y, target);
            double treeCoverBonus = hasPeekSpot ? 2000 : 800;
            // When stalling, heavily suppress tree camping to force the bot to break out
            if (isStalling) treeCoverBonus -= 1500;
            score += treeCoverBonus;
            if (hasPeekSpot && enemyCount >= 2) score += 200;

            // Graduated own-health urgency — stacking bonuses (still applies even when stalling)
            if (myTank.Health <= 75) score += 300;
            if (myTank.Health <= 50) score += 700;
            if (myTank.Health <= 25) score += 1500;

            // Penalise moving from one tree to another — wastes a turn
            if (currentlyInTree && (x != ctx.Tank.X || y != ctx.Tank.Y))
                score -= 1500;
        }

        // Anti-stall: penalize any tile that was visited recently
        if (isStalling && visitedTiles.Contains((x, y)))
            score -= 3000;

        // Bonus for being adjacent to a tree — quick retreat available
        if (tileType != TileType.Tree && IsAdjacentToTree(ctx, x, y))
            score += 300;

        // Peek-a-boo: when we ARE in a tree, strongly reward stepping out to a tile with LOS
        if (currentlyInTree && tileType != TileType.Tree && target != null)
        {
            if (HasLineOfSight(x, y, target.X, target.Y, ctx))
                score += 800;

            // Peek alternation: prefer alternating left/right side
            if (_peekLeftNext  && x < myTank.X) score += 150;
            if (!_peekLeftNext && x > myTank.X) score += 150;
        }

        // 3. Aggressive proximity
        if (target != null)
        {
            int pathDist = (distMap != null) ? distMap[x, y] : 999;
            double distanceCostPerStep = enemyCount >= 2 ? 100 : 150;

            if (pathDist < 999)
            {
                score -= pathDist * distanceCostPerStep;
                // Graduated target-health urgency — stacking extra aggression
                if (target.Health <= 75) score -= pathDist * 30;
                if (target.Health <= 50) score -= pathDist * 70;
                if (target.Health <= 25) score -= pathDist * 100;
            }
            else
            {
                score -= Distance(x, y, target.X, target.Y) * 50;
            }

            // 4. Line of sight priority — scale with enemy count
            if (HasLineOfSight(x, y, target.X, target.Y, ctx))
            {
                score += enemyCount >= 2 ? 1800 : 2500;
                if (enemyCount == 1 && Distance(x, y, target.X, target.Y) <= 5)
                    score += 1000;
            }
            else
            {
                int tdx = Math.Abs(target.X - x);
                int tdy = Math.Abs(target.Y - y);
                int minor = Math.Min(tdx, tdy), major = Math.Max(tdx, tdy);
                if (major > 0 && minor < major * 0.3)
                    score += 600;
            }

            // 5. Jiggle reversal bonus — at close range, reward reversing last move
            int closeDist = (distMap != null && distMap[x, y] < 999) ? distMap[x, y] : (int)Distance(x, y, target.X, target.Y);
            if (closeDist <= 6)
            {
                Direction? candidateDir = DirectionToCandidate(myTank.X, myTank.Y, x, y);
                if (_lastMove.HasValue && candidateDir.HasValue && WouldReverse(_lastMove.Value, candidateDir.Value))
                    score += 700;
            }

            // 6. Side-step bonus (1v1 only) — break/reform alignment with enemy
            if (enemyCount == 1)
            {
                bool currentlyAligned = IsOnStraightOrDiagonalLine(myTank.X, myTank.Y, target.X, target.Y);
                bool candidateAligned = IsOnStraightOrDiagonalLine(x, y, target.X, target.Y);
                if (currentlyAligned && !candidateAligned) score += 500;
                else if (!currentlyAligned && candidateAligned) score += 300;
                else if (currentlyAligned && candidateAligned) score -= 200;
            }
        }

        // 7. Unpredictability — small nudge to keep moving
        if (x != ctx.Tank.X || y != ctx.Tank.Y)
            score += 50;

        // Mild center bias
        score -= Distance(x, y, ctx.GetMapWidth() / 2.0, ctx.GetMapHeight() / 2.0) * 5;

        if (!noNoise)
            score += (_random.NextDouble() - 0.5) * 5.0;

        return score;
    }

    // Returns true if any cardinal neighbour of (treeX, treeY) is passable and has LOS to target
    private bool HasAdjacentFirePosition(ITurnContext ctx, int treeX, int treeY, ITank? target)
    {
        if (target == null) return false;
        int[] dx = { 0, 0, -1, 1 };
        int[] dy = { 1, -1, 0, 0 };
        for (int i = 0; i < 4; i++)
        {
            int ax = treeX + dx[i];
            int ay = treeY + dy[i];
            if (ax < 0 || ax >= ctx.GetMapWidth() || ay < 0 || ay >= ctx.GetMapHeight()) continue;
            if (ctx.GetTile(ay, ax).TileType == TileType.Water) continue;
            if (HasLineOfSight(ax, ay, target.X, target.Y, ctx)) return true;
        }
        return false;
    }

    private bool IsAdjacentToTree(ITurnContext ctx, int x, int y)
    {
        int[] dx = { 0, 0, -1, 1 };
        int[] dy = { 1, -1, 0, 0 };
        for (int i = 0; i < 4; i++)
        {
            int ax = x + dx[i];
            int ay = y + dy[i];
            if (ax < 0 || ax >= ctx.GetMapWidth() || ay < 0 || ay >= ctx.GetMapHeight()) continue;
            if (ctx.GetTile(ay, ax).TileType == TileType.Tree) return true;
        }
        return false;
    }

    private bool HasLineOfSight(int x1, int y1, int x2, int y2, ITurnContext ctx)
    {
        int dx = x2 - x1;
        int dy = y2 - y1;
        if (dx != 0 && dy != 0 && Math.Abs(dx) != Math.Abs(dy)) return false;

        // FIX: GetTile(y, x) — cannot fire out of a tree
        if (ctx.GetTile(y1, x1).TileType == TileType.Tree) return false;

        int stepX = Math.Sign(dx);
        int stepY = Math.Sign(dy);
        int steps  = Math.Max(Math.Abs(dx), Math.Abs(dy));
        for (int i = 1; i < steps; i++)
        {
            int tx = x1 + stepX * i;
            int ty = y1 + stepY * i;
            var t = ctx.GetTile(ty, tx).TileType;
            if (t == TileType.Tree || t == TileType.Building) return false;
        }
        return true;
    }

    private void GetDangerZones(ITurnContext ctx, ITank myTank,
        out HashSet<(int, int)> realDanger, out HashSet<(int, int)> predictedDanger)
    {
        realDanger      = new HashSet<(int, int)>();
        predictedDanger = new HashSet<(int, int)>();

        foreach (var bullet in ctx.GetBullets())
        {
            int bdx = 0, bdy = 0;
            if (bullet.Direction.HasFlag(TurretDirection.North)) bdy =  1;
            if (bullet.Direction.HasFlag(TurretDirection.South)) bdy = -1;
            if (bullet.Direction.HasFlag(TurretDirection.East))  bdx = -1;
            if (bullet.Direction.HasFlag(TurretDirection.West))  bdx =  1;

            // Ignore bullets already moving away from us
            int vx = myTank.X - bullet.X;
            int vy = myTank.Y - bullet.Y;
            if (vx * bdx + vy * bdy < 0) continue;

            AddBulletPath(realDanger, bullet.X, bullet.Y, bullet.Direction, ctx);
        }

        foreach (var enemy in ctx.GetTanks().Where(t => t.OwnerId != myTank.OwnerId && !t.Destroyed))
        {
            TurretDirection predictedDir = GetTurretDirectionToTarget(enemy.X, enemy.Y, myTank.X, myTank.Y, enemy.TurretDirection);
            AddBulletPath(predictedDanger, enemy.X, enemy.Y, enemy.TurretDirection, ctx, maxRange: 8);
            if (predictedDir != enemy.TurretDirection)
                AddBulletPath(predictedDanger, enemy.X, enemy.Y, predictedDir, ctx, maxRange: 8);
        }
    }

    private void AddBulletPath(HashSet<(int, int)> zones, int startX, int startY,
        TurretDirection dir, ITurnContext ctx, int maxRange = int.MaxValue)
    {
        int dx = 0, dy = 0;
        if (dir.HasFlag(TurretDirection.North)) dy =  1;
        if (dir.HasFlag(TurretDirection.South)) dy = -1;
        if (dir.HasFlag(TurretDirection.East))  dx = -1;
        if (dir.HasFlag(TurretDirection.West))  dx =  1;

        for (int i = 0; i <= maxRange; i++)
        {
            int tx = startX + dx * i;
            int ty = startY + dy * i;
            if (tx < 0 || tx >= ctx.GetMapWidth() || ty < 0 || ty >= ctx.GetMapHeight()) break;
            zones.Add((tx, ty));
            var tileType = ctx.GetTile(ty, tx).TileType;
            if (tileType == TileType.Tree) break;
            if (tileType == TileType.Building && i > 0) break;
        }
    }

    private bool IsMoveValid(ITurnContext ctx, ITank myTank, Direction direction, out int nextX, out int nextY)
    {
        nextX = myTank.X;
        nextY = myTank.Y;
        switch (direction)
        {
            case Direction.North: nextY++; break;
            case Direction.South: nextY--; break;
            case Direction.East:  nextX--; break;
            case Direction.West:  nextX++; break;
        }
        if (nextX < 0 || nextX >= ctx.GetMapWidth() || nextY < 0 || nextY >= ctx.GetMapHeight())
            return false;
        if (ctx.GetTile(nextY, nextX).TileType == TileType.Water) return false;
        int tx = nextX, ty = nextY;
        return !ctx.GetTanks().Any(t => t.X == tx && t.Y == ty);
    }

    private double Distance(int x1, int y1, int x2, int y2)
        => Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));

    private double Distance(int x1, int y1, double x2, double y2)
        => Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));

    private TurretDirection GetTurretDirectionToTarget(int fromX, int fromY, int toX, int toY, TurretDirection currentDir)
    {
        int dx = toX - fromX;
        int dy = toY - fromY;
        if (dx == 0 && dy == 0) return currentDir;

        int absDx = Math.Abs(dx);
        int absDy = Math.Abs(dy);

        bool nearDiagonal = absDx > 0 && absDy > 0
            && Math.Min(absDx, absDy) >= Math.Max(absDx, absDy) * 0.65;

        if (nearDiagonal)
        {
            if (dx > 0 && dy > 0) return TurretDirection.NorthWest;
            if (dx > 0 && dy < 0) return TurretDirection.SouthWest;
            if (dx < 0 && dy > 0) return TurretDirection.NorthEast;
            return TurretDirection.SouthEast;
        }

        if (absDy >= absDx) return dy > 0 ? TurretDirection.North : TurretDirection.South;
        return dx > 0 ? TurretDirection.West : TurretDirection.East;
    }

    private TurretDirection GetRandomTurretDirection() => _allDirections[_random.Next(_allDirections.Length)];

    private bool IsOnStraightOrDiagonalLine(int x1, int y1, int x2, int y2)
    {
        int dx = Math.Abs(x1 - x2), dy = Math.Abs(y1 - y2);
        return x1 == x2 || y1 == y2 || dx == dy;
    }

    private Direction? DirectionToCandidate(int fromX, int fromY, int toX, int toY)
    {
        if (toY > fromY) return Direction.North;
        if (toY < fromY) return Direction.South;
        if (toX < fromX) return Direction.East;
        if (toX > fromX) return Direction.West;
        return null;
    }

    private bool WouldReverse(Direction previous, Direction current) => previous switch
    {
        Direction.North => current == Direction.South,
        Direction.South => current == Direction.North,
        Direction.East  => current == Direction.West,
        Direction.West  => current == Direction.East,
        _ => false
    };
}
