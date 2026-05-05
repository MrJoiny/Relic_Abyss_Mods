using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace RelicAbyssInstantChestDrops;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class InstantChestDropsPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "joiny.relicabyss.instantchestdrops";
    public const string PluginName = "Relic Abyss Instant Chest Drops";
    public const string PluginVersion = "0.1.1";

    private static readonly FieldInfo ChestRarityField = AccessTools.Field(typeof(InteractableEntity), "chestRarity");
    private static readonly HashSet<InteractableEntity> OpenedChests = new();
    private static ConfigEntry<bool> dropEquipmentOnGround = null!;
    private static ManualLogSource log = null!;
    private Harmony? harmony;

    private void Awake()
    {
        log = Logger;
        dropEquipmentOnGround = Config.Bind(
            "Chest Drops",
            "DropEquipmentOnGround",
            true,
            "When true, equipment rewards always drop on the ground. When false, equipment is added to inventory first and only drops if inventory is full, matching vanilla collection behavior.");

        harmony = new Harmony(PluginGuid);
        harmony.PatchAll();
        log.LogInfo($"{PluginName} {PluginVersion} loaded. Chest opening UI will be skipped.");
    }

    private void OnDestroy()
    {
        harmony?.UnpatchSelf();
    }

    [HarmonyPatch(typeof(InteractableEntity), "OpenChest")]
    private static class InteractableEntityOpenChestPatch
    {
        private static bool Prefix(InteractableEntity __instance)
        {
            if (!__instance.isInteractable || OpenedChests.Contains(__instance))
            {
                __instance.isInteractable = false;
                ClearSelectedChest(__instance);
                return false;
            }

            EQUIPMENTRARITY rarity = GetChestRarity(__instance);
            int lootAmount = GetLootAmount(rarity);
            OpenedChests.Add(__instance);
            __instance.isInteractable = false;
            ClearSelectedChest(__instance);

            if (__instance.anim != null)
            {
                __instance.anim.SetTrigger("Open");
            }

            float baseGold = __instance.GetGoldAmount();
            float goldMultiplier = PlayerManager.playerStatManager?.currentStats.goldMultiplier ?? 1f;
            log.LogInfo($"Chest loot plan start: chestId={__instance.GetInstanceID()}, rarity={rarity}, rewards={lootAmount}, baseGold={baseGold:0.##}, goldMultiplier={goldMultiplier:0.###}, effectiveGold={baseGold * goldMultiplier:0.##}, position={__instance.transform.position}.");

            SpawnLoot(__instance, rarity, lootAmount);
            log.LogInfo($"Chest loot plan gold: chestId={__instance.GetInstanceID()}, amount={baseGold * goldMultiplier:0.##}.");
            __instance.TrySpawnGold();
            __instance.onChestOpen.Invoke();
            __instance.isInteractable = false;
            ClearSelectedChest(__instance);

            log.LogInfo($"Chest loot plan end: chestId={__instance.GetInstanceID()}, rarity={rarity}, rewards={lootAmount}.");
            return false;
        }

        private static EQUIPMENTRARITY GetChestRarity(InteractableEntity chest)
        {
            if (ChestRarityField?.GetValue(chest) is EQUIPMENTRARITY rarity)
            {
                return rarity;
            }

            log.LogWarning("Could not read chest rarity; defaulting to COMMON.");
            return EQUIPMENTRARITY.COMMON;
        }

        private static int GetLootAmount(EQUIPMENTRARITY rarity)
        {
            return rarity switch
            {
                EQUIPMENTRARITY.COMMON => 1,
                EQUIPMENTRARITY.RARE => 2,
                EQUIPMENTRARITY.EPIC => 3,
                EQUIPMENTRARITY.LEGENDARY => 5,
                EQUIPMENTRARITY.ABYSS => 5,
                EQUIPMENTRARITY.PURE => 5,
                _ => 1
            };
        }

        private static void SpawnLoot(InteractableEntity chest, EQUIPMENTRARITY rarity, int lootAmount)
        {
            for (int i = 0; i < lootAmount; i++)
            {
                float value = Random.value;
                if (value >= 0.6f)
                {
                    log.LogInfo($"Chest loot roll #{i + 1}: roll={value:0.000}, branch=equipment.");
                    SpawnEquipment(chest, rarity, i + 1);
                }
                else if (value > 0.15f)
                {
                    log.LogInfo($"Chest loot roll #{i + 1}: roll={value:0.000}, branch=relic-or-fallback.");
                    List<RelicData> viableRelics = PerksScreenManager.instance.GetViableRelics(isRare: false);
                    if (viableRelics.Count > 0)
                    {
                        RelicData relicData = viableRelics[Random.Range(0, viableRelics.Count - 1)];
                        log.LogInfo($"Chest loot item #{i + 1}: type=relic, name={DescribeRelic(relicData)}, viableRelics={viableRelics.Count}.");
                        chest.SpawnRelic(relicData);
                    }
                    else if (Random.value >= 0.5f)
                    {
                        log.LogInfo($"Chest loot item #{i + 1}: no viable relics, fallback=equipment.");
                        SpawnEquipment(chest, rarity, i + 1);
                    }
                    else
                    {
                        log.LogInfo($"Chest loot item #{i + 1}: no viable relics, fallback=pickup.");
                        SpawnPickupItem(chest, i + 1);
                    }
                }
                else
                {
                    log.LogInfo($"Chest loot roll #{i + 1}: roll={value:0.000}, branch=pickup.");
                    SpawnPickupItem(chest, i + 1);
                }
            }
        }

        private static void SpawnEquipment(InteractableEntity chest, EQUIPMENTRARITY rarity, int lootIndex)
        {
            EquipmentData equipmentData = LevelManager.instance.GetRandomEquipmentLoot();
            InventoryItem item = InventoryManager.instance.CreateInventoryItem(equipmentData, rarity);
            log.LogInfo($"Chest loot item #{lootIndex}: type=equipment, data={DescribeEquipment(equipmentData)}, itemName={item.itemName}, rarity={item.itemRarity}, level={item.itemLevel}, enchantments={item.enchantments.Count}.");

            if (dropEquipmentOnGround.Value)
            {
                chest.SpawnEquipmentItem(item);
                return;
            }

            if (InventoryManager.instance.TryAddInventoryItem(item))
            {
                RecordItemPickupForTutorial();
                log.LogInfo($"Chest loot item #{lootIndex}: equipment added to inventory.");
                return;
            }

            log.LogInfo($"Chest loot item #{lootIndex}: inventory full, dropping equipment on ground.");
            chest.SpawnEquipmentItem(item);
        }

        private static void SpawnPickupItem(InteractableEntity chest, int lootIndex)
        {
            if (Random.value >= 0.5f)
            {
                PickupData? pickupData = GetEnemyCardDrop();
                if (pickupData != null)
                {
                    log.LogInfo($"Chest loot item #{lootIndex}: type=enemy-card, pickup={DescribePickup(pickupData)}, amount=1.");
                    chest.SpawnPickup(pickupData, 1);
                }

                return;
            }

            LootTableData lootTable = LevelManager.instance.GetLootData();
            if (lootTable == null)
            {
                log.LogWarning("Could not read level loot table.");
                return;
            }

            List<PickupData> itemLoot = lootTable.pickupItemLootData;
            if (itemLoot.Count == 0)
            {
                log.LogWarning("Chest pickup item loot table is empty.");
                return;
            }

            PickupData pickup = itemLoot[Random.Range(0, itemLoot.Count - 1)];
            log.LogInfo($"Chest loot item #{lootIndex}: type=pickup-item, pickup={DescribePickup(pickup)}, amount=5, lootTableItems={itemLoot.Count}.");
            chest.SpawnPickup(pickup, 5);
        }

        private static PickupData? GetEnemyCardDrop()
        {
            List<PickupData> commonDrops = new();
            List<PickupData> rareDrops = new();
            PickupData? bossDrop = null;

            foreach (EnemyData enemyData in LevelManager.instance.levelData.enemiesInLevel)
            {
                if (enemyData.cardDrop.pickupRarity == PICKUPRARITY.COMMON)
                {
                    commonDrops.Add(enemyData.cardDrop);
                }
                else if (enemyData.cardDrop.pickupRarity == PICKUPRARITY.RARE)
                {
                    rareDrops.Add(enemyData.cardDrop);
                }
                else if (enemyData.cardDrop.pickupRarity == PICKUPRARITY.BOSS)
                {
                    bossDrop = enemyData.cardDrop;
                }
            }

            if (commonDrops.Count == 0 && rareDrops.Count == 0 && bossDrop == null)
            {
                log.LogWarning("No enemy card drops were available for chest reward.");
                return null;
            }

            float value = Random.value;
            float lootMultiplier = LevelManager.instance.levelData.levelDetails.difficultyLevels[LevelManager.instance.GetSavedLevelDifficulty()].lootMultiplier;
            float bossThreshold = 1f - 0.05f * lootMultiplier;
            float rareThreshold = 1f - 0.35f * lootMultiplier;
            rareThreshold = rareThreshold < 0.5f ? 0.5f : rareThreshold;

            if (value >= bossThreshold && bossDrop != null)
            {
                return bossDrop;
            }

            if (value >= rareThreshold && rareDrops.Count > 0)
            {
                return rareDrops[Random.Range(0, rareDrops.Count - 1)];
            }

            if (commonDrops.Count > 0)
            {
                return commonDrops[Random.Range(0, commonDrops.Count - 1)];
            }

            return rareDrops.Count > 0 ? rareDrops[Random.Range(0, rareDrops.Count - 1)] : bossDrop;
        }

        private static string DescribeEquipment(EquipmentData? data)
        {
            return data == null ? "<null>" : $"{data.equipmentName} ({data.equipmentRef}, {data.itemType})";
        }

        private static string DescribeRelic(RelicData? data)
        {
            return data == null ? "<null>" : $"{data.relicName} ({data.relicRef}, {data.relicRarity})";
        }

        private static string DescribePickup(PickupData? data)
        {
            return data == null ? "<null>" : $"{data.pickupName} (pickupType={data.pickupType}, itemType={data.itemType}, rarity={data.pickupRarity})";
        }

        private static void RecordItemPickupForTutorial()
        {
            TutorialManager.instance.hasPickedupItem = true;
            if (GameManager.instance.tutorialOn &&
                TutorialManager.instance.initialTutorialDone &&
                LevelManager.instance != null &&
                !TutorialManager.instance.IsTutorialFinished("Inventory HUD"))
            {
                TutorialManager.instance.OpenTutorial("Inventory HUD");
            }
        }
    }

    [HarmonyPatch(typeof(InteractableEntity), "OnEnable")]
    private static class InteractableEntityOnEnablePatch
    {
        private static void Postfix(InteractableEntity __instance)
        {
            if (__instance.currentInteractableType == InteractableEntity.INTERACTABLE_TYPE.CHEST &&
                OpenedChests.Contains(__instance))
            {
                __instance.isInteractable = false;
                ClearSelectedChest(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(ObjectPooler), nameof(ObjectPooler.GetObject))]
    private static class ObjectPoolerGetObjectPatch
    {
        private static void Postfix(GameObject __result)
        {
            ResetChestForSpawn(__result);
        }
    }

    private static void ResetChestForSpawn(GameObject gameObject)
    {
        if (gameObject == null || !gameObject.TryGetComponent<InteractableEntity>(out var chest) ||
            chest.currentInteractableType != InteractableEntity.INTERACTABLE_TYPE.CHEST)
        {
            return;
        }

        OpenedChests.Remove(chest);
    }

    private static void ClearSelectedChest(InteractableEntity chest)
    {
        if (InteractionPopupManager.instance != null && InteractionPopupManager.instance.selectedInteractable == chest)
        {
            InteractionPopupManager.instance.selectedInteractable = null;
            InteractionPopupManager.instance.ChangeInteraction(InteractionPopupManager.INTERACTION_TYPE.NONE, null, Vector3.zero, Vector3.zero);
        }
    }
}
