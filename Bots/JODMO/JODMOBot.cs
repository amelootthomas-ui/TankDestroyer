using System.Runtime.CompilerServices;
using TankDestroyer.API;

namespace JODMO.Bot;

[Bot("JODMO", "Johan De Moor", "CC6600")]
public class JODMOBot : IPlayerBot
{
    private Random _random = new();
    private IBullet[] bullets;
    private IEnumerable<ITank> enemyTanks;
    int mapHeight;
    int mapWidth;
    private ITank nearestTank;
    private Dictionary<TileType, IEnumerable<ITile>> map;


    public void DoTurn(ITurnContext turnContext)
    {
        //ToDo kogels ontwijken, rekening houden met terrein
        //if (map == null)
        //{
        //    map = TerrainHelper.CalulateMap(turnContext);
        //}

        bullets = turnContext.GetBullets();
        enemyTanks = turnContext.GetTanks().Where(tank => tank != turnContext.Tank && !tank.Destroyed);
        
        if (enemyTanks.Count() > 1)
        {
            nearestTank = LocateEnemyTankService.LocateNearestTank(enemyTanks, turnContext.Tank);
        }
        else
        {
            nearestTank = enemyTanks.First();
        }
        if (turnContext.Tank.EnemyInLineOfSight(turnContext.GetTile(turnContext.Tank.X, turnContext.Tank.Y), enemyTanks.First(), turnContext))
        {
            Console.WriteLine("In line of sight");
            turnContext.RotateTurret(TurretDirectionService.CalculateTurretDirection(nearestTank, turnContext.Tank));

            turnContext.Fire();
            return;
        }
        var directionOfEnemy = MovementService.MoveToEnemy(nearestTank, turnContext.Tank, turnContext);
        if (directionOfEnemy is Direction direction)
        {
            turnContext.MoveTank(direction);
        }
        //mapHeight = turnContext.GetMapHeight();
        //mapWidth = turnContext.GetMapWidth();
        //var enumValues = Enum.GetValues<TurretDirection>();
        //var enumDirectionValues = Enum.GetValues<Direction>();

        //turnContext.MoveTank(enumDirectionValues[_random.Next(0, enumDirectionValues.Length)]);
        turnContext.RotateTurret(TurretDirectionService.CalculateTurretDirection(nearestTank, turnContext.Tank));

        turnContext.Fire();
    }
}