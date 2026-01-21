using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Windows;
using static MultiThreadedLiquid.MultiThreadedLiquid;
using static MultiThreadedLiquid.SharedState;

namespace MultiThreadedLiquid
{
    public static class SharedState
    {
        public static Harmony harmony;
    }

    [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
    public class MultiThreadedLiquid : BaseUnityPlugin
    {
        public static ManualLogSource logger;
        public const string pluginGuid = "shushu.casualtiesunknown.multithreadedliquid";
        public const string pluginName = "Multi-Threaded Liquid";

        // Year.Month.Version.Bugfix
        public const string pluginVersion = "26.1.1.0";

        public static MultiThreadedLiquid Instance;

        public static int isOkayToPatch = 0;
        public static string activeVersion = "";

        public void Awake()
        {
            Instance = this;
            logger = Logger;

            logger.LogInfo("Awake() ran - mod loaded!");

            harmony = new Harmony(pluginGuid);

            StartCoroutine(CheckGameVersion(harmony));
        }

        public static void Log(string message)
        {
            logger.LogInfo(message);
        }

        public static IEnumerator CheckGameVersion(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(PreRunScript), "Awake"), prefix: new HarmonyMethod(typeof(MultiThreadedLiquid).GetMethod("VersionCheck")));

            while (true)
            {
                if (isOkayToPatch == 1)
                {
                    break;
                }
                if (isOkayToPatch == -1)
                {
                    harmony.Unpatch(AccessTools.Method(typeof(PreRunScript), "Awake"), HarmonyPatchType.Prefix);
                    logger.LogError($"Game version is not {activeVersion}, mod exiting...");
                    yield break;
                }
                yield return null;
            }

            harmony.Unpatch(AccessTools.Method(typeof(PreRunScript), "Awake"), HarmonyPatchType.Prefix);

            List<MethodInfo> patches = typeof(MyPatches).GetMethods(BindingFlags.Static | BindingFlags.Public).ToList();
            foreach (MethodInfo patch in patches)
            {
                try
                {
                    string[] splitName = patch.Name.Replace("__", "$").Split('_');
                    for (int i = 0; i < splitName.Length; i++)
                        splitName[i] = splitName[i].Replace("$", "_");
                    if (splitName.Length < 3)
                        throw new Exception($"Patch method is named incorrectly\nPlease make sure the Patch method is named in the following pattern:\n\tTargetClass_TargetMethod_PatchType[_Version]");

                    if (splitName.Length >= 4)
                        if (splitName[3] != activeVersion)
                        {
                            Log($"{patch.Name} is not supported by version {activeVersion}");
                            continue;
                        }

                    string targetType = splitName[0];
                    MethodType targetMethodType;
                    if (splitName[1].Contains("get_"))
                        targetMethodType = MethodType.Getter;
                    else if (splitName[1].Contains("set_"))
                        targetMethodType = MethodType.Setter;
                    else
                        targetMethodType = MethodType.Normal;
                    string ogTargetMethod = splitName[1];
                    string targetMethod = splitName[1].Replace("get_", "").Replace("set_", "");
                    string patchType = splitName[2];

                    MethodInfo ogScript = null;
                    switch (targetMethodType)
                    {
                        case MethodType.Enumerator:
                        case MethodType.Normal:
                            ogScript = AccessTools.Method(AccessTools.TypeByName(targetType), targetMethod);
                            break;

                        case MethodType.Getter:
                            ogScript = AccessTools.PropertyGetter(AccessTools.TypeByName(targetType), targetMethod);
                            break;

                        case MethodType.Setter:
                        case MethodType.Constructor:
                        case MethodType.StaticConstructor:
                        default:
                            throw new Exception($"Unknown patch method\nPatch method type \"{targetMethodType}\" currently has no handling");
                    }

                    MethodInfo patchScript = typeof(MyPatches).GetMethod(patch.Name);
                    List<string> validPatchTypes = new List<string>
                    {
                        "Prefix",
                        "Postfix",
                        "Transpiler"
                    };
                    if (ogScript == null || patchScript == null || !validPatchTypes.Contains(patchType))
                    {
                        throw new Exception("Patch method is named incorrectly\nPlease make sure the Patch method is named in the following pattern:\n\tTargetClass_TargetMethod_PatchType[_Version]");
                    }
                    HarmonyMethod harmonyMethod = new HarmonyMethod(patchScript)
                    {
                        methodType = targetMethodType
                    };

                    HarmonyMethod postfix = null;
                    HarmonyMethod prefix = null;
                    HarmonyMethod transpiler = null;
                    switch (patchType)
                    {
                        case "Prefix":
                            prefix = harmonyMethod;
                            break;

                        case "Postfix":
                            postfix = harmonyMethod;
                            break;

                        case "Transpiler":
                            transpiler = harmonyMethod;
                            break;
                    }
                    harmony.Patch(ogScript, prefix: prefix, postfix: postfix, transpiler: transpiler);
                    Log("Patched " + targetType + "." + targetMethod + " as a " + patchType);
                }
                catch (Exception exception)
                {
                    logger.LogError($"Failed to patch {patch.Name}");
                    logger.LogError(exception);
                }
            }

            // If you have any PreRunScript Awake/Start patches, uncomment next line
            //SceneManager.LoadScene("PreGen");
        }

