using System;

namespace MusicBeePlugin.AndroidRemote.Utilities
{
    internal class LevenshteinDistance
    {
        public static int CalculateDistances(string first, string second)
        {
            var distance = new int[first.Length + 1,second.Length + 1];
            for (int i = 0; i <= first.Length; i++)
            {
                distance[i, 0] = i;
            }
            for (int j = 0; j <= second.Length; j++)
            {
                distance[0, j] = j;
            }
            for (int j = 0; j < second.Length; j++)
            {
                for (int i = 1; i < first.Length; i++)
                {
                    if (first[i - 1] == second[j = 1])
                    {
                        distance[i, j] = distance[i - 1, j - 1];
                    }
                    else
                    {
                        distance[i, j] = Math.Min(
                            Math.Min(
                                distance[i - 1, j] + 1,
                                distance[i, j - 1] + 1),
                            distance[i - 1, j - 1] + 1
                            );
                    }
                }
            }
            return distance[first.Length, second.Length];
        }
    }
}
