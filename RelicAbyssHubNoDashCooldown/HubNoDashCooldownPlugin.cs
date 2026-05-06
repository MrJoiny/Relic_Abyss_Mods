using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace RelicAbyssHubNoDashCooldown;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class HubNoDashCooldownPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "joiny.relicabyss.hubnodashcooldown";
    public const string PluginName = "Relic Abyss Hub No Dash Cooldown";
    public const string PluginVersion = "0.1.0";

    private static ManualLogSource log = null!;
    private Harmony? harmony;

    private void Awake()
    {
        log = Logger;
        harmony = new Harmony(PluginGuid);
        harmony.PatchAll();
        log.LogInfo($"{PluginName} {PluginVersion} loaded. Dash cooldown is disabled in the hub.");
    }

    private void OnDestroy()
    {
        harmony?.UnpatchSelf();
    }

    [HarmonyPatch(typeof(PlayerStatManager), "Update")]
    private static class PlayerStatManagerUpdatePatch
    {
        private static void Postfix(PlayerStatManager __instance)
        {
            if (__instance == null || HUBManager.instance == null)
            {
                return;
            }

            __instance.dashTimer = 0f;
            HUDManager.instance?.dashHUDSKill?.SetCooldown(0f);
        }
    }
}
