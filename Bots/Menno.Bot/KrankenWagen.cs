using System;
using System.Collections.Generic;
using System.Linq;
using TankDestroyer.API;

namespace Menno.Bot;

[Bot("Krankenwagen", "Menno", "FF0090")]
public class KrankenWagen : IPlayerBot
{
    private static readonly Direction[] MoveDirections =
    {
        Direction.North,
        Direction.South,
        Direction.East,
        Direction.West
    };

    private readonly Random _random = new();
    private readonly Queue<string> _recentPositions = new();

    private Direction? _lastMove;
    private int _lastEnemyHealth = -1;
    private int _noProgressTurns;
    private int _turn;

    public void DoTurn(ITurnContext ctx)
    {
        var me = ctx.Tank;

        if (me == null || me.Destroyed)
            return;

        var enemies = ctx.GetTanks()
            .Where(t => !t.Destroyed && t.OwnerId != me.OwnerId)
            .ToArray();

        if (enemies.Length == 0)
            return;

        TrackProgress(me, enemies);

        var best = BuildCandidates(ctx, me, enemies)
            .OrderByDescending(c => c.Score)
            .First();

        if (best.Move.HasValue)
        {
            ctx.MoveTank(best.Move.Value);
            _lastMove = best.Move.Value;
        }

        var target = best.ShotTarget ?? PickStrategicTarget(ctx, best.X, best.Y, enemies);

        var aimDirection = best.ShotTarget != null
            ? DirectionToTarget(best.X, best.Y, target.X, target.Y)
            : DirectionTowardApprox(best.X, best.Y, target.X, target.Y);

        if ((int)aimDirection != 0)
            ctx.RotateTurret(aimDirection);

        if (TileAt(ctx, best.X, best.Y) != TileType.Tree)
            ctx.Fire();
    }

    private List<Candidate> BuildCandidates(ITurnContext ctx, ITank me, ITank[] enemies)
    {
        var candidates = new List<Candidate>
        {
            EvaluateCandidate(ctx, me, enemies, null, me.X, me.Y)
        };

        foreach (var move in MoveDirections)
        {
            var next = PositionAfterMove(me.X, me.Y, move);

            if (IsLegalMoveDestination(ctx, next.X, next.Y))
                candidates.Add(EvaluateCandidate(ctx, me, enemies, move, next.X, next.Y));
        }

        return candidates;
    }

    private Candidate EvaluateCandidate(
        ITurnContext ctx,
        ITank me,
        ITank[] enemies,
        Direction? move,
        int x,
        int y)
    {
        var candidate = new Candidate
        {
            Move = move,
            X = x,
            Y = y
        };

        var score = 0;
        var tile = TileAt(ctx, x, y);
        var stalemate = IsStalemateMode();

        var incomingBulletDamage = ExistingBulletDamage(ctx, x, y);

        if (incomingBulletDamage > 0)
        {
            score -= 100_000;
            score -= incomingBulletDamage * 1_500;
        }

        var shotTarget = PickBestShotTarget(ctx, x, y, enemies);
        candidate.ShotTarget = shotTarget;

        score += CoverScore(tile, shotTarget != null);

        if (shotTarget != null)
        {
            var damage = DamageAt(ctx, shotTarget.X, shotTarget.Y);
            var distance = Distance(x, y, shotTarget.X, shotTarget.Y);

            score += 7_000;
            score += damage * 80;
            score += (100 - shotTarget.Health) * 35;
            score -= distance * 25;

            if (damage >= shotTarget.Health)
                score += 25_000;

            if (shotTarget.Health <= 25)
                score += 5_000;
            else if (shotTarget.Health <= 50)
                score += 2_500;
            else if (shotTarget.Health <= 75)
                score += 1_000;

            score += x == shotTarget.X || y == shotTarget.Y ? 600 : 350;

            if (stalemate)
                score += 1_500;
        }
        else
        {
            var pathToShot = DistanceToNearestFiringCell(ctx, x, y, enemies);

            score -= Math.Min(pathToShot, 10) * (stalemate ? 650 : 350);
            score += AlignmentPotential(ctx, x, y, enemies);
            score -= DistanceToClosestEnemy(x, y, enemies) * (stalemate ? 35 : 15);
        }

        var exposureDamage = EnemyExposureDamage(ctx, x, y, enemies);
        var exposureCount = EnemyExposureCount(ctx, x, y, enemies);

        score -= exposureDamage * 90;
        score -= exposureCount * 750;

        score += MobilityScore(ctx, me, x, y) * 120;
        score -= EdgePenalty(ctx, x, y);

        if (_lastMove.HasValue && move.HasValue && WouldReverse(_lastMove.Value, move.Value))
            score += shotTarget != null ? 180 : 45;

        if (move == null && shotTarget != null)
            score += 250;

        if (stalemate)
        {
            var key = $"{x}:{y}";
            var visits = _recentPositions.Count(p => p == key);

            score -= visits * 180;

            if (move == null && shotTarget == null)
                score -= 800;
        }

        score += _random.Next(0, 20);

        candidate.Score = score;
        return candidate;
    }

