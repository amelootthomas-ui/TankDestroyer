using LIOM.Bot.Extensions;
using LIOM.Bot.Objects;
using LIOM.Bot.Services.Location;
using LIOM.Bot.Services.Priority;
using LIOM.Bot.Services.Tanks;
using LIOM.Bot.Services.Turret;
using TankDestroyer.API;

namespace LIOM.Bot;

[Bot("Search bot", "Liam", "FFFF00")]
public class SearchyBot : IPlayerBot
{
    private int _turnCounter;
    private int _height;
    private int _width;
    private ITank? _tank;
    private ITank? _movedTank;
    private ITurnContext? _turnContext;
    private readonly PriorityCalculatorPipeline _pipeline = new();
    private ITankService? _tankService;
    private readonly IRelativeLocationCalculator _relativeLocationCalculator;
    private readonly ITurretService _turretService;
    private LiomDirection? _movementDirection;
    private readonly int _minimumDistance = 10;


    public SearchyBot()
    {
        _pipeline.Add(new PriorityDistanceCalculator());
        _pipeline.Add(new RepeatingPenaltyPriority(5));
        _pipeline.Add(new BulletPriority());
        _relativeLocationCalculator = new RelativeLocationCalculator();
        _turretService = new TurretService();
    }

    public void DoTurn(ITurnContext turnContext)
    {
        _turnCounter++;

        Init(turnContext);

        if (_turnContext is null) return;
        if (_tank is null) return;

        _pipeline.AddHistory(_turnCounter, new Location(_tank.X, _tank.Y));

        if (_tankService == null)
        {
            _tankService = new TankService(_tank.OwnerId, turnContext.GetTanks(), _relativeLocationCalculator);
            _pipeline.Add(new TilePriorityCalculator(_relativeLocationCalculator, _tankService, _minimumDistance));
        }
        else
        {
            _tankService.UpdateTanks(turnContext.GetTanks());
        }

        Search();
        Attack();
    }

    private void Init(ITurnContext turnContext)
    {
        _turnContext = turnContext;
        _tank = turnContext.Tank;
        _height = _height != 0 ? _height : turnContext.GetMapHeight();
        _width = _width != 0 ? _width : turnContext.GetMapHeight();
    }

    private void Search()
    {
        CalculateMove();
        FindClosestTank();
        Rotate();
        Move();
    }

    private void CalculateMove()
    {
        var priorityDirections =
            MoveDirections()
                .OrderByDescending(priority => priority.Priority)
                .ToList();

        var highestPriorityDirection = priorityDirections.FirstOrDefault();
        var highestDirections = priorityDirections
            .Where(priorityDirection =>
                Math.Abs(priorityDirection.Priority - (highestPriorityDirection?.Priority ?? -1.0)) < 0.5)
            .ToList();
        var amount = highestDirections.Count;
        var random = new Random();

        _movementDirection = highestDirections.ElementAtOrDefault(random.Next(0, amount))?.Direction;

        var x = _tank!.X;
        var y = _tank!.Y;

        x += _movementDirection switch
        {
            LiomDirection.East => 1,
            LiomDirection.West => -1,
            _ => 0
        };

        y += _movementDirection switch
        {
            LiomDirection.North => -1,
            LiomDirection.South => 1,
            _ => 0
        };

        _movedTank = new FakeTank(x, y, _tank!.Health, TurretDirection.East, false, false, _tank.OwnerId);
    }

    private void FindClosestTank() => _tankService?.FindClosestTank();


    private record DirectionPriority(double Priority, LiomDirection Direction);

    private void Rotate()
    {
        var closestTank = _tankService?.FindClosestTank();

        if (_tank is null) return;
        if (closestTank is null) return;

        var location = _relativeLocationCalculator.Calculate(_movedTank!, closestTank);
        var direction = _turretService.CalculateDirection(location);
        var turretDirection = direction.ToTurretDirection();
        
        _turnContext!.RotateTurret(turretDirection);
    }


    private void Move()
    {
        var movementDirection = _movementDirection?.ToDirection();
        _turnContext!.MoveTank(movementDirection ?? Direction.North);
    }

    private IEnumerable<DirectionPriority> MoveDirections()
    {
        var containingList = new List<LiomDirection>()
        {
            LiomDirection.North,
            LiomDirection.East,
            LiomDirection.South,
            LiomDirection.West
        };


        var directions = Enum.GetValues(typeof(LiomDirection))
            .Cast<LiomDirection>()
            .Where(containingList.Contains)
            .Select(d => new DirectionPriority(_pipeline.Calculate(_turnContext!, d), d))
            .ToList();

        return directions;
    }


    private void Attack()
    {
        _turnContext!.Fire();
    }
}