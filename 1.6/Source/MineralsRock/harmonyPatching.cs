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
            ThingDef rockDef = DefDatabase<ThingDef>.GetNamedSilentFail($"Small{rockName}");
            
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

    // Auto-generate StaticMineral defs for unreplaced natural rocks
    [StaticConstructorOnStartup]
    public static class AutoGenerateRockReplacements
    {

        static AutoGenerateRockReplacements()
        {
            Log.Message("Starting automatic rock replacement generation");

            int generatedCount = 0;
            // Cache all defs first to prevent modification during enumeration
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs.ToList())
            {
                if (def.building == null || !def.building.isNaturalRock || def.thingClass == typeof(StaticMineral))
                    continue;

                // Check if already replaced
                bool alreadyReplaced = DefDatabase<ThingDef>.AllDefs
                    .OfType<ThingDef_StaticMineral>()
                    .Any(sm => sm.ThingsToReplace != null && sm.ThingsToReplace.Contains(def.defName));

                if (alreadyReplaced)
                    continue;

                // Create new solid def
                ThingDef_StaticMineral solidDef = ThingDef_StaticMineral.MakeSolidGenericRockBaseDef();
                solidDef.defName = "MR_Solid" + def.defName;
                solidDef.label = "Solid " + def.label;
                solidDef.description = def.description;
                solidDef.graphicData.color = def.graphicData.color;
                solidDef.building.mineableThing = def.building.mineableThing;
                solidDef.building.mineableDropChance = def.building.mineableDropChance;
                solidDef.ThingsToReplace = new List<string> { def.defName };
                DefDatabase<ThingDef_StaticMineral>.Add(solidDef);

                // Create new weathered def
                ThingDef_StaticMineral weathedDef = ThingDef_StaticMineral.MakeWeatheredGenericRockBaseDef();
                weathedDef.defName = "MR_Weathered" + def.defName;
                weathedDef.label = "Weathered " + def.label;
                weathedDef.description = def.description;
                weathedDef.graphicData.color = def.graphicData.color;
                weathedDef.building.mineableThing = def.building.mineableThing;
                weathedDef.building.mineableDropChance = def.building.mineableDropChance * 0.8f;
                weathedDef.ThingsToReplace = new List<string> { def.defName };
                weathedDef.allowedTerrains = new List<string> { def.defName + "_Rough" };
                weathedDef.associatedOres = new List<string> { solidDef.defName };
                DefDatabase<ThingDef_StaticMineral>.Add(weathedDef);

                // Create new hewn def
                ThingDef_StaticMineral hewnDef = ThingDef_StaticMineral.MakeHewnGenericRockBaseDef();
                hewnDef.defName = "MR_Hewn" + def.defName;
                hewnDef.label = "Hewn " + def.label;
                hewnDef.description = def.description;
                hewnDef.graphicData.color = def.graphicData.color;
                hewnDef.building.mineableThing = def.building.mineableThing;
                hewnDef.building.mineableDropChance = def.building.mineableDropChance * 1.2f;
                hewnDef.ThingsToReplace = new List<string> { def.defName };
                DefDatabase<ThingDef_StaticMineral>.Add(hewnDef);

                // Create new smoothed def
                ThingDef_StaticMineral smoothedDef = ThingDef_StaticMineral.MakeSmoothedGenericRockBaseDef();
                smoothedDef.defName = "MR_Smoothed" + def.defName;
                smoothedDef.label = "Smoothed " + def.label;
                smoothedDef.description = def.description;
                smoothedDef.graphicData.color = def.graphicData.color;
                smoothedDef.building.mineableThing = def.building.mineableThing;
                smoothedDef.building.mineableDropChance = def.building.mineableDropChance * 1.2f;
                DefDatabase<ThingDef_StaticMineral>.Add(smoothedDef);

                // Create new boulder def
                ThingDef_StaticMineral boulderDef = ThingDef_StaticMineral.MakeBoulderGenericRockBaseDef();
                boulderDef.defName = "MR_Boulder" + def.defName;
                boulderDef.label = def.label + " Boulder";
                boulderDef.description = def.description;
                boulderDef.graphicData.color = def.graphicData.color;
                boulderDef.building.mineableThing = def.building.mineableThing;
                boulderDef.building.mineableDropChance = def.building.mineableDropChance * 1.5f;
                boulderDef.neededNearbyTerrains = new List<string> { weathedDef.defName, solidDef.defName, def.defName + "_Rough", def.defName };
                boulderDef.associatedOres = new List<string> { def.defName, weathedDef.defName };
                DefDatabase<ThingDef_StaticMineral>.Add(boulderDef);

                // Create new small rock def
                ThingDef_StaticMineral smallDef = ThingDef_StaticMineral.MakeSmallGenericRockBaseDef();
                smallDef.defName = "MR_Small" + def.defName;
                smallDef.label = def.label + " Rocks";
                smallDef.description = def.description;
                smallDef.graphicData.color = def.graphicData.color;
                smallDef.building.mineableThing = def.building.mineableThing;
                smallDef.building.mineableDropChance = def.building.mineableDropChance * 0.3f;
                smallDef.neededNearbyTerrains = new List<string> { boulderDef.defName, weathedDef.defName, solidDef.defName, def.defName + "_Rough", def.defName };
                smallDef.associatedOres = new List<string> { boulderDef.defName, def.defName, weathedDef.defName };
                DefDatabase<ThingDef_StaticMineral>.Add(smallDef);

                generatedCount++;
                Log.Message($"MineralsFramework: Auto-generated replacement rocks for {def.defName}");
            }
            
            Log.Message($"Generated {generatedCount} automatic rock replacement defs");
        }
    }
}
