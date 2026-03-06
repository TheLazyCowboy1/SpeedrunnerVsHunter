using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Security.Permissions;
using BepInEx;
using EasyModSetup;
using Menu;
using MonoMod.RuntimeDetour;
using RainMeadow;
using UnityEngine;

#pragma warning disable CS0618

[module: UnverifiableCode]
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]

namespace SpeedrunnerVsHunter;

[BepInDependency("henpemaz.rainmeadow", BepInDependency.DependencyFlags.HardDependency)]
[BepInPlugin("LazyCowboy.SpeedrunnerVsHunter", "SpeedrunnerVsHunter", "0.0.1")]
public class Plugin : SimplerPlugin
{

    #region Setup
    public override int LogLevel => Options.LogLevel;

    public Plugin() : base(null)//base(new Options()) //no config menu for now
    {
    }


    #endregion

    public static bool RandomizeStartingShelter = false;

    private const string RANDOMIZE_SHELTERS_ID = "RANDOMIZESHELTER";
    private CheckBox RandomShelterCheckbox = null;

    #region Hooks

    private static System.Reflection.BindingFlags defaultFlags =
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
        | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
    public override void ApplyHooks()
    {
        On.OverWorld.WorldLoaded += OverWorld_WorldLoaded;

        saveStateHook = new Hook(
            typeof(RainMeadow.RainMeadow).GetMethod(nameof(RainMeadow.RainMeadow.SaveStateHandler), defaultFlags),
            RainMeadow_SaveStateHandler
            );
        menuHook = new Hook(
            typeof(StoryOnlineMenu).GetConstructors(defaultFlags)[0],
            StoryOnlineMenu_ctor
            );

        On.Menu.SlugcatSelectMenu.GetChecked += SlugcatSelectMenu_GetChecked;
        On.Menu.SlugcatSelectMenu.SetChecked += SlugcatSelectMenu_SetChecked;
    }

    private Hook saveStateHook, menuHook;

    public override void RemoveHooks()
    {
        On.OverWorld.WorldLoaded -= OverWorld_WorldLoaded;
        saveStateHook?.Undo();
        menuHook?.Undo();
        On.Menu.SlugcatSelectMenu.GetChecked -= SlugcatSelectMenu_GetChecked;
        On.Menu.SlugcatSelectMenu.SetChecked -= SlugcatSelectMenu_SetChecked;
    }

    //when changing regions, update the StoryGameMode flags so that we still let new players join
    private void OverWorld_WorldLoaded(On.OverWorld.orig_WorldLoaded orig, OverWorld self, bool warpUsed)
    {
        string oldRegion = self.activeWorld?.name;
        AbstractRoom myRoom = self.reportBackToGate?.room.abstractRoom ?? (self.specialWarpCallback as Watcher.WarpPoint)?.room.abstractRoom;

        orig(self, warpUsed);

        try
        {
            if (OnlineManager.lobby == null || !OnlineManager.lobby.isOwner || !RainMeadow.RainMeadow.isStoryMode(out StoryGameMode gameMode))
                return;
            if (oldRegion == null || self.activeWorld.name == oldRegion)
            {
                Log($"Didn't change regions. Old region={oldRegion}, activeWorld.name={self.activeWorld.name}");
                return;
            }

            bool foundNewDen = false;
            //change myLastDenPos
            try
            {
                if (myRoom == null) //if we don't have the current room
                {
                    foreach (var player in self.game.Players) //loop through players
                    {
                        if (player != null && player.world == self.activeWorld && player.realizedCreature != null) //use the room of the first realized player
                        {
                            myRoom = player.Room;
                            break;
                        }
                    }

                    Error($"Gate/warp room for world {self.activeWorld.name} is null! Using first realized player room: {myRoom?.name}");
                }

                Vector2 myPos = myRoom.mapPos;
                float bestScore = float.PositiveInfinity;
                foreach (AbstractRoom room in self.activeWorld.abstractRooms)
                {
                    if (room.shelter && !self.activeWorld.DisabledMapIndices.Contains(room.index) && room.mapPos != Vector2.zero) //must actually have a defined map pos
                    {
                        float score = (room.mapPos - myPos).sqrMagnitude;
                        if (score < bestScore)
                        {
                            bestScore = score;
                            gameMode.myLastDenPos = room.name;
                            foundNewDen = true;
                        }
                    }
                }
            }
            catch (Exception ex) { Error(ex); }

            if (foundNewDen) //don't change the den if we didn't find any
            {
                gameMode.region = self.activeWorld.name;
                gameMode.changedRegions = false; //lie and say we didn't change regions
                gameMode.readyForTransition = StoryGameMode.ReadyForTransition.Closed;
                Log("Closest den in region: " + gameMode.myLastDenPos);
            }
            else
                Error("Could not find any den in region " + self.activeWorld.name);
        }
        catch (Exception ex) { Error(ex); }
    }