        public static void VersionCheck()
        {
            Dictionary<string, string[]> supportedVersions = new Dictionary<string, string[]>
            {
                ["Text (TMP) (18)"] = new string[] { "V5 Pre-testing 5", "5_5" },
                ["Text (TMP) (17)"] = new string[] { "V5 Pre-testing 4", "5_4" }
            };
            foreach (var supportedVersion in supportedVersions)
            {
                if (isOkayToPatch == 0)
                {
                    GameObject obj = GameObject.Find(supportedVersion.Key);
                    if (obj == null)
                        continue;
                    if (obj.GetComponent<TextMeshProUGUI>().text.Contains(supportedVersion.Value[0]))
                    {
                        activeVersion = supportedVersion.Value[1];
                        isOkayToPatch = 1;
                        break;
                    }
                }
            }
            if (isOkayToPatch == 0)
                isOkayToPatch = -1;
        }
    }

    public class MyPatches
    {
        public static byte[,] fluidSnapshot;
        public static byte[,] blockSnapshot;
        private static readonly ConcurrentQueue<FluidMove> pendingLiquidMoves = new ConcurrentQueue<FluidMove>();
        private static readonly ConcurrentQueue<FluidUpd> pendingLiquidUpdates = new ConcurrentQueue<FluidUpd>();
        private static readonly ConcurrentQueue<FluidIncrMove> pendingLiquidIncrMoves = new ConcurrentQueue<FluidIncrMove>();
        private static readonly ConcurrentQueue<FluidSound> pendingLiquidSounds = new ConcurrentQueue<FluidSound>();
        private static int tileCooldown = 0;
        public static bool useCustomSim = true;

        [HarmonyPatch(typeof(Body), "Update")]
        [HarmonyPostfix]
        public static void Body_Update_Postfix()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.I))
            {
                useCustomSim = !useCustomSim;
            }
        }

        private struct FluidMove
        {
            public Vector2Int From;
            public Vector2Int To;
        }

        private struct FluidUpd
        {
            public Vector2Int Pos;
            public byte Value;
        }

        private struct FluidIncrMove
        {
            public Vector2 Dir;
            public Vector2Int Pos;
        }

        private struct FluidSound
        {
            public Vector2Int Pos;
        }

        private static bool Empty(int localX, int localY)
        {
            if (localX < 0 || localX >= fluidSnapshot.GetLength(0) || localY < 0 || localY >= fluidSnapshot.GetLength(1))
                return false;
            return fluidSnapshot[localX, localY] == 0 && blockSnapshot[localX, localY] == 0;
        }

        public static Stopwatch sw = new Stopwatch();

        [HarmonyPatch(typeof(FluidManager), "SimulationStep")]
        [HarmonyPrefix]
        public static bool FluidManager_SimulationStep_Prefix()
        {
            sw.Restart();

            if (useCustomSim)
            {
                (RangeI, RangeI) simulationRange = FluidManager.main.SimulationRangeIndex();

                int minX = simulationRange.Item1.min;
                int maxX = simulationRange.Item1.max;
                int minY = simulationRange.Item2.min;
                int maxY = simulationRange.Item2.max;

                int width = maxX - minX;
                int height = maxY - minY;

                ushort[,] worldBlocks = (ushort[,])AccessTools.Field(typeof(WorldGeneration), "worldBlocks").GetValue(WorldGeneration.world);
                fluidSnapshot = new byte[width, height];
                blockSnapshot = new byte[width, height];
                for (int x = minX; x < maxX; x++)
                    for (int y = minY; y < maxY; y++)
                    {
                        fluidSnapshot[x - minX, y - minY] = FluidManager.main.fluid[x, y];
                        blockSnapshot[x - minX, y - minY] = (byte)worldBlocks[x, y];
                    }

                sw.Stop();
                UnityEngine.Debug.Log($"[Patch] Snapshot copy took: {sw.Elapsed.TotalMilliseconds} ms");

                sw.Restart();
                ModifiedSimulationStep();
                sw.Stop();

                UnityEngine.Debug.Log($"[Patch] ModifiedSimulationStep took: {sw.Elapsed.TotalMilliseconds} ms");
            }
            else
            {
                return true;
            }
            return false;
        }

        [HarmonyPatch(typeof(FluidManager), "SimulationStep")]
        [HarmonyPostfix]
        public static void FluidManager_SimulationStep_Postfix()
        {
            if (useCustomSim)
                return;
            sw.Stop();
            UnityEngine.Debug.Log($"[Original] SimulationStep took: {sw.Elapsed.TotalMilliseconds} ms");
        }

        private static bool IsValidWorldPos(int x, int y)
        {
            var fluid = FluidManager.main.fluid;
            return x >= 0 && x < fluid.GetLength(0) && y >= 0 && y < fluid.GetLength(1);
        }

        private static void ModifiedSimulationStep()
        {
            (RangeI, RangeI) simulationRange = FluidManager.main.SimulationRangeIndex();

            int minX = simulationRange.Item1.min;
            int maxX = simulationRange.Item1.max;
            int minY = simulationRange.Item2.min;
            int maxY = simulationRange.Item2.max;

            int width = maxX - minX;
            int height = maxY - minY;

            Parallel.For(0, width, () => new System.Random(), (localX, state, localRand) =>
            {
                for (int localY = 0; localY < height; localY++)
                {
                    if (fluidSnapshot[localX, localY] != 0)
                    {
                        bool belowEmpty = (localY > 0) && Empty(localX, localY - 1);

                        var from = new Vector2Int(localX + minX, localY + minY);
                        var toDown = new Vector2Int(localX + minX, localY + minY - 1);

                        if (belowEmpty)
                        {
                            if (localY <= 2)
                            {
                                if (IsValidWorldPos(from.x, from.y))
                                    pendingLiquidUpdates.Enqueue(new FluidUpd { Pos = from, Value = 0 });
                            }
                            if (localY > 0 && IsValidWorldPos(from.x, from.y) && IsValidWorldPos(toDown.x, toDown.y))
                                pendingLiquidMoves.Enqueue(new FluidMove { From = from, To = toDown });

                            if (IsValidWorldPos(from.x, from.y))
                            {
                                pendingLiquidIncrMoves.Enqueue(new FluidIncrMove { Dir = Vector2.down, Pos = from });
                                pendingLiquidSounds.Enqueue(new FluidSound { Pos = from });
                            }
                        }
                        else
                        {
                            if (localY > 0)
                            {
                                if ((fluidSnapshot[localX, localY] == 2 && fluidSnapshot[localX, localY - 1] == 1) ||
                                    (fluidSnapshot[localX, localY] == 1 && fluidSnapshot[localX, localY - 1] == 2))
                                {
                                    var posBelow = new Vector2Int(localX + minX, localY + minY - 1);
                                    if (IsValidWorldPos(posBelow.x, posBelow.y))
                                        pendingLiquidUpdates.Enqueue(new FluidUpd { Pos = posBelow, Value = 2 });
                                    if (IsValidWorldPos(from.x, from.y))
                                        pendingLiquidUpdates.Enqueue(new FluidUpd { Pos = from, Value = 2 });
                                }

                                bool rightEmpty = Empty(localX + 1, localY);
                                bool leftEmpty = Empty(localX - 1, localY);

                                if (rightEmpty && leftEmpty)
                                {
                                    var toPos = new Vector2Int(localX + minX + (localRand.NextDouble() > 0.5 ? 1 : -1), localY + minY);
                                    if (IsValidWorldPos(from.x, from.y) && IsValidWorldPos(toPos.x, toPos.y))
                                        pendingLiquidMoves.Enqueue(new FluidMove { From = from, To = toPos });
                                }
                                else if (rightEmpty)
                                {
                                    var toPos = new Vector2Int(localX + minX + 1, localY + minY);
                                    if (IsValidWorldPos(from.x, from.y) && IsValidWorldPos(toPos.x, toPos.y))
                                    {
                                        pendingLiquidMoves.Enqueue(new FluidMove { From = from, To = toPos });
                                        pendingLiquidIncrMoves.Enqueue(new FluidIncrMove { Dir = Vector2.right, Pos = from });
                                    }
                                }
                                else if (leftEmpty)
                                {
                                    var toPos = new Vector2Int(localX + minX - 1, localY + minY);
                                    if (IsValidWorldPos(from.x, from.y) && IsValidWorldPos(toPos.x, toPos.y))
                                    {
                                        pendingLiquidMoves.Enqueue(new FluidMove { From = from, To = toPos });
                                        pendingLiquidIncrMoves.Enqueue(new FluidIncrMove { Dir = Vector2.left, Pos = from });
                                    }
                                }
                            }
                        }
                    }
                }
                return localRand;
            }, _ => { });

            while (pendingLiquidMoves.TryDequeue(out FluidMove move))
            {
                if (IsValidWorldPos(move.From.x, move.From.y) && IsValidWorldPos(move.To.x, move.To.y))
                    FluidManager.main.Swap(move.From, move.To);
            }
            while (pendingLiquidUpdates.TryDequeue(out FluidUpd upd))
            {
                if (IsValidWorldPos(upd.Pos.x, upd.Pos.y))
                    FluidManager.main.fluid[upd.Pos.x, upd.Pos.y] = upd.Value;
            }
            while (pendingLiquidIncrMoves.TryDequeue(out FluidIncrMove incr))
            {
                if (IsValidWorldPos(incr.Pos.x, incr.Pos.y))
                    FluidManager.main.IncrMove(incr.Dir, incr.Pos);
            }
            while (pendingLiquidSounds.TryDequeue(out FluidSound sound))
            {
                tileCooldown++;
                if (tileCooldown > 16)
                {
                    tileCooldown = 0;
                    if (Time.timeScale <= 1f)
                        Sound.Play("waterflow" + UnityEngine.Random.Range(1, 4).ToString(), WorldGeneration.world.BlockToWorldPos(sound.Pos), false, true, null, 1f, 1f, false, false);
                }
            }
        }
    }
}