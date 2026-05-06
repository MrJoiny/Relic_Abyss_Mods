using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RelicAbyssAdvancementTextFix;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class AdvancementTextFixPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "joiny.relicabyss.advancementtextfix";
    public const string PluginName = "Relic Abyss Advancement Text Fix";
    public const string PluginVersion = "0.1.0";

    private static readonly FieldInfo PerkItemRarityField = AccessTools.Field(typeof(PerkItemUI), "rarity");
    private static ManualLogSource log = null!;
    private Harmony? harmony;

    private void Awake()
    {
        log = Logger;
        harmony = new Harmony(PluginGuid);
        harmony.PatchAll();
        log.LogInfo($"{PluginName} {PluginVersion} loaded. Advancement text now uses applied stat values.");
    }

    private void OnDestroy()
    {
        harmony?.UnpatchSelf();
    }

    [HarmonyPatch(typeof(PerkItemUI))]
    [HarmonyPatch(nameof(PerkItemUI.Initialize), typeof(AdvancementData))]
    private static class PerkItemUIInitializeAdvancementPatch
    {
        private static void Postfix(PerkItemUI __instance, AdvancementData aData)
        {
            if (__instance.descriptionText == null || aData == null)
            {
                return;
            }

            if (PerkItemRarityField?.GetValue(__instance) is not EQUIPMENTRARITY rarity)
            {
                log.LogWarning("Could not read PerkItemUI rarity; leaving vanilla advancement text unchanged.");
                return;
            }

            __instance.descriptionText.text = AdvancementTextBuilder.BuildChoiceDescription(aData, rarity);
            AdvancementCardLayout.UseSingleTextArea(__instance);
        }
    }

    [HarmonyPatch(typeof(StatAdvancementItem))]
    [HarmonyPatch(nameof(StatAdvancementItem.Initialize))]
    private static class StatAdvancementItemInitializePatch
    {
        private static void Postfix(StatAdvancementItem __instance, AquiredAdvancement aRef)
        {
            if (aRef?.advancementData == null)
            {
                return;
            }

            TooltipHotspot tooltipHotspot = __instance.GetComponent<TooltipHotspot>();
            if (tooltipHotspot != null)
            {
                tooltipHotspot.textToDisplay = AdvancementTextBuilder.BuildAcquiredDescription(aRef);
            }
        }
    }

}

internal static class AdvancementTextBuilder
{
    public static string BuildChoiceDescription(AdvancementData data, EQUIPMENTRARITY rarity)
    {
        AquiredAdvancement? acquired = PlayerManager.playerStatManager?.GetAquiredAdvancement(data.advancementType);
        bool alreadyOwned = acquired != null && acquired.rarities.Count > 0;
        List<string> lines = BuildLines(data, new[] { rarity }, includeDerivedStats: true, includeRuntimeDependentValues: true, alreadyOwned: alreadyOwned);
        if (!alreadyOwned)
        {
            return JoinSections(("", lines));
        }

        List<EQUIPMENTRARITY> totalRarities = new(acquired!.rarities);
        totalRarities.Add(rarity);
        List<string> totalLines = BuildLines(data, totalRarities, includeDerivedStats: false, includeRuntimeDependentValues: false, alreadyOwned: true);
        return JoinSections(("This pick", lines), ("Total after pick", totalLines));
    }

    public static string BuildAcquiredDescription(AquiredAdvancement advancement)
    {
        List<string> lines = BuildLines(advancement.advancementData, advancement.rarities, includeDerivedStats: true, includeRuntimeDependentValues: false, alreadyOwned: true);
        return JoinSections(("Current total", lines));
    }

    private static List<string> BuildLines(AdvancementData data, IReadOnlyList<EQUIPMENTRARITY> rarities, bool includeDerivedStats, bool includeRuntimeDependentValues, bool alreadyOwned)
    {
        EffectSummary effect = new();
        foreach (EQUIPMENTRARITY rarity in rarities)
        {
            ApplyEffect(data, rarity, effect, includeRuntimeDependentValues, alreadyOwned);
        }

        return effect.ToLines(includeDerivedStats, includeRuntimeDependentValues);
    }

