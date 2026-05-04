using TankDestroyer.API;
using System.Collections.Generic;

namespace ThePlague.Bot;

[Bot("The Plague Bot", "Kris", "16C60C")]
public class ThePlagueBot : IPlayerBot
{
    public void DoTurn(ITurnContext turnContext)
    {
        var myTank = turnContext.Tank;
        var tanks = turnContext.GetTanks();
        var bullets = turnContext.GetBullets();
        
        ITank closestEnemy = GetClosestEnemy(myTank, tanks);
        
        // Always dodge incoming bullets first
        Direction? dodgeDirection = GetDodgeDirection(turnContext, myTank, bullets);
        if (dodgeDirection.HasValue)
        {
            MoveTankToDirection(turnContext, dodgeDirection);   
            MoveTurretTowardsClosestEnemy(turnContext, myTank, closestEnemy);
            turnContext.Fire();
            return;
        }
        
        if (myTank.Health <= 25)
        {
            Direction? coverDirection = GetDirectionTowardsCover(turnContext, myTank);
            if (coverDirection.HasValue)
            {
                MoveTankToDirection(turnContext, coverDirection);
            }

            MoveTurretTowardsClosestEnemy(turnContext, myTank, closestEnemy);

            turnContext.Fire();
            return;
        }
        
        if (closestEnemy != null)
        {
            MoveTankToDirection(turnContext, GetValidDirectionTowards(turnContext, myTank, closestEnemy));
            MoveTurretTowardsClosestEnemy(turnContext, myTank, closestEnemy);
        }
        
        turnContext.Fire();
    }

    private void MoveTankToDirection(ITurnContext turnContext, Direction? direction)
    {
        if (!direction.HasValue)
            return;

        turnContext.MoveTank(direction.Value);
    }
    
    private void MoveTurretTowardsClosestEnemy(ITurnContext turnContext, ITank myTank, ITank closestEnemy)
    {
        if (closestEnemy == null)
            return;

        TurretDirection turretDirection = GetTurretDirectionTowards(myTank, closestEnemy);
        turnContext.RotateTurret(turretDirection);
    }

    private Direction? GetDodgeDirection(ITurnContext turnContext, ITank myTank, IBullet[] bullets)
    {
        foreach (var bullet in bullets)
        {
            var nextPos = GetNextBulletPosition(bullet);
            if (nextPos.X == myTank.X && nextPos.Y == myTank.Y)
            {
                // Bullet will hit next turn, dodge
                return GetSafeMoveDirection(turnContext, myTank, bullet.Direction);
            }
        }
        return null;
    }
    
    private ITank GetClosestEnemy(ITank myTank, ITank[] tanks)
    {
        ITank closestEnemy = null;
        int minDistance = int.MaxValue;
        
        foreach (var tank in tanks)
        {
            if (tank.OwnerId != myTank.OwnerId && !tank.Destroyed)
            {
                int distance = Math.Abs(tank.X - myTank.X) + Math.Abs(tank.Y - myTank.Y);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    closestEnemy = tank;
                }
            }
        }
        
