using HarmonyLib;
using MineralsFramework;
using MineralsRock;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace MineralsRock
{
    [HarmonyPatch(typeof(ThingSetMaker_Meteorite), "Reset")]
    public static class ThingSetMaker_Meteorite_Reset_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ThingSetMaker_Meteorite __instance)
        {
            // Modify the static nonSmoothedMineables list after reset
            var filteredMineables = (DefDatabase<ThingDef>.AllDefsListForReading.Where((ThingDef x) => x.mineable && !x.building.mineablePreventMeteorite && !x.IsSmoothed &&
typeof(MineralsFramework.ThingDef_StaticMineral).IsAssignableFrom(x.GetType()))).ToList();

            ThingSetMaker_Meteorite.nonSmoothedMineables = filteredMineables;

            if (MineralsFrameworkMain.Settings.debugModeEnabled)
            {
                Log.Message($"Minerals: Replaced meteorite mineables with {filteredMineables.Count} MineralsFramework defs");
            }
        }
    }


    [HarmonyPatch(typeof(ThingSetMaker_Meteorite), "FindRandomMineableDef")]
    public static class ThingSetMaker_Meteorite_FindRandomMineableDef_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref ThingDef __result)
        {
            // Completely replace original logic with simple random selection
            if (ThingSetMaker_Meteorite.nonSmoothedMineables.Count == 0)
                return true;
            
            __result = ThingSetMaker_Meteorite.nonSmoothedMineables.RandomElement();
            
            return false; // Skip original method
        }
    }

    [HarmonyPatch(typeof(Skyfaller), "Impact")]
    public static class Skyfaller_Impact_Patch
    {
        [HarmonyPrefix]
        public static void Postfix(Skyfaller __instance)
        {
            if (MineralsFrameworkMain.Settings.debugModeEnabled)
            {
                Log.Message($"MineralsRock: attempting to make skyfaller rock ring.");
            }
            
            if (__instance.innerContainer.Count < 3)
            {
                if (MineralsFrameworkMain.Settings.debugModeEnabled)
                {
                    Log.Message($"MineralsRock: to small of a skyfaller to make ring.");
                }
                return;
            }

            // Get nearest rocky terrain
            IntVec3 center = __instance.Position;
            TerrainDef nearestRockTerrain = null;
            int maxDist = 25;
            
            for (int x = center.x - maxDist; x <= center.x + maxDist; x++)
            {
                for (int z = center.z - maxDist; z <= center.z + maxDist; z++)
                {
                    IntVec3 pos = new IntVec3(x, 0, z);
                    if (pos.InBounds(__instance.Map))
                    {
                        TerrainDef terrain = __instance.Map.terrainGrid.TerrainAt(pos);
                        if (terrain != null && terrain.IsRock)
                        {
                            nearestRockTerrain = terrain;
                            break;
                        }
                    }
                }
                if (nearestRockTerrain != null) break;
            }

            if (nearestRockTerrain == null)
                return;

            // Try to find matching passable rock def (e.g. "Granite_Rough" -> "PassableGranite")
            string rockName = nearestRockTerrain.defName.Split('_')[0];
            ThingDef rockDef = DefDatabase<ThingDef>.GetNamedSilentFail($"Passable{rockName}");
            
            if (rockDef == null)
            {
                if (MineralsFrameworkMain.Settings.debugModeEnabled)
                {
                    Log.Message($"MineralsRock: cant find nearby terrain to make ring.");
                }
                return;
            }

            // Calculate ring size based on item count
            int itemCount = __instance.innerContainer.Count;
            int radius = (int)(GenMath.Sqrt(itemCount) * 2f);
            if (radius < 2)
            {
                radius = 2;
            }

            // Generate hollow ring pattern
            Log.Message($"MineralsRock: center: {center}.");
            foreach (IntVec3 offset in GenRadial.RadialPatternInRadius(radius))
            {
                Log.Message($"MineralsRock: offset: {offset}.");
                // Only place rocks in outer ring
                float distance = (float)Math.Sqrt(offset.x * offset.x + offset.y * offset.y + offset.z * offset.z);
                float rockSize = (0.33f - (radius - distance) / radius) * 3 + ((float)Rand.Range(-7, 2) / 10f);
                Log.Message($"MineralsRock: distance: {distance}.");
                if (rockSize < 0.05f)
                    continue;
                if (rockSize > 1f)
                    rockSize = 1;

                IntVec3 targetPos = center + offset;
                if (targetPos.InBounds(__instance.Map) && 
                    targetPos.GetEdifice(__instance.Map) == null && 
                    GenSight.LineOfSight(center, targetPos, __instance.Map))
                {
                    MineralsFramework.StaticMineral spawned = (MineralsFramework.StaticMineral) GenSpawn.Spawn(rockDef, targetPos, __instance.Map);
                    spawned.size = rockSize;
                }
            }

            if (MineralsFrameworkMain.Settings.debugModeEnabled)
            {
                Log.Message($"Generated {rockDef.defName} ring with radius {radius} around {center}");
            }
        }
    }
}
