using System;
using System.Collections.Generic;
using System.Text;
using TankDestroyer.API;
using static System.Net.WebRequestMethods;

namespace JODMO.Bot
{
    internal static class LocateEnemyTankService
    {
        public static ITank LocateNearestTank(IEnumerable<ITank> tanks, ITank myTank)
        {
            int distanceToTank = 9999999;
            ITank nearestTank = tanks.First();
            foreach (ITank tank in tanks) 
            {
                var difference = Math.Abs(tank.X - myTank.X) + Math.Abs(tank.Y - myTank.Y);
                if (difference < distanceToTank)
                {
                    nearestTank = tank;
                }
            }
            return nearestTank;
        }
    }
}
