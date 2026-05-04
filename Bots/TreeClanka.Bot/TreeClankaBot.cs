using System;
using System.Collections.Generic;
using System.Linq;
using TankDestroyer.API;

namespace TreeClanka.Bot;

[Bot("TreeClanka", "Team TreeClanka", "F527B6")]
public class TreeClankaBot : IPlayerBot
{
    private readonly Random _random = new();

    public void DoTurn(ITurnContext ctx)
    {
        ITank me = ctx.Tank;
        var enemies = ctx.GetTanks().Where(t => t.OwnerId != me.OwnerId && !t.Destroyed).ToList();

        ITank? target = PickTarget(ctx, me, enemies, out int[,]? dist);
        Direction? move = PickMove(ctx, me, target, dist);

        int x = me.X, y = me.Y;
        if (move.HasValue)
        {
            ctx.MoveTank(move.Value);
            ApplyMove(move.Value, ref x, ref y);
        }

        var shot = FindBestShot(ctx, me, x, y, target);

        if (shot.HasValue)
        {
            ctx.RotateTurret(shot.Value.dir);
            ctx.Fire();
        }
        else if (target != null)
        {
            ctx.RotateTurret(AimToward(x, y, target.X, target.Y, me.TurretDirection));
        }
        else
        {
            ctx.RotateTurret(RandomTurretDirection());
        }

        ctx.Fire();
    }

    private ITank? PickTarget(ITurnContext ctx, ITank me, List<ITank> enemies, out int[,]? bestMap)
    {
        bestMap = null;
        ITank? best = null;
        double bestScore = double.MaxValue;

        foreach (var e in enemies)
        {
            var map = DistanceMap(ctx, e.X, e.Y);
            int path = map[me.X, me.Y];
            double score = path < 999 ? path * 10 : Dist(me.X, me.Y, e.X, e.Y) * 50;
            if (HasShot(me.X, me.Y, e.X, e.Y, ctx)) score -= 40;

            if (score < bestScore)
            {
                bestScore = score;
                best = e;
                bestMap = map;
            }
        }

        return best;
    }

    private Direction? PickMove(ITurnContext ctx, ITank me, ITank? target, int[,]? dist)
    {
        var danger = DangerZones(ctx, me);
        Direction? bestDir = null;
        double bestScore = Score(ctx, me, me.X, me.Y, target, dist, danger);

        foreach (Direction dir in Enum.GetValues(typeof(Direction)))
        {
            if (!IsMoveValid(ctx, me, dir, out int x, out int y)) continue;
            double score = Score(ctx, me, x, y, target, dist, danger);

            if (score > bestScore + 15 || danger.Contains((me.X, me.Y)) && score > bestScore)
            {
                bestScore = score;
                bestDir = dir;
            }
        }

        return bestDir;
    }

    private double Score(ITurnContext ctx, ITank me, int x, int y, ITank? target, int[,]? dist, HashSet<(int, int)> danger)
    {
        double score = danger.Contains((x, y)) ? -10000 : 0;
        score += TreeScore(ctx, x, y);
        score -= ThreatScore(ctx, me, x, y);

        if (target != null)
        {
            int path = dist == null ? 999 : dist[x, y];
            score -= path < 999 ? path * 100 : Dist(x, y, target.X, target.Y) * 50;
            var shot = FindBestShot(ctx, me, x, y, target);

            if (shot.HasValue)
            {
                score += 1500;

                if (shot.Value.enemy == target)
                    score += 400;

                if (shot.Value.distance <= 6)
                    score += 250;
            }
            else if (BlockedLine(x, y, target.X, target.Y, ctx))
            {
                score += 300;
            }
        }

        score -= Dist(x, y, ctx.GetMapWidth() / 2, ctx.GetMapHeight() / 2);
        return score + (_random.NextDouble() - 0.5) * 0.1;
    }

