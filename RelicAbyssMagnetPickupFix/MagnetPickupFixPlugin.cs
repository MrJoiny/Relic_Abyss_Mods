using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace RelicAbyssMagnetPickupFix;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class MagnetPickupFixPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "joiny.relicabyss.magnetpickupfix";
    public const string PluginName = "Relic Abyss Magnet Pickup Fix";
    public const string PluginVersion = "0.1.0";

    private static ManualLogSource log = null!;
    private Harmony? harmony;

    private void Awake()
    {
        log = Logger;
        harmony = new Harmony(PluginGuid);
        harmony.PatchAll();
        log.LogInfo($"{PluginName} {PluginVersion} loaded. Magnetized gold and item pickups will be marked collectible.");
    }

    private void OnDestroy()
    {
        harmony?.UnpatchSelf();
    }

    [HarmonyPatch(typeof(Pickup), "OnFixedUpdate")]
    private static class PickupOnFixedUpdatePatch
    {
        private static void Prefix(Pickup __instance)
        {
            if (__instance == null ||
                __instance.hasCollected ||
                __instance.pickupData == null ||
                PlayerManager.playerController == null ||
                !PlayerManager.playerController.isMagnetizing ||
                __instance.pickupData.pickupType == PICKUPTYPE.MAGNET ||
                __instance.pickupData.pickupType == PICKUPTYPE.HEALING)
            {
                return;
            }

            __instance.OnMagnetize();
        }
    }
}
