using EasyModSetup;

namespace SpeedrunnerVsHunter;

public class Options : AutoConfigOptions
{
    private const string GENERAL = "General";

    public Options() : base(new TabInfo[]
    {
        new(GENERAL)
    })
    {
        //LogLevel = 3; //temporarily enable all logs
    }

    //GENERAL

    //[Config(GENERAL, "Log Level", "When this number is higher, less important logs are displayed."), LimitRange(0, 3)]
    public static int LogLevel = 1;



    /**
     * I was going to add these configs, but I can't sync them without making the mod high-impact.
     * These options COULD still exist, but it would probably just cause chaos if the clients could decide their own spawn positions.
     */

    [Config(GENERAL, "Target Random Shelter Distance", "How far Hunters should spawn from the Speedrunner's shelter. Outskirts is a bit more than 1000 wide.", precision = 0), LimitRange(0, 30000)]
    public static float TargetRandomShelterDistance = 800f;

    [Config(GENERAL, "Random Shelter Randomness", "How randomly shelters are chosen. If this is 1, then any shelter (other than the host's) can be chosen with equal probability, so the Target Random Shelter Distance is completely ignored."), LimitRange(0, 1)]
    public static float RandomShelterRandomness = 1f;

}
