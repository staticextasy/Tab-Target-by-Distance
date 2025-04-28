using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ErenshorTabTargetFix
{
    [BepInPlugin("com.staticextasy.tabtargetfix", "Tab Target Fix", "1.0.1")]
    public class TabTargetFixPlugin : BaseUnityPlugin
    {
        public static ConfigEntry<bool> EnableTabTargetFix;
        private static Harmony harmony;
        private static bool isPatched = false;
        private static BepInEx.Logging.ManualLogSource log;
        private static Character lastSelectedTarget = null;
        public static ConfigEntry<float> MaxTabTargetDistance;


        private void Awake()
        {
            log = Logger;

            EnableTabTargetFix = Config.Bind(
                "General",
                "EnableTabTargetFix",
                true,
                "Enable sorting tab targeting to nearest target first."
            );

            MaxTabTargetDistance = Config.Bind(
            "General",
            "MaxTabTargetDistance",
            20f,
            new ConfigDescription("Maximum distance (in meters) to tab-target enemies. Targets further than this will not be selected.", new AcceptableValueRange<float>(5f, 100f))
            );


            EnableTabTargetFix.SettingChanged += OnSettingChanged;

            harmony = new Harmony("com.staticextasy.tabtargetfix");

            if (EnableTabTargetFix.Value)
            {
                ApplyPatches();
            }
        }

        private void OnSettingChanged(object sender, EventArgs e)
        {
            if (EnableTabTargetFix.Value && !isPatched)
            {
                ApplyPatches();
            }
            else if (!EnableTabTargetFix.Value && isPatched)
            {
                RemovePatches();
            }
        }

        private void ApplyPatches()
        {
            harmony.PatchAll();
            isPatched = true;
            log.LogInfo("[TabTargetFix] Patch applied.");
        }

        private void RemovePatches()
        {
            harmony.UnpatchSelf();
            isPatched = false;
            log.LogInfo("[TabTargetFix] Patch removed.");
        }
    }

    [HarmonyPatch(typeof(TargetTracker), nameof(TargetTracker.TabTarget))]
    public static class TargetTracker_TabTarget_Patch
    {
        static readonly AccessTools.FieldRef<TargetTracker, List<Character>> SortedTargetsRef = AccessTools.FieldRefAccess<TargetTracker, List<Character>>("SortedTargets");
        static readonly AccessTools.FieldRef<TargetTracker, int> CurIndexRef = AccessTools.FieldRefAccess<TargetTracker, int>("curIndex");

        private static Character lastSelectedTarget = null; // <<< NEW

        static bool Prefix(TargetTracker __instance, ref Character __result, Character _cur)
        {
            if (!TabTargetFixPlugin.EnableTabTargetFix.Value)
                return true;

            var nearbyTargets = __instance.NearbyTargets;
            var sortedTargets = SortedTargetsRef(__instance);
            var curIndex = CurIndexRef(__instance);

            if (nearbyTargets.Count == 0)
            {
                __result = null;
                lastSelectedTarget = null;
                return false;
            }

            // Always rebuild sortedTargets fresh every tab press
            sortedTargets.Clear();

            float maxTabTargetDistance = TabTargetFixPlugin.MaxTabTargetDistance.Value; // <- LIMIT TAB to 20m (you can adjust this later!)

            foreach (Character character in nearbyTargets)
            {
                if (!character.MyNPC.SimPlayer &&
                    !character.MyStats.Charmed &&
                    GameData.PlayerControl.CheckLOS(character))
                {
                    float distance = Vector3.Distance(GameData.PlayerControl.transform.position, character.transform.position);
                    if (distance <= maxTabTargetDistance) // <<<< ONLY ADD CLOSE ONES
                    {
                        sortedTargets.Add(character);
                    }
                }
            }

            if (sortedTargets.Count <= 0)
            {
                __result = null;
                lastSelectedTarget = null;
                return false;
            }

            // Sort by distance
            sortedTargets.Sort((a, b) =>
            {
                float distA = Vector3.Distance(GameData.PlayerControl.transform.position, a.transform.position);
                float distB = Vector3.Distance(GameData.PlayerControl.transform.position, b.transform.position);
                return distA.CompareTo(distB);
            });

            // Find where the last selected target is
            int startIndex = 0;
            if (lastSelectedTarget != null)
            {
                startIndex = sortedTargets.IndexOf(lastSelectedTarget);
                if (startIndex == -1)
                    startIndex = 0;
            }

            bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

            if (ctrlHeld)
            {
                startIndex--;
                if (startIndex < 0)
                {
                    startIndex = sortedTargets.Count - 1;
                }
            }
            else
            {
                startIndex++;
                if (startIndex >= sortedTargets.Count)
                {
                    startIndex = 0;
                }
            }

            __result = sortedTargets[startIndex];
            lastSelectedTarget = __result;
            CurIndexRef(__instance) = startIndex;

            return false;
        }

    }

    [HarmonyPatch(typeof(TargetTracker), "Update")]
    public static class TargetTracker_Update_Patch
    {
        private static HashSet<int> adjustedObjects = new HashSet<int>();

        static void Postfix(TargetTracker __instance)
        {
            if (__instance == null)
                return;

            int instanceId = __instance.GetInstanceID();

            if (adjustedObjects.Contains(instanceId))
                return; // Already adjusted this one

            SphereCollider collider = __instance.GetComponent<SphereCollider>();
            if (collider != null)
            {
                if (collider.radius < 80f)
                {
                    collider.radius = 80f;
                    collider.isTrigger = true;
                    BepInEx.Logging.Logger.CreateLogSource("TabTargetFix").LogInfo($"[TabTargetFix] Increased scan radius to {collider.radius} for {__instance.name}");
                }

                adjustedObjects.Add(instanceId);
            }
        }
    }
}
