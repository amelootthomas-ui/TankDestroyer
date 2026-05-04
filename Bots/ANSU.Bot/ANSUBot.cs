using TankDestroyer.API;

namespace ANSU.Bot;

[Bot("ANSU TankHunter", "ANSU", "FF6600")]
public class ANSUBot : IPlayerBot
{
    // ENGINE BUG WORKAROUND:
    // PlayerTurnContext.GetTile(y,x) calls World.GetTile(y,x) but World.GetTile(x,y)
    // treats first arg as X. So ctx.GetTile(A,B) = Tiles[(B*W)+A].
    // To get tile at world(x,y) call ctx.GetTile(x, y) — x first.

    // ── Main turn ─────────────────────────────────────────────────────────────

    public void DoTurn(ITurnContext ctx)
    {
        var me = ctx.Tank;

        var enemies = ctx.GetTanks()
            .Where(t => t.OwnerId != me.OwnerId && !t.Destroyed)
            .ToArray();

        if (enemies.Length == 0) return;

        // Target: prefer enemies that fired at us last turn, then nearest
        var target = enemies
            .OrderByDescending(e => e.Fired && EnemyAimedAtUs(e, me) ? 2 : EnemyAimedAtUs(e, me) ? 1 : 0)
            .ThenBy(e => ManhattanDist(me.X, me.Y, e.X, e.Y))
            .First();

        // Decide movement first so we can aim from the post-move position
        var moveDir = ChooseMove(ctx, me, target);

        int postX = moveDir.HasValue ? NewX(me.X, moveDir.Value) : me.X;
        int postY = moveDir.HasValue ? NewY(me.Y, moveDir.Value) : me.Y;

        // Rotate turret toward target from WHERE WE WILL BE after moving
        var aimDir = GetBestAimDirection(postX, postY, target.X, target.Y);
        ctx.RotateTurret(aimDir);

        if (moveDir.HasValue)
            ctx.MoveTank(moveDir.Value);

        // Cannot fire from a tree tile (bullet self-destructs immediately)
        if (GetTileType(ctx, postX, postY) == TileType.Tree) return;

        if (HasClearPath(ctx, postX, postY, target.X, target.Y, aimDir))
            ctx.Fire();
    }

    // ── Movement decision ─────────────────────────────────────────────────────

    private Direction? ChooseMove(ITurnContext ctx, ITank me, ITank target)
    {
        var bullets = ctx.GetBullets();

        // 1. Dodge any bullet that will hit our current position
        foreach (var b in bullets)
        {
            if (IsBulletThreat(b, me.X, me.Y))
            {
                var dodge = GetSafeDodge(ctx, me, bullets);
                if (dodge.HasValue) return dodge;
                break;
            }
        }

        // 2. Emergency cover when health is critical
        if (me.Health <= 25)
        {
            var cover = SeekCover(ctx, me);
            if (cover.HasValue) return cover;
        }

        // 3. Sidestep only when enemy actually fired at us this turn
        //    (avoids the alignment-sidestep-alignment infinite loop)
        if (target.Fired && EnemyAimedAtUs(target, me))
        {
            var sidestep = GetSidestep(ctx, me, target);
            if (sidestep.HasValue) return sidestep;
        }

        // 4. Close in on target / align for a clear cardinal shot
        return GetAlignmentMove(ctx, me, target);
    }

    // ── Dodge ─────────────────────────────────────────────────────────────────

    private static bool IsBulletThreat(IBullet bullet, int x, int y)
    {
        int sx = 0, sy = 0;
        GetStep(bullet.Direction, ref sx, ref sy);
        for (int i = 1; i <= 6; i++)
            if (bullet.X + sx * i == x && bullet.Y + sy * i == y) return true;
        return false;
    }

