using BepInEx.Configuration;
using System.Drawing;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BWLArchipelago
{
    public class ArchipelagoConnectionUI : MonoBehaviour
    {
        private string serverUrl = "";
        private string serverPort = "";
        private string slotName = "";
        private string password = "";
        private bool isVisible = false; // Hidden until main menu loads
        private string statusMessage = "";

        private void Start()
        {
            // Pre-populate fields with last used values from config
            serverUrl = BWLArchipelagoPlugin.ConfigServerUrl.Value;
            serverPort = BWLArchipelagoPlugin.ConfigServerPort.Value.ToString();
            slotName = BWLArchipelagoPlugin.ConfigSlotName.Value;
            password = BWLArchipelagoPlugin.ConfigPassword.Value;

            Application.quitting += () => isVisible = false;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            // Listen for scene changes to show UI when main menu loads
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
        }

        private GameObject mainMenuObject;
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            BWLArchipelagoPlugin.Log.LogInfo("Scene loaded: " + scene.name);

            if (scene.name == "MainMenu")
            {
                // Cache the main menu object reference
                mainMenuObject = GameObject.Find("MainMenuCanvas");
                if (mainMenuObject == null)
                    mainMenuObject = GameObject.Find("MainMenuView");
                if (mainMenuObject == null)
                    mainMenuObject = GameObject.Find("MainMenu");

                BWLArchipelagoPlugin.Log.LogInfo(
                    "Main menu object found: " + (mainMenuObject?.name ?? "null")
                );

                if (!ArchipelagoManager.IsConnected)
                    isVisible = true;
            }
        }

        private void OnGUI()
        {
            if (!isVisible) return;
            if (!Application.isPlaying) return;

            // Hide when the main menu canvas deactivates (e.g. during quit fade)
            if (mainMenuObject != null && !mainMenuObject.activeInHierarchy)
            {
                isVisible = false;
                return;
            }

            float width = 800f;
            float height = 480f;
            float x = (Screen.width - width) / 2f;
            float y = (Screen.height - height) / 2f;

            GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.fontSize = 22;

            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 18;

            GUIStyle textFieldStyle = new GUIStyle(GUI.skin.textField);
            textFieldStyle.fontSize = 18;

            GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.fontSize = 18;

            GUIStyle statusStyle = new GUIStyle(GUI.skin.label);
            statusStyle.fontSize = 15;

            GUI.Box(new Rect(x, y, width, height), "Archipelago Connection", boxStyle);

            float labelX = x + 25f;
            float fieldX = x + 200f;
            float fieldWidth = 550f;
            float startY = y + 60f;
            float lineHeight = 70f;

            GUI.Label(new Rect(labelX, startY, 175f, 40f), "Server:", labelStyle);
            serverUrl = GUI.TextField(
                new Rect(fieldX, startY, fieldWidth, 40f), serverUrl, textFieldStyle
            );

            GUI.Label(new Rect(labelX, startY + lineHeight, 175f, 40f), "Port:", labelStyle);
            serverPort = GUI.TextField(
                new Rect(fieldX, startY + lineHeight, fieldWidth, 40f), serverPort, textFieldStyle
            );

            GUI.Label(new Rect(labelX, startY + lineHeight * 2, 175f, 40f), "Slot Name:", labelStyle);
            slotName = GUI.TextField(
                new Rect(fieldX, startY + lineHeight * 2, fieldWidth, 40f), slotName, textFieldStyle
            );

            GUI.Label(new Rect(labelX, startY + lineHeight * 3, 175f, 40f), "Password:", labelStyle);
            password = GUI.PasswordField(
                new Rect(fieldX, startY + lineHeight * 3, fieldWidth, 40f),
                password, '*', textFieldStyle
            );

            if (GUI.Button(
                new Rect(x + 25f, startY + lineHeight * 4, 365f, 55f),
                "Connect", buttonStyle))
            {
                TryConnect();
            }

            if (GUI.Button(
                new Rect(x + 410f, startY + lineHeight * 4, 365f, 55f),
                "Play Offline", buttonStyle))
            {
                isVisible = false;
            }

            if (!string.IsNullOrEmpty(statusMessage))
            {
                GUI.Label(
                    new Rect(x + 25f, startY + lineHeight * 4 + 60f, 750f, 40f),
                    statusMessage, statusStyle
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
        private void Update()
        {
            if (!isVisible) return;

            // If the main menu canvas is gone, hide the connection UI
            // This catches the quit fade-out without needing to patch the quit button
            GameObject menuCanvas = GameObject.Find("MainMenuCanvas");
            if (menuCanvas == null)
                menuCanvas = GameObject.Find("MainMenu");
            if (menuCanvas == null)
                menuCanvas = GameObject.Find("Canvas");

            if (menuCanvas != null && !menuCanvas.activeInHierarchy)
            {
                isVisible = false;
            }
        }

        private void OnApplicationQuit()
        {
            isVisible = false;
        }
        private void OnSceneUnloaded(Scene scene)
        {
            BWLArchipelagoPlugin.Log.LogInfo("Scene unloaded: " + scene.name);
            isVisible = false;
        }
    }
}