    private void TrackProgress(ITank me, ITank[] enemies)
    {
        _turn++;

        _recentPositions.Enqueue($"{me.X}:{me.Y}");

        while (_recentPositions.Count > 10)
            _recentPositions.Dequeue();

        var enemyHealth = enemies.Sum(e => Math.Max(0, e.Health));

        if (_lastEnemyHealth >= 0 && enemyHealth < _lastEnemyHealth)
            _noProgressTurns = 0;
        else
            _noProgressTurns++;

        _lastEnemyHealth = enemyHealth;
    }

    private bool IsStalemateMode()
    {
        if (_noProgressTurns >= 60)
            return true;

        var recent = _recentPositions.ToArray();

        return _turn > 80
               && recent.Length >= 8
               && recent.TakeLast(8).Distinct().Count() <= 2;
    }

    private ITank? PickBestShotTarget(ITurnContext ctx, int fromX, int fromY, ITank[] enemies)
    {
        ITank? best = null;
        var bestScore = int.MinValue;

        foreach (var enemy in enemies)
        {
            if (!CanHit(ctx, fromX, fromY, enemy.X, enemy.Y))
                continue;

            var damage = DamageAt(ctx, enemy.X, enemy.Y);
            var distance = Distance(fromX, fromY, enemy.X, enemy.Y);

            var score = damage * 100;
            score += (100 - enemy.Health) * 45;
            score -= distance * 30;

            if (damage >= enemy.Health)
                score += 30_000;

            if (enemy.Health <= 25)
                score += 5_000;
            else if (enemy.Health <= 50)
                score += 2_500;
            else if (enemy.Health <= 75)
                score += 1_000;

            if (score > bestScore)
            {
                bestScore = score;
                best = enemy;
            }
        }

        return best;
    }

    private ITank PickStrategicTarget(ITurnContext ctx, int fromX, int fromY, ITank[] enemies)
    {
        return enemies
            .OrderByDescending(e =>
            {
                var score = (100 - e.Health) * 80;
                score -= Distance(fromX, fromY, e.X, e.Y) * 25;

                if (CanHit(ctx, e.X, e.Y, fromX, fromY))
                    score += 1_500;

                if (IsOnShotLine(fromX, fromY, e.X, e.Y))
                    score += 600;

                return score;
            })
            .First();
    }

    private static int CoverScore(TileType tile, bool hasShot)
    {
        return tile switch
        {
            TileType.Building => 1_600,
            TileType.Tree => hasShot ? -4_000 : 1_400,
            TileType.Sand => 180,
            TileType.Grass => 100,
            _ => 0
        };
    }

    private int ExistingBulletDamage(ITurnContext ctx, int x, int y)
    {
        var total = 0;

        foreach (var bullet in ctx.GetBullets())
        {
            if (WouldBulletHitCell(ctx, bullet, x, y))
                total += DamageAt(ctx, x, y);
        }

        return total;
    }

    private bool WouldBulletHitCell(ITurnContext ctx, IBullet bullet, int targetX, int targetY)
    {
        foreach (var cell in BulletCells(
                     bullet.X,
                     bullet.Y,
                     bullet.Direction,
                     ctx.GetMapWidth(),
                     ctx.GetMapHeight()))
        {
            if (cell.X == targetX && cell.Y == targetY)
                return true;

            var tile = TileAt(ctx, cell.X, cell.Y);

            if (tile == TileType.Tree)
                return false;

            if (tile == TileType.Building && (cell.X != bullet.X || cell.Y != bullet.Y))
                return false;
        }

        return false;
    }

    private int EnemyExposureDamage(ITurnContext ctx, int x, int y, ITank[] enemies)
    {
        var total = 0;
        var damage = DamageAt(ctx, x, y);

        foreach (var enemy in enemies)
        {
            if (EnemyCanShootCell(ctx, enemy, x, y))
                total += damage;
        }

        return total;
    }

    private int EnemyExposureCount(ITurnContext ctx, int x, int y, ITank[] enemies)
    {
        return enemies.Count(enemy => EnemyCanShootCell(ctx, enemy, x, y));
    }

