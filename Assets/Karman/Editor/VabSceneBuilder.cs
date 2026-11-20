using System.IO;
using Karman.VAB;
using Karman.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UIElements;

namespace Karman.EditorTools
{
    /// <summary>Generates the Vehicle Assembly Building scene: hall, mobile launch platform, umbilical tower, lighting, VAB tools.</summary>
    public static class VabSceneBuilder
    {
        const string Parts = "Assets/Karman/Art/Models/Parts";

        static Material Lit(string name, Color c, float smooth, float metal = 0, Color? emission = null)
        {
            var m = KarmanSetup.Mat(name, "HDRP/Lit");
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            if (emission.HasValue)
            {
                m.SetColor("_EmissiveColor", emission.Value * 1500f);   // nits
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            HDMaterial.ValidateMaterial(m);
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
            PbrMaterials.EnsureSsao();
            var floor = PbrMaterials.Triplanar("VAB_Floor", "hangar_concrete_floor", new Color(0.62f, 0.63f, 0.64f), 7f, 0, 0.55f, 0.6f, 0.45f, 0.5f);
            floor.SetFloat("_Panel", 4f);
            var cladding = PbrMaterials.Triplanar("VAB_Cladding", "painted_metal_shutter", new Color(2.7f, 2.75f, 2.85f), 3.2f, 0.1f, 0.7f, 1f, 1f, 0.4f);
            var cladDark = PbrMaterials.Triplanar("VAB_CladdingDark", "painted_metal_shutter", new Color(1.1f, 1.12f, 1.18f), 3.2f, 0.1f, 0.8f, 1f, 1f, 0.5f);
            var steel = PbrMaterials.Triplanar("VAB_Steel", "metal_plate_02", new Color(0.55f, 0.57f, 0.6f), 2f, 0.9f, 0.6f, 0.6f, 0.6f, 0.8f);
            var darkSteel = PbrMaterials.Triplanar("VAB_DarkSteel", "metal_plate_02", new Color(0.36f, 0.37f, 0.4f), 2f, 0.25f, 0.7f, 0.6f, 0.5f, 0.8f);
            var paint = PbrMaterials.Triplanar("VAB_SafetyPaint", "metal_plate_02", new Color(0.85f, 0.62f, 0.12f), 1.5f, 0.2f, 0.7f, 0.4f, 0.3f, 1f);
            var panelLight = Lit("VAB_LightPanel", Color.black, 0.5f, 0, new Color(4.5f, 4.6f, 4.8f));
            var floorLine = Lit("VAB_FloorLine", new Color(0.75f, 0.55f, 0.1f), 0.35f);

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.65f, 0.7f);
            RenderSettings.ambientEquatorColor = new Color(0.48f, 0.5f, 0.53f);
            RenderSettings.ambientGroundColor = new Color(0.3f, 0.3f, 0.31f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Exponential; RenderSettings.fogDensity = 0.0007f;
            RenderSettings.fogColor = new Color(0.62f, 0.65f, 0.69f);

            var hall = new GameObject("Assembly Hall").transform;
            const float W = 240, H = 210, D = 240;
            Box("Floor", new Vector3(0, -0.5f, 0), new Vector3(W, 1, D), floor, hall);
            Box("Ceiling", new Vector3(0, H, 0), new Vector3(W, 1, D), cladDark, hall);
            Box("Wall N", new Vector3(0, H / 2, D / 2), new Vector3(W, H, 1), cladding, hall);
            Box("Wall E", new Vector3(W / 2, H / 2, 0), new Vector3(1, H, D), cladding, hall);
            Box("Wall W", new Vector3(-W / 2, H / 2, 0), new Vector3(1, H, D), cladding, hall);
            // South wall: high-bay door (darker ribbed leaf in a steel frame).
            Box("Wall S left", new Vector3(-W / 2 + 40, H / 2, -D / 2), new Vector3(80, H, 1), cladding, hall);
            Box("Wall S right", new Vector3(W / 2 - 40, H / 2, -D / 2), new Vector3(80, H, 1), cladding, hall);
            Box("Wall S lintel", new Vector3(0, H - 15, -D / 2), new Vector3(80, 30, 1), cladding, hall);
            Box("High-bay door", new Vector3(0, (H - 30) / 2, -D / 2 - 0.5f), new Vector3(80, H - 30, 1), cladDark, hall);
            Box("Door frame L", new Vector3(-40.5f, H / 2, -D / 2 + 0.8f), new Vector3(1.6f, H, 1.6f), steel, hall);
            Box("Door frame R", new Vector3(40.5f, H / 2, -D / 2 + 0.8f), new Vector3(1.6f, H, 1.6f), steel, hall);
            // Structure: wall columns, roof trusses and a grid of soft ceiling light panels.
            for (int i = -5; i <= 5; i++)
            {
                float x = i * 22f;
                foreach (float z in new[] { D / 2 - 1.5f, -D / 2 + 1.5f })
                    if (Mathf.Abs(x) > 42 || z > 0) Box("Column", new Vector3(x, H / 2, z), new Vector3(1.8f, H, 1.8f), darkSteel, hall);
                Box("Roof truss", new Vector3(x, H - 4, 0), new Vector3(1.2f, 6f, D), darkSteel, hall);
                for (int k = -4; k <= 4; k++) Box("Light panel", new Vector3(x + 11, H - 7.5f, k * 24f), new Vector3(8f, 0.3f, 3f), panelLight, hall);
            }
            for (int lvl = 1; lvl <= 4; lvl++)
            {
                float y = lvl * 32f;
                foreach (float sgn in new[] { -1f, 1f })
                {
                    Box("Catwalk", new Vector3(sgn * (W / 2 - 4), y, 0), new Vector3(7, 0.35f, D - 10), steel, hall);
                    Box("Rail", new Vector3(sgn * (W / 2 - 7.4f), y + 1.1f, 0), new Vector3(0.08f, 0.08f, D - 10), steel, hall);
                    Box("Kick plate", new Vector3(sgn * (W / 2 - 7.4f), y + 0.25f, 0), new Vector3(0.05f, 0.3f, D - 10), darkSteel, hall);
                }
            }
            // Floor markings: subtle painted assembly grid around the platform.
            for (int k = -2; k <= 2; k++)
            {
                Box("Line X", new Vector3(0, 0.01f, k * 30f), new Vector3(150, 0.02f, 0.25f), floorLine, hall);
                Box("Line Z", new Vector3(k * 30f, 0.01f, 0), new Vector3(0.25f, 0.02f, 150), floorLine, hall);
            }

            // Mobile launch platform and umbilical tower.
            var mlp = new GameObject("Mobile Launch Platform").transform;
            Box("Deck", new Vector3(0, 3, 0), new Vector3(40, 6, 34), darkSteel, mlp);
            Box("Deck plate", new Vector3(0, 6.05f, 0), new Vector3(38, 0.1f, 32), steel, mlp);
            Box("Exhaust opening", new Vector3(0, 6.08f, 0), new Vector3(10, 0.1f, 10), Lit("VAB_Hole", new Color(0.01f, 0.01f, 0.01f), 0.1f), mlp);
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4 + Mathf.PI / 8;
                Box("Hold-down", new Vector3(Mathf.Cos(a) * 4.6f, 6.6f, Mathf.Sin(a) * 4.6f), new Vector3(0.7f, 1.1f, 0.7f), paint, mlp);
            }
            var tower = new GameObject("Umbilical Tower").transform;
            tower.SetParent(mlp, false); tower.localPosition = new Vector3(-16, 6, 0);
            LaunchSiteBuilder.Truss(tower, 7f, 118f, 6f, steel);
            for (float y = 18; y < 118; y += 18) Box("Service arm", new Vector3(7f / 2 + 2.6f, y, 0), new Vector3(5.2f, 0.8f, 1.8f), steel, tower);

            // Lighting: eight ceiling floods aimed at the vehicle plus a cool fill.
            var lights = new GameObject("Lights").transform;
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI * 2 / 8 + 0.2f;
                var go = new GameObject("Flood " + k);
                go.transform.SetParent(lights, false);
                go.transform.position = new Vector3(Mathf.Cos(a) * 80, H - 14, Mathf.Sin(a) * 80);
                go.transform.LookAt(new Vector3(0, 45, 0));
                var l = go.AddComponent<Light>();
                l.type = LightType.Spot; l.spotAngle = 55; l.innerSpotAngle = 30; l.range = 400;
                KarmanSetup.HdLight(l, 120000f, LightUnit.Lumen);
                l.color = k % 2 == 0 ? new Color(1f, 0.97f, 0.92f) : new Color(0.92f, 0.96f, 1f);
                l.shadows = k < 2 ? LightShadows.Soft : LightShadows.None;
            }
            var fill = new GameObject("Fill").AddComponent<Light>();
            fill.type = LightType.Directional; fill.color = new Color(0.75f, 0.82f, 1f);
            KarmanSetup.HdLight(fill, 900f, LightUnit.Lux);
            fill.transform.rotation = Quaternion.Euler(55, -30, 0);
            fill.shadows = LightShadows.Soft;

