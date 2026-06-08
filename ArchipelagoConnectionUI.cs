using BepInEx.Configuration;
using UnityEngine;

namespace BWLArchipelago
{
    // In-game connection UI. Appears on launch so the player can enter their
    // Archipelago server details without editing any files. Last used values
    // are saved to the BepInEx config so they pre-populate on next launch.
    public class ArchipelagoConnectionUI : MonoBehaviour
    {
        private string serverUrl = "";
        private string serverPort = "";
        private string slotName = "";
        private string password = "";
        private bool isVisible = true;
        private string statusMessage = "";

        private void Start()
        {
            // Pre-populate fields with last used values from config
            serverUrl = BWLArchipelagoPlugin.ConfigServerUrl.Value;
            serverPort = BWLArchipelagoPlugin.ConfigServerPort.Value.ToString();
            slotName = BWLArchipelagoPlugin.ConfigSlotName.Value;
            password = BWLArchipelagoPlugin.ConfigPassword.Value;
        }

        private void OnGUI()
        {
            if (!isVisible) return;

            float width = 420f;
            float height = 270f;
            float x = (Screen.width - width) / 2f;
            float y = (Screen.height - height) / 2f;

            GUI.Box(new Rect(x, y, width, height), "Archipelago Connection");

            float labelX = x + 15f;
            float fieldX = x + 130f;
            float fieldWidth = 270f;
            float startY = y + 35f;
            float lineHeight = 38f;

            GUI.Label(new Rect(labelX, startY, 115f, 25f), "Server:");
            serverUrl = GUI.TextField(
                new Rect(fieldX, startY, fieldWidth, 25f), serverUrl
            );

            GUI.Label(new Rect(labelX, startY + lineHeight, 115f, 25f), "Port:");
            serverPort = GUI.TextField(
                new Rect(fieldX, startY + lineHeight, fieldWidth, 25f), serverPort
            );

            GUI.Label(new Rect(labelX, startY + lineHeight * 2, 115f, 25f), "Slot Name:");
            slotName = GUI.TextField(
                new Rect(fieldX, startY + lineHeight * 2, fieldWidth, 25f), slotName
            );

            GUI.Label(new Rect(labelX, startY + lineHeight * 3, 115f, 25f), "Password:");
            password = GUI.PasswordField(
                new Rect(fieldX, startY + lineHeight * 3, fieldWidth, 25f), password, '*'
            );

            if (GUI.Button(
                new Rect(x + 15f, startY + lineHeight * 4, 185f, 32f), "Connect"))
            {
                TryConnect();
            }

            if (GUI.Button(
                new Rect(x + 215f, startY + lineHeight * 4, 185f, 32f), "Play Offline"))
            {
                isVisible = false;
            }

            if (!string.IsNullOrEmpty(statusMessage))
            {
                GUI.Label(
                    new Rect(x + 15f, startY + lineHeight * 4 + 38f, 390f, 25f),
                    statusMessage
                );
            }
        }

        private void TryConnect()
        {
            if (string.IsNullOrEmpty(slotName))
            {
                statusMessage = "Slot name is required.";
                return;
            }

            if (!int.TryParse(serverPort, out int port))
            {
                statusMessage = "Invalid port number.";
                return;
            }

            statusMessage = "Connecting...";

            ArchipelagoManager.ServerUrl = serverUrl;
            ArchipelagoManager.ServerPort = port;
            ArchipelagoManager.SlotName = slotName;
            ArchipelagoManager.Password = password;

            bool success = ArchipelagoManager.Connect();

            if (success)
            {
                statusMessage = "Connected!";

                // Save successful connection details for next launch
                BWLArchipelagoPlugin.ConfigServerUrl.Value = serverUrl;
                BWLArchipelagoPlugin.ConfigServerPort.Value = port;
                BWLArchipelagoPlugin.ConfigSlotName.Value = slotName;
                BWLArchipelagoPlugin.ConfigPassword.Value = password;

                isVisible = false;
            }
            else
            {
                statusMessage = "Connection failed. Check your details and try again.";
            }
        }
    }
}
