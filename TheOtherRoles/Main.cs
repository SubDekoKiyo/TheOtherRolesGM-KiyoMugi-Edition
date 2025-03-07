﻿global using UnhollowerBaseLib;
global using UnhollowerBaseLib.Attributes;
global using UnhollowerRuntimeLib;

using BepInEx;
using BepInEx.Configuration;
using BepInEx.IL2CPP;
using HarmonyLib;
using Hazel;
using System.Collections.Generic;
using System.Linq;
using System;
using System.IO;
using UnityEngine;
using TheOtherRoles.Modules;
using TheOtherRoles.Utilities;
using TheOtherRoles.Patches;

namespace TheOtherRoles
{
    [BepInPlugin(Id, "The Other Roles GM", VersionString)]
    [BepInDependency(SubmergedCompatibility.SUBMERGED_GUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInProcess("Among Us.exe")]
    public class TheOtherRolesPlugin : BasePlugin
    {
        public const string Id = "kiyomori.mugicha.theotherrolesgmkm";
        public const string VersionString = "3.1.2";
        public static System.Version Version = System.Version.Parse(VersionString);
        internal static BepInEx.Logging.ManualLogSource Logger;

        public Harmony Harmony { get; } = new Harmony(Id);
        public static TheOtherRolesPlugin Instance;

        public static int optionsPage = 1;

        public static ConfigEntry<bool> DebugMode { get; private set; }
        public static ConfigEntry<bool> GhostsSeeTasks { get; set; }
        public static ConfigEntry<bool> GhostsSeeRoles { get; set; }
        public static ConfigEntry<bool> GhostsSeeVotes { get; set; }
        public static ConfigEntry<bool> ShowRoleSummary { get; set; }
        public static ConfigEntry<bool> HideNameplates { get; set; }
        public static ConfigEntry<bool> ShowLighterDarker { get; set; }
        public static ConfigEntry<bool> HideTaskArrows { get; set; }
        //public static ConfigEntry<bool> ShowDebugData { get; set; }
        public static ConfigEntry<bool> EnableHorseMode { get; set; }
        public static ConfigEntry<bool> StreamerMode { get; set; }
        public static ConfigEntry<string> StreamerModeReplacementText { get; set; }
        public static ConfigEntry<string> StreamerModeReplacementColor { get; set; }
        public static ConfigEntry<string> Ip { get; set; }
        public static ConfigEntry<ushort> Port { get; set; }
        public static ConfigEntry<string> DebugRepo { get; private set; }
        public static ConfigEntry<string> ShowPopUpVersion { get; set; }
        public static Sprite ModStamp;

        public static IRegionInfo[] defaultRegions;
        public static void UpdateRegions()
        {
            ServerManager serverManager = FastDestroyableSingleton<ServerManager>.Instance;
            IRegionInfo[] regions = defaultRegions;
            var CustomRegion = new DnsRegionInfo(Ip.Value, "Custom", StringNames.NoTranslation, Ip.Value, Port.Value, false);
            regions = regions.Concat(new IRegionInfo[] { CustomRegion.CastFast<IRegionInfo>() }).ToArray();
            ServerManager.DefaultRegions = regions;
            serverManager.AvailableRegions = regions;
        }

        public override void Load()
        {
            ModTranslation.Load();
            Logger = Log;

            DebugMode = Config.Bind("Custom", "Enable Debug Mode", false);
            StreamerMode = Config.Bind("Custom", "Enable Streamer Mode", false);
            GhostsSeeTasks = Config.Bind("Custom", "Ghosts See Remaining Tasks", true);
            GhostsSeeRoles = Config.Bind("Custom", "Ghosts See Roles", true);
            GhostsSeeVotes = Config.Bind("Custom", "Ghosts See Votes", true);
            ShowRoleSummary = Config.Bind("Custom", "Show Role Summary", true);
            HideNameplates = Config.Bind("Custom", "Hide Nameplates", false);
            ShowLighterDarker = Config.Bind("Custom", "Show Lighter / Darker", false);
            HideTaskArrows = Config.Bind("Custom", "Hide Task Arrows", false);
            //ShowDebugData = Config.Bind("Custom", "Show Debug Data", false);
            EnableHorseMode = Config.Bind("Custom", "Enable Horse Mode", false);
            ShowPopUpVersion = Config.Bind("Custom", "Show PopUp", "0");
            DebugRepo = Config.Bind("Custom", "Debug Hat Repo", "");
            StreamerModeReplacementText = Config.Bind("Custom", "Streamer Mode Replacement Text", "\n\nTheOtherRolesGM KM");
            StreamerModeReplacementColor = Config.Bind("Custom", "Streamer Mode Replacement Text Hex Color", "#87AAF5FF");

            Ip = Config.Bind("Custom", "Custom Server IP", "127.0.0.1");
            Port = Config.Bind("Custom", "Custom Server Port", (ushort)22023);
            defaultRegions = ServerManager.DefaultRegions;

            UpdateRegions();

            GameOptionsData.RecommendedImpostors = Enumerable.Repeat(3, 16).ToArray();
            GameOptionsData.MaxImpostors = Enumerable.Repeat(15, 16).ToArray(); // Max Imp = Recommended Imp = 3
            GameOptionsData.MinPlayers = Enumerable.Repeat(4, 15).ToArray(); // Min Players = 4

            DebugMode = Config.Bind("Custom", "Enable Debug Mode", false);
            Instance = this;
            CustomOptionHolder.Load();
            CustomColors.Load();
            RandomGeneratorPatch.Initialize();

            Patches.FreeNamePatch.Initialize();
            SubmergedCompatibility.Initialize();
            Patches.SubmergedPatch.Patch();
            Harmony.PatchAll();
        }

        public static Sprite GetModStamp()
        {
            if (ModStamp) return ModStamp;
            return ModStamp = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.ModStamp.png", 150f);
        }
    }

    // Deactivate bans, since I always leave my local testing game and ban myself
    [HarmonyPatch(typeof(StatsManager), nameof(StatsManager.AmBanned), MethodType.Getter)]
    public static class AmBannedPatch
    {
        public static void Postfix(out bool __result)
        {
            __result = false;
        }
    }

    [HarmonyPatch(typeof(ChatController), nameof(ChatController.Awake))]
    public static class ChatControllerAwakePatch
    {
        private static void Prefix()
        {
            if (!EOSManager.Instance.isKWSMinor)
            {
                SaveManager.chatModeType = 1;
                SaveManager.isGuest = false;
            }
        }
    }

    // Debugging tools
    [HarmonyPatch(typeof(KeyboardJoystick), nameof(KeyboardJoystick.Update))]
    //DebugModeがONの時使用可
    public static class DebugManager
    {
        private static readonly System.Random random = new System.Random((int)DateTime.Now.Ticks);
        private static List<PlayerControl> bots = new List<PlayerControl>();

        public static void Postfix(KeyboardJoystick __instance)
        {
            // F10でクルー強制勝利
            if (Input.GetKeyDown(KeyCode.F10) && AmongUsClient.Instance.GameState == InnerNet.InnerNetClient.GameStates.Started && AmongUsClient.Instance.AmHost)
            {
                MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.CrewmateEnd, Hazel.SendOption.Reliable, -1);
                AmongUsClient.Instance.FinishRpcImmediately(writer);
                RPCProcedure.crewmateEnd();
                Modules.Logger.SendInGame("ForceEnd(Crewmate Win)");
            }
            // F11でクルー強制勝利
            if (Input.GetKeyDown(KeyCode.F11) && AmongUsClient.Instance.GameState == InnerNet.InnerNetClient.GameStates.Started && AmongUsClient.Instance.AmHost)
            {
                MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.ImpostorEnd, Hazel.SendOption.Reliable, -1);
                AmongUsClient.Instance.FinishRpcImmediately(writer);
                RPCProcedure.impostorEnd();
                Modules.Logger.SendInGame("ForceEnd(Impostor Win)");
            }

            // F11&F12を変えた理由・・・誰かがSteamでスクショ撮ろうとして廃村したから

            if (!TheOtherRolesPlugin.DebugMode.Value) return;

            // Spawn dummys
            if (Input.GetKeyDown(KeyCode.F))
            {
                var playerControl = UnityEngine.Object.Instantiate(AmongUsClient.Instance.PlayerPrefab);
                var i = playerControl.PlayerId = (byte)GameData.Instance.GetAvailableId();

                bots.Add(playerControl);
                GameData.Instance.AddPlayer(playerControl);
                AmongUsClient.Instance.Spawn(playerControl, -2, InnerNet.SpawnFlags.None);

                int hat = random.Next(HatManager.Instance.allHats.Count);
                int pet = random.Next(HatManager.Instance.allPets.Count);
                int skin = random.Next(HatManager.Instance.allSkins.Count);
                int visor = random.Next(HatManager.Instance.allVisors.Count);
                int color = random.Next(Palette.PlayerColors.Length);
                int nameplate = random.Next(HatManager.Instance.allNamePlates.Count);

                playerControl.transform.position = PlayerControl.LocalPlayer.transform.position;
                playerControl.GetComponent<DummyBehaviour>().enabled = true;
                playerControl.NetTransform.enabled = false;
                playerControl.SetName(RandomString(10));
                playerControl.SetColor(color);
                playerControl.SetHat(HatManager.Instance.allHats[hat].ProductId, color);
                playerControl.SetPet(HatManager.Instance.allPets[pet].ProductId, color);
                playerControl.SetVisor(HatManager.Instance.allVisors[visor].ProductId, color);
                playerControl.SetSkin(HatManager.Instance.allSkins[skin].ProductId, color);
                playerControl.SetNamePlate(HatManager.Instance.allNamePlates[nameplate].ProductId);
                GameData.Instance.RpcSetTasks(playerControl.PlayerId, new byte[0]);
            }
        }

