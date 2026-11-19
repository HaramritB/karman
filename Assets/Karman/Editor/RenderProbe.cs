using System.IO;
using Karman.Core.Vehicles;
using Karman.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Karman.EditorTools
{
    /// <summary>Batch-mode look-dev renders of single parts (Karman ▸ Render Part Probe).</summary>
    public static class RenderProbe
    {
        [MenuItem("Karman/Render Part Probe")]
        public static void Run()
        {
            EditorSettings.asyncShaderCompilation = false;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cat = AssetDatabase.LoadAssetAtPath<PartModelCatalog>("Assets/Karman/Settings/PartModelCatalog.asset");
            var lib = PartLibrary.Lookup(PartLibrary.CreateStarter());
            var root = new GameObject("Probe").transform;
            string[] ids = { "tank.kerolox.37x8", "nose.37", "engine.kr1v", "engine.kr1.cluster9", "decoupler.37" };
            float x = 0;
            foreach (var id in ids)
            {
                var go = cat.Create(lib(id), root);
                go.transform.localPosition = new Vector3(x, 0, 0);
                x += 6;
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                    foreach (var m in r.sharedMaterials) Debug.Log($"[Probe] {id} renderer {r.name} material {m?.name} shader {m?.shader?.name} bounds {r.bounds.size}");
                var mf = go.GetComponentInChildren<MeshFilter>();
                if (mf) { var mesh = mf.sharedMesh; Debug.Log($"[Probe] {id} mesh verts {mesh.vertexCount} normals {mesh.normals.Length} n0 {(mesh.normals.Length > 0 ? mesh.normals[0].ToString() : "-")} lossyScale {mf.transform.lossyScale}"); }
            }
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.3f, 0.3f, 0.35f);
            var light = new GameObject("Sun").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.1f; light.transform.rotation = Quaternion.Euler(35, -40, 0);
            var cam = new GameObject("Cam").AddComponent<Camera>(); cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            cam.transform.position = new Vector3(12, 3, -28); cam.transform.LookAt(new Vector3(12, 0, 0)); cam.backgroundColor = new Color(0.2f, 0.22f, 0.25f); cam.clearFlags = CameraClearFlags.SolidColor;
            var rt = new RenderTexture(1600, 900, 24); cam.targetTexture = rt;
            var refSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere); refSphere.transform.position = new Vector3(30, 0, 0); refSphere.transform.localScale = Vector3.one * 3;
            var lit = new Material(Shader.Find("Universal Render Pipeline/Lit")); lit.SetColor("_BaseColor", Color.red); refSphere.GetComponent<Renderer>().sharedMaterial = lit;
            var soot = AssetDatabase.LoadAssetAtPath<Material>("Assets/Karman/Materials/Part_Soot.mat");
            var s2 = GameObject.CreatePrimitive(PrimitiveType.Sphere); s2.transform.position = new Vector3(-6, 0, 0); s2.transform.localScale = Vector3.one * 3; s2.GetComponent<Renderer>().sharedMaterial = soot;
            Debug.Log($"[Probe] pipeline {GraphicsSettings.currentRenderPipeline?.name} soot {soot?.GetColor("_BaseColor")}");
            for (int k = 0; k < 3; k++) cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply();
            Directory.CreateDirectory("Captures");
            File.WriteAllBytes("Captures/part_probe.png", tex.EncodeToPNG());
            Debug.Log("[Probe] saved");
        }
    }
}
