using TankDestroyer.API;

namespace Kotiba.Bot;

[Bot("Kotiba bot", "By Kotiba", "0ad859")]
public class KotibaBot : IPlayerBot
{
   
public void DoTurn(ITurnContext turnContext)
{
    turnContext.Fire();
    var myTank = turnContext.Tank;
    var allTanks = turnContext.GetTanks();
    var enemies = allTanks.Where(t => t.OwnerId != myTank.OwnerId && !t.Destroyed).ToArray();
    if (enemies.Length == 0) return;

    var bullets = turnContext.GetBullets();
    var currentTile = turnContext.GetTile(myTank.Y, myTank.X);
    var target = SelectBestTarget(turnContext, myTank, enemies);

    if (IsUnderImmediateThreat(turnContext, myTank, bullets))
    {
        if (TryRetreatFromBullets(turnContext, myTank, bullets, out var fleeDirection))
        {
            turnContext.MoveTank(fleeDirection);
            return;
        }
    }

    if (currentTile != null && currentTile.TileType == TileType.Tree)
    {
        var directions = new[] { Direction.North, Direction.East, Direction.South, Direction.West };
        foreach (var dir in directions)
        {
            if (!CanMove(turnContext, dir)) continue;

            int newX = GetNewX(myTank.X, dir);
            int newY = GetNewY(myTank.Y, dir);
            var tile = turnContext.GetTile(newY, newX);

            if (tile != null && tile.TileType == TileType.Grass)
            {
                turnContext.MoveTank(dir);
                return;
            }
        }

        foreach (var dir in directions)
        {
            if (CanMove(turnContext, dir))
            {
                turnContext.MoveTank(dir);
                return;
            }
        }

        return;
    }

    if (target != null)
    {
        var bestDir = GetTargetTurretDirection(myTank, target);
        if (myTank.TurretDirection != bestDir)
        {
            turnContext.RotateTurret(bestDir);
        }
 
    }

    Direction? moveChoice = FindBestMoveDirection(turnContext, myTank, enemies, bullets, target);
    if (moveChoice.HasValue)
    {
        turnContext.MoveTank(moveChoice.Value);
    }
}

private ITank? SelectBestTarget(ITurnContext context, ITank myTank, ITank[] enemies)
{
    ITank? bestTarget = null;
    int bestScore = int.MinValue;

    foreach (var enemy in enemies)
    {
        int distance = Math.Abs(enemy.X - myTank.X) + Math.Abs(enemy.Y - myTank.Y);
        int score = -distance * 10;

        if (CanShootEnemy(context, myTank, enemy))
            score += 750;

        if (IsPathClear(context, myTank, enemy, GetTargetTurretDirection(myTank, enemy)))
            score += 200;

        if (!HasClearLineOfFireFrom(context, enemy.X, enemy.Y, myTank.X, myTank.Y))
            score += 50;

        if (score > bestScore)
        {
            bestScore = score;
            bestTarget = enemy;
        }
    }

    return bestTarget;
}

private TurretDirection GetTargetTurretDirection(ITank myTank, ITank enemy)
{
    int deltaX = enemy.X - myTank.X;
    int deltaY = enemy.Y - myTank.Y;

    if (Math.Abs(deltaY) > Math.Abs(deltaX))
        return deltaY > 0 ? TurretDirection.North : TurretDirection.South;

    return deltaX > 0 ? TurretDirection.West : TurretDirection.East;
}

private bool CanShootEnemy(ITurnContext context, ITank myTank, ITank enemy)
{
    int dx = enemy.X - myTank.X;
    int dy = enemy.Y - myTank.Y;

    if (!IsAligned(dx, dy, out var turretDir))
        return false;

    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) > 6)
        return false;

    return IsPathClearFromPosition(context, myTank.X, myTank.Y, enemy, turretDir);
}

