using System.IO;
using Karman.VAB;
using Karman.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace Karman.EditorTools
{
    /// <summary>Generates the Vehicle Assembly Building scene: hall, mobile launch platform, umbilical tower, lighting, VAB tools.</summary>
    public static class VabSceneBuilder
    {
        const string Parts = "Assets/Karman/Art/Models/Parts";

        static Material Lit(string name, Color c, float smooth, float metal = 0, Color? emission = null)
        {
            var m = KarmanSetup.Mat(name, "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        static GameObject Box(string name, Vector3 pos, Vector3 size, Material m, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = m;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        public static void Build(string path)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var concrete = Lit("VAB_Concrete", new Color(0.36f, 0.37f, 0.38f), 0.42f);
            var wall = Lit("VAB_Wall", new Color(0.13f, 0.145f, 0.17f), 0.25f);
            var steel = Lit("VAB_Steel", new Color(0.55f, 0.56f, 0.58f), 0.55f, 0.85f);
            var darkSteel = Lit("VAB_DarkSteel", new Color(0.12f, 0.12f, 0.13f), 0.45f, 0.7f);
            var strip = Lit("VAB_LightStrip", Color.black, 0.5f, 0, new Color(4.5f, 4.8f, 5.2f));
            var accent = Lit("VAB_Accent", new Color(0.05f, 0.08f, 0.1f), 0.5f, 0, new Color(0.2f, 0.75f, 1.6f));
            var paint = Lit("VAB_SafetyPaint", new Color(0.85f, 0.55f, 0.08f), 0.4f);

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.22f, 0.25f, 0.3f);
            RenderSettings.ambientEquatorColor = new Color(0.12f, 0.13f, 0.15f);
            RenderSettings.ambientGroundColor = new Color(0.06f, 0.06f, 0.07f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Exponential; RenderSettings.fogDensity = 0.0025f;
            RenderSettings.fogColor = new Color(0.07f, 0.08f, 0.1f);

            var hall = new GameObject("Assembly Hall").transform;
            const float W = 150, H = 130, D = 150;
            Box("Floor", new Vector3(0, -0.5f, 0), new Vector3(W, 1, D), concrete, hall);
            Box("Ceiling", new Vector3(0, H, 0), new Vector3(W, 1, D), wall, hall);
            Box("Wall N", new Vector3(0, H / 2, D / 2), new Vector3(W, H, 1), wall, hall);
            Box("Wall S", new Vector3(0, H / 2, -D / 2), new Vector3(W, H, 1), wall, hall);
            Box("Wall E", new Vector3(W / 2, H / 2, 0), new Vector3(1, H, D), wall, hall);
            Box("Wall W", new Vector3(-W / 2, H / 2, 0), new Vector3(1, H, D), wall, hall);
            // Structural columns, light strips, catwalk levels.
            for (int i = -3; i <= 3; i++)
            {
                float x = i * 20f;
                foreach (float z in new[] { D / 2 - 2.5f, -D / 2 + 2.5f })
                {
                    Box("Column", new Vector3(x, H / 2, z), new Vector3(2.5f, H, 2.5f), darkSteel, hall);
                    Box("Strip", new Vector3(x + 10, H / 2, z + (z > 0 ? 1.8f : -1.8f) * -0.2f), new Vector3(0.35f, H * 0.85f, 0.2f), strip, hall);
                }
                Box("Truss", new Vector3(x, H - 5, 0), new Vector3(1.6f, 3f, D), darkSteel, hall);
            }
            for (int lvl = 1; lvl <= 4; lvl++)
            {
                float y = lvl * 24f;
                Box("Catwalk E", new Vector3(W / 2 - 4, y, 0), new Vector3(6, 0.4f, D - 8), steel, hall);
                Box("Catwalk W", new Vector3(-W / 2 + 4, y, 0), new Vector3(6, 0.4f, D - 8), steel, hall);
                Box("Rail E", new Vector3(W / 2 - 7, y + 1.1f, 0), new Vector3(0.1f, 0.1f, D - 8), paint, hall);
                Box("Rail W", new Vector3(-W / 2 + 7, y + 1.1f, 0), new Vector3(0.1f, 0.1f, D - 8), paint, hall);
            }
            Box("Floor accent ring", new Vector3(0, 0.02f, 0), new Vector3(46, 0.04f, 0.4f), accent, hall);
            Box("Floor accent ring 2", new Vector3(0, 0.02f, 0), new Vector3(0.4f, 0.04f, 46), accent, hall);

            // Mobile launch platform and umbilical tower.
            var mlp = new GameObject("Mobile Launch Platform").transform;
            Box("Deck", new Vector3(0, 3, 0), new Vector3(32, 6, 26), darkSteel, mlp);
            Box("Deck plate", new Vector3(0, 6.05f, 0), new Vector3(30, 0.1f, 24), steel, mlp);
            Box("Exhaust opening", new Vector3(0, 6.08f, 0), new Vector3(9, 0.1f, 9), Lit("VAB_Hole", Color.black, 0.1f), mlp);
            for (int k = 0; k < 4; k++)
            {
                float a = k * Mathf.PI / 2 + Mathf.PI / 4;
                Box("Hold-down", new Vector3(Mathf.Cos(a) * 3.6f, 6.6f, Mathf.Sin(a) * 3.6f), new Vector3(0.8f, 1.1f, 0.8f), paint, mlp);
            }
            var tower = new GameObject("Umbilical Tower").transform;
            tower.SetParent(mlp, false); tower.localPosition = new Vector3(-11, 6, 0);
            const float TH = 92, TW = 6;
            foreach (var c in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
                Box("Leg", new Vector3(c.x * TW / 2, TH / 2, c.y * TW / 2), new Vector3(0.6f, TH, 0.6f), paint, tower);
            for (float y = 4; y < TH; y += 4)
            {
                Box("Brace X", new Vector3(0, y, TW / 2), new Vector3(TW, 0.35f, 0.35f), paint, tower);
                Box("Brace X", new Vector3(0, y, -TW / 2), new Vector3(TW, 0.35f, 0.35f), paint, tower);
                Box("Brace Z", new Vector3(TW / 2, y, 0), new Vector3(0.35f, 0.35f, TW), paint, tower);
                Box("Brace Z", new Vector3(-TW / 2, y, 0), new Vector3(0.35f, 0.35f, TW), paint, tower);
                if (((int)y) % 16 == 0) Box("Arm", new Vector3(TW / 2 + 2.2f, y, 0), new Vector3(4.4f, 0.6f, 1.6f), steel, tower);
            }

            // Lighting: ceiling floods aimed at the vehicle plus a cool fill.
            var lights = new GameObject("Lights").transform;
            for (int k = 0; k < 6; k++)
            {
                float a = k * Mathf.PI * 2 / 6 + 0.3f;
                var go = new GameObject("Flood " + k);
                go.transform.SetParent(lights, false);
                go.transform.position = new Vector3(Mathf.Cos(a) * 55, H - 12, Mathf.Sin(a) * 55);
                go.transform.LookAt(new Vector3(0, 30, 0));
                var l = go.AddComponent<Light>();
                l.type = LightType.Spot; l.spotAngle = 48; l.innerSpotAngle = 25; l.range = 220; l.intensity = 9000;
                l.color = k % 2 == 0 ? new Color(1f, 0.96f, 0.9f) : new Color(0.9f, 0.95f, 1f);
                l.shadows = k < 2 ? LightShadows.Soft : LightShadows.None;
            }
            var fill = new GameObject("Fill").AddComponent<Light>();
            fill.type = LightType.Directional; fill.intensity = 0.35f; fill.color = new Color(0.7f, 0.8f, 1f);
            fill.transform.rotation = Quaternion.Euler(50, -30, 0);

            // Craft root and VAB tools.
            var craftRoot = new GameObject("Craft").transform;
            var camGo = new GameObject("VAB Camera"); camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.05f, 0.055f, 0.065f);
            cam.nearClipPlane = 0.3f; cam.farClipPlane = 1500; cam.fieldOfView = 45; cam.allowHDR = true;
            camGo.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
            camGo.AddComponent<AudioListener>();

            var vabGo = new GameObject("VAB");
            var vab = vabGo.AddComponent<VabController>();
            vab.cam = cam; vab.craftRoot = craftRoot; vab.platformTop = new Vector3(0, 6.1f, 0);
            vab.catalog = Catalog();
            var ghostOk = KarmanSetup.Mat("VAB_GhostOk", "Karman/Line"); ghostOk.SetColor("_Color", new Color(0.3f, 1f, 0.55f, 0.45f));
            var ghostBad = KarmanSetup.Mat("VAB_GhostBad", "Karman/Line"); ghostBad.SetColor("_Color", new Color(1f, 0.3f, 0.25f, 0.4f));
            var sel = KarmanSetup.Mat("VAB_Selection", "Karman/Line"); sel.SetColor("_Color", new Color(0.36f, 0.78f, 1f, 0.28f));
            vab.ghostOk = ghostOk; vab.ghostBad = ghostBad; vab.selectionMaterial = sel;
            vab.comMarker = Marker("Centre of mass", new Color(1f, 0.85f, 0.2f, 0.95f), craftRoot);
            vab.cotMarker = Marker("Centre of thrust", new Color(1f, 0.3f, 0.9f, 0.95f), craftRoot);

            var vcam = camGo.AddComponent<VabCamera>();
            vcam.vab = vab;

            var volGo = new GameObject("Global Volume");
            var vol = volGo.AddComponent<Volume>(); vol.isGlobal = true;
            vol.sharedProfile = KarmanSetupProfiles.VabProfile();

            var hud = new GameObject("VAB HUD");
            hud.AddComponent<UIDocument>().panelSettings = KarmanSetup.BuildPanelSettings();
            hud.AddComponent<VabHud>().vab = vab;
            hud.AddComponent<Karman.Diagnostics.VabCapture>().vab = vab;

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            EditorSceneManager.SaveScene(scene, path);
        }

        static PartModelCatalog catalogCache;

        /// <summary>Shared part-model catalogue asset (VAB and flight).</summary>
        public static PartModelCatalog Catalog()
        {
            if (catalogCache != null) return catalogCache;
            const string path = "Assets/Karman/Settings/PartModelCatalog.asset";
            var cat = AssetDatabase.LoadAssetAtPath<PartModelCatalog>(path);
            if (cat == null) { cat = ScriptableObject.CreateInstance<PartModelCatalog>(); AssetDatabase.CreateAsset(cat, path); }
            string[] ids = { "gemini", "tank_8", "tank_10", "tank_16", "tank_32", "engine_booster", "engine_cluster9", "engine_vacuum", "srb", "decoupler", "radial_decoupler", "adapter", "interstage", "nose" };
            var prefabs = new GameObject[ids.Length];
            for (int i = 0; i < ids.Length; i++)
                prefabs[i] = ids[i] == "gemini" ? GeminiPrefab() : KarmanSetup.Load<GameObject>($"{Parts}/{ids[i]}.fbx");
            cat.modelIds = ids; cat.prefabs = prefabs;
            EditorUtility.SetDirty(cat);
            AssetDatabase.SaveAssets();
            return catalogCache = cat;
        }

        static Transform Marker(string name, Color c, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name; Object.DestroyImmediate(go.GetComponent<Collider>());
            var m = KarmanSetup.Mat("VAB_" + name.Replace(" ", ""), "Karman/Marker"); m.SetColor("_Color", c);
            go.GetComponent<Renderer>().sharedMaterial = m;
            go.transform.SetParent(parent, false); go.transform.localScale = Vector3.one * 1.3f;
            return go.transform;
        }

        static GameObject GeminiPrefab()
        {
            string path = "Assets/Karman/Art/Prefabs/Part_gemini.prefab";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var root = new GameObject("Gemini");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(KarmanSetup.Load<GameObject>("Assets/Karman/Art/Models/Spacecraft/Gemini.fbx"));
            model.transform.SetParent(root.transform, false);
            var b = KarmanSetup.RendererBounds(model);
            model.transform.localPosition -= b.center;
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }
    }

    static class KarmanSetupProfiles
    {
        public static VolumeProfile VabProfile()
        {
            string path = "Assets/Karman/Settings/VabPostProcess.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            var bloom = profile.Add<Bloom>(true); bloom.threshold.Override(1.0f); bloom.intensity.Override(0.6f); bloom.scatter.Override(0.65f);
            var tone = profile.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.ACES);
            var color = profile.Add<ColorAdjustments>(true); color.postExposure.Override(0.3f); color.contrast.Override(10f);
            var vig = profile.Add<Vignette>(true); vig.intensity.Override(0.25f);
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            return profile;
        }
    }
}
