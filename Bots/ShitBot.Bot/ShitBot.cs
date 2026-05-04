using System.Collections;
using System.Numerics;
using System.Runtime.InteropServices;
using TankDestroyer.API;

namespace ShitBot;

[Bot("Maroubot", "Marouane", "000000")]
public class ShitBot : IPlayerBot
{
    private static readonly (Direction Direction, int Dx, int Dy)[] MovementOptions =
    {
        (Direction.North, 0, 1),
        (Direction.South, 0, -1),
        (Direction.East, -1, 0),
        (Direction.West, 1, 0)
    };

    public void DoTurn(ITurnContext turnContext)
    {
        var me = turnContext.Tank;
        var enemies = turnContext.GetTanks().Where(t => t.OwnerId != me.OwnerId && !t.Destroyed).ToArray();
        if (enemies.Length == 0)
        {
            return;
        }

        var allTanks = turnContext.GetTanks();
        var targetEnemy = SelectTarget(me, enemies);
        var bestPlan = ChooseBestPlan(turnContext, me, targetEnemy, enemies, allTanks);

        turnContext.RotateTurret(bestPlan.TurretDirection);
        if (bestPlan.MoveDirection.HasValue)
        {
            turnContext.MoveTank(bestPlan.MoveDirection.Value);
        }

        if (bestPlan.ShouldFire)
        {
            turnContext.Fire();
        }
    }

    private static Plan ChooseBestPlan(ITurnContext turnContext, ITank me, ITank target, ITank[] enemies, ITank[] allTanks)
    {
        Plan best = new(null, me.TurretDirection, false, int.MaxValue);
        foreach (var candidate in GetCandidatePositions(turnContext, me, allTanks))
        {
            var shootableTarget = FindBestShootableTarget(turnContext, candidate.X, candidate.Y, enemies, allTanks, out var shootDirection);
            bool canFire = shootableTarget != null;
            var turretDirection = canFire ? shootDirection : GetTurretDirection(target.X - candidate.X, target.Y - candidate.Y);
            var distance = GetNearestEnemyDistance(candidate.X, candidate.Y, enemies);
            var dangerPenalty = IsInEnemyFireLine(turnContext, candidate.X, candidate.Y, enemies, allTanks) ? 20 : 0;
            var score = (canFire ? 0 : distance * 4) + dangerPenalty + (candidate.MoveDirection.HasValue ? 1 : 0);
            if (canFire)
            {
                score -= 10;
            }

            if (score < best.Score || (score == best.Score && canFire && !best.ShouldFire))
            {
                best = new(candidate.MoveDirection, turretDirection, canFire, score);
            }
        }

        return best;
    }

    private static IEnumerable<(Direction? MoveDirection, int X, int Y)> GetCandidatePositions(ITurnContext turnContext, ITank me, ITank[] allTanks)
    {
        yield return (null, me.X, me.Y);

        foreach (var option in MovementOptions)
        {
            var x = me.X + option.Dx;
            var y = me.Y + option.Dy;
            if (!IsValidMove(turnContext, x, y, allTanks))
            {
                continue;
            }

            yield return (option.Direction, x, y);
        }
    }

    private static bool IsValidMove(ITurnContext turnContext, int x, int y, ITank[] allTanks)
    {
        if (x < 0 || y < 0 || x >= turnContext.GetMapWidth() || y >= turnContext.GetMapHeight())
        {
            return false;
        }

        if (allTanks.Any(t => t.X == x && t.Y == y))
        {
            return false;
        }

        var tile = GetTileAt(turnContext, x, y);
        return tile != null && tile.TileType != TileType.Water;
    }

    private static ITank? FindBestShootableTarget(ITurnContext turnContext, int x, int y, ITank[] enemies, ITank[] allTanks, out TurretDirection turretDirection)
    {
        turretDirection = TurretDirection.North;
        ITank? bestTarget = null;
        var bestDistance = int.MaxValue;

        foreach (var enemy in enemies)
        {
            if (!TryGetTurretDirection(x, y, enemy.X, enemy.Y, out var direction))
            {
                continue;
            }

            if (!IsShotClear(turnContext, x, y, enemy.X, enemy.Y, allTanks, direction))
            {
                continue;
            }

            var distance = Math.Abs(enemy.X - x) + Math.Abs(enemy.Y - y);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestTarget = enemy;
                turretDirection = direction;
            }
        }

