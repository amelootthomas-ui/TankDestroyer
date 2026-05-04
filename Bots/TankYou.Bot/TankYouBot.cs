using TankDestroyer.API;

namespace TankYou.Bot;


// [Bot("Tank You Dummy 3", "Hannah W.", "E303FC")]
// public class TankYouBot3 : TankYouBot;
//
// [Bot("Tank You Dummy 4", "Hannah W.", "E303FC")]
// public class TankYouBot4 : TankYouBot;
//
// [Bot("Tank You Dummy 2", "Hannah W.", "E303FC")]
// public class TankYouBot2 : TankYouBot;

[Bot("Tank You", "Hannah W.", "E303FC")]
public class TankYouBot : IPlayerBot
{
    private Sentiment _sentiment = Sentiment.Cautious;
    private Targeting _targeting = Targeting.Nearest;
    private (int x, int y)? _orbitGoal = null;
    private int goalAge = 0;
    private TurnActions _actions = new TurnActions();

    public void DoTurn(ITurnContext turnContext)
    {
        _actions = new TurnActions
        {
            Fire = true
        };
        goalAge++;
        if (!_actions.Moved)
        {
            var (x, y) = (turnContext.Tank.X, turnContext.Tank.Y);
            var enemies = turnContext.GetTanks()
              .Where(t => t.OwnerId != turnContext.Tank.OwnerId && !t.Destroyed)
              .ToList();
            enemies.Sort((t1, t2) => Math.Sqrt(Math.Pow(t1.X - x, 2) + Math.Pow(t1.Y - y, 2)).CompareTo(
                Math.Sqrt(Math.Pow(t2.X - x, 2) + Math.Pow(t2.Y - y, 2))));

            if (enemies.Count == 0)
            {
                return;
            }
            var target = enemies[0];
            if (target.Position().SamePosition((x, y)))
            {
                return;
            }

            if (_orbitGoal == null || _orbitGoal.Value == (x, y) || goalAge > 3)
            {
                goalAge = 0;
                _orbitGoal = PathFinder.GetOrbitGoal(turnContext, (x, y), target);
            }

            var path = PathFinder.FindPath(turnContext, (x, y), _orbitGoal.Value);
            PathVisualiser.Print(turnContext, path);
            if (path.Count > 1)
            {
                _actions.MoveDirection = path[1].ToDirection((x, y));
            }
            _actions.RotateDirection = target.Position().ToTurretDirection((x, y));
        }

        // TODO: Determine if we can hit an opponent and shoot if so
        // TODO: Move towards the nearest opponent

        if (_actions.Moved)
        {
            turnContext.MoveTank(_actions.MoveDirection!.Value.Opposite());
        }

        if (_actions.RotateDirection.HasValue)
        {
            turnContext.RotateTurret(_actions.RotateDirection.Value);
        }

        if (_actions.Fire)
        {
            turnContext.Fire();
        }
    }
}

internal static class ITankExtensions
{
    public static (int x, int y) Position(this ITank tank) => (tank.X, tank.Y);
    public static bool SamePosition(this (int x, int y) po1, (int x, int y) po2) => po1.x == po2.x && po1.y == po2.y;
}

internal static class DirectionExtensions
{
    public static Direction Opposite(this Direction direction) => direction switch
    {
        Direction.North => Direction.South,
        Direction.East => Direction.West,
        Direction.South => Direction.North,
        Direction.West => Direction.East,
        _ => throw new NotImplementedException(),
    };
    public static Direction ToDirection(this (int x, int y) target, (int x, int y) current)
    {
        var dx = target.x - current.x;
        var dy = target.y - current.y;

        if (WithinDeadzone(dx) && dy < 0) return Direction.North;
        if (dx > 0 && WithinDeadzone(dy)) return Direction.East;
        if (WithinDeadzone(dx) && dy > 0) return Direction.South;
        if (dx < 0 && WithinDeadzone(dy)) return Direction.West;

        if (dy < dx) return Direction.South;
        if (dx < dy) return Direction.West;
        if (dy < -dx) return Direction.North;
        if (dx < -dy) return Direction.East;

        if (dx == dy) return Direction.South;
        if (dx == -dy) return Direction.North;
        if (-dx == dy) return Direction.South;
        if (dx == -dy) return Direction.North;

        throw new ArgumentException("Target and current positions cannot be the same.");
    }

