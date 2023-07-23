using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ServerSync;
using UnityEngine;

namespace CustomBiomeMessages
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class CustomBiomeMessagesPlugin : BaseUnityPlugin
    {
        internal const string ModName = "CustomBiomeMessages";
        internal const string ModVersion = "1.0.0";
        internal const string Author = "Azumatt";
        private const string ModGUID = Author + "." + ModName;
        private static string ConfigFileName = ModGUID + ".cfg";
        private static string ConfigFileFullPath = Paths.ConfigPath + Path.DirectorySeparatorChar + ConfigFileName;

        internal static string ConnectionError = "";

        private readonly Harmony _harmony = new(ModGUID);

        public static readonly ManualLogSource CustomBiomeMessagesLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);

        private static readonly ConfigSync ConfigSync = new(ModGUID) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };

        internal static readonly Dictionary<string, ConfigEntry<string>> BiomeTextConfigs = new();

        public enum Toggle
        {
            On = 1,
            Off = 0
        }

        public void Awake()
        {
            _serverConfigLocked = config("1 - General", "Lock Configuration", Toggle.On, "If on, the configuration is locked and can be changed by server admins only.");
            _ = ConfigSync.AddLockingConfigEntry(_serverConfigLocked);


            // Iterate through enum Heighmap.Biome and create a config entry for each one.
            foreach (Heightmap.Biome biome in Enum.GetValues(typeof(Heightmap.Biome)))
            {
                // Create a config entry for each biome.
                BiomeTextConfigs[biome.ToString().ToLower()] = config("2 - Biomes", $"{biome.ToString()} Text", $"$biome_{biome.ToString().ToLower()}", "The message to display when entering the " + biome.ToString() + " biome.");
            }

            Assembly assembly = Assembly.GetExecutingAssembly();
            _harmony.PatchAll(assembly);
            SetupWatcher();
        }

        private void OnDestroy()
        {
            Config.Save();
        }

        private void SetupWatcher()
        {
            FileSystemWatcher watcher = new(Paths.ConfigPath, ConfigFileName);
            watcher.Changed += ReadConfigValues;
            watcher.Created += ReadConfigValues;
            watcher.Renamed += ReadConfigValues;
            watcher.IncludeSubdirectories = true;
            watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
            watcher.EnableRaisingEvents = true;
        }

        private void ReadConfigValues(object sender, FileSystemEventArgs e)
        {
            if (!File.Exists(ConfigFileFullPath)) return;
            try
            {
                CustomBiomeMessagesLogger.LogDebug("ReadConfigValues called");
                Config.Reload();
            }
            catch
            {
                CustomBiomeMessagesLogger.LogError($"There was an issue loading your {ConfigFileName}");
                CustomBiomeMessagesLogger.LogError("Please check your config entries for spelling and format!");
            }
        }


        #region ConfigOptions

        private static ConfigEntry<Toggle> _serverConfigLocked = null!;

        private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description,
            bool synchronizedSetting = true)
        {
            ConfigDescription extendedDescription =
                new(
                    description.Description +
                    (synchronizedSetting ? " [Synced with Server]" : " [Not Synced with Server]"),
                    description.AcceptableValues, description.Tags);
            ConfigEntry<T> configEntry = Config.Bind(group, name, value, extendedDescription);
            //var configEntry = Config.Bind(group, name, value, description);

            SyncedConfigEntry<T> syncedConfigEntry = ConfigSync.AddConfigEntry(configEntry);
            syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

            return configEntry;
        }

        private ConfigEntry<T> config<T>(string group, string name, T value, string description,
            bool synchronizedSetting = true)
        {
            return config(group, name, value, new ConfigDescription(description), synchronizedSetting);
        }

        private class ConfigurationManagerAttributes
        {
            public bool? Browsable = false;
        }

        class AcceptableShortcuts : AcceptableValueBase
        {
            public AcceptableShortcuts() : base(typeof(KeyboardShortcut))
            {
            }

            public override object Clamp(object value) => value;
            public override bool IsValid(object value) => true;

            public override string ToDescriptionString() =>
                "# Acceptable values: " + string.Join(", ", UnityInput.Current.SupportedKeyCodes);
        }

        #endregion
    }

    [HarmonyPatch(typeof(MessageHud), nameof(MessageHud.ShowBiomeFoundMsg))]
    static class MessageHudShowBiomeFoundMsgPatch
    {
        static void Prefix(MessageHud __instance, ref string text, bool playStinger)
        {
            if (playStinger == false) return; // If the stinger is disabled, don't do anything. Stinger being disabled happens when the player is teleporting.

            var biomeKey = text.Replace("$biome_", "").Replace("_", "");
            // Check for the biome being in our config dictionary, display the custom message based on the configuration value for that biome.
            if (CustomBiomeMessagesPlugin.BiomeTextConfigs.TryGetValue(biomeKey, out ConfigEntry<string>? config))
            {
                text = config.Value;
            }
        }
    }
}