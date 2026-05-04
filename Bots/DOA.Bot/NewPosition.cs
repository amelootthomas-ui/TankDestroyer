using TankDestroyer.API;

namespace DOA.Bot;

public record NewPosition
{
    public Direction? MoveTo { get; init; }
    public int X { get; init;}
    public int Y { get; init;}

    public int Damage { get; set; } = 0;

    public NewPosition((int Y, int X) basePosition, Direction? moveTo = null)
    {
        if (!moveTo.HasValue)
        {
            Y = basePosition.Y;
            X = basePosition.X;

            return;
        }

        MoveTo = moveTo;
        X = basePosition.X + MoveTo switch
        { // note that East and West are flipped :(
            Direction.West => -1,
            Direction.East =>  1,
            _ => 0
        };
        Y = basePosition.Y + MoveTo switch
        {
            Direction.South => -1,
            Direction.North =>  1,
            _ => 0
        };
    }

    public static bool PositionExists((int Y, int X) basePosition, Direction direction, ITurnContext context)
    {
        var x = basePosition.X + direction switch
        { // note that East and West are flipped :(
            Direction.West => -1,
            Direction.East =>  1,
            _ => 0
        };
        var y = basePosition.Y + direction switch
        {
            Direction.South => -1,
            Direction.North =>  1,
            _ => 0
        };

        return 0 <= x && x <= context.GetMapWidth()
            && 0 <= y && y <= context.GetMapHeight();
    }
}