    private bool EnemyCanShootCell(ITurnContext ctx, ITank enemy, int targetX, int targetY)
    {
        return TankPossiblePositions(ctx, enemy)
            .Any(position => CanHit(ctx, position.X, position.Y, targetX, targetY));
    }

    private IEnumerable<Position> TankPossiblePositions(ITurnContext ctx, ITank tank)
    {
        yield return new Position(tank.X, tank.Y);

        foreach (var move in MoveDirections)
        {
            var next = PositionAfterMove(tank.X, tank.Y, move);

            if (IsLegalMoveDestination(ctx, next.X, next.Y))
                yield return next;
        }
    }

    private bool CanHit(ITurnContext ctx, int fromX, int fromY, int toX, int toY)
    {
        if (fromX == toX && fromY == toY)
            return false;

        if (TileAt(ctx, fromX, fromY) == TileType.Tree)
            return false;

        if (!IsOnShotLine(fromX, fromY, toX, toY))
            return false;

        var dx = Math.Abs(toX - fromX);
        var dy = Math.Abs(toY - fromY);

        var range = Math.Max(dx, dy);

        if (dx == dy)
        {
            if (range > 4)
                return false;
        }
        else
        {
            if (range > 6)
                return false;
        }

        var direction = DirectionToTarget(fromX, fromY, toX, toY);

        foreach (var cell in BulletCells(fromX, fromY, direction, ctx.GetMapWidth(), ctx.GetMapHeight()).Skip(1))
        {
            if (cell.X == toX && cell.Y == toY)
                return true;

            var tile = TileAt(ctx, cell.X, cell.Y);

            if (tile is TileType.Tree or TileType.Building)
                return false;
        }

        return false;
    }

    private int DistanceToNearestFiringCell(ITurnContext ctx, int startX, int startY, ITank[] enemies)
    {
        const int searchLimit = 8;

        if (!InsideMap(ctx, startX, startY))
            return 99;

        var visited = new bool[ctx.GetMapWidth(), ctx.GetMapHeight()];
        var queue = new Queue<Position>();

        visited[startX, startY] = true;
        queue.Enqueue(new Position(startX, startY, 0));

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (current.Distance > searchLimit)
                continue;

            if (enemies.Any(e => CanHit(ctx, current.X, current.Y, e.X, e.Y)))
                return current.Distance;

            foreach (var move in MoveDirections)
            {
                var next = PositionAfterMove(current.X, current.Y, move);

                if (!InsideMap(ctx, next.X, next.Y))
                    continue;

                if (visited[next.X, next.Y])
                    continue;

                if (!IsPassableForPath(ctx, next.X, next.Y))
                    continue;

                visited[next.X, next.Y] = true;
                queue.Enqueue(new Position(next.X, next.Y, current.Distance + 1));
            }
        }