    private double TreeScore(ITurnContext ctx, int x, int y)
    {
        int close = CountTiles(ctx, x, y, TileType.Tree, 1);
        int near = CountTiles(ctx, x, y, TileType.Tree, 2);
        int far = CountTiles(ctx, x, y, TileType.Tree, 3);
        return close * 300 + near * 55 + far * 10 + (close > 0 ? 350 : 0);
    }

    private double ThreatScore(ITurnContext ctx, ITank me, int x, int y)
    {
        double threat = 0;

        foreach (var e in ctx.GetTanks().Where(t => t.OwnerId != me.OwnerId && !t.Destroyed))
        {
            if (HasShot(e.X, e.Y, x, y, ctx))
                threat += 750 + (Dist(e.X, e.Y, x, y) <= 6 ? 400 : 0);
            else if (BlockedLine(e.X, e.Y, x, y, ctx))
                threat -= 120;
        }

        return threat;
    }

    private int[,] DistanceMap(ITurnContext ctx, int startX, int startY)
    {
        int w = ctx.GetMapWidth(), h = ctx.GetMapHeight();
        int[,] dist = new int[w, h];

        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                dist[x, y] = 999;

        Queue<(int x, int y)> q = new();
        q.Enqueue((startX, startY));
        dist[startX, startY] = 0;

        int[] dx = { 0, 0, -1, 1 };
        int[] dy = { 1, -1, 0, 0 };

        while (q.Count > 0)
        {
            var c = q.Dequeue();

            for (int i = 0; i < 4; i++)
            {
                int nx = c.x + dx[i], ny = c.y + dy[i];
                if (!Inside(ctx, nx, ny) || ctx.GetTile(nx, ny).TileType == TileType.Water || dist[nx, ny] != 999)
                    continue;

                dist[nx, ny] = dist[c.x, c.y] + 1;
                q.Enqueue((nx, ny));
            }
        }

        return dist;
    }

    private HashSet<(int, int)> DangerZones(ITurnContext ctx, ITank me)
    {
        var zones = new HashSet<(int, int)>();

        foreach (var b in ctx.GetBullets())
        {
            DirDelta(b.Direction, out int dx, out int dy);
            int vx = me.X - b.X, vy = me.Y - b.Y;
            if (vx * dx + vy * dy >= 0)
                AddPath(zones, b.X, b.Y, b.Direction, ctx);
        }

        foreach (var e in ctx.GetTanks().Where(t => t.OwnerId != me.OwnerId && !t.Destroyed))
        {
            AddPath(zones, e.X, e.Y, e.TurretDirection, ctx);
            AddPath(zones, e.X, e.Y, TurretTo(e.X, e.Y, me.X, me.Y, e.TurretDirection), ctx);
        }

        return zones;
    }

    private void AddPath(HashSet<(int, int)> zones, int x, int y, TurretDirection dir, ITurnContext ctx)
    {
        DirDelta(dir, out int dx, out int dy);

        for (int i = 0; i <= 20; i++)
        {
            int tx = x + dx * i, ty = y + dy * i;
            if (!Inside(ctx, tx, ty)) break;

            zones.Add((tx, ty));
            if (i > 0 && BlocksShot(ctx.GetTile(tx, ty))) break;
        }
    }

    private bool IsMoveValid(ITurnContext turnContext, ITank myTank, Direction direction, out int nextX, out int nextY)
    {
        nextX = myTank.X;
        nextY = myTank.Y;
        switch (direction)
        {
            case Direction.North: nextY++; break;
            case Direction.South: nextY--; break;
            case Direction.East: nextX--; break;
            case Direction.West: nextX++; break;
        }
        if (nextX < 0 || nextX >= turnContext.GetMapWidth() || nextY < 0 || nextY >= turnContext.GetMapHeight())
            return false;
        ITile nextTile = turnContext.GetTile(nextX, nextY);
        if (nextTile.TileType == TileType.Water) return false;
        int targetX = nextX;
        int targetY = nextY;
        return !turnContext.GetTanks().Any(t => t.X == targetX && t.Y == targetY && !t.Destroyed && t.OwnerId != myTank.OwnerId);
    }


