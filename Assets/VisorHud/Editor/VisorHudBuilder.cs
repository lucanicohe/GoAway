#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace VisorHudTools
{
    /// <summary>Makes the HUD sprites import correctly (Sprite, no mipmaps, no compression blur).</summary>
    public class VisorHudAssetProcessor : AssetPostprocessor
    {
        public const string TexFolder = "Assets/VisorHud/Textures/";

        /// <summary>Applies the HUD sprite settings. Returns true if anything had to change.</summary>
        public static bool ApplySettings(TextureImporter ti)
        {
            bool changed = false;
            if (ti.textureType != TextureImporterType.Sprite) { ti.textureType = TextureImporterType.Sprite; changed = true; }
            if (ti.spriteImportMode != SpriteImportMode.Single) { ti.spriteImportMode = SpriteImportMode.Single; changed = true; }
            if (ti.alphaSource != TextureImporterAlphaSource.FromInput) { ti.alphaSource = TextureImporterAlphaSource.FromInput; changed = true; }
            if (!ti.alphaIsTransparency) { ti.alphaIsTransparency = true; changed = true; }
            if (ti.mipmapEnabled) { ti.mipmapEnabled = false; changed = true; }
            if (ti.filterMode != FilterMode.Bilinear) { ti.filterMode = FilterMode.Bilinear; changed = true; }
            if (ti.wrapMode != TextureWrapMode.Clamp) { ti.wrapMode = TextureWrapMode.Clamp; changed = true; }
            if (ti.textureCompression != TextureImporterCompression.Uncompressed) { ti.textureCompression = TextureImporterCompression.Uncompressed; changed = true; }
            if (ti.maxTextureSize != 2048) { ti.maxTextureSize = 2048; changed = true; }
            if (Mathf.Abs(ti.spritePixelsPerUnit - 100f) > 0.01f) { ti.spritePixelsPerUnit = 100f; changed = true; }
            return changed;
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(TexFolder)) return;
            ApplySettings((TextureImporter)assetImporter);
        }
    }

    /// <summary>
    /// Builds the Visor HUD canvas (prefab) and a test scene. No dependency on any other package.
    /// Menu: Tools > Visor HUD
    /// </summary>
    public static class VisorHudBuilder
    {
        const string Root = "Assets/VisorHud";
        const string Tex = Root + "/Textures/";
        const string PrefabPath = Root + "/Prefabs/VisorHUD.prefab";
        const string TestScene = Root + "/Scenes/VisorHud_Test.unity";
        const string PrefKey = "VisorHud_Offered";

        // layout (canvas units, reference 1920x1080)
        const float TopVisorHeight = 190f;                       // top visor band
        static readonly Vector2 BarPos = new Vector2(365f, 58f); // bar sits flush against the panel's inner edge
        static readonly Vector2 BarSize = new Vector2(190f, 95f);
        const float LabelOffsetX = 12f;                          // follows the slant of the bar

        // compass letters sit just under the dashed line of the top visor (the dashes run from x=338 to x=1708 of the 2048 px sprite)
        const float CompassMinX = 338f / 2048f;
        const float CompassMaxX = 1708f / 2048f;
        const float CompassTop = 116f;                           // canvas units below the top edge of the screen
        const float CompassHeight = 32f;

        // ------------------------------------------------------------ helpers

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        /// <summary>
        /// Makes sure every HUD PNG is imported as a Sprite. Needed when the textures were imported before the
        /// import settings existed (e.g. while the scripts were still compiling) - they would otherwise be plain
        /// textures, LoadAssetAtPath&lt;Sprite&gt; returns null and the HUD shows white blocks.
        /// </summary>
        public static int FixTextureImports()
        {
            int fixedCount = 0;
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Textures" });
            foreach (string g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (!path.EndsWith(".png")) continue;
                if (EnsureSpriteImport(path)) fixedCount++;
            }
            return fixedCount;
        }

        static bool EnsureSpriteImport(string path)
        {
            TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) return false;
            }
            if (!VisorHudAssetProcessor.ApplySettings(ti)) return false;
            ti.SaveAndReimport();
            return true;
        }

        static Sprite LoadSprite(string name)
        {
            string p = Tex + name + ".png";
            if (!System.IO.File.Exists(p))
            {
                Debug.LogWarning("Visor HUD: texture file is missing: " + p);
                return null;
            }
            EnsureSpriteImport(p);
            Sprite s = AssetDatabase.LoadAssetAtPath<Sprite>(p);
            if (s == null)
            {
                AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                s = AssetDatabase.LoadAssetAtPath<Sprite>(p);
            }
            if (s == null) Debug.LogWarning("Visor HUD: sprite not found: " + p);
            return s;
        }

        static RectTransform NewUI(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        static Image AddImage(RectTransform rt, Sprite sprite)
        {
            Image img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = Color.white;
            img.raycastTarget = false;        // the HUD never blocks the mouse
            return img;
        }

        static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        static void StretchToParent(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static Font HudFont()
        {
#if UNITY_2022_2_OR_NEWER
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
            return Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
        }

        static Text MakeLabel(Transform bar, string text, float xOffset)
        {
            RectTransform rt = NewUI("Label", bar);
            // hangs just under the bar, centred on it
            Place(rt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(xOffset, -4f), new Vector2(240f, 32f));
            Text t = rt.gameObject.AddComponent<Text>();
            t.font = HudFont();
            t.text = text;
            t.fontSize = 22;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.UpperCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.color = new Color(0.84f, 0.93f, 0.96f, 1f);
            t.raycastTarget = false;
            Shadow sh = rt.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.6f);
            sh.effectDistance = new Vector2(1.5f, -1.5f);
            return t;
        }

        static Image MakeBar(Transform parent, string name, Vector2 anchor, Vector2 pos, Sprite sprite, string label, float labelOffsetX)
        {
            RectTransform bar = NewUI(name, parent);
            Place(bar, anchor, anchor, anchor, pos, BarSize);

            RectTransform back = NewUI("Back", bar);
            StretchToParent(back);
            AddImage(back, sprite);

            RectTransform fill = NewUI("Fill", bar);
            StretchToParent(fill);
            Image f = AddImage(fill, sprite);
            f.type = Image.Type.Filled;
            f.fillMethod = Image.FillMethod.Vertical;                 // like a glass: full at the top, drains downwards
            f.fillOrigin = (int)Image.OriginVertical.Bottom;
            f.fillAmount = 1f;

            MakeLabel(bar, label, labelOffsetX);
            return f;
        }

        /// <summary>A sprite that fills its parent exactly (used for the layers of the visor).</summary>
        static Image MakeLayer(Transform parent, string name, string sprite)
        {
            RectTransform rt = NewUI(name, parent);
            StretchToParent(rt);
            return AddImage(rt, LoadSprite(sprite));
        }

        static void MakeCompassLetters(Transform parent)
        {
            RectTransform area = NewUI("Compass Letters", parent);
            Place(area, new Vector2(CompassMinX, 1f), new Vector2(CompassMaxX, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -CompassTop), new Vector2(0f, CompassHeight));
            string[] letters = { "N", "E", "S", "W" };
            for (int i = 0; i < letters.Length; i++)
            {
                RectTransform rt = NewUI("Letter " + letters[i], area);
                Place(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(64f, CompassHeight));
                Text t = rt.gameObject.AddComponent<Text>();
                t.font = HudFont();
                t.text = letters[i];
                t.fontSize = 24;
                t.fontStyle = FontStyle.Bold;
                t.alignment = TextAnchor.MiddleCenter;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.verticalOverflow = VerticalWrapMode.Overflow;
                t.color = Color.white;
                t.raycastTarget = false;
                Shadow sh = rt.gameObject.AddComponent<Shadow>();
                sh.effectColor = new Color(0f, 0f, 0f, 0.6f);
                sh.effectDistance = new Vector2(1.5f, -1.5f);
            }
        }

        // ------------------------------------------------------------ the HUD

        static GameObject BuildHud()
        {
            GameObject root = new GameObject("Visor HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;      // independent of any camera (works with the Cinemachine rig)
            canvas.sortingOrder = 50;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1.0f;

            Transform t = root.transform;

            // top visor (stretches across the screen). Layers, bottom to top:
            //   Top Body (opaque metal with the vents) - Top Edge (metal edge line) - Compass Line (dashes) - Compass Ticks - Compass Letters
            RectTransform top = NewUI("Top Visor", t);
            Place(top, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, TopVisorHeight));
            MakeLayer(top, "Top Body", "Hud_Top");
            MakeLayer(top, "Top Edge", "Hud_Top_Line");
            MakeLayer(top, "Compass Line", "Hud_Top_Compass");
            MakeLayer(top, "Compass Ticks", "Hud_Top_Ticks");
            MakeCompassLetters(top);

            // left / right visor arcs
            RectTransform la = NewUI("Left Arc", t);
            Place(la, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 40f), new Vector2(380f, 760f));
            AddImage(la, LoadSprite("Hud_Arc_L"));
            RectTransform ra = NewUI("Right Arc", t);
            Place(ra, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 40f), new Vector2(380f, 760f));
            AddImage(ra, LoadSprite("Hud_Arc_R"));

            // bottom corner panels
            RectTransform lp = NewUI("Left Panel", t);
            Place(lp, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(493f, 157f));
            MakeLayer(lp, "Left Panel Body", "Hud_Panel_L");
            MakeLayer(lp, "Left Panel Edge", "Hud_Panel_Line_L");
            RectTransform rp = NewUI("Right Panel", t);
            Place(rp, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(493f, 157f));
            MakeLayer(rp, "Right Panel Body", "Hud_Panel_R");
            MakeLayer(rp, "Right Panel Edge", "Hud_Panel_Line_R");

            // the two striped bars: left = health (green), right = ammo (red)
            Image hFill = MakeBar(t, "Health Bar", new Vector2(0f, 0f), new Vector2(BarPos.x, BarPos.y), LoadSprite("Hud_Bar_L"), "HEALTH", LabelOffsetX);
            Image aFill = MakeBar(t, "Ammo Bar", new Vector2(1f, 0f), new Vector2(-BarPos.x, BarPos.y), LoadSprite("Hud_Bar_R"), "AMMO", -LabelOffsetX);

            // crosshair
            RectTransform ch = NewUI("Crosshair", t);
            Place(ch, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(96f, 96f));
            AddImage(ch, LoadSprite("Hud_Crosshair"));

            VisorHud hud = root.AddComponent<VisorHud>();   // wires everything by name and applies the colours
            hud.health = 1f;
            hud.ammo = 1f;
            return root;
        }

        static GameObject CreatePrefabAsset()
        {
            EnsureFolder(Root + "/Prefabs");
            AssetDatabase.Refresh();
            FixTextureImports();
            GameObject temp = BuildHud();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, PrefabPath);
            Object.DestroyImmediate(temp);
            AssetDatabase.SaveAssets();
            return prefab;
        }

        // ------------------------------------------------------------ menu items

        [MenuItem("Tools/Visor HUD/Fix Texture Import (white blocks)")]
        static void MenuFixTextures()
        {
            int n = FixTextureImports();
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("Visor HUD",
                n > 0 ? "Re-imported " + n + " texture(s) as Sprites.\n\nNow run Tools > Visor HUD > Build Test Scene (or Create HUD Prefab) again so the HUD picks them up."
                      : "All HUD textures are already imported as Sprites.\n\nIf the HUD still looks white, run Build Test Scene again to rebuild the prefab.", "OK");
        }

        [MenuItem("Tools/Visor HUD/Create HUD Prefab")]
        static void MenuCreatePrefab()
        {
            GameObject prefab = CreatePrefabAsset();
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
            EditorUtility.DisplayDialog("Visor HUD", "Prefab created:\n" + PrefabPath + "\n\nDrag it into any scene (it is a Canvas, so it can go anywhere in the Hierarchy).", "OK");
        }

        [MenuItem("Tools/Visor HUD/Add HUD To Open Scene")]
        static void MenuAddToScene()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) prefab = CreatePrefabAsset();
            GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(inst, "Add Visor HUD");
            Selection.activeGameObject = inst;
            EditorSceneManager.MarkSceneDirty(inst.scene);
        }

        [MenuItem("Tools/Visor HUD/Build Test Scene")]
        public static void BuildTestScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureFolder(Root + "/Scenes");
            GameObject prefab = CreatePrefabAsset();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(6f, 1f, 6f);
            for (int i = 0; i < 8; i++)
            {
                GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = "Box " + i;
                float ang = i * Mathf.PI * 2f / 8f;
                float h = 0.5f + (i % 3) * 0.6f;
                box.transform.position = new Vector3(Mathf.Cos(ang) * 8f, h * 0.5f, Mathf.Sin(ang) * 8f);
                box.transform.localScale = new Vector3(2f, h, 2f);
            }
            GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pillar.name = "Pillar";
            pillar.transform.position = new Vector3(0f, 2f, 12f);
            pillar.transform.localScale = new Vector3(1.5f, 2f, 1.5f);

            // use the first-person player rig from the other package, if it is in this project
            bool hasPlayer = false;
            string[] guids = AssetDatabase.FindAssets("FirstPersonRig t:Prefab");
            if (guids.Length > 0)
            {
                GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guids[0]));
                if (playerPrefab != null)
                {
                    UnityEngine.Camera defaultCam = UnityEngine.Camera.main;
                    if (defaultCam != null) Object.DestroyImmediate(defaultCam.gameObject);   // the prefab brings its own Main Camera
                    GameObject player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
                    player.transform.position = new Vector3(0f, 0.1f, 0f);
                    hasPlayer = true;
                }
            }
            if (!hasPlayer)
            {
                UnityEngine.Camera cam = UnityEngine.Camera.main;
                if (cam != null)
                {
                    cam.transform.position = new Vector3(0f, 1.7f, -6f);
                    cam.transform.LookAt(new Vector3(0f, 1.5f, 10f));
                }
            }

            GameObject hud = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            hud.name = "Visor HUD";

            EditorSceneManager.SaveScene(scene, TestScene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = hud;
            EditorGUIUtility.PingObject(prefab);

            EditorUtility.DisplayDialog("Visor HUD test scene ready",
                "Scene:  " + TestScene + "\nPrefab: " + PrefabPath + "\n\n" +
                "Press PLAY. Select 'Visor HUD' in the Hierarchy and drag the Health and Ammo sliders in the Inspector.\n" +
                (hasPlayer ? "(Your first-person player rig was found and added.)" : "(No first-person rig found, so a plain camera is used. Import the first-person package to walk around.)"), "OK");
        }

        // ------------------------------------------------------------ one-time welcome / update notice

        const int PackageVersion = 3;   // raise when the prefab layout changes, so an update offers to rebuild it once
        const string VersionKey = "VisorHud_Version";

        [InitializeOnLoadMethod]
        static void Welcome()
        {
            EditorApplication.update += WaitThenWelcome;
        }

        static void WaitThenWelcome()
        {
            // never touch assets / scenes while Unity is still compiling or importing
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            EditorApplication.update -= WaitThenWelcome;

            string key = PrefKey + Application.dataPath.GetHashCode();
            string versionKey = VersionKey + Application.dataPath.GetHashCode();
            bool welcomed = EditorPrefs.GetBool(key, false);
            int oldVersion = EditorPrefs.GetInt(versionKey, 0);
            EditorPrefs.SetInt(versionKey, PackageVersion);

            if (!welcomed)
            {
                EditorPrefs.SetBool(key, true);
                int c = EditorUtility.DisplayDialogComplex("Visor HUD",
                    "Visor HUD imported.\n\nWhat would you like to do?\n(Everything is also under the menu Tools > Visor HUD)",
                    "Build test scene + prefab", "Later", "Add HUD to open scene");
                if (c == 0) BuildTestScene();
                else if (c == 2) MenuAddToScene();
            }
            else if (oldVersion < PackageVersion)
            {
                bool rebuild = EditorUtility.DisplayDialog("Visor HUD updated",
                    "The HUD now has a metal visor, a compass (N E S W) and colour + opacity settings for every element.\n\n" +
                    "The prefab has to be rebuilt once to get them. Rebuild Assets/VisorHud/Prefabs/VisorHUD.prefab now?\n" +
                    "(You can also do it later: Tools > Visor HUD > Create HUD Prefab.)",
                    "Rebuild prefab", "Later");
                if (rebuild) CreatePrefabAsset();
            }
        }
    }
}
#endif
