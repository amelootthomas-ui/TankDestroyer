using TankDestroyer.API;

namespace Hunter.Bot;

[Bot("ApexBot", "The Ultimate Champion", "#FFD700")]
public class ApexBot : IPlayerBot
{
    private readonly Random _random = new();
    private (int x, int y) _lastEnemyPos = (-1, -1);
    private int _turns = 0;

    public void DoTurn(ITurnContext ctx)
    {
        _turns++;
        var me = ctx.Tank;
        var enemies = ctx.GetTanks().Where(t => !t.Destroyed && t.OwnerId != me.OwnerId).ToList();

        if (enemies.Count == 0)
        {
            _turns = 0;
            _lastEnemyPos = (-1, -1);
            return;
        }

        var target = enemies.OrderBy(e => e.Health).ThenBy(e => Dist(me.X, me.Y, e.X, e.Y)).First();

        var state = GetState(me, target);
        var bestFiringCell = FindNearestFiringCell(ctx, me, target);

        var candidates = new List<MoveCandidate>();
        candidates.Add(EvaluateMove(ctx, me, target, null, me.X, me.Y, bestFiringCell, state));

        var directions = new[] { Direction.North, Direction.South, Direction.East, Direction.West };
        foreach (var dir in directions)
        {
            int nx = me.X, ny = me.Y;
            if (dir == Direction.North) ny++;
            else if (dir == Direction.South) ny--;
            else if (dir == Direction.East) nx--;
            else if (dir == Direction.West) nx++;

            if (IsLegal(ctx, nx, ny))
            {
                candidates.Add(EvaluateMove(ctx, me, target, dir, nx, ny, bestFiringCell, state));
            }
        }

        var best = candidates.OrderByDescending(c => c.Score).First();
        if (best.Dir.HasValue)
        {
            ctx.MoveTank(best.Dir.Value);
        }

        int finalX = best.X, finalY = best.Y;
        int targetX = target.X, targetY = target.Y;

        if (_lastEnemyPos.x != -1)
        {
            int predX = target.X + (target.X - _lastEnemyPos.x);
            int predY = target.Y + (target.Y - _lastEnemyPos.y);
            if (IsLegal(ctx, predX, predY) && CanHit(ctx, finalX, finalY, predX, predY))
            {
                targetX = predX;
                targetY = predY;
            }
        }
        _lastEnemyPos = (target.X, target.Y);

        ctx.RotateTurret(GetAimDirection(finalX, finalY, targetX, targetY));

        if (ctx.GetTile(finalX, finalY).TileType != TileType.Tree)
        {
            ctx.Fire();
        }
    }

    private string GetState(ITank me, ITank target)
    {
        if (target.Health <= 30) return "FINISHER";
        if (_turns > 120 && me.Health > target.Health + 20) return "FORTRESS";
        return "AGGRESSIVE";
    }

    private MoveCandidate EvaluateMove(ITurnContext ctx, ITank me, ITank target, Direction? dir, int x, int y, (int x, int y)? firingCell, string state)
    {
        double score = 0;
        var tile = ctx.GetTile(x, y).TileType;

        // A. ABSOLUTE DODGE (The Hunter Layer)
        foreach (var bullet in ctx.GetBullets())
        {
            if (WillBulletHit(ctx, bullet, x, y))
                return new MoveCandidate { Dir = dir, X = x, Y = y, Score = -1000000 };
        }

        // B. EXPOSURE (The Schwarzenegger Layer)
        bool enemyCanHitMe = CanEnemyHitMe(ctx, x, y, target);
        bool iCanHitEnemy = CanHit(ctx, x, y, target.X, target.Y);

        if (enemyCanHitMe)
        {
            int damage = GetDamageAt(ctx, x, y);
            // Higher reward for armor (Buildings/Trees)
            double multiplier = (state == "AGGRESSIVE") ? 300 : (state == "FINISHER" ? 100 : 1500);
            score -= damage * multiplier;
            if (!iCanHitEnemy || tile == TileType.Tree) score -= 30000;
        }

        // C. OFFENSE (The BlitzKrieg Layer)
        bool canFireFromHere = tile != TileType.Tree;
        if (iCanHitEnemy && canFireFromHere)
        {
            if (target.Health <= 75) score += 150000;
            else score += 50000;

            // Buildings are the best offensive spots (Armor + Firing)
            if (tile == TileType.Building) score += 15000;
            if (dir == null) score += 2000;
        }
        else if (firingCell.HasValue)
        {
            int d = Dist(x, y, firingCell.Value.x, firingCell.Value.y);
            score -= d * 800;

            int distToEnemy = Dist(x, y, target.X, target.Y);
            if (state == "AGGRESSIVE") score -= distToEnemy * 500;
        }

        // D. POSITIONING
        int distToTarget = Dist(x, y, target.X, target.Y);
        if (state == "FORTRESS")
        {
            score += distToTarget * 500;
        }
        else
        {
            if (distToTarget >= 3 && distToTarget <= 5) score += 6000;
            else if (distToTarget < 3) score -= (3 - distToTarget) * 3000;
            else score -= distToTarget * 600;
        }

        // E. MAP
        int centerX = ctx.GetMapWidth() / 2;
        int centerY = ctx.GetMapHeight() / 2;
        score -= Dist(x, y, centerX, centerY) * 100;

        return new MoveCandidate { Dir = dir, X = x, Y = y, Score = score };
    }

