using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace RelicAbyssLevelUpStatsFix;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class LevelUpStatsFixPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "joiny.relicabyss.levelupstatsfix";
    public const string PluginName = "Relic Abyss Level Up Stats Fix";
    public const string PluginVersion = "0.1.0";

    private static readonly MethodInfo? spawnAdvancementMethod = AccessTools.Method(
        typeof(PerksScreenManager),
        "SpawnPerk",
        new[] { typeof(AdvancementData) });

    private static readonly MethodInfo? trySpawnExtraItemsMethod = AccessTools.Method(
        typeof(PerksScreenManager),
        "TrySpawnExtraItems");

    private static ManualLogSource log = null!;
    private Harmony? harmony;

    private void Awake()
    {
        log = Logger;
        harmony = new Harmony(PluginGuid);
        harmony.PatchAll();
        log.LogInfo($"{PluginName} {PluginVersion} loaded. Stat pages are limited to every fifth level and stat banishes are respected.");
    }

    private void OnDestroy()
    {
        harmony?.UnpatchSelf();
    }

    [HarmonyPatch(typeof(PerksScreenManager), nameof(PerksScreenManager.OpenPerksScreen))]
    private static class PerksScreenManagerOpenPerksScreenPatch
    {
        private static void Prefix(PerksScreenManager __instance, int count)
        {
            if (__instance.perkOrderIndex < 0 || __instance.perkOrderIndex >= __instance.perkChains.Count)
            {
                return;
            }

            PerkChain chain = __instance.perkChains[__instance.perkOrderIndex];
            if (__instance.isCustomAdvancement)
            {
                if (chain.perkType == PerksScreenManager.PERK_TYPE.STAT)
                {
                    chain.Initialize(PerksScreenManager.PERK_TYPE.ADVANCEMENT);
                }
                return;
            }

            int level = PlayerManager.playerStatManager.currentStats.level;
            if (count > 0 && level > 0 && level % 5 == 0)
            {
                chain.Initialize(PerksScreenManager.PERK_TYPE.STAT);
                return;
            }

            if (chain.perkType == PerksScreenManager.PERK_TYPE.STAT && (level <= 0 || level % 5 != 0))
            {
                PerksScreenManager.PERK_TYPE type = Random.value > 0.55f
                    ? PerksScreenManager.PERK_TYPE.ADVANCEMENT
                    : PerksScreenManager.PERK_TYPE.PERK;
                chain.Initialize(type);
            }
        }
    }

    [HarmonyPatch(typeof(PerksScreenManager), "TrySpawnStatAdvancement")]
    private static class PerksScreenManagerTrySpawnStatAdvancementPatch
    {
        private static bool Prefix(PerksScreenManager __instance)
        {
            if (spawnAdvancementMethod == null || trySpawnExtraItemsMethod == null)
            {
                log.LogWarning("Could not find private perk spawn methods. Falling back to the game's stat spawn logic.");
                return true;
            }

            List<AdvancementData> viable = new();
            foreach (AdvancementData advancement in DataRepository.instance.statAdvancements)
            {
                if (__instance.banishedAdvancements.Contains(advancement))
                {
                    continue;
                }

                bool alreadyShown = false;
                foreach (PerkItemUI perkItem in __instance.perkItems)
                {
                    if (perkItem.advancementRef == advancement)
                    {
                        alreadyShown = true;
                        break;
                    }
                }

                if (!alreadyShown)
                {
                    viable.Add(advancement);
                }
            }

            if (viable.Count == 0)
            {
                trySpawnExtraItemsMethod.Invoke(__instance, null);
                return false;
            }

            AdvancementData selected = viable[Random.Range(0, viable.Count)];
            spawnAdvancementMethod.Invoke(__instance, new object[] { selected });
            return false;
        }
    }
}