    private void ApplyMove(Direction dir, ref int x, ref int y)
    {
        switch (dir)
        {
            case Direction.North: y++; break;
            case Direction.South: y--; break;
            case Direction.East: x--; break;
            case Direction.West: x++; break;
        }
    }

    private bool HasShot(int x1, int y1, int x2, int y2, ITurnContext ctx)
    {
        return LineCheck(x1, y1, x2, y2, ctx, blockedWanted: false);
    }

    private bool BlockedLine(int x1, int y1, int x2, int y2, ITurnContext ctx)
    {
        return LineCheck(x1, y1, x2, y2, ctx, blockedWanted: true);
    }

    private bool LineCheck(int x1, int y1, int x2, int y2, ITurnContext ctx, bool blockedWanted)
    {
        int dx = x2 - x1, dy = y2 - y1;
        if (dx != 0 && dy != 0 && Math.Abs(dx) != Math.Abs(dy)) return false;

        int sx = Math.Sign(dx), sy = Math.Sign(dy);
        int steps = Math.Max(Math.Abs(dx), Math.Abs(dy));

        for (int i = 1; i < steps; i++)
        {
            int x = x1 + sx * i, y = y1 + sy * i;
            if (!Inside(ctx, x, y)) return false;
            if (BlocksShot(ctx.GetTile(x, y))) return blockedWanted;
        }

        return !blockedWanted;
    }

    private int CountTiles(ITurnContext ctx, int x, int y, TileType type, int radius)
    {
        int count = 0;

        for (int dx = -radius; dx <= radius; dx++)
        for (int dy = -radius; dy <= radius; dy++)
        {
            if (dx == 0 && dy == 0 || Math.Abs(dx) + Math.Abs(dy) > radius) continue;
            int tx = x + dx, ty = y + dy;
            if (Inside(ctx, tx, ty) && ctx.GetTile(tx, ty).TileType == type) count++;
        }

        return count;
    }

    private void DirDelta(TurretDirection dir, out int dx, out int dy)
    {
        dx = 0;
        dy = 0;
        if (dir.HasFlag(TurretDirection.North)) dy = 1;
        if (dir.HasFlag(TurretDirection.South)) dy = -1;
        if (dir.HasFlag(TurretDirection.East)) dx = -1;
        if (dir.HasFlag(TurretDirection.West)) dx = 1;
    }

    private bool Inside(ITurnContext ctx, int x, int y)
    {
        return x >= 0 && x < ctx.GetMapWidth() && y >= 0 && y < ctx.GetMapHeight();
    }

    private bool BlocksShot(ITile tile)
    {
        return tile.TileType == TileType.Tree || tile.TileType == TileType.Building;
    }

