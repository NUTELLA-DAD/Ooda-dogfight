namespace Ooda.Turn.Core
{
    /// <summary>
    /// The Alpine Campaign: the missions the mission briefing offers after the squadron briefing, numbered in the order
    /// the route on its map joins them (up the island from the farmland in the south to the peaks in the north, then out
    /// to sea to the city, and on over the open sea into the cloud). Each one is a dogfight over one of the arenas
    /// (Arenas.All), flown with the flight and the start picked in the squadron briefing. New missions join the end of All. Pure C#, no Unity.
    /// </summary>
    public struct CampaignMission
    {
        /// <summary>The mission's name on the briefing, as it is painted (upper case).</summary>
        public string title;
        /// <summary>One line: what you are there to do.</summary>
        public string objective;
        /// <summary>Which arena it is flown over (Arenas.All).</summary>
        public int arena;
        /// <summary>How hard the ground makes it, 0 easy to 3 ace (the rock and the towers kill as surely as the guns).</summary>
        public int difficulty;
    }

    public static class Campaign
    {
        public const string Name = "Alpine Campaign";

        public static readonly CampaignMission[] All =
        {
            new CampaignMission { title = "FARMLAND PATROL", objective = "Clear the bandits from over the fields and the factory town", arena = 3, difficulty = 0 },
            new CampaignMission { title = "LAKE SWEEP", objective = "Catch them up the lake before they reach the village", arena = 2, difficulty = 1 },
            new CampaignMission { title = "HIGH PASS", objective = "Hold the saddle where the railway crosses the mountains", arena = 1, difficulty = 2 },
            new CampaignMission { title = "PEAK MAZE", objective = "Hunt them through the canyons among the snowy peaks", arena = 0, difficulty = 3 },
            new CampaignMission { title = "SKYLINE CITY", objective = "Chase them down the river through the city of towers", arena = Arenas.CityIndex, difficulty = 3 },
            new CampaignMission { title = "CLOUD SEA", objective = "Hunt them through the towering clouds out over the open sea", arena = Arenas.CloudIndex, difficulty = 2 },
        };

        public static int Count => All.Length;

        public static CampaignMission Get(int i) => All[i >= 0 && i < All.Length ? i : 0];

        /// <summary>The first mission flown over an arena, or -1.</summary>
        public static int OfArena(int arena)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].arena == arena) return i;
            return -1;
        }

        public static string Difficulty(int d) => d <= 0 ? "EASY" : d == 1 ? "NORMAL" : d == 2 ? "HARD" : "ACE";
    }
}