private bool IsPathClearFromPosition(ITurnContext context, int x, int y, ITank enemy, TurretDirection dir)
{
    int distance;
    if (dir == TurretDirection.North || dir == TurretDirection.South)
    {
        distance = Math.Abs(enemy.Y - y);
    }
    else if (dir == TurretDirection.East || dir == TurretDirection.West)
    {
        distance = Math.Abs(enemy.X - x);
    }
    else
    {
        return false;
    }

    if (distance > 6 || distance == 0)
        return false;

    int stepX = Math.Sign(enemy.X - x);
    int stepY = Math.Sign(enemy.Y - y);

    for (int i = 1; i < distance; i++)
    {
        var checkX = x + stepX * i;
        var checkY = y + stepY * i;
        var tile = context.GetTile(checkY, checkX);
        if (tile == null || tile.TileType == TileType.Tree || tile.TileType == TileType.Building)
            return false;
    }

    return true;
}

private Direction? FindBestMoveDirection(ITurnContext context, ITank myTank, ITank[] enemies, IBullet[] bullets, ITank? target)
{
    Direction? bestDir = null;
    int bestScore = int.MinValue;
    var directions = new[] { Direction.North, Direction.East, Direction.South, Direction.West };

    foreach (var dir in directions)
    {
        if (!CanMove(context, dir))
            continue;

        int newX = GetNewX(myTank.X, dir);
        int newY = GetNewY(myTank.Y, dir);

        int distance = target != null
            ? Math.Abs(newX - target.X) + Math.Abs(newY - target.Y)
            : enemies.Min(e => Math.Abs(newX - e.X) + Math.Abs(newY - e.Y));

        bool safe = IsPositionSafeFromBullets(context, newX, newY, bullets);
        bool cover = IsAdjacentToBlockingTile(context, newX, newY);
        bool canShootFromHere = target != null && CanShootEnemyFrom(context, newX, newY, target);

        int score = -distance * 15;
        if (safe) score += 250;
        if (cover) score += 120;
        if (canShootFromHere) score += 600;
        if (!safe) score -= 200;

        if (score > bestScore)
        {
            bestScore = score;
            bestDir = dir;
        }
    }

    return bestDir;
}

private bool CanShootEnemyFrom(ITurnContext context, int x, int y, ITank enemy)
{
    int dx = enemy.X - x;
    int dy = enemy.Y - y;

    if (!IsAligned(dx, dy, out var turretDir))
        return false;

    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) > 6)
        return false;

    return IsPathClearFromPosition(context, x, y, enemy, turretDir);
}

private bool HasClearLineOfFireFrom(ITurnContext context, int sx, int sy, int tx, int ty)
{
    int dx = tx - sx;
    int dy = ty - sy;
    if (!IsAligned(dx, dy, out _))
        return false;

    int stepX = Math.Sign(dx);
    int stepY = Math.Sign(dy);
    int steps = Math.Max(Math.Abs(dx), Math.Abs(dy));
    if (steps > 6)
        return false;

    for (int i = 1; i < steps; i++)
    {
        int x = sx + stepX * i;
        int y = sy + stepY * i;
        var tile = context.GetTile(y, x);
        if (tile == null || tile.TileType == TileType.Tree || tile.TileType == TileType.Building)
            return false;
    }

    return true;
}

