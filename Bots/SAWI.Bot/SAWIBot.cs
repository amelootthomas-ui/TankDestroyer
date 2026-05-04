using System.Numerics;
using System.Text;
using TankDestroyer.API;
using static System.Net.WebRequestMethods;

namespace SAWI.Bot;

[Bot("SAWI Bot", "Probably dies within a few minutes", "74DFFF")]
public class ExampleBot : IPlayerBot
{
    private Random random = new Random();
    private ITile[,]? map;
    private ITile? foundTile;

    public void DoTurn(ITurnContext turnContext)
    {
        CreateMap(turnContext);

        if(map == null)
        {
            return;
        }

        List<ITile> tiles = GetTiles(turnContext, TileType.Building);

        if(foundTile == null)
        {
            foundTile = GetClosestTile(turnContext, tiles);
        }

        if (foundTile != null)
        {
            if (!IsOnTile(turnContext, foundTile))
            {
                MoveToTile(turnContext, foundTile);
            }
        }
        else
        {
            MoveRandom(turnContext);
        }

        ITank? target = GetClosestEnemy(turnContext);

        if(target == null)
        {
            return;
        }

        TurretDirection aimDirection = Aim(turnContext, target);
        turnContext.RotateTurret(aimDirection);
        turnContext.Fire();
    }

    private ITile[,] CreateMap(ITurnContext context)
    {
        map = new ITile[context.GetMapWidth(),context.GetMapHeight()];
        for (int x = 0; x < context.GetMapWidth(); x++)
        {
            for (int y = 0; y < context.GetMapHeight(); y++)
            {
                map[x, y] = context.GetTile(x, y);
            }
        }
        return map;
    }

    private List<ITile> GetTiles(ITurnContext context, TileType tileType)
    {
        List<ITile> tiles = new();
        if(map == null)
        {
            return tiles;
        }

        for (int y = 0; y < context.GetMapHeight(); y++)
        {
            for (int x = 0; x < context.GetMapWidth(); x++)
            {
                if (map[x, y].TileType == tileType)
                    tiles.Add(map[x, y]);
            }
        }
        return tiles;
    }

    private TurretDirection Aim(ITurnContext context, ITank enemy)
    {
        int xDistance = enemy.X - context.Tank.X;
        int yDistance = enemy.Y - context.Tank.Y;

        bool matching = Math.Abs(xDistance) == Math.Abs(yDistance);

        if(matching && xDistance < 0 && yDistance < 0)
            return Adjusted(TurretDirection.NorthWest);

        if (matching && xDistance < 0 && yDistance > 0)
            return Adjusted(TurretDirection.SouthWest);

        if (matching && xDistance > 0 && yDistance < 0)
            return Adjusted(TurretDirection.NorthEast);

        if (matching && xDistance > 0 && yDistance > 0)
            return Adjusted(TurretDirection.SouthEast);

        if(!matching && xDistance == 0)
        {
            if (yDistance < 0)
                return Adjusted(TurretDirection.North);
            if (yDistance > 0)
                return Adjusted(TurretDirection.South);
        }

        if (!matching && yDistance == 0)
        {
            if (xDistance < 0)
                return Adjusted(TurretDirection.West);
            if (xDistance > 0)
                return Adjusted(TurretDirection.East);
        }

        if (xDistance > yDistance)
        {
            if (xDistance < 0)
            {
                if (yDistance > 1)
                {
                    return Adjusted(TurretDirection.SouthWest);
                }
                else if (yDistance < -1)
                {
                    return Adjusted(TurretDirection.NorthWest);
                }
                else
                {
                    return Adjusted(TurretDirection.West);
                }
            }
                
            if (xDistance > 0)
            {
                if (yDistance > 1)
                {
                    return Adjusted(TurretDirection.SouthEast);
                }
                else if (yDistance < -1)
                {
                    return Adjusted(TurretDirection.NorthEast);
                }
                else
                {
                    return Adjusted(TurretDirection.East);
                }
            }
                
        }
        else
        {
            if (yDistance < 0)
            {
                if(xDistance > 1)
                {
                    return Adjusted(TurretDirection.NorthEast);
                } 
                else if(xDistance < -1)
                {
                    return Adjusted(TurretDirection.NorthWest);
                } 
                else
                {
                    return Adjusted(TurretDirection.North);
                }
            }
                
            if (yDistance > 0)
            {
                if (xDistance > 1)
                {
                    return Adjusted(TurretDirection.SouthEast);
                }
                else if (xDistance < -1)
                {
                    return Adjusted(TurretDirection.SouthWest);
                }
                else
                {
                    return Adjusted(TurretDirection.South);
                }
            }     
        }

        if(xDistance == yDistance)
        {
            Console.WriteLine("2 Tanks, 1 Tile");
            return Adjusted(TurretDirection.South);
        }
        return Adjusted(TurretDirection.South);
    }

