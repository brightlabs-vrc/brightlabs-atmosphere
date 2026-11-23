#if UNITY_EDITOR
// Copyright 2026 Ayase Minori and Bright Labs
// Licensed under the BSD-3-Clause License
// See LICENSE for details

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using BrightLabs.Vendor.Atmosphere;

namespace BrightLabs.Vendor.Atmosphere.Editor
{
    /// <summary>
    /// Generic editor setup for the screen-space weather overlay volume.
    /// Creates/configures a large inverted sphere with the weather overlay material,
    /// parented to an <see cref="AtmosphereController"/> when present.
    ///
    /// Nakayama-specific forecast board and particle wiring are intentionally excluded;
    /// see Samples~/NakayamaIntegration for reference.
    /// </summary>
    public static class WeatherOverlaySetup
    {
        public const string OverlayShaderName = "Nakayama/WeatherOverlay";
        public const string DefaultOverlayMaterialPath = "Assets/Materials/WeatherOverlay.mat";
        public const string OverlayObjectName = "WeatherOverlayVolume";

        [MenuItem("BrightLabs/Atmosphere/Setup Weather Overlay Volume", false, 10)]
        public static void Setup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying)
            {
                return;
            }

            AtmosphereController atmosphere = Object.FindObjectOfType<AtmosphereController>(true);
            EnsureWeatherOverlayVolume(atmosphere, DefaultOverlayMaterialPath);
            if (!Application.isBatchMode)
            {
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            }
            Debug.Log("[BrightLabs.Vendor.Atmosphere] Configured WeatherOverlayVolume.");
        }

        public static GameObject EnsureWeatherOverlayVolume(AtmosphereController atmosphere, string materialPath)
        {
            Shader overlayShader = Shader.Find(OverlayShaderName);
            if (overlayShader == null)
            {
                Debug.LogWarning("[BrightLabs.Vendor.Atmosphere] Shader '" + OverlayShaderName + "' not found; skipping WeatherOverlayVolume setup.");
                return null;
            }

            Material overlayMat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (overlayMat == null)
            {
                string dir = System.IO.Path.GetDirectoryName(materialPath);
                if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
                {
                    EnsureFolder(dir);
                }

                overlayMat = new Material(overlayShader);
                overlayMat.name = "WeatherOverlay";
                AssetDatabase.CreateAsset(overlayMat, materialPath);
            }
            else if (overlayMat.shader != overlayShader)
            {
                overlayMat.shader = overlayShader;
                EditorUtility.SetDirty(overlayMat);
            }

            Transform parentTr = atmosphere != null ? atmosphere.transform : null;
            GameObject volumeGO = GameObject.Find(OverlayObjectName);
            if (volumeGO == null)
            {
                volumeGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                volumeGO.name = OverlayObjectName;
                Undo.RegisterCreatedObjectUndo(volumeGO, "Create WeatherOverlayVolume");
            }

            if (parentTr != null && volumeGO.transform.parent != parentTr)
            {
                volumeGO.transform.SetParent(parentTr, false);
            }

            volumeGO.layer = 0;
            volumeGO.transform.localPosition = Vector3.zero;
            volumeGO.transform.localRotation = Quaternion.identity;
            volumeGO.transform.localScale = new Vector3(3000f, 3000f, 3000f);

            Collider col = volumeGO.GetComponent<Collider>();
            if (col != null)
            {
                Object.DestroyImmediate(col);
            }

            MeshRenderer mr = volumeGO.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = overlayMat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                mr.allowOcclusionWhenDynamic = false;
                EditorUtility.SetDirty(mr);
            }

            return volumeGO;
        }

        private static void EnsureFolder(string assetFolderPath)
        {
            if (AssetDatabase.IsValidFolder(assetFolderPath)) return;
            string normalized = assetFolderPath.Replace("\\", "/");
            string[] parts = normalized.Split('/');
            if (parts.Length == 0) return;
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }
    }
}
#endif
