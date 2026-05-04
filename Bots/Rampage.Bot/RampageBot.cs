using TankDestroyer.API;

namespace Rampage.Bot;

[Bot("Rampage bot", "AI Assistant", "RAMPAGE")]
public class RampageBot : IPlayerBot
{
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

        // Vind de dichtstbijzijnde vijand
        var nearestEnemy = enemies
            .OrderBy(t => Distance(new Position(me.X, me.Y), new Position(t.X, t.Y)))
            .First();

        var targetPosition = new Position(nearestEnemy.X, nearestEnemy.Y);

        // Beweeg richting de vijand
        var direction = GetDirectionTowards(new Position(me.X, me.Y), targetPosition);
        if (CanMoveTo(turnContext, Step(new Position(me.X, me.Y), direction)))
        {
            turnContext.MoveTank(direction);
        }

        // Probeer te schieten
        var shotDirection = DirectionTo(new Position(me.X, me.Y), targetPosition);
        if (CanHit(turnContext, new Position(me.X, me.Y), targetPosition))
        {
            turnContext.RotateTurret(shotDirection);
            turnContext.Fire();
        }
        else
        {
            // Draai turret naar vijand
            turnContext.RotateTurret(shotDirection);
        }
    }

    private bool CanMoveTo(ITurnContext turnContext, Position position)
    {
        if (position.X < 0 || position.Y < 0 ||
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

    private Direction GetDirectionTowards(Position from, Position to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;

        if (Math.Abs(dx) > Math.Abs(dy))
        {
            return dx > 0 ? Direction.West : Direction.East;
        }
        else
        {
            return dy > 0 ? Direction.North : Direction.South;
        }
    }

    private TurretDirection DirectionTo(Position from, Position to)
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

    private static int Distance(Position from, Position to)
    {
        return Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y));
    }

    private readonly record struct Position(int X, int Y);
}