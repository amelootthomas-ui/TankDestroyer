using TankDestroyer.API;

namespace LeeroyJenkins.Bot;

[Bot("Leeroy Jenkins 2.0", "Kevin & Deon", "ff4d00")]
public class LeeroyJenkinsBot : IPlayerBot
{
    private static readonly Random _random = new();
    private static int turnCount = 0;

    public void DoTurn(ITurnContext turnContext)
    {
        var tanks = turnContext.GetTanks();
        var ourTank = turnContext.Tank;

        var target = GetClosestTank(tanks, ourTank);

        Direction? movementDirection;

        if (_random.Next(100 + turnCount) < (5 + turnCount))
        {
            movementDirection = GetRandomLegalMove(ourTank, turnContext);
        }
        else
        {
            movementDirection = GetMoveDirection(ourTank, target, turnContext);
        }

        var turretRotation = GetTurretRotation(ourTank, movementDirection, target);

        //var enumValues = Enum.GetValues<TurretDirection>();
        //var enumDirectionValues = Enum.GetValues<Direction>();

        if (movementDirection is not null)
        {
            turnContext.MoveTank(movementDirection.Value); //enumDirectionValues[_random.Next(0, enumDirectionValues.Length)]
        }
        turnContext.RotateTurret(turretRotation.Value);

        turnContext.Fire();
        turnCount++;
        if (turnCount > 50)
        {
            turnCount = 0;
        }
    }

    private TurretDirection? GetTurretRotation(ITank ourTank, Direction? movementDirection, ITank target)
    {
        if (target == null) return null;

        var xDiff = target.X - ourTank.X;
        var yDiff = target.Y - ourTank.Y;

        var absDiffX = Math.Abs(xDiff);
        var absDiffY = Math.Abs(yDiff);

        if (absDiffX > absDiffY * 2)
        {
            return xDiff < 0 ? TurretDirection.East : TurretDirection.West;
        }
        else if (absDiffY > absDiffX * 2)
        {
            return yDiff > 0 ? TurretDirection.North : TurretDirection.South;
        }

        TurretDirection direction = 0;

        if (xDiff < 0)
            direction |= TurretDirection.East;
        else if (xDiff > 0)
            direction |= TurretDirection.West;

        if (yDiff > 0)
            direction |= TurretDirection.North;
        else if (yDiff < 0)
            direction |= TurretDirection.South;

        return direction == 0 ? null : direction;
    }

    public ITank? GetClosestTank(ITank[] tanks, ITank ourTank)
    {
        ITank? closestTank = null;
        var closestDistance = int.MaxValue;

        foreach (var tank in tanks)
        {
            if (tank.OwnerId == ourTank.OwnerId) continue;
            if (tank.Destroyed) continue;

            var dx = Math.Abs(tank.X - ourTank.X);
            var dy = Math.Abs(tank.Y - ourTank.Y);
            var distance = dx * dx + dy * dy;
            if (closestTank == null || distance < closestDistance)
            {
                closestTank = tank;
                closestDistance = distance;
            }
        }

        return closestTank;
    }

    private Direction? GetMoveDirection(ITank our, ITank target, ITurnContext turnContext)
    {
        var dx = target.X - our.X;
        var dy = target.Y - our.Y;

        if (dx == 0 && dy == 0) return null;

        Direction primaryDirection;
        Direction secondaryDirection;
        Direction thirdDirection;
        Direction fourthDirection;

        if (Math.Abs(dx) > Math.Abs(dy))
        {
            primaryDirection = dx > 0 ? Direction.West : Direction.East;
            secondaryDirection = dy > 0 ? Direction.North : Direction.South;
            thirdDirection = dx > 0 ? Direction.East : Direction.West;
            fourthDirection = dy > 0 ? Direction.South : Direction.North;
        }
        else
        {
            primaryDirection = dy > 0 ? Direction.North : Direction.South;
            secondaryDirection = dx > 0 ? Direction.West : Direction.East;
            thirdDirection = dy > 0 ? Direction.South : Direction.North;
            fourthDirection = dx > 0 ? Direction.East : Direction.West;
        }

        var directionsToTry = new[] { primaryDirection, secondaryDirection, thirdDirection, fourthDirection };

        foreach (var direction in directionsToTry)
        {
            if (IsLegalTile(our, direction, turnContext))
            {
                var bulletDir = DodgeBullets(turnContext);
                bulletDir.TryGetValue(direction, out var isUnsafe);
                if (isUnsafe) continue;
                return direction;
            }
        }

        return null;
    }

