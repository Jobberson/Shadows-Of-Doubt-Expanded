using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace ShadowsOfDoubtExpanded;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency("Venomaus.SOD.Common")]
public class SoDExpandedPlugin : BasePlugin
{
    internal static ManualLogSource Logger;
    internal static Harmony HarmonyInstance;

    public override void Load()
    {
        Logger = Log;
        Logger.LogInfo("ShadowsOfDoubtExpanded loading...");

        HarmonyInstance = new Harmony(MyPluginInfo.PLUGIN_GUID);
        HarmonyInstance.PatchAll();

        Logger.LogInfo("ShadowsOfDoubtExpanded loaded. Killer pool expansion armed.");
    }
}
