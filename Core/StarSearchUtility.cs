using System;
using System.Collections.Generic;
using System.Linq;

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
            if (allStars == null || allStars.Count == 0)
                return new List<NamedStar>();

            if (string.IsNullOrWhiteSpace(query))
            {
                var sorted = new List<NamedStar>(allStars);
                sorted.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                return sorted;
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

            var results = new List<NamedStar>();
            int limit = maxResults > 0 ? Math.Min(maxResults, scored.Count) : scored.Count;
            for (int i = 0; i < limit; i++)
                results.Add(scored[i].Star);

            return results;
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
