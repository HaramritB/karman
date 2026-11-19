using System.IO;
using System.Linq;
using Karman.Cameras;
using Karman.Core;
using Karman.Diagnostics;
using Karman.Flight;
using Karman.Map;
using Karman.Rendering;
using Karman.Simulation;
using Karman.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace Karman.EditorTools
{
    /// <summary>
    /// Reproducible project setup: builds materials, render settings and the Flight scene from code so the scene
    /// can be regenerated in batch mode (Unity -executeMethod Karman.EditorTools.KarmanSetup.BuildAll).
    /// </summary>
    public static class KarmanSetup
    {
        const string Root = "Assets/Karman";
        const string Tex = Root + "/Art/Textures";
        const string MatDir = Root + "/Materials";
        const string SettingsDir = Root + "/Settings";
        public const string ScenePath = Root + "/Scenes/Flight.unity";
        public const string MenuScenePath = Root + "/Scenes/MainMenu.unity";
        public const string VabScenePath = Root + "/Scenes/VAB.unity";

        [MenuItem("Karman/Rebuild Flight Scene")]
        public static void BuildAll()
        {
            AssetDatabase.Refresh();
            ConfigureRenderPipeline();
            ConfigurePlayer();
            VabSceneBuilder.Catalog();
            BuildSpaceScene(MenuScenePath, true);
            BuildScene();
            VabSceneBuilder.Build(VabScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MenuScenePath, true), new EditorBuildSettingsScene(VabScenePath, true), new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[KarmanSetup] Flight scene rebuilt");
        }

        [MenuItem("Karman/Build macOS Player")]
        public static void BuildMac()
        {
            BuildAll();
            Directory.CreateDirectory("Builds/macOS");
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { MenuScenePath, VabScenePath, ScenePath },
                locationPathName = "Builds/macOS/Karman.app",
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log($"[KarmanSetup] Build {report.summary.result}: {report.summary.totalSize / (1024 * 1024)} MB, {report.summary.totalErrors} errors, {report.summary.totalTime}");
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        // ---------------------------------------------------------------- settings

        static void ConfigureRenderPipeline()
        {
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            if (urp == null) { Debug.LogError("PC_RPAsset missing"); return; }
            urp.supportsCameraDepthTexture = true;
            urp.supportsHDR = true;
            urp.msaaSampleCount = 4;
            urp.shadowDistance = 250f;
            urp.shadowCascadeCount = 2;
            urp.renderScale = 1f;
            EditorUtility.SetDirty(urp);
            GraphicsSettings.defaultRenderPipeline = urp;
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = urp;
            }
            QualitySettings.SetQualityLevel(QualitySettings.names.Length - 1, false);
            QualitySettings.vSyncCount = 0;
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "Karman";
            PlayerSettings.productName = "KÁRMÁN";
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone, "com.karman.spaceflight");
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneOSX, new[] { GraphicsDeviceType.Metal });
        }

        // ---------------------------------------------------------------- materials

        internal static T Load<T>(string path) where T : Object
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a == null) Debug.LogWarning($"[KarmanSetup] missing asset {path}");
            return a;
        }

        internal static Material Mat(string name, string shader)
        {
            Directory.CreateDirectory(MatDir);
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Shader.Find(shader);
            if (sh == null) Debug.LogError($"[KarmanSetup] shader not found: {shader}");
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            else m.shader = sh;
            return m;
        }

        // ---------------------------------------------------------------- scene

        static void BuildScene() => BuildSpaceScene(ScenePath, false);

        static void BuildSpaceScene(string scenePath, bool menu)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(scenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var earthMat = Mat("Earth", "Karman/Earth");
            earthMat.SetTexture("_DayTex", Load<Texture2D>($"{Tex}/Earth/8k_earth_daymap.jpg"));
            earthMat.SetTexture("_NightTex", Load<Texture2D>($"{Tex}/Earth/8k_earth_nightmap.jpg"));
            earthMat.SetTexture("_CloudTex", Load<Texture2D>($"{Tex}/Earth/8k_earth_clouds.jpg"));
            earthMat.SetTexture("_NormalTex", Load<Texture2D>($"{Tex}/Earth/2k_earth_normal_map.png"));
            earthMat.SetTexture("_SpecTex", Load<Texture2D>($"{Tex}/Earth/2k_earth_specular_map.png"));
            var atmoMat = Mat("EarthAtmosphere", "Karman/Atmosphere");
            atmoMat.SetFloat("_SunIntensity", 11f);
            var moonMat = Mat("Moon", "Karman/Moon");
            moonMat.SetTexture("_ColorTex", Load<Texture2D>($"{Tex}/Moon/moon_color_4k.png"));
            moonMat.SetTexture("_SlopeTex", Load<Texture2D>($"{Tex}/Moon/moon_normal_eastnorth.png"));
            moonMat.SetFloat("_Albedo", 0.75f);
            moonMat.SetFloat("_NormalStrength", 0.9f);
            var sunMat = Mat("Sun", "Karman/Planet");
            sunMat.SetTexture("_MainTex", Load<Texture2D>($"{Tex}/Planets/2k_sun.jpg"));
            sunMat.SetFloat("_Emission", 40f);
            var skyMat = Mat("Sky", "Karman/Sky");
            skyMat.SetTexture("_MainTex", Load<Texture2D>($"{Tex}/Sky/8k_stars_milky_way.jpg"));
            var lineMat = Mat("MapLine", "Karman/Line");
            var plumeMat = Mat("Plume", "Karman/Plume");
            var glareMat = Mat("SunGlare", "Karman/Glare");
            var ringMat = Mat("SaturnRing", "Karman/Ring");
            ringMat.SetTexture("_MainTex", Load<Texture2D>($"{Tex}/Planets/2k_saturn_ring_alpha.png"));
            var planetTemplate = Mat("PlanetTemplate", "Karman/Planet");

            RenderSettings.skybox = skyMat;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.012f, 0.014f, 0.02f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 0.35f;
            RenderSettings.fog = false;

            // Simulation
            var simGo = new GameObject("Simulation");
            simGo.AddComponent<SimulationController>();

            // Lighting
            var sunLightGo = new GameObject("Sun Light");
            var sunLight = sunLightGo.AddComponent<Light>();
            sunLight.type = LightType.Directional;
            sunLight.color = new Color(1f, 0.97f, 0.93f);
            sunLight.intensity = 3.2f;
            sunLight.shadows = LightShadows.Soft;
            sunLight.shadowResolution = LightShadowResolution.VeryHigh;
            sunLight.shadowNormalBias = 0.2f;
            sunLight.shadowBias = 0.02f;
            var shineGo = new GameObject("Earthshine Light");
            var shine = shineGo.AddComponent<Light>();
            shine.type = LightType.Directional;
            shine.color = new Color(0.55f, 0.68f, 1f);
            shine.intensity = 0.2f;
            shine.shadows = LightShadows.None;

            // Bodies
            var bodiesRoot = new GameObject("Celestial Bodies").transform;
            var sphereHi = SaveMesh(SphereMeshFactory.CubeSphere(160), "CubeSphere160");
            var sphereMid = SaveMesh(SphereMeshFactory.CubeSphere(96), "CubeSphere96");
            var sphereLo = SaveMesh(SphereMeshFactory.CubeSphere(48), "CubeSphere48");

            var earthView = MakeBody(bodiesRoot, SolarSystemData.Earth, BodyShading.Earth, earthMat, sphereHi);
            var atmoGo = new GameObject("Atmosphere");
            atmoGo.transform.SetParent(earthView.transform, false);
            atmoGo.AddComponent<MeshFilter>().sharedMesh = sphereMid;
            var atmoR = atmoGo.AddComponent<MeshRenderer>();
            atmoR.sharedMaterial = atmoMat;
            atmoR.shadowCastingMode = ShadowCastingMode.Off;
            earthView.atmosphere = atmoR;
            var moonView = MakeBody(bodiesRoot, SolarSystemData.Moon, BodyShading.Moon, moonMat, sphereHi);
            var loader = simGo.AddComponent<TerrainLoader>();
            loader.moonDem = Load<TextAsset>($"{Root}/Data/Moon/moon_dem16.bytes");
            var moonTerrainMat = Mat("MoonTerrain", "Karman/MoonTerrain");
            moonTerrainMat.SetTexture("_ColorTex", Load<Texture2D>($"{Tex}/Moon/moon_color_4k.png"));
            var quadtree = moonView.gameObject.AddComponent<TerrainQuadtree>();
            quadtree.bodyView = moonView;
            quadtree.material = moonTerrainMat;
            var sunView = MakeBody(bodiesRoot, SolarSystemData.Sun, BodyShading.Sun, sunMat, sphereLo);

            string[] planetIds = { SolarSystemData.Mercury, SolarSystemData.Venus, SolarSystemData.Mars, SolarSystemData.Jupiter, SolarSystemData.Saturn, SolarSystemData.Uranus, SolarSystemData.Neptune };
            string[] planetTex = { "2k_mercury.jpg", "2k_venus_atmosphere.jpg", "2k_mars.jpg", "2k_jupiter.jpg", "2k_saturn.jpg", "2k_uranus.jpg", "2k_neptune.jpg" };
            var texList = new System.Collections.Generic.List<Texture>();
            for (int i = 0; i < planetIds.Length; i++)
            {
                var m = Mat("Planet_" + planetIds[i], "Karman/Planet");
                var t = Load<Texture2D>($"{Tex}/Planets/{planetTex[i]}");
                m.SetTexture("_MainTex", t);
                texList.Add(t);
                if (planetIds[i] == SolarSystemData.Venus) m.SetColor("_RimColor", new Color(1f, 0.9f, 0.7f, 0.6f));
                if (planetIds[i] == SolarSystemData.Mars) m.SetColor("_RimColor", new Color(1f, 0.6f, 0.45f, 0.25f));
                if (planetIds[i] == SolarSystemData.Uranus || planetIds[i] == SolarSystemData.Neptune) m.SetColor("_RimColor", new Color(0.6f, 0.85f, 1f, 0.4f));
                var view = MakeBody(bodiesRoot, planetIds[i], BodyShading.Planet, m, sphereMid);
                if (planetIds[i] == SolarSystemData.Saturn)
                {
                    var ring = new GameObject("Rings");
                    ring.transform.SetParent(view.transform, false);
                    ring.AddComponent<MeshFilter>();
                    ring.AddComponent<RingView>();
                    var rr = ring.AddComponent<MeshRenderer>();
                    rr.sharedMaterial = ringMat;
                    rr.shadowCastingMode = ShadowCastingMode.Off;
                    view.rings = rr;
                }
            }

            // Solar glare quad
            var glare = GameObject.CreatePrimitive(PrimitiveType.Quad);
            glare.name = "Sun Glare";
            Object.DestroyImmediate(glare.GetComponent<Collider>());
            glare.GetComponent<MeshRenderer>().sharedMaterial = glareMat;
            glare.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            glare.transform.SetParent(sunView.transform.parent, false);

            var lighting = simGo.AddComponent<SpaceLighting>();
            lighting.sunLight = sunLight;
            lighting.planetShineLight = shine;
            lighting.glare = glare.transform;

            VesselController vessel = null; VesselView vesselView = null;
            if (!menu) {
            // Vessel: assembled at runtime from the launch request (VAB) or the default Gemini craft.
            var vesselGo = new GameObject("Vessel");
            vessel = vesselGo.AddComponent<VesselController>();
            vesselView = vesselGo.AddComponent<VesselView>();
            vesselView.catalog = VabSceneBuilder.Catalog();
            vesselView.plumeMaterial = plumeMat;
            vesselView.plumeMesh = SaveMesh(PlumeMesh(), "PlumeCone");
            vesselView.canopyMaterial = Mat("ParachuteCanopy", "Karman/Line");
            var plasmaMat = Mat("EntryPlasma", "Karman/Plume");
            plasmaMat.SetColor("_Color", new Color(1f, 0.55f, 0.35f)); plasmaMat.SetColor("_EdgeColor", new Color(1f, 0.25f, 0.45f)); plasmaMat.SetFloat("_Intensity", 2.5f);
            vesselView.plasmaMaterial = plasmaMat;
            vessel.view = vesselView;
            LaunchSiteBuilder.Build(earthMat);
            }

            // Cameras
            var camGo = new GameObject("Flight Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 55;
            cam.allowHDR = true;
            cam.allowMSAA = true;
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.None;
            camData.requiresDepthTexture = true;
            var orbitCam = camGo.AddComponent<OrbitCamera>();
            camGo.AddComponent<AudioListener>();

            var mapCamGo = new GameObject("Map Camera");
            var mapCam = mapCamGo.AddComponent<Camera>();
            mapCam.clearFlags = CameraClearFlags.Skybox;
            mapCam.fieldOfView = 50;
            mapCam.allowHDR = true;
            var mapData = mapCamGo.AddComponent<UniversalAdditionalCameraData>();
            mapData.renderPostProcessing = true;

            // Post-processing
            var volGo = new GameObject("Global Volume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = BuildVolumeProfile();

            // UI
            var panel = BuildPanelSettings();
            if (menu)
            {
                orbitCam.targetKind = OrbitCamera.TargetKind.Body;
                orbitCam.targetBodyId = SolarSystemData.Earth;
                orbitCam.distance = 6.371e6 * 2.6; orbitCam.yaw = 150; orbitCam.pitch = 12;
                Object.DestroyImmediate(mapCamGo);
                var menuGo = new GameObject("Main Menu");
                menuGo.AddComponent<UIDocument>().panelSettings = panel;
                var mm = menuGo.AddComponent<Karman.UI.MainMenu>();
                mm.orbitCamera = orbitCam;
                EditorSceneManager.SaveScene(scene, scenePath);
                return;
            }
            var hudGo = new GameObject("Flight HUD");
            var hudDoc = hudGo.AddComponent<UIDocument>();
            hudDoc.panelSettings = panel;
            hudDoc.sortingOrder = 0;
            var hud = hudGo.AddComponent<FlightHud>();
            var navball = hudGo.AddComponent<NavballView>();
            navball.ballMaterial = Mat("Navball", "Karman/Navball");
            navball.vessel = vessel;
            hud.navball = navball;
            var autopilot = simGo.AddComponent<AscentAutopilot>();
            autopilot.vessel = vessel;
            hud.autopilot = autopilot;
            var lunarAuto = simGo.AddComponent<LunarAutopilot>();
            lunarAuto.vessel = vessel;
            hud.lunarAutopilot = lunarAuto;
            var lunarCapture = simGo.AddComponent<LunarCapture>();
            lunarCapture.orbitCamera = orbitCam; lunarCapture.autopilot = lunarAuto; lunarCapture.planner = null;
            var launchCapture = simGo.AddComponent<LaunchCapture>();
            launchCapture.orbitCamera = orbitCam;
            launchCapture.autopilot = autopilot;

            var mapGo = new GameObject("Map View");
            var mapLabelsGo = new GameObject("Map Labels");
            mapLabelsGo.transform.SetParent(mapGo.transform.parent, false);
            var mapDoc = mapLabelsGo.AddComponent<UIDocument>();
            mapDoc.panelSettings = panel;
            mapDoc.sortingOrder = 1;
            var map = mapGo.AddComponent<MapView>();
            map.mapCamera = mapCam;
            map.flightCamera = cam;
            map.labelsDocument = mapDoc;
            map.lineMaterial = lineMat;
            map.planetMaterialTemplate = planetTemplate;
            map.sunMaterial = sunMat;
            map.earthMaterial = earthMat;
            map.moonMaterial = moonMat;
            map.bodyTextureIds = planetIds;
            map.bodyTextures = texList.ToArray();
            var planner = simGo.AddComponent<ManeuverPlanner>();
            planner.vessel = vessel;
            var overlay = mapGo.AddComponent<MapPlanningOverlay>();
            overlay.map = map; overlay.planner = planner; overlay.lineMaterial = lineMat;
            var planCapture = simGo.AddComponent<PlanningCapture>();
            planCapture.orbitCamera = orbitCam; planCapture.mapView = map; planCapture.planner = planner;


            var capture = simGo.AddComponent<AutoCapture>();
            capture.orbitCamera = orbitCam;
            capture.mapView = map;

            EditorSceneManager.SaveScene(scene, scenePath);
        }

        static CelestialBodyView MakeBody(Transform parent, string id, BodyShading shading, Material mat, Mesh mesh)
        {
            var go = new GameObject(id.Substring(0, 1).ToUpper() + id.Substring(1));
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var view = go.AddComponent<CelestialBodyView>();
            view.bodyId = id;
            view.shading = shading;
            view.surface = mr;
            return view;
        }

        internal static Bounds RendererBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds();
            var b = rs[0].bounds;
            foreach (var r in rs.Skip(1)) b.Encapsulate(r.bounds);
            return b;
        }

        static Mesh SaveMesh(Mesh mesh, string name)
        {
            string dir = Root + "/Art/Meshes";
            Directory.CreateDirectory(dir);
            string path = $"{dir}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) AssetDatabase.DeleteAsset(path);
            var copy = Object.Instantiate(mesh);
            copy.name = name;
            AssetDatabase.CreateAsset(copy, path);
            return copy;
        }

        /// <summary>Open cone from the nozzle (y = 0, radius 0.12) to the tail (y = -1, radius 0.5).</summary>
        static Mesh PlumeMesh()
        {
            const int seg = 32, rings = 12;
            var v = new System.Collections.Generic.List<Vector3>(); var n = new System.Collections.Generic.List<Vector3>();
            var uv = new System.Collections.Generic.List<Vector2>(); var t = new System.Collections.Generic.List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float f = (float)r / rings;
                float radius = Mathf.Lerp(0.12f, 0.5f, Mathf.Sqrt(f));
                for (int s = 0; s <= seg; s++)
                {
                    float a = s * Mathf.PI * 2 / seg;
                    var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    v.Add(d * radius + Vector3.down * f); n.Add(d); uv.Add(new Vector2((float)s / seg, f));
                }
            }
            for (int r = 0; r < rings; r++)
            for (int s = 0; s < seg; s++)
            {
                int i = r * (seg + 1) + s;
                t.AddRange(new[] { i, i + seg + 1, i + 1, i + 1, i + seg + 1, i + seg + 2 });
            }
            var m = new Mesh { name = "PlumeCone" };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        static VolumeProfile BuildVolumeProfile()
        {
            Directory.CreateDirectory(SettingsDir);
            string path = $"{SettingsDir}/SpacePostProcess.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.1f);
            bloom.intensity.Override(0.55f);
            bloom.scatter.Override(0.72f);
            bloom.highQualityFiltering.Override(true);
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.15f);
            color.contrast.Override(8f);
            color.saturation.Override(4f);
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.2f);
            vig.smoothness.Override(0.5f);
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        internal static PanelSettings BuildPanelSettings()
        {
            Directory.CreateDirectory(SettingsDir);
            string themePath = $"{SettingsDir}/KarmanRuntimeTheme.tss";
            if (!File.Exists(themePath))
            {
                File.WriteAllText(themePath, "@import url(\"unity-theme://default\");\n");
                AssetDatabase.ImportAsset(themePath);
            }
            string path = $"{SettingsDir}/KarmanPanelSettings.asset";
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (ps == null)
            {
                ps = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(ps, path);
            }
            ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(themePath);
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            EditorUtility.SetDirty(ps);
            return ps;
        }
    }
}
