using TankDestroyer.API;

namespace HDJO.Bot;

public enum BotStyle
{
    Aggressive,
    Defensive,
}

public class MapCell
{
    public int X { get; set; }
    public int Y { get; set; }
    public bool IsWall { get; set; }
    public bool IsOccupied { get; set; }
    public bool ImPassable { get; set; }
    public TileType TileType { get; set; }
}

[Bot("Laurens bot 2", "Laurens", "00BDFF")]
public class LaurensBot2 : IPlayerBot
{
    private Direction _lastDirection = Direction.North;
    private ITurnContext _currentContext = null!;
    private List<MapCell> _map = [];
    private Random _random = new();
    private ITank _targetTank = null!;
    private ITank _myTank => _currentContext.Tank;
    private BotStyle _currentStyle = BotStyle.Aggressive;

    public HashSet<(int, int)> CalculateFiringZone((int x, int y) tankPosition)
    {
        HashSet<(int, int)> firingZone = [];
        // The tank can fire in one of 8 directions, and bullets travel 7 (including the tanks current position)
        for (int i = 1; i <= 7; i++)
        {
            firingZone.Add((tankPosition.x + i, tankPosition.y)); // East
            firingZone.Add((tankPosition.x - i, tankPosition.y)); // West
            firingZone.Add((tankPosition.x, tankPosition.y + i)); // South
            firingZone.Add((tankPosition.x, tankPosition.y - i)); // North
            firingZone.Add((tankPosition.x + i, tankPosition.y + i)); // South-East
            firingZone.Add((tankPosition.x - i, tankPosition.y + i)); // South-West
            firingZone.Add((tankPosition.x + i, tankPosition.y - i)); // North-East
            firingZone.Add((tankPosition.x - i, tankPosition.y - i)); // North-West
        }

        return firingZone;
    }

    public HashSet<(int x, int y)> GetBulletPath((int x, int y) pos, TurretDirection dir)
    {
        int dx = (dir.HasFlag(TurretDirection.West) ? 1 : 0) + (dir.HasFlag(TurretDirection.East) ? -1 : 0);
        int dy = (dir.HasFlag(TurretDirection.South) ? -1 : 0) + (dir.HasFlag(TurretDirection.North) ? 1 : 0);

        var path = new HashSet<(int x, int y)>();
        for (int i = 0; i < 7; i++)
            path.Add((pos.x + dx * i, pos.y + dy * i));
        return path;
    }

    public void Defend()
    {
        // Move out of fire zone existing tanks if possible
        // Move out of zones where existing bullets are travelling if possible
        HashSet<(int, int)> dangerZone = [];
        HashSet<(int, int)> bulletZone = [];
        foreach (var tank in _currentContext.GetTanks())
        {
            if (tank.Destroyed)
                continue;
            if (tank == _myTank)
                continue;
            // Ignore tanks that are further than 7 tiles away
            if (Math.Abs(tank.X - _myTank.X) > 8 || Math.Abs(tank.Y - _myTank.Y) > 8)
                continue;
            // Edit this code so it takes into account that the tank can move in 4 directions for 1 tile before firing
            dangerZone.UnionWith(CalculateFiringZone((tank.X, tank.Y))); // Current postion of enemy tank
            dangerZone.UnionWith(CalculateFiringZone((tank.X, tank.Y - 1))); // Enemy tank moves north
            dangerZone.UnionWith(CalculateFiringZone((tank.X + 1, tank.Y))); // Enemy tank moves east
            dangerZone.UnionWith(CalculateFiringZone((tank.X, tank.Y + 1))); // Enemy tank moves south
            dangerZone.UnionWith(CalculateFiringZone((tank.X - 1, tank.Y))); // Enemy tank moves west
        }

        foreach (var bullet in _currentContext.GetBullets())
        {
            // Add the current position of the bullet to the danger zone
            bulletZone.Add((bullet.X, bullet.Y));
            // Add the next positions of the bullet to the explosion zone
            bulletZone.UnionWith(GetBulletPath((bullet.X, bullet.Y), bullet.Direction));
        }
        HashSet<(int, int)> possibleMoves = [];
        possibleMoves.Add((_myTank.X, _myTank.Y)); // Staying in place
        possibleMoves.Add((_myTank.X, _myTank.Y - 1)); // Move north
        possibleMoves.Add((_myTank.X + 1, _myTank.Y)); // Move east
        possibleMoves.Add((_myTank.X, _myTank.Y + 1)); // Move south
        possibleMoves.Add((_myTank.X - 1, _myTank.Y)); // Move west

        var walkableTiles = _map.Where(c => c.TileType != TileType.Water).Select(c => (c.X, c.Y)).ToHashSet();
        possibleMoves.IntersectWith(walkableTiles);
        var safeMoves = possibleMoves.ToHashSet();

        // Filter out moves that are in the danger zone or bullet zone
        safeMoves.ExceptWith(bulletZone);
        if (safeMoves.Count == 1)
        {
            MoveTo(safeMoves.First());
            return;
        }
        safeMoves.ExceptWith(dangerZone);
        if (safeMoves.Count == 1)
        {
            MoveTo(safeMoves.First());
            return;
        }

        if (safeMoves.Count == 0)
        {
            safeMoves = possibleMoves;
        }

        var orderedMoves = safeMoves.OrderBy(m => Math.Abs(m.Item1 - _targetTank.X) + Math.Abs(m.Item2 - _targetTank.Y));

        (int x, int y) bestMove = _currentStyle switch
        {
            BotStyle.Aggressive => orderedMoves.First(),
            BotStyle.Defensive => orderedMoves.Last(),
            _ => orderedMoves.First(),
        };

        MoveTo(bestMove);
    }