    private static void ApplyEffect(AdvancementData data, EQUIPMENTRARITY rarity, EffectSummary effect, bool includeRuntimeDependentValues, bool alreadyOwned)
    {
        float firstValue = data.advancementValues.Count > 0 ? data.advancementValues[0].value : 0f;
        float secondValue = data.advancementValues.Count > 1 ? data.advancementValues[1].value : 0f;
        float thirdValue = 0f;

        // The game's ApplyAdvancements method overwrites its second value with
        // value[2] and never assigns the third variable. Mirror that behavior so
        // the text describes the actual runtime effect instead of the asset intent.
        if (data.advancementValues.Count > 2)
        {
            secondValue = data.advancementValues[2].value;
        }

        float rarityPercent = rarity switch
        {
            EQUIPMENTRARITY.RARE => GameManager.instance?.gameGeneralData?.rarePercentValue ?? 0f,
            EQUIPMENTRARITY.EPIC => GameManager.instance?.gameGeneralData?.epicPercentValue ?? 0f,
            EQUIPMENTRARITY.LEGENDARY => GameManager.instance?.gameGeneralData?.legendaryPercentValue ?? 0f,
            _ => 0f
        };
        float rarityFraction = rarityPercent / 100f;
        int numericalRarity = rarity switch
        {
            EQUIPMENTRARITY.RARE => 1,
            EQUIPMENTRARITY.EPIC => 2,
            EQUIPMENTRARITY.LEGENDARY => 3,
            _ => 0
        };

        float Scaled(float value) => value + value * rarityFraction;
        float PercentValue(float value) => value + rarityPercent;
        float PercentFraction(float value) => value / 100f + rarityFraction;
        float NumericalValue(float value) => value + numericalRarity;

        switch (data.advancementType)
        {
            case ADVANCEMENT.STRENGTH:
                effect.Strength += Scaled(firstValue);
                break;
            case ADVANCEMENT.DEXTERITY:
                effect.Dexterity += Scaled(firstValue);
                break;
            case ADVANCEMENT.VITALITY:
                effect.Vitality += Scaled(firstValue);
                break;
            case ADVANCEMENT.AGILITY:
                effect.Agility += Scaled(firstValue);
                break;
            case ADVANCEMENT.INTELLIGENCE:
                effect.Intelligence += Scaled(firstValue);
                break;
            case ADVANCEMENT.PATIENCE:
                effect.Patience += Scaled(firstValue);
                break;
            case ADVANCEMENT.LUCK:
                effect.Luck += Scaled(firstValue);
                break;
            case ADVANCEMENT.MANIPULATION:
                effect.AddPercent("Projectile Size", PercentValue(firstValue));
                break;
            case ADVANCEMENT.FORCE:
                effect.AddPercent("Knockback", PercentValue(firstValue));
                break;
            case ADVANCEMENT.LEARNER:
                effect.AddPercent("Experience Gain", PercentValue(firstValue));
                break;
            case ADVANCEMENT.PREPARED:
                effect.AddPercent("Ultimate Cooldown", PercentValue(firstValue));
                break;
            case ADVANCEMENT.COWARDICE:
                effect.AddPercent("Dash Cooldown", PercentValue(firstValue));
                break;
            case ADVANCEMENT.BIGGER_POCKET:
                effect.AddFlat("Small Projectile Count", NumericalValue(firstValue), decimals: 0);
                break;
            case ADVANCEMENT.ARSENAL:
                effect.AddFlat("Medium Projectile Count", NumericalValue(firstValue), decimals: 0);
                break;
            case ADVANCEMENT.HUNTER:
                effect.AddPercent("Meat Spawn Rate", PercentValue(firstValue));
                break;
            case ADVANCEMENT.THIEF:
                effect.AddPercent("Gold Drop Chance", PercentValue(firstValue));
                break;
            case ADVANCEMENT.DEADLY:
                effect.AddPercent("Crit Dmg", PercentValue(firstValue));
                break;
            case ADVANCEMENT.CHRONOLOGIST:
                effect.AddPercent("Projectile Duration", PercentValue(firstValue));
                break;
            case ADVANCEMENT.PROTECTOR:
                effect.AddFlat("Max Shield", PercentFraction(firstValue), decimals: 2);
                break;
            case ADVANCEMENT.DESTROYER:
            case ADVANCEMENT.WILD_ATTACKS:
                effect.AddPercent("All Dmg", PercentValue(firstValue));
                if (data.advancementType == ADVANCEMENT.WILD_ATTACKS)
                {
                    effect.AddNote("Critical Chance is set to <color=red>0</color>");
                }
                break;
            case ADVANCEMENT.PIETY:
                effect.AddPercent("All Healing", PercentValue(firstValue));
                break;
            case ADVANCEMENT.FLASH:
                effect.AddFlat("Invulnerability Time", firstValue + 0.02f * numericalRarity, "s", decimals: 2);
                break;
            case ADVANCEMENT.SLOW_CASTER:
                effect.AddPercent("Cooldown", -PercentValue(firstValue));
                effect.AddPercent("Magic Dmg", PercentValue(secondValue));
                break;
            case ADVANCEMENT.GLASS_CANNON:
                effect.AddPercent("Max Health", -PercentValue(firstValue));
                effect.AddPercent("Physical Dmg", PercentValue(secondValue));
                break;
            case ADVANCEMENT.RISK_TAKER:
                effect.AddPercent("Dodge Chance", -PercentValue(firstValue));
                effect.AddPercent("Move Speed", PercentValue(secondValue));
                break;
            case ADVANCEMENT.LONG_REACH:
                effect.AddPercent("Pickup Range", PercentValue(firstValue));
                break;
            case ADVANCEMENT.STACKED:
                effect.AddFlat("Defense", Scaled(firstValue), decimals: 1);
                break;
            case ADVANCEMENT.GREEDY:
                effect.AddPercent("Max Health", -PercentValue(firstValue));
                effect.AddPercent("Gold Gain", PercentValue(firstValue));
                break;
            case ADVANCEMENT.HYBRID:
                effect.Strength += Scaled(firstValue);
                effect.Intelligence += Scaled(firstValue);
                break;
            case ADVANCEMENT.UTILITY:
                effect.Agility += Scaled(firstValue);
                effect.Luck += Scaled(firstValue);
                break;
            case ADVANCEMENT.TAKE_STANCE:
                effect.AddPercent("Attack Speed", PercentValue(firstValue));
                effect.AddPercent("Move Speed", -PercentValue(secondValue));
                break;
            case ADVANCEMENT.MICRO_MAGIC:
                effect.AddPercent("Cooldown", PercentValue(firstValue));
                effect.AddPercent("Magic Dmg", -PercentValue(secondValue));
                break;
            case ADVANCEMENT.RUNNER:
                effect.AddPercent("Move Speed", PercentValue(firstValue));
                effect.AddPercent("Attack Speed", -PercentValue(secondValue));
                effect.AddPercent("Cooldown", -PercentValue(thirdValue));
                break;
            case ADVANCEMENT.CURSED:
                effect.AddPercent("Enemy Health", PercentValue(firstValue));
                effect.AddPercent("Reward Quality", PercentValue(secondValue));
                break;
            case ADVANCEMENT.ENCUMBERED:
                effect.AddPercent("Move Speed", -PercentValue(firstValue));
                effect.AddPercent("Reward Quality", PercentValue(secondValue));
                break;
            case ADVANCEMENT.FACE_TANK:
                effect.AddSet("Invulnerability Time", "0s");
                effect.AddPercent("Max Health", PercentValue(secondValue));
                break;
            case ADVANCEMENT.HERMIT:
                effect.AddSet("Max Health", "10");
                effect.AddSet("Current Health", "10");
                if (includeRuntimeDependentValues)
                {
                    float currentMaxHealth = PlayerManager.playerStatManager?.currentStats?.GetMaxHealth() ?? 0f;
                    effect.AddFlat("Shield on pickup", currentMaxHealth * secondValue, decimals: 1);
                }
                else
                {
                    effect.AddSet("Shield on pickup", $"{FormatNumber(secondValue * 100f, 1)}% of max health at pickup");
                }
                break;
            case ADVANCEMENT.INFINITY_STORAGE:
                effect.AddFlat("Large Projectile Count", NumericalValue(firstValue), decimals: 0);
                break;
            case ADVANCEMENT.QUICK_HANDS:
                effect.AddPercent("Projectile Interval", PercentValue(firstValue));
                break;
            case ADVANCEMENT.TAUNT:
                effect.AddPercent("Special Enemy Spawns", PercentValue(firstValue));
                break;
            case ADVANCEMENT.SLIPPERY_FEET:
                effect.AddPercent("Dash Range", PercentValue(firstValue));
                break;
            case ADVANCEMENT.UNFAIR_TRADE:
                Stats? stats = PlayerManager.playerStatManager?.currentStats;
                string maxHealthDelta = "";
                if (stats?.characterStats != null)
                {
                    float healthBase = stats.characterStats.maxHealth +
                                       stats.vitality * stats.characterStats.maxHealthMultiplier * 10f +
                                       stats.bonusStats.bonusHealth +
                                       stats.trainingStats.bonusHealth +
                                       stats.buffStats.bonusHealth -
                                       stats.debuffStats.bonusHealth;
                    float delta = healthBase * -50f / 100f;
                    maxHealthDelta = $" ({Colorize(FormatSigned(delta, "", 1), delta)})";
                }

                effect.AddPercent("Max Health", -50f, maxHealthDelta);
                effect.AddPercent("Enemy Health", -PercentValue(firstValue));
                break;
            case ADVANCEMENT.OUTCAST:
                effect.AddFlat("Banishes", NumericalValue(firstValue), decimals: 0);
                break;
            case ADVANCEMENT.GAMBLER:
                effect.AddFlat("Rerolls", NumericalValue(firstValue), decimals: 0);
                break;
            case ADVANCEMENT.JAILOR:
                effect.AddFlat("Locks", NumericalValue(firstValue), decimals: 0);
                break;
            case ADVANCEMENT.JOUST:
                effect.MoveSpeedDamageMultiplier += PercentFraction(firstValue);
                break;
            case ADVANCEMENT.BULKING:
                effect.AddPercent("Max Health", Scaled(firstValue));
                effect.AddFlat("Defense", Scaled(secondValue), decimals: 1);
                break;
            case ADVANCEMENT.TOOL_MASTER:
                effect.AddSet("Next 3 reward slots", "Perks");
                break;
            case ADVANCEMENT.EVOLUTION:
                effect.AddSet("Next 3 reward slots", "Advancements");
                break;
            case ADVANCEMENT.MENACING_AURA:
                effect.PickupRangeDamageMultiplier += PercentFraction(firstValue);
                if (includeRuntimeDependentValues && !alreadyOwned)
                {
                    effect.AddSet("Pickup Range Damage Aura", "enabled");
                }
                break;
            case ADVANCEMENT.FLIP_FLOP:
                effect.AddSet("Dmg Type", "physical and magic are swapped");
                effect.AddNote("Additional levels do not change this in the current game code.");
                break;
            case ADVANCEMENT.GOLDEN_HEART:
                effect.AddSet("On taking damage", "15% chance to spawn 5 gold");
                effect.AddNote("Additional levels do not increase the chance or gold amount in the current game code.");
                break;
            case ADVANCEMENT.GOLDEN_EDGE:
                float gold = DataManager.instance?.transientSaveData?.gold ?? 0f;
                float flatDamage = gold / 500f;
                effect.AddSet("Hit Dmg", $"{FormatSigned(flatDamage, "", 1)} from current gold");
                effect.AddNote("Additional levels do not change this in the current game code.");
                break;
            case ADVANCEMENT.GOLDEN_SHIELD:
                effect.AddSet("Dmg Taken", "gold is spent before health is lost");
                effect.AddNote("Additional levels do not change this in the current game code.");
                break;
            default:
                effect.AddNote("No direct stat change was found for this advancement in the current game code.");
                break;
        }
    }

