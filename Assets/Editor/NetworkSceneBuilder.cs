using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

namespace NTG.EditorTools
{
    /// <summary>
    /// Tools > NTG > Build Scenes &amp; Prefabs.
    /// Generates: PlayerCapsule, NetworkManager, UICanvas prefabs and the four scenes
    /// (ServerEntry, ClientMenu, Lobby, Game), and sets the build scene list.
    ///
    /// Runtime scripts (SessionMenuUI, LobbyUI, ...) are wired automatically
    /// IF they exist in the project. Safe to re-run later to add the wiring
    /// once the scripts are in — it rebuilds everything from scratch.
    /// </summary>
    public static class NetworkSceneBuilder
    {
        private const string ScenesDir = "Assets/Scenes";
        private const string PrefabsDir = "Assets/Prefabs";
        private const string ClientMenuPath = ScenesDir + "/ClientMenu.unity";
        private const string ServerEntryPath = ScenesDir + "/ServerEntry.unity";
        private const string LobbyPath = ScenesDir + "/Lobby.unity";
        private const string GamePath = ScenesDir + "/Game.unity";

        private static Font _font;
        private static bool _missingScriptWarned;

        [MenuItem("Tools/NTG/Build Scenes && Prefabs")]
        public static void BuildAll()
        {
            _missingScriptWarned = false;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            EnsureFolder("Assets", "Scenes");
            EnsureFolder("Assets", "Prefabs");

            // Temp scene context for prefab creation.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject playerPrefab = BuildPlayerPrefab();
            GameObject nmPrefab = BuildNetworkManagerPrefab(playerPrefab);
            GameObject canvasPrefab = BuildCanvasPrefab();

            BuildServerEntryScene(nmPrefab, playerPrefab);
            BuildClientMenuScene(nmPrefab, canvasPrefab);
            BuildLobbyScene(canvasPrefab);
            BuildGameScene(canvasPrefab);

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ClientMenuPath, true),
                new EditorBuildSettingsScene(ServerEntryPath, true),
                new EditorBuildSettingsScene(LobbyPath, true),
                new EditorBuildSettingsScene(GamePath, true),
            };

            EditorSceneManager.OpenScene(ClientMenuPath);

