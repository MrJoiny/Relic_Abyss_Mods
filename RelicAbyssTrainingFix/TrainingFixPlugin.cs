using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace RelicAbyssTrainingFix;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class TrainingFixPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "joiny.relicabyss.trainingfix";
    public const string PluginName = "Relic Abyss Training Fix";
    public const string PluginVersion = "0.1.0";

    private static ManualLogSource log = null!;
    private Harmony? harmony;

    private void Awake()
    {
        log = Logger;
        harmony = new Harmony(PluginGuid);
        harmony.PatchAll();
        log.LogInfo($"{PluginName} {PluginVersion} loaded. Training max-rank purchase cap patched from 9 to 10.");
    }

    private void OnDestroy()
    {
        harmony?.UnpatchSelf();
    }

    [HarmonyPatch(typeof(TrainingManager), nameof(TrainingManager.Train))]
    private static class TrainingManagerTrainPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new(instructions);
            FieldInfo currentTrainingItemField = AccessTools.Field(typeof(TrainingManager), nameof(TrainingManager.currentTrainingItem));
            FieldInfo trainingItemLevelField = AccessTools.Field(typeof(TrainingItem), nameof(TrainingItem.level));

            if (currentTrainingItemField == null || trainingItemLevelField == null)
            {
                log.LogWarning("Could not find training fields needed to patch TrainingManager.Train.");
                return codes;
            }

            for (int i = 0; i <= codes.Count - 3; i++)
            {
                if (LoadsField(codes[i], currentTrainingItemField) &&
                    LoadsField(codes[i + 1], trainingItemLevelField) &&
                    LoadsInt(codes[i + 2], 9))
                {
                    SetLoadInt(codes[i + 2], 10);
                    log.LogDebug("Patched TrainingManager.Train max-level guard from 9 to 10.");
                    return codes;
                }
            }

            log.LogWarning("Could not find anchored TrainingManager.Train max-level guard to patch.");
            return codes;
        }

        private static bool LoadsField(CodeInstruction instruction, FieldInfo field)
        {
            return instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, field);
        }

        private static bool LoadsInt(CodeInstruction instruction, int value)
        {
            if (instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int intValue)
            {
                return intValue == value;
            }

            if (instruction.opcode == OpCodes.Ldc_I4_S && instruction.operand is sbyte sbyteValue)
            {
                return sbyteValue == value;
            }

            return value switch
            {
                -1 => instruction.opcode == OpCodes.Ldc_I4_M1,
                0 => instruction.opcode == OpCodes.Ldc_I4_0,
                1 => instruction.opcode == OpCodes.Ldc_I4_1,
                2 => instruction.opcode == OpCodes.Ldc_I4_2,
                3 => instruction.opcode == OpCodes.Ldc_I4_3,
                4 => instruction.opcode == OpCodes.Ldc_I4_4,
                5 => instruction.opcode == OpCodes.Ldc_I4_5,
                6 => instruction.opcode == OpCodes.Ldc_I4_6,
                7 => instruction.opcode == OpCodes.Ldc_I4_7,
                8 => instruction.opcode == OpCodes.Ldc_I4_8,
                _ => false
            };
        }

        private static void SetLoadInt(CodeInstruction instruction, int value)
        {
            instruction.opcode = OpCodes.Ldc_I4_S;
            instruction.operand = (sbyte)value;
        }
    }
}
