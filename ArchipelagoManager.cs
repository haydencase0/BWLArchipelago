using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace BWLArchipelago
{
    public static class ArchipelagoManager
    {
        // Connection details - set by ArchipelagoConnectionUI before Connect() is called
        public static string ServerUrl = "archipelago.gg";
        public static int ServerPort = 38281;
        public static string SlotName = "";
        public static string Password = "";
        public static string GameName = "Before We Leave";

        // Session
        private static ArchipelagoSession session;

        // State
        public static bool IsConnected = false;

        // Set to true while GrantTechnology is calling AddResearchedTechnology,
        // so AddResearchedTechnologyPatch lets it through.
        public static bool IsGrantingTechnology = false;

        // Set to true while AddResearchedTechnology is running for a player-checked tech.
        // EvaluateAvailableBuildings, EvaluateBuildingButtons, and
        // EvaluateAvailableBuildingCards are all skipped in this context so
        // buildings only unlock from AP grants, not from player checks.
        public static bool IsCompletingPlayerCheck = false;

        // Set to true while StartResearchAction.IsValid is running so
        // HasResearchedTechnologyPatch can intercept prerequisite checks.
        public static bool IsCheckingResearchValidity = false;

        // The tech currently being validated by StartResearchAction.IsValid.
        // HasResearchedTechnologyPatch skips this tech so it isn't blocked
        // as "already researched" when the player is trying to research it.
        public static string CurrentlyValidatingTech = null;

        // Technologies received from AP but not yet granted (game not loaded yet,
        // or library was busy researching when the item arrived).
        private static List<string> pendingUnlocks = new List<string>();

        // Checks that couldn't be sent because AP wasn't connected yet.
        private static List<string> pendingChecks = new List<string>();

        // Technologies the player has successfully sent as AP location checks.
        // Drives checkmark display and prerequisite satisfaction.
        private static HashSet<string> researchedTechnologies = new HashSet<string>();

        // Technologies successfully granted to the player via AP.
        // Tracked to avoid granting the same tech twice.
        // Also used by EvaluateAvailableBuildingsPatch to keep buildings unlocked.
        private static HashSet<string> grantedTechnologies = new HashSet<string>();

        // Techs the player has queued for research but library hasn't finished yet.
        // Prevents duplicate checks if the player clicks Research multiple times.
        private static HashSet<string> queuedResearches = new HashSet<string>();

        private static List<(string resourceName, int amount)> pendingResourceGrants 
            = new List<(string, int)>();

        // Maps AP item names to internal game technology names where they differ.
        private static readonly Dictionary<string, string> itemNameToTechName
            = new Dictionary<string, string>
            {
                // Add mappings here as you discover mismatches between AP and the game.
                // Example: { "Farm", "Gardening" },
            };

        // Maps progressive AP item names to an ordered list of game tech names.
        // Each time a progressive item is received, the next ungranted tech in the
        // list is granted. This prevents players from getting stuck behind high-level
        // techs they can't use yet.
        private static readonly Dictionary<string, List<string>> progressiveTechGroups
            = new Dictionary<string, List<string>>
            {
                { "Progressive Housing", new List<string> { "House", "School", "Apartment" } },
                { "Progressive Mining", new List<string> { "Mining", "Metalwork", "Glass", "Laser" } },
                { "Progressive Elevator", new List<string> { "Elevator", "SpaceElevator" } },
                { "Progressive Power", new List<string> { "Repair", "Power", "OilPower", "CleanPower" } },
                { "Progressive Happiness", new List<string> { "Pump", "Music", "MeetingSquare" } },
                { "Progressive Food", new List<string> { "Gardening", "Cooking", "Farming", "Baking" } },
                { "Progressive Upgrade", new List<string> { "Tinkering", "Automation", "Filtering" } },
                { "Progressive Rocket", new List<string> { "Fuel", "Space" } },
                { "Progressive Shipping", new List<string> { "Shipping", "AdvancedShipping", "Airships" } }
            };

        // Logger reference
        private static ManualLogSource Log;

        public static void Initialize(ManualLogSource log)
        {
            Log = log;
            Log.LogInfo("ArchipelagoManager initialized.");
        }

        // Connects to the AP server using the current ServerUrl, ServerPort,
        // SlotName, and Password values. Returns true on success.
        public static bool Connect()
        {
            Log.LogInfo(
                "Connecting to Archipelago server: " +
                ServerUrl + ":" + ServerPort + " as " + SlotName
            );

            session = ArchipelagoSessionFactory.CreateSession(ServerUrl, ServerPort);

            session.Items.ItemReceived += OnItemReceived;
            session.Socket.ErrorReceived += OnError;
            session.Socket.SocketClosed += OnSocketClosed;

            LoginResult result = session.TryConnectAndLogin(
                GameName,
                SlotName,
                ItemsHandlingFlags.AllItems,
                password: Password
            );

            if (result.Successful)
            {
                IsConnected = true;
                Log.LogInfo("Connected to Archipelago successfully!");

                // Flush any checks that were queued while disconnected
                if (pendingChecks.Count > 0)
                {
                    Log.LogInfo("Flushing " + pendingChecks.Count + " pending checks.");
                    List<string> toSend = new List<string>(pendingChecks);
                    pendingChecks.Clear();
                    foreach (string check in toSend)
                        SendCheckInternal(check);
                }

                return true;
            }
            else
            {
                IsConnected = false;
                LoginFailure failure = (LoginFailure)result;
                foreach (string error in failure.Errors)
                    Log.LogError("Archipelago connection error: " + error);
                return false;
            }
        }

        public static void Disconnect()
        {
            if (session != null && IsConnected)
            {
                session.Socket.DisconnectAsync();
                IsConnected = false;
                Log.LogInfo("Disconnected from Archipelago.");
            }
        }

        // Called by StartResearchExecutePatch when the player queues a research.
        public static void SendCheck(string locationName)
        {
            string techName = locationName.Replace("Researched ", "");

            if (!IsConnected)
            {
                Log.LogWarning("Not connected - queuing check: " + locationName);
                queuedResearches.Add(techName);
                pendingChecks.Add(locationName);
                return;
            }

            queuedResearches.Add(techName);
            SendCheckInternal(locationName);
        }

        private static void SendCheckInternal(string locationName)
        {
            Log.LogInfo("Sending check: " + locationName);

            long locationId = session.Locations.GetLocationIdFromName(
                GameName,
                locationName
            );

            if (locationId < 0)
            {
                Log.LogError("Unknown location: " + locationName);
                return;
            }

            session.Locations.CompleteLocationChecks(locationId);
            Log.LogInfo("Check sent: " + locationName);
        }

        // Called by DequeueTechnologyPatch when the library finishes a player-queued research.
        public static void MarkTechnologyResearched(string techName)
        {
            researchedTechnologies.Add(techName);
            Log.LogInfo("Marked as researched (checkmark): " + techName);
        }

        // Called by CancelResearchExecutePatch when the player cancels a research.
        public static void CancelResearch(string techName)
        {
            queuedResearches.Remove(techName);
            Log.LogInfo("Research cancelled, removed from queue: " + techName);
        }

        public static bool IsResearchQueued(string techName)
        {
            return queuedResearches.Contains(techName);
        }

        public static bool IsTechnologyResearched(string techName)
        {
            return researchedTechnologies.Contains(techName);
        }

        public static bool IsTechnologyGranted(string techName)
        {
            return grantedTechnologies.Contains(techName);
        }

        // Temporarily adds all granted techs to the game's researchedTechnologies set
        // so EvaluateAvailableBuildings sees them and keeps buildings unlocked.
        public static void AddGrantedTechsToResearched()
        {
            object[] context = GetPlayerAndTechManager();
            if (context == null) return;

            object player = context[0];
            object techManager = context[1];
            MethodInfo getTechnology = context[2] as MethodInfo;

            FieldInfo researchedField = AccessTools.Field(
                player.GetType(), "researchedTechnologies"
            );
            object researchedSet = researchedField?.GetValue(player);
            if (researchedSet == null) return;

            MethodInfo addMethod = researchedSet.GetType().GetMethod("Add");

            foreach (string techName in grantedTechnologies)
            {
                object techDef = getTechnology?.Invoke(
                    techManager, new object[] { techName }
                );
                if (techDef != null)
                    addMethod?.Invoke(researchedSet, new object[] { techDef });
            }
        }

        // Removes all granted techs from the game's researchedTechnologies set
        // after EvaluateAvailableBuildings has run.
        public static void RemoveGrantedTechsFromResearched()
        {
            object[] context = GetPlayerAndTechManager();
            if (context == null) return;

            object player = context[0];
            object techManager = context[1];
            MethodInfo getTechnology = context[2] as MethodInfo;

            FieldInfo researchedField = AccessTools.Field(
                player.GetType(), "researchedTechnologies"
            );
            object researchedSet = researchedField?.GetValue(player);
            if (researchedSet == null) return;

            MethodInfo removeMethod = researchedSet.GetType().GetMethod("Remove");

            foreach (string techName in grantedTechnologies)
            {
                object techDef = getTechnology?.Invoke(
                    techManager, new object[] { techName }
                );
                if (techDef != null)
                    removeMethod?.Invoke(researchedSet, new object[] { techDef });
            }
        }

        // Shared helper to get player and tech manager via reflection.
        private static object[] GetPlayerAndTechManager()
        {
            Type gameControllerType = AccessTools.TypeByName("GameController");
            if (gameControllerType == null) return null;

            MethodInfo getSinglePlayerView = AccessTools.Method(
                gameControllerType, "GetSinglePlayerView"
            );
            object playerView = getSinglePlayerView?.Invoke(null, null);
            if (playerView == null) return null;

            PropertyInfo playerProp = AccessTools.Property(
                playerView.GetType(), "Player"
            );
            object player = playerProp?.GetValue(playerView, null);
            if (player == null) return null;

            Type appDataType = AccessTools.TypeByName("AppData");
            FieldInfo techManagerField = AccessTools.Field(
                appDataType, "technologyDefinitionManager"
            );
            object techManager = techManagerField?.GetValue(null);
            if (techManager == null) return null;

            MethodInfo getTechnology = AccessTools.Method(
                techManager.GetType(),
                "GetTechnology",
                new Type[] { typeof(string) }
            );

            return new object[] { player, techManager, getTechnology };
        }

        // Returns true if any library is currently researching a technology.
        // Used by GrantTechnology to defer grants until research completes.
        private static bool IsAnyLibraryBusy()
        {
            try
            {
                foreach (Entity entity in GameManager.EntityManager
                    .GetEntitiesWithComponent(EntityComponentType.Library))
                {
                    if (!entity.Enabled) continue;
                    LibraryComponent lib = entity.GetLibrary();
                    if (lib != null && lib.IsResearching())
                        return true;
                }
            }
            catch
            {
                // EntityManager may not be ready yet - treat as not busy
            }
            return false;
        }

        // Called when the game finishes loading, and by DequeueTechnologyPatch
        // after each research completes to flush deferred grants.
        public static void OnGameReady()
        {
            Log.LogInfo("Game ready. Flushing " + pendingUnlocks.Count + " pending unlocks.");

            List<string> toGrant = new List<string>(pendingUnlocks);
            pendingUnlocks.Clear();

            foreach (string techName in toGrant)
                GrantTechnology(techName);
        }

        private static void OnItemReceived(ReceivedItemsHelper helper)
        {
            while (helper.Any())
            {
                ItemInfo item = helper.DequeueItem();
                string itemName = item.ItemName;

                Log.LogInfo("Item received from Archipelago: " + itemName);

                // Check for resource grant items
                if (itemName == "10 Stone")
                {
                    Log.LogInfo("Resource item received: " + itemName);
                    GrantResource("Stone", 10);
                    continue;
                }

                // Check if this is a progressive item first
                if (progressiveTechGroups.TryGetValue(itemName, out List<string> techList))
                {
                    // Find the first tech in the list not yet granted
                    bool found = false;
                    foreach (string techName in techList)
                    {
                        if (!grantedTechnologies.Contains(techName))
                        {
                            Log.LogInfo(
                                "Progressive item: " + itemName +
                                " -> granting: " + techName
                            );
                            GrantTechnology(techName);
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        Log.LogWarning(
                            "Progressive item received but all techs already granted: " +
                            itemName
                        );
                    }
                    continue;
                }

                // Non-progressive item - strip " Technology" suffix and apply name
                // mapping if one exists, then grant directly
                string singleTechName = itemName;
                if (singleTechName.EndsWith(" Technology"))
                    singleTechName = singleTechName.Substring(
                        0, singleTechName.Length - " Technology".Length
                    );

                if (itemNameToTechName.TryGetValue(singleTechName, out string mappedName))
                {
                    Log.LogInfo("Mapped '" + singleTechName + "' to '" + mappedName + "'");
                    singleTechName = mappedName;
                }

                GrantTechnology(singleTechName);
            }
        }

        private static void GrantTechnology(string techName)
        {
            if (grantedTechnologies.Contains(techName))
            {
                Log.LogInfo("Already granted, skipping: " + techName);
                return;
            }

            Type gameControllerType = AccessTools.TypeByName("GameController");
            if (gameControllerType == null)
            {
                Log.LogWarning("GameController not found. Queuing: " + techName);
                pendingUnlocks.Add(techName);
                return;
            }

            MethodInfo getSinglePlayerView = AccessTools.Method(
                gameControllerType, "GetSinglePlayerView"
            );
            object playerView = getSinglePlayerView?.Invoke(null, null);

            if (playerView == null)
            {
                Log.LogInfo("No PlayerView yet. Queuing: " + techName);
                pendingUnlocks.Add(techName);
                return;
            }

            if (IsAnyLibraryBusy())
            {
                Log.LogInfo("Library busy - deferring grant: " + techName);
                pendingUnlocks.Add(techName);
                return;
            }

            PropertyInfo playerProp = AccessTools.Property(playerView.GetType(), "Player");
            object player = playerProp?.GetValue(playerView, null);

            if (player == null)
            {
                Log.LogWarning("Could not get Player. Queuing: " + techName);
                pendingUnlocks.Add(techName);
                return;
            }

            Type appDataType = AccessTools.TypeByName("AppData");
            FieldInfo techManagerField = AccessTools.Field(
                appDataType, "technologyDefinitionManager"
            );
            object techManager = techManagerField?.GetValue(null);

            if (techManager == null)
            {
                Log.LogWarning("TechnologyDefinitionManager not available. Queuing: " + techName);
                pendingUnlocks.Add(techName);
                return;
            }

            MethodInfo getTechnology = AccessTools.Method(
                techManager.GetType(),
                "GetTechnology",
                new Type[] { typeof(string) }
            );
            object techDef = getTechnology?.Invoke(techManager, new object[] { techName });

            if (techDef == null)
            {
                Log.LogWarning("Could not find TechnologyDefinition for: " + techName);
                return;
            }

            Type technologyType = AccessTools.TypeByName("TechnologyDefinition");
            Type entityType = AccessTools.TypeByName("Entity");

            MethodInfo addResearched = AccessTools.Method(
                player.GetType(),
                "AddResearchedTechnology",
                new Type[] { technologyType, entityType, typeof(bool) }
            );

            if (addResearched == null)
            {
                Log.LogWarning("Could not find AddResearchedTechnology method.");
                return;
            }

            FieldInfo researchedField = AccessTools.Field(
                player.GetType(), "researchedTechnologies"
            );
            object researchedSet = researchedField?.GetValue(player);

            if (researchedSet == null)
            {
                Log.LogWarning("Could not get researchedTechnologies set.");
                return;
            }

            grantedTechnologies.Add(techName);

            IsGrantingTechnology = true;
            addResearched.Invoke(player, new object[] { techDef, null, false });
            IsGrantingTechnology = false;

            MethodInfo removeSelf = researchedSet.GetType().GetMethod("Remove");
            object wasRemoved = removeSelf?.Invoke(researchedSet, new object[] { techDef });
            Log.LogInfo("Remove result for " + techName + ": " + wasRemoved);

            Log.LogInfo("Technology granted: " + techName);
        }

        private static void GrantResource(string resourceName, int amount)
        {
            foreach (Entity entity in GameManager.EntityManager
                .GetEntitiesWithComponent(EntityComponentType.Storage))
            {
                if (!entity.Enabled) continue;
                BuildingComponent building = entity.GetBuilding();
                if (building == null) continue;
                if (!building.BuildingIsWarehouseOrStorageHub()) continue;

                StorageComponent storage = entity.GetStorage();
                if (storage == null) continue;

                ResourceStorage resource = storage.GetIncomingResourceByName(resourceName);
                if (resource != null)
                {
                    resource.Store(amount, false, false);
                    Log.LogInfo("Granted " + amount + " " + resourceName +
                        " to " + building.Name);
                    return;
                }
            }

            // No warehouse available yet - defer until one is built
            Log.LogInfo("No warehouse found - deferring resource grant: " +
                amount + " " + resourceName);
            pendingResourceGrants.Add((resourceName, amount));
        }

        public static void FlushPendingResourceGrants()
        {
            if (pendingResourceGrants.Count == 0) return;

            List<(string, int)> toGrant = new List<(string, int)>(pendingResourceGrants);
            pendingResourceGrants.Clear();

            foreach (var (resourceName, amount) in toGrant)
                GrantResource(resourceName, amount);
        }

        private static void OnError(Exception exception, string message)
        {
            Log.LogError("Archipelago socket error: " + message);
            if (exception != null)
                Log.LogError(exception.ToString());
            IsConnected = false;
        }

        private static void OnSocketClosed(string reason)
        {
            Log.LogWarning("Archipelago socket closed: " + reason);
            IsConnected = false;
        }
    }
}