    private bool CanEnemyHitMe(ITurnContext ctx, int myX, int myY, ITank enemy)
    {
        if (CanHit(ctx, enemy.X, enemy.Y, myX, myY)) return true;

        foreach (var dir in new[] { Direction.North, Direction.South, Direction.East, Direction.West })
        {
            int ex = enemy.X, ey = enemy.Y;
            if (dir == Direction.North) ey++;
            else if (dir == Direction.South) ey--;
            else if (dir == Direction.East) ex--;
            else if (dir == Direction.West) ex++;

            if (ex >= 0 && ex < ctx.GetMapWidth() && ey >= 0 && ey < ctx.GetMapHeight())
            {
                var t = ctx.GetTile(ex, ey).TileType;
                if (t != TileType.Water)
                {
                    if (CanHit(ctx, ex, ey, myX, myY)) return true;
                }
            }
        }
        return false;
    }

    private int GetDamageAt(ITurnContext ctx, int x, int y)
    {
        var tile = ctx.GetTile(x, y).TileType;
        return tile switch
        {
            TileType.Tree => 25,
            TileType.Building => 50,
            _ => 75
        };
    }

    private (int x, int y)? FindNearestFiringCell(ITurnContext ctx, ITank me, ITank target)
    {
        int w = ctx.GetMapWidth();
        int h = ctx.GetMapHeight();
        var visited = new bool[w, h];
        var queue = new Queue<(int x, int y, int d)>();

        queue.Enqueue((me.X, me.Y, 0));
        visited[me.X, me.Y] = true;

        while (queue.Count > 0)
        {
            var curr = queue.Dequeue();
            if (curr.d > 20) break;

            if (CanHit(ctx, curr.x, curr.y, target.X, target.Y))
                return (curr.x, curr.y);

            foreach (var dir in new[] { Direction.North, Direction.South, Direction.East, Direction.West })
            {
                int nx = curr.x, ny = curr.y;
                if (dir == Direction.North) ny++;
                else if (dir == Direction.South) ny--;
                else if (dir == Direction.East) nx--;
                else if (dir == Direction.West) nx++;

                if (nx >= 0 && nx < w && ny >= 0 && ny < h && !visited[nx, ny] && IsLegal(ctx, nx, ny))
                {
                    visited[nx, ny] = true;
                    queue.Enqueue((nx, ny, curr.d + 1));
                }
            }
        }
        return null;
    }

    private bool IsLegal(ITurnContext ctx, int x, int y)
    {
        if (x < 0 || x >= ctx.GetMapWidth() || y < 0 || y >= ctx.GetMapHeight()) return false;
        var tile = ctx.GetTile(x, y);
        if (tile.TileType == TileType.Water) return false;
        if (ctx.GetTanks().Any(t => !t.Destroyed && t.X == x && t.Y == y && t.OwnerId != ctx.Tank.OwnerId)) return false;
        return true;
    }

    private bool CanHit(ITurnContext ctx, int fx, int fy, int tx, int ty)
    {
        if (fx == tx && fy == ty) return false;
        int dx = tx - fx;
        int dy = ty - fy;
        bool straight = dx == 0 || dy == 0;
        bool diagonal = Math.Abs(dx) == Math.Abs(dy);
        if (!straight && !diagonal) return false;

        int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
        if (straight && dist > 6) return false;
        if (diagonal && dist > 4) return false;

        int sx = Math.Sign(dx);
        int sy = Math.Sign(dy);
        for (int i = 1; i < dist; i++)
        {
            int cx = fx + sx * i;
            int cy = fy + sy * i;
            var t = ctx.GetTile(cx, cy).TileType;
            if (t == TileType.Building || t == TileType.Tree) return false;
        }
        return true;
    }

    private bool WillBulletHit(ITurnContext ctx, IBullet b, int tx, int ty)
    {
        if (b.X == ctx.Tank.X && b.Y == ctx.Tank.Y) return false;

        double dx = 0, dy = 0;
        if (b.Direction.HasFlag(TurretDirection.North)) dy = 1;
        if (b.Direction.HasFlag(TurretDirection.South)) dy = -1;
        if (b.Direction.HasFlag(TurretDirection.West)) dx = 1;
        if (b.Direction.HasFlag(TurretDirection.East)) dx = -1;

        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.001) return false;

        double nX = dx / len;
        double nY = dy / len;

        for (int i = 0; i <= 11; i++)
        {
            int bx = (int)(nX * i) + b.X;
            int by = (int)(nY * i) + b.Y;

            if (bx < 0 || bx >= ctx.GetMapWidth() || by < 0 || by >= ctx.GetMapHeight()) break;
            if (bx == tx && by == ty) return true;

            var tile = ctx.GetTile(bx, by).TileType;
            if (i > 0 && (tile == TileType.Building || tile == TileType.Tree)) break;
        }
        return false;
    }

    private TurretDirection GetAimDirection(int fx, int fy, int tx, int ty)
    {
        TurretDirection res = 0;
        if (ty > fy) res |= TurretDirection.North;
        if (ty < fy) res |= TurretDirection.South;
        if (tx > fx) res |= TurretDirection.West;
        if (tx < fx) res |= TurretDirection.East;
        return res;
    }

    private int Dist(int x1, int y1, int x2, int y2) => Math.Abs(x1 - x2) + Math.Abs(y1 - y2);

    private class MoveCandidate
    {
        public Direction? Dir;
        public int X, Y;
        public double Score;
    }
}