    private static Direction? GetSafeDodge(ITurnContext ctx, ITank me, IBullet[] allBullets)
    {
        // Mark directions that land in another bullet's path
        var dangerDirs = new HashSet<Direction>();
        foreach (var b in allBullets)
            foreach (var dir in AllDirections())
                if (IsBulletThreat(b, NewX(me.X, dir), NewY(me.Y, dir)))
                    dangerDirs.Add(dir);

        // Prefer perpendicular to the incoming bullet
        var threat = allBullets.First(b => IsBulletThreat(b, me.X, me.Y));
        bool ns = threat.Direction.HasFlag(TurretDirection.North) ||
                  threat.Direction.HasFlag(TurretDirection.South);
        bool ew = threat.Direction.HasFlag(TurretDirection.East) ||
                  threat.Direction.HasFlag(TurretDirection.West);

        Direction[] preferred = (ns && !ew)
            ? new[] { Direction.East, Direction.West, Direction.North, Direction.South }
            : (ew && !ns)
                ? new[] { Direction.North, Direction.South, Direction.East, Direction.West }
                : AllDirections().ToArray();

        foreach (var dir in preferred)
            if (!dangerDirs.Contains(dir) && CanMove(ctx, me, dir)) return dir;

        // Fallback: any passable direction
        foreach (var dir in preferred)
            if (CanMove(ctx, me, dir)) return dir;

        return null;
    }

    // ── Cover ─────────────────────────────────────────────────────────────────

    private static Direction? SeekCover(ITurnContext ctx, ITank me)
    {
        // Tree = 25% damage (best), Building = 50% (better than 75% open ground)
        foreach (var type in new[] { TileType.Tree, TileType.Building })
            foreach (var dir in AllDirections())
            {
                int nx = NewX(me.X, dir), ny = NewY(me.Y, dir);
                if (InBounds(ctx, nx, ny) && GetTileType(ctx, nx, ny) == type && CanMove(ctx, me, dir))
                    return dir;
            }
        return null;
    }

    // ── Sidestep ──────────────────────────────────────────────────────────────

    private static Direction? GetSidestep(ITurnContext ctx, ITank me, ITank enemy)
    {
        int dx = me.X - enemy.X;
        int dy = enemy.Y - me.Y;

        // Step perpendicular to the axis toward the enemy
        Direction[] candidates = (dx == 0)
            ? new[] { Direction.East, Direction.West }
            : (dy == 0)
                ? new[] { Direction.North, Direction.South }
                : new[] { Direction.East, Direction.West, Direction.North, Direction.South };

        // Fallback: just move away even if not perfectly safe
        foreach (var dir in candidates)
            if (CanMove(ctx, me, dir)) return dir;
        return null;
    }

    private static bool EnemyAimedAtUs(ITank enemy, ITank me)
    {
        int sx = 0, sy = 0;
        GetStep(enemy.TurretDirection, ref sx, ref sy);
        for (int i = 1; i <= 6; i++)
            if (enemy.X + sx * i == me.X && enemy.Y + sy * i == me.Y) return true;
        return false;
    }

    // ── Alignment / approach ──────────────────────────────────────────────────

    private static Direction? GetAlignmentMove(ITurnContext ctx, ITank me, ITank target)
    {
        int dx = me.X - target.X;  // positive → target is East
        int dy = target.Y - me.Y;  // positive → target is North

        // Already on the same column → push forward until adjacent
        if (dx == 0)
        {
            if (Math.Abs(dy) > 1)
            {
                var d = dy > 0 ? Direction.North : Direction.South;
                if (CanMove(ctx, me, d)) return d;

                // Blocked (water/tank in the way) → detour sideways
                return CanMove(ctx, me, Direction.East) ? Direction.East
                     : CanMove(ctx, me, Direction.West) ? Direction.West
                     : null;
            }
            return null;
        }

        // Already on the same row → push forward until adjacent
        if (dy == 0)
        {
            if (Math.Abs(dx) > 1)
            {
                var d = dx > 0 ? Direction.East : Direction.West;
                if (CanMove(ctx, me, d)) return d;

                // Blocked → detour vertically
                return CanMove(ctx, me, Direction.North) ? Direction.North
                     : CanMove(ctx, me, Direction.South) ? Direction.South
                     : null;
            }
            return null;
        }

        // Not yet aligned: reduce the smaller axis offset first
        if (Math.Abs(dx) <= Math.Abs(dy))
        {
            var d = dx > 0 ? Direction.East : Direction.West;
            if (CanMove(ctx, me, d)) return d;
        }

        {
            var d = dy > 0 ? Direction.North : Direction.South;
            if (CanMove(ctx, me, d)) return d;
        }

        if (Math.Abs(dx) > Math.Abs(dy))
        {
            var d = dx > 0 ? Direction.East : Direction.West;
            if (CanMove(ctx, me, d)) return d;
        }

        return null;
    }

    // ── Shooting ──────────────────────────────────────────────────────────────

