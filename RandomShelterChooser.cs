using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using Vector2 = UnityEngine.Vector2;

namespace SpeedrunnerVsHunter;

/// <summary>
/// Used to find the exact shelter at which to spawn.
/// </summary>
public static class RandomShelterChooser
{
    //public const float MINIMUM_SHELTER_DISTANCE = 500f; //regions are typically 2000-4000 wide, so this is pretty small

    /// <summary>
    /// Gets a shelter to respawn at!
    /// What is this mess... this sort is totally ridiculous;
    /// but it should work.
    /// Are you glad I didn't make you do this, lol?
    /// </summary>
    /// <param name="region">The region name.</param>
    /// <param name="slugcat">The slugcat.</param>
    /// <param name="teamShelters">A list of shelters assigned to other teams.</param>
    /// <param name="team">The team the player is on.</param>
    /// <param name="distanceLeniency">How far away the shelter CAN be. 0 = MUST be furthest possible shelter; 1 = ANY possible shelter.</param>
    /// <returns></returns>
    /// <exception cref="IndexOutOfRangeException">Thrown if not enough shelters in the region to support the number of teams.</exception>
    public static string GetRespawnShelter(string region, SlugcatStats.Name slugcat, string[] teamShelters, int team, float distanceLeniency = 0.5f, float targetDistance = 600, float mapBorderDistance = 100)
    {
        RandomShelterFilter.FindValidShelterPositions(region, slugcat);

        if (teamShelters.Length >= RandomShelterFilter.shelterNames.Length + RandomShelterFilter.secondaryShelterNames.Length)
            throw new IndexOutOfRangeException("There are more team shelters than there are shelters in the region!!!!");

        List<Vector2> shelterLocs = new();
        foreach (var s in teamShelters)
        {
            int idx = Array.IndexOf(RandomShelterFilter.shelterNames, s);
            if (idx >= 0) shelterLocs.Add(RandomShelterFilter.shelterPositions[idx]);
            else
            {
                idx = Array.IndexOf(RandomShelterFilter.secondaryShelterNames, s);
                if (idx >= 0) shelterLocs.Add(RandomShelterFilter.secondaryShelterPositions[idx]);
                else throw new KeyNotFoundException($"Cannot find position of shelter {s}!");
            }
        }

        string myShelter = team >= teamShelters.Length ? null : teamShelters[team];

        IEnumerable<(string shelter, Vector2 pos)> unorderedShelters = RandomShelterFilter.shelterNames
            .Select((n, i) => (n, RandomShelterFilter.shelterPositions[i]));
            //.Where(kvp => myShelter == null || kvp.n == myShelter || RoomBlacklister.InBounds(kvp.Item2, shelterLocs, mapBorderDistance)); //don't allow shelters out of range when respawning

        //optionally add secondary shelters, if necessary
        if (teamShelters.Length >= RandomShelterFilter.shelterNames.Length)
        {
            unorderedShelters = unorderedShelters.Concat(
                    RandomShelterFilter.secondaryShelterNames
                    .Select((n, i) => (n, RandomShelterFilter.secondaryShelterPositions[i]))
                //.Where(kvp => myShelter == null || kvp.n == myShelter || RoomBlacklister.InBounds(kvp.Item2, shelterLocs, mapBorderDistance)) //don't allow shelters out of range when respawning
                );
        }
        unorderedShelters = unorderedShelters
            .Where(kvp => kvp.shelter == myShelter || !teamShelters.Contains(kvp.shelter)) //don't spawn in other teams' shelters!!!
            .OrderBy(kvp => UnityEngine.Random.value); //shuffle list

        if (team < teamShelters.Length)
            shelterLocs.RemoveAt(team); //only consider other teams' shelters

        //manual sort... :(
        List<(string shelter, float score)> orderedShelters = new(unorderedShelters.Count());
        foreach (var s in unorderedShelters)
        {
            //float score = MIN_DISTANCE(s.Item2, otherShelterLocs) - (RandomShelterFilter.PENALIZED_SHELTERS.Contains(s.n) ? 100000000 : 0); //higher score = better
            float score = ShelterScore(s.pos, shelterLocs, targetDistance) - (RandomShelterFilter.PENALIZED_SHELTERS.Contains(s.shelter) ? 100000000 : 0); //higher score = better
            int idx = orderedShelters.FindIndex(os => os.score < score); //index of first shelter with a worse score
            if (idx < 0) orderedShelters.Add((s.shelter, score)); //this is the worst; add to the end
            else orderedShelters.Insert(idx, (s.shelter, score)); //insert in front of worse shelter
        }

        var shelterArr = orderedShelters.Select(s => s.shelter).ToArray();
        //RainMeadow.RainMeadow.Debug($"[CTP]: Choosing top {distanceLeniency} of shelters: {string.Join(", ", shelterArr)}");
        Plugin.Log($"Choosing top {distanceLeniency} of shelters: {string.Join(", ", orderedShelters.Select(s => $"({s.Item1},{s.Item2})").ToArray())}");

        int randomIdx = Mathf.FloorToInt(UnityEngine.Random.value * shelterArr.Length * distanceLeniency); //find a random shelter in the BEST FIRST HALF
        if (randomIdx >= shelterArr.Length) randomIdx = shelterArr.Length - 1;

        return shelterArr[randomIdx];
    }

