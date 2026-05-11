using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Yarn.Compiler;
using Yarn.Unity;

namespace RelicAbyssChestStatue;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class ChestStatuePlugin : BaseUnityPlugin
{
    public const string PluginGuid = "joiny.relicabyss.cheststatue";
    public const string PluginName = "Relic Abyss Chest Statue";
    public const string PluginVersion = "0.1.0";
    private const float InventoryCloseDelay = 1.35f;
    private const int ChestSlotCount = 3;
    private const string AssetBundleResourceName = "RelicAbyssChestStatue.cheststatue";
    private const string PrefabName = "Chest Statue";
    private const string DialogueText = "An old chest statue opens, promising safe passage for your items.";
    private const string ChestStatueConfigSection = "Chest Statue";
    private const string ChestSlotNamePrefix = "Chest Statue Slot ";
    private const string ChestSendButtonName = "Chest Statue Send Button";
    private const string EquipmentTitleText = "Equipment";
    private const string ChestTitleText = "Chest Statue";
    private const string SendButtonText = "Send";
    private const string SmithButtonText = "Smith";
    private const string SendAnimatorTrigger = "Send";
    private const string SentAnimatorState = "Sent";

    private static readonly int SentAnimatorStateHash = Animator.StringToHash(SentAnimatorState);
    private static readonly FieldInfo ChunksField = AccessTools.Field(typeof(WorldGenerator), "chunks");
    private static readonly FieldInfo YarnProjectField = AccessTools.Field(typeof(DialogueRunner), "yarnProject");
    private static readonly FieldInfo SmithingButtonObjField = AccessTools.Field(typeof(InventoryManager), "smithingButtonObj");
    private static readonly FieldInfo SmithingCostTextField = AccessTools.Field(typeof(InventoryManager), "smithingCostText");
    private static readonly Dictionary<DialogueRunner, HashSet<string>> registeredDialogueNodes = new Dictionary<DialogueRunner, HashSet<string>>();
    private static readonly Dictionary<string, string> dialogueLines = new Dictionary<string, string>();
    private static readonly List<InventoryUIItem> chestStatueSlots = new List<InventoryUIItem>();
    private static readonly Dictionary<TextMeshProUGUI, string> equipmentTitleTexts = new Dictionary<TextMeshProUGUI, string>();
    private static readonly HashSet<DialogueRunner> commandRunners = new HashSet<DialogueRunner>();
    private static int dialogueLineIndex;
    private static bool pendingInventoryOpen;
    private static bool chestInventoryOpen;
    private static GameObject? chestSendButton;
    private static TextMeshProUGUI? chestSendButtonText;
    private static TextMeshProUGUI? chestTributeText;
    private static Transform? chestStatueMessageTransform;
    private static InteractableEntity? chestStatueInteractable;

    private static ConfigEntry<bool> modEnabled = null!;
    private static ConfigEntry<int> spawnAttempts = null!;
    private static ConfigEntry<float> spawnChance = null!;
    private static ManualLogSource log = null!;
    private static GameObject? prefab;
    private static ObjectPooler? registeredPooler;
    private static int interactableLayer = -1;
    private static int referenceSortingLayerId;
    private static int referenceSortingOrder;
    private static Material? referenceMaterial;
    private static SpriteMaskInteraction referenceMaskInteraction;
    private static bool hasSortReference;
    private static bool loadFailed;

    private Harmony? harmony;

    private void Awake()
    {
        log = Logger;
        modEnabled = Config.Bind(ChestStatueConfigSection, "Enabled", true, "Enables the chest statue shrine.");
        spawnAttempts = Config.Bind("Spawning", "SpawnAttemptsPerChunk", 1, "How many chest statue spawn rolls are made for each generated chunk.");
        spawnChance = Config.Bind("Spawning", "SpawnChancePercent", 7.5f, "Chance for each chest statue spawn roll.");

        LoadPrefab();
        harmony = new Harmony(PluginGuid);
        harmony.PatchAll();
        TryRegister(ObjectPooler.instance);
        log.LogInfo($"{PluginName} {PluginVersion} loaded.");
    }

    private void OnDestroy()
    {
        harmony?.UnpatchSelf();
    }

    private void Update()
    {
        TryRegisterCurrentPooler();
        TryOpenPendingChestInventory();
        TryCloseChestInventory();
    }

    private static void TryRegisterCurrentPooler()
    {
        if (ObjectPooler.instance != null && registeredPooler != ObjectPooler.instance)
        {
            TryRegister(ObjectPooler.instance);
        }
    }

