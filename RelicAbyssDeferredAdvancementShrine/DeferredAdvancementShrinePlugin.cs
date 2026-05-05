using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace RelicAbyssDeferredAdvancementShrine;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class DeferredAdvancementShrinePlugin : BaseUnityPlugin
{
    public const string PluginGuid = "joiny.relicabyss.deferredadvancementshrine";
    public const string PluginName = "Relic Abyss Deferred Advancement Shrine";
    public const string PluginVersion = "0.1.0";

    private static DeferredAdvancementShrinePlugin? instance;
    private static ManualLogSource log = null!;
    private static bool pendingAdvancement;
    private static bool allowDeferredOpen;

    private Harmony? harmony;

    private void Awake()
    {
        instance = this;
        log = Logger;
        harmony = new Harmony(PluginGuid);
        harmony.PatchAll();
        log.LogInfo($"{PluginName} {PluginVersion} loaded. Advancement shrine menu opens only during gameplay.");
    }

    private void OnDestroy()
    {
        harmony?.UnpatchSelf();
        if (instance == this)
        {
            instance = null;
        }
    }

    private void Update()
    {
        if (!pendingAdvancement || !CanOpenAdvancementNow())
        {
            return;
        }

        pendingAdvancement = false;
        allowDeferredOpen = true;
        log.LogInfo("Opening deferred advancement shrine menu.");
        PerksScreenManager.instance.OpenCustomAdvancement();
        allowDeferredOpen = false;
    }

    private static bool CanOpenAdvancementNow()
    {
        return GameManager.instance != null &&
               GameManager.instance.currentGameplayType == GameManager.GAMEPLAY_TYPE.GAMEPLAY &&
               PerksScreenManager.instance != null &&
               !PerksScreenManager.instance.perksPanelObject.activeInHierarchy;
    }

    [HarmonyPatch(typeof(PerksScreenManager), nameof(PerksScreenManager.OpenCustomAdvancement))]
    private static class PerksScreenManagerOpenCustomAdvancementPatch
    {
        private static bool Prefix()
        {
            if (allowDeferredOpen || CanOpenAdvancementNow())
            {
                return true;
            }

            pendingAdvancement = true;
            log.LogInfo($"Deferring advancement shrine menu while gameplay state is {GetGameplayStateForLog()}.");
            return false;
        }
    }

    private static string GetGameplayStateForLog()
    {
        return GameManager.instance != null
            ? GameManager.instance.currentGameplayType.ToString()
            : "<GameManager unavailable>";
    }
}
