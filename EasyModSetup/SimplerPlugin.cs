using BepInEx;
using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace EasyModSetup;

/// <summary>
/// Makes it easier to make new plugins.
/// Just pass your config options to the constructor (if you have any)
/// and override the ApplyHooks and RemoveHooks functions.
/// If you need to apply any hooks before mods are initialized, try overriding the Initialize function and adding such hooks there.
/// </summary>
[BepInDependency("henpemaz.rainmeadow", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("twofour2.rainReloader", BepInDependency.DependencyFlags.SoftDependency)]
public abstract class SimplerPlugin : BaseUnityPlugin
{
    #region VirtualMethods

    /// <summary>
    /// The higher this number is, the more logs will be shown.
    /// </summary>
    public virtual int LogLevel => 1;

    /// <summary>
    /// Called when the plugin is first awoken. Sometimes useful; depends on the project.
    /// </summary>
    public virtual void Initialize() { }

    /// <summary>
    /// Called immediately after mods are initialized, but ONLY IF hooks are unapplied.
    /// Add hooks like: On.Player.Jump += Player_Jump;
    /// </summary>
    public abstract void ApplyHooks();
    /// <summary>
    /// Called on OnDisable, but ONLY IF hooks were applied.
    /// Remove hooks like: On.Player.Jump -= Player_Jump;
    /// </summary>
    public abstract void RemoveHooks();

    public virtual void ModsApplied() { }

    #endregion

    #region PluginData

    public static string MOD_ID = "error";
    public static string MOD_NAME = "error";
    public static string MOD_VERSION = "error";
    public static string PluginPath = "error";
    public static bool RainMeadowEnabled = false;

    public static SimplerPlugin Instance;

    public static OptionInterface ConfigOptions;

    /// <param name="options">The mod config options. Set to null if your mod should not have a remix/config menu.</param>
    public SimplerPlugin(OptionInterface options) : base()
    {
        Instance = this;
        ConfigOptions = options;

        var data = this.Info.Metadata;
        MOD_ID = data.GUID;
        MOD_NAME = data.Name;
        MOD_VERSION = data.Version.ToString();
        Log("Plugin created");
    }

    #endregion

    #region Setup

    public void Awake()
    {
        //EasyExtEnum.Register();

        Initialize();
        Log("Plugin awoken");
    }

    private bool hooksApplied = false; //a hopefully pointless fail-safe
    public void OnEnable()
    {
        //EasyExtEnum.Register(); //to reflect hot-reload changes?

        On.RainWorld.OnModsInit += RainWorld_OnModsInit;

        //for using Rain Reloader (hot mod reloads), since it loads mods AFTER OnModsInit
        try
        {
            if (ModManager.ActiveMods.Any(m => m.id == MOD_ID))
            {
                CheckIfMeadowEnabled();
                ModsApplied();
                ApplyHooksIfNeeded();
                if (ConfigOptions != null)
                {
                    MachineConnector.SetRegisteredOI(MOD_ID, ConfigOptions);
                    MachineConnector.ReloadConfig(ConfigOptions);
                }
            }
        }
        catch (Exception ex) { Error(ex); }
    }

    public void OnDisable()
    {
        if (!hooksApplied) return;

        On.RainWorld.OnModsInit -= RainWorld_OnModsInit;
        AutoConfigOptions.RemoveHooks();

        try
        {
            //if (RainMeadowEnabled)
                //MeadowExt.RemoveHooks();
        } catch { }

        RemoveHooks();
        Log("Removed hooks");

        hooksApplied = false;
    }

    private void ApplyHooksIfNeeded()
    {
        if (hooksApplied) return;

        try
        {
            if (ConfigOptions is AutoConfigOptions)
                AutoConfigOptions.ApplyHooks();
        } catch (Exception ex) { Error(ex); }

        try
        {
            if (RainMeadowEnabled)
            {
                //AutoSync.RegisterSyncedVars();
                //MeadowExt.ApplyHooks();
            }
        }
        catch (Exception ex) { Error("Rain Meadow is apparently inactive: " + ex); RainMeadowEnabled = false; }

        ApplyHooks();
        hooksApplied = true;
        Log("Applied hooks");
    }
    private void CheckIfMeadowEnabled()
    {
        RainMeadowEnabled = ModManager.ActiveMods.Any(m => m.id == "henpemaz_rainmeadow");
        Log("Rain Meadow enabled: " + RainMeadowEnabled);
    }


    private void RainWorld_OnModsInit(On.RainWorld.orig_OnModsInit orig, RainWorld self)
    {
        orig(self);

        try
        {
            if (ConfigOptions != null)
                MachineConnector.SetRegisteredOI(MOD_ID, ConfigOptions); //register config menu

            PluginPath = ModManager.ActiveMods.Find(m => m.id == MOD_ID).path;

            CheckIfMeadowEnabled();
            ModsApplied();
            ApplyHooksIfNeeded();
        }
        catch (Exception ex) { Error(ex); }
    }

    #endregion

    #region Tools

    public static void Log(object o, int logLevel = 1, [CallerFilePath] string file = "", [CallerMemberName] string name = "", [CallerLineNumber] int line = -1)
    {
        if (Instance != null && logLevel <= Instance.LogLevel)
            Instance.Logger.LogDebug(LogText(o, file, name, line));
    }

    public static void Error(object o, [CallerFilePath] string file = "", [CallerMemberName] string name = "", [CallerLineNumber] int line = -1)
        => Instance?.Logger.LogError(LogText(o, file, name, line));

    //private static DateTime PluginStartTime = DateTime.Now;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string LogText(object o, string file, string name, int line)
    {
        try
        {
            return $"[{DateTime.Now.ToString("HH:mm:ss.ffffff")},{Path.GetFileName(file)}.{name}:{line}]: {o}";
        }
        catch (Exception ex)
        {
            Instance?.Logger.LogError(ex);
        }
        return o.ToString();
    }

    #endregion
}
