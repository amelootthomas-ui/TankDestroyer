using TankDestroyer.API;

namespace Vanguard.Bot;

[Bot("Vanguard bot", "Yassine", "VANGUARD")]
public class VanguardBot : IPlayerBot
{
    private static readonly Direction[] MoveDirections =
    [
        Direction.North,
        Direction.South,
        Direction.East,
        Direction.West
    ];

    private readonly Random _random = new();
    private int _turnsWithoutProgress = 0;
    private int _lastNearestDistance = int.MaxValue;
    private int _lastHealth = int.MaxValue;

    public void DoTurn(ITurnContext turnContext)
    {
        var me = turnContext.Tank;
        if (me.Destroyed)
        {
            return;
        }
        if (_lastHealth == int.MaxValue)
        {
            _lastHealth = me.Health;
        }

        var enemies = turnContext.GetTanks()
            .Where(t => t.OwnerId != me.OwnerId && !t.Destroyed)
            .ToArray();

        if (enemies.Length == 0)
        {
            return;
        }

        var current = new Position(me.X, me.Y);
        var nearestDistance = enemies.Min(e => Distance(current, new Position(e.X, e.Y)));
        bool hasProgress = false;
        bool healthLost = me.Health < _lastHealth;
        bool attackMode = _turnsWithoutProgress >= 30 || healthLost;

        var bestMove = ChooseMove(turnContext, current, enemies, attackMode);
        Console.WriteLine($"Vanguard: Moving to ({bestMove.Position.X},{bestMove.Position.Y}) from ({current.X},{current.Y}) attackMode={attackMode}");
        if (bestMove.Direction.HasValue)
        {
            turnContext.MoveTank(bestMove.Direction.Value);
        }

        var firingPosition = bestMove.Position;
        var shot = ChooseShot(turnContext, firingPosition, enemies, attackMode);
        if (shot.HasValue)
        {
            Console.WriteLine($"Vanguard: Shooting at {shot.Value}");
            turnContext.RotateTurret(shot.Value);
            turnContext.Fire();
            hasProgress = true;
        }
        else
        {
            var nearest = enemies
                .OrderBy(t => Distance(firingPosition, new Position(t.X, t.Y)))
                .First();

            var turretDir = DirectionTo(firingPosition, new Position(nearest.X, nearest.Y));
            Console.WriteLine($"Vanguard: Rotating turret to {turretDir} towards nearest enemy");
            turnContext.RotateTurret(turretDir);
        }

        // Check progress
        var newNearestDistance = enemies.Min(e => Distance(bestMove.Position, new Position(e.X, e.Y)));
        if (hasProgress || healthLost || newNearestDistance < _lastNearestDistance)
        {
            _turnsWithoutProgress = 0;
        }
        else
        {
            _turnsWithoutProgress++;
        }
        _lastNearestDistance = newNearestDistance;
        _lastHealth = me.Health;

        Console.WriteLine($"Vanguard: Turns without progress: {_turnsWithoutProgress}");
    }

    private MoveChoice ChooseMove(ITurnContext turnContext, Position current, ITank[] enemies, bool attackMode)
    {
        var candidates = new List<MoveChoice> { new(null, current) };
        foreach (var direction in MoveDirections)
        {
            var next = Step(current, direction);
            if (CanMoveTo(turnContext, next))
            {
                candidates.Add(new MoveChoice(direction, next));
            }
        }

        var scoredChoices = candidates
            .Select(choice => new { choice, score = ScorePosition(turnContext, choice.Position, enemies, attackMode) })
            .ToArray();

        var bestScore = scoredChoices.Max(x => x.score);
        var bestChoices = scoredChoices
            .Where(x => x.score == bestScore)
            .Select(x => x.choice)
            .ToArray();

        return bestChoices[_random.Next(bestChoices.Length)];
    }

    private int ScorePosition(ITurnContext turnContext, Position position, ITank[] enemies, bool attackMode)
    {
        var tile = turnContext.GetTile(position.Y, position.X).TileType;
        var nearestEnemyDistance = enemies.Min(enemy => Distance(position, new Position(enemy.X, enemy.Y)));
        var visibleShots = enemies.Count(enemy => CanHit(turnContext, position, new Position(enemy.X, enemy.Y)));

        var score = 0;
        if (attackMode)
        {
            // Full attack mode: ignore cover, prioritize closing distance
            score += (16 - Math.Min(nearestEnemyDistance, 16)) * 20;
            score += visibleShots * 200;
            // Ignore bullet paths in attack mode
        }
        else
        {
            score += tile switch
            {
                TileType.Building => 40,
                TileType.Tree => 25,
                TileType.Sand => 8,
                _ => 0
            };

            score += visibleShots * 160;
            score += (14 - Math.Min(nearestEnemyDistance, 14)) * 14;

            if (IsInBulletPath(turnContext, position))
            {
                score -= 180;
            }

            if (tile == TileType.Tree && visibleShots > 0)
            {
                score -= 40;
            }

            score += _random.Next(-20, 21);
        }

        return score;
    }

