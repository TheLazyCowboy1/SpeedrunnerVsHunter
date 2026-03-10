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

    #region HooksSetup

    private static System.Reflection.BindingFlags defaultFlags =
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
        | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
    public override void ApplyHooks()
    {
        On.OverWorld.WorldLoaded += OverWorld_WorldLoaded;

        canJoinHook = new Hook(
            typeof(StoryGameMode).GetProperty(nameof(StoryGameMode.canJoinGame)).GetGetMethod(),
            StoryGameMode_canJoinGame
            );
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

    private Hook canJoinHook, saveStateHook, menuHook;

    public override void RemoveHooks()
    {
        On.OverWorld.WorldLoaded -= OverWorld_WorldLoaded;
        canJoinHook?.Undo();
        saveStateHook?.Undo();
        menuHook?.Undo();
        On.Menu.SlugcatSelectMenu.GetChecked -= SlugcatSelectMenu_GetChecked;
        On.Menu.SlugcatSelectMenu.SetChecked -= SlugcatSelectMenu_SetChecked;
    }

    #endregion

    #region ChangingWorldRPCs

    private static bool lobbyOwnerHasMod = false;
    private static OnlinePlayer lastAskedLobbyOwner = null;

    /// <summary>
    /// Run if a player does in fact have this mod. This function will update lobbyOwnerHasMod and lastAskedLobbyOwner if necessary.
    /// </summary>
    /// <param name="player">The player who has this mod.</param>
    public static void PlayerHasMod(OnlinePlayer player)
    {
        if (OnlineManager.lobby != null && player == OnlineManager.lobby.owner)
        {
            if (!lobbyOwnerHasMod || player != lastAskedLobbyOwner) //only log it when relevant
                Log($"Lobby owner {player.id.DisplayName} has this mod");
            lastAskedLobbyOwner = player;
            lobbyOwnerHasMod = true;
        }
    }

    /// <summary>
    /// Clearing myLastDenPos causes me to use the host's denPos (gameMode.defaultDenPos) instead of my own.
    /// </summary>
    [Obsolete("Clients now just read defaultDenPos from state, since it actually is in the state somewhere")]
    [SoftRPCMethod] //apparently SoftRPCs still take RPCEvents, although they should actually be instanced of type SoftRPCEvent (so ev as SoftRPCEvent should work)
    public static void ClearMyLastDenPos(RPCEvent ev)//, string newDenPos)
    {
        if (RainMeadow.RainMeadow.isStoryMode(out StoryGameMode gameMode))
        {
            //gameMode.myLastDenPos = newDenPos; //set my den pos
            gameMode.myLastDenPos = null; //use the host's denPos
            gameMode.myLastWarp = null; //don't try to spawn in at a warp
            ev.Resolve(new GenericResult.Ok());
            Log("Cleared myLastDenPos, per request from " + ev.from.id.DisplayName);

            //no need to ask if host has this mod, because he just sent me an RPC from this mod!
            PlayerHasMod(ev.from);
        }
        else
            ev.Resolve(new GenericResult.Fail());
    }

    [SoftRPCMethod]
    public static void AskHasModRPC(RPCEvent ev)
    {
        ev.Resolve(new GenericResult.Ok()); //yup, I got this mod!
    }

    public static void AskIfPlayerHasMod(OnlinePlayer player)
    {
        lastAskedLobbyOwner = player; //set to asked, so that we don't ask again
        lobbyOwnerHasMod = false; //assume the result is negative until we hear otherwise
        lastAskedLobbyOwner.InvokeRPC(AskHasModRPC)
            .Then(result =>
            {
                if (result is GenericResult.Ok)
                    PlayerHasMod(player);
                else
                    Log($"Player {player.id.DisplayName} does NOT have this mod");
            });
        Log($"Asking player {player.id.DisplayName} whether he has this mod");
    }

    #endregion

    #region ChangingWorldHooks

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

            gameMode.region = self.activeWorld.name; //update region; not sure why Meadow doesn't do this already

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
                            //gameMode.myLastDenPos = room.name; //this doesn't do anything for clients
                            gameMode.defaultDenPos = room.name; //WHY ARE NONE OF THE DEN FIELDS ACTUALLY SYNCED???? clients receive this value but do not copy it over!??
                            foundNewDen = true;
                        }
                    }
                }
            }
            catch (Exception ex) { Error(ex); }

            if (foundNewDen) //don't change the den if we didn't find any
            {
                //gameMode.region = self.activeWorld.name; //update region; not sure why Meadow doesn't do this already
                //gameMode.changedRegions = false; //we can't do this, because what if a client doesn't have this mod and tries to load into wrong region?
                //gameMode.readyForTransition = StoryGameMode.ReadyForTransition.Closed; //actually don't change this; Meadow will change this to Closed on its own at the right time
                
                Log("Closest den in region: " + gameMode.defaultDenPos);
                return; //no need to do anything more here?

                //change myLastDenPos for ALL current players in the lobby
                foreach (OnlinePlayer player in OnlineManager.players)
                {
                    if (player.isMe) continue; //don't invoke RPC to myself, duh

                    player.InvokeRPC(ClearMyLastDenPos); //try using a SoftRPC
                        /*.Then(result =>
                        {
                            if (result is not GenericResult.Ok) //if it fails, use GoToWinScreen (not ideal but it should work)
                                player.InvokeRPC(StoryRPCs.GoToWinScreen, false, false, gameMode.defaultDenPos, null);
                        //THIS ONLY WORKS IF THE PLAYER IS IN THE GAME; IN WHICH CASE, WE DON'T WANT IT TO CHANGE!!!
                        });*/
                }
            }
            else
                Error("Could not find any den in region " + self.activeWorld.name);
        }
        catch (Exception ex) { Error(ex); }
    }

    /// <summary>
    /// If the host has this mod and changes regions, we get a new shelter to use when joining the game.
    /// HOWEVER, if I do NOT have this mod and host changes regions, then I MIGHT (sometimes; not always) try to load into the wrong region.
    /// So I must first ask if the host has the mod, and then I can join.
    /// </summary>
    private bool StoryGameMode_canJoinGame(Func<StoryGameMode, bool> orig, StoryGameMode self)
    {
        if (self.changedRegions)
        {
            if (lastAskedLobbyOwner == null || lastAskedLobbyOwner != self.lobby.owner) //we haven't asked for permission yet
            {
                AskIfPlayerHasMod(self.lobby.owner);
            }
            else if (lobbyOwnerHasMod) //lobby owner has mod, so act as if changedRegions is always false
            {
                self.changedRegions = false;
                bool ret = orig(self);
                self.changedRegions = true;
                return ret;
            }
        }

        return orig(self);
    }

    private void MyLastDenPosFix(StoryGameMode storyGameMode)
    {
        try
        {
            if (!storyGameMode.lobby.isOwner) //don't mess with the host's save
            {
                //changedRegions is always false when starting the game, apparently
                //if (storyGameMode.changedRegions && lobbyOwnerHasMod && storyGameMode.lobby.owner == lastAskedLobbyOwner)
                if (lobbyOwnerHasMod && storyGameMode.lobby.owner == lastAskedLobbyOwner) //if host does not have this mod, defaultDenPos may be inaccurate
                {
                    //dig up defaultDenPos out of the abyss we call the state data structure. It IS THERE and WE DO HAVE IT; we just have to dig it up!
                    //lobby => lobbyState => lobbyResourceDataStates => StoryLobbyData.State => defaultDenPos
                    storyGameMode.myLastDenPos = (storyGameMode.lobby.latestState.resourceDataStates.list.Find(s => s is StoryLobbyData.State) as StoryLobbyData.State).defaultDenPos;
                    Log("Found defaultDenPos in StoryLobbyData.State: " + storyGameMode.myLastDenPos);
                }
                else
                {
                    storyGameMode.myLastDenPos = null; //clients always clear myLastDenPos; it only causes problems
                    Log("Set myLastDenPos to null, because that annoying thing only causes problems.");
                }
            }
        }
        catch (Exception ex) { Error(ex); }
    }

    #endregion

    #region RandomizeShelterHooks

    public static bool RandomizeStartingShelter = false;

    private const string RANDOMIZE_SHELTERS_ID = "RANDOMIZESHELTER";
    private CheckBox RandomShelterCheckbox = null;

    /// <summary>
    /// pre-fix = use defaultDenPos if we have changed regions; otherwise use the saveState's den.
    /// post-fix = if RandomizeStartingShelter, randomize the spawn den location.
    /// </summary>
    private void RainMeadow_SaveStateHandler(Action<RainMeadow.RainMeadow, PlayerProgression, StoryGameMode, RainWorldGame> orig, RainMeadow.RainMeadow realSelf, PlayerProgression self, StoryGameMode storyGameMode, RainWorldGame game)
    {
        MyLastDenPosFix(storyGameMode);

        orig(realSelf, self, storyGameMode, game);

        try
        {
            if (RandomShelterCheckbox != null)
            {
                RandomizeStartingShelter = RandomShelterCheckbox.Checked; //read it
                RandomShelterCheckbox.RemoveSprites(); //remove it
                RandomShelterCheckbox = null; //destroy it
                Log("RandomizeStartingShelter = " + RandomizeStartingShelter);
            }
            if (!RandomizeStartingShelter)
                return;

            string den = self.currentSaveState.warpPointTargetAfterWarpPointSave?.destRoom ?? self.currentSaveState.denPosition; //use last warp if it exists; hopefully it doesn't
            try
            { //find any shelter except the current one
                self.currentSaveState.denPosition = RandomShelterChooser.GetRespawnShelter(den.Split('_')[0], self.currentSaveState.saveStateNumber, new string[] { den }, 1, 1f, 1000f, 10000f);
            } catch (KeyNotFoundException ex)
            {
                Log("Cannot find position of lobby spawn shelter: ERROR: " + ex.Message);
                //try again; this time just to find ANY random shelter
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