    private static void TryOpenPendingChestInventory()
    {
        if (!pendingInventoryOpen || DialogueManager.instance == null || DialogueManager.instance.isDialogueOpen)
        {
            return;
        }

        pendingInventoryOpen = false;
        OpenInventoryWithoutStats();
    }

    private static void TryCloseChestInventory()
    {
        if (chestInventoryOpen && InventoryManager.instance != null && !InventoryManager.instance.inventoryObject.activeInHierarchy)
        {
            CloseChestStatueInventory();
        }
    }

    private static void CloseChestStatueInventory()
    {
        chestInventoryOpen = false;
        RestoreEquipmentTitle();
        ClearChestStatueSlots();
        ClearChestStatueSendButton();
        SetEquipmentVisible(true);
        ClearChestStatueSession();
    }

    private static void LoadPrefab()
    {
        if (!modEnabled.Value)
        {
            return;
        }

        AssetBundle? bundle = LoadEmbeddedBundle();
        if (bundle == null)
        {
            loadFailed = true;
            return;
        }

        prefab = bundle.LoadAsset<GameObject>(PrefabName);
        if (prefab == null)
        {
            loadFailed = true;
            log.LogWarning($"Chest statue asset bundle does not contain a GameObject named '{PrefabName}'.");
            return;
        }

        PrepareStatue(prefab);
        log.LogInfo($"Loaded chest statue prefab '{prefab.name}'.");
    }

    private static AssetBundle? LoadEmbeddedBundle()
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(AssetBundleResourceName);
        if (stream == null)
        {
            return null;
        }