    private bool IsLegalTile(ITank ourTank, Direction direction, ITurnContext turnContext)
    {
        (int X, int Y) nextPos = GetNextPosition(ourTank, direction);

        if (nextPos.X < 0 || nextPos.X >= turnContext.GetMapWidth() ||
            nextPos.Y < 0 || nextPos.Y >= turnContext.GetMapHeight())
        {
            return false;
        }

        var tile = turnContext.GetTile(nextPos.Y, nextPos.X);
        return tile is not null && tile.TileType != TileType.Water;
    }

    private static (int X, int Y) GetNextPosition(ITank ourTank, Direction direction)
    {
        var nextPos = (ourTank.X, ourTank.Y);
        switch (direction)
        {
            case Direction.North:
                nextPos.Y += 1;
                break;
            case Direction.West:
                nextPos.X += 1;
                break;
            case Direction.South:
                nextPos.Y -= 1;
                break;
            case Direction.East:
                nextPos.X -= 1;
                break;
        }

        return nextPos;
    }

    //North = Y+1//NorthWest = X+1, Y+1//West = X+1//SouthWest = X+1, Y-1//South = Y-1//SouthEast = X-1, Y-1//East = X-1//NorthEast = X-1, Y+1
    public Dictionary<Direction, bool> DodgeBullets(ITurnContext turnContext)
    {

        var unSafeDirections = new Dictionary<Direction, bool> { };


    var unsafePositions = new List<(int x, int y)> { };


    var bullets = turnContext.GetBullets();

        var enemyTanks = turnContext.GetTanks();

        foreach (var tank in enemyTanks)

        {

            if (tank.OwnerId == turnContext.Tank.OwnerId) continue;

            if (tank.Destroyed) continue;


            unsafePositions.Add((tank.X, tank.Y));

            GetNorthTankDirectionNodes(tank, unsafePositions);

            GetEastTankDirectionNodes(tank, unsafePositions);

            GetSouthTankDirectionNodes(tank, unsafePositions);

            GetWestTankDirectionNodes(tank, unsafePositions);

        }

        foreach (var bullet in bullets)

        {

            unsafePositions.Add((bullet.X, bullet.Y));


            switch (bullet.Direction)

            {

                case TurretDirection.North:

                    GetNorthDirectionNodes(bullet, unsafePositions);

                    break;

                case TurretDirection.NorthEast:

                    unsafePositions.Add((bullet.X - 1, bullet.Y + 1));

                    unsafePositions.Add((bullet.X - 2, bullet.Y + 2));

                    unsafePositions.Add((bullet.X - 3, bullet.Y + 3));

                    break;

                case TurretDirection.East:

                    GetEastDirectionNodes(bullet, unsafePositions);

                    break;

                case TurretDirection.SouthEast:

                    unsafePositions.Add((bullet.X - 1, bullet.Y - 1));

                    unsafePositions.Add((bullet.X - 2, bullet.Y - 2));

                    unsafePositions.Add((bullet.X - 3, bullet.Y - 3));

                    break;

                case TurretDirection.South:

                    GetSouthDirectionNodes(bullet, unsafePositions);

                    break;

                case TurretDirection.SouthWest:

                    unsafePositions.Add((bullet.X + 1, bullet.Y - 1));

                    unsafePositions.Add((bullet.X + 2, bullet.Y - 2));

                    unsafePositions.Add((bullet.X + 3, bullet.Y - 3));

                    break;

                case TurretDirection.West:

                    GetWestDirectionNodes(bullet, unsafePositions);

                    break;

                case TurretDirection.NorthWest:

                    unsafePositions.Add((bullet.X + 1, bullet.Y + 1));

                    unsafePositions.Add((bullet.X + 2, bullet.Y + 2));

                    unsafePositions.Add((bullet.X + 3, bullet.Y + 3));

                    break;

            }

        }


        foreach (var node in unsafePositions)

{

    if (node.y == turnContext.Tank.Y + 1 && node.x == turnContext.Tank.X)

    {

        unSafeDirections[Direction.North] = true;

    }


    if (node.y == turnContext.Tank.Y - 1 && node.x == turnContext.Tank.X)

    {

        unSafeDirections[Direction.South] = true;

    }


    if (node.y == turnContext.Tank.Y && node.x == turnContext.Tank.X + 1)

    {

        unSafeDirections[Direction.West] = true;

    }


    if (node.y == turnContext.Tank.Y && node.x == turnContext.Tank.X - 1)

    {

        unSafeDirections[Direction.East] = true;

    }

}


return unSafeDirections;

    }