    private static string JoinSections(params (string Header, List<string> Lines)[] sections)
    {
        StringBuilder builder = new();
        foreach ((string header, List<string> lines) in sections)
        {
            if (lines.Count == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            if (!string.IsNullOrEmpty(header))
            {
                builder.AppendLine($"<b>{header}</b>");
            }

            foreach (string line in lines)
            {
                builder.AppendLine(line);
            }
        }

        return builder.ToString().TrimEnd();
    }

    private sealed class EffectSummary
    {
        private readonly List<ValueLine> valueLines = new();
        private readonly List<SetLine> setLines = new();
        private readonly List<string> notes = new();

        public float Strength { get; set; }
        public float Dexterity { get; set; }
        public float Vitality { get; set; }
        public float Agility { get; set; }
        public float Intelligence { get; set; }
        public float Patience { get; set; }
        public float Luck { get; set; }
        public float MoveSpeedDamageMultiplier { get; set; }
        public float PickupRangeDamageMultiplier { get; set; }

        public void AddFlat(string label, float value, string suffix = "", int decimals = 1)
        {
            AddValue(label, value, suffix, decimals);
        }

        public void AddPercent(string label, float value, string detail = "")
        {
            AddValue(label, value, "%", 1, detail);
        }

        public void AddSet(string label, string value)
        {
            foreach (SetLine line in setLines)
            {
                if (line.Label == label && line.Value == value)
                {
                    return;
                }
            }

            setLines.Add(new SetLine(label, value));
        }

        public void AddNote(string text)
        {
            if (!notes.Contains(text))
            {
                notes.Add(text);
            }
        }

        public List<string> ToLines(bool includeDerivedStats, bool includeRuntimeDependentValues)
        {
            List<string> lines = new();
            AddPrimaryStatLines(lines, includeDerivedStats);

            foreach (ValueLine line in valueLines)
            {
                if (!NearlyZero(line.Value))
                {
                    string detail = string.IsNullOrEmpty(line.Detail) ? GetPercentDeltaDetail(line.Label, line.Value) : line.Detail;
                    lines.Add($"{line.Label}: {Colorize(FormatSigned(line.Value, line.Suffix, line.Decimals), line.Value)}{detail}");
                }
            }

            if (!NearlyZero(MoveSpeedDamageMultiplier))
            {
                lines.Add($"Dmg From Move Speed: {Colorize(FormatSigned(MoveSpeedDamageMultiplier, " x Move Speed", 2), MoveSpeedDamageMultiplier)}");
                if (includeRuntimeDependentValues)
                {
                    float currentMoveSpeed = PlayerManager.playerStatManager?.currentStats?.GetMoveSpeed() ?? 0f;
                    float currentDamage = MoveSpeedDamageMultiplier * currentMoveSpeed;
                    lines.Add($"Current flat dmg: {Colorize(FormatSigned(currentDamage, "", 1), currentDamage)}");
                }
            }

            if (!NearlyZero(PickupRangeDamageMultiplier))
            {
                lines.Add($"Aura Dmg: {Colorize(FormatSigned(PickupRangeDamageMultiplier, " x Attack Dmg", 2), PickupRangeDamageMultiplier)}");
            }

            foreach (SetLine line in setLines)
            {
                lines.Add($"{line.Label}: <color=yellow>{line.Value}</color>");
            }

            foreach (string note in notes)
            {
                lines.Add(note);
            }

            return lines;
        }

        private void AddValue(string label, float value, string suffix, int decimals, string detail = "")
        {
            for (int i = 0; i < valueLines.Count; i++)
            {
                ValueLine line = valueLines[i];
                if (line.Label == label && line.Suffix == suffix && line.Decimals == decimals)
                {
                    string mergedDetail = string.IsNullOrEmpty(line.Detail) ? detail : line.Detail;
                    valueLines[i] = new ValueLine(line.Label, line.Value + value, line.Suffix, line.Decimals, mergedDetail);
                    return;
                }
            }

            valueLines.Add(new ValueLine(label, value, suffix, decimals, detail));
        }

        private void AddPrimaryStatLines(List<string> lines, bool includeDerivedStats)
        {
            List<string> derivedLines = new();
            AddPrimaryStatLine(lines, derivedLines, "Strength", Strength,
                includeDerivedStats ? GetPhysicalDamageDelta(Strength) : EmptyDerivedLines());
            AddPrimaryStatLine(lines, derivedLines, "Dexterity", Dexterity,
                includeDerivedStats
                    ? GetSingleStatDelta("Attack Speed", Dexterity,
                        (stats, dexterity) => stats.GetModifiedAttackSpeed(dexterity) - stats.GetAttackSpeed())
                    : EmptyDerivedLines());
            AddPrimaryStatLine(lines, derivedLines, "Vitality", Vitality,
                includeDerivedStats ? GetVitalityDelta(Vitality) : EmptyDerivedLines());
            AddPrimaryStatLine(lines, derivedLines, "Agility", Agility,
                includeDerivedStats ? GetAgilityDelta(Agility) : EmptyDerivedLines());
            AddPrimaryStatLine(lines, derivedLines, "Intelligence", Intelligence,
                includeDerivedStats ? GetMagicDamageDelta(Intelligence) : EmptyDerivedLines());
            AddPrimaryStatLine(lines, derivedLines, "Patience", Patience,
                includeDerivedStats
                    ? GetSingleStatDelta("Cooldown", Patience,
                        (stats, patience) => stats.GetModifiedCooldownReduct(patience) - stats.GetCooldownReduct())
                    : EmptyDerivedLines());
            AddPrimaryStatLine(lines, derivedLines, "Luck", Luck,
                includeDerivedStats ? GetLuckDelta(Luck) : EmptyDerivedLines());

            if (derivedLines.Count == 0)
            {
                return;
            }

            lines.Add("");
            lines.Add("<b>Also changes:</b>");
            lines.AddRange(derivedLines);
        }

        private static void AddPrimaryStatLine(List<string> lines, List<string> derivedOutput, string label, float value, List<string> derivedLines)
        {
            if (NearlyZero(value))
            {
                return;
            }

            lines.Add($"{label}: {Colorize(FormatSigned(value, "", 1), value)}");
            derivedOutput.AddRange(derivedLines);
        }

        private static List<string> GetPhysicalDamageDelta(float strength)
        {
            Stats? stats = PlayerManager.playerStatManager?.currentStats;
            if (stats == null)
            {
                return EmptyDerivedLines();
            }

            float min = stats.GetModifiedMinPhysicalDamage(strength) - stats.GetMinPhysicalDamage();
            float max = stats.GetModifiedMaxPhysicalDamage(strength) - stats.GetMaxPhysicalDamage();
            return new List<string> { $"Physical Dmg: {Colorize(FormatRange(min, max), (min + max) / 2f)}" };
        }

        private static List<string> GetMagicDamageDelta(float intelligence)
        {
            Stats? stats = PlayerManager.playerStatManager?.currentStats;
            if (stats == null)
            {
                return EmptyDerivedLines();
            }

            float min = stats.GetModifiedMinMagicalDamage(intelligence) - stats.GetMinMagicalDamage();
            float max = stats.GetModifiedMaxMagicalDamage(intelligence) - stats.GetMaxMagicalDamage();
            return new List<string> { $"Magic Dmg: {Colorize(FormatRange(min, max), (min + max) / 2f)}" };
        }

        private static List<string> GetVitalityDelta(float vitality)
        {
            Stats? stats = PlayerManager.playerStatManager?.currentStats;
            if (stats == null)
            {
                return EmptyDerivedLines();
            }

            float health = stats.GetModifiedMaxHealth(vitality) - stats.GetMaxHealth();
            float regen = stats.GetModifiedRegeneration(vitality) - stats.GetRegeneration();
            return new List<string>
            {
                $"Max Health: {Colorize(FormatSigned(health, "", 1), health)}",
                $"Regeneration: {Colorize(FormatSigned(regen, "", 2), regen)}"
            };
        }

        private static List<string> GetAgilityDelta(float agility)
        {
            Stats? stats = PlayerManager.playerStatManager?.currentStats;
            if (stats == null)
            {
                return EmptyDerivedLines();
            }

            float moveSpeed = stats.GetModifiedMoveSpeed(agility) - stats.GetMoveSpeed();
            float dodge = stats.GetModifiedDodge(agility) - stats.GetDodge();
            return new List<string>
            {
                $"Move Speed: {Colorize(FormatSigned(moveSpeed, "", 2), moveSpeed)}",
                $"Dodge: {Colorize(FormatSigned(dodge, "%", 2), dodge)}"
            };
        }

        private static List<string> GetLuckDelta(float luck)
        {
            Stats? stats = PlayerManager.playerStatManager?.currentStats;
            if (stats == null)
            {
                return EmptyDerivedLines();
            }

            float crit = stats.GetModifiedCritChance(luck) - stats.GetCritChance();
            float loot = 0f;
            if (GameManager.instance?.gameGeneralData != null)
            {
                loot = luck / 100f * GameManager.instance.gameGeneralData.luckLootPercent;
            }

            return new List<string>
            {
                $"Crit Chance: {Colorize(FormatSigned(crit, "%", 2), crit)}",
                $"Loot Rarity: {Colorize(FormatSigned(loot, "%", 2), loot)}"
            };
        }

        private static List<string> GetSingleStatDelta(string label, float value, Func<Stats, float, float> getDelta)
        {
            Stats? stats = PlayerManager.playerStatManager?.currentStats;
            if (stats == null)
            {
                return EmptyDerivedLines();
            }

            float delta = getDelta(stats, value);
            return new List<string> { $"{label}: {Colorize(FormatSigned(delta, "", 2), delta)}" };
        }

        private static List<string> EmptyDerivedLines()
        {
            return new List<string>();
        }

    }

    private readonly struct ValueLine
    {
        public ValueLine(string label, float value, string suffix, int decimals, string detail)
        {
            Label = label;
            Value = value;
            Suffix = suffix;
            Decimals = decimals;
            Detail = detail;
        }

        public string Label { get; }
        public float Value { get; }
        public string Suffix { get; }
        public int Decimals { get; }
        public string Detail { get; }
    }

    private readonly struct SetLine
    {
        public SetLine(string label, string value)
        {
            Label = label;
            Value = value;
        }

        public string Label { get; }
        public string Value { get; }
    }

    private static string FormatRange(float min, float max)
    {
        if (Math.Abs(min - max) < 0.05f)
        {
            return FormatSigned((min + max) / 2f, "", 1);
        }

        string sign = (min + max) / 2f >= 0f ? "+" : "";
        return $"{sign}{FormatNumber(min, 1)} to {sign}{FormatNumber(max, 1)}";
    }

    private static string FormatSigned(float value, string suffix, int decimals)
    {
        string sign = value > 0f ? "+" : "";
        return $"{sign}{FormatNumber(value, decimals)}{suffix}";
    }

    private static string GetPercentDeltaDetail(string label, float percent)
    {
        Stats? stats = PlayerManager.playerStatManager?.currentStats;
        if (stats?.characterStats == null)
        {
            return "";
        }

        return label switch
        {
            "Max Health" => FormatDeltaDetail(GetMaxHealthDelta(stats, percent), "", 1),
            "Physical Dmg" => FormatRangeDeltaDetail(
                GetDamageDelta(stats.GetMinPhysicalDamage(), stats.allDamage, percent),
                GetDamageDelta(stats.GetMaxPhysicalDamage(), stats.allDamage, percent)),
            "Magic Dmg" => FormatRangeDeltaDetail(
                GetDamageDelta(stats.GetMinMagicalDamage(), stats.allDamage, percent),
                GetDamageDelta(stats.GetMaxMagicalDamage(), stats.allDamage, percent)),
            "Defense" => FormatDeltaDetail(GetSimplePercentDelta(GetDefenseBase(stats), percent), "", 1),
            "Move Speed" => FormatDeltaDetail(GetSimplePercentDelta(GetMoveSpeedBase(stats), percent), "", 2),
            "Attack Speed" => FormatDeltaDetail(GetSimplePercentDelta(GetAttackSpeedBase(stats), percent), "", 2),
            "Dodge Chance" => FormatDeltaDetail(GetClampedPercentDelta(GetDodgeBase(stats), GetCurrentDodgePercent(stats), percent, 0f, GameManager.instance.gameGeneralData.maxDodgePercent), "%", 2),
            "Cooldown" => FormatDeltaDetail(GetSimplePercentDelta(GetCooldownReductionBase(stats), percent), "", 2),
            "Pickup Range" => FormatDeltaDetail(GetSimplePercentDelta(GetPickupRangeBase(stats), percent), "", 2),
            "Crit Dmg" => FormatDeltaDetail(GetSimplePercentDelta(GetCritDamageBase(stats), percent), "", 1),
            "Critical Chance" => FormatDeltaDetail(GetClampedPercentDelta(GetCritChanceBase(stats), GetCurrentCritChancePercent(stats), percent, 0f, 100f), "%", 2),
            _ => ""
        };
    }

    private static string FormatDeltaDetail(float delta, string suffix, int decimals)
    {
        if (NearlyZero(delta))
        {
            return "";
        }

        return $" ({Colorize(FormatSigned(delta, suffix, decimals), delta)})";
    }

    private static string FormatRangeDeltaDetail(float min, float max)
    {
        if (NearlyZero(min) && NearlyZero(max))
        {
            return "";
        }

        return $" ({Colorize(FormatRange(min, max), (min + max) / 2f)})";
    }

    private static float GetDamageDelta(float baseDamage, float allDamage, float percent)
    {
        float delta = GetSimplePercentDelta(baseDamage, percent);
        return delta + delta * allDamage;
    }

    private static float GetSimplePercentDelta(float baseValue, float percent)
    {
        return baseValue * percent / 100f;
    }

    private static float GetClampedPercentDelta(float baseValue, float currentPercent, float addedPercent, float min, float max)
    {
        float current = Mathf.Clamp(baseValue + baseValue * currentPercent / 100f, min, max);
        float after = Mathf.Clamp(baseValue + baseValue * (currentPercent + addedPercent) / 100f, min, max);
        return after - current;
    }

    private static float GetMaxHealthBase(Stats stats)
    {
        return stats.characterStats.maxHealth +
               stats.vitality * stats.characterStats.maxHealthMultiplier * 10f +
               stats.bonusStats.bonusHealth +
               stats.trainingStats.bonusHealth +
               stats.buffStats.bonusHealth -
               stats.debuffStats.bonusHealth;
    }

    private static float GetMaxHealthDelta(Stats stats, float percent)
    {
        return PlayerManager.playerStatManager.HasAdvancement(ADVANCEMENT.HERMIT)
            ? 0f
            : GetSimplePercentDelta(GetMaxHealthBase(stats), percent);
    }

    private static float GetDefenseBase(Stats stats)
    {
        return stats.characterStats.defense +
               stats.bonusStats.bonusDefense +
               stats.trainingStats.bonusDefense +
               stats.buffStats.bonusDefense -
               stats.debuffStats.bonusDefense;
    }

    private static float GetMoveSpeedBase(Stats stats)
    {
        return stats.characterStats.moveSpeed +
               stats.agility * stats.characterStats.moveSpeedMultiplier +
               stats.bonusStats.bonusMoveSpeed +
               stats.trainingStats.bonusMoveSpeed +
               stats.buffStats.bonusMoveSpeed -
               stats.debuffStats.bonusMoveSpeed;
    }

    private static float GetAttackSpeedBase(Stats stats)
    {
        return stats.characterStats.attackSpeed +
               stats.dexterity * stats.characterStats.attackSpeedMultiplier +
               stats.bonusStats.bonusAttackSpeed +
               stats.trainingStats.bonusAttackSpeed +
               stats.buffStats.bonusAttackSpeed -
               stats.debuffStats.bonusAttackSpeed;
    }

    private static float GetDodgeBase(Stats stats)
    {
        return stats.characterStats.dodge +
               stats.agility * stats.characterStats.dodgeMultiplier +
               stats.bonusStats.bonusDodge +
               stats.trainingStats.bonusDodge +
               stats.buffStats.bonusDodge -
               stats.debuffStats.bonusDodge;
    }

    private static float GetCurrentDodgePercent(Stats stats)
    {
        return stats.bonusStats.bonusPercentDodge +
               stats.trainingStats.bonusPercentDodge +
               stats.buffStats.bonusPercentDodge -
               stats.debuffStats.bonusPercentDodge;
    }

    private static float GetCooldownReductionBase(Stats stats)
    {
        return stats.characterStats.cooldownReduct +
               stats.patience * stats.characterStats.cooldownReductionMultiplier +
               stats.bonusStats.bonusCooldownReduct +
               stats.trainingStats.bonusCooldownReduct +
               stats.buffStats.bonusCooldownReduct -
               stats.debuffStats.bonusCooldownReduct;
    }

    private static float GetPickupRangeBase(Stats stats)
    {
        return stats.characterStats.pickupRange +
               stats.bonusStats.bonusPickupRange +
               stats.trainingStats.bonusPickupRange +
               stats.buffStats.bonusPickupRange -
               stats.debuffStats.bonusPickupRange;
    }

    private static float GetCritDamageBase(Stats stats)
    {
        return stats.characterStats.critDmg +
               stats.bonusStats.bonusCritDmg +
               stats.trainingStats.bonusCritDmg +
               stats.buffStats.bonusCritDmg -
               stats.debuffStats.bonusCritDmg;
    }

    private static float GetCritChanceBase(Stats stats)
    {
        return stats.characterStats.critChance +
               stats.luck * stats.characterStats.critChanceMultiplier +
               stats.bonusStats.bonusCritChance +
               stats.trainingStats.bonusCritChance +
               stats.buffStats.bonusCritChance -
               stats.debuffStats.bonusCritChance;
    }

    private static float GetCurrentCritChancePercent(Stats stats)
    {
        return stats.bonusStats.bonusPercentCritChance +
               stats.trainingStats.bonusPercentCritChance +
               stats.buffStats.bonusPercentCritChance -
               stats.debuffStats.bonusPercentCritChance;
    }

    private static string FormatNumber(float value, int decimals)
    {
        return Math.Abs(value % 1f) < 0.05f
            ? value.ToString("F0")
            : value.ToString($"F{decimals}");
    }

    private static string Colorize(string text, float value)
    {
        if (NearlyZero(value))
        {
            return text;
        }

        return value > 0f ? $"<color=green>{text}</color>" : $"<color=red>{text}</color>";
    }

    private static bool NearlyZero(float value)
    {
        return Math.Abs(value) < 0.0001f;
    }

}

internal static class AdvancementCardLayout
{
    public static void UseSingleTextArea(PerkItemUI item)
    {
        if (item.descriptionText == null || item.projectileDetailsText == null)
        {
            return;
        }

        TextMeshProUGUI descriptionText = item.descriptionText;
        TextMeshProUGUI detailsText = item.projectileDetailsText;
        descriptionText.enableWordWrapping = true;
        descriptionText.overflowMode = TextOverflowModes.Overflow;
        descriptionText.enableAutoSizing = true;
        descriptionText.fontSizeMax = descriptionText.fontSize;
        descriptionText.fontSizeMin = Mathf.Max(24f, descriptionText.fontSize * 0.65f);
        detailsText.text = "";

        RectTransform descriptionRect = descriptionText.rectTransform;
        ScrollRect? scrollRect = detailsText.GetComponentInParent<ScrollRect>(includeInactive: true);
        RectTransform? detailsRoot = scrollRect != null
            ? scrollRect.transform as RectTransform
            : detailsText.transform.parent as RectTransform;
        if (detailsRoot == null)
        {
            return;
        }

        HideSeparatorsBetween(descriptionRect, detailsRoot, item.rarityText?.rectTransform, item.transform);
        RectTransform? commonParent = descriptionRect.parent as RectTransform;
        if (commonParent != null && detailsRoot.parent == commonParent)
        {
            Vector3[] descriptionCorners = new Vector3[4];
            Vector3[] targetCorners = new Vector3[4];
            descriptionRect.GetWorldCorners(descriptionCorners);
            detailsRoot.GetWorldCorners(targetCorners);

            Vector2 min = WorldToLocal(commonParent, descriptionCorners[0]);
            Vector2 max = WorldToLocal(commonParent, descriptionCorners[2]);
            ExpandBounds(commonParent, targetCorners[0], ref min, ref max);
            ExpandBounds(commonParent, targetCorners[2], ref min, ref max);

            descriptionRect.anchorMin = new Vector2(0.5f, 0.5f);
            descriptionRect.anchorMax = new Vector2(0.5f, 0.5f);
            descriptionRect.pivot = new Vector2(0.5f, 0.5f);
            descriptionRect.anchoredPosition = (min + max) * 0.5f;
            descriptionRect.sizeDelta = max - min;
        }

        detailsRoot.gameObject.SetActive(false);
    }