    private static bool HasClearPath(ITurnContext ctx, int fromX, int fromY,
                                     int toX, int toY, TurretDirection dir)
    {
        int sx = 0, sy = 0;
        GetStep(dir, ref sx, ref sy);

        int x = fromX + sx, y = fromY + sy;
        for (int i = 0; i < 6; i++)
        {
            if (!InBounds(ctx, x, y)) return false;
            if (x == toX && y == toY) return true;

            var tt = GetTileType(ctx, x, y);
            if (tt == TileType.Tree || tt == TileType.Building) return false;

            x += sx;
            y += sy;
        }
        return false;
    }

    // ── Direction helpers ─────────────────────────────────────────────────────

    private static TurretDirection GetBestAimDirection(int myX, int myY, int tX, int tY)
    {
        var exact = GetExactDirection(myX, myY, tX, tY);
        if (exact.HasValue) return exact.Value;

        // Map angle to nearest of 8 directions
        int dx = myX - tX;
        int dy = tY - myY;
        double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        if (angle < 0) angle += 360;

        if (angle >= 67.5 && angle < 112.5) return TurretDirection.North;
        if (angle >= 22.5 && angle < 67.5)  return TurretDirection.NorthEast;
        if (angle >= 337.5 || angle < 22.5) return TurretDirection.East;
        if (angle >= 292.5 && angle < 337.5) return TurretDirection.SouthEast;
        if (angle >= 247.5 && angle < 292.5) return TurretDirection.South;
        if (angle >= 202.5 && angle < 247.5) return TurretDirection.SouthWest;
        if (angle >= 157.5 && angle < 202.5) return TurretDirection.West;
        return TurretDirection.NorthWest;
    }

    private static TurretDirection? GetExactDirection(int myX, int myY, int tX, int tY)
    {
        int dx = myX - tX;
        int dy = tY - myY;

        if (dx == 0 && dy > 0) return TurretDirection.North;
        if (dx == 0 && dy < 0) return TurretDirection.South;
        if (dy == 0 && dx > 0) return TurretDirection.East;
        if (dy == 0 && dx < 0) return TurretDirection.West;
        if (dx > 0 && dy > 0 && dx == dy)  return TurretDirection.NorthEast;
        if (dx < 0 && dy > 0 && -dx == dy) return TurretDirection.NorthWest;
        if (dx > 0 && dy < 0 && dx == -dy) return TurretDirection.SouthEast;
        if (dx < 0 && dy < 0 && dx == dy)  return TurretDirection.SouthWest;
        return null;
    }

    private static void GetStep(TurretDirection dir, ref int sx, ref int sy)
    {
        if (dir.HasFlag(TurretDirection.North)) sy += 1;
        if (dir.HasFlag(TurretDirection.South)) sy -= 1;
        if (dir.HasFlag(TurretDirection.East))  sx -= 1;
        if (dir.HasFlag(TurretDirection.West))  sx += 1;
    }

    // ── Tile / movement helpers ───────────────────────────────────────────────

    // Note: ctx.GetTile(x, y) with x first compensates for the engine's x/y swap bug.
    private static TileType GetTileType(ITurnContext ctx, int x, int y)
        => ctx.GetTile(x, y).TileType;

    private static bool InBounds(ITurnContext ctx, int x, int y)
        => x >= 0 && x < ctx.GetMapWidth() && y >= 0 && y < ctx.GetMapHeight();

    private static bool CanMove(ITurnContext ctx, ITank me, Direction dir)
    {
        int nx = NewX(me.X, dir), ny = NewY(me.Y, dir);
        if (!InBounds(ctx, nx, ny)) return false;
        if (GetTileType(ctx, nx, ny) == TileType.Water) return false;
        return ctx.GetTanks().All(t => t.X != nx || t.Y != ny);
    }

    private static int NewX(int x, Direction dir) => dir switch
    {
        Direction.East => x - 1,
        Direction.West => x + 1,
        _ => x
    };

    private static int NewY(int y, Direction dir) => dir switch
    {
        Direction.North => y + 1,
        Direction.South => y - 1,
        _ => y
    };

    private static Direction[] AllDirections() =>
        new[] { Direction.North, Direction.South, Direction.East, Direction.West };

    private static int ManhattanDist(int x1, int y1, int x2, int y2)
        => Math.Abs(x1 - x2) + Math.Abs(y1 - y2);
}
