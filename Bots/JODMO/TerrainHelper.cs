using System;
using System.Collections.Generic;
using System.Text;
using TankDestroyer.API;

namespace JODMO.Bot
{
    internal static class TerrainHelper
    {
        //public static Dictionary<TileType, IEnumerable<ITile>> CalulateMap(ITurnContext turnContext)
        //{
        //    Dictionary<TileType, IEnumerable<ITile>> map = new Dictionary<TileType, IEnumerable<ITile>>();
        //    for (int width = 0; width <= turnContext.GetMapWidth(); width++)
        //    {
        //        for (int height = 0; height <= turnContext.GetMapHeight(); height++)
        //        {
        //            var currentTile = turnContext.GetTile(height, width);
        //            IEnumerable<ITile> tiles;
        //            if (map.TryGetValue(currentTile.TileType, out tiles))
        //            {
        //                map[currentTile.TileType] = tiles.Concat(new[] { currentTile });
        //            }
        //            else
        //            {
        //                map.Add(currentTile.TileType, new[] { currentTile });
        //            }
        //        }
        //    }
        //    return map;
        //}
        public static ITile CalculateCosestGrass(Dictionary<TileType, IEnumerable<ITile>> map, ITile currentPosition)
        {
            return CalculateClosestTileForTileType(map, currentPosition, TileType.Grass);
        }
        public static ITile CalculateCosestSand(Dictionary<TileType, IEnumerable<ITile>> map, ITile currentPosition)
        {
            return CalculateClosestTileForTileType(map, currentPosition, TileType.Sand);
        }
        public static ITile CalculateCosestBuilding(Dictionary<TileType, IEnumerable<ITile>> map, ITile currentPosition)
        {
            return CalculateClosestTileForTileType(map, currentPosition, TileType.Building);
        }
        public static ITile CalculateCosestTree(Dictionary<TileType, IEnumerable<ITile>> map, ITile currentPosition)
        {
            return CalculateClosestTileForTileType(map, currentPosition, TileType.Tree);
        }
        public static ITile CalculateCosestWater(Dictionary<TileType, IEnumerable<ITile>> map, ITile currentPosition)
        {
            return CalculateClosestTileForTileType(map, currentPosition, TileType.Water);
        }

        private static ITile CalculateClosestTileForTileType(Dictionary<TileType, IEnumerable<ITile>> map, ITile currentPosition, TileType type)
        {
            var typeTiles = map.GetValueOrDefault(type);
            int lowestDifference = 9999999;
            ITile closestTile = typeTiles.First();
            foreach (ITile tile in typeTiles)
            {
                var difference = Math.Abs(tile.X - currentPosition.X) + Math.Abs(tile.Y - currentPosition.Y);
                if (difference < lowestDifference)
                {
                    closestTile = tile;
                }
            }
            return closestTile;
        }
    }
}