        public static string RandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, length)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        [HarmonyPatch(typeof(ChatController), nameof(ChatController.Update))]
        public static class ChatControllerAwakePatch
        {
            public static void Prefix()
            {
                SaveManager.chatModeType = 1;
                SaveManager.isGuest = false;
            }
            public static void Postfix(ChatController __instance)
            {
                SaveManager.chatModeType = 1;
                SaveManager.isGuest = false;
                if (Input.GetKeyDown(KeyCode.F1))
                {
                    if (!__instance.isActiveAndEnabled) return;
                    __instance.SetVisible(false);
                    new LateTask(() =>
                    {
                        __instance.SetVisible(true);
                    }, 0f, "AntiChatBug");
                }
            }
        }

        [HarmonyPatch(typeof(VersionShower), nameof(VersionShower.Start))]
        public class DebugModeOnPatch
        {
            public static void Postfix(VersionShower __instance)
            {
                __instance.text.text += "\n <color=#ff351f>TheOtherRolesGM KiyoMugiEdition</color>";
                __instance.text.transform.position = new Vector3(-5.2333f, 2.75f, 0f);
                if(TheOtherRolesPlugin.DebugMode.Value)
                {
                    __instance.text.text += " : <color=#7fffd4>DebugMode</color> <color=#00ff7f>On</color>";
                }
            }
        }
    }
}