    private double Dist(int x1, int y1, int x2, int y2)
    {
        return Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));
    }

    private TurretDirection TurretTo(int fromX, int fromY, int toX, int toY, TurretDirection current)
    {
        int dx = toX - fromX, dy = toY - fromY;

        if (dx > 0 && dy > 0) return TurretDirection.NorthWest;
        if (dx > 0 && dy < 0) return TurretDirection.SouthWest;
        if (dx < 0 && dy > 0) return TurretDirection.NorthEast;
        if (dx < 0 && dy < 0) return TurretDirection.SouthEast;
        if (dx == 0 && dy > 0) return TurretDirection.North;
        if (dx == 0 && dy < 0) return TurretDirection.South;
        if (dx > 0) return TurretDirection.West;
        if (dx < 0) return TurretDirection.East;
        return current;
    }

    private TurretDirection RandomTurretDirection()
    {
        TurretDirection[] dirs =
        {
            TurretDirection.North,
            TurretDirection.South,
            TurretDirection.East,
            TurretDirection.West
        };

        return dirs[_random.Next(dirs.Length)];
    }

    private (TurretDirection dir, ITank enemy, int distance)? FindBestShot(
    ITurnContext ctx,
    ITank me,
    int fromX,
    int fromY,
    ITank? preferredTarget)
    {
        TurretDirection[] dirs =
        {
            TurretDirection.North,
            TurretDirection.South,
            TurretDirection.East,
            TurretDirection.West,
            TurretDirection.NorthEast,
            TurretDirection.NorthWest,
            TurretDirection.SouthEast,
            TurretDirection.SouthWest
        };

        (TurretDirection dir, ITank enemy, int distance)? best = null;
        double bestScore = double.MinValue;

        foreach (var dir in dirs)
        {
            var hit = FirstEnemyHitInDirection(ctx, me, fromX, fromY, dir);
            if (!hit.HasValue) continue;

            double score = 1000;

            // Prefer closer shots.
            score -= hit.Value.distance * 50;

            // Prefer the movement target if possible.
            if (preferredTarget != null && hit.Value.enemy == preferredTarget)
                score += 500;

            // Prefer enemies that are close enough to be dangerous.
            if (hit.Value.distance <= 6)
                score += 250;

            if (score > bestScore)
            {
                bestScore = score;
                best = (dir, hit.Value.enemy, hit.Value.distance);
            }
        }

        return best;
    }
    private (ITank enemy, int distance)? FirstEnemyHitInDirection(
        ITurnContext ctx,
        ITank me,
        int fromX,
        int fromY,
        TurretDirection dir)
    {
        DirDelta(dir, out int dx, out int dy);

        for (int i = 1; i <= 20; i++)
        {
            int x = fromX + dx * i;
            int y = fromY + dy * i;

            if (!Inside(ctx, x, y))
                break;

            if (BlocksShot(ctx.GetTile(x, y)))
                break;

            var tank = ctx.GetTanks()
                .FirstOrDefault(t => !t.Destroyed && t.X == x && t.Y == y);

            if (tank == null)
                continue;

            // Do not shoot allies.
            if (tank.OwnerId == me.OwnerId)
                return null;

            return (tank, i);
        }

        return null;
    }
    private TurretDirection AimToward(int fromX, int fromY, int toX, int toY, TurretDirection current)
    {
        var exact = ExactShotDirection(fromX, fromY, toX, toY);
        if (exact.HasValue)
            return exact.Value;

        int dx = toX - fromX;
        int dy = toY - fromY;

        if (dx == 0 && dy == 0)
            return current;

        int ax = Math.Abs(dx);
        int ay = Math.Abs(dy);

        // Mostly horizontal.
        if (ax > ay * 2)
            return dx > 0 ? TurretDirection.West : TurretDirection.East;

        // Mostly vertical.
        if (ay > ax * 2)
            return dy > 0 ? TurretDirection.North : TurretDirection.South;

        // Rough diagonal fallback.
        if (dx > 0 && dy > 0) return TurretDirection.NorthWest;
        if (dx > 0 && dy < 0) return TurretDirection.SouthWest;
        if (dx < 0 && dy > 0) return TurretDirection.NorthEast;
        if (dx < 0 && dy < 0) return TurretDirection.SouthEast;

        if (dx > 0) return TurretDirection.West;
        if (dx < 0) return TurretDirection.East;
        if (dy > 0) return TurretDirection.North;
        if (dy < 0) return TurretDirection.South;

        return current;
    }
    private TurretDirection? ExactShotDirection(int fromX, int fromY, int toX, int toY)
    {
        int dx = toX - fromX;
        int dy = toY - fromY;

        if (dx == 0 && dy == 0)
            return null;

        if (dx == 0)
            return dy > 0 ? TurretDirection.North : TurretDirection.South;

        if (dy == 0)
            return dx > 0 ? TurretDirection.West : TurretDirection.East;

        if (Math.Abs(dx) != Math.Abs(dy))
            return null;

        if (dx > 0 && dy > 0) return TurretDirection.NorthWest;
        if (dx > 0 && dy < 0) return TurretDirection.SouthWest;
        if (dx < 0 && dy > 0) return TurretDirection.NorthEast;
        if (dx < 0 && dy < 0) return TurretDirection.SouthEast;

        return null;
    }
}