using TankDestroyer.API;

namespace Aegis.Bot;

[Bot("Aegis bot", "Yassine", "18A999")]
public class AegisBot : IPlayerBot
{
    private static readonly Direction[] MoveDirections =
    [
        Direction.North,
        Direction.South,
        Direction.East,
        Direction.West
    ];

    private readonly Random _random = new();

    public void DoTurn(ITurnContext turnContext)
    {
        var me = turnContext.Tank;
        if (me.Destroyed)
        {
            return;
        }

        var enemies = turnContext.GetTanks()
            .Where(t => t.OwnerId != me.OwnerId && !t.Destroyed)
            .ToArray();

        if (enemies.Length == 0)
        {
            return;
        }

        var current = new Position(me.X, me.Y);
        var bestMove = ChooseMove(turnContext, current, enemies);
        Console.WriteLine($"Aegis: Moving to ({bestMove.Position.X},{bestMove.Position.Y}) from ({current.X},{current.Y})");
        if (bestMove.Direction.HasValue)
        {
            turnContext.MoveTank(bestMove.Direction.Value);
        }

        var firingPosition = bestMove.Position;
        var shot = ChooseShot(turnContext, firingPosition, enemies);
        if (shot.HasValue)
        {
            Console.WriteLine($"Aegis: Shooting at {shot.Value}");
            turnContext.RotateTurret(shot.Value);
            turnContext.Fire();
            return;
        }

        var nearest = enemies
            .OrderBy(t => Distance(firingPosition, new Position(t.X, t.Y)))
            .First();

        var turretDir = DirectionTo(firingPosition, new Position(nearest.X, nearest.Y));
        Console.WriteLine($"Aegis: Rotating turret to {turretDir} towards nearest enemy");
        turnContext.RotateTurret(turretDir);
    }

    private MoveChoice ChooseMove(ITurnContext turnContext, Position current, ITank[] enemies)
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
            .Select(choice => new { choice, score = ScorePosition(turnContext, choice.Position, enemies) })
            .OrderByDescending(x => x.score)
            .ToArray();

        var topScore = scoredChoices.First().score;
        var bestChoices = scoredChoices
            .Where(x => x.score >= topScore - 14)
            .Select(x => x.choice)
            .ToArray();

        return bestChoices[_random.Next(bestChoices.Length)];
    }

    private int ScorePosition(ITurnContext turnContext, Position position, ITank[] enemies)
    {
        var tile = turnContext.GetTile(position.Y, position.X).TileType;
        var nearestEnemyDistance = enemies.Min(enemy => Distance(position, new Position(enemy.X, enemy.Y)));
        var visibleShots = enemies.Count(enemy => CanHit(turnContext, position, new Position(enemy.X, enemy.Y)));

        var score = 0;
        score += tile switch
        {
            TileType.Building => 42,
            TileType.Tree => 28,
            TileType.Sand => 9,
            _ => 0
        };

        score += visibleShots * 130;
        score += enemies.Sum(enemy => TargetPriority(position, enemy));
        score += Math.Max(0, 12 - Math.Min(nearestEnemyDistance, 12)) * 10;

        if (IsInBulletPath(turnContext, position))
        {
            score -= 160;
        }

        if (tile == TileType.Tree && visibleShots > 0)
        {
            score -= 28;
        }

        score += _random.Next(-18, 19);
        return score;
    }

    private int TargetPriority(Position position, ITank enemy)
    {
        var distance = Distance(position, new Position(enemy.X, enemy.Y));
        var score = 0;
        score += Math.Max(0, 12 - distance) * 10;
        score += Math.Max(0, 40 - enemy.Health);
        score += enemy.Health <= 30 ? 50 : 0;
        return score;
    }

    private TurretDirection? ChooseShot(ITurnContext turnContext, Position from, ITank[] enemies)
    {
        if (turnContext.GetTile(from.Y, from.X).TileType == TileType.Tree)
        {
            return null;
        }

        return enemies
            .Where(enemy => CanHit(turnContext, from, new Position(enemy.X, enemy.Y)))
            .OrderBy(enemy => enemy.Health)
            .ThenByDescending(enemy => DamageOnTile(turnContext.GetTile(enemy.Y, enemy.X).TileType))
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