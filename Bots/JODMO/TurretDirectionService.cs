using System;
using System.Collections.Generic;
using System.Text;
using TankDestroyer.API;

namespace JODMO.Bot
{
    internal class TurretDirectionService
    {
        public static TurretDirection CalculateTurretDirection(ITank enemyTank, ITank myTank)
        {
            if (enemyTank.X > myTank.X && enemyTank.Y == myTank.Y)
            {
                return TurretDirection.West;
            }
            if (enemyTank.X < myTank.X && enemyTank.Y == myTank.Y)
            {
                return TurretDirection.East;
            }
            if (enemyTank.X == myTank.X && enemyTank.Y > myTank.Y)
            {
                return TurretDirection.North;
            }
            if (enemyTank.X == myTank.X && enemyTank.Y < myTank.Y)
            {
                return TurretDirection.South;
            }
            if (enemyTank.X > myTank.X && enemyTank.Y > myTank.Y)
            {
                return TurretDirection.NorthWest;
            }
            if (enemyTank.X < myTank.X && enemyTank.Y > myTank.Y)
            {
                return TurretDirection.NorthEast;
            }
            if (enemyTank.X > myTank.X && enemyTank.Y < myTank.Y)
            {
                return TurretDirection.SouthWest;
            }
            else /*(enemyTank.X < myTank.X && enemyTank.Y < myTank.Y)*/
            {
                return TurretDirection.SouthEast;
            }

        }
    }
}