            // Reflections of the bright hall on paint and metal (rendered once on load).
            var probe = new GameObject("Hall Reflection Probe").AddComponent<ReflectionProbe>();
            var probeHd = probe.gameObject.GetComponent<HDAdditionalReflectionData>() ?? probe.gameObject.AddComponent<HDAdditionalReflectionData>();
            probeHd.mode = ProbeSettings.Mode.Realtime; probeHd.realtimeMode = ProbeSettings.RealtimeMode.OnEnable;
            probe.transform.position = new Vector3(0, 50, 30);
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
            probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.OnAwake;
            probe.resolution = 256; probe.hdr = true; probe.size = new Vector3(W, H * 2, D); probe.boxProjection = false;
            probe.cullingMask = ~0;

            // Craft root and VAB tools.
            var craftRoot = new GameObject("Craft").transform;
            var camGo = new GameObject("VAB Camera"); camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.62f, 0.65f, 0.69f);
            cam.nearClipPlane = 0.3f; cam.farClipPlane = 1500; cam.fieldOfView = 45; cam.allowHDR = true;
            KarmanSetup.HdCamera(cam);
            camGo.AddComponent<AudioListener>();

            var vabGo = new GameObject("VAB");
            var vab = vabGo.AddComponent<VabController>();
            vab.cam = cam; vab.craftRoot = craftRoot; vab.platformTop = new Vector3(0, 6.1f, 0);
            vab.catalog = Catalog();
            var ghostOk = KarmanSetup.Mat("VAB_GhostOk", "Karman/Line"); ghostOk.SetColor("_Color", new Color(0.3f, 1f, 0.55f, 0.45f));
            var ghostBad = KarmanSetup.Mat("VAB_GhostBad", "Karman/Line"); ghostBad.SetColor("_Color", new Color(1f, 0.3f, 0.25f, 0.4f));
            var sel = KarmanSetup.Mat("VAB_Selection", "Karman/Line"); sel.SetColor("_Color", new Color(0.36f, 0.78f, 1f, 0.28f));
            vab.ghostOk = ghostOk; vab.ghostBad = ghostBad; vab.selectionMaterial = sel;
            vab.interstageMaterial = PbrMaterials.Triplanar("Interstage", "metal_plate_02", new Color(0.055f, 0.055f, 0.06f), 0.6f, 0.0f, 0.5f, 0.35f, 0.3f, 1, 1);
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
            string[] ids = { "gemini", "lm_descent", "lm_ascent", "tank_8", "tank_10", "tank_16", "tank_32", "tank_s15", "tank_s30", "engine_booster", "engine_cluster9", "engine_cluster4", "engine_vacuum", "srb", "decoupler", "radial_decoupler", "adapter", "interstage", "nose" };
            var prefabs = new GameObject[ids.Length];
            for (int i = 0; i < ids.Length; i++)
                prefabs[i] = ids[i] == "gemini" ? GeminiPrefab() : ids[i].StartsWith("lm_") ? KarmanSetup.Load<GameObject>($"Assets/Karman/Art/Models/Lander/{ids[i]}.fbx") : KarmanSetup.Load<GameObject>($"{Parts}/{ids[i]}.fbx");
            cat.modelIds = ids; cat.prefabs = prefabs;
            var (names, mats) = PbrMaterials.VehicleSet();
            cat.materialNames = names; cat.materials = mats;
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
            // Indoor hall: soft grey gradient sky for ambient fill, auto exposure tuned for interiors.
            var env = profile.Add<VisualEnvironment>(true); env.skyType.Override((int)SkyType.Gradient); env.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
            var grad = profile.Add<GradientSky>(true);
            grad.top.Override(new Color(0.75f, 0.78f, 0.82f)); grad.middle.Override(new Color(0.6f, 0.62f, 0.65f)); grad.bottom.Override(new Color(0.35f, 0.35f, 0.36f));
            grad.multiplier.Override(1f); grad.skyIntensityMode.Override(SkyIntensityMode.Lux); grad.desiredLuxValue.Override(900f);
            var exposure = profile.Add<Exposure>(true); exposure.mode.Override(ExposureMode.AutomaticHistogram); exposure.limitMin.Override(6f); exposure.limitMax.Override(12f); exposure.compensation.Override(0.4f);
            var tone = profile.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true); bloom.intensity.Override(0.2f);
            var ao = profile.Add<ScreenSpaceAmbientOcclusion>(true); ao.intensity.Override(1.3f); ao.radius.Override(2f);
            var ssr = profile.Add<ScreenSpaceReflection>(true); ssr.enabled.Override(true);
            var contact = profile.Add<ContactShadows>(true); contact.enable.Override(true); contact.length.Override(0.5f);
            var vig = profile.Add<Vignette>(true); vig.intensity.Override(0.2f);
            var mb = profile.Add<MotionBlur>(true); mb.intensity.Override(0f);
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            return profile;
        }
    }
}