    private string BuildArrayString(ITurnContext context)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("Map state");
        for (int y = 0; y < context.GetMapHeight(); y++)
        {
            for (int x = 0; x < context.GetMapWidth(); x++)
            {
                char tileType = map?[x, y].TileType switch
                    {
                    TileType.Tree => 'T',
                    TileType.Grass => 'G',
                    TileType.Sand => 'S',
                    TileType.Building => 'B',
                    TileType.Water => 'W',
                    _ => '?'
                };
                sb.Append(tileType);
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private ITile? GetClosestTile(ITurnContext context, List<ITile> tiles)
    {
        int closestDistance = int.MaxValue;
        ITile? closestTile = null;

        foreach (ITile tile in tiles)
        {
            if (closestTile == null)
                closestTile = tile;

            int travelDistance = Math.Abs(tile.X - context.Tank.X) + Math.Abs(tile.Y - context.Tank.Y);

            if(travelDistance < closestDistance)
            {
                closestDistance = travelDistance;
                closestTile = tile;
            }
        }
        return closestTile;
    }

    private ITank? GetClosestEnemy(ITurnContext context)
    {
        float closestDistance = float.MaxValue;
        ITank? closestEnemy = null;

        context.GetTanks();
        foreach (ITank tank in context.GetTanks())
        {
            if (tank.OwnerId == context.Tank.OwnerId)
                continue;

            if (tank.Destroyed)
                continue;

            if(closestEnemy == null)
            {
                closestEnemy = tank;
            }

            float distanceToTank = Vector2.Distance(
                new Vector2(context.Tank.X, context.Tank.Y),
                new Vector2(tank.X, tank.Y)
                );

            if (distanceToTank < closestDistance)
            {
                closestDistance = distanceToTank;
                closestEnemy = tank;
            }
        }
        return closestEnemy;
    }

    private bool IsOnTile(ITurnContext context, ITile tile)
        => context.Tank.X == tile.X && context.Tank.Y == tile.Y;

    private void MoveToTile(ITurnContext context, ITile tile)
    {
        ITile? leftTile = context.Tank.X != 0 ? map?[context.Tank.X - 1, context.Tank.Y] : null;
        ITile? topTile = context.Tank.Y != 0 ? map?[context.Tank.X, context.Tank.Y - 1] : null;
        ITile? rightTile = context.Tank.X + 1 != context.GetMapWidth() ? map?[context.Tank.X + 1, context.Tank.Y] : null;
        ITile? bottomTile = context.Tank.Y + 1 != context.GetMapHeight() ? map?[context.Tank.X, context.Tank.Y + 1] : null;

        if (leftTile != null && leftTile.TileType != TileType.Water && tile.X < context.Tank.X)
        {
            context.MoveTank(Adjusted(Direction.West));
            return;
        }

        if (topTile != null && topTile.TileType != TileType.Water && tile.Y < context.Tank.Y)
        {
            context.MoveTank(Adjusted(Direction.North));
            return;
        }

        if (rightTile != null && rightTile.TileType != TileType.Water && tile.X > context.Tank.X)
        {
            context.MoveTank(Adjusted(Direction.East));
            return;
        }

        if (bottomTile != null && bottomTile.TileType != TileType.Water && tile.Y > context.Tank.Y)
        {
            context.MoveTank(Adjusted(Direction.South));
            return;
        }

        MoveRandom(context);
    }

    private Direction Adjusted(Direction direction)
        => direction switch
        {
            Direction.West => Direction.East,
            Direction.East => Direction.West,
            Direction.North => Direction.South,
            Direction.South => Direction.North,
            _ => Direction.South
        };

    private TurretDirection Adjusted(TurretDirection direction)
        => direction switch
        {
            TurretDirection.West => TurretDirection.East,
            TurretDirection.East => TurretDirection.West,
            TurretDirection.North => TurretDirection.South,
            TurretDirection.South => TurretDirection.North,
            TurretDirection.SouthWest => TurretDirection.NorthEast,
            TurretDirection.SouthEast => TurretDirection.NorthWest,
            TurretDirection.NorthWest => TurretDirection.SouthEast,
            TurretDirection.NorthEast => TurretDirection.SouthWest,
            _ => TurretDirection.South
        };

    private void MoveRandom(ITurnContext context)
    {
        Direction[] directions = Enum.GetValues<Direction>();
        context.MoveTank(directions[random.Next(0, directions.Length)]);
    }
}