    public void CreateMap()
    {
        _map = new List<MapCell>();
        for (int x = 0; x < _currentContext.GetMapWidth(); x++)
        {
            for (int y = 0; y < _currentContext.GetMapHeight(); y++)
            {
                var tile = _currentContext.GetTile(x, y);
                _map.Add(
                    new MapCell
                    {
                        X = x,
                        Y = y,
                        TileType = tile.TileType,
                        ImPassable = tile.TileType == TileType.Water,
                        IsOccupied = _currentContext.GetTanks().Any(t => t.X == x && t.Y == y),
                    }
                );
            }
        }
    }

    public void MoveWest()
    {
        _currentContext.MoveTank(Direction.East);
        _lastDirection = Direction.West;
    }

    public void MoveEast()
    {
        _currentContext.MoveTank(Direction.West);
        _lastDirection = Direction.East;
    }

    public void MoveNorth()
    {
        _currentContext.MoveTank(Direction.South);
        _lastDirection = Direction.North;
    }

    public void MoveSouth()
    {
        _currentContext.MoveTank(Direction.North);
        _lastDirection = Direction.South;
    }

    public TurretDirection Aim(ITank a, ITank b)
    {
        const float Threshold = 0.414f;
        float dx = b.X - a.X,
            dy = b.Y - a.Y;
        float absX = Math.Abs(dx),
            absY = Math.Abs(dy);
        TurretDirection d = 0;

        if (absY > absX * Threshold)
            d |= (b.Y > a.Y) ? TurretDirection.North : TurretDirection.South;
        if (absX > absY * Threshold)
            d |= (b.X > a.X) ? TurretDirection.West : TurretDirection.East;

        return d;
    }

    public void MoveTo((int x, int y) target)
    {
        bool checkXFirst = _lastDirection == Direction.North || _lastDirection == Direction.South;

        if (checkXFirst)
        {
            if (target.x > _myTank.X)
                MoveEast();
            else if (target.x < _myTank.X)
                MoveWest();
            else if (target.y > _myTank.Y)
                MoveSouth();
            else if (target.y < _myTank.Y)
                MoveNorth();
        }
        else
        {
            if (target.y > _myTank.Y)
                MoveSouth();
            else if (target.y < _myTank.Y)
                MoveNorth();
            else if (target.x > _myTank.X)
                MoveEast();
            else if (target.x < _myTank.X)
                MoveWest();
        }
    }

    public void DoTurn(ITurnContext turnContext)
    {
        _currentContext = turnContext;
        CreateMap();

        var myTank = turnContext.Tank;
        _targetTank = turnContext.GetTanks().FirstOrDefault(t => t != myTank);

        turnContext.RotateTurret(Aim(myTank, _targetTank));

        Defend();

        turnContext.Fire();
    }
}
