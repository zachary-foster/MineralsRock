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

    // Auto-generate StaticMineral defs for unreplaced natural rocks
    [StaticConstructorOnStartup]
    public static class AutoGenerateRockReplacements
    {
        public static ThingDef_StaticMineral CreateGenericRockBase()
        {
            MineralsFramework.ThingDef_StaticMineral def = new MineralsFramework.ThingDef_StaticMineral();

            def.thingClass = typeof(MineralsFramework.StaticMineral);
            def.category = ThingCategory.Building;
            def.selectable = true;
            def.neverMultiSelect = true;
            def.altitudeLayer = AltitudeLayer.Building;
            def.passability = Traversability.Standable;
            def.castEdgeShadows = false;
            def.fillPercent = 0.05f;
            def.coversFloor = false;
            def.blockWind = false;
            def.pathCost = 60;
            def.mineable = true;
            def.leaveResourcesWhenKilled = true;
            def.filthLeaving = DefDatabase<ThingDef>.GetNamed("Filth_RubbleRock");
            def.drawerType = DrawerType.MapMeshOnly;
            def.scatterableOnMapGen = false;
            def.hideAtSnowOrSandDepth = 0.5f;
            def.spawnRadius = 2;
            def.perMapProbability = 0.5f;
            def.minClusterProbability = 0.001f;
            def.maxClusterProbability = 0.01f;
            def.minClusterSize = 1;
            def.maxClusterSize = 10;
            def.initialSizeMin = 0.3f;
            def.initialSizeMax = 0.9f;
            def.initialSizeVariation = 0.3f;
            def.allowedBiomes = null;
            def.allowedTerrains = null;
            def.disallowedTerrains = new List<string> { "LavaDeep", "LavaShallow", "WaterDeep", "WaterOceanDeep", "Space", "CooledLava" };
            def.neededNearbyTerrains = null;
            def.neededNearbyTerrainRadius = 3f;
            def.neededNearbyTerrainSizeEffect = true;
            def.mustBeUnderRoof = true;
            def.mustBeUnderThickRoof = false;
            def.mustBeUnroofed = false;
            def.mustBeNotUnderThickRoof = false;
            def.mustBeNearPassable = false;
            def.maxMeshCount = 4;
            def.visualSizeRange = new FloatRange(0.3f, 1.0f);
            def.visualClustering = 0.5f;
            def.visualSpread = 1.5f;
            def.visualSizeVariation = 0.2f;
            def.canSpawnOnThings = false;
            def.coloredByTerrain = false;
            def.newMapGenStep = "rocks";
            def.newMapSpawnOrder = 100;
            def.tags = new List<string>();
            def.otherSettlementMiningRadius = 0;
            def.sizeScaledByAbundance = false;
            def.mineSpeedFactor = 1f;
            def.snowTextureThreshold = 0.8f;
            def.hiddenInSnowThreshold = 999f;
            def.building = new BuildingProperties();
            def.building.isInert = true;
            def.building.canBuildNonEdificesUnder = false;
            def.building.isNaturalRock = false;
            def.building.isResourceRock = true;
            def.building.mineableDropChance = 0f;
            def.building.mineableYield = 0;
            def.building.mineableNonMinedEfficiency = 0;
            def.building.claimable = false;
            def.building.alwaysDeconstructible = false;
            def.building.isEdifice = true;
            def.building.destroyShakeAmount = 0;
            def.building.mineablePreventMeteorite = true;

            return def;
        }

        public static ThingDef_StaticMineral CreateGenericImpassableRockBase()
        {
            ThingDef_StaticMineral def = CreateGenericRockBase();

            def.drawerType = DrawerType.MapMeshOnly;
            def.terrainAffordanceNeeded = TerrainAffordanceDefOf.Heavy;
            def.filthLeaving = DefDatabase<ThingDef>.GetNamed("Filth_RubbleRock");
            def.altitudeLayer = AltitudeLayer.Building;
            def.passability = Traversability.Impassable;
            def.castEdgeShadows = true;
            def.fillPercent = 1f;
            def.coversFloor = true;
            def.rotatable = true;
            def.saveCompressible = true;
            def.holdsRoof = true;
            def.staticSunShadowHeight = 1.0f;
            def.blockLight = true;
            def.blockWind = true;
            def.mineable = true;
            def.hideAtSnowOrSandDepth = 2f;
            def.snowTextureThreshold = 0.8f;
            def.hiddenInSnowThreshold = 999f;
            if (def.statBases == null) def.statBases = new List<StatModifier>();
            def.statBases.Add(new StatModifier { stat = StatDefOf.Flammability, value = 0f });
            def.building.isInert = true;
            def.building.canBuildNonEdificesUnder = false;
            def.building.mineableDropChance = 0.01f;
            def.building.mineableYield = 1;
            def.building.mineableNonMinedEfficiency = 0;
            def.building.claimable = false;
            def.building.alwaysDeconstructible = false;
            def.building.isEdifice = true;
            def.tags = new List<string> { "rock", "wall" };

            return def;
        }

        public static ThingDef_StaticMineral CreateGenericWeatheredRock()
        {
            ThingDef_StaticMineral def = CreateGenericImpassableRockBase();

            def.graphicData = new GraphicData();
            def.graphicData.shaderType = ShaderTypeDefOf.CutoutComplex;
            def.graphicData.graphicClass = typeof(Graphic_Random);
            def.graphicData.drawSize = new Vector2(4f, 4f);
            def.mustBeUnderRoof = false;
            def.mustBeUnderThickRoof = false;
            def.mustBeUnroofed = true;
            def.mustBeNotUnderThickRoof = false;
            def.maxMeshCount = 1;
            def.visualSizeRange = new FloatRange(1.75f, 1.9f);
            def.visualClustering = 1.0f;
            def.visualSpread = 0.5f;
            def.visualSizeVariation = 0.08f;
            def.verticalOffset = 0.2f;
            def.newMapSpawnOrder = 60;
            def.snowTextureThreshold = 0.8f;
            def.graphicData.texPath = "Things/Rock/WeatheredGranite";
            def.graphicData.color = new Color(105f / 255f, 95f / 255f, 97f / 255f);
            def.uiIconPath = "Things/Rock/WeatheredGranite/WeatheredGraniteA";
            def.statBases = new List<StatModifier>
            {
                new StatModifier { stat = StatDefOf.MaxHitPoints, value = 1000f },
                new StatModifier { stat = StatDefOf.Flammability, value = 0f },
                new StatModifier { stat = StatDefOf.Beauty, value = 1f }
            };
            def.building.mineableDropChance = 0.3f;
            def.spawnRadius = 1;
            def.perMapProbability = 1f;
            def.minClusterProbability = 0.01f;
            def.maxClusterProbability = 0.02f;
            def.minClusterSize = 1;
            def.maxClusterSize = 5;
            def.initialSizeMin = 1f;
            def.initialSizeMax = 1f;
            def.initialSizeVariation = 0.3f;
            def.neededNearbyTerrainRadius = 2f;
            def.neededNearbyTerrainSizeEffect = false;
            def.mineSpeedFactor = 1f;
            def.randomlyDropResources = new List<MineralsFramework.RandomResourceDrop>
            {
                new MineralsFramework.RandomResourceDrop
                {
                    ResourceDefName = "RoughGem",
                    DropProbability = 0.02f,
                    MinMiningSkill = 4
                }
            };
            def.tags = new List<string> { "rock", "wall", "weathered" };

            return def;
        }

        public static ThingDef_SolidRock CreateGenericSolidRock()
        {
            ThingDef_SolidRock def = (ThingDef_SolidRock)CreateGenericImpassableRockBase();
            def.thingClass = typeof(SolidRock);
            def.graphicData = new GraphicData();
            def.graphicData.shaderType = ShaderTypeDefOf.CutoutComplex;
            def.graphicData.graphicClass = typeof(Graphic_Random);
            def.graphicData.drawSize = new Vector2(4f, 4f);
            def.mustBeUnderRoof = false;
            def.mustBeUnderThickRoof = false;
            def.mustBeUnroofed = false;
            def.mustBeNotUnderThickRoof = true;
            def.maxMeshCount = 1;
            def.visualSizeRange = new FloatRange(1.75f, 1.9f);
            def.visualClustering = 1.0f;
            def.visualSpread = 0.5f;
            def.visualSizeVariation = 0.08f;
            def.verticalOffset = 0.2f;
            def.newMapSpawnOrder = 50;
            def.snowTextureThreshold = 1f;
            def.graphicData.texPath = "Things/Rock/SolidGranite";
            def.graphicData.color = new Color(105f / 255f, 95f / 255f, 97f / 255f);
            def.uiIconPath = "Things/Rock/SolidGranite/SolidGraniteA";
            def.statBases = new List<StatModifier>
            {
                new StatModifier { stat = StatDefOf.MaxHitPoints, value = 1100f },
                new StatModifier { stat = StatDefOf.Flammability, value = 0f },
                new StatModifier { stat = StatDefOf.Beauty, value = 0f }
            };
            def.building.mineableDropChance = 0.35f;
            def.spawnRadius = 1;
            def.perMapProbability = 0f;
            def.minClusterProbability = 0f;
            def.maxClusterProbability = 0f;
            def.minClusterSize = 1;
            def.maxClusterSize = 5;
            def.initialSizeMin = 1f;
            def.initialSizeMax = 1f;
            def.initialSizeVariation = 0.3f;
            def.neededNearbyTerrainRadius = 2f;
            def.neededNearbyTerrainSizeEffect = false;
            def.mineSpeedFactor = 0.9f;
            def.randomlyDropResources = new List<MineralsFramework.RandomResourceDrop>
            {
                new MineralsFramework.RandomResourceDrop
                {
                    ResourceDefName = "RoughGem",
                    DropProbability = 0.02f,
                    MinMiningSkill = 4
                }
            };
            def.tags = new List<string> { "rock", "wall", "solid" };

            return def;
        }

        public static ThingDef_StaticMineral CreateGenericHewnRock()
        {
            ThingDef_StaticMineral def = CreateGenericImpassableRockBase();

            GraphicData graphicData = new GraphicData();
            graphicData.shaderType = ShaderTypeDefOf.CutoutComplex;
            graphicData.graphicClass = typeof(Graphic_Random);
            graphicData.linkType = LinkDrawerType.CornerFiller;

            graphicData.linkFlags = LinkFlags.Wall | LinkFlags.Rock | LinkFlags.MapEdge;

            graphicData.damageData = new DamageGraphicData();
            graphicData.damageData.cornerTL = "Damage/Corner";
            graphicData.damageData.cornerTR = "Damage/Corner";
            graphicData.damageData.cornerBL = "Damage/Corner";
            graphicData.damageData.cornerBR = "Damage/Corner";
            graphicData.damageData.edgeTop = "Damage/Edge";
            graphicData.damageData.edgeBot = "Damage/Edge";
            graphicData.damageData.edgeLeft = "Damage/Edge";
            graphicData.damageData.edgeRight = "Damage/Edge";
            def.mustBeUnderRoof = false;
            def.mustBeUnderThickRoof = true;
            def.mustBeUnroofed = false;
            def.mustBeNotUnderThickRoof = false;
            def.maxMeshCount = 1;
            def.visualSizeRange = new FloatRange(1f, 1f);
            def.visualClustering = 1.0f;
            def.visualSpread = 0.5f;
            def.visualSizeVariation = 0.08f;
            def.verticalOffset = 0.2f;
            def.newMapSpawnOrder = 30;
            def.snowTextureThreshold = 1f;
            def.graphicData.texPath = "Things/Rock/HewnGranite";
            def.graphicData.color = new Color(105f / 255f, 95f / 255f, 97f / 255f);
            def.uiIconPath = "Things/Rock/HewnRockWall/HewnRockWallA";
            def.statBases = new List<StatModifier>
            {
                new StatModifier { stat = StatDefOf.MaxHitPoints, value = 1200f },
                new StatModifier { stat = StatDefOf.Flammability, value = 0f },
                new StatModifier { stat = StatDefOf.Beauty, value = 0f }
            };
            def.building.mineableDropChance = 0.4f;
            def.spawnRadius = 1;
            def.perMapProbability = 0f;
            def.minClusterProbability = 0f;
            def.maxClusterProbability = 0f;
            def.minClusterSize = 1;
            def.maxClusterSize = 5;
            def.initialSizeMin = 1f;
            def.initialSizeMax = 1f;
            def.initialSizeVariation = 0.3f;
            def.neededNearbyTerrainRadius = 2f;
            def.neededNearbyTerrainSizeEffect = false;
            def.mineSpeedFactor = 0.8f;
            def.randomlyDropResources = new List<MineralsFramework.RandomResourceDrop>
            {
                new MineralsFramework.RandomResourceDrop
                {
                    ResourceDefName = "RoughGem",
                    DropProbability = 0.03f,
                    MinMiningSkill = 4
                }
            };
            def.tags = new List<string> { "rock", "wall", "hewn" };

            return def;
        }


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

                // Create new weathered def
                ThingDef_StaticMineral weathedDef = CreateGenericWeatheredRock();
                weathedDef.defName = "MF_Weathered" + def.defName;
                weathedDef.label = "Weathered " + def.label;
                weathedDef.description = def.description;
                weathedDef.graphicData.color = def.graphicData.color;
                weathedDef.randomColorsOne = null;
                weathedDef.randomColorsTwo = null;
                weathedDef.building.mineableThing = def.building.mineableThing;
                weathedDef.building.mineableDropChance = def.building.mineableDropChance;
                weathedDef.randomlyDropResources = new List<RandomResourceDrop>();
                weathedDef.ThingsToReplace = new List<string> { def.defName };
                weathedDef.allowedTerrains = new List<string> { def.defName + "_Rough" };
                weathedDef.neededNearbyTerrains = new List<string> { def.defName + "_Rough" };
                weathedDef.mineSpeedFactor = 1;
                DefDatabase<ThingDef_StaticMineral>.Add(weathedDef);

                // Create new hewn def
                ThingDef_StaticMineral hewnDef = CreateGenericHewnRock();
                hewnDef.defName = "MF_Hewn" + def.defName;
                hewnDef.label = "Hewn " + def.label;
                hewnDef.description = def.description;
                hewnDef.graphicData.color = def.graphicData.color;
                hewnDef.randomColorsOne = null;
                hewnDef.randomColorsTwo = null;
                hewnDef.building.mineableThing = def.building.mineableThing;
                hewnDef.building.mineableDropChance = def.building.mineableDropChance;
                hewnDef.randomlyDropResources = new List<RandomResourceDrop>();
                hewnDef.ThingsToReplace = new List<string> { def.defName };
                hewnDef.allowedTerrains = new List<string> { def.defName + "_Rough" };
                hewnDef.neededNearbyTerrains = new List<string> { def.defName + "_Rough" };
                hewnDef.mineSpeedFactor = 1;
                DefDatabase<ThingDef_StaticMineral>.Add(hewnDef);

                // Create new solid def
                ThingDef_SolidRock solidDef = CreateGenericSolidRock();
                solidDef.defName = "MF_Solid" + def.defName;
                solidDef.label = "Solid " + def.label;
                solidDef.description = def.description;
                solidDef.graphicData.color = def.graphicData.color;
                solidDef.randomColorsOne = null;
                solidDef.randomColorsTwo = null;
                solidDef.building.mineableThing = def.building.mineableThing;
                solidDef.building.mineableDropChance = def.building.mineableDropChance;
                solidDef.randomlyDropResources = new List<RandomResourceDrop>();
                solidDef.ThingsToReplace = new List<string> { def.defName, hewnDef.defName };
                solidDef.allowedTerrains = new List<string> { def.defName + "_Rough" };
                solidDef.neededNearbyTerrains = new List<string> { def.defName + "_Rough" };
                solidDef.mineSpeedFactor = 1;
                DefDatabase<ThingDef_SolidRock>.Add(solidDef);


                generatedCount++;
                Log.Message($"MineralsFramework: Auto-generated replacement rocks for {def.defName}");
            }
            
            Log.Message($"Generated {generatedCount} automatic rock replacement defs");
        }
    }
}
