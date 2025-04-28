using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ErenshorTabTargetFix
{
    [BepInPlugin("com.yourname.erenshor.tabtargetfix", "Erenshor Tab Target Fix", "1.0.0")]
    public class TabTargetFixPlugin : BaseUnityPlugin
    {
        public static ConfigEntry<bool> EnableTabTargetFix;

        private void Awake()
        {
            EnableTabTargetFix = Config.Bind(
                "General",
                "EnableTabTargetFix",
                true,
                "Enable sorting tab targeting to nearest target first."
            );

            Harmony harmony = new Harmony("com.yourname.erenshor.tabtargetfix");
            harmony.PatchAll();
        }
    }

    [HarmonyPatch(typeof(TargetTracker), nameof(TargetTracker.TabTarget))]
    public static class TargetTracker_TabTarget_Patch
    {
        static bool Prefix(TargetTracker __instance, ref Character __result, Character _cur)
        {
            if (!TabTargetFixPlugin.EnableTabTargetFix.Value)
            {
                // If setting is off, allow original method to run
                return true;
            }

            // Custom behavior when setting is on
            __instance.SortedTargets.Clear();

            if (__instance.NearbyTargets.Count == 1 && !__instance.NearbyTargets[0].MyNPC.SimPlayer && !__instance.NearbyTargets[0].MyStats.Charmed)
            {
                __result = __instance.NearbyTargets[0];
                return false; // Skip original
            }

            if (__instance.NearbyTargets.Count > 1)
            {
                foreach (Character character in __instance.NearbyTargets)
                {
                    if (!character.MyNPC.SimPlayer && !character.MyStats.Charmed && GameData.PlayerControl.CheckLOS(character) &&
                        (character.MyFaction != Character.Faction.Mineral || (!GameData.InCombat && character.MyFaction == Character.Faction.Mineral)))
                    {
                        __instance.SortedTargets.Add(character);
                    }
                }

                if (__instance.SortedTargets.Count <= 0)
                {
                    foreach (Character character in __instance.NearbyTargets)
                    {
                        if (!character.MyNPC.SimPlayer && !character.MyStats.Charmed && GameData.PlayerControl.CheckLOS(character))
                        {
                            __instance.SortedTargets.Add(character);
                        }
                    }
                }

                // Sort by distance
                __instance.SortedTargets.Sort((a, b) =>
                {
                    float distA = Vector3.Distance(GameData.PlayerControl.transform.position, a.transform.position);
                    float distB = Vector3.Distance(GameData.PlayerControl.transform.position, b.transform.position);
                    return distA.CompareTo(distB);
                });

                __instance.curIndex = 0;
            }

            if (__instance.SortedTargets.Count > 0)
            {
                if (__instance.curIndex >= __instance.SortedTargets.Count)
                {
                    __instance.curIndex = 0;
                }
                __result = __instance.SortedTargets[__instance.curIndex++];
                return false; // Skip original
            }

            __result = null;
            return false; // Skip original
        }
    }
}
