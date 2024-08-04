using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MonoMod.Utils;
using Object = UnityEngine.Object;

namespace NoCompanyPenalty.Patches;

[HarmonyPatch(typeof(StartOfRound))]
public static class StartOfRoundPatch {
    [HarmonyPatch(nameof(StartOfRound.EndOfGame), MethodType.Enumerator)]
    [HarmonyPostfix]
    private static void SyncMoneyAfterEndOfGame() {
        var terminal = Object.FindObjectOfType<Terminal>();

        if (terminal == null || !terminal) return;

        if (terminal is {
                IsHost: false, IsServer: false,
            }) return;

        terminal.SyncGroupCreditsClientRpc(terminal.groupCredits, terminal.numberOfItemsInDropship);
    }

    [HarmonyPatch(nameof(StartOfRound.EndOfGame), MethodType.Enumerator)]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> CheckDeadPlayers(IEnumerable<CodeInstruction> instructions) {
        var originalInstructions = instructions.ToList();
        var codeInstructions = new List<CodeInstruction>(originalInstructions);

        try {
            /*NoCompanyPenalty.Logger.LogDebug("Before:");
            foreach (var codeInstruction in codes) NoCompanyPenalty.Logger.LogDebug(codeInstruction);
            NoCompanyPenalty.Logger.LogDebug("~~~~~~~~~~");*/

            // IL code pattern to look for
            /*
               IL_0321: ldarg.0      // this
               IL_0322: ldloc.1      // startOfRound
               IL_0323: ldfld        int32 StartOfRound::connectedPlayersAmount
               IL_0328: ldc.i4.1
               IL_0329: add
               IL_032a: ldloc.1      // startOfRound
               IL_032b: ldfld        int32 StartOfRound::livingPlayers
               IL_0330: sub
               IL_0331: stfld        int32 StartOfRound/'<EndOfGame>d__278'::'<playersDead>5__2'
             */

            var targetInstructionIndex = -1;

            // Find the instruction where playersDead is calculated
            for (var index = 0; index < codeInstructions.Count - 6; index++) {
                if (codeInstructions[index].opcode != OpCodes.Add
                 || codeInstructions[index + 1].opcode != OpCodes.Ldloc_1
                 || codeInstructions[index + 2].opcode != OpCodes.Ldfld
                 || codeInstructions[index + 3].opcode != OpCodes.Sub
                 || codeInstructions[index + 4].opcode != OpCodes.Stfld) continue;
                targetInstructionIndex = index + 5; // Position after storing playersDead
                break;
            }

            if (targetInstructionIndex == -1) {
                NoCompanyPenalty.Logger.LogFatal("Could not find pattern!");
                return originalInstructions;
            }

            // We need to load the address of the 'playersDead' field, so let's find its local index
            var storeFieldInstruction = codeInstructions[targetInstructionIndex - 1];
            if (storeFieldInstruction.operand is not FieldInfo playersDeadField) {
                if (storeFieldInstruction.operand != null) {
                    NoCompanyPenalty.Logger.LogFatal($"Unexpected operand type, expected FieldInfo, but got {storeFieldInstruction.operand.GetType()}"
                                                   + $" ({storeFieldInstruction})!");
                } else {
                    NoCompanyPenalty.Logger.LogFatal($"Unexpected operand type, expected FieldInfo, but got {null} ({storeFieldInstruction})!");
                }

                return originalInstructions;
            }

            NoCompanyPenalty.Logger.LogInfo($"Pattern found! Injecting {typeof(StartOfRoundPatch)}.{nameof(SetDead)}!");

            var setDeadMethod = typeof(StartOfRoundPatch).GetMethod(nameof(SetDead));

            // Load the address of the playersDead field and call SetDead
            codeInstructions.Insert(targetInstructionIndex, new(OpCodes.Ldarg_0)); // Load 'this'
            codeInstructions.Insert(targetInstructionIndex + 1, new(OpCodes.Ldflda, playersDeadField));
            codeInstructions.Insert(targetInstructionIndex + 2, new(OpCodes.Call, setDeadMethod));

            /*NoCompanyPenalty.Logger.LogDebug("After:");
            foreach (var codeInstruction in codes) NoCompanyPenalty.Logger.LogDebug(codeInstruction);
            NoCompanyPenalty.Logger.LogDebug("~~~~~~~~~~");*/
            return codeInstructions;
        } catch (Exception exception) {
            exception.LogDetailed();

            return originalInstructions;
        }
    }

    public static void SetDead(ref int deadPlayers) {
        if (StartOfRound.Instance == null) {
            NoCompanyPenalty.Logger.LogError("StartOfRound null!");
            return;
        }

        if (StartOfRound.Instance.currentLevel == null) {
            NoCompanyPenalty.Logger.LogError("Current level null!");
            return;
        }

        var sceneName = StartOfRound.Instance.currentLevel.sceneName;

        if (!sceneName.Equals("CompanyBuilding")) {
            NoCompanyPenalty.Logger.LogDebug($"CompanyBuilding != {sceneName}");
            return;
        }

        NoCompanyPenalty.Logger.LogDebug($"Previous dead players: {deadPlayers}");

        deadPlayers = 0;
    }
}