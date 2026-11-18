using System.IO;
using Karman.Flight;
using Karman.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Karman.EditorTools
{
    /// <summary>
    /// Generates the Earth launch complex, modelled on LC-39A (Kennedy Space Center): a 15 m raised pad with flame
    /// trench, fixed service structure with access arms and lightning mast, water-deluge tower, crawlerway, a
    /// processing hangar, night floodlights and the pad exhaust cloud. Local frame: +X east, +Y up, +Z north.
    /// </summary>
    public static class LaunchSiteBuilder
    {
        static Material Lit(string name, Color c, float smooth, float metal = 0)
        {
            var m = KarmanSetup.Mat(name, "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
            EditorUtility.SetDirty(m);
            return m;
        }

        static GameObject Box(string name, Vector3 pos, Vector3 size, Material m, Transform parent, Vector3? euler = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = size;
            if (euler.HasValue) go.transform.localEulerAngles = euler.Value;
            go.GetComponent<Renderer>().sharedMaterial = m;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static GameObject Cyl(string name, Vector3 pos, float radius, float height, Material m, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = new Vector3(radius * 2, height / 2, radius * 2);
            go.GetComponent<Renderer>().sharedMaterial = m;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        public static GameObject Build(Material earthMat)
        {
            var site = new GameObject("Launch Site (LC-39A)");
            var view = site.AddComponent<LaunchSiteView>();

            // Ground patch sampling the same imagery as the globe.
            var groundMat = KarmanSetup.Mat("LaunchSiteGround", "Karman/Ground");
            groundMat.SetTexture("_DayTex", earthMat.GetTexture("_DayTex"));
            groundMat.SetTexture("_NightTex", earthMat.GetTexture("_NightTex"));
            groundMat.SetTexture("_SpecTex", earthMat.GetTexture("_SpecTex"));
            var ground = new GameObject("Ground Patch");
            ground.transform.SetParent(site.transform, false);
            ground.AddComponent<MeshFilter>();
            var gr = ground.AddComponent<MeshRenderer>();
            gr.sharedMaterial = groundMat; gr.shadowCastingMode = ShadowCastingMode.Off; gr.receiveShadows = true;
            view.ground = gr;

            var concrete = Lit("Site_Concrete", new Color(0.62f, 0.61f, 0.58f), 0.25f);
            var darkConcrete = Lit("Site_ConcreteDark", new Color(0.18f, 0.18f, 0.18f), 0.2f);
            var gravel = Lit("Site_Crawlerway", new Color(0.55f, 0.52f, 0.47f), 0.1f);
            var steelRed = Lit("Site_TowerRed", new Color(0.55f, 0.12f, 0.08f), 0.35f, 0.3f);
            var steelGrey = Lit("Site_SteelGrey", new Color(0.45f, 0.46f, 0.48f), 0.45f, 0.7f);
            var white = Lit("Site_PaintWhite", new Color(0.85f, 0.86f, 0.87f), 0.4f);
            var black = Lit("Site_Black", new Color(0.03f, 0.03f, 0.03f), 0.05f);
            var root = new GameObject("Pad").transform;
            root.SetParent(site.transform, false);

            // Raised pad (LC-39A hardstand ~15 m above grade) with ramp and flame trench.
            Box("Pad mound", new Vector3(0, 7.5f, 0), new Vector3(120, 15, 110), concrete, root);
            Box("Pad apron", new Vector3(0, 0.05f, 0), new Vector3(200, 0.1f, 190), darkConcrete, root);
            Box("Ramp", new Vector3(0, 7.5f, -95), new Vector3(30, 15, 85), concrete, root, new Vector3(-10f, 0, 0));
            Box("Flame trench", new Vector3(0, 15.02f, 22), new Vector3(12, 0.05f, 70), black, root);
            Box("Exhaust opening", new Vector3(0, 15.03f, 0), new Vector3(11, 0.05f, 11), black, root);
            for (int k = 0; k < 4; k++)
            {
                float a = k * Mathf.PI / 2 + Mathf.PI / 4;
                Box("Hold-down post", new Vector3(Mathf.Cos(a) * 4.2f, 16.0f, Mathf.Sin(a) * 4.2f), new Vector3(1.0f, 2.0f, 1.0f), steelGrey, root);
            }

            // Fixed service structure west of the vehicle: lattice tower, access arms, lightning mast.
            var fss = new GameObject("Fixed Service Structure").transform;
            fss.SetParent(root, false); fss.localPosition = new Vector3(-17, 15, 0);
            const float H = 88, W = 10;
            foreach (var c in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
                Box("Leg", new Vector3(c.x * W / 2, H / 2, c.y * W / 2), new Vector3(0.9f, H, 0.9f), steelRed, fss);
            for (float y = 6; y < H; y += 6)
            {
                Box("Floor", new Vector3(0, y, 0), new Vector3(W, 0.4f, W), steelGrey, fss);
                Box("Brace", new Vector3(0, y - 3, W / 2), new Vector3(W * 1.1f, 0.35f, 0.35f), steelRed, fss, new Vector3(0, 0, 32));
                Box("Brace", new Vector3(0, y - 3, -W / 2), new Vector3(W * 1.1f, 0.35f, 0.35f), steelRed, fss, new Vector3(0, 0, -32));
                Box("Brace", new Vector3(W / 2, y - 3, 0), new Vector3(0.35f, 0.35f, W * 1.1f), steelRed, fss, new Vector3(32, 0, 0));
                Box("Brace", new Vector3(-W / 2, y - 3, 0), new Vector3(0.35f, 0.35f, W * 1.1f), steelRed, fss, new Vector3(-32, 0, 0));
            }
            Box("Crew access arm", new Vector3(W / 2 + 4.5f, 60, 0), new Vector3(9, 2.6f, 2.4f), white, fss);
            Box("Umbilical arm", new Vector3(W / 2 + 4f, 36, 0), new Vector3(8, 1.2f, 1.4f), steelGrey, fss);
            Cyl("Lightning mast", new Vector3(0, H + 18, 0), 0.4f, 36, steelGrey, fss);

            // Water deluge tower (LC-39A's ~88 m water tower), north-west of the pad.
            var wt = new GameObject("Water Tower").transform;
            wt.SetParent(root, false); wt.localPosition = new Vector3(-150, 0, 150);
            for (int k = 0; k < 6; k++)
            {
                float a = k * Mathf.PI / 3;
                Cyl("Leg", new Vector3(Mathf.Cos(a) * 7, 34, Mathf.Sin(a) * 7), 0.5f, 68, steelGrey, wt);
            }
            Cyl("Tank", new Vector3(0, 76, 0), 9, 16, white, wt);
            Cyl("Riser", new Vector3(0, 34, 0), 1.2f, 68, steelGrey, wt);

            // Crawlerway to the south and a processing hangar.
            Box("Crawlerway", new Vector3(0, 0.08f, -1300), new Vector3(40, 0.12f, 2400), gravel, root);
            Box("Crawlerway median", new Vector3(0, 0.1f, -1300), new Vector3(12, 0.12f, 2400), Lit("Site_Grass", new Color(0.22f, 0.27f, 0.14f), 0.05f), root);
            Box("Processing hangar", new Vector3(140, 18, -320), new Vector3(110, 36, 60), white, root);
            Box("Hangar door", new Vector3(140, 14, -289.9f), new Vector3(70, 28, 0.3f), steelGrey, root);

            // Night floodlights on four masts around the pad.
            for (int k = 0; k < 4; k++)
            {
                float a = k * Mathf.PI / 2 + Mathf.PI / 4;
                var mast = Cyl("Flood mast", new Vector3(Mathf.Cos(a) * 75, 25, Mathf.Sin(a) * 75), 0.5f, 50, steelGrey, root);
                var lg = new GameObject("Floodlight");
                lg.transform.SetParent(root, false);
                lg.transform.localPosition = new Vector3(Mathf.Cos(a) * 75, 50, Mathf.Sin(a) * 75);
                lg.transform.LookAt(lg.transform.parent.TransformPoint(new Vector3(0, 40, 0)));
                var l = lg.AddComponent<Light>();
                l.type = LightType.Spot; l.spotAngle = 40; l.range = 260; l.intensity = 1500; l.color = new Color(1f, 0.93f, 0.82f);
                l.shadows = LightShadows.None;
            }

            // Exhaust cloud from the flame trench.
            var smokeGo = new GameObject("Pad Exhaust Cloud");
            smokeGo.transform.SetParent(root, false);
            smokeGo.transform.localPosition = new Vector3(0, 15, 25);
            var ps = smokeGo.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true; main.playOnAwake = false; main.duration = 10;
            main.startLifetime = new ParticleSystem.MinMaxCurve(7, 13);
            main.startSpeed = new ParticleSystem.MinMaxCurve(18, 45);
            main.startSize = new ParticleSystem.MinMaxCurve(14, 32);
            main.startColor = new Color(0.86f, 0.84f, 0.8f, 0.55f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 2500;
            main.gravityModifier = -0.02f;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 55; shape.radius = 6;
            shape.rotation = new Vector3(-30, 0, 0);
            var em = ps.emission; em.rateOverTime = 0;
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 0.6f, 1, 3.2f));
            var col = ps.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(new Color(1, 0.85f, 0.6f), 0), new GradientColorKey(Color.white, 0.15f), new GradientColorKey(new Color(0.75f, 0.75f, 0.75f), 1) },
                         new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.6f, 0.1f), new GradientAlphaKey(0.35f, 0.6f), new GradientAlphaKey(0, 1) });
            col.color = grad;
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local;
            vel.y = new ParticleSystem.MinMaxCurve(3, 9); vel.x = new ParticleSystem.MinMaxCurve(0, 0); vel.z = new ParticleSystem.MinMaxCurve(0, 0);
            var limit = ps.limitVelocityOverLifetime; limit.enabled = true; limit.limit = 12; limit.dampen = 0.08f;
            var pr = smokeGo.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = SmokeMaterial();
            pr.sortMode = ParticleSystemSortMode.Distance;
            var fx = site.AddComponent<LaunchEffects>();
            fx.smoke = ps;
            return site;
        }

        static Material SmokeMaterial()
        {
            string texPath = "Assets/Karman/Art/Textures/FX/smoke_puff.png";
            Directory.CreateDirectory(Path.GetDirectoryName(texPath));
            if (!File.Exists(texPath))
            {
                const int N = 128;
                var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
                for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2 - 1, dy = (y + 0.5f) / N * 2 - 1;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float n = Mathf.PerlinNoise(x * 0.08f, y * 0.08f) * 0.6f + Mathf.PerlinNoise(x * 0.21f + 7, y * 0.21f + 3) * 0.4f;
                    float a = Mathf.Clamp01(1 - r) ; a = a * a * (0.55f + 0.45f * n);
                    float shade = 0.75f + 0.25f * n;
                    tex.SetPixel(x, y, new Color(shade, shade, shade, a));
                }
                File.WriteAllBytes(texPath, tex.EncodeToPNG());
                AssetDatabase.ImportAsset(texPath);
            }
            var m = KarmanSetup.Mat("PadSmoke", "Universal Render Pipeline/Particles/Unlit");
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
            m.SetFloat("_Surface", 1); // transparent
            m.SetFloat("_Blend", 0);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