        return closestEnemy;
    }
    
    private (int X, int Y) GetNextBulletPosition(IBullet bullet)
    {
        int x = bullet.X;
        int y = bullet.Y;
        var dir = bullet.Direction;
        
        if ((dir & TurretDirection.North) != 0) y++;
        if ((dir & TurretDirection.South) != 0) y--;
        if ((dir & TurretDirection.East) != 0) x--;
        if ((dir & TurretDirection.West) != 0) x++;
        
        return (x, y);
    }
    
    private Direction? GetSafeMoveDirection(ITurnContext turnContext, ITank myTank, TurretDirection bulletDir)
    {
        List<Direction> possibleDirs = new List<Direction>();
        
        if ((bulletDir & (TurretDirection.North | TurretDirection.South)) != 0)
        {
            possibleDirs.Add(Direction.East);
            possibleDirs.Add(Direction.West);
        }
        else
        {
            possibleDirs.Add(Direction.North);
            possibleDirs.Add(Direction.South);
        }
        
        foreach (Direction dir in Enum.GetValues(typeof(Direction)))
        {
            if (!possibleDirs.Contains(dir))
                possibleDirs.Add(dir);
        }
        
        foreach (var dir in possibleDirs)
        {
            var newPos = GetNewPosition(myTank.X, myTank.Y, dir);
            if (IsValidMove(turnContext, newPos.X, newPos.Y))
            {
                return dir;
            }
        }
        
        return null;
    }
    
    private (int X, int Y) GetNewPosition(int x, int y, Direction dir)
    {
        switch (dir)
        {
            case Direction.North: return (x, y + 1);
            case Direction.South: return (x, y - 1);
            case Direction.East: return (x - 1, y);
            case Direction.West: return (x + 1, y);
            default: return (x, y);
        }
    }
    
    private bool IsValidMove(ITurnContext turnContext, int x, int y)
    {
        if (x < 0 || y < 0 || x >= turnContext.GetMapWidth() || y >= turnContext.GetMapHeight())
            return false;
        
        var tile = turnContext.GetTile(y, x);
        return tile.TileType == TileType.Grass || tile.TileType == TileType.Sand;
    }
    
    private Direction? GetValidDirectionTowards(ITurnContext turnContext, ITank from, ITank to)
    {
        int deltaX = to.X - from.X;
        int deltaY = to.Y - from.Y;
        
        // Prefer movement in the primary direction
        List<Direction> preferredDirs = new List<Direction>();
        
        if (Math.Abs(deltaY) > Math.Abs(deltaX))
        {
            preferredDirs.Add(deltaY > 0 ? Direction.North : Direction.South);
            preferredDirs.Add(deltaX > 0 ? Direction.West : Direction.East);
        }
        else
        {
            preferredDirs.Add(deltaX > 0 ? Direction.West : Direction.East);
            preferredDirs.Add(deltaY > 0 ? Direction.North : Direction.South);
        }
        
        foreach (Direction dir in Enum.GetValues(typeof(Direction)))
        {
            if (!preferredDirs.Contains(dir))
                preferredDirs.Add(dir);
        }
        
        // Find first valid direction (avoiding water)
        foreach (var dir in preferredDirs)
        {
            var newPos = GetNewPosition(from.X, from.Y, dir);
            if (IsValidMove(turnContext, newPos.X, newPos.Y))
            {
                return dir;
            }
        }
        
        return null;
    }
    
    private Direction? GetDirectionTowardsCover(ITurnContext turnContext, ITank myTank)
    {
        int mapWidth = turnContext.GetMapWidth();
        int mapHeight = turnContext.GetMapHeight();
        
        // Find nearby trees and move towards them
        (int X, int Y)? nearestTree = null;
        int minDistance = int.MaxValue;
        
        // Search in a limited radius for performance
        int searchRadius = 10;
        for (int y = Math.Max(0, myTank.Y - searchRadius); y <= Math.Min(mapHeight - 1, myTank.Y + searchRadius); y++)
        {
            for (int x = Math.Max(0, myTank.X - searchRadius); x <= Math.Min(mapWidth - 1, myTank.X + searchRadius); x++)
            {
                var tile = turnContext.GetTile(y, x);
                if (tile.TileType == TileType.Tree)
                {
                    int distance = Math.Abs(x - myTank.X) + Math.Abs(y - myTank.Y);
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        nearestTree = (x, y);
                    }
                }
            }
        }
        
        // If we found a tree, move towards it while avoiding water
        if (nearestTree.HasValue)
        {
            int deltaX = nearestTree.Value.X - myTank.X;
            int deltaY = nearestTree.Value.Y - myTank.Y;
            
            List<Direction> preferredDirs = new List<Direction>();
            
            if (Math.Abs(deltaY) > Math.Abs(deltaX))
            {
                preferredDirs.Add(deltaY > 0 ? Direction.North : Direction.South);
                preferredDirs.Add(deltaX > 0 ? Direction.West : Direction.East);
            }
            else
            {
                preferredDirs.Add(deltaX > 0 ? Direction.West : Direction.East);
                preferredDirs.Add(deltaY > 0 ? Direction.North : Direction.South);
            }
            
            foreach (Direction dir in Enum.GetValues(typeof(Direction)))
            {
                if (!preferredDirs.Contains(dir))
                    preferredDirs.Add(dir);
            }
            
            foreach (var dir in preferredDirs)
            {
                var newPos = GetNewPosition(myTank.X, myTank.Y, dir);
                if (IsValidMove(turnContext, newPos.X, newPos.Y))
                {
                    return dir;
                }
            }
        }
        
        return null;
    }
    
    private Direction GetDirectionTowards(ITank from, ITank to)
    {
        int deltaX = to.X - from.X;
        int deltaY = to.Y - from.Y;
        
        if (Math.Abs(deltaY) > Math.Abs(deltaX))
        {
            return deltaY > 0 ? Direction.North : Direction.South;
        }
        else
        {
            return deltaX > 0 ? Direction.West : Direction.East;
        }
    }
    
    private TurretDirection GetTurretDirectionTowards(ITank from, ITank to)
    {
        int deltaX = to.X - from.X;
        int deltaY = to.Y - from.Y;
        
        TurretDirection vertical = deltaY > 0 ? TurretDirection.North : TurretDirection.South;
        TurretDirection horizontal = deltaX > 0 ? TurretDirection.West : TurretDirection.East;
 
        if (Math.Abs(deltaX) > 2 && Math.Abs(deltaY) > 2)
        {
            return vertical | horizontal;
        }
        
        return Math.Abs(deltaY) > Math.Abs(deltaX) ? vertical : horizontal;
    }
}