    private static void ExpandBounds(RectTransform parent, Vector3 worldPoint, ref Vector2 min, ref Vector2 max)
    {
        Vector2 local = WorldToLocal(parent, worldPoint);
        min = Vector2.Min(min, local);
        max = Vector2.Max(max, local);
    }

    private static Vector2 WorldToLocal(RectTransform parent, Vector3 worldPoint)
    {
        Vector3 localPoint = parent.InverseTransformPoint(worldPoint);
        return new Vector2(localPoint.x, localPoint.y);
    }

    private static void HideSeparatorsBetween(RectTransform descriptionRect, RectTransform detailsRoot, RectTransform? rarityRect, Transform cardRoot)
    {
        Rect worldBounds = GetWorldBounds(descriptionRect);
        worldBounds = Encapsulate(worldBounds, GetWorldBounds(detailsRoot));
        if (rarityRect != null)
        {
            worldBounds = Encapsulate(worldBounds, GetWorldBounds(rarityRect));
        }

        float lowerY = Math.Min(GetWorldBounds(descriptionRect).yMin, GetWorldBounds(detailsRoot).yMin);
        float upperY = rarityRect != null
            ? GetWorldBounds(rarityRect).yMin
            : Math.Max(GetWorldCenter(descriptionRect).y, GetWorldCenter(detailsRoot).y);
        float contentWidth = worldBounds.width;
        List<float> dividerYs = new();

        Graphic[] graphics = cardRoot.GetComponentsInChildren<Graphic>(includeInactive: true);
        foreach (Graphic graphic in graphics)
        {
            if (graphic is TextMeshProUGUI || graphic.transform == descriptionRect || graphic.transform == detailsRoot)
            {
                continue;
            }

            RectTransform? rect = graphic.transform as RectTransform;
            if (rect == null)
            {
                continue;
            }

            Rect candidateBounds = GetWorldBounds(rect);
            Vector2 candidateCenter = GetWorldCenter(rect);
            bool isHorizontalSeparator = candidateBounds.height > 0f &&
                                         candidateBounds.height <= 36f &&
                                         candidateBounds.width >= contentWidth * 0.45f;
            bool isInAdvancementBody = candidateCenter.x >= worldBounds.xMin &&
                                       candidateCenter.x <= worldBounds.xMax &&
                                       candidateCenter.y >= lowerY &&
                                       candidateCenter.y <= upperY;

            if (isHorizontalSeparator && isInAdvancementBody)
            {
                graphic.gameObject.SetActive(false);
                dividerYs.Add(candidateCenter.y);
            }
        }

        if (dividerYs.Count == 0)
        {
            return;
        }

        foreach (Graphic graphic in graphics)
        {
            if (!graphic.gameObject.activeSelf || graphic is TextMeshProUGUI)
            {
                continue;
            }

            RectTransform? rect = graphic.transform as RectTransform;
            if (rect == null)
            {
                continue;
            }

            Rect candidateBounds = GetWorldBounds(rect);
            Vector2 candidateCenter = GetWorldCenter(rect);
            bool isSmallEndCap = candidateBounds.width <= 48f && candidateBounds.height <= 48f;
            bool isInAdvancementBody = candidateCenter.x >= worldBounds.xMin &&
                                       candidateCenter.x <= worldBounds.xMax &&
                                       candidateCenter.y >= lowerY &&
                                       candidateCenter.y <= upperY;
            if (!isSmallEndCap || !isInAdvancementBody)
            {
                continue;
            }

            foreach (float dividerY in dividerYs)
            {
                if (Math.Abs(candidateCenter.y - dividerY) <= 24f)
                {
                    graphic.gameObject.SetActive(false);
                    break;
                }
            }
        }
    }

    private static Rect GetWorldBounds(RectTransform rectTransform)
    {
        Vector3[] corners = new Vector3[4];
        rectTransform.GetWorldCorners(corners);
        float minX = corners[0].x;
        float minY = corners[0].y;
        float maxX = corners[0].x;
        float maxY = corners[0].y;

        for (int i = 1; i < corners.Length; i++)
        {
            minX = Math.Min(minX, corners[i].x);
            minY = Math.Min(minY, corners[i].y);
            maxX = Math.Max(maxX, corners[i].x);
            maxY = Math.Max(maxY, corners[i].y);
        }

        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    private static Rect Encapsulate(Rect first, Rect second)
    {
        return Rect.MinMaxRect(
            Math.Min(first.xMin, second.xMin),
            Math.Min(first.yMin, second.yMin),
            Math.Max(first.xMax, second.xMax),
            Math.Max(first.yMax, second.yMax));
    }

    private static Vector2 GetWorldCenter(RectTransform rectTransform)
    {
        Rect bounds = GetWorldBounds(rectTransform);
        return bounds.center;
    }
}
