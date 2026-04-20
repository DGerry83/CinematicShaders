using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CinematicShaders.Core
{
    /// <summary>
    /// Shared star search utility with relevance-based ranking.
    /// Used by both the legacy editor window and the holographic Star Console.
    /// </summary>
    public static class StarSearchUtility
    {
        /// <summary>
        /// Searches a star list and returns results ranked by relevance.
        /// </summary>
        /// <param name="allStars">The full catalog of stars.</param>
        /// <param name="query">The search query.</param>
        /// <param name="maxResults">Maximum number of results to return. Zero or negative = unlimited.</param>
        /// <returns>
        /// If query is empty/whitespace: all stars sorted alphabetically by name.
        /// Otherwise: matching stars sorted by relevance score (highest first), then alphabetically.
        /// </returns>
        public static List<NamedStar> SearchStars(List<NamedStar> allStars, string query, int maxResults = 0)
        {
            return SearchStars(allStars, query, maxResults, 0);
        }

        public static List<NamedStar> SearchStars(List<NamedStar> allStars, string query, int maxResults, int offset)
        {
            if (allStars == null || allStars.Count == 0)
                return new List<NamedStar>();

            if (string.IsNullOrWhiteSpace(query))
            {
                var sorted = new List<NamedStar>(allStars);
                sorted.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                return SliceResults(sorted, maxResults, offset);
            }

            string queryLower = query.ToLowerInvariant();
            var scored = new List<ScoredStar>(allStars.Count);

            foreach (var star in allStars)
            {
                int score = ComputeRelevanceScore(star, queryLower);
                if (score > 0)
                    scored.Add(new ScoredStar { Star = star, Score = score });
            }

            // Sort by score descending, then name ascending
            scored.Sort((a, b) =>
            {
                int scoreDiff = b.Score.CompareTo(a.Score);
                if (scoreDiff != 0) return scoreDiff;
                return string.Compare(a.Star.Name, b.Star.Name, StringComparison.OrdinalIgnoreCase);
            });

            var allMatches = new List<NamedStar>(scored.Count);
            for (int i = 0; i < scored.Count; i++)
                allMatches.Add(scored[i].Star);

            return SliceResults(allMatches, maxResults, offset);
        }

        private static List<NamedStar> SliceResults(List<NamedStar> sortedResults, int maxResults, int offset)
        {
            if (offset <= 0 && maxResults <= 0)
                return sortedResults;

            var sliced = new List<NamedStar>();
            int start = Mathf.Max(0, offset);
            int end = maxResults > 0 ? Mathf.Min(start + maxResults, sortedResults.Count) : sortedResults.Count;
            for (int i = start; i < end; i++)
                sliced.Add(sortedResults[i]);
            return sliced;
        }

        /// <summary>
        /// Computes a relevance score for a star against a query.
        /// Higher scores indicate stronger matches.
        /// </summary>
        private static int ComputeRelevanceScore(NamedStar star, string queryLower)
        {
            string nameLower = star.Name.ToLowerInvariant();
            string hipStr = star.HipparcosID.ToString();

            if (hipStr == queryLower)                       return 100; // Exact HIP ID
            if (nameLower == queryLower)                    return 90;  // Exact name
            if (nameLower.StartsWith(queryLower))           return 80;  // Starts with query
            if (nameLower.Contains(" " + queryLower))       return 60;  // Word boundary
            if (nameLower.Contains(queryLower))             return 40;  // Contains anywhere
            if (hipStr.Contains(queryLower))                return 20;  // HIP contains digits

            return 0;
        }

        private struct ScoredStar
        {
            public NamedStar Star;
            public int Score;
        }
    }
}
