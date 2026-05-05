using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace RelicAbyssInstantShrines;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class InstantShrinesPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "joiny.relicabyss.instantshrines";
    public const string PluginName = "Relic Abyss Instant Shrines";
    public const string PluginVersion = "0.1.0";

    private static readonly FieldInfo ShrineFinishedField = AccessTools.Field(typeof(Shrine), "shrineFinished");
    private static bool shrineFinishedFieldWarningLogged;
    private static ManualLogSource log = null!;

    private static ConfigEntry<bool> enableRandomShrine = null!;
    private static ConfigEntry<bool> enableFusionUpgradeShrine = null!;
    private static ConfigEntry<bool> enableSacrificeShrine = null!;
    private static ConfigEntry<bool> enableExperienceShrine = null!;
    private static ConfigEntry<bool> enableAdvancementShrine = null!;
    private Harmony? harmony;

    private void Awake()
    {
        log = Logger;
        enableRandomShrine = Config.Bind("Instant Shrines", "Random", true, "Instantly activate random shrines.");
        enableFusionUpgradeShrine = Config.Bind("Instant Shrines", "FusionUpgrade", true, "Instantly activate fusion shrines only when they are on the upgrade path. Combine fusion remains vanilla.");
        enableSacrificeShrine = Config.Bind("Instant Shrines", "Sacrifice", true, "Instantly activate sacrifice shrines.");
        enableExperienceShrine = Config.Bind("Instant Shrines", "Experience", true, "Instantly activate experience monoliths.");
        enableAdvancementShrine = Config.Bind("Instant Shrines", "Advancement", true, "Instantly activate advancement shrines.");
        harmony = new Harmony(PluginGuid);
        harmony.PatchAll();
        log.LogInfo($"{PluginName} {PluginVersion} loaded. Configured shrine types activate without dialogue.");
    }

    private void OnDestroy()
    {
        harmony?.UnpatchSelf();
    }

    [HarmonyPatch(typeof(Shrine), nameof(Shrine.Interact))]
    private static class ShrineInteractPatch
    {
        private static bool Prefix(Shrine __instance)
        {
            if (!ShouldActivateInstantly(__instance))
            {
                return true;
            }

            if (ShrineFinishedField == null)
            {
                LogMissingShrineFinishedField();
                return true;
            }

            if (ShrineFinishedField.GetValue(__instance) is true)
            {
                return true;
            }

            __instance.shrineAnimationDone = false;
            if (DialogueManager.instance != null)
            {
                DialogueManager.instance.currentShrine = __instance;
            }

            __instance.UseShrine();
            log.LogInfo($"Activated {__instance.currentShrineType} shrine instantly.");
            return false;
        }

        private static bool ShouldActivateInstantly(Shrine shrine)
        {
            return shrine.currentShrineType switch
            {
                Shrine.SHRINE_TYPE.RANDOM => enableRandomShrine.Value,
                Shrine.SHRINE_TYPE.FUSION => enableFusionUpgradeShrine.Value && IsFusionUpgradeShrine(shrine),
                Shrine.SHRINE_TYPE.SACRIFICE => enableSacrificeShrine.Value,
                Shrine.SHRINE_TYPE.EXPERIENCE => enableExperienceShrine.Value,
                Shrine.SHRINE_TYPE.ADVANCEMENT => enableAdvancementShrine.Value,
                _ => false
            };
        }

        private static bool IsFusionUpgradeShrine(Shrine shrine)
        {
            return shrine.shrineCustomString != "combine";
        }

        private static void LogMissingShrineFinishedField()
        {
            if (shrineFinishedFieldWarningLogged)
            {
                return;
            }

            shrineFinishedFieldWarningLogged = true;
            log.LogWarning("Could not find Shrine.shrineFinished; falling back to vanilla shrine interaction.");
        }
    }
}
