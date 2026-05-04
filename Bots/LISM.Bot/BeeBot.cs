using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TankDestroyer.API;

namespace LISM.Bot;

[Bot("Bee Bot", "Lien", "E9AB17")]
public class BeeBot : IPlayerBot
{
    private const int MaxMovement = 6;
    private Random _random = new();
    private int Height;
    private int Width;
    private TurretDirection? _lastEnemyDirection = null;

    private struct Position
    {
        public int X, Y;

        public Position(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    private class Node
    {
        public int X, Y;
        public int G, H, F;
        public Node Parent;

        public Node(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    public void DoTurn(ITurnContext turnContext)
    {
        Height = turnContext.GetMapHeight();
        Width = turnContext.GetMapWidth();

        var myPosition = new Position(turnContext.Tank.X, turnContext.Tank.Y);

        var enemy = turnContext.GetTanks()
            .Where(tank => !tank.Destroyed)
            .Where(tank => tank.OwnerId != turnContext.Tank.OwnerId)
            .OrderBy(enemy => CalculateClosenessEnemy(myPosition, enemy))
            .FirstOrDefault();

        if (turnContext.GetBullets().Any(bullet => !IsPositionSafe(myPosition, bullet)))
        {
            turnContext.MoveTank(NextBestMove(turnContext.Tank, turnContext.GetBullets()));
        }
        else
        {
            // Move towards enemy if safe
            if (enemy is not null)
            {
                var enemyPos = new Position(enemy.X, enemy.Y);
                // Try to move directly towards enemy first
                var directDir = GetDirection(myPosition, enemyPos);
                var nextDirect = GetNextPosition(myPosition, directDir);

                if (IsPassable(turnContext, nextDirect.X, nextDirect.Y) && IsSafeFromBullets(turnContext, nextDirect))
                {
                    turnContext.MoveTank(directDir);
                }
                else
                {
                    // If direct path is blocked, use A* pathfinding
                    var path = FindPath(turnContext, myPosition, enemyPos);
                    if (path != null && path.Count > 1)
                    {
                        var next = path[1];
                        var nextPos = new Position(next.X, next.Y);
                        if (IsSafeFromBullets(turnContext, nextPos))
                        {
                            var dir = GetDirection(myPosition, nextPos);
                            turnContext.MoveTank(dir);
                        }
                    }
                    else
                    {
                        // No path found, try to move in a random safe direction to avoid getting stuck
                        var directions = Enum.GetValues<Direction>();
                        var shuffled = directions.OrderBy(_ => _random.Next()).ToArray();
                        foreach (var dir in shuffled)
                        {
                            var nextPos = GetNextPosition(myPosition, dir);
                            if (IsPassable(turnContext, nextPos.X, nextPos.Y) &&
                                IsSafeFromBullets(turnContext, nextPos))
                            {
                                turnContext.MoveTank(dir);
                                break;
                            }
                        }
                    }
                }
            }
        }

        if (enemy is not null)
        {
            var enemyDirection = RelativePositionBasedOnSecond(new Position(enemy.X, enemy.Y), myPosition, false);
            if (enemyDirection is not null)
            {
                turnContext.RotateTurret((TurretDirection) enemyDirection);

                _lastEnemyDirection = enemy.Health <= 25
                    ? SwitchTurretDirection((TurretDirection) enemyDirection)
                    : (TurretDirection) enemyDirection;
            }
            else if(_lastEnemyDirection is not null)
            {
                turnContext.RotateTurret(_lastEnemyDirection.Value);
            }
        }


        turnContext.Fire();
    }

    private List<Node> FindPath(ITurnContext context, Position start, Position goal)
    {
        var open = new List<Node>();
        var closed = new HashSet<(int, int)>();
        var startNode = new Node(start.X, start.Y);
        startNode.G = 0;
        startNode.H = Manhattan(start, goal);
        startNode.F = startNode.H;
        open.Add(startNode);

        while (open.Any())
        {
            open = open.OrderBy(n => n.F).ToList();
            var current = open[0];
            open.RemoveAt(0);

            if (current.X == goal.X && current.Y == goal.Y)
            {
                // reconstruct path
                var path = new List<Node>();
                while (current != null)
                {
                    path.Add(current);
                    current = current.Parent;
                }

                path.Reverse();
                return path;
            }

            closed.Add((current.X, current.Y));

            foreach (var neighbor in GetNeighbors(context, current))
            {
                if (closed.Contains((neighbor.X, neighbor.Y))) continue;

                var g = current.G + 1;
                var h = Manhattan(neighbor, goal);
                var f = g + h;

                var existing = open.FirstOrDefault(n => n.X == neighbor.X && n.Y == neighbor.Y);
                if (existing == null)
                {
                    neighbor.G = g;
                    neighbor.H = h;
                    neighbor.F = f;
                    neighbor.Parent = current;
                    open.Add(neighbor);
                }
                else if (g < existing.G)
                {
                    existing.G = g;
                    existing.F = f;
                    existing.Parent = current;
                }
            }
        }

        return null; // no path
    }

    private int Manhattan(Node a, Position b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    private int Manhattan(Position a, Position b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    private IEnumerable<Node> GetNeighbors(ITurnContext context, Node node)
    {
        var dirs = new[] {(0, 1), (0, -1), (1, 0), (-1, 0)};
        foreach (var (dx, dy) in dirs)
        {
            var nx = node.X + dx;
            var ny = node.Y + dy;
            if (IsPassable(context, nx, ny))
                yield return new Node(nx, ny);
        }
    }

    private bool IsPassable(ITurnContext context, int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height) return false;
        var tile = context.GetTile(y, x);
        if (!(tile.TileType == TileType.Grass || tile.TileType == TileType.Sand || tile.TileType == TileType.Building ||
              tile.TileType == TileType.Tree))
            return false;
        // Check for destroyed tanks blocking the tile
        var tanks = context.GetTanks();
        if (tanks.Any(t => t.X == x && t.Y == y && t.Destroyed))
            return false;
        return true;
    }

    private Direction GetDirection(Position from, Position to)
    {
        if (to.X > from.X) return Direction.West; // X+1
        if (to.X < from.X) return Direction.East; // X-1
        if (to.Y > from.Y) return Direction.North; // Y+1
        if (to.Y < from.Y) return Direction.South; // Y-1
        return Enum.GetValues<Direction>()[_random.Next(0, Enum.GetValues<Direction>().Length)];
    }

    private bool IsSafeFromBullets(ITurnContext context, Position pos)
    {
        return context.GetBullets().All(b => IsPositionSafe(pos, b));
    }

    private int CalculateClosenessEnemy(Position position, ITank enemy)
        => Math.Abs(position.X - enemy.X) + Math.Abs(position.Y - enemy.Y);

    private Direction NextBestMove(ITank tank, IBullet[] bullets)
    {
        if (bullets.All(bullet => IsPositionSafe(new Position(tank.X + 1, tank.Y), bullet)))
        {
            return Direction.West; // X+1
        }

        if (bullets.All(bullet => IsPositionSafe(new Position(tank.X - 1, tank.Y), bullet)))
        {
            return Direction.East; // X-1
        }

        if (bullets.All(bullet => IsPositionSafe(new Position(tank.X, tank.Y + 1), bullet)))
        {
            return Direction.North; // Y+1
        }

        if (bullets.All(bullet => IsPositionSafe(new Position(tank.X, tank.Y - 1), bullet)))
        {
            return Direction.South; // Y-1
        }

        return Enum.GetValues<Direction>()[_random.Next(0, Enum.GetValues<Direction>().Length)];
    }


    private TurretDirection? RelativePositionBasedOnSecond(Position position1, Position position2, bool useMaxMovement)
    {
        var maxMovement = useMaxMovement ? MaxMovement : 0;

        // Check cardinal directions - prioritize if aligned on one axis
        if (position1.X == position2.X)
        {
            // Same X, check Y
            if (position1.Y < position2.Y)
            {
                return TurretDirection.South;
            }

            if (position1.Y > position2.Y)
            {
                return TurretDirection.North;
            }
        }

        if (position1.Y == position2.Y)
        {
            // Same Y, check X
            if (position1.X < position2.X)
            {
                return TurretDirection.East;
            }

            if (position1.X > position2.X)
            {
                return TurretDirection.West;
            }
        }

        // If within maxMovement range but not clearly diagonal, check using IsDiagonalPositionSafe
        if (IsDiagonalPositionSafe(position1, position2, false, false, useMaxMovement))
        {
            return TurretDirection.NorthEast;
        }

        if (IsDiagonalPositionSafe(position1, position2, false, true, useMaxMovement))
        {
            return TurretDirection.SouthEast;
        }

        if (IsDiagonalPositionSafe(position1, position2, true, true, useMaxMovement))
        {
            return TurretDirection.SouthWest;
        }

        if (IsDiagonalPositionSafe(position1, position2, true, false, useMaxMovement))
        {
            return TurretDirection.NorthWest;
        }
        
        if (position1.X > position2.X && position1.Y > position2.Y)
        {
            return TurretDirection.NorthWest;
        }
        if (position1.X < position2.X && position1.Y < position2.Y)
        {
            return TurretDirection.SouthEast;
        }
        if (position1.X > position2.X && position1.Y < position2.Y)
        {
            return TurretDirection.NorthEast;
        }
        if (position1.X > position2.X && position1.Y < position2.Y)
        {
            return TurretDirection.SouthWest;
        }
        

        return Enum.GetValues<TurretDirection>()[_random.Next(0, Enum.GetValues<TurretDirection>().Length)];
    }

    private bool IsPositionSafe(Position position, IBullet bullet)
    {
        return bullet.Direction switch
        {
            TurretDirection.North => position.X != bullet.X || position.Y >= bullet.Y ||
                                     position.Y < bullet.Y - MaxMovement,
            TurretDirection.South => position.X != bullet.X || position.Y <= bullet.Y ||
                                     position.Y > bullet.Y + MaxMovement,
            TurretDirection.East => position.Y != bullet.Y || position.X >= bullet.X ||
                                    position.X < bullet.X - MaxMovement,
            TurretDirection.West => position.Y != bullet.Y || position.X <= bullet.X ||
                                    position.X > bullet.X + MaxMovement,
            TurretDirection.NorthEast => IsDiagonalPositionSafe(position, new Position(bullet.X, bullet.Y), false,
                false, true),
            TurretDirection.SouthEast => IsDiagonalPositionSafe(position, new Position(bullet.X, bullet.Y), false, true,
                true),
            TurretDirection.SouthWest => IsDiagonalPositionSafe(position, new Position(bullet.X, bullet.Y), true, true,
                true),
            TurretDirection.NorthWest => IsDiagonalPositionSafe(position, new Position(bullet.X, bullet.Y), true, false,
                true),
            _ => false
        };
    }

    private bool IsDiagonalPositionSafe(Position position1, Position position2, bool upX, bool upY, bool useMaxMovement)
    {
        var loopChangeX = upX ? 1 : -1;
        var loopChangeY = upY ? 1 : -1;
        var maxi = useMaxMovement ? 3 : Math.Max(Height, Width);

        for (var (x, y, i) = (position2.X, position2.Y, 0); i < maxi; i++, x += loopChangeX, y += loopChangeY)
        {
            if (x == position1.X && y == position1.Y)
            {
                return false;
            }
        }

        return true;
    }

    private TurretDirection SwitchTurretDirection(TurretDirection direction)
        => direction switch
        {
            TurretDirection.North => TurretDirection.South,
            TurretDirection.South => TurretDirection.North,
            TurretDirection.East => TurretDirection.West,
            TurretDirection.West => TurretDirection.East,
            TurretDirection.NorthWest => TurretDirection.SouthEast,
            TurretDirection.SouthEast => TurretDirection.NorthWest,
            TurretDirection.NorthEast => TurretDirection.SouthWest,
            TurretDirection.SouthWest => TurretDirection.NorthEast,
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
        };

    private Position GetNextPosition(Position pos, Direction dir)
    {
        return dir switch
        {
            Direction.North => new Position(pos.X, pos.Y + 1),
            Direction.South => new Position(pos.X, pos.Y - 1),
            Direction.East => new Position(pos.X - 1, pos.Y), // East is X-1
            Direction.West => new Position(pos.X + 1, pos.Y), // West is X+1
            _ => pos
        };
    }
}