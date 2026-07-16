using System;
using System.IO;
using System.Reflection;
using TestFXTrade.Fx.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace TestFXTrade.Editor
{
    public static class FxCanvasSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string CanvasName = "USDJPY Advisor Canvas";
        private const string PageFolderPath = "Assets/Resources/Pages";
        private const string PagePrefabPath = PageFolderPath + "/FxTradeAdvisorPage.prefab";
        private const string PageName = "FxTradeAdvisorPage";
        private const string SourceFontPath = "Assets/Resources/Fonts/NotoSansSC-Regular.otf";
        private const string SceneFontPath = "Assets/Resources/Fonts/NotoSansSC-Regular SDF.asset";
        private const string SettingsIconPath = "Assets/setting_17909217.png";
        private const string PreviewPath = "Logs/FxTradeAdvisorPagePreview.png";
        private const string LoadingPreviewPath = "Logs/FxTradeAdvisorLoadingPreview.png";
        private const string SettingsPreviewPath = "Logs/FxTradeAdvisorSettingsPreview.png";

        [MenuItem("Tools/FX Trade/Rebuild Advisor Page Prefab")]
        public static void RebuildAdvisorPagePrefab()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            RemoveExistingAdvisorUi();
            EnsurePageFolder();

            GameObject pageObject = new GameObject(
                PageName,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            FxTradeAdvisorApp app = pageObject.AddComponent<FxTradeAdvisorApp>();
            TMP_FontAsset sceneFont = LoadOrCreateSceneFont();
            Sprite settingsIcon = LoadSettingsIcon();

            FieldInfo fontField = typeof(FxTradeAdvisorApp).GetField(
                "font",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (fontField == null)
            {
                throw new MissingFieldException(typeof(FxTradeAdvisorApp).FullName, "font");
            }

            fontField.SetValue(app, sceneFont);

            FieldInfo settingsIconField = typeof(FxTradeAdvisorApp).GetField(
                "settingsIcon",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (settingsIconField == null)
            {
                throw new MissingFieldException(typeof(FxTradeAdvisorApp).FullName, "settingsIcon");
            }

            settingsIconField.SetValue(app, settingsIcon);

            MethodInfo buildUi = typeof(FxTradeAdvisorApp).GetMethod(
                "BuildUi",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (buildUi == null)
            {
                throw new MissingMethodException(typeof(FxTradeAdvisorApp).FullName, "BuildUi");
            }

            buildUi.Invoke(app, null);
            Canvas.ForceUpdateCanvases();

            EditorUtility.SetDirty(pageObject);
            EditorUtility.SetDirty(app);
            GameObject pagePrefab = PrefabUtility.SaveAsPrefabAsset(pageObject, PagePrefabPath, out bool saved);
            if (!saved || pagePrefab == null)
            {
                throw new InvalidOperationException($"Could not save the UI page prefab at {PagePrefabPath}.");
            }

            UnityEngine.Object.DestroyImmediate(pageObject);
            RemoveGeneratedEventSystem();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new InvalidOperationException($"Could not save the page-free scene at {ScenePath}.");
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"Rebuilt dynamic uGUI page prefab at {PagePrefabPath} and removed it from {ScenePath}.");
        }

        [MenuItem("Tools/FX Trade/Render Advisor Page Previews")]
        public static void RenderAdvisorPagePreviews()
        {
            GameObject pagePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PagePrefabPath);
            if (pagePrefab == null)
            {
                throw new InvalidOperationException($"Could not load the page prefab at {PagePrefabPath}.");
            }

            GameObject page = PrefabUtility.InstantiatePrefab(pagePrefab) as GameObject;
            GameObject cameraObject = new GameObject("Advisor Preview Camera", typeof(Camera));
            Camera camera = cameraObject.GetComponent<Camera>();
            RenderTexture previewTexture = new RenderTexture(780, 1688, 24, RenderTextureFormat.ARGB32);
            RenderTexture previousActive = RenderTexture.active;

            try
            {
                Canvas canvas = page.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1f;
                canvas.pixelPerfect = true;

                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color32(10, 14, 20, 255);
                camera.orthographic = true;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = 10f;
                camera.targetTexture = previewTexture;

                previewTexture.Create();
                Canvas.ForceUpdateCanvases();
                string previewPath = ResolveProjectPath(PreviewPath);
                string loadingPreviewPath = ResolveProjectPath(LoadingPreviewPath);
                string settingsPreviewPath = ResolveProjectPath(SettingsPreviewPath);
                RenderPreview(camera, previewTexture, previewPath);

                Transform loadingIndicator = page.transform.Find(
                    "Root/Safe Area Content/Header Card/Header Bar/Header Actions/Loading Indicator");
                if (loadingIndicator == null)
                {
                    throw new InvalidOperationException("The page prefab does not contain the loading indicator.");
                }

                loadingIndicator.gameObject.SetActive(true);
                LayoutRebuilder.ForceRebuildLayoutImmediate(loadingIndicator.parent as RectTransform);
                Canvas.ForceUpdateCanvases();
                RenderPreview(camera, previewTexture, loadingPreviewPath);
                loadingIndicator.gameObject.SetActive(false);

                Transform settingsOverlay = page.transform.Find("Root/Settings Overlay");
                if (settingsOverlay == null)
                {
                    throw new InvalidOperationException("The page prefab does not contain the settings overlay.");
                }

                settingsOverlay.gameObject.SetActive(true);
                Canvas.ForceUpdateCanvases();
                RenderPreview(camera, previewTexture, settingsPreviewPath);
                Debug.Log(
                    $"Rendered advisor page previews to {previewPath}, {loadingPreviewPath}, " +
                    $"and {settingsPreviewPath}.");
            }
            finally
            {
                RenderTexture.active = previousActive;
                previewTexture.Release();
                UnityEngine.Object.DestroyImmediate(previewTexture);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(page);
            }
        }

        private static void RenderPreview(Camera camera, RenderTexture renderTexture, string path)
        {
            camera.Render();
            RenderTexture.active = renderTexture;
            Texture2D image = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBA32, false);
            try
            {
                image.ReadPixels(new Rect(0f, 0f, renderTexture.width, renderTexture.height), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(image);
            }
        }

        private static string ResolveProjectPath(string relativePath)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", relativePath));
        }

        private static void EnsurePageFolder()
        {
            if (!AssetDatabase.IsValidFolder(PageFolderPath))
            {
                AssetDatabase.CreateFolder("Assets/Resources", "Pages");
            }
        }

        private static TMP_FontAsset LoadOrCreateSceneFont()
        {
            TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SceneFontPath);
            if (fontAsset != null)
            {
                return fontAsset;
            }

            Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
            if (sourceFont == null)
            {
                throw new InvalidOperationException($"Could not load the Canvas source font at {SourceFontPath}.");
            }

            fontAsset = TMP_FontAsset.CreateFontAsset(
                sourceFont,
                90,
                9,
                GlyphRenderMode.SDFAA,
                1024,
                1024,
                AtlasPopulationMode.Dynamic,
                true);
            if (fontAsset == null)
            {
                throw new InvalidOperationException("TextMesh Pro could not create the Canvas font asset.");
            }

            fontAsset.name = "NotoSansSC Regular SDF";
            AssetDatabase.CreateAsset(fontAsset, SceneFontPath);

            Texture2D[] atlasTextures = fontAsset.atlasTextures;
            for (int i = 0; i < atlasTextures.Length; i++)
            {
                if (atlasTextures[i] != null)
                {
                    AssetDatabase.AddObjectToAsset(atlasTextures[i], fontAsset);
                }
            }

            if (fontAsset.material != null)
            {
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            }

            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();
            return fontAsset;
        }

        private static Sprite LoadSettingsIcon()
        {
            UnityEngine.Object[] iconAssets = AssetDatabase.LoadAllAssetsAtPath(SettingsIconPath);
            for (int i = 0; i < iconAssets.Length; i++)
            {
                if (iconAssets[i] is Sprite sprite)
                {
                    return sprite;
                }
            }

            throw new InvalidOperationException($"Could not load the settings icon at {SettingsIconPath}.");
        }

        private static void RemoveExistingAdvisorUi()
        {
            FxTradeAdvisorApp[] apps = UnityEngine.Object.FindObjectsByType<FxTradeAdvisorApp>(
                FindObjectsInactive.Include);
            for (int i = 0; i < apps.Length; i++)
            {
                if (apps[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(apps[i].gameObject);
                }
            }

            GameObject canvas = GameObject.Find(CanvasName);
            if (canvas != null)
            {
                UnityEngine.Object.DestroyImmediate(canvas);
            }

            RemoveGeneratedEventSystem();
        }

        private static void RemoveGeneratedEventSystem()
        {
            EventSystem eventSystem = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
            if (eventSystem != null && eventSystem.gameObject.name == "EventSystem")
            {
                UnityEngine.Object.DestroyImmediate(eventSystem.gameObject);
            }
        }
    }
}
