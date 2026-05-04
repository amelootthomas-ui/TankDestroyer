using TankDestroyer.API;

namespace LIOM.Bot.Objects;

public class FakeTank(
    int x,
    int y,
    int health,
    TurretDirection turretDirection,
    bool destroyed,
    bool fired,
    int ownerId)
    : ITank
{
    public int X { get; } = x;
    public int Y { get; } = y;
    public int Health { get; } = health;
    public TurretDirection TurretDirection { get; } = turretDirection;
    public bool Destroyed { get; } = destroyed;
    public bool Fired { get; } = fired;
    public int OwnerId { get; } = ownerId;
}