    public void GetNorthDirectionNodes(IBullet bullet, List<(int x, int y)> unsafePositions)

{

    for (int currentPos = bullet.Y; currentPos < bullet.Y + 7; currentPos++)

    {

        unsafePositions.Add((bullet.X, currentPos));

    }

}


public void GetSouthDirectionNodes(IBullet bullet, List<(int x, int y)> unsafePositions)

{

    for (int currentPos = bullet.Y; currentPos > bullet.Y - 7; currentPos--)

    {

        unsafePositions.Add((bullet.X, currentPos));

    }

}


public void GetWestDirectionNodes(IBullet bullet, List<(int x, int y)> unsafePositions)

{

    for (int currentPos = bullet.X; currentPos < bullet.X + 7; currentPos++)

    {

        unsafePositions.Add((currentPos, bullet.Y));

    }

}


public void GetEastDirectionNodes(IBullet bullet, List<(int x, int y)> unsafePositions)

{

    for (int currentPos = bullet.X; currentPos > bullet.X - 7; currentPos--)

    {

        unsafePositions.Add((currentPos, bullet.Y));

    }

}

    public void GetNorthTankDirectionNodes(ITank tank, List<(int x, int y)> unsafePositions)

    {

        for (int currentPos = tank.Y; currentPos < tank.Y + 7; currentPos++)

        {

            unsafePositions.Add((tank.X, currentPos));

        }

    }


    public void GetSouthTankDirectionNodes(ITank tank, List<(int x, int y)> unsafePositions)

    {

        for (int currentPos = tank.Y; currentPos > tank.Y - 7; currentPos--)

        {

            unsafePositions.Add((tank.X, currentPos));

        }

    }


    public void GetWestTankDirectionNodes(ITank tank, List<(int x, int y)> unsafePositions)

    {

        for (int currentPos = tank.X; currentPos < tank.X + 7; currentPos++)

        {

            unsafePositions.Add((currentPos, tank.Y));

        }

    }


    public void GetEastTankDirectionNodes(ITank tank, List<(int x, int y)> unsafePositions)

    {

        for (int currentPos = tank.X; currentPos > tank.X - 7; currentPos--)

        {

            unsafePositions.Add((currentPos, tank.Y));

        }

    }

    private Direction? GetRandomLegalMove(ITank ourTank, ITurnContext turnContext)
    {
        var allDirections = new[] { Direction.North, Direction.South, Direction.East, Direction.West };
        var shuffledDirections = allDirections.OrderBy(_ => _random.Next()).ToArray();

        foreach (var direction in shuffledDirections)
        {
            if (IsLegalTile(ourTank, direction, turnContext))
            {
                var bulletDir = DodgeBullets(turnContext);
                bulletDir.TryGetValue(direction, out var isUnsafe);
                if (!isUnsafe)
                {
                    return direction;
                }
            }
        }

        return null;
    }
}