        using MemoryStream buffer = new MemoryStream();
        stream.CopyTo(buffer);
        AssetBundle bundle = AssetBundle.LoadFromMemory(buffer.ToArray());
        if (bundle == null)
        {
            log.LogWarning($"Could not load embedded chest statue asset bundle '{AssetBundleResourceName}'.");
        }
        return bundle;
    }

    private static void TryRegister(ObjectPooler? pooler)
    {
        if (!modEnabled.Value || prefab == null || pooler == null)
        {
            return;
        }

        TrySetSortReference(pooler);
        foreach (PooledObject pooled in pooler.worldObjects)
        {
            if (pooled.objectPrefab != null && pooled.objectPrefab.name == PrefabName)
            {
                PrepareStatue(pooled.objectPrefab);
                ApplyWorldLayers(pooled.objectPrefab, pooler);
                registeredPooler = pooler;
                return;
            }
        }

        ApplyWorldLayers(prefab, pooler);
        pooler.TryAddToObjectPooler(prefab, ObjectPooler.POOLED_OBJECT_TYPE.WORLD);
        registeredPooler = pooler;
        log.LogInfo($"Registered chest statue prefab '{PrefabName}' in the world object pool.");
    }

    private static void PrepareStatue(GameObject statue)
    {
        statue.name = PrefabName;
        if (statue.GetComponent<ChestStatueMarker>() == null)
        {
            statue.AddComponent<ChestStatueMarker>();
        }
        if (statue.GetComponent<ChestStatueRenderer>() == null)
        {
            statue.AddComponent<ChestStatueRenderer>();
        }

        BoxCollider2D trigger = (statue.GetComponent<ChestStatueInteractionTrigger>() ??
            statue.AddComponent<ChestStatueInteractionTrigger>()).Trigger;
        InteractableEntity interactable = trigger.GetComponent<InteractableEntity>() ?? trigger.gameObject.AddComponent<InteractableEntity>();
        interactable.isInteractable = true;
        interactable.currentInteractableType = InteractableEntity.INTERACTABLE_TYPE.SHRINE;
        interactable.interactionPopupType = InteractionPopupManager.INTERACTION_TYPE.INTERACT;

        Collider2D[] colliders = statue.GetComponentsInChildren<Collider2D>(includeInactive: true);
        if (colliders.Length == 1 && colliders[0] == trigger)
        {
            BoxCollider2D solid = statue.AddComponent<BoxCollider2D>();
            solid.size = new Vector2(2f, 2f);
            colliders = statue.GetComponentsInChildren<Collider2D>(includeInactive: true);
        }

        foreach (Collider2D col in colliders)
        {
            col.isTrigger = col == trigger;
        }

        trigger.size = new Vector2(2.5f, 2.5f);
        trigger.offset = new Vector2(0f, 0.5f);
        ApplyRendering(statue);
    }

    private static void ApplyWorldLayers(GameObject statue, ObjectPooler pooler)
    {
        if (interactableLayer < 0)
        {
            foreach (PooledObject pooled in pooler.worldObjects)
            {
                if (pooled.objectPrefab != null && pooled.objectPrefab != statue &&
                    pooled.objectPrefab.GetComponent<InteractableEntity>() != null)
                {
                    interactableLayer = pooled.objectPrefab.layer;
                    break;
                }
            }
        }

        if (interactableLayer < 0)
        {
            return;
        }

        SetLayer(statue, interactableLayer);
        BoxCollider2D? trigger = statue.GetComponent<ChestStatueInteractionTrigger>()?.Trigger;
        if (trigger != null)
        {
            SetLayer(trigger.gameObject, interactableLayer);
        }
    }

    private static void TrySetSortReference(ObjectPooler pooler)
    {
        foreach (PooledObject pooled in pooler.worldObjects)
        {
            GameObject shrine = pooled.objectPrefab;
            if (shrine == null || shrine.name == PrefabName || shrine.GetComponent<Shrine>() == null)
            {
                continue;
            }

            SpriteRenderer renderer = shrine.GetComponentInChildren<SpriteRenderer>(includeInactive: true);
            if (renderer == null)
            {
                continue;
            }

            referenceSortingLayerId = renderer.sortingLayerID;
            referenceSortingOrder = renderer.sortingOrder;
            referenceMaterial = renderer.sharedMaterial;
            referenceMaskInteraction = renderer.maskInteraction;
            hasSortReference = true;
            return;
        }
    }

    private static void SetLayer(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
        {
            SetLayer(child.gameObject, layer);
        }
    }

    internal static void ApplyRendering(GameObject statue)
    {
        foreach (SortingGroup group in statue.GetComponentsInChildren<SortingGroup>(includeInactive: true))
        {
            group.enabled = false;
        }

        foreach (SpriteRenderer renderer in statue.GetComponentsInChildren<SpriteRenderer>(includeInactive: true))
        {
            renderer.enabled = true;
            renderer.color = Color.white;
            renderer.spriteSortPoint = SpriteSortPoint.Pivot;
            if (hasSortReference)
            {
                renderer.sortingLayerID = referenceSortingLayerId;
                renderer.sortingOrder = referenceSortingOrder;
                renderer.sharedMaterial = referenceMaterial;
                renderer.maskInteraction = referenceMaskInteraction;
            }
        }
    }

    private static void AddChunkSpawns(WorldGenerator generator)
    {
        TryRegister(ObjectPooler.instance);
        if (!modEnabled.Value || registeredPooler != ObjectPooler.instance || loadFailed || spawnAttempts.Value <= 0 || spawnChance.Value <= 0f ||
            GetChunks(generator) is not List<Chunk> chunks || chunks.Count == 0)
        {
            return;
        }

        Chunk chunk = chunks[chunks.Count - 1];
        for (int i = 0; i < spawnAttempts.Value; i++)
        {
            if (Random.value > spawnChance.Value / 100f)
            {
                continue;
            }

            chunk.chunkData.Add(new ChunkData
            {
                assignedObject = PrefabName,
                position = new Vector2(
                    Random.Range(0f - generator.chunkSize.x / 2f, generator.chunkSize.x / 2f),
                    Random.Range(0f - generator.chunkSize.y / 2f, generator.chunkSize.y / 2f)),
                zRotation = 0f,
                isFlipped = false,
                isDestructible = false,
                isShrine = false
            });
        }
    }

    internal static bool TryInteract(InteractableEntity interactable)
    {
        if (!modEnabled.Value || interactable.GetComponent<ChestStatueMarker>() == null)
        {
            return false;
        }

        if (InteractionPopupManager.instance != null)
        {
            InteractionPopupManager.instance.ChangeInteraction(InteractionPopupManager.INTERACTION_TYPE.NONE, null, Vector3.zero, Vector3.zero);
        }

        if (DialogueManager.instance != null)
        {
            DialogueManager.instance.currentlyInteracting = interactable;
            chestStatueMessageTransform = interactable.transform;
            chestStatueInteractable = interactable;
            DialogueRunner runner = DialogueManager.instance.GetComponent<DialogueRunner>();
            string node = "ChestStatue";
            if (runner != null && (runner.NodeExists(node) || TryRegisterDialogue(runner, node)))
            {
                RegisterDialogueCommands(runner);

                DialogueManager.instance.PlayDialogue(node);
                return true;
            }

            log.LogWarning($"Could not start chest statue dialogue node '{node}'.");
        }

        return true;
    }

    private static void OpenInventoryWithoutStats()
    {
        if (InventoryManager.instance == null || StatsScreenManager.instance == null)
        {
            return;
        }

        InventoryManager.instance.OpenInventory();
        chestInventoryOpen = true;
        InventoryManager.instance.goldText.text = ((int)DataManager.instance.transientSaveData.gold).ToString();
        StatsScreenManager.instance.attributePanel.SetParent(StatsScreenManager.instance.attributeHolder);
        SetEquipmentVisible(false);
        CreateChestStatueSlots();
        CreateChestStatueSendButton();
        RenameEquipmentTitle();
        UpdateChestStatueTribute();
        UIX.UpdateLayout(InventoryManager.instance.horizontalHolder);
    }

    private static void SetEquipmentVisible(bool visible)
    {
        if (InventoryManager.instance == null)
        {
            return;
        }

        foreach (InventoryUIItem item in InventoryManager.instance.equipmentUIItems)
        {
            item.gameObject.SetActive(visible);
        }
    }

    private static void CreateChestStatueSlots()
    {
        ClearChestStatueSlots();
        if (InventoryManager.instance == null)
        {
            return;
        }

        int slotCount = Mathf.Min(ChestSlotCount, InventoryManager.instance.equipmentUIItems.Count);
        for (int i = 0; i < slotCount; i++)
        {
            InventoryUIItem source = InventoryManager.instance.equipmentUIItems[i];
            Transform parent = source.transform.parent;
            GameObject slot = Object.Instantiate(source.gameObject, parent);
            slot.name = ChestSlotNamePrefix + (i + 1);
            InventoryUIItem uiItem = slot.GetComponent<InventoryUIItem>();
            uiItem.inventoryType = InventoryUIItem.INVENTORYITEM_TYPE.ITEM;
            uiItem.specialInventoryType = InventoryUIItem.SPECIAL_INVENTORY_TYPE.NONE;
            uiItem.Initialize(new InventoryItem());
            if (InventoryManager.instance.inventoryUIItems.Count > 0)
            {
                InventoryUIItem inventorySlot = InventoryManager.instance.inventoryUIItems[0];
                Image inventoryImage = inventorySlot.GetComponent<Image>();
                Image slotImage = slot.GetComponent<Image>();
                if (inventoryImage != null && slotImage != null)
                {
                    slotImage.sprite = inventoryImage.sprite;
                    slotImage.type = inventoryImage.type;
                    slotImage.color = inventoryImage.color;
                    slotImage.material = inventoryImage.material;
                }

                uiItem.holderImage.sprite = inventorySlot.holderImage.sprite;
                uiItem.holderImage.type = inventorySlot.holderImage.type;
                uiItem.holderImage.color = inventorySlot.holderImage.color;
                uiItem.holderImage.material = inventorySlot.holderImage.material;
            }
            uiItem.highlightImage.gameObject.SetActive(false);
            Button button = slot.GetComponent<Button>();
            if (button != null)
            {
                button.interactable = true;
            }

            slot.SetActive(true);
            chestStatueSlots.Add(uiItem);
        }
    }

    private static void ClearChestStatueSlots()
    {
        foreach (InventoryUIItem slot in chestStatueSlots)
        {
            if (slot != null)
            {
                Object.Destroy(slot.gameObject);
            }
        }
        chestStatueSlots.Clear();
    }

    private static void CreateChestStatueSendButton()
    {
        ClearChestStatueSendButton();
        if (InventoryManager.instance == null || chestStatueSlots.Count == 0)
        {
            return;
        }

        GameObject? source = GetSmithingButton(InventoryManager.instance);
        if (source == null)
        {
            log.LogWarning("Could not find the vanilla smithing button for chest statue send button.");
            return;
        }

        chestSendButton = CreateSendButtonClone(source, chestStatueSlots[0].transform.parent);
        ResetSendButtonClickHandler(chestSendButton);
        ResolveSendButtonTexts(chestSendButton, GetSmithingCostText(InventoryManager.instance));

        chestSendButton.SetActive(true);
        ApplyChestStatueButtonText(GetChestStatueTribute());
        RefreshChestStatueButtonLayout();
        InventoryManager.instance.StartCoroutine(CorRefreshChestStatueButton());
    }

    private static GameObject CreateSendButtonClone(GameObject source, Transform parent)
    {
        GameObject sendButton = Object.Instantiate(source, parent);
        sendButton.name = ChestSendButtonName;
        sendButton.SetActive(false);
        return sendButton;
    }

    private static void ResetSendButtonClickHandler(GameObject sendButton)
    {
        Button button = sendButton.GetComponent<Button>();
        if (button == null)
        {
            log.LogWarning("Chest statue send button clone has no Button component.");
            return;
        }

        // Cloned scene buttons keep persistent inspector listeners; replace the event entirely.
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(SendChestStatueItems);
        button.interactable = true;
    }

    private static void ResolveSendButtonTexts(GameObject sendButton, TextMeshProUGUI? sourceCostText)
    {
        TextMeshProUGUI[] texts = sendButton.GetComponentsInChildren<TextMeshProUGUI>(includeInactive: true);
        chestSendButtonText = null;
        chestTributeText = null;
        foreach (TextMeshProUGUI text in texts)
        {
            if (sourceCostText != null && text.name == sourceCostText.name)
            {
                chestTributeText = text;
                continue;
            }

            if (chestSendButtonText == null)
            {
                chestSendButtonText = text;
            }
        }

        if (chestTributeText == null)
        {
            chestTributeText = texts.FirstOrDefault(text => text != chestSendButtonText && int.TryParse(text.text, out _));
        }

        if (chestTributeText == null && texts.Length > 1)
        {
            chestTributeText = texts.FirstOrDefault(text => text != chestSendButtonText);
        }

        if (chestSendButtonText == null || chestTributeText == null)
        {
            log.LogWarning("Could not resolve all text labels on the chest statue send button clone.");
        }
    }

    private static GameObject? GetSmithingButton(InventoryManager inventory)
    {
        return SmithingButtonObjField.GetValue(inventory) as GameObject;
    }

    private static TextMeshProUGUI? GetSmithingCostText(InventoryManager inventory)
    {
        return SmithingCostTextField.GetValue(inventory) as TextMeshProUGUI;
    }

    private static List<Chunk>? GetChunks(WorldGenerator generator)
    {
        return ChunksField.GetValue(generator) as List<Chunk>;
    }

    private static YarnProject? GetYarnProject(DialogueRunner runner)
    {
        return YarnProjectField.GetValue(runner) as YarnProject;
    }

    private static void ClearChestStatueSendButton()
    {
        if (chestSendButton != null)
        {
            Object.Destroy(chestSendButton);
        }
        chestSendButton = null;
        chestSendButtonText = null;
        chestTributeText = null;
    }

    private static void RenameEquipmentTitle()
    {
        TextMeshProUGUI? titleText = FindEquipmentTitleText();
        if (titleText == null)
        {
            return;
        }

        equipmentTitleTexts[titleText] = titleText.text;
        titleText.SetText(ChestTitleText);
    }

    private static TextMeshProUGUI? FindEquipmentTitleText()
    {
        if (InventoryManager.instance == null || InventoryManager.instance.equipmentUIItems.Count == 0)
        {
            return null;
        }

        Transform? slotParent = InventoryManager.instance.equipmentUIItems[0].transform.parent;
        Transform? searchRoot = slotParent != null && slotParent.parent != null ? slotParent.parent : slotParent;
        if (searchRoot == null)
        {
            return null;
        }

        foreach (TextMeshProUGUI text in searchRoot.GetComponentsInChildren<TextMeshProUGUI>(includeInactive: true))
        {
            if (text.text.Trim() == EquipmentTitleText)
            {
                return text;
            }
        }

        return null;
    }

    private static void RestoreEquipmentTitle()
    {
        foreach (KeyValuePair<TextMeshProUGUI, string> titleText in equipmentTitleTexts)
        {
            if (titleText.Key != null)
            {
                titleText.Key.SetText(titleText.Value);
            }
        }
        equipmentTitleTexts.Clear();
    }

    private static void SendChestStatueItems()
    {
        if (InventoryManager.instance == null || DataManager.instance == null)
        {
            return;
        }

        if (InventoryManager.instance.currentlySelectedItem != null && !string.IsNullOrWhiteSpace(InventoryManager.instance.currentlySelectedItem.itemName))
        {
            AudioManager.instance.PlaySFX("Cancel - Heavy");
            return;
        }

        int tribute = GetChestStatueTribute();
        if (tribute <= 0)
        {
            AudioManager.instance.PlaySFX("Cancel - Heavy");
            return;
        }

        if (DataManager.instance.transientSaveData.gold < tribute)
        {
            AudioManager.instance.PlaySFX("Cancel - Heavy");
            Notifier.instance?.Notify(Notifier.NOTIFIER_STATE.MESSAGE, "Not enough in-round gold for the chest statue tribute.");
            return;
        }

        foreach (InventoryUIItem slot in chestStatueSlots)
        {
            if (slot.itemRef == null || string.IsNullOrWhiteSpace(slot.itemRef.itemName))
            {
                continue;
            }

            DataManager.instance.currentSaveData.rewardItems.Add(new InventoryItem(slot.itemRef));
            InventoryManager.instance.RemoveItem(slot.itemRef);
            slot.Initialize(new InventoryItem());
        }

        DataManager.instance.transientSaveData.gold -= tribute;
        DataManager.instance.SaveData();
        InventoryManager.instance.goldText.text = ((int)DataManager.instance.transientSaveData.gold).ToString();
        if (HUDManager.instance != null)
        {
            HUDManager.instance.goldText.SetText(((int)DataManager.instance.transientSaveData.gold).ToString());
            HUDManager.instance.UpdateHUD();
        }
        UpdateChestStatueTribute();
        UIX.UpdateLayout(InventoryManager.instance.horizontalHolder);
        AudioManager.instance.PlaySFX("Confirm - Heavy");
        DisableCurrentChestStatue();
        InventoryManager.instance.CloseInventory();
        StartChestStatueSendFeedback();
    }

    private static void StartChestStatueSendFeedback()
    {
        if (chestStatueInteractable == null)
        {
            return;
        }

        Transform? messageTransform = chestStatueMessageTransform;
        ChestStatueInteractionTrigger? triggerRoot = chestStatueInteractable.GetComponentInParent<ChestStatueInteractionTrigger>();
        Transform statueRoot = triggerRoot != null ? triggerRoot.transform : chestStatueInteractable.transform;
        chestStatueInteractable.StartCoroutine(CorChestStatueSendFeedback(statueRoot, messageTransform));
        ClearChestStatueSession();
    }

    private static void ClearChestStatueSession()
    {
        chestStatueMessageTransform = null;
        chestStatueInteractable = null;
    }

    private static IEnumerator CorChestStatueSendFeedback(Transform statueRoot, Transform? messageTransform)
    {
        yield return new WaitForSecondsRealtime(InventoryCloseDelay);
        TriggerChestStatueSendAnimation(statueRoot);
        if (FeedbackManager.instance != null && messageTransform != null)
        {
            FeedbackManager.instance.SpawnAnimatedText("<color=#42C7FF>Retrieve your items in Horizon's End</color>", messageTransform, new Vector2(0f, 2f));
        }
    }

    private static void TriggerChestStatueSendAnimation(Transform statueRoot)
    {
        Animator[] animators = statueRoot.GetComponentsInChildren<Animator>(includeInactive: true);
        bool triggered = false;
        foreach (Animator animator in animators)
        {
            bool hasSendTrigger = animator.parameters.Any(parameter =>
                parameter.name == SendAnimatorTrigger && parameter.type == AnimatorControllerParameterType.Trigger);
            bool hasSentState = animator.HasState(0, SentAnimatorStateHash);
            if (!hasSendTrigger && !hasSentState)
            {
                continue;
            }

            animator.gameObject.SetActive(true);
            animator.enabled = true;
            animator.updateMode = AnimatorUpdateMode.UnscaledTime;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.speed = 1f;
            if (hasSentState)
            {
                animator.Play(SentAnimatorState, 0, 0f);
            }
            else
            {
                animator.ResetTrigger(SendAnimatorTrigger);
                animator.SetTrigger(SendAnimatorTrigger);
            }
            animator.Update(0f);
            triggered = true;
        }

        if (!triggered)
        {
            log.LogWarning($"Could not find a chest statue child Animator with a '{SentAnimatorState}' state or a Trigger parameter named '{SendAnimatorTrigger}'.");
        }
    }

    private static void DisableCurrentChestStatue()
    {
        if (chestStatueInteractable == null)
        {
            return;
        }

        chestStatueInteractable.isInteractable = false;
        if (InteractionPopupManager.instance != null && InteractionPopupManager.instance.selectedInteractable == chestStatueInteractable)
        {
            InteractionPopupManager.instance.ChangeInteraction(InteractionPopupManager.INTERACTION_TYPE.NONE, null, Vector3.zero, Vector3.zero);
            InteractionPopupManager.instance.selectedInteractable = null;
        }
    }

    private static void UpdateChestStatueTribute()
    {
        int tribute = GetChestStatueTribute();
        if (chestSendButton == null)
        {
            CreateChestStatueSendButton();
            return;
        }

        ApplyChestStatueButtonText(tribute);
        RefreshChestStatueButtonLayout();
    }

    private static void ApplyChestStatueButtonText(int tribute)
    {
        if (chestSendButton != null)
        {
            foreach (TextMeshProUGUI text in chestSendButton.GetComponentsInChildren<TextMeshProUGUI>(includeInactive: true))
            {
                if (text == chestTributeText)
                {
                    text.SetText(tribute.ToString());
                    text.ForceMeshUpdate();
                    continue;
                }

                if (text == chestSendButtonText || text.text.Contains(SmithButtonText) || text.text.Contains(SendButtonText))
                {
                    text.SetText(SendButtonText);
                    text.ForceMeshUpdate();
                }
            }
        }
    }

    private static IEnumerator CorRefreshChestStatueButton()
    {
        yield return null;
        if (chestInventoryOpen)
        {
            ApplyChestStatueButtonText(GetChestStatueTribute());
            RefreshChestStatueButtonLayout();
        }
    }

    private static void RefreshChestStatueButtonLayout()
    {
        if (chestSendButton == null)
        {
            return;
        }

        UIX.UpdateLayout(chestSendButton.transform);
        if (chestSendButton.transform.parent != null)
        {
            UIX.UpdateLayout(chestSendButton.transform.parent);
        }

        if (chestSendButton.transform is RectTransform buttonRect)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(buttonRect);
        }

        if (chestSendButton.transform.parent is RectTransform parentRect)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);
        }
    }

    private static int GetChestStatueTribute()
    {
        int tribute = 0;
        foreach (InventoryUIItem slot in chestStatueSlots)
        {
            if (slot.itemRef != null && !string.IsNullOrWhiteSpace(slot.itemRef.itemName))
            {
                tribute += GetItemWorth(slot) * 2;
            }
        }
        return tribute;
    }

    private static int GetItemWorth(InventoryUIItem slot)
    {
        if (slot.eData != null)
        {
            return (int)slot.eData.goldCost;
        }

        if (slot.pData != null)
        {
            return (int)(slot.pData.goldCost * (float)slot.itemRef.itemAmount);
        }

        return 0;
    }

    private static bool TryRegisterDialogue(DialogueRunner runner, string node)
    {
        HashSet<string> nodes = GetRegisteredDialogueNodes(runner);
        if (nodes.Contains(node))
        {
            return runner.Dialogue.NodeExists(node);
        }

        int index = dialogueLineIndex++;
        string lineId = "ChestStatueLine_" + index;
        string sendLineId = "ChestStatueSendItemsOption_" + index;
        string exitLineId = "ChestStatueExitOption_" + index;
        string text = DialogueText.Replace("\r", " ").Replace("\n", " ");
        string source = "title: " + node + "\n---\n" +
            text + " #line:" + lineId + "\n" +
            "-> Send items #line:" + sendLineId + "\n" +
            "    <<cheststatueinventory>>\n" +
            "-> Exit #line:" + exitLineId + "\n" +
            "    <<cheststatueexit>>\n" +
            "===";
        CompilationResult result = Compiler.Compile(CompilationJob.CreateFromString("ChestStatue.yarn", source, null));
        foreach (Diagnostic diagnostic in result.Diagnostics)
        {
            if (diagnostic.Severity == Diagnostic.DiagnosticSeverity.Error)
            {
                log.LogWarning($"Could not compile chest statue dialogue: {diagnostic}");
                return false;
            }
        }

        YarnProject? project = GetYarnProject(runner);
        if (project == null)
        {
            log.LogWarning("Could not find the dialogue Yarn project for chest statue dialogue.");
            return false;
        }

        runner.Dialogue.AddProgram(result.Program);
        foreach (KeyValuePair<string, Yarn.Node> yarnNode in result.Program.Nodes)
        {
            project.Program.Nodes[yarnNode.Key] = yarnNode.Value;
        }

        foreach (KeyValuePair<string, StringInfo> line in result.StringTable)
        {
            dialogueLines[line.Key] = line.Value.text;
        }

        nodes.Add(node);
        return runner.Dialogue.NodeExists(node);
    }

    private static void RegisterDialogueCommands(DialogueRunner runner)
    {
        foreach (DialogueRunner commandRunner in commandRunners.ToArray())
        {
            if (!commandRunner)
            {
                commandRunners.Remove(commandRunner);
            }
        }

        if (commandRunners.Contains(runner))
        {
            return;
        }

        runner.AddCommandHandler("cheststatueinventory", (System.Action)(() => pendingInventoryOpen = true));
        runner.AddCommandHandler("cheststatueexit", (System.Action)(() => { }));
        commandRunners.Add(runner);
    }

    private static HashSet<string> GetRegisteredDialogueNodes(DialogueRunner runner)
    {
        foreach (DialogueRunner registeredRunner in registeredDialogueNodes.Keys.ToArray())
        {
            if (!registeredRunner)
            {
                registeredDialogueNodes.Remove(registeredRunner);
            }
        }

        if (!registeredDialogueNodes.TryGetValue(runner, out HashSet<string> nodes))
        {
            nodes = new HashSet<string>();
            registeredDialogueNodes[runner] = nodes;
        }

        return nodes;
    }

    private static bool TryGetDialogueLine(string lineId, out string text)
    {
        return dialogueLines.TryGetValue(lineId, out text);
    }

    [HarmonyPatch(typeof(ObjectPooler), "Awake")]
    private static class ObjectPoolerAwakePatch
    {
        private static void Postfix(ObjectPooler __instance)
        {
            TryRegister(__instance);
        }
    }

    [HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.InitializeWorld))]
    private static class WorldGeneratorInitializeWorldPatch
    {
        private static void Prefix()
        {
            TryRegister(ObjectPooler.instance);
        }
    }

    [HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.CreateChunk))]
    private static class WorldGeneratorCreateChunkPatch
    {
        private static void Postfix(WorldGenerator __instance)
        {
            AddChunkSpawns(__instance);
        }
    }

    [HarmonyPatch(typeof(InteractableEntity), nameof(InteractableEntity.Interact))]
    private static class InteractableEntityInteractPatch
    {
        private static bool Prefix(InteractableEntity __instance)
        {
            return !TryInteract(__instance);
        }
    }

    [HarmonyPatch(typeof(InventoryUIItem), nameof(InventoryUIItem.OnClick))]
    private static class InventoryUIItemOnClickPatch
    {
        private static void Postfix()
        {
            if (chestInventoryOpen)
            {
                UpdateChestStatueTribute();
            }
        }
    }

    [HarmonyPatch]
    private static class LineProviderGetLocalizedLinePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            return AccessTools.AllTypes()
                .Where(type => !type.IsAbstract && typeof(LineProviderBehaviour).IsAssignableFrom(type))
                .Select(type => AccessTools.Method(type, nameof(LineProviderBehaviour.GetLocalizedLine), new[] { typeof(Yarn.Line) }))
                .Where(method => method != null);
        }

        private static bool Prefix(Yarn.Line line, ref LocalizedLine __result)
        {
            if (!TryGetDialogueLine(line.ID, out string text))
            {
                return true;
            }

            __result = new LocalizedLine
            {
                TextID = line.ID,
                RawText = text,
                Substitutions = line.Substitutions,
                Metadata = new string[0]
            };
            return false;
        }
    }
}

