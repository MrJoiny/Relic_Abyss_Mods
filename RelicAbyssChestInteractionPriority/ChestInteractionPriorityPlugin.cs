using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace RelicAbyssChestInteractionPriority;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class ChestInteractionPriorityPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "joiny.relicabyss.chestinteractionpriority";
    public const string PluginName = "Relic Abyss Chest Interaction Priority";
    public const string PluginVersion = "0.1.0";

    private static ManualLogSource log = null!;
    private Harmony? harmony;

    private void Awake()
    {
        log = Logger;
        harmony = new Harmony(PluginGuid);
        harmony.PatchAll();
        log.LogInfo($"{PluginName} {PluginVersion} loaded. Chests have interaction priority over nearby item pickups.");
    }

    private void OnDestroy()
    {
        harmony?.UnpatchSelf();
    }

    [HarmonyPatch(typeof(InteractionPopupManager), "ProcessInteractables")]
    private static class InteractionPopupManagerProcessInteractablesPatch
    {
        private static bool Prefix(InteractionPopupManager __instance)
        {
            if (GameManager.instance.currentGameplayType != GameManager.GAMEPLAY_TYPE.GAMEPLAY)
            {
                ClearSelection(__instance);
                return false;
            }

            __instance.interactableEntities = PlayerManager.playerController.entityInteractableScanner.ScanInteractables();
            InteractableEntity? preferred = FindPreferredInteractable(__instance);

            __instance.cycleIndicator.gameObject.SetActive(
                __instance.interactableEntities.Count > 1 &&
                __instance.currentInteractionType != InteractionPopupManager.INTERACTION_TYPE.NONE);

            if (preferred == null)
            {
                ClearSelection(__instance);
                return false;
            }

            if (__instance.selectedInteractable == preferred)
            {
                return false;
            }

            HidePickupText(__instance.selectedInteractable);
            SelectInteractable(__instance, preferred);
            return false;
        }

        private static InteractableEntity? FindPreferredInteractable(InteractionPopupManager manager)
        {
            InteractableEntity? nearest = null;
            InteractableEntity? nearestChest = null;
            float nearestDistance = float.MaxValue;
            float nearestChestDistance = float.MaxValue;

            foreach (InteractableEntity interactable in manager.interactableEntities)
            {
                float distance = Vector3.Distance(manager.transform.position, interactable.transform.position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = interactable;
                }

                if (interactable.currentInteractableType == InteractableEntity.INTERACTABLE_TYPE.CHEST && distance < nearestChestDistance)
                {
                    nearestChestDistance = distance;
                    nearestChest = interactable;
                }
            }

            return nearestChest ?? nearest;
        }

        private static void SelectInteractable(InteractionPopupManager manager, InteractableEntity interactable)
        {
            manager.selectedInteractable = interactable;
            manager.ChangeInteraction(
                interactable.interactionPopupType,
                interactable.transform,
                interactable.indicatorOffset,
                interactable.interactionPopupOffset);
            ShowPickupText(interactable);
        }

        private static void ClearSelection(InteractionPopupManager manager)
        {
            HidePickupText(manager.selectedInteractable);
            manager.selectedInteractable = null;
            manager.ChangeInteraction(InteractionPopupManager.INTERACTION_TYPE.NONE, null, Vector3.zero, Vector3.zero);
        }

        private static void ShowPickupText(InteractableEntity? interactable)
        {
            if (interactable == null)
            {
                return;
            }

            if (interactable.TryGetComponent<EquipmentPickup>(out var equipmentPickup))
            {
                equipmentPickup.equipmentText.gameObject.SetActive(true);
            }

            if (interactable.TryGetComponent<RelicPickup>(out var relicPickup))
            {
                relicPickup.relicText.gameObject.SetActive(true);
            }
        }

        private static void HidePickupText(InteractableEntity? interactable)
        {
            if (interactable == null)
            {
                return;
            }

            if (interactable.TryGetComponent<EquipmentPickup>(out var equipmentPickup))
            {
                equipmentPickup.equipmentText.gameObject.SetActive(false);
            }

            if (interactable.TryGetComponent<RelicPickup>(out var relicPickup))
            {
                relicPickup.relicText.gameObject.SetActive(false);
            }
        }
    }
}
