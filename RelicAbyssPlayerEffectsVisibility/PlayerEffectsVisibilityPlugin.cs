using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine.Localization.Components;
using UnityEngine;
using UnityEngine.UI;

namespace RelicAbyssPlayerEffectsVisibility;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class PlayerEffectsVisibilityPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "joiny.relicabyss.playereffectsvisibility";
    public const string PluginName = "Relic Abyss Player Effects Visibility";
    public const string PluginVersion = "0.1.0";
    private const string SettingsSliderName = "Effect Visibility Slider";
    private const string LegacySettingsSliderName = "Player Effects Visibility Slider";
    private const string SettingsTitle = "Effect Visibility";
    private const float PercentChangeThreshold = 0.01f;

    private readonly Dictionary<int, ParticleColorState> particleColorStates = new();
    private readonly Dictionary<int, ColorState<SpriteRenderer>> spriteColorStates = new();
    private readonly Dictionary<int, LineColorState<TrailRenderer>> trailColorStates = new();
    private readonly Dictionary<int, LineColorState<LineRenderer>> lineColorStates = new();
    private static readonly FieldInfo AttackColliderIsPlayerField = typeof(AttackCollider).GetField("isPlayer", BindingFlags.NonPublic | BindingFlags.Instance);

    private static PlayerEffectsVisibilityPlugin? instance;

    private ConfigEntry<float> visibilityPercent = null!;
    private ConfigEntry<bool> addSettingsSlider = null!;
    private ConfigEntry<bool> showSettingsFallback = null!;
    private ConfigEntry<bool> includePlayerAttackColliders = null!;
    private ManualLogSource log = null!;
    private Slider? settingsSlider;
    private TextMeshProUGUI? settingsTitleText;
    private TextMeshProUGUI? settingsValueText;
    private Harmony? harmony;
    private float lastAppliedPercent = -1f;
    private bool settingsSliderWarningLogged;
    private GUIStyle? fallbackBoxStyle;
    private GUIStyle? fallbackLabelStyle;

    private void Awake()
    {
        instance = this;
        log = Logger;
        visibilityPercent = Config.Bind("Player Effects", "VisibilityPercent", 100f, "Player-owned effect visibility from 0 to 100. Enemy/mob effects are not modified.");
        includePlayerAttackColliders = Config.Bind("Player Effects", "IncludePlayerAttackColliders", true, "Also affect spawned objects outside the player pool when they contain a player-owned AttackCollider.");
        addSettingsSlider = Config.Bind("Settings UI", "AddSettingsSlider", true, "Clone an existing settings slider row and add Effect Visibility.");
        showSettingsFallback = Config.Bind("Settings UI", "ShowSettingsFallbackOverlay", false, "Show a small fallback slider while the settings menu is open.");
        visibilityPercent.Value = Mathf.Clamp(visibilityPercent.Value, 0f, 100f);

        harmony = new Harmony(PluginGuid);
        harmony.PatchAll();
        if (AttackColliderIsPlayerField == null)
        {
            log.LogWarning("Could not find AttackCollider.isPlayer; non-player-pool attack effects will not be detected.");
        }

        log.LogInfo($"{PluginName} {PluginVersion} loaded. Visibility: {visibilityPercent.Value:0}%.");
    }

    private void OnDestroy()
    {
        RestoreTrackedVisibility();
        harmony?.UnpatchSelf();
        if (instance == this)
        {
            instance = null;
        }
    }

    private void Update()
    {
        if (addSettingsSlider.Value && settingsSlider == null && SettingsManager.instance != null)
        {
            TryCreateSettingsSlider(SettingsManager.instance);
        }

        ForceSettingsSliderText();
        if (ShouldApplyVisibilityToPools())
        {
            ApplyVisibilityToPlayerPools();
        }
    }

    private void OnGUI()
    {
        if (!showSettingsFallback.Value || SettingsManager.instance?.settingsObj == null || !SettingsManager.instance.settingsObj.activeInHierarchy)
        {
            return;
        }

        fallbackBoxStyle ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft };
        fallbackLabelStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = Color.white } };

        Rect rect = new(Screen.width - 332, 18, 314, 92);
        GUI.Box(rect, GUIContent.none, fallbackBoxStyle);
        GUI.Label(new Rect(rect.x + 12, rect.y + 8, rect.width - 24, 24), SettingsTitle, fallbackLabelStyle);
        float newValue = GUI.HorizontalSlider(new Rect(rect.x + 12, rect.y + 42, rect.width - 74, 24), visibilityPercent.Value, 0f, 100f);
        GUI.Label(new Rect(rect.x + rect.width - 54, rect.y + 34, 44, 24), $"{Mathf.RoundToInt(visibilityPercent.Value)}%", fallbackLabelStyle);

        if (Math.Abs(newValue - visibilityPercent.Value) > PercentChangeThreshold)
        {
            SetVisibilityPercent(newValue, updateSettingsSlider: true, applyNow: true);
        }
    }

    private void TryCreateSettingsSlider(SettingsManager settings)
    {
        if (settings.sfxSlider == null || settingsSlider != null)
        {
            return;
        }

        Transform sourceRow = settings.sfxSlider.transform.parent != null
            ? settings.sfxSlider.transform.parent
            : settings.sfxSlider.transform;
        Transform? parent = FindGraphicsSettingsContent(settings)
            ?? settings.graphicsSettingsObj?.transform
            ?? sourceRow.parent
            ?? settings.audioSettingsObj?.transform
            ?? settings.settingsObj?.transform;
        if (parent == null)
        {
            LogSettingsSliderWarning("Could not create Effect Visibility slider: no settings UI parent was available.");
            return;
        }

        List<Transform> graphicsRows = FindKnownGraphicsRows(settings, parent);

        Transform? existing = null;
        if (settings.settingsObj != null)
        {
            existing = FindDescendantByName(settings.settingsObj.transform, SettingsSliderName)
                ?? FindDescendantByName(settings.settingsObj.transform, LegacySettingsSliderName);
        }

        if (existing != null)
        {
            existing.name = SettingsSliderName;
            PlaceSettingsRowAtEnd(existing, parent, sourceRow, graphicsRows);
            GameObject existingRow = existing.gameObject;
            AssignSettingsTexts(existingRow);
            if (!ConfigureSettingsSlider(existingRow))
            {
                LogSettingsSliderWarning("Could not reuse Effect Visibility slider: existing row had no Slider component.");
                return;
            }

            settingsSliderWarningLogged = false;
            ForceSettingsSliderText();
            UIX.UpdateLayout(parent);
            log.LogInfo("Effect Visibility slider already exists; reusing it.");
            return;
        }

        GameObject row = Instantiate(sourceRow.gameObject, parent);
        row.name = SettingsSliderName;
        row.SetActive(true);
        PlaceSettingsRowAtEnd(row.transform, parent, sourceRow, graphicsRows);

        foreach (CustomToggle toggle in row.GetComponentsInChildren<CustomToggle>(true))
        {
            Destroy(toggle);
        }

        foreach (LocalizeStringEvent localizeStringEvent in row.GetComponentsInChildren<LocalizeStringEvent>(true))
        {
            Destroy(localizeStringEvent);
        }

        AssignSettingsTexts(row);
        if (!ConfigureSettingsSlider(row))
        {
            LogSettingsSliderWarning("Could not create Effect Visibility slider: cloned row had no Slider component.");
            Destroy(row);
            return;
        }

        settingsSliderWarningLogged = false;
        ForceSettingsSliderText();
        UIX.UpdateLayout(parent);
        log.LogInfo($"Added Effect Visibility slider to settings UI under '{GetTransformPath(parent)}'.");
    }

    private void LogSettingsSliderWarning(string message)
    {
        if (settingsSliderWarningLogged)
        {
            return;
        }

        settingsSliderWarningLogged = true;
        log.LogWarning(message);
    }

    private bool ConfigureSettingsSlider(GameObject row)
    {
        settingsSlider = row.GetComponentInChildren<Slider>(true);
        if (settingsSlider == null)
        {
            return false;
        }

        settingsSlider.minValue = 0f;
        settingsSlider.maxValue = 100f;
        settingsSlider.wholeNumbers = false;
        settingsSlider.SetValueWithoutNotify(visibilityPercent.Value);
        settingsSlider.onValueChanged.RemoveAllListeners();
        settingsSlider.onValueChanged.AddListener(OnSettingsSliderChanged);
        return true;
    }

    private void AssignSettingsTexts(GameObject row)
    {
        TextMeshProUGUI[] labels = row.GetComponentsInChildren<TextMeshProUGUI>(true);
        if (labels.Length == 0)
        {
            return;
        }

        settingsValueText = labels
            .OrderByDescending(label => label.transform.position.x)
            .FirstOrDefault();
        settingsTitleText = labels
            .Where(label => label != settingsValueText)
            .OrderBy(label => label.transform.position.x)
            .FirstOrDefault()
            ?? labels.FirstOrDefault(label => label != settingsValueText);
    }

    private void OnSettingsSliderChanged(float value)
    {
        SetVisibilityPercent(value, updateSettingsSlider: false, applyNow: true);
    }

    private void SetVisibilityPercent(float value, bool updateSettingsSlider, bool applyNow)
    {
        visibilityPercent.Value = Mathf.Clamp(value, 0f, 100f);
        if (updateSettingsSlider && settingsSlider != null)
        {
            settingsSlider.SetValueWithoutNotify(visibilityPercent.Value);
        }

        UpdateSettingsValueText();
        if (applyNow)
        {
            ApplyVisibilityToPlayerPools();
        }
    }

    private bool ShouldApplyVisibilityToPools()
    {
        return ObjectPooler.instance != null &&
               Math.Abs(lastAppliedPercent - visibilityPercent.Value) >= PercentChangeThreshold;
    }

    private void UpdateSettingsValueText()
    {
        if (settingsValueText != null)
        {
            settingsValueText.text = $"{Mathf.RoundToInt(visibilityPercent.Value)}%";
        }
    }

    private void ForceSettingsSliderText()
    {
        if (settingsTitleText != null && settingsTitleText.text != SettingsTitle)
        {
            settingsTitleText.text = SettingsTitle;
        }

        UpdateSettingsValueText();
    }

    private void ApplyVisibilityToPlayerPools()
    {
        ObjectPooler pooler = ObjectPooler.instance;
        if (pooler == null || pooler.playerObjects == null)
        {
            return;
        }

        float alphaMultiplier = Mathf.Clamp01(visibilityPercent.Value / 100f);
        ApplyVisibilityToPools(pooler.playerObjects, forceApply: true, alphaMultiplier);
        if (includePlayerAttackColliders.Value)
        {
            ApplyVisibilityToPools(pooler.worldObjects, forceApply: false, alphaMultiplier);
            ApplyVisibilityToPools(pooler.miscObjects, forceApply: false, alphaMultiplier);
        }

        lastAppliedPercent = visibilityPercent.Value;
    }

    private void ApplyVisibilityToPools(List<PooledObject>? pools, bool forceApply, float alphaMultiplier)
    {
        if (pools == null)
        {
            return;
        }

        foreach (PooledObject pooledObject in pools)
        {
            if (pooledObject?.objectPool == null)
            {
                continue;
            }

            foreach (GameObject pooledGameObject in pooledObject.objectPool)
            {
                if (pooledGameObject == null)
                {
                    continue;
                }

                if (forceApply || IsPlayerAttackObject(pooledGameObject))
                {
                    ApplyVisibility(pooledGameObject, alphaMultiplier);
                }
            }
        }
    }

    private void InstallVisibilityApplicator(GameObject spawnedObject, ObjectPooler.POOLED_OBJECT_TYPE poolType)
    {
        if (spawnedObject == null ||
            poolType != ObjectPooler.POOLED_OBJECT_TYPE.PLAYER && !includePlayerAttackColliders.Value)
        {
            return;
        }

        VisibilityApplicator applicator = spawnedObject.GetComponent<VisibilityApplicator>()
            ?? spawnedObject.AddComponent<VisibilityApplicator>();
        applicator.Initialize(poolType);
    }

    private void ApplyVisibilityToSpawnedPlayerObject(GameObject spawnedObject, ObjectPooler.POOLED_OBJECT_TYPE poolType)
    {
        if (spawnedObject == null)
        {
            return;
        }

        bool shouldApply = poolType == ObjectPooler.POOLED_OBJECT_TYPE.PLAYER
            || includePlayerAttackColliders.Value && IsPlayerAttackObject(spawnedObject);
        if (!shouldApply)
        {
            return;
        }

        ApplyVisibility(spawnedObject, Mathf.Clamp01(visibilityPercent.Value / 100f));
    }

    private static bool IsPlayerAttackObject(GameObject gameObject)
    {
        foreach (AttackCollider attackCollider in gameObject.GetComponentsInChildren<AttackCollider>(true))
        {
            if (AttackColliderIsPlayerField?.GetValue(attackCollider) is bool isPlayer && isPlayer)
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyVisibility(GameObject root, float alphaMultiplier)
    {
        foreach (ParticleSystem particleSystem in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            ApplyParticleVisibility(particleSystem, alphaMultiplier);
        }

        foreach (SpriteRenderer spriteRenderer in root.GetComponentsInChildren<SpriteRenderer>(true))
        {
            spriteRenderer.color = ApplyColorVisibility(spriteColorStates, spriteRenderer, spriteRenderer.color, alphaMultiplier);
        }

        foreach (TrailRenderer trailRenderer in root.GetComponentsInChildren<TrailRenderer>(true))
        {
            ApplyLineVisibility(
                trailColorStates,
                trailRenderer,
                trailRenderer.startColor,
                trailRenderer.endColor,
                alphaMultiplier,
                (start, end) =>
                {
                    trailRenderer.startColor = start;
                    trailRenderer.endColor = end;
                });
        }

        foreach (LineRenderer lineRenderer in root.GetComponentsInChildren<LineRenderer>(true))
        {
            ApplyLineVisibility(
                lineColorStates,
                lineRenderer,
                lineRenderer.startColor,
                lineRenderer.endColor,
                alphaMultiplier,
                (start, end) =>
                {
                    lineRenderer.startColor = start;
                    lineRenderer.endColor = end;
                });
        }
    }

    private void ApplyParticleVisibility(ParticleSystem particleSystem, float alphaMultiplier)
    {
        int id = particleSystem.GetInstanceID();
        ParticleSystem.MainModule main = particleSystem.main;
        ParticleSystem.MinMaxGradient currentGradient = CopyGradient(main.startColor);
        if (!particleColorStates.TryGetValue(id, out ParticleColorState state))
        {
            state = new ParticleColorState(particleSystem);
            particleColorStates[id] = state;
        }

        if (!state.HasApplied || !GradientApproximatelyEqual(currentGradient, state.AppliedGradient))
        {
            state.BaseGradient = currentGradient;
        }

        state.AppliedGradient = ScaleGradientAlpha(state.BaseGradient, alphaMultiplier);
        state.HasApplied = true;
        main.startColor = CopyGradient(state.AppliedGradient);
    }

    private static Color ApplyColorVisibility<TComponent>(
        Dictionary<int, ColorState<TComponent>> states,
        TComponent component,
        Color currentColor,
        float alphaMultiplier)
        where TComponent : Component
    {
        int id = component.GetInstanceID();
        if (!states.TryGetValue(id, out ColorState<TComponent> state))
        {
            state = new ColorState<TComponent>(component);
            states[id] = state;
        }

        if (!state.HasApplied || !ColorApproximatelyEqual(currentColor, state.AppliedColor))
        {
            state.BaseColor = currentColor;
        }

        state.AppliedColor = WithScaledAlpha(state.BaseColor, alphaMultiplier);
        state.HasApplied = true;
        return state.AppliedColor;
    }

    private static void ApplyLineVisibility<TComponent>(
        Dictionary<int, LineColorState<TComponent>> states,
        TComponent component,
        Color currentStart,
        Color currentEnd,
        float alphaMultiplier,
        Action<Color, Color> applyColors)
        where TComponent : Component
    {
        int id = component.GetInstanceID();
        if (!states.TryGetValue(id, out LineColorState<TComponent> state))
        {
            state = new LineColorState<TComponent>(component);
            states[id] = state;
        }

        LineColors currentColors = new(currentStart, currentEnd);
        if (!state.HasApplied || !LineColorsApproximatelyEqual(currentColors, state.AppliedColors))
        {
            state.BaseColors = currentColors;
        }

        state.AppliedColors = new LineColors(
            WithScaledAlpha(state.BaseColors.Start, alphaMultiplier),
            WithScaledAlpha(state.BaseColors.End, alphaMultiplier));
        state.HasApplied = true;
        applyColors(state.AppliedColors.Start, state.AppliedColors.End);
    }

    private void RestoreTrackedVisibility()
    {
        foreach (ParticleColorState state in particleColorStates.Values)
        {
            if (state.ParticleSystem == null || !state.HasApplied)
            {
                continue;
            }

            ParticleSystem.MainModule main = state.ParticleSystem.main;
            if (GradientApproximatelyEqual(main.startColor, state.AppliedGradient))
            {
                main.startColor = CopyGradient(state.BaseGradient);
            }
        }

        foreach (ColorState<SpriteRenderer> state in spriteColorStates.Values)
        {
            if (state.Component != null && state.HasApplied && ColorApproximatelyEqual(state.Component.color, state.AppliedColor))
            {
                state.Component.color = state.BaseColor;
            }
        }

        foreach (LineColorState<TrailRenderer> state in trailColorStates.Values)
        {
            if (state.Component != null &&
                state.HasApplied &&
                LineColorsApproximatelyEqual(new LineColors(state.Component.startColor, state.Component.endColor), state.AppliedColors))
            {
                state.Component.startColor = state.BaseColors.Start;
                state.Component.endColor = state.BaseColors.End;
            }
        }

        foreach (LineColorState<LineRenderer> state in lineColorStates.Values)
        {
            if (state.Component != null &&
                state.HasApplied &&
                LineColorsApproximatelyEqual(new LineColors(state.Component.startColor, state.Component.endColor), state.AppliedColors))
            {
                state.Component.startColor = state.BaseColors.Start;
                state.Component.endColor = state.BaseColors.End;
            }
        }

        particleColorStates.Clear();
        spriteColorStates.Clear();
        trailColorStates.Clear();
        lineColorStates.Clear();
    }

    private static ParticleSystem.MinMaxGradient ScaleGradientAlpha(ParticleSystem.MinMaxGradient gradient, float alphaMultiplier)
    {
        switch (gradient.mode)
        {
            case ParticleSystemGradientMode.Color:
                gradient.color = WithScaledAlpha(gradient.color, alphaMultiplier);
                break;
            case ParticleSystemGradientMode.TwoColors:
                gradient.colorMin = WithScaledAlpha(gradient.colorMin, alphaMultiplier);
                gradient.colorMax = WithScaledAlpha(gradient.colorMax, alphaMultiplier);
                break;
            case ParticleSystemGradientMode.Gradient:
                gradient.gradient = ScaleGradientAlpha(gradient.gradient, alphaMultiplier);
                break;
            case ParticleSystemGradientMode.TwoGradients:
                gradient.gradientMin = ScaleGradientAlpha(gradient.gradientMin, alphaMultiplier);
                gradient.gradientMax = ScaleGradientAlpha(gradient.gradientMax, alphaMultiplier);
                break;
            case ParticleSystemGradientMode.RandomColor:
                gradient.gradient = ScaleGradientAlpha(gradient.gradient, alphaMultiplier);
                break;
            default:
                gradient.color = WithScaledAlpha(gradient.color, alphaMultiplier);
                break;
        }

        return gradient;
    }

    private static ParticleSystem.MinMaxGradient CopyGradient(ParticleSystem.MinMaxGradient gradient)
    {
        switch (gradient.mode)
        {
            case ParticleSystemGradientMode.Gradient:
                gradient.gradient = CloneGradient(gradient.gradient);
                break;
            case ParticleSystemGradientMode.TwoGradients:
                gradient.gradientMin = CloneGradient(gradient.gradientMin);
                gradient.gradientMax = CloneGradient(gradient.gradientMax);
                break;
            case ParticleSystemGradientMode.RandomColor:
                gradient.gradient = CloneGradient(gradient.gradient);
                break;
        }

        return gradient;
    }

    private static Gradient ScaleGradientAlpha(Gradient gradient, float alphaMultiplier)
    {
        Gradient scaledGradient = CloneGradient(gradient);
        GradientAlphaKey[] alphaKeys = scaledGradient.alphaKeys;
        for (int i = 0; i < alphaKeys.Length; i++)
        {
            alphaKeys[i].alpha *= alphaMultiplier;
        }

        scaledGradient.alphaKeys = alphaKeys;
        return scaledGradient;
    }

    private static Gradient CloneGradient(Gradient source)
    {
        Gradient clone = new();
        clone.SetKeys(source.colorKeys, source.alphaKeys);
        return clone;
    }

    private static Color WithScaledAlpha(Color color, float alphaMultiplier)
    {
        color.a *= alphaMultiplier;
        return color;
    }

    private static bool ColorApproximatelyEqual(Color left, Color right)
    {
        return Mathf.Abs(left.r - right.r) <= 0.001f &&
               Mathf.Abs(left.g - right.g) <= 0.001f &&
               Mathf.Abs(left.b - right.b) <= 0.001f &&
               Mathf.Abs(left.a - right.a) <= 0.001f;
    }

    private static bool LineColorsApproximatelyEqual(LineColors left, LineColors right)
    {
        return ColorApproximatelyEqual(left.Start, right.Start) &&
               ColorApproximatelyEqual(left.End, right.End);
    }

    private static bool GradientApproximatelyEqual(ParticleSystem.MinMaxGradient left, ParticleSystem.MinMaxGradient right)
    {
        if (left.mode != right.mode)
        {
            return false;
        }

        switch (left.mode)
        {
            case ParticleSystemGradientMode.Color:
                return ColorApproximatelyEqual(left.color, right.color);
            case ParticleSystemGradientMode.TwoColors:
                return ColorApproximatelyEqual(left.colorMin, right.colorMin) &&
                       ColorApproximatelyEqual(left.colorMax, right.colorMax);
            case ParticleSystemGradientMode.Gradient:
            case ParticleSystemGradientMode.RandomColor:
                return GradientApproximatelyEqual(left.gradient, right.gradient);
            case ParticleSystemGradientMode.TwoGradients:
                return GradientApproximatelyEqual(left.gradientMin, right.gradientMin) &&
                       GradientApproximatelyEqual(left.gradientMax, right.gradientMax);
            default:
                return ColorApproximatelyEqual(left.color, right.color);
        }
    }

    private static bool GradientApproximatelyEqual(Gradient? left, Gradient? right)
    {
        if (left == null || right == null)
        {
            return left == right;
        }

        GradientColorKey[] leftColorKeys = left.colorKeys;
        GradientColorKey[] rightColorKeys = right.colorKeys;
        if (leftColorKeys.Length != rightColorKeys.Length)
        {
            return false;
        }

        for (int i = 0; i < leftColorKeys.Length; i++)
        {
            if (Mathf.Abs(leftColorKeys[i].time - rightColorKeys[i].time) > 0.001f ||
                !ColorApproximatelyEqual(leftColorKeys[i].color, rightColorKeys[i].color))
            {
                return false;
            }
        }

        GradientAlphaKey[] leftAlphaKeys = left.alphaKeys;
        GradientAlphaKey[] rightAlphaKeys = right.alphaKeys;
        if (leftAlphaKeys.Length != rightAlphaKeys.Length)
        {
            return false;
        }

        for (int i = 0; i < leftAlphaKeys.Length; i++)
        {
            if (Mathf.Abs(leftAlphaKeys[i].time - rightAlphaKeys[i].time) > 0.001f ||
                Mathf.Abs(leftAlphaKeys[i].alpha - rightAlphaKeys[i].alpha) > 0.001f)
            {
                return false;
            }
        }

        return true;
    }

    private static string GetTransformPath(Transform transform)
    {
        List<string> parts = new();
        Transform? current = transform;
        while (current != null)
        {
            parts.Add(current.name);
            current = current.parent;
        }

        parts.Reverse();
        return string.Join("/", parts);
    }

    private static Transform? FindGraphicsSettingsContent(SettingsManager settings)
    {
        Transform? root = settings.graphicsSettingsObj != null ? settings.graphicsSettingsObj.transform : null;
        Dictionary<Transform, HashSet<Transform>> candidates = new();
        foreach (Component control in GetGraphicsControls(settings))
        {
            Transform controlTransform = control.transform;
            if (root != null && !controlTransform.IsChildOf(root))
            {
                continue;
            }

            Transform? candidate = controlTransform.parent;
            while (candidate != null && (root == null || candidate == root || candidate.IsChildOf(root)))
            {
                Transform? row = GetDirectChildUnder(candidate, controlTransform);
                if (row != null)
                {
                    if (!candidates.TryGetValue(candidate, out HashSet<Transform> rows))
                    {
                        rows = new HashSet<Transform>();
                        candidates[candidate] = rows;
                    }

                    rows.Add(row);
                }

                if (candidate == root)
                {
                    break;
                }

                candidate = candidate.parent;
            }
        }

        return candidates
            .Where(candidate => candidate.Value.Count > 1)
            .OrderByDescending(candidate => candidate.Value.Count)
            .ThenByDescending(candidate => GetTransformDepth(candidate.Key))
            .Select(candidate => candidate.Key)
            .FirstOrDefault();
    }

    private static List<Transform> FindKnownGraphicsRows(SettingsManager settings, Transform parent)
    {
        return GetGraphicsControls(settings)
            .Select(control => GetDirectChildUnder(parent, control.transform))
            .Where(row => row != null)
            .Distinct()
            .OrderBy(row => row!.GetSiblingIndex())
            .Cast<Transform>()
            .ToList();
    }

    private static IEnumerable<Component> GetGraphicsControls(SettingsManager settings)
    {
        if (settings.screenResolutionDropdown != null)
        {
            yield return settings.screenResolutionDropdown;
        }

        if (settings.fullscreenToggle != null)
        {
            yield return settings.fullscreenToggle;
        }

        if (settings.damageFlashToggle != null)
        {
            yield return settings.damageFlashToggle;
        }

        if (settings.screenShakeToggle != null)
        {
            yield return settings.screenShakeToggle;
        }

        if (settings.bloomToggle != null)
        {
            yield return settings.bloomToggle;
        }

        if (settings.fogToggle != null)
        {
            yield return settings.fogToggle;
        }

        if (settings.damageNumbersToggle != null)
        {
            yield return settings.damageNumbersToggle;
        }

        if (settings.goldNumbersToggle != null)
        {
            yield return settings.goldNumbersToggle;
        }

        if (settings.healingNumbersToggle != null)
        {
            yield return settings.healingNumbersToggle;
        }

        if (settings.enemySkillIndicatorToggle != null)
        {
            yield return settings.enemySkillIndicatorToggle;
        }
    }

    private static void PlaceSettingsRowAtEnd(Transform row, Transform parent, Transform sourceRow, List<Transform> referenceRows)
    {
        row.SetParent(parent, worldPositionStays: false);
        int lastGraphicsSibling = referenceRows
            .Where(referenceRow => referenceRow != row)
            .Select(referenceRow => referenceRow.GetSiblingIndex())
            .DefaultIfEmpty(parent.childCount - 1)
            .Max();
        row.SetSiblingIndex(Mathf.Clamp(lastGraphicsSibling + 1, 0, parent.childCount - 1));

        if (row is not RectTransform rowRect)
        {
            return;
        }

        if (sourceRow is RectTransform sourceRect)
        {
            rowRect.localScale = sourceRect.localScale;
            rowRect.sizeDelta = sourceRect.sizeDelta;
        }

        bool parentUsesLayout = parent.GetComponent<LayoutGroup>() != null;
        List<RectTransform> referenceRects = referenceRows
            .Where(referenceRow => referenceRow != row)
            .OfType<RectTransform>()
            .ToList();
        RectTransform? lastVisualRow = referenceRects
            .OrderBy(referenceRow => referenceRow.anchoredPosition.y)
            .FirstOrDefault();

        if (!parentUsesLayout && lastVisualRow != null)
        {
            rowRect.anchorMin = lastVisualRow.anchorMin;
            rowRect.anchorMax = lastVisualRow.anchorMax;
            rowRect.pivot = lastVisualRow.pivot;
            rowRect.anchoredPosition = new Vector2(
                lastVisualRow.anchoredPosition.x,
                lastVisualRow.anchoredPosition.y - EstimateRowStep(referenceRects, rowRect, lastVisualRow));
        }

        if (parent is RectTransform parentRect)
        {
            ExpandParentHeightToInclude(parentRect, rowRect);
            LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);
        }
    }

    private static Transform? GetDirectChildUnder(Transform ancestor, Transform descendant)
    {
        if (ancestor == descendant || !descendant.IsChildOf(ancestor))
        {
            return null;
        }

        Transform current = descendant;
        while (current.parent != null && current.parent != ancestor)
        {
            current = current.parent;
        }

        return current.parent == ancestor ? current : null;
    }

    private static int GetTransformDepth(Transform transform)
    {
        int depth = 0;
        Transform? current = transform;
        while (current != null)
        {
            depth++;
            current = current.parent;
        }

        return depth;
    }

    private static float EstimateRowStep(List<RectTransform> referenceRows, RectTransform rowRect, RectTransform lastVisualRow)
    {
        List<float> orderedY = referenceRows
            .Select(referenceRow => referenceRow.anchoredPosition.y)
            .Distinct()
            .OrderByDescending(y => y)
            .ToList();
        List<float> steps = new();
        for (int i = 1; i < orderedY.Count; i++)
        {
            float step = orderedY[i - 1] - orderedY[i];
            if (step > 1f)
            {
                steps.Add(step);
            }
        }

        if (steps.Count > 0)
        {
            return Mathf.Max(24f, steps.Average());
        }

        return Mathf.Max(36f, Mathf.Max(Mathf.Abs(lastVisualRow.sizeDelta.y), Mathf.Abs(rowRect.sizeDelta.y)) + 8f);
    }

    private static void ExpandParentHeightToInclude(RectTransform parentRect, RectTransform rowRect)
    {
        float rowBottom = Mathf.Abs(rowRect.anchoredPosition.y) + Mathf.Max(24f, Mathf.Abs(rowRect.sizeDelta.y));
        if (parentRect.sizeDelta.y < rowBottom + 16f)
        {
            parentRect.sizeDelta = new Vector2(parentRect.sizeDelta.x, rowBottom + 16f);
        }
    }

    private static Transform? FindDescendantByName(Transform root, string name)
    {
        if (root.name == name)
        {
            return root;
        }

        foreach (Transform child in root)
        {
            Transform? found = FindDescendantByName(child, name);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    [HarmonyPatch(typeof(SettingsManager), "SetUpSettingsUI")]
    private static class SettingsManagerSetUpSettingsUiPatch
    {
        private static void Postfix(SettingsManager __instance)
        {
            instance?.TryCreateSettingsSlider(__instance);
        }
    }

    [HarmonyPatch(typeof(ObjectPooler), nameof(ObjectPooler.GetObject))]
    private static class ObjectPoolerGetObjectPatch
    {
        private static void Postfix(GameObject __result, ObjectPooler.POOLED_OBJECT_TYPE type)
        {
            instance?.InstallVisibilityApplicator(__result, type);
        }
    }

    private sealed class VisibilityApplicator : MonoBehaviour
    {
        private ObjectPooler.POOLED_OBJECT_TYPE poolType;
        private bool pendingApply;

        public void Initialize(ObjectPooler.POOLED_OBJECT_TYPE newPoolType)
        {
            poolType = newPoolType;
            pendingApply = gameObject.activeInHierarchy;
        }

        private void OnEnable()
        {
            pendingApply = true;
        }

        private void LateUpdate()
        {
            if (!pendingApply)
            {
                return;
            }

            pendingApply = false;
            instance?.ApplyVisibilityToSpawnedPlayerObject(gameObject, poolType);
        }
    }

    private sealed class ParticleColorState
    {
        public ParticleColorState(ParticleSystem particleSystem)
        {
            ParticleSystem = particleSystem;
        }

        public ParticleSystem ParticleSystem { get; }
        public ParticleSystem.MinMaxGradient BaseGradient { get; set; }
        public ParticleSystem.MinMaxGradient AppliedGradient { get; set; }
        public bool HasApplied { get; set; }
    }

    private sealed class ColorState<TComponent>
        where TComponent : Component
    {
        public ColorState(TComponent component)
        {
            Component = component;
        }

        public TComponent Component { get; }
        public Color BaseColor { get; set; }
        public Color AppliedColor { get; set; }
        public bool HasApplied { get; set; }
    }

    private sealed class LineColorState<TComponent>
        where TComponent : Component
    {
        public LineColorState(TComponent component)
        {
            Component = component;
        }

        public TComponent Component { get; }
        public LineColors BaseColors { get; set; }
        public LineColors AppliedColors { get; set; }
        public bool HasApplied { get; set; }
    }

    private readonly struct LineColors
    {
        public LineColors(Color start, Color end)
        {
            Start = start;
            End = end;
        }

        public Color Start { get; }
        public Color End { get; }
    }
}
