using TankDestroyer.API;

namespace TankYou.Bot;

internal static class PathVisualiser
{
    public static void Print(
        ITurnContext context,
        List<(int x, int y)> path,
        int frame = 0)
    {
        int width = context.GetMapWidth();
        int height = context.GetMapHeight();

        var pathSet = new HashSet<(int, int)>(path);
        var tanks = context.GetTanks().ToList();
        var bullets = context.GetBullets().ToList();

        bool flash = frame % 2 == 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var pos = (x, y);

                var tank = tanks.FirstOrDefault(t => t.X == x && t.Y == y);
                var bullet = bullets.FirstOrDefault(b => b.X == x && b.Y == y);
                bool isDanger = Danger.IsDangerous(context, pos) != -1;
                bool isPath = pathSet.Contains(pos);
                bool isUnsafePath = isPath && isDanger;

                if (tank != null)
                {
                    Console.ForegroundColor = tank.OwnerId == context.Tank.OwnerId
                        ? ConsoleColor.Green
                        : ConsoleColor.Red;

                    Write2(GetTankArrow(tank.TurretDirection));
                    continue;
                }

                if (bullet != null)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Write2(GetBulletChar(bullet.Direction));
                    continue;
                }

                if (isUnsafePath)
                {
                    Console.ForegroundColor = ConsoleColor.DarkRed;

                    Write2('!');
                    continue;
                }

                if (isPath)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Write2('*');
                    continue;
                }

                if (isDanger)
                {
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Write2('x');
                    continue;
                }

                PrintTile(context, x, y);
            }

            Console.WriteLine();
        }

        Console.ResetColor();
        Console.WriteLine();
    }

    private static void Write2(char c)
    {
        Console.Write(c);
        Console.Write(' ');
        Console.ResetColor();
    }

    private static void PrintTile(ITurnContext context, int x, int y)
    {
        switch (context.GetTile(x, y).TileType)
        {
            case TileType.Grass:
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Write2('.');
                break;

            case TileType.Sand:
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Write2(',');
                break;

            case TileType.Tree:
                Console.ForegroundColor = ConsoleColor.DarkGreen;
                Write2('T');
                break;

            case TileType.Building:
                Console.ForegroundColor = ConsoleColor.Gray;
                Write2('^');
                break;

            case TileType.Water:
                Console.ForegroundColor = ConsoleColor.DarkBlue;
                Write2('#');
                break;

            default:
                Write2('?');
                break;
        }
    }

    private static char GetTankArrow(TurretDirection dir) => dir switch
    {
        TurretDirection.North => '↓',
        TurretDirection.NorthEast => '↙',
        TurretDirection.East => '←',
        TurretDirection.SouthEast => '↖',
        TurretDirection.South => '↑',
        TurretDirection.SouthWest => '↗',
        TurretDirection.West => '→',
        TurretDirection.NorthWest => '↘',
        _ => '?'
    };
    private static char GetBulletChar(TurretDirection dir) => dir switch
    {
        TurretDirection.North or TurretDirection.South => '│',
        TurretDirection.East or TurretDirection.West => '─',
        TurretDirection.NorthEast or TurretDirection.SouthWest => '/',
        TurretDirection.NorthWest or TurretDirection.SouthEast => '\\',
        _ => 'o'
    };
}