    private static float MIN_DISTANCE(Vector2 a, List<Vector2> b)
    {
        if (b.Count < 1) return 0;
        float dist = float.PositiveInfinity;
        foreach (Vector2 v in b) dist = Mathf.Min(dist, (a - v).SqrMagnitude());
        return dist;
    }
    private static float MAX_DISTANCE(Vector2 a, List<Vector2> b)
    {
        if (b.Count < 1) return 0;
        float dist = 0;
        foreach (Vector2 v in b) dist = Mathf.Min(dist, (a - v).SqrMagnitude());
        return dist;
    }
    private static float ShelterScore(Vector2 shelterPos, List<Vector2> otherShelters, float targetDistance)
    {
        if (otherShelters.Count < 1) return 0;
        float total = 0;
        foreach (Vector2 v in otherShelters)
        {
            float dist = Vector2.Distance(shelterPos, v);
            if (dist > targetDistance)
            {
                total -= (dist - targetDistance) * (dist - targetDistance); //penalize the excess
                dist = targetDistance; //cap the benefit
            }
            total += dist * dist;
        }
        return total;
    }
}

/// <summary>
/// Used to find all valid shelters in the region.
/// Don't use this; use RandomShelterChooser if possible.
/// </summary>
public static class RandomShelterFilter
{
    public static string[] BANNED_SHELTERS = new string[] //put shelter names here, of course. Like SB_S03 or something
    {
        /*
        "SB_S09", //top of Sub ravine
        "SU_S05", //OE shelter; accessible only to Saint
        "OE_S03", //the shelter near SU
        "SH_S11", //near SH gate; accessible only to a few slugcats
        "SL_STOP", //above Moon
        "SL_S15", //MS gate; extremely hard to access
        "SL_S13", //precipice
        "MS_S01", //Submerged Superstructure blocked; only Bitter Aerie allowed
        "MS_LAB5", //^^^
        "MS_S03",
        "MS_S04",
        "MS_S05",
        "MS_S06",
        "MS_S09",
        "RM_S04", //top of the Rot; mostly inaccessible
        "HI_WS01" //Hydroponics shelter
        */
    };

    public static string[] PENALIZED_SHELTERS = new string[] //shelters that should be used ONLY as a last resort
    {
        /*
        "DS_S03", //in SB gate; a long linear path to get to it; easy to guard, hard to escape
        "UG_S03", //^^^
        "LF_S04", //in SB gate; just really far away
        "SS_S04", //near SS_UW gate; again: long, linear path
        "GW_S09", //near SH gate; again: far removed; guarded by a scav toll
        "VS_S02" //near SL gate; way too far away from other shelters
        */
    };

    public static string[] ALTERNATIVE_SHELTERS = new string[] //used if there aren't enough shelters for every team
    {
        /*
        //"WSKB_C06", //this one spawns inside a wall, sadly
        "WSKB_C05", //my 4th choice of room... the C07 drops you into the void, 06 and 12 is in a wall; 15 is literally the big middle room
        "WSKB_C15", //extra Sunlit Port rooms to act as shelters
        "WSKB_C08",
        "WSUR_A02" //Crumbling Fringes room
        */
    };

    public static string[] shelterNames = new string[0];
    public static Vector2[] shelterPositions = new Vector2[0];

    public static string[] secondaryShelterNames = new string[0];
    public static Vector2[] secondaryShelterPositions = new Vector2[0];

    private static string lastSearchedRegion = "";
    private static SlugcatStats.Name lastSearchedSlugcat = null;