public sealed class ChestStatueMarker : MonoBehaviour
{
}

public sealed class ChestStatueInteractionTrigger : MonoBehaviour
{
    private const string TriggerName = "Chest Statue Interaction Trigger";

    private BoxCollider2D? trigger;

    private void OnEnable()
    {
        InteractableEntity? interactable = Trigger.GetComponent<InteractableEntity>();
        if (interactable != null)
        {
            interactable.isInteractable = true;
        }
    }

    public BoxCollider2D Trigger
    {
        get
        {
            if (trigger == null)
            {
                Transform child = transform.Find(TriggerName);
                GameObject triggerObject = child != null ? child.gameObject : new GameObject(TriggerName);
                if (child == null)
                {
                    triggerObject.transform.SetParent(transform, worldPositionStays: false);
                }

                triggerObject.transform.localPosition = Vector3.zero;
                triggerObject.transform.localRotation = Quaternion.identity;
                triggerObject.transform.localScale = Vector3.one;

                if (triggerObject.GetComponent<ChestStatueMarker>() == null)
                {
                    triggerObject.AddComponent<ChestStatueMarker>();
                }

                trigger = triggerObject.GetComponent<BoxCollider2D>() ?? triggerObject.AddComponent<BoxCollider2D>();
            }

            return trigger;
        }
    }
}

public sealed class ChestStatueRenderer : MonoBehaviour
{
    private void OnEnable()
    {
        ChestStatuePlugin.ApplyRendering(gameObject);
    }
}
