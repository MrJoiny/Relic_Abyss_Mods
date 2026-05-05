using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace RelicAbyssExpBuffIcon;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class ExpBuffIconPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "joiny.relicabyss.expbufficon";
    public const string PluginName = "Relic Abyss Exp Buff Icon";
    public const string PluginVersion = "0.1.0";
    private const float ExperienceMonolithBonus = 0.3f;
    private const float ExperienceMonolithDuration = 30f;

    private static ExpBuffIconPlugin? instance;

    private readonly List<float> activeExperienceMonolithTimers = new();
    private ConfigEntry<float> iconX = null!;
    private ConfigEntry<float> iconY = null!;
    private ConfigEntry<float> iconSize = null!;
    private ManualLogSource log = null!;
    private GUIStyle? boxStyle;
    private GUIStyle? iconTitleStyle;
    private GUIStyle? iconStackStyle;
    private Harmony? harmony;

    private void Awake()
    {
        instance = this;
        log = Logger;
        iconX = Config.Bind("Buff Icon", "X", 18f, "Icon X position in screen pixels.");
        iconY = Config.Bind("Buff Icon", "Y", 96f, "Icon Y position in screen pixels.");
        iconSize = Config.Bind("Buff Icon", "IconSize", 84f, "Experience Monolith icon size in screen pixels.");

        harmony = new Harmony(PluginGuid);
        harmony.PatchAll();
        log.LogInfo($"{PluginName} {PluginVersion} loaded.");
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
        for (int i = 0; i < activeExperienceMonolithTimers.Count; i++)
        {
            activeExperienceMonolithTimers[i] -= Time.deltaTime;
        }
    }

    private void OnGUI()
    {
        if (activeExperienceMonolithTimers.Count == 0)
        {
            return;
        }

        EnsureIconStyles();

        int stacks = activeExperienceMonolithTimers.Count;
        float size = Mathf.Clamp(iconSize.Value, 56f, 160f);
        Rect rect = new(
            Mathf.Clamp(iconX.Value, 0f, Mathf.Max(0f, Screen.width - size)),
            Mathf.Clamp(iconY.Value, 0f, Mathf.Max(0f, Screen.height - size)),
            size,
            size);

        GUI.Box(rect, GUIContent.none, boxStyle);
        GUI.Label(new Rect(rect.x, rect.y + size * 0.13f, rect.width, size * 0.4f), "EXP", iconTitleStyle);
        GUI.Label(new Rect(rect.x, rect.y + size * 0.5f, rect.width, size * 0.34f), $"x{stacks}", iconStackStyle);
    }

    private void EnsureIconStyles()
    {
        if (boxStyle != null)
        {
            return;
        }

        boxStyle = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(10, 10, 10, 10)
        };
        iconTitleStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.47f, 0.9f, 1f) }
        };
        iconStackStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 24,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
    }

    private void TrackExperienceMonolithBuff(float amount)
    {
        if (Mathf.Abs(amount - ExperienceMonolithBonus) <= 0.001f)
        {
            activeExperienceMonolithTimers.Add(ExperienceMonolithDuration);
            return;
        }

        if (Mathf.Abs(amount + ExperienceMonolithBonus) <= 0.001f && activeExperienceMonolithTimers.Count > 0)
        {
            activeExperienceMonolithTimers.RemoveAt(GetShortestRemainingExperienceMonolithTimerIndex());
        }
    }

    private int GetShortestRemainingExperienceMonolithTimerIndex()
    {
        int index = 0;
        float shortest = activeExperienceMonolithTimers[0];
        for (int i = 1; i < activeExperienceMonolithTimers.Count; i++)
        {
            if (activeExperienceMonolithTimers[i] < shortest)
            {
                shortest = activeExperienceMonolithTimers[i];
                index = i;
            }
        }

        return index;
    }

    [HarmonyPatch(typeof(Stats), nameof(Stats.AddExpMultiplier))]
    private static class StatsAddExpMultiplierPatch
    {
        private static void Prefix(float amount)
        {
            instance?.TrackExperienceMonolithBuff(amount);
        }
    }
}
