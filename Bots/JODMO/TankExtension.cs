using System;
using System.Collections.Generic;
using System.Text;
using TankDestroyer.API;

namespace JODMO.Bot
{
    internal static class TankExtension
    {
        public static bool EnemyInLineOfSight(this ITank tank, ITile tile, ITank enemyTank, ITurnContext turnContext)
        {
            var sweetSpots = MovementService.CalculateSweetspots(tile, enemyTank, turnContext);
            if (tile.X == enemyTank.X)
            {
                if (tile.Y < enemyTank.Y)
                {
                    for (int i = tile.Y; i <= enemyTank.Y; i++)
                    {
                        var currentTile = turnContext.GetTile(tile.X, i);
                        if (currentTile.TileType == TileType.Tree && !sweetSpots[tile.X, i])
                        {
                            return false;
                        }
                    }
                }
                else
                {
                    for (int i = tile.Y; i >= enemyTank.Y; i--)
                    {
                        var currentTile = turnContext.GetTile(tile.X, i);
                        if (currentTile.TileType == TileType.Tree && !sweetSpots[tile.X, i])
                        {
                            return false;
                        }
                    }
                }
                return true;
            }
            else if (tile.Y == enemyTank.Y)
            {
                if (tile.X < enemyTank.X)
                {
                    for (int i = tile.X; i <= enemyTank.X; i++)
                    {
                        var currentTile = turnContext.GetTile(i, tile.Y);
                        if (currentTile.TileType == TileType.Tree && !sweetSpots[i, tile.Y])
                        {
                            return false;
                        }
                    }
                }
                else
                {
                    for (int i = tile.X; i >= enemyTank.X; i--)
                    {
                        var currentTile = turnContext.GetTile(i, tile.Y);
                        if (currentTile.TileType == TileType.Tree && !sweetSpots[i, tile.Y])
                        {
                            return false;
                        }
                    }
                }
                return true;
            }
            return false;
        }

        
    }
}