        return 99;
    }

    private static IEnumerable<Position> BulletCells(
        int startX,
        int startY,
        TurretDirection direction,
        int width,
        int height)
    {
        var vector = DirectionVector(direction);

        var length = Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y);

        if (length <= 0.0001)
            yield break;

        var normalizedX = vector.X / length;
        var normalizedY = vector.Y / length;

        for (var i = 0; i <= 6; i++)
        {
            yield return new Position(
                Clamp((int)(normalizedX * i) + startX, 0, width - 1),
                Clamp((int)(normalizedY * i) + startY, 0, height - 1));
        }
    }

    private static Position DirectionVector(TurretDirection direction)
    {
        var x = 0;
        var y = 0;

        if (direction.HasFlag(TurretDirection.North))
            y += 1;

        if (direction.HasFlag(TurretDirection.South))
            y -= 1;

        if (direction.HasFlag(TurretDirection.West))
            x += 1;

        if (direction.HasFlag(TurretDirection.East))
            x -= 1;

        return new Position(x, y);
    }

    private int AlignmentPotential(ITurnContext ctx, int x, int y, ITank[] enemies)
    {
        var score = 0;

        foreach (var enemy in enemies)
        {
            var distance = Distance(x, y, enemy.X, enemy.Y);

            if (IsOnShotLine(x, y, enemy.X, enemy.Y))
            {
                score += 750;
                score -= distance * 30;
            }

            if (CanHit(ctx, enemy.X, enemy.Y, x, y))
                score -= 500;
        }

        return score;
    }

    private int MobilityScore(ITurnContext ctx, ITank me, int x, int y)
    {
        var count = 0;

        foreach (var move in MoveDirections)
        {
            var next = PositionAfterMove(x, y, move);

            if (!InsideMap(ctx, next.X, next.Y))
                continue;

            if (TileAt(ctx, next.X, next.Y) == TileType.Water)
                continue;

            if (ctx.GetTanks().Any(t => t.X == next.X && t.Y == next.Y))
                continue;

            count++;
        }

        return count;
    }

    private int EdgePenalty(ITurnContext ctx, int x, int y)
    {
        var minEdge = Math.Min(
            Math.Min(x, ctx.GetMapWidth() - 1 - x),
            Math.Min(y, ctx.GetMapHeight() - 1 - y));

        return minEdge switch
        {
            0 => 350,
            1 => 120,
            _ => 0
        };
    }

    private bool IsLegalMoveDestination(ITurnContext ctx, int x, int y)
    {
        return InsideMap(ctx, x, y)
               && TileAt(ctx, x, y) != TileType.Water
               && !ctx.GetTanks().Any(t => t.X == x && t.Y == y);
    }

    private bool IsPassableForPath(ITurnContext ctx, int x, int y)
    {
        return InsideMap(ctx, x, y)
               && TileAt(ctx, x, y) != TileType.Water
               && !ctx.GetTanks().Any(t => t.X == x && t.Y == y);
    }

    private static Position PositionAfterMove(int x, int y, Direction direction)
    {
        return direction switch
        {
            Direction.North => new Position(x, y + 1),
            Direction.South => new Position(x, y - 1),
            Direction.East => new Position(x - 1, y),
            Direction.West => new Position(x + 1, y),
            _ => new Position(x, y)
        };
    }

    private static TurretDirection DirectionToTarget(int fromX, int fromY, int toX, int toY)
    {
        var result = (TurretDirection)0;

        if (toY > fromY)
            result |= TurretDirection.North;
        else if (toY < fromY)
            result |= TurretDirection.South;

        if (toX > fromX)
            result |= TurretDirection.West;
        else if (toX < fromX)
            result |= TurretDirection.East;

        return result;
    }

    private static TurretDirection DirectionTowardApprox(int fromX, int fromY, int toX, int toY)
    {
        var dx = toX - fromX;
        var dy = toY - fromY;

        var absDx = Math.Abs(dx);
        var absDy = Math.Abs(dy);

        var horizontal = dx switch
        {
            > 0 => TurretDirection.West,
            < 0 => TurretDirection.East,
            _ => (TurretDirection)0
        };

        var vertical = dy switch
        {
            > 0 => TurretDirection.North,
            < 0 => TurretDirection.South,
            _ => (TurretDirection)0
        };

        if (absDx == 0)
            return vertical;

        if (absDy == 0)
            return horizontal;

        if (absDx >= absDy * 2)
            return horizontal;

        if (absDy >= absDx * 2)
            return vertical;

        return horizontal | vertical;
    }

    private static bool IsOnShotLine(int x1, int y1, int x2, int y2)
    {
        var dx = Math.Abs(x1 - x2);
        var dy = Math.Abs(y1 - y2);

        return dx == 0 || dy == 0 || dx == dy;
    }

    private static bool WouldReverse(Direction previous, Direction current)
    {
        return previous switch
        {
            Direction.North => current == Direction.South,
            Direction.South => current == Direction.North,
            Direction.East => current == Direction.West,
            Direction.West => current == Direction.East,
            _ => false
        };
    }

    private static int DamageAt(ITurnContext ctx, int x, int y)
    {
        return TileAt(ctx, x, y) switch
        {
            TileType.Tree => 25,
            TileType.Building => 50,
            _ => 75
        };
    }

    private static TileType TileAt(ITurnContext ctx, int x, int y)
    {
        return ctx.GetTile(x, y).TileType;
    }

    private static bool InsideMap(ITurnContext ctx, int x, int y)
    {
        return x >= 0
               && y >= 0
               && x < ctx.GetMapWidth()
               && y < ctx.GetMapHeight();
    }

    private static int DistanceToClosestEnemy(int x, int y, ITank[] enemies)
    {
        return enemies.Min(e => Distance(x, y, e.X, e.Y));
    }

    private static int Distance(int x1, int y1, int x2, int y2)
    {
        return Math.Abs(x1 - x2) + Math.Abs(y1 - y2);
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min)
            return min;

        if (value > max)
            return max;

        return value;
    }

    private sealed class Candidate
    {
        public Direction? Move { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Score { get; set; }
        public ITank? ShotTarget { get; set; }
    }

    private sealed class Position
    {
        public Position(int x, int y, int distance = 0)
        {
            X = x;
            Y = y;
            Distance = distance;
        }

        public int X { get; }
        public int Y { get; }
        public int Distance { get; }
    }
}