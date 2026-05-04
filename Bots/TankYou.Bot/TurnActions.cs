using TankDestroyer.API;

namespace TankYou.Bot;

internal record TurnActions
{
    public Direction? MoveDirection { get; set; }
    public TurretDirection? RotateDirection { get; set; }
    public bool Fire { get; set; }

    public bool Moved => MoveDirection.HasValue;
}