    private TurretDirection? ChooseShot(ITurnContext turnContext, Position from, ITank[] enemies, bool attackMode)
    {
        if (turnContext.GetTile(from.Y, from.X).TileType == TileType.Tree)
        {
            return null;
        }

        if (attackMode)
        {
            // In attack mode, shoot at closest enemy even if not optimal
            var nearest = enemies.OrderBy(e => Distance(from, new Position(e.X, e.Y))).First();
            if (CanHit(turnContext, from, new Position(nearest.X, nearest.Y)))
            {
                return DirectionTo(from, new Position(nearest.X, nearest.Y));
            }
        }

        return enemies
            .Where(enemy => CanHit(turnContext, from, new Position(enemy.X, enemy.Y)))
            .OrderByDescending(enemy => DamageOnTile(turnContext.GetTile(enemy.Y, enemy.X).TileType))
            .ThenBy(enemy => Distance(from, new Position(enemy.X, enemy.Y)))
            .Select(enemy => (TurretDirection?)DirectionTo(from, new Position(enemy.X, enemy.Y)))
            .FirstOrDefault();
    }

    private static int DamageOnTile(TileType tileType)
    {
        return tileType switch
        {
            TileType.Tree => 25,
            TileType.Building => 50,
            _ => 75
        };
    }

    private bool CanMoveTo(ITurnContext turnContext, Position position)
    {
        if (position.X < 0 ||
            position.Y < 0 ||
            position.X >= turnContext.GetMapWidth() ||
            position.Y >= turnContext.GetMapHeight())
        {
            return false;
        }

        if (turnContext.GetTile(position.Y, position.X).TileType == TileType.Water)
        {
            return false;
        }

        return !turnContext.GetTanks()
            .Any(t => !t.Destroyed && t.X == position.X && t.Y == position.Y);
    }

    private bool CanHit(ITurnContext turnContext, Position from, Position target)
    {
        if (!IsStraightOrDiagonal(from, target))
        {
            return false;
        }

        var distance = Distance(from, target);
        if (distance > 6)
        {
            return false;
        }

        var stepX = Math.Sign(target.X - from.X);
        var stepY = Math.Sign(target.Y - from.Y);
        for (var i = 1; i < distance; i++)
        {
            var tile = turnContext.GetTile(from.Y + (stepY * i), from.X + (stepX * i)).TileType;
            if (tile is TileType.Tree or TileType.Building)
            {
                return false;
            }
        }

        return true;
    }

    private bool IsInBulletPath(ITurnContext turnContext, Position position)
    {
        foreach (var bullet in turnContext.GetBullets())
        {
            if (bullet.X == position.X && bullet.Y == position.Y)
            {
                return true;
            }

            var step = StepFor(bullet.Direction);
            for (var i = 1; i <= 6; i++)
            {
                var x = bullet.X + (step.X * i);
                var y = bullet.Y + (step.Y * i);
                if (x < 0 || y < 0 || x >= turnContext.GetMapWidth() || y >= turnContext.GetMapHeight())
                {
                    break;
                }

                if (x == position.X && y == position.Y)
                {
                    return true;
                }

                var tile = turnContext.GetTile(y, x).TileType;
                if (tile is TileType.Tree or TileType.Building)
                {
                    break;
                }
            }
        }

        return false;
    }

    private static bool IsStraightOrDiagonal(Position from, Position to)
    {
        var dx = Math.Abs(to.X - from.X);
        var dy = Math.Abs(to.Y - from.Y);
        return dx == 0 || dy == 0 || dx == dy;
    }

    private static Position Step(Position position, Direction direction)
    {
        return direction switch
        {
            Direction.North => position with { Y = position.Y + 1 },
            Direction.South => position with { Y = position.Y - 1 },
            Direction.East => position with { X = position.X - 1 },
            Direction.West => position with { X = position.X + 1 },
            _ => position
        };
    }

    private static Position StepFor(TurretDirection direction)
    {
        var x = 0;
        var y = 0;
        if (direction.HasFlag(TurretDirection.North))
        {
            y++;
        }

        if (direction.HasFlag(TurretDirection.South))
        {
            y--;
        }

        if (direction.HasFlag(TurretDirection.West))
        {
            x++;
        }

        if (direction.HasFlag(TurretDirection.East))
        {
            x--;
        }

        return new Position(x, y);
    }

    private static TurretDirection DirectionTo(Position from, Position to)
    {
        var direction = 0;
        if (to.Y > from.Y)
        {
            direction |= (int)TurretDirection.North;
        }
        else if (to.Y < from.Y)
        {
            direction |= (int)TurretDirection.South;
        }

        if (to.X < from.X)
        {
            direction |= (int)TurretDirection.East;
        }
        else if (to.X > from.X)
        {
            direction |= (int)TurretDirection.West;
        }

        return direction == 0 ? TurretDirection.North : (TurretDirection)direction;
    }

    private static int Distance(Position from, Position to)
    {
        return Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y));
    }

    private readonly record struct Position(int X, int Y);

    private readonly record struct MoveChoice(Direction? Direction, Position Position);
}