            Debug.Log("[NTG] Scene build complete. Scenes: ClientMenu, ServerEntry, Lobby, Game. " +
                      "Build Settings scene list updated." +
                      (_missingScriptWarned
                          ? " Some scripts were missing — layout created without them; re-run this after adding the runtime scripts to wire everything."
                          : " All runtime scripts found and wired."));
        }

        // ---------- Prefabs ----------

        private static GameObject BuildPlayerPrefab()
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "PlayerCapsule";
            go.AddComponent<NetworkObject>();
            AddScriptIfExists(go, "PlayerObject");

            string path = PrefabsDir + "/PlayerCapsule.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            UnityEngine.Object.DestroyImmediate(go);
            Debug.Log("[NTG] Created " + path);
            return prefab;
        }

        private static GameObject BuildNetworkManagerPrefab(GameObject playerPrefab)
        {
            var go = new GameObject("NetworkManager");
            var nm = go.AddComponent<NetworkManager>();
            var transport = go.AddComponent<UnityTransport>();
            nm.NetworkConfig.NetworkTransport = transport; // UDP, default port 7777
            nm.AddNetworkPrefab(playerPrefab);
            AddScriptIfExists(go, "PersistentNetworkManager");

            string path = PrefabsDir + "/NetworkManager.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            UnityEngine.Object.DestroyImmediate(go);
            Debug.Log("[NTG] Created " + path);
            return prefab;
        }

        private static GameObject BuildCanvasPrefab()
        {
            var go = new GameObject("UICanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); // landscape, mobile first
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            string path = PrefabsDir + "/UICanvas.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            UnityEngine.Object.DestroyImmediate(go);
            Debug.Log("[NTG] Created " + path);
            return prefab;
        }

        // ---------- Scenes ----------

        private static void BuildServerEntryScene(GameObject nmPrefab, GameObject playerPrefab)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            PrefabUtility.InstantiatePrefab(nmPrefab, scene);

            var serverRoot = new GameObject("ServerRoot");
            AddScriptIfExists(serverRoot, "ServerStartup");
            var spawner = AddScriptIfExists(serverRoot, "PlayerSpawner");
            Wire(spawner, ("playerPrefab", playerPrefab));

            Save(scene, ServerEntryPath);
        }

        private static void BuildClientMenuScene(GameObject nmPrefab, GameObject canvasPrefab)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            PrefabUtility.InstantiatePrefab(nmPrefab, scene);
            AddCameraAndLight();
            AddEventSystem();
            var canvas = InstantiateCanvas(canvasPrefab, scene);

            MakeText(canvas, "Title", "NTG MULTIPLAYER", new Vector2(0, 350), new Vector2(1000, 110), 64, TextAnchor.MiddleCenter);
            var findMatch = MakeButton(canvas, "FindMatchButton", "FIND MATCH", new Vector2(0, 100), new Vector2(700, 130));
            var status = MakeText(canvas, "StatusText", "", new Vector2(0, -150), new Vector2(1400, 120), 30, TextAnchor.MiddleCenter);

            var ui = AddScriptIfExists(canvas.gameObject, "SessionMenuUI");
            Wire(ui,
                ("findMatchButton", findMatch),
                ("statusText", status));

            Save(scene, ClientMenuPath);
        }

        private static void BuildLobbyScene(GameObject canvasPrefab)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            AddCameraAndLight();
            AddEventSystem();
            var canvas = InstantiateCanvas(canvasPrefab, scene);

            MakeText(canvas, "Title", "LOBBY", new Vector2(0, 460), new Vector2(900, 100), 56, TextAnchor.MiddleCenter);
            var roster = MakeText(canvas, "RosterText", "...", new Vector2(0, 150), new Vector2(1000, 420), 34, TextAnchor.UpperLeft);
            var status = MakeText(canvas, "StatusText", "Waiting for players", new Vector2(0, -160), new Vector2(1400, 80), 30, TextAnchor.MiddleCenter);
            var start = MakeButton(canvas, "StartButton", "START MATCH", new Vector2(-260, -350), new Vector2(500, 110));
            var ready = MakeButton(canvas, "ReadyButton", "READY", new Vector2(260, -350), new Vector2(500, 110));
            var leave = MakeButton(canvas, "LeaveButton", "LEAVE", new Vector2(0, -490), new Vector2(400, 80));

            var ui = AddScriptIfExists(canvas.gameObject, "LobbyUI");
            Wire(ui,
                ("rosterText", roster),
                ("statusText", status),
                ("startButton", start),
                ("readyButton", ready),
                ("leaveButton", leave));

            Save(scene, LobbyPath);
        }

        private static void BuildGameScene(GameObject canvasPrefab)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            AddCameraAndLight(camPos: new Vector3(0, 9, -11), camPitch: 55f);
            AddEventSystem();
            var canvas = InstantiateCanvas(canvasPrefab, scene);

            var info = MakeText(canvas, "InfoText", "In Game", new Vector2(0, 460), new Vector2(1000, 80), 40, TextAnchor.MiddleCenter);
            var disconnect = MakeButton(canvas, "DisconnectButton", "DISCONNECT", new Vector2(0, -430), new Vector2(700, 110));

            var hud = AddScriptIfExists(canvas.gameObject, "GameHUD");
            Wire(hud, ("infoText", info), ("disconnectButton", disconnect));

            // Placeholder arena.
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(2f, 1f, 2f);

            Save(scene, GamePath);
        }

        // ---------- UI helpers ----------

        private static RectTransform InstantiateCanvas(GameObject canvasPrefab, Scene scene)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(canvasPrefab, scene);
            return go.GetComponent<RectTransform>();
        }

        private static void AddEventSystem()
        {
            var go = new GameObject("EventSystem", typeof(EventSystem));
            // Project is Input System only -> StandaloneInputModule would not work.
            var module = go.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }

        private static Text MakeText(Transform parent, string name, string content, Vector2 pos, Vector2 size,
            int fontSize, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            SetRect(rt, pos, size);

            var text = go.GetComponent<Text>();
            text.font = _font;
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = Color.white;
            return text;
        }

        private static Button MakeButton(Transform parent, string name, string label, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            SetRect((RectTransform)go.transform, pos, size);
            go.GetComponent<Image>().color = new Color(0.25f, 0.5f, 0.9f);

            var labelGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(go.transform, false);
            var rt = (RectTransform)labelGo.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var text = labelGo.GetComponent<Text>();
            text.font = _font;
            text.text = label;
            text.fontSize = 40;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;

            return go.GetComponent<Button>();
        }

        private static InputField MakeInput(Transform parent, string name, string placeholder, Vector2 pos, Vector2 size,
            InputField.ContentType contentType = InputField.ContentType.Standard)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(InputField));
            go.transform.SetParent(parent, false);
            SetRect((RectTransform)go.transform, pos, size);
            go.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.17f);

            var input = go.GetComponent<InputField>();
            input.contentType = contentType;
            input.targetGraphic = go.GetComponent<Image>();

            var phGo = new GameObject("Placeholder", typeof(RectTransform), typeof(Text));
            phGo.transform.SetParent(go.transform, false);
            var phRt = (RectTransform)phGo.transform;
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.offsetMin = new Vector2(15, 5);
            phRt.offsetMax = new Vector2(-15, -5);
            var ph = phGo.GetComponent<Text>();
            ph.font = _font;
            ph.text = placeholder;
            ph.fontSize = 32;
            ph.fontStyle = FontStyle.Italic;
            ph.color = new Color(1, 1, 1, 0.4f);
            ph.alignment = TextAnchor.MiddleLeft;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            var textRt = (RectTransform)textGo.transform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(15, 5);
            textRt.offsetMax = new Vector2(-15, -5);
            var text = textGo.GetComponent<Text>();
            text.font = _font;
            text.fontSize = 32;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;

            input.placeholder = ph;
            input.textComponent = text;
            return input;
        }

        private static void SetRect(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        // ---------- Misc helpers ----------

        private static void AddCameraAndLight()
        {
            AddCameraAndLight(new Vector3(0, 0, -10), 0f);
        }

        private static void AddCameraAndLight(Vector3 camPos, float camPitch)
        {
            var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            camGo.transform.position = camPos;
            camGo.transform.rotation = Quaternion.Euler(camPitch, 0, 0);
            camGo.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
            camGo.GetComponent<Camera>().backgroundColor = new Color(0.1f, 0.1f, 0.12f);

            var lightGo = new GameObject("Directional Light", typeof(Light));
            lightGo.GetComponent<Light>().type = LightType.Directional;
            lightGo.transform.rotation = Quaternion.Euler(50, -30, 0);
        }

        private static Component AddScriptIfExists(GameObject go, string typeName)
        {
            Type t = Type.GetType("NTG." + typeName + ", Assembly-CSharp");
            if (t == null)
            {
                _missingScriptWarned = true;
                Debug.LogWarning($"[NTG] Script NTG.{typeName} not found — '{go.name}' created without it.");
                return null;
            }
            return go.AddComponent(t);
        }

        private static void Wire(Component component, params (string field, UnityEngine.Object value)[] pairs)
        {
            if (component == null) return;
            var so = new SerializedObject(component);
            foreach (var (field, value) in pairs)
            {
                var prop = so.FindProperty(field);
                if (prop != null) prop.objectReferenceValue = value;
                else Debug.LogWarning($"[NTG] Field '{field}' not found on {component.GetType().Name}");
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Save(Scene scene, string path)
        {
            EditorSceneManager.SaveScene(scene, path);
            Debug.Log("[NTG] Created " + path);
        }

        private static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name))
                AssetDatabase.CreateFolder(parent, name);
        }
    }
}