private bool IsAdjacentToBlockingTile(ITurnContext context, int x, int y)
{
    var neighbors = new[] { (x, y + 1), (x, y - 1), (x + 1, y), (x - 1, y) };

    foreach (var (nx, ny) in neighbors)
    {
        if (nx < 0 || ny < 0 || nx >= context.GetMapWidth() || ny >= context.GetMapHeight())
            continue;

        var tile = context.GetTile(ny, nx);
        if (tile != null && (tile.TileType == TileType.Tree || tile.TileType == TileType.Building))
            return true;
    }

    return false;
}
    private bool CanMove(ITurnContext context, Direction direction)
    {
        int newX = GetNewX(context.Tank.X, direction);
        int newY = GetNewY(context.Tank.Y, direction);
        if (newX < 0 || newX >= context.GetMapWidth() || newY < 0 || newY >= context.GetMapHeight()) return false;
        var tile = context.GetTile(newY, newX);
        return tile.TileType != TileType.Water;
    }

    private bool IsUnderImmediateThreat(ITurnContext context, ITank myTank, IBullet[] bullets)
    {
        foreach (var bullet in bullets)
        {
            if (IsBulletThreateningPosition(context, bullet, myTank.X, myTank.Y))
                return true;
        }
        return false;
    }

    private bool TryRetreatFromBullets(ITurnContext context, ITank myTank, IBullet[] bullets, out Direction direction)
    {
        direction = Direction.North;
        var directions = new[] { Direction.North, Direction.East, Direction.South, Direction.West };
        int bestScore = int.MinValue;
        bool found = false;

        foreach (var dir in directions)
        {
            if (!CanMove(context, dir))
                continue;

            int newX = GetNewX(myTank.X, dir);
            int newY = GetNewY(myTank.Y, dir);

            if (!IsPositionSafeFromBullets(context, newX, newY, bullets))
                continue;

            int minThreatDist = int.MaxValue;
            foreach (var bullet in bullets)
            {
                if (!IsBulletThreateningPosition(context, bullet, newX, newY))
                    continue;

                int dist = Math.Max(Math.Abs(newX - bullet.X), Math.Abs(newY - bullet.Y));
                if (dist < minThreatDist)
                    minThreatDist = dist;
            }

            int score = minThreatDist == int.MaxValue ? 1000 : minThreatDist * 10;
            score += Math.Abs(newX - myTank.X) + Math.Abs(newY - myTank.Y);

            if (!found || score > bestScore)
            {
                bestScore = score;
                direction = dir;
                found = true;
            }
        }

        return found;
    }

    private bool IsPositionSafeFromBullets(ITurnContext context, int x, int y, IBullet[] bullets)
    {
        foreach (var bullet in bullets)
        {
            if (IsBulletThreateningPosition(context, bullet, x, y))
                return false;
        }
        return true;
    }

    private bool IsBulletThreateningPosition(ITurnContext context, IBullet bullet, int x, int y)
    {
        int dx = x - bullet.X;
        int dy = y - bullet.Y;

        if (!IsAligned(dx, dy, out var direction))
            return false;

        if (!BulletDirectionMatches(bullet.Direction, direction))
            return false;

        int steps = Math.Max(Math.Abs(dx), Math.Abs(dy));
        if (steps == 0 || steps > 6)
            return false;

        int stepX = Math.Sign(dx);
        int stepY = Math.Sign(dy);
        for (int i = 1; i < steps; i++)
        {
            int checkX = bullet.X + stepX * i;
            int checkY = bullet.Y + stepY * i;

            var tile = context.GetTile(checkY, checkX);
            if (tile == null || tile.TileType == TileType.Tree || tile.TileType == TileType.Building)
                return false;
        }

        return true;
    }

    private static bool BulletDirectionMatches(TurretDirection bulletDirection, TurretDirection targetDirection)
    {
        return targetDirection switch
        {
            TurretDirection.North => bulletDirection.HasFlag(TurretDirection.North),
            TurretDirection.South => bulletDirection.HasFlag(TurretDirection.South),
            TurretDirection.West => bulletDirection.HasFlag(TurretDirection.West),
            TurretDirection.East => bulletDirection.HasFlag(TurretDirection.East),
            TurretDirection.NorthWest => bulletDirection.HasFlag(TurretDirection.North) && bulletDirection.HasFlag(TurretDirection.West),
            TurretDirection.NorthEast => bulletDirection.HasFlag(TurretDirection.North) && bulletDirection.HasFlag(TurretDirection.East),
            TurretDirection.SouthWest => bulletDirection.HasFlag(TurretDirection.South) && bulletDirection.HasFlag(TurretDirection.West),
            TurretDirection.SouthEast => bulletDirection.HasFlag(TurretDirection.South) && bulletDirection.HasFlag(TurretDirection.East),
            _ => false,
        };
    }

    private static bool IsAligned(int dx, int dy, out TurretDirection direction)
    {
        direction = TurretDirection.North;
        if (dx == 0)
        {
            direction = dy > 0 ? TurretDirection.North : TurretDirection.South;
            return true;
        }

        if (dy == 0)
        {
            direction = dx > 0 ? TurretDirection.West : TurretDirection.East;
            return true;
        }

        if (Math.Abs(dx) == Math.Abs(dy))
        {
            if (dx > 0 && dy > 0) direction = TurretDirection.NorthWest;
            else if (dx > 0 && dy < 0) direction = TurretDirection.SouthWest;
            else if (dx < 0 && dy > 0) direction = TurretDirection.NorthEast;
            else direction = TurretDirection.SouthEast;
            return true;
        }

        return false;
    }

    private int GetNewX(int x, Direction direction)
    {
        return direction switch
        {
            Direction.East => x - 1,
            Direction.West => x + 1,
            _ => x
        };
    }

    private int GetNewY(int y, Direction direction)
    {
        return direction switch
        {
            Direction.North => y + 1,
            Direction.South => y - 1,
            _ => y
        };
    }

    private Direction GetMoveDirection(int deltaX, int deltaY)
    {
        if (Math.Abs(deltaY) > Math.Abs(deltaX))
        {
            return deltaY > 0 ? Direction.North : Direction.South;
        }
        else
        {
            return deltaX > 0 ? Direction.West : Direction.East;
        }
    }

    private bool IsPathClear(ITurnContext context, ITank myTank, ITank enemy, TurretDirection dir)
    {
        int distance;
        if (dir == TurretDirection.North || dir == TurretDirection.South)
        {
            distance = Math.Abs(enemy.Y - myTank.Y);
        }
        else if (dir == TurretDirection.East || dir == TurretDirection.West)
        {
            distance = Math.Abs(enemy.X - myTank.X);
        }
        else
        {
            return false;
        }
        if (distance > 6 || distance == 0) return false;

        if (dir == TurretDirection.North)
        {
            if (enemy.X != myTank.X || enemy.Y <= myTank.Y) return false;
            for (int y = myTank.Y + 1; y < enemy.Y; y++)
            {
                if (y >= context.GetMapHeight()) return false;
                var tile = context.GetTile(y, myTank.X);
                if (tile == null || tile.TileType == TileType.Tree || tile.TileType == TileType.Building) return false;
            }
            return true;
        }
        else if (dir == TurretDirection.South)
        {
            if (enemy.X != myTank.X || enemy.Y >= myTank.Y) return false;
            for (int y = myTank.Y - 1; y > enemy.Y; y--)
            {
                if (y < 0) return false;
                var tile = context.GetTile(y, myTank.X);
                if (tile == null || tile.TileType == TileType.Tree || tile.TileType == TileType.Building) return false;
            }
            return true;
        }
        else if (dir == TurretDirection.East)
        {
            if (enemy.Y != myTank.Y || enemy.X >= myTank.X) return false;
            for (int x = myTank.X - 1; x > enemy.X; x--)
            {
                if (x < 0) return false;
                var tile = context.GetTile(myTank.Y, x);
                if (tile == null || tile.TileType == TileType.Tree || tile.TileType == TileType.Building) return false;
            }
            return true;
        }
        else if (dir == TurretDirection.West)
        {
            if (enemy.Y != myTank.Y || enemy.X <= myTank.X) return false;
            for (int x = myTank.X + 1; x < enemy.X; x++)
            {
                if (x >= context.GetMapWidth()) return false;
                var tile = context.GetTile(myTank.Y, x);
                if (tile == null || tile.TileType == TileType.Tree || tile.TileType == TileType.Building) return false;
            }
            return true;
        }
        return false;
    }
}