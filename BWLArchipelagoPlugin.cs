using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BWLArchipelago
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class BWLArchipelagoPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        public BWLArchipelagoPlugin()
        {
            Log = BepInEx.Logging.Logger.CreateLogSource("BWL Archipelago");

            Log.LogInfo("BWL Archipelago mod loading...");

            ArchipelagoManager.Initialize(Log);

            Type playerType = AccessTools.TypeByName("Player");
            Type technologyType = AccessTools.TypeByName("TechnologyDefinition");
            Type entityType = AccessTools.TypeByName("Entity");

            if (playerType == null || technologyType == null || entityType == null)
            {
                Log.LogError("Could not find required game types. Aborting.");
                return;
            }

            Harmony harmony = new Harmony(PluginInfo.PLUGIN_GUID);

            // --- Patch 1: StartResearchAction.Execute ---
            // Sends the AP check when the player queues a research.
            // Lets Execute run so the library works normally.
            // Blocks if the player already queued a check for this tech.

            MethodInfo executeTarget = AccessTools.Method(
                typeof(StartResearchAction), "Execute"
            );

            if (executeTarget == null)
            {
                Log.LogError("Could not find StartResearchAction.Execute. Aborting.");
                return;
            }

            harmony.Patch(
                executeTarget,
                prefix: new HarmonyMethod(
                    typeof(StartResearchExecutePatch),
                    nameof(StartResearchExecutePatch.Prefix)
                ),
                postfix: new HarmonyMethod(
                    typeof(StartResearchExecutePatch),
                    nameof(StartResearchExecutePatch.Postfix)
                )
            );

            Log.LogInfo("Patch applied: StartResearchAction.Execute.");

            // --- Patch 2: AddResearchedTechnology ---
            // Prefix: sets IsCompletingPlayerCheck flag for non-AP calls so
            // EvaluateAvailableBuildings is skipped during player checks.
            // Postfix: removes tech from researchedTechnologies so no checkmark
            // appears from player checks. The original runs fully so library
            // queue transitions work correctly.
            // AP grants pass through unchanged via IsGrantingTechnology flag.

            MethodInfo addResearchedTarget = AccessTools.Method(
                playerType,
                "AddResearchedTechnology",
                new Type[] { technologyType, entityType, typeof(bool) }
            );

            if (addResearchedTarget == null)
            {
                Log.LogError("Could not find AddResearchedTechnology. Aborting.");
                return;
            }

            harmony.Patch(
                addResearchedTarget,
                prefix: new HarmonyMethod(
                    typeof(AddResearchedTechnologyPatch),
                    nameof(AddResearchedTechnologyPatch.Prefix)
                ),
                postfix: new HarmonyMethod(
                    typeof(AddResearchedTechnologyPatch),
                    nameof(AddResearchedTechnologyPatch.Postfix)
                )
            );

            Log.LogInfo("Patch applied: AddResearchedTechnology.");

            // --- Patch 3: PlanetIsland.EvaluateAvailableBuildings ---
            // Skips evaluation entirely during player checks (buildings only
            // unlock from AP grants).
            // For all other calls: temporarily adds all granted techs to
            // researchedTechnologies before evaluation so buildings stay unlocked,
            // then removes them after.

            try
            {
                Type planetIslandType = AccessTools.TypeByName("PlanetIsland");
                MethodInfo evaluateTarget = planetIslandType != null
                    ? AccessTools.Method(
                        planetIslandType,
                        "EvaluateAvailableBuildings",
                        new Type[] { typeof(bool), typeof(bool) }
                    )
                    : null;

                if (evaluateTarget == null)
                {
                    Log.LogError(
                        "Could not find PlanetIsland.EvaluateAvailableBuildings."
                    );
                }
                else
                {
                    harmony.Patch(
                        evaluateTarget,
                        prefix: new HarmonyMethod(
                            typeof(EvaluateAvailableBuildingsPatch),
                            nameof(EvaluateAvailableBuildingsPatch.Prefix)
                        ),
                        postfix: new HarmonyMethod(
                            typeof(EvaluateAvailableBuildingsPatch),
                            nameof(EvaluateAvailableBuildingsPatch.Postfix)
                        )
                    );
                    Log.LogInfo(
                        "Patch applied: PlanetIsland.EvaluateAvailableBuildings."
                    );
                }
            }
            catch (Exception ex)
            {
                Log.LogError(
                    "Exception patching EvaluateAvailableBuildings: " + ex.Message
                );
            }

            Type cardControllerType = AccessTools.TypeByName("GameCardController");

            MethodInfo evalBuildingButtonsTarget = cardControllerType != null
                ? AccessTools.Method(cardControllerType, "EvaluateBuildingButtons")
                : null;

            if (evalBuildingButtonsTarget != null)
            {
                harmony.Patch(
                    evalBuildingButtonsTarget,
                    prefix: new HarmonyMethod(
                        typeof(EvaluateBuildingButtonsPatch),
                        nameof(EvaluateBuildingButtonsPatch.Prefix)
                    )
                );
                Log.LogInfo("Patch applied: GameCardController.EvaluateBuildingButtons.");
            }

            MethodInfo evalBuildingCardsTarget = cardControllerType != null
                ? AccessTools.Method(
                    cardControllerType,
                    "EvaluateAvailableBuildingCards",
                    new Type[] { typeof(bool) }
                )
                : null;

            if (evalBuildingCardsTarget != null)
            {
                harmony.Patch(
                    evalBuildingCardsTarget,
                    prefix: new HarmonyMethod(
                        typeof(EvaluateAvailableBuildingCardsPatch),
                        nameof(EvaluateAvailableBuildingCardsPatch.Prefix)
                    )
                );
                Log.LogInfo("Patch applied: GameCardController.EvaluateAvailableBuildingCards.");
            }

            // --- Patch 4: TaskOutputLibrary.Execute ---
            // Logs library task completion for debugging.
            // The original handles AddResearchedTechnology and DequeueTechnology.

            Type taskOutputLibraryType = AccessTools.TypeByName("TaskOutputLibrary");
            MethodInfo taskOutputLibraryTarget = taskOutputLibraryType != null
                ? AccessTools.Method(taskOutputLibraryType, "Execute")
                : null;

            if (taskOutputLibraryTarget == null)
            {
                Log.LogError("Could not find TaskOutputLibrary.Execute.");
            }
            else
            {
                harmony.Patch(
                    taskOutputLibraryTarget,
                    prefix: new HarmonyMethod(
                        typeof(TaskOutputLibraryPatch),
                        nameof(TaskOutputLibraryPatch.Prefix)
                    )
                );
                Log.LogInfo("Patch applied: TaskOutputLibrary.Execute.");
            }

            // --- Patch 5: CancelResearchAction.Execute ---
            // Removes the tech from queuedResearches when cancelled.

            MethodInfo cancelExecuteTarget = AccessTools.Method(
                typeof(CancelResearchAction), "Execute"
            );

            if (cancelExecuteTarget != null)
            {
                harmony.Patch(
                    cancelExecuteTarget,
                    postfix: new HarmonyMethod(
                        typeof(CancelResearchExecutePatch),
                        nameof(CancelResearchExecutePatch.Postfix)
                    )
                );
                Log.LogInfo("Patch applied: CancelResearchAction.Execute.");
            }

            // --- Patch 6: LibraryComponent.DequeueTechnology ---
            // Fires when any research leaves the library queue.
            // Marks the tech as checked and flushes deferred AP grants.

            MethodInfo dequeueTechTarget = AccessTools.Method(
                typeof(LibraryComponent), "DequeueTechnology"
            );

            if (dequeueTechTarget != null)
            {
                harmony.Patch(
                    dequeueTechTarget,
                    prefix: new HarmonyMethod(
                        typeof(DequeueTechnologyPatch),
                        nameof(DequeueTechnologyPatch.Prefix)
                    ),
                    postfix: new HarmonyMethod(
                        typeof(DequeueTechnologyPatch),
                        nameof(DequeueTechnologyPatch.Postfix)
                    )
                );
                Log.LogInfo("Patch applied: LibraryComponent.DequeueTechnology.");
            }

            // --- Patch 7: StartResearchAction.IsValid ---
            // Sets IsCheckingResearchValidity and CurrentlyValidatingTech around
            // the call so HasResearchedTechnologyPatch knows when and what to intercept.

            MethodInfo isValidTarget = AccessTools.Method(
                typeof(StartResearchAction),
                "IsValid",
                new Type[] { typeof(short), typeof(Entity), typeof(TechnologyDefinition),
                             typeof(string), typeof(int), typeof(string).MakeByRefType() }
            );

            if (isValidTarget != null)
            {
                harmony.Patch(
                    isValidTarget,
                    prefix: new HarmonyMethod(
                        typeof(StartResearchIsValidPatch),
                        nameof(StartResearchIsValidPatch.Prefix)
                    ),
                    postfix: new HarmonyMethod(
                        typeof(StartResearchIsValidPatch),
                        nameof(StartResearchIsValidPatch.Postfix)
                    )
                );
                Log.LogInfo("Patch applied: StartResearchAction.IsValid.");
            }

            // --- Patch 8: HasResearchedTechnology ---
            // Only active during StartResearchAction.IsValid.
            // Returns true for player-checked techs so they satisfy prerequisites.
            // Steps aside for the tech being validated itself.

            MethodInfo hasResearchedTarget = AccessTools.Method(
                playerType,
                "HasResearchedTechnology",
                new Type[] { technologyType }
            );

            if (hasResearchedTarget != null)
            {
                harmony.Patch(
                    hasResearchedTarget,
                    prefix: new HarmonyMethod(
                        typeof(HasResearchedTechnologyPatch),
                        nameof(HasResearchedTechnologyPatch.Prefix)
                    )
                );
                Log.LogInfo("Patch applied: HasResearchedTechnology.");
            }

            // --- Patch 9: TechnologyButton.Update_Internal ---
            // Shows checkmark for player-checked techs.
            // Everything else renders normally.

            MethodInfo updateInternalTarget = AccessTools.Method(
                typeof(TechnologyButton), "Update_Internal"
            );

            if (updateInternalTarget == null)
            {
                Log.LogError("Could not find TechnologyButton.Update_Internal.");
            }
            else
            {
                harmony.Patch(
                    updateInternalTarget,
                    postfix: new HarmonyMethod(
                        typeof(TechnologyButtonUpdatePatch),
                        nameof(TechnologyButtonUpdatePatch.Postfix)
                    )
                );
                Log.LogInfo("Patch applied: TechnologyButton.Update_Internal.");
            }

            // --- Patch 10: TechnologyDetails.Update_Internal ---
            // For AP-granted items the player hasn't checked yet:
            // undoes the "done" state and shows the Research button.

            MethodInfo detailsUpdateTarget = AccessTools.Method(
                AccessTools.TypeByName("TechnologyDetails"), "Update_Internal"
            );

            if (detailsUpdateTarget == null)
            {
                Log.LogError("Could not find TechnologyDetails.Update_Internal.");
            }
            else
            {
                harmony.Patch(
                    detailsUpdateTarget,
                    postfix: new HarmonyMethod(
                        typeof(TechnologyDetailsUpdatePatch),
                        nameof(TechnologyDetailsUpdatePatch.Postfix)
                    )
                );
                Log.LogInfo("Patch applied: TechnologyDetails.Update_Internal.");
            }

            // --- Patch 11: ReadyToStartGame ---
            // Flushes pending AP grants when the game finishes loading.

            MethodInfo readyToStartTarget = AccessTools.Method(
                typeof(GameController), "ReadyToStartGame"
            );

            if (readyToStartTarget == null)
            {
                Log.LogError("Could not find ReadyToStartGame.");
            }
            else
            {
                harmony.Patch(
                    readyToStartTarget,
                    postfix: new HarmonyMethod(
                        typeof(ReadyToStartGamePatch),
                        nameof(ReadyToStartGamePatch.Postfix)
                    )
                );
                Log.LogInfo("Patch applied: ReadyToStartGame.");
            }

            // Connect to Archipelago - always last so all patches are in place first
            ArchipelagoManager.Connect();

            Log.LogInfo("BWL Archipelago mod ready.");
        }
    }

    // Patch 1: Sends the AP check when the player queues a research.
    // Postfix logs state after Execute for debugging.
    public static class StartResearchExecutePatch
    {
        public static bool Prefix(StartResearchAction __instance)
        {
            FieldInfo techField = AccessTools.Field(
                typeof(StartResearchAction), "technology"
            );
            TechnologyDefinition technology = techField?.GetValue(__instance)
                as TechnologyDefinition;

            if (technology == null)
                return true;

            string techName = technology.Name;

            if (ArchipelagoManager.IsResearchQueued(techName))
            {
                BWLArchipelagoPlugin.Log.LogInfo(
                    "Already queued check for: " + techName + " - blocking execute."
                );
                return false;
            }

            BWLArchipelagoPlugin.Log.LogInfo(
                "Research queued, sending AP check: " + techName
            );
            ArchipelagoManager.SendCheck("Researched " + techName);

            return true;
        }

        public static void Postfix(StartResearchAction __instance)
        {
            FieldInfo libField = AccessTools.Field(
                typeof(StartResearchAction), "libraryEntity"
            );
            Entity libraryEntity = libField?.GetValue(__instance) as Entity;
            if (libraryEntity == null) return;

            BuildingComponent building = libraryEntity.GetBuilding();
            LibraryComponent library = libraryEntity.GetLibrary();

            var tasks = building?.GetBuildingTasks();
            BWLArchipelagoPlugin.Log.LogInfo(
                "After Execute - ActiveTasks: " + (building?.ActiveTasks?.Count ?? -1) +
                " | Researching: " + (library?.ResearchingTechnologies?.Count ?? -1) +
                " | BuildingTasks: " + (tasks?.Count ?? -1) +
                " | TaskIsValid: " + (tasks?.Count > 0
                    ? tasks[0].IsValid(libraryEntity, null).ToString()
                    : "no tasks")
            );
        }
    }

    // Patch 2: Prefix sets IsCompletingPlayerCheck for non-AP calls.
    // Postfix removes tech from researchedTechnologies after the original runs.
    // The original runs fully so library queue transitions work correctly.
    public static class AddResearchedTechnologyPatch
    {
        public static void Prefix(
            object __instance,
            object technology,
            object discoveringEntity,
            bool grantedByRepair)
        {
            if (technology == null) return;
            if (ArchipelagoManager.IsGrantingTechnology) return;

            ArchipelagoManager.IsCompletingPlayerCheck = true;
        }

        public static void Postfix(
            object __instance,
            object technology,
            object discoveringEntity,
            bool grantedByRepair)
        {
            string techName = GetName(technology);
            BWLArchipelagoPlugin.Log.LogInfo(
                "AddResearchedTechnology Postfix - technology: " + techName +
                " | IsGrantingTechnology: " + ArchipelagoManager.IsGrantingTechnology
            );

            if (!ArchipelagoManager.IsCompletingPlayerCheck) return;
            ArchipelagoManager.IsCompletingPlayerCheck = false;
        }

        internal static string GetName(object technology)
        {
            if (technology == null)
                return "<null>";

            FieldInfo field = AccessTools.Field(technology.GetType(), "Name");
            if (field != null)
            {
                object val = field.GetValue(technology);
                if (val != null)
                    return val.ToString();
            }

            PropertyInfo prop = AccessTools.Property(technology.GetType(), "Name");
            if (prop != null)
            {
                object val = prop.GetValue(technology, null);
                if (val != null)
                    return val.ToString();
            }

            return technology.GetType().Name;
        }
    }

    // Patch 3: Manages granted tech visibility during EvaluateAvailableBuildings.
    // Skips entirely during player checks.
    // For all other calls: adds all granted techs before evaluation so buildings
    // stay unlocked, then removes them after so no checkmarks appear.
    public static class EvaluateAvailableBuildingsPatch
    {
        public static bool Prefix()
        {
            if (ArchipelagoManager.IsCompletingPlayerCheck)
                return false;

            ArchipelagoManager.AddGrantedTechsToResearched();
            return true;
        }

        public static void Postfix()
        {
            if (ArchipelagoManager.IsCompletingPlayerCheck)
                return;

            ArchipelagoManager.RemoveGrantedTechsFromResearched();
        }
    }

    public static class EvaluateBuildingButtonsPatch
    {
        public static bool Prefix()
        {
            return !ArchipelagoManager.IsCompletingPlayerCheck;
        }
    }

    public static class EvaluateAvailableBuildingCardsPatch
    {
        public static bool Prefix()
        {
            return !ArchipelagoManager.IsCompletingPlayerCheck;
        }
    }

    // Patch 4: Logs library task completion.
    // The original handles AddResearchedTechnology and DequeueTechnology.
    public static class TaskOutputLibraryPatch
    {
        public static bool Prefix(Entity entity)
        {
            if (ArchipelagoManager.IsGrantingTechnology)
                return true;

            LibraryComponent library = entity?.GetLibrary();
            if (library == null) return true;
            if (library.ResearchingTechnologies.Count == 0) return true;

            TechnologyDefinition technology = library.ResearchingTechnologies[0];
            string techName = technology.Name;

            BWLArchipelagoPlugin.Log.LogInfo(
                "TaskOutputLibrary firing for: " + techName +
                " | IsQueued: " + ArchipelagoManager.IsResearchQueued(techName)
            );

            return true;
        }
    }

    // Patch 5: Removes the tech from queuedResearches when cancelled.
    public static class CancelResearchExecutePatch
    {
        public static void Postfix(CancelResearchAction __instance)
        {
            FieldInfo techField = AccessTools.Field(
                typeof(CancelResearchAction), "technology"
            );
            TechnologyDefinition technology = techField?.GetValue(__instance)
                as TechnologyDefinition;

            if (technology == null) return;

            ArchipelagoManager.CancelResearch(technology.Name);
        }
    }

    // Patch 6: Fires when any research leaves the library queue.
    // Prefix marks the tech as checked so the checkmark appears.
    // Postfix flushes deferred AP grants after the dequeue fully completes.
    public static class DequeueTechnologyPatch
    {
        public static void Prefix(LibraryComponent __instance, TechnologyDefinition technology)
        {
            if (technology == null) return;
            if (ArchipelagoManager.IsGrantingTechnology) return;

            string techName = technology.Name;

            BWLArchipelagoPlugin.Log.LogInfo(
                "DequeueTechnology called for: " + techName +
                " | IsQueued: " + ArchipelagoManager.IsResearchQueued(techName)
            );

            if (!ArchipelagoManager.IsResearchQueued(techName)) return;

            ArchipelagoManager.MarkTechnologyResearched(techName);
        }

        public static void Postfix(LibraryComponent __instance, TechnologyDefinition technology)
        {
            if (technology == null) return;
            if (ArchipelagoManager.IsGrantingTechnology) return;
            if (!ArchipelagoManager.IsTechnologyResearched(technology.Name)) return;

            // Flush deferred grants now that dequeue has fully completed
            // and the library is no longer busy with this research.
            ArchipelagoManager.OnGameReady();

            BuildingComponent building = __instance.Owner?.GetBuilding();
            BWLArchipelagoPlugin.Log.LogInfo(
                "After Dequeue flush - ActiveTasks: " +
                (building?.ActiveTasks?.Count ?? -1) +
                " | Researching: " + __instance.ResearchingTechnologies.Count +
                " | Operating: " + (building?.Operating ?? false)
            );
        }
    }

    // Patch 7: Sets flags around StartResearchAction.IsValid.
    public static class StartResearchIsValidPatch
    {
        public static void Prefix(TechnologyDefinition technology)
        {
            ArchipelagoManager.IsCheckingResearchValidity = true;
            ArchipelagoManager.CurrentlyValidatingTech = technology?.Name;
        }

        public static void Postfix()
        {
            ArchipelagoManager.IsCheckingResearchValidity = false;
            ArchipelagoManager.CurrentlyValidatingTech = null;
        }
    }

    // Patch 8: Only active during StartResearchAction.IsValid.
    // Returns true for player-checked techs so they satisfy prerequisites.
    public static class HasResearchedTechnologyPatch
    {
        public static bool Prefix(
            object __instance,
            object technology,
            ref bool __result)
        {
            if (technology == null)
                return true;

            string techName = AddResearchedTechnologyPatch.GetName(technology);
            if (techName == null || techName == "<null>")
                return true;

            // Return true for player-checked techs so prerequisites are satisfied
            // everywhere - including TaskOutputLibrary.IsValid which checks prereqs
            // before allowing the library task to start.
            if (ArchipelagoManager.IsTechnologyResearched(techName))
            {
                __result = true;
                return false;
            }

            // Also return true for AP-granted techs during IsValid checks only,
            // so they satisfy prerequisites for downstream research.
            if (ArchipelagoManager.IsCheckingResearchValidity &&
                techName != ArchipelagoManager.CurrentlyValidatingTech &&
                ArchipelagoManager.IsTechnologyGranted(techName))
            {
                __result = true;
                return false;
            }

            return true;
        }
    }

    // Patch 9: Shows checkmark for player-checked techs in the tech tree.
    public static class TechnologyButtonUpdatePatch
    {
        public static void Postfix(object __instance)
        {
            FieldInfo techField = AccessTools.Field(__instance.GetType(), "technology");
            object technology = techField?.GetValue(__instance);
            if (technology == null) return;

            string techName = AddResearchedTechnologyPatch.GetName(technology);
            if (techName == null || techName == "<null>") return;

            if (!ArchipelagoManager.IsTechnologyResearched(techName)) return;

            FieldInfo doneField = AccessTools.Field(__instance.GetType(), "Done");
            FieldInfo zoomedDoneField = AccessTools.Field(
                __instance.GetType(), "ZoomedDone"
            );
            FieldInfo backgroundField = AccessTools.Field(
                __instance.GetType(), "Background"
            );
            FieldInfo completeBgField = AccessTools.Field(
                __instance.GetType(), "CompleteBackground"
            );
            FieldInfo doneIconField = AccessTools.Field(__instance.GetType(), "DoneIcon");
            FieldInfo iconField = AccessTools.Field(__instance.GetType(), "Icon");
            FieldInfo requirementsField = AccessTools.Field(
                __instance.GetType(), "requirements"
            );
            FieldInfo timerBgField = AccessTools.Field(
                __instance.GetType(), "TimerBackground"
            );
            FieldInfo zoomedTimerBgField = AccessTools.Field(
                __instance.GetType(), "ZoomedTimerBackground"
            );
            FieldInfo buttonField = AccessTools.Field(__instance.GetType(), "button");
            FieldInfo colourBorderField = AccessTools.Field(
                __instance.GetType(), "ColourBorder"
            );
            FieldInfo colourBorderZoomedField = AccessTools.Field(
                __instance.GetType(), "ColourBorderZoomed"
            );

            object done = doneField?.GetValue(__instance);
            object zoomedDone = zoomedDoneField?.GetValue(__instance);
            object background = backgroundField?.GetValue(__instance);
            object completeBg = completeBgField?.GetValue(__instance);
            object doneIcon = doneIconField?.GetValue(__instance);
            object icon = iconField?.GetValue(__instance);
            object requirements = requirementsField?.GetValue(__instance);
            object timerBg = timerBgField?.GetValue(__instance);
            object zoomedTimerBg = zoomedTimerBgField?.GetValue(__instance);
            object button = buttonField?.GetValue(__instance);
            object colourBorder = colourBorderField?.GetValue(__instance);
            object colourBorderZoomed = colourBorderZoomedField?.GetValue(__instance);

            if (done is GameObject doneGo) doneGo.SetActive(true);
            if (zoomedDone is GameObject zDoneGo) zDoneGo.SetActive(true);
            if (doneIcon is Image doneImg) doneImg.gameObject.SetActive(true);
            if (icon is Image iconImg) iconImg.gameObject.SetActive(false);
            if (requirements is GameObject reqGo) reqGo.SetActive(false);
            if (timerBg is GameObject timerGo) timerGo.SetActive(false);
            if (zoomedTimerBg is GameObject zTimerGo) zTimerGo.SetActive(false);
            if (background is Image bgImg && completeBg is Sprite completeSpr)
                bgImg.sprite = completeSpr;
            if (button is Button btn) btn.interactable = true;
            if (colourBorder is Image cb) cb.gameObject.SetActive(false);
            if (colourBorderZoomed is Image cbz) cbz.gameObject.SetActive(false);
        }
    }

    // Patch 10: For AP-granted items the player hasn't checked yet,
    // undoes the "done" state and shows the Research button in the popup.
    public static class TechnologyDetailsUpdatePatch
    {
        public static void Postfix(object __instance)
        {
            FieldInfo techField = AccessTools.Field(__instance.GetType(), "technology");
            object technology = techField?.GetValue(__instance);
            if (technology == null) return;

            string techName = AddResearchedTechnologyPatch.GetName(technology);
            if (techName == null || techName == "<null>") return;

            bool isPlayerChecked = ArchipelagoManager.IsTechnologyResearched(techName);
            bool isAPGranted = ArchipelagoManager.IsTechnologyGranted(techName);

            if (!isAPGranted || isPlayerChecked) return;

            FieldInfo doneField = AccessTools.Field(__instance.GetType(), "Done");
            FieldInfo researchButtonField = AccessTools.Field(
                __instance.GetType(), "ResearchButton"
            );
            FieldInfo researchButtonTextField = AccessTools.Field(
                __instance.GetType(), "ResearchButtonText"
            );
            FieldInfo requirementsField = AccessTools.Field(
                __instance.GetType(), "requirements"
            );
            FieldInfo timeDisplayField = AccessTools.Field(
                __instance.GetType(), "TimeDisplay"
            );
            FieldInfo infoBackgroundField = AccessTools.Field(
                __instance.GetType(), "InfoBackground"
            );
            FieldInfo viewField = AccessTools.Field(__instance.GetType(), "view");
            FieldInfo libraryField = AccessTools.Field(__instance.GetType(), "library");

            object done = doneField?.GetValue(__instance);
            object researchButton = researchButtonField?.GetValue(__instance);
            object researchButtonText = researchButtonTextField?.GetValue(__instance);
            object requirements = requirementsField?.GetValue(__instance);
            object timeDisplay = timeDisplayField?.GetValue(__instance);
            object infoBackground = infoBackgroundField?.GetValue(__instance);
            object view = viewField?.GetValue(__instance);
            object library = libraryField?.GetValue(__instance);

            if (done is GameObject doneGo) doneGo.SetActive(false);
            if (requirements is GameObject reqGo) reqGo.SetActive(true);
            if (timeDisplay is GameObject timeGo) timeGo.SetActive(true);
            if (infoBackground is GameObject infoBgGo) infoBgGo.SetActive(true);

            PropertyInfo gameObjectProp = researchButton?.GetType().GetProperty("gameObject");
            object researchButtonGo = gameObjectProp?.GetValue(researchButton, null);
            if (researchButtonGo is GameObject rbGo) rbGo.SetActive(true);

            if (researchButtonText is TextMeshProUGUI rbText)
                rbText.text = GameController.GetTranslation("UI/Research");

            if (view != null && library is Entity libraryEntity)
            {
                PropertyInfo playerProp = AccessTools.Property(view.GetType(), "Player");
                object player = playerProp?.GetValue(view, null);

                if (player != null && technology is TechnologyDefinition techDef)
                {
                    PropertyInfo playerIdProp = AccessTools.Property(
                        player.GetType(), "PlayerId"
                    );
                    object playerId = playerIdProp?.GetValue(player, null);

                    string errorReason;
                    bool isValid = StartResearchAction.IsValid(
                        (short)(int)playerId,
                        libraryEntity,
                        techDef,
                        "",
                        0,
                        out errorReason
                    );

                    PropertyInfo buttonProp = AccessTools.Property(
                        researchButton.GetType(), "Button"
                    );
                    object btn = buttonProp?.GetValue(researchButton, null);
                    if (btn is Button b) b.interactable = isValid;
                }
            }
        }
    }

    // Patch 11: Flushes pending AP grants when the game finishes loading.
    public static class ReadyToStartGamePatch
    {
        public static void Postfix()
        {
            BWLArchipelagoPlugin.Log.LogInfo("Game ready - flushing pending unlocks.");
            ArchipelagoManager.OnGameReady();
        }
    }
}