    private const int DeadZone = 0;
    private static bool WithinDeadzone(int z) => Math.Abs(z) <= DeadZone;

    public static TurretDirection ToTurretDirection(this (int x, int y) target, (int x, int y) current)
    {
        var dx = target.x - current.x;
        var dy = target.y - current.y;

        if (WithinDeadzone(dx) && dy < 0) return TurretDirection.South;
        if (dx > 0 && WithinDeadzone(dy)) return TurretDirection.West;
        if (WithinDeadzone(dx) && dy > 0) return TurretDirection.North;
        if (dx < 0 && WithinDeadzone(dy)) return TurretDirection.East;
        if (dx > 0 && dy < 0) return TurretDirection.SouthWest;
        if (dx > 0 && dy > 0) return TurretDirection.NorthWest;
        if (dx < 0 && dy > 0) return TurretDirection.NorthEast;
        if (dx < 0 && dy < 0) return TurretDirection.SouthEast;

        return TurretDirection.North; // Default direction if target and current positions are the same
    }
}

internal static class ITurnContextExtensions
{
    public static IEnumerable<(int x, int y)> ValidMoves(this ITurnContext context)
    {
        var moves = new List<(int x, int y)>();
        if (context.ValidTile(context.Tank.X, context.Tank.Y - 1)) moves.Add((context.Tank.X, context.Tank.Y - 1));
        if (context.ValidTile(context.Tank.X + 1, context.Tank.Y)) moves.Add((context.Tank.X + 1, context.Tank.Y));
        if (context.ValidTile(context.Tank.X, context.Tank.Y + 1)) moves.Add((context.Tank.X, context.Tank.Y + 1));
        if (context.ValidTile(context.Tank.X - 1, context.Tank.Y)) moves.Add((context.Tank.X - 1, context.Tank.Y));
        return moves;
    }

    public static bool ValidTile(this ITurnContext context, int x, int y)
    {
        try
        {

            return context.GetTile(x, y).Valid()
                && context.GetMapHeight() > y && y >= 0
                && context.GetMapWidth() > x && x >= 0;
        }
        catch
        {
            return false;
        }
    }
}

internal static class ITileExtensions
{
    public static bool Valid(this ITile tile) => tile.TileType != TileType.Water;
}

internal class Locator
{
    private const int BulletDiagonalSpeed = 3;
    private const int BulletStraightSpeed = 6;

    public static bool WillCollide((int x, int y) position, IBullet bullet, ITurnContext context, int turn)
    {
        var (dx, dy) = GetVector(bullet.Direction);
        int speed = IsDiagonal(dx, dy) ? BulletDiagonalSpeed : BulletStraightSpeed;

        int startStep = turn * speed;
        int endStep = (turn + 1) * speed;

        int bx = bullet.X;
        int by = bullet.Y;

        for (int i = 0; i < endStep; i++)
        {
            bx += dx;
            by += dy;

            if (bx < 0 || by < 0 || bx >= context.GetMapWidth() || by >= context.GetMapHeight())
                return false;

            var tile = context.GetTile(bx, by).TileType;
            if (tile == TileType.Tree || tile == TileType.Building)
                return false;

            if (i >= startStep && (bx, by) == position)
                return true;
        }

        return false;
    }

    private static (int dx, int dy) GetVector(TurretDirection dir) => dir switch
    {
        TurretDirection.North => (0, 1),
        TurretDirection.NorthEast => (-1, 1),
        TurretDirection.East => (-1, 0),
        TurretDirection.SouthEast => (-1, -1),
        TurretDirection.South => (0, -1),
        TurretDirection.SouthWest => (1, -1),
        TurretDirection.West => (1, 0),
        TurretDirection.NorthWest => (1, 1),
        _ => (0, 0)
    };

    private static bool IsDiagonal(int dx, int dy) => dx != 0 && dy != 0;
}
