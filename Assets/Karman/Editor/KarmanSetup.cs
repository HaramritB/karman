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
using UnityEngine.Rendering.HighDefinition;
using UnityEditor.Rendering.HighDefinition;
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

        /// <summary>
        /// The LROC colour mosaic is stretched for display (area-weighted linear mean 0.30); the Moon's mean normal
        /// albedo is about 0.12. This factor brings the texture to a physical albedo (mean 0.13) for both the distant
        /// Moon sphere and the terrain patches, so the Moon keeps its brightness when the terrain takes over.
        /// </summary>
        const float MoonAlbedoScale = 0.45f;

        [MenuItem("Karman/Rebuild Flight Scene")]
        public static void BuildAll()
        {
            AssetDatabase.Refresh();
            ConfigureRenderPipeline();
            ConfigurePlayer();
            VabSceneBuilder.Catalog();
            BuildSpaceScene(MenuScenePath, true);
            VabSceneBuilder.Build(VabScenePath);
            BuildScene();
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MenuScenePath, true), new EditorBuildSettingsScene(VabScenePath, true), new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[KarmanSetup] Flight scene rebuilt");
        }

        [MenuItem("Karman/Build macOS Player")]
        public static void BuildMac()
        {
            BuildAll();
            string dir = System.Environment.GetEnvironmentVariable("KARMAN_BUILD_DIR") ?? "Builds/macOS";
            Directory.CreateDirectory(dir);
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { MenuScenePath, VabScenePath, ScenePath },
                locationPathName = $"{dir}/Karman.app",
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
            const string path = "Assets/Settings/HDRP_Karman.asset";
            var hd = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(path);
            if (hd == null) { hd = ScriptableObject.CreateInstance<HDRenderPipelineAsset>(); AssetDatabase.CreateAsset(hd, path); }
            var rp = hd.currentPlatformRenderPipelineSettings;
            rp.supportVolumetricClouds = true;
            rp.supportVolumetrics = true;
            rp.supportSSR = true;
            rp.supportSSAO = true;
            rp.supportMotionVectors = true;
            rp.colorBufferFormat = RenderPipelineSettings.ColorBufferFormat.R16G16B16A16; // alpha for the navball render texture
            rp.supportedLitShaderMode = RenderPipelineSettings.SupportedLitShaderMode.Both;
            rp.dynamicResolutionSettings.enabled = true;
            rp.dynamicResolutionSettings.forceResolution = false;
            rp.hdShadowInitParams.directionalShadowsDepthBits = DepthBits.Depth32;
            hd.currentPlatformRenderPipelineSettings = rp;
            EditorUtility.SetDirty(hd);
            GraphicsSettings.defaultRenderPipeline = hd;
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = hd;
            }
            QualitySettings.SetQualityLevel(QualitySettings.names.Length - 1, false);
            QualitySettings.vSyncCount = 0;
            AssetDatabase.SaveAssets();
        }

        /// <summary>HDRP camera: temporal AA, sky-cleared background, post-processing on.</summary>
        internal static HDAdditionalCameraData HdCamera(Camera cam)
        {
            var d = cam.GetComponent<HDAdditionalCameraData>() ?? cam.gameObject.AddComponent<HDAdditionalCameraData>();
            d.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
            d.clearColorMode = HDAdditionalCameraData.ClearColorMode.Sky;
            d.stopNaNs = true;
            d.allowDynamicResolution = true; cam.allowDynamicResolution = true;   // quality presets scale the render resolution   // guards against black blotches from extreme values (Sun) in TAA and bloom
            return d;
        }

        /// <summary>Two-sided HDRP Lit-variant material (generated terrain patches have mixed winding).</summary>
        internal static void DoubleSided(Material m)
        {
            m.SetFloat("_DoubleSidedEnable", 1); m.SetFloat("_CullMode", 0); m.SetFloat("_CullModeForward", 0);
            m.SetFloat("_DoubleSidedNormalMode", 2); m.EnableKeyword("_DOUBLESIDED_ON");
            EditorUtility.SetDirty(m);
        }

        /// <summary>HDRP light data with physical units.</summary>
        internal static HDAdditionalLightData HdLight(Light l, float intensity, LightUnit unit)
        {
            var d = l.GetComponent<HDAdditionalLightData>() ?? l.gameObject.AddComponent<HDAdditionalLightData>();
            l.lightUnit = unit;
            l.intensity = intensity;
            return d;
        }

        /// <summary>Stars panorama as a cubemap for the physically based sky's space emission.</summary>
        static Cubemap StarsCubemap()
        {
            string p = $"{Tex}/Sky/8k_stars_milky_way.jpg";
            var imp = (TextureImporter)AssetImporter.GetAtPath(p);
            if (imp != null && imp.textureShape != TextureImporterShape.TextureCube)
            {
                imp.textureShape = TextureImporterShape.TextureCube;
                imp.generateCubemap = TextureImporterGenerateCubemap.Cylindrical;
                imp.maxTextureSize = 4096;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Cubemap>(p);
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
            PlayerSettings.macRetinaSupport = false;   // render at window points, not 2× Retina pixels (HDRP cost scales with pixels)
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

            // Photometry: the self-lit body shaders output reflectance relative to a white Lambertian surface facing the
            // Sun, and SpaceLighting scales that by E_sun / π (nits). _SunIntensity = 1 therefore means "physically lit by
            // the same 120,000 lx Sun as the HDRP-lit vessel and terrain", so one exposure fits everything in a frame.
            var earthMat = Mat("Earth", "Karman/Earth");
            earthMat.SetFloat("_SunIntensity", 1f);
            // City lights are far brighter than reality (so the night side reads at the darkest exposure the camera
            // adapts to) but well below sunlit ground, so they fade out of day-exposed frames.
            earthMat.SetFloat("_NightIntensity", 0.9f);
            earthMat.SetTexture("_DayTex", Load<Texture2D>($"{Tex}/Earth/8k_earth_daymap.jpg"));
            earthMat.SetTexture("_NightTex", Load<Texture2D>($"{Tex}/Earth/8k_earth_nightmap.jpg"));
            earthMat.SetTexture("_CloudTex", Load<Texture2D>($"{Tex}/Earth/8k_earth_clouds.jpg"));
            earthMat.SetTexture("_NormalTex", Load<Texture2D>($"{Tex}/Earth/2k_earth_normal_map.png"));
            earthMat.SetTexture("_SpecTex", Load<Texture2D>($"{Tex}/Earth/2k_earth_specular_map.png"));
            earthMat.SetTexture("_NoiseTex", NoiseTexture());
            var atmoMat = Mat("EarthAtmosphere", "Karman/Atmosphere");
            atmoMat.SetFloat("_SunIntensity", 11f);
            var moonMat = Mat("Moon", "Karman/Moon");
            moonMat.SetTexture("_ColorTex", Load<Texture2D>($"{Tex}/Moon/moon_color_4k.png"));
            moonMat.SetTexture("_SlopeTex", Load<Texture2D>($"{Tex}/Moon/moon_normal_eastnorth.png"));
            moonMat.SetFloat("_SunIntensity", 1f);
            moonMat.SetFloat("_Albedo", MoonAlbedoScale);
            moonMat.SetFloat("_NormalStrength", 0.9f);
            var sunMat = Mat("Sun", "Karman/Planet");
            sunMat.SetTexture("_MainTex", Load<Texture2D>($"{Tex}/Planets/2k_sun.jpg"));
            sunMat.SetFloat("_Emission", 40f);
            var lineMat = Mat("MapLine", "Karman/Line");
            var plumeMat = Mat("Plume", "Karman/Plume");
            var glareMat = Mat("SunGlare", "Karman/Glare");
            glareMat.SetFloat("_Intensity", 0.25f);   // HDRP bloom already blooms the sun; keep only a faint halo
            var ringMat = Mat("SaturnRing", "Karman/Ring");
            ringMat.SetTexture("_MainTex", Load<Texture2D>($"{Tex}/Planets/2k_saturn_ring_alpha.png"));
            var planetTemplate = Mat("PlanetTemplate", "Karman/Planet");
            planetTemplate.SetFloat("_SunIntensity", 1f);

            // HDRP draws the sky (physically based sky + star cubemap in the volume profile); no legacy skybox.
            RenderSettings.skybox = null;
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
            var sunHd = HdLight(sunLight, 120000f, LightUnit.Lux);
            sunHd.angularDiameter = 0.53f;
            sunHd.flareSize = 0.6f; sunHd.flareFalloff = 10f; sunHd.flareMultiplier = 0.6f;   // tight solar corona instead of a screen-wide veil
            sunHd.SetShadowResolution(4096);
            sunLight.shadows = LightShadows.Soft;
            sunLight.shadowResolution = LightShadowResolution.VeryHigh;
            sunLight.shadowNormalBias = 0.2f;
            sunLight.shadowBias = 0.02f;
            var shineGo = new GameObject("Earthshine Light");
            var shine = shineGo.AddComponent<Light>();
            shine.type = LightType.Directional;
            shine.color = new Color(0.55f, 0.68f, 1f);
            HdLight(shine, 2000f, LightUnit.Lux);
            var fillGo = new GameObject("Camera Fill Light");
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional; fill.color = new Color(0.8f, 0.87f, 1f); fill.shadows = LightShadows.None;
            var fillHd = HdLight(fill, 2500f, LightUnit.Lux);
            fillHd.interactsWithSky = false;
            shine.shadows = LightShadows.None;

            // Bodies
            var bodiesRoot = new GameObject("Celestial Bodies").transform;
            var sphereHi = SaveMesh(SphereMeshFactory.CubeSphere(160), "CubeSphere160");
            var sphereMid = SaveMesh(SphereMeshFactory.CubeSphere(96), "CubeSphere96");
            var sphereLo = SaveMesh(SphereMeshFactory.CubeSphere(48), "CubeSphere48");

            var earthView = MakeBody(bodiesRoot, SolarSystemData.Earth, BodyShading.Earth, earthMat, sphereHi);
            /* HDRP: the physically based sky renders Earth's atmosphere (sky, limb glow, aerial perspective).
            var atmoGo = new GameObject("Atmosphere");
            atmoGo.transform.SetParent(earthView.transform, false);
            atmoGo.AddComponent<MeshFilter>().sharedMesh = sphereMid;
            var atmoR = atmoGo.AddComponent<MeshRenderer>();
            atmoR.sharedMaterial = atmoMat;
            atmoR.shadowCastingMode = ShadowCastingMode.Off;
            earthView.atmosphere = atmoR; */
            var moonView = MakeBody(bodiesRoot, SolarSystemData.Moon, BodyShading.Moon, moonMat, sphereHi);
            var loader = simGo.AddComponent<TerrainLoader>();
            loader.moonDem = Load<TextAsset>($"{Root}/Data/Moon/moon_dem16.bytes");
            var moonTerrainMat = Mat("MoonTerrain", "Karman/MoonTerrain");
            DoubleSided(moonTerrainMat);
            moonTerrainMat.SetFloat("_Albedo", MoonAlbedoScale);
            moonTerrainMat.SetTexture("_ColorTex", Load<Texture2D>($"{Tex}/Moon/moon_color_4k.png"));
            moonTerrainMat.SetTexture("_NearDiff", PbrMaterials.Tex("gravelly_sand", "diff"));
            moonTerrainMat.SetTexture("_NearNor", PbrMaterials.Tex("gravelly_sand", "nor"));
            moonTerrainMat.SetTexture("_MidDiff", PbrMaterials.Tex("aerial_rocks_02", "diff"));
            moonTerrainMat.SetTexture("_MidNor", PbrMaterials.Tex("aerial_rocks_02", "nor"));
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
                m.SetFloat("_SunIntensity", 1f);   // shown as if at 1 AU: the 1/r² falloff is left to the eye's adaptation
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
            var probeGo = new GameObject("Sky Reflection Probe");
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.EveryFrame;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            probe.resolution = 128; probe.hdr = true; probe.importance = 10;
            probe.size = new Vector3(20000, 20000, 20000); probe.nearClipPlane = 30f; probe.farClipPlane = 200000f;
            probe.cullingMask = ~(1 << 30);
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            lighting.skyProbe = probe;
            lighting.sunIntensity = 120000f;
            lighting.earthShineMax = 13000f;
            lighting.cameraFill = fill;
            Object.DestroyImmediate(probeGo);
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
            vesselView.interstageMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Karman/Materials/Interstage.mat");
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
            HdCamera(cam);
            var orbitCam = camGo.AddComponent<OrbitCamera>();
            camGo.AddComponent<AudioListener>();

            var mapCamGo = new GameObject("Map Camera");
            var mapCam = mapCamGo.AddComponent<Camera>();
            mapCam.clearFlags = CameraClearFlags.Skybox;
            mapCam.fieldOfView = 50;
            mapCam.allowHDR = true;
            // The map is a diagram: bodies are specks on black, so metering cannot work. It gets a fixed daylight
            // exposure from a volume on the map layer that only the map camera sees.
            HdCamera(mapCam).volumeLayerMask = 1 | (1 << MapView.Layer);
            var mapVolGo = new GameObject("Map Volume") { layer = MapView.Layer };
            var mapVol = mapVolGo.AddComponent<Volume>();
            mapVol.isGlobal = true; mapVol.priority = 10;
            mapVol.sharedProfile = BuildMapProfile();

            // Post-processing
            var volGo = new GameObject("Global Volume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = BuildVolumeProfile();
            volGo.AddComponent<PlanetSky>().volume = vol;

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
            navball.ballMaterial.SetColor("_Sky", new Color(0.13f, 0.30f, 0.50f)); navball.ballMaterial.SetColor("_Ground", new Color(0.30f, 0.21f, 0.13f));
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
            // Planet-centred physically based sky (Earth): sky colour, sunsets, the limb seen from orbit and aerial
            // perspective on all geometry. PlanetSky moves its centre with the floating origin.
            var env = profile.Add<VisualEnvironment>(true);
            env.skyType.Override((int)SkyType.PhysicallyBased);
            env.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
            env.planetRadius.Override(6371f);
            env.centerMode.Override(VisualEnvironment.PlanetMode.Manual);
            env.renderingSpace.Override(RenderingSpace.World);
            var sky = profile.Add<PhysicallyBasedSky>(true);
            sky.type.Override(PhysicallyBasedSkyModel.EarthAdvanced);
            sky.atmosphericScattering.Override(true);
            sky.aerosolDensity.Override(sky.aerosolDensity.value * 0.4f);   // clearer limb and less milky Earth from orbit
            var stars = StarsCubemap();
            if (stars != null) { sky.spaceEmissionTexture.Override(stars); sky.spaceEmissionMultiplier.Override(600f); }
            // Exposure, as a camera in space would be set: for the sunlit subject, however small it is in the frame.
            // Metering the brightest fifth of the picture (less the top 1 %: Sun, glints) keeps black sky from dragging
            // the exposure up; EV 15.2 holds sunlit cloud (34,000 nits) just below white, EV 9 is the darkest adaptation
            // (night side, eclipse), six stops down.
            var exposure = profile.Add<Exposure>(true);
            exposure.mode.Override(ExposureMode.AutomaticHistogram);
            exposure.histogramPercentages.Override(new Vector2(80f, 99f));
            exposure.limitMin.Override(9f);
            exposure.limitMax.Override(15.2f);
            exposure.compensation.Override(1.6f);
            exposure.adaptationSpeedDarkToLight.Override(2.5f);
            exposure.adaptationSpeedLightToDark.Override(1.5f);
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.12f);
            bloom.scatter.Override(0.55f);
            var fog = profile.Add<Fog>(true);
            fog.enabled.Override(true);
            fog.meanFreePath.Override(60000f);
            fog.maxFogDistance.Override(200000f);
            fog.baseHeight.Override(0f);
            fog.maximumHeight.Override(3000f);
            var clouds = profile.Add<VolumetricClouds>(true);
            clouds.enable.Override(true);
            clouds.cloudPreset = VolumetricClouds.CloudPresets.Sparse;
            var ao = profile.Add<ScreenSpaceAmbientOcclusion>(true);
            ao.intensity.Override(1.2f);
            ao.radius.Override(1.5f);
            var ssr = profile.Add<ScreenSpaceReflection>(true);
            ssr.enabled.Override(true);
            var contact = profile.Add<ContactShadows>(true);
            contact.enable.Override(true);
            contact.length.Override(0.4f);
            var shadows = profile.Add<HDShadowSettings>(true);
            shadows.maxShadowDistance.Override(1500f);
            shadows.cascadeShadowSplitCount.Override(4);
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.18f);
            var mb = profile.Add<MotionBlur>(true); mb.intensity.Override(0f);       // time warp makes motion blur smear everything
            var dof = profile.Add<DepthOfField>(true); dof.focusMode.Override(DepthOfFieldMode.Off);
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        /// <summary>
        /// Tileable fractal noise (256², one channel, mean 0.5) used by the planet shaders for structure below the
        /// resolution of the global maps. Generated once and kept as a project-owned asset.
        /// </summary>
        internal static Texture2D NoiseTexture()
        {
            string path = $"{Tex}/FX/noise_fbm.png";
            if (!File.Exists(path))
            {
                const int N = 256;
                var rng = new System.Random(20261126);
                // Periodic value noise: a lattice of period P wraps exactly, so every octave tiles.
                float[] Octave(int period)
                {
                    var lattice = new float[period * period];
                    for (int k = 0; k < lattice.Length; k++) lattice[k] = (float)rng.NextDouble();
                    var o = new float[N * N];
                    for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float fx = (float)x / N * period, fy = (float)y / N * period;
                        int x0 = (int)fx, y0 = (int)fy; float tx = fx - x0, ty = fy - y0;
                        tx = tx * tx * tx * (tx * (tx * 6 - 15) + 10); ty = ty * ty * ty * (ty * (ty * 6 - 15) + 10);
                        int x1 = (x0 + 1) % period, y1 = (y0 + 1) % period;
                        float a = lattice[y0 * period + x0], b = lattice[y0 * period + x1], c = lattice[y1 * period + x0], d = lattice[y1 * period + x1];
                        o[y * N + x] = Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
                    }
                    return o;
                }
                var sum = new float[N * N];
                float amp = 1, norm = 0;
                foreach (int period in new[] { 4, 8, 16, 32, 64, 128 })
                {
                    var o = Octave(period);
                    for (int k = 0; k < sum.Length; k++) sum[k] += o[k] * amp;
                    norm += amp; amp *= 0.55f;
                }
                // Stretch to the full range around a mean of 0.5 so "noise - 0.5" is a balanced signed detail term.
                float mean = 0, min = float.MaxValue, max = float.MinValue;
                for (int k = 0; k < sum.Length; k++) { sum[k] /= norm; mean += sum[k]; min = Mathf.Min(min, sum[k]); max = Mathf.Max(max, sum[k]); }
                mean /= sum.Length;
                float half = Mathf.Max(max - mean, mean - min);
                var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
                for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float v = Mathf.Clamp01(0.5f + 0.5f * (sum[y * N + x] - mean) / half);
                    tex.SetPixel(x, y, new Color(v, v, v, 1));
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
            }
            return Load<Texture2D>(path);
        }

        static VolumeProfile BuildMapProfile()
        {
            string path = $"{SettingsDir}/MapPostProcess.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            var exposure = profile.Add<Exposure>(true);
            exposure.mode.Override(ExposureMode.Fixed);
            exposure.fixedExposure.Override(14.3f);
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