    public static void FindValidShelterPositions(string region, SlugcatStats.Name slugcat)
    {
        if (lastSearchedRegion == region && lastSearchedSlugcat == slugcat)
            return; //there's nothing to be done! lucky you!

        string filePath = FindRegionFile(region);
        if (!File.Exists(filePath)) throw new FileNotFoundException($"Failed to find file for {region}");

        //get region shelter list
        var shelters = FindRegionShelters(filePath, slugcat.value);
        var shelters2 = ALTERNATIVE_SHELTERS.Where(s => s.StartsWith(region)).ToList();

        //blacklist unaccessible shelters
        BlacklistShelters(shelters, filePath, slugcat.value);
        BlacklistShelters(shelters2, filePath, slugcat.value);
        //shelterNames = shelters.ToArray();

        //get map positions
        string mapPath = FindMapFile(region, slugcat.value);
        if (!File.Exists(mapPath)) throw new FileNotFoundException($"Failed to find map file for {region}");
        shelterPositions = FindShelterPositions(shelters, mapPath);
        shelterNames = shelters.ToArray();
        secondaryShelterPositions = FindShelterPositions(shelters2, mapPath);
        secondaryShelterNames = shelters2.ToArray();

        lastSearchedRegion = region;
        lastSearchedSlugcat = slugcat;
    }

    private static List<string> FindRegionShelters(string path, string slugcat)
    {
        List<string> shelters = new();

        string[] lines = File.ReadAllLines(path);

        bool roomStartFound = false;
        foreach (string line in lines)
        {
            if (!roomStartFound) //block start
            {
                if (line.StartsWith("ROOMS"))
                    roomStartFound = true;
            }
            else if (line.StartsWith("END ")) //block end
                break;
            else //actual line, hopefully
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                string l = line.Trim();
                if (l.StartsWith("//")) continue; //don't process comments

                string[] data = Regex.Split(l, " : ");
                if (!shelters.Contains(data[0]) && data[data.Length - 1] == "SHELTER" || data[data.Length - 1] == "ANCIENTSHELTER")
                    shelters.Add(data[0]);
            }
        }

        return shelters;
    }

    private static void BlacklistShelters(List<string> shelters, string path, string slugcat)
    {
        //start with manual blacklist!
        foreach (string s in BANNED_SHELTERS) shelters.Remove(s);

        string[] lines = File.ReadAllLines(path);

        bool conditionalFound = false;
        foreach (string line in lines)
        {
            if (!conditionalFound) //block start
            {
                if (line.StartsWith("CONDITIONAL"))
                    conditionalFound = true;
            }
            else if (line.StartsWith("END ")) //block end
                break;
            else //actual line, hopefully
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                string l = line.Trim();
                if (l.StartsWith("//")) continue; //don't process comments

                string[] data = Regex.Split(l, " : ");
                if (data.Length < 3) continue; //invalid data; who knows what weird line this might be!!

                if (shelters.Contains(data[2])) //potentially blacklist it!
                {
                    if (data[0] == slugcat && data[1] == "HIDEROOM") shelters.Remove(data[2]);
                    if (data[0] != slugcat && data[1] == "EXCLUSIVEROOM") shelters.Remove(data[2]);
                }
            }
        }
    }

    private static Vector2[] FindShelterPositions(List<string> shelterNames, string path)
    {
        Vector2[] shelterLocs = new Vector2[shelterNames.Count];

        string[] lines = File.ReadAllLines(path);

        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith("Connection:"))
                break;

            string l = line.Trim();
            if (l.StartsWith("//")) continue; //don't process comments; although there shouldn't be any in map files!!!

            string[] nameData = l.Split(':');
            int idx = shelterNames.IndexOf(nameData[0]);
            if (idx < 0) continue;

            string data = nameData[1].Trim();
            string[] d = Regex.Split(data, "><");
            if (d.Length < 2) continue; //invalid data, somehow?
            if (float.TryParse(d[0], out var x) && float.TryParse(d[1], out var y))
                shelterLocs[idx] = new Vector2(x, y);
        }

        //blacklist shelters that weren't found
        for (int i = shelterLocs.Length-1; i > 0; i--)
        {
            if (shelterLocs[i] == null)
            {//if location not found
                RainMeadow.RainMeadow.Error("[CTP]: Could not find location of shelter " + shelterNames[i]);
                shelterNames.RemoveAt(i);
            }
        }

        return shelterLocs.Where(vec => vec != null).ToArray();
    }

    private static string FindRegionFile(string region)
    {
        return AssetManager.ResolveFilePath(Path.Combine("world", region, "world_" + region + ".txt"));
    }
    private static string FindMapFile(string region, string slugcat)
    {
        string path = AssetManager.ResolveFilePath(Path.Combine("world", region, "map_" + region + "-" + slugcat + ".txt"));
        if (File.Exists(path)) return path;
        return AssetManager.ResolveFilePath(Path.Combine("world", region, "map_" + region + ".txt"));
    }
}
