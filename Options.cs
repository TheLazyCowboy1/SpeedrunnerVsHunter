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

    [Config(GENERAL, "Log Level", "When this number is higher, less important logs are displayed."), LimitRange(0, 3)]
    public static int LogLevel = 1;

    [Config(GENERAL, "Test String", "This is a test", width = 150f)]
    public static string TestString = "Hi!";
    [Config(GENERAL, "Test ComboBox", "This is also a test", width = 150f, dropdownOptions = new string[] {"Option1", "Option2", "Option3"})]
    public static string TestString2 = "Hi!";

}
