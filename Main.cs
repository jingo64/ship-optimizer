using System.Reflection;
using HarmonyLib;
using UnityModManagerNet;

namespace ShipOptimizerConfig
{
    public static class Main
    {
        public static bool enabled;
        public static UnityModManager.ModEntry mod;
        public static Settings settings;

        static bool Load(UnityModManager.ModEntry modEntry)
        {
            mod = modEntry;
            settings = UnityModManager.ModSettings.Load<Settings>(modEntry);
            OptimizerConfig.LoadFrom(settings);
            modEntry.OnToggle = OnToggle;
            modEntry.OnSaveGUI = OnSaveGUI;
            new Harmony(modEntry.Info.Id).PatchAll(Assembly.GetExecutingAssembly());
            modEntry.Logger.Log("ShipOptimizerConfig loaded");
            return true;
        }

        static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            enabled = value;
            UI.OptimizerPanel.SetModActive(value);
            return true;
        }

        static void OnSaveGUI(UnityModManager.ModEntry modEntry) => SaveSettings();

        public static void SaveSettings()
        {
            OptimizerConfig.SaveTo(settings);
            settings.Save(mod);
        }

        public static void Log(string message) => mod?.Logger.Log(message);

        public static void Error(string message) => mod?.Logger.Error(message);
    }
}
