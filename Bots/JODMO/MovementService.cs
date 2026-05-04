using System;
using System.Collections.Generic;
using System.Text;
using TankDestroyer.API;

namespace JODMO.Bot
{
    internal static class MovementService
    {
        public static Direction? MoveToEnemy(ITank enemy, ITank myTank, ITurnContext turnContext)
        {
            // kijken wat al de mogelijkheden zijn, dan kijken welke van de posities het beste is om te kunnen schieten
            //int mapWidth = turnContext.GetMapWidth() - 1;
            //int mapHeight = turnContext.GetMapHeight() - 1;

            //List<ITile> possiblePositions = new();
            //possiblePositions.Add(turnContext.GetTile(myTank.X, myTank.Y));
            //if (myTank.X < mapWidth)
            //{
            //    possiblePositions.Add(turnContext.GetTile(myTank.X + 1, myTank.Y));
            //}
            //if (myTank.Y < mapHeight)
            //{
            //    possiblePositions.Add(turnContext.GetTile(myTank.X, myTank.Y + 1));
            //}
            //if (myTank.X > 0)
            //{
            //    possiblePositions.Add(turnContext.GetTile(myTank.X - 1, myTank.Y));
            //}
            //if (myTank.Y > 0)
            //{
            //    possiblePositions.Add(turnContext.GetTile(myTank.X, myTank.Y - 1));
            //}
            //ITile bestMove = turnContext.GetTile(enemy.X, enemy.Y);
            //foreach (var possiblePosition in possiblePositions)
            //{
            //    if (myTank.EnemyInLineOfSight(possiblePosition, enemy, turnContext))
            //    {
            //        bestMove = possiblePosition;
            //        break;
            //    }

            //}
            if (enemy.X > myTank.X && turnContext.GetTile(myTank.X + 1, myTank.Y).TileType != TileType.Water)
            {
                return Direction.West;
            }
            else if (enemy.X < myTank.X && turnContext.GetTile(myTank.X - 1, myTank.Y).TileType != TileType.Water)
            {
                return Direction.East;
            }
            else if (enemy.Y > myTank.Y && turnContext.GetTile(myTank.X, myTank.Y + 1).TileType != TileType.Water)
            {
                return Direction.North;
            }
            else if (enemy.Y < myTank.Y && turnContext.GetTile(myTank.X, myTank.Y + 1).TileType != TileType.Water)
            {
                return Direction.South;
            }
            return null;
        }

        public static bool[,] CalculateSweetspots(ITile tile, ITank enemyTank, ITurnContext turnContext)
        {
            int mapWidth = turnContext.GetMapWidth();
            int mapHeight = turnContext.GetMapHeight();
            bool[,] sweetSpots = new bool[mapWidth, mapHeight];
            for (int i = 0; i < mapWidth; i++)
            {
                for (int j = 0; j < mapHeight; j++)
                {
                    sweetSpots[i, j] = i == enemyTank.X || j == enemyTank.Y || (tile.X + tile.Y == i + j) || (tile.X - tile.Y == i - j);
                }
            }
            return sweetSpots;
        }
    }
}