        return bestTarget;
    }

    private static bool IsInEnemyFireLine(ITurnContext turnContext, int x, int y, ITank[] enemies, ITank[] allTanks)
    {
        foreach (var enemy in enemies)
        {
            if (enemy.TurretDirection == 0)
            {
                continue;
            }

            var dx = x - enemy.X;
            var dy = y - enemy.Y;
            if (!IsAlignedForTurret(dx, dy, enemy.TurretDirection))
            {
                continue;
            }

            if (IsShotClear(turnContext, enemy.X, enemy.Y, x, y, allTanks, enemy.TurretDirection))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetTurretDirection(int fromX, int fromY, int toX, int toY, out TurretDirection direction)
    {
        direction = GetTurretDirection(toX - fromX, toY - fromY);
        return direction != 0 && IsAlignedForTurret(toX - fromX, toY - fromY, direction);
    }

    private static bool IsAlignedForTurret(int dx, int dy, TurretDirection direction)
    {
        if (dx == 0 && dy == 0)
        {
            return false;
        }

        var hasVertical = direction.HasFlag(TurretDirection.North) || direction.HasFlag(TurretDirection.South);
        var hasHorizontal = direction.HasFlag(TurretDirection.East) || direction.HasFlag(TurretDirection.West);

        if (hasVertical && hasHorizontal)
        {
            return Math.Abs(dx) == Math.Abs(dy);
        }

        if (hasVertical)
        {
            return dx == 0;
        }

        if (hasHorizontal)
        {
            return dy == 0;
        }

        return false;
    }

    private static bool IsShotClear(ITurnContext turnContext, int startX, int startY, int targetX, int targetY, ITank[] allTanks, TurretDirection direction)
    {
        var vector = GetDirectionVector(direction);
        var normalized = Vector2.Normalize(new Vector2(vector.Dx, vector.Dy));

        for (int step = 0; step <= 6; step++)
        {
            var cell = normalized * step;
            var cellX = Math.Clamp((int)cell.X + startX, 0, turnContext.GetMapWidth() - 1);
            var cellY = Math.Clamp((int)cell.Y + startY, 0, turnContext.GetMapHeight() - 1);

            if (cellX == targetX && cellY == targetY)
            {
                return true;
            }

            var blockingTank = allTanks.FirstOrDefault(t => t.X == cellX && t.Y == cellY && t.OwnerId != (startX == targetX && startY == targetY ? -1 : 0));
            if (blockingTank != null && !(blockingTank.X == targetX && blockingTank.Y == targetY))
            {
                return false;
            }

            var tile = GetTileAt(turnContext, cellX, cellY);
            if (tile == null)
            {
                return false;
            }

            if (tile.TileType == TileType.Tree)
            {
                return false;
            }

            if (tile.TileType == TileType.Building && (cellX != startX || cellY != startY))
            {
                return false;
            }
        }

        return false;
    }

    private static (int Dx, int Dy) GetDirectionVector(TurretDirection direction)
    {
        var dx = 0;
        var dy = 0;
        if (direction.HasFlag(TurretDirection.North))
        {
            dy += 1;
        }

        if (direction.HasFlag(TurretDirection.South))
        {
            dy -= 1;
        }

        if (direction.HasFlag(TurretDirection.West))
        {
            dx += 1;
        }

        if (direction.HasFlag(TurretDirection.East))
        {
            dx -= 1;
        }

        return (dx, dy);
    }

    private static TurretDirection GetTurretDirection(int dx, int dy)
    {
        var direction = (TurretDirection)0;
        if (dy > 0)
        {
            direction |= TurretDirection.North;
        }

        if (dy < 0)
        {
            direction |= TurretDirection.South;
        }

        if (dx > 0)
        {
            direction |= TurretDirection.West;
        }

        if (dx < 0)
        {
            direction |= TurretDirection.East;
        }

        return direction == 0 ? TurretDirection.North : direction;
    }

    private static int GetNearestEnemyDistance(int x, int y, ITank[] enemies)
    {
        return enemies.Min(enemy => Math.Abs(enemy.X - x) + Math.Abs(enemy.Y - y));
    }

    private static ITile? GetTileAt(ITurnContext turnContext, int x, int y)
    {
        if (x < 0 || y < 0 || x >= turnContext.GetMapWidth() || y >= turnContext.GetMapHeight())
        {
            return null;
        }

        return turnContext.GetTile(x, y);
    }

    private static ITank SelectTarget(ITank me, ITank[] enemies)
    {
        return enemies
            .OrderBy(e => e.Health)
            .ThenBy(e => Math.Abs(e.X - me.X) + Math.Abs(e.Y - me.Y))
            .First();
    }

    private sealed record Plan(Direction? MoveDirection, TurretDirection TurretDirection, bool ShouldFire, int Score);
}