using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;

namespace RelicAbyssManualDroppedPickups;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class ManualDroppedPickupsPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "joiny.relicabyss.manualdroppedpickups";
    public const string PluginName = "Relic Abyss Manual Dropped Pickups";
    public const string PluginVersion = "0.1.0";

    private static ManualLogSource log = null!;
    private Harmony? harmony;

    private void Awake()
    {
        log = Logger;
        harmony = new Harmony(PluginGuid);
        harmony.PatchAll();
        log.LogInfo($"{PluginName} {PluginVersion} loaded. Inventory-dropped item pickups now require interaction.");
    }

    private void OnDestroy()
    {
        harmony?.UnpatchSelf();
    }

    [HarmonyPatch(typeof(InventoryManager), nameof(InventoryManager.DropSelected))]
    private static class InventoryManagerDropSelectedPatch
    {
        private static bool Prefix(InventoryManager __instance)
        {
            InventoryItem item = __instance.currentlySelectedItem;
            PickupData pickupData = PickupManager.instance.GetPickupDataByName(item.itemName);
            if (pickupData == null || DataRepository.instance.GetEquipmentDataByName(item.itemName) != null)
            {
                return true;
            }

            GameObject obj = ObjectPooler.instance.GetObject("Pickup", ObjectPooler.POOLED_OBJECT_TYPE.MISC);
            int originalLayer = obj.layer;
            obj.transform.position = PlayerManager.playerController.transform.position;

            Pickup pickup = obj.GetComponent<Pickup>();
            pickup.InitializePickup(pickupData, item.itemAmount);

            ManualDroppedPickup marker = obj.GetComponent<ManualDroppedPickup>() ?? obj.AddComponent<ManualDroppedPickup>();
            marker.Configure(pickup, originalLayer, GetEquipmentPickupLayer(originalLayer));

            obj.SetActive(true);
            __instance.inventoryItems.Remove(item);
            __instance.currentlySelectedItem = null;
            MouseFollowGraphic.instance.DisableGraphics();
            return false;
        }

        private static int GetEquipmentPickupLayer(int fallback)
        {
            foreach (PooledObject pooled in ObjectPooler.instance.miscObjects)
            {
                if (pooled.objectPrefab != null && pooled.objectPrefab.name == "Equipment Pickup")
                {
                    return pooled.objectPrefab.layer;
                }
            }

            return fallback;
        }
    }

    [HarmonyPatch(typeof(Pickup), "OnFixedUpdate")]
    private static class PickupOnFixedUpdatePatch
    {
        private static bool Prefix(Pickup __instance)
        {
            return !IsManual(__instance);
        }
    }

    [HarmonyPatch(typeof(Pickup), nameof(Pickup.OnMagnetize))]
    private static class PickupOnMagnetizePatch
    {
        private static bool Prefix(Pickup __instance)
        {
            return !IsManual(__instance);
        }
    }

    [HarmonyPatch(typeof(Pickup), nameof(Pickup.OnCollect))]
    private static class PickupOnCollectPatch
    {
        private static bool Prefix(Pickup __instance)
        {
            if (!__instance.TryGetComponent(out ManualDroppedPickup marker) || !marker.IsManual)
            {
                return true;
            }

            return marker.IsCollecting && marker.CanCollect();
        }
    }

    [HarmonyPatch(typeof(InteractableEntity), nameof(InteractableEntity.Interact))]
    private static class InteractableEntityInteractPatch
    {
        private static bool Prefix(InteractableEntity __instance)
        {
            if (!__instance.TryGetComponent(out ManualDroppedPickup marker) || !marker.IsManual)
            {
                return true;
            }

            InteractionPopupManager.instance.ChangeInteraction(InteractionPopupManager.INTERACTION_TYPE.NONE, null, Vector3.zero, Vector3.zero);
            DialogueManager.instance.currentlyInteracting = __instance;
            marker.TryPickup();
            return false;
        }
    }

    private static bool IsManual(Pickup pickup)
    {
        return pickup.TryGetComponent(out ManualDroppedPickup marker) && marker.IsManual && !marker.IsCollecting;
    }
}

internal sealed class ManualDroppedPickup : MonoBehaviour
{
    private Pickup pickup = null!;
    private InteractableEntity interactable = null!;
    private CircleCollider2D interactionTrigger = null!;
    private int originalLayer;

    public bool IsManual { get; private set; }
    public bool IsCollecting { get; private set; }

    public void Configure(Pickup pickupRef, int originalLayerRef, int interactableLayer)
    {
        pickup = pickupRef;
        originalLayer = originalLayerRef;
        IsManual = true;
        IsCollecting = false;

        interactable = GetComponent<InteractableEntity>() ?? gameObject.AddComponent<InteractableEntity>();
        interactable.onEnable ??= new UnityEvent();
        interactable.enabled = true;
        interactable.isInteractable = true;
        interactable.currentInteractableType = InteractableEntity.INTERACTABLE_TYPE.EQUIPMENT;
        interactable.interactionPopupType = InteractionPopupManager.INTERACTION_TYPE.PICKUP;
        interactable.indicatorOffset = Vector3.zero;
        interactable.interactionPopupOffset = Vector3.up;

        interactionTrigger ??= gameObject.AddComponent<CircleCollider2D>();
        interactionTrigger.isTrigger = true;
        interactionTrigger.radius = Mathf.Max(interactionTrigger.radius, 0.6f);
        interactionTrigger.enabled = true;

        gameObject.layer = interactableLayer;
    }

    public bool CanCollect()
    {
        PickupData data = pickup.pickupData;
        if (data.isConsumable)
        {
            foreach (InventoryItem item in InventoryManager.instance.inventoryItems)
            {
                if (item.itemName == data.pickupName)
                {
                    return true;
                }
            }
        }

        return InventoryManager.instance.inventoryItems.Count < DataManager.instance.currentSaveData.inventorySize;
    }

    public void TryPickup()
    {
        if (!CanCollect())
        {
            return;
        }

        IsCollecting = true;
        pickup.OnCollect();
        IsCollecting = false;

        if (pickup.hasCollected)
        {
            interactable.isInteractable = false;
            if (InteractionPopupManager.instance.selectedInteractable == interactable)
            {
                InteractionPopupManager.instance.selectedInteractable = null;
            }
        }
    }

    private void OnDisable()
    {
        IsManual = false;
        IsCollecting = false;
        gameObject.layer = originalLayer;

        if (interactionTrigger != null)
        {
            interactionTrigger.enabled = false;
        }

        if (interactable != null)
        {
            interactable.isInteractable = false;
            interactable.enabled = false;
        }
    }
}