    //if RandomizeStartingShelter, randomize the spawn den location
    private void RainMeadow_SaveStateHandler(Action<RainMeadow.RainMeadow, PlayerProgression, StoryGameMode, RainWorldGame> orig, RainMeadow.RainMeadow realSelf, PlayerProgression self, StoryGameMode storyGameMode, RainWorldGame game)
    {
        orig(realSelf, self, storyGameMode, game);

        try
        {
            if (RandomShelterCheckbox != null)
            {
                RandomizeStartingShelter = RandomShelterCheckbox.Checked; //read it
                RandomShelterCheckbox.RemoveSprites(); //erase it
                RandomShelterCheckbox = null; //destroy it
                Log("RandomizeStartingShelter = " + RandomizeStartingShelter);
            }
            if (!RandomizeStartingShelter)
                return;

            string den = self.currentSaveState.denPosition;
            try
            { //find any shelter except the current one
                self.currentSaveState.denPosition = RandomShelterChooser.GetRespawnShelter(den.Split('_')[0], self.currentSaveState.saveStateNumber, new string[] { den }, 1, 1f, 1000f, 10000f);
            } catch (KeyNotFoundException ex)
            {
                Log(ex); //try again; this time just to find ANY random shelter
                self.currentSaveState.denPosition = RandomShelterChooser.GetRespawnShelter(den.Split('_')[0], self.currentSaveState.saveStateNumber, new string[0], 0, 1f, 1000f, 10000f);
            }
            self.currentSaveState.warpPointTargetAfterWarpPointSave = null; //don't spawn at the warp!

            Log("Chose random respawn shelter: " + self.currentSaveState.denPosition);
        }
        catch (Exception ex) { Error(ex); }
    }

    //create the Random Shelter checkbox
    private void StoryOnlineMenu_ctor(Action<StoryOnlineMenu, ProcessManager> orig, StoryOnlineMenu self, ProcessManager manager)
    {
        orig(self, manager);

        try
        {
            if (self.ID != RainMeadow.RainMeadow.Ext_ProcessID.StoryMenu) //don't do it for custom gamemodes based off of Story, like Capture the Pearl
                return;

            RandomShelterCheckbox?.RemoveSprites(); //whatcha still doing alive??? Get outta here

            //add checkbox just below restartCheckbox
            RandomShelterCheckbox = new(self, self.pages[0], self, self.restartCheckboxPos + new Vector2(0, 70f), 95f, self.Translate("Random Shelter"), RANDOMIZE_SHELTERS_ID, false);
            //RandomizeSheltersCheckbox.Checked = RandomizeStartingShelter;
            self.pages[0].subObjects.Add(RandomShelterCheckbox);

            Log("Added RandomizeSheltersCheckbox");
        }
        catch (Exception ex) { Error(ex); }
    }


    //WHYYYYYYYYY
    private bool SlugcatSelectMenu_GetChecked(On.Menu.SlugcatSelectMenu.orig_GetChecked orig, SlugcatSelectMenu self, CheckBox box)
    {
        if (box.IDString == RANDOMIZE_SHELTERS_ID) return RandomizeStartingShelter;
        return orig(self, box);
    }

    private void SlugcatSelectMenu_SetChecked(On.Menu.SlugcatSelectMenu.orig_SetChecked orig, SlugcatSelectMenu self, CheckBox box, bool c)
    {
        if (box.IDString == RANDOMIZE_SHELTERS_ID)
        {
            RandomizeStartingShelter = c;
            return;
        }
        orig(self, box, c);
    }

    #endregion

}
