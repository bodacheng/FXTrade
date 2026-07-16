using System;
using System.IO;
using System.Reflection;
using TestFXTrade.Fx.UI;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AddressableAssets;
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
        private const string AddressableUiFolderPath = "Assets/UI/Addressable";
        private const string SettingsWindowPrefabPath = AddressableUiFolderPath + "/FxTradeSettingsWindow.prefab";
        private const string LanguageWindowPrefabPath = AddressableUiFolderPath + "/FxTradeLanguageWindow.prefab";
        private const string UsageGuideWindowPrefabPath = AddressableUiFolderPath + "/FxTradeUsageGuideWindow.prefab";
        private const string AdviceWindowPrefabPath = AddressableUiFolderPath + "/FxTradeAdviceWindow.prefab";
        private const string AddressableUiGroupName = "FX Trade UI";
        private const string AddressableUiLabel = "FXTradeUI";
        private const string PageName = "FxTradeAdvisorPage";
        private const string SourceFontPath = "Assets/Resources/Fonts/NotoSansSC-Regular.otf";
        private const string SceneFontPath = "Assets/Resources/Fonts/NotoSansSC-Regular SDF.asset";
        private const string SettingsIconPath = "Assets/Sprite/setting_17909217.png";
        private const string UsageGuideIconPath = "Assets/Sprite/question-mark-symbol-isolated-on-transparent_68186040.png";
        private const string PreviewPath = "Logs/FxTradeAdvisorPagePreview.png";
        private const string LoadingPreviewPath = "Logs/FxTradeAdvisorLoadingPreview.png";
        private const string SettingsPreviewPath = "Logs/FxTradeAdvisorSettingsPreview.png";
        private const string LanguagePreviewPath = "Logs/FxTradeAdvisorLanguagePreview.png";
        private const string UsageGuidePreviewPath = "Logs/FxTradeAdvisorUsageGuidePreview.png";
        private const string AdvicePreviewPath = "Logs/FxTradeAdvisorAdvicePreview.png";

        [MenuItem("Tools/FX Trade/Rebuild Advisor Page Prefab")]
        public static void RebuildAdvisorPagePrefab()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            RemoveExistingAdvisorUi();
            EnsurePageFolder();
            EnsureAddressableUiFolder();

            GameObject pageObject = new GameObject(
                PageName,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            FxTradeAdvisorApp app = pageObject.AddComponent<FxTradeAdvisorApp>();
            TMP_FontAsset sceneFont = LoadOrCreateSceneFont();
            Sprite settingsIcon = LoadSettingsIcon();
            Sprite usageGuideIcon = LoadUsageGuideIcon();

            SetPrivateField(app, "font", sceneFont);
            SetPrivateField(app, "settingsIcon", settingsIcon);
            SetPrivateField(app, "usageGuideIcon", usageGuideIcon);

            GameObject windowBuilderRoot = new GameObject("Addressable UI Builder Root", typeof(RectTransform));
            try
            {
                BuildAndSaveWindowPrefab(app, windowBuilderRoot.transform, "BuildSettingsOverlay", SettingsWindowPrefabPath);
                BuildAndSaveWindowPrefab(app, windowBuilderRoot.transform, "BuildLanguageSettingsOverlay", LanguageWindowPrefabPath);
                BuildAndSaveWindowPrefab(app, windowBuilderRoot.transform, "BuildUsageGuideOverlay", UsageGuideWindowPrefabPath);
                BuildAndSaveWindowPrefab(app, windowBuilderRoot.transform, "BuildAdviceOverlay", AdviceWindowPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(windowBuilderRoot);
            }

            ConfigureAddressableUiGroup();
            SetPrivateField(
                app,
                "settingsWindowPrefab",
                new AssetReferenceGameObject(AssetDatabase.AssetPathToGUID(SettingsWindowPrefabPath)));
            SetPrivateField(
                app,
                "languageWindowPrefab",
                new AssetReferenceGameObject(AssetDatabase.AssetPathToGUID(LanguageWindowPrefabPath)));
            SetPrivateField(
                app,
                "usageGuideWindowPrefab",
                new AssetReferenceGameObject(AssetDatabase.AssetPathToGUID(UsageGuideWindowPrefabPath)));
            SetPrivateField(
                app,
                "adviceWindowPrefab",
                new AssetReferenceGameObject(AssetDatabase.AssetPathToGUID(AdviceWindowPrefabPath)));

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
            Debug.Log(
                $"Rebuilt {PagePrefabPath} plus four Addressable UI window prefabs in group " +
                $"'{AddressableUiGroupName}', and removed scene-owned UI from {ScenePath}.");
        }

        [MenuItem("Tools/FX Trade/Build Addressable UI Content")]
        public static void BuildAddressableUiContent()
        {
            ConfigureAddressableUiGroup();
            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
            if (result == null || !string.IsNullOrWhiteSpace(result.Error))
            {
                throw new InvalidOperationException(
                    result == null ? "Addressables returned no build result." : result.Error);
            }

            Debug.Log(
                $"Built Addressable UI content with {result.LocationCount} locations to {result.OutputPath}.");
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
                string languagePreviewPath = ResolveProjectPath(LanguagePreviewPath);
                string usageGuidePreviewPath = ResolveProjectPath(UsageGuidePreviewPath);
                string advicePreviewPath = ResolveProjectPath(AdvicePreviewPath);
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

                Transform windowParent = page.transform.Find("Root");
                if (windowParent == null)
                {
                    throw new InvalidOperationException("The page prefab does not contain its UI root.");
                }

                RenderWindowPreview(
                    camera,
                    previewTexture,
                    windowParent,
                    SettingsWindowPrefabPath,
                    settingsPreviewPath);
                RenderWindowPreview(
                    camera,
                    previewTexture,
                    windowParent,
                    LanguageWindowPrefabPath,
                    languagePreviewPath);
                RenderWindowPreview(
                    camera,
                    previewTexture,
                    windowParent,
                    UsageGuideWindowPrefabPath,
                    usageGuidePreviewPath);
                RenderWindowPreview(
                    camera,
                    previewTexture,
                    windowParent,
                    AdviceWindowPrefabPath,
                    advicePreviewPath);

                Debug.Log(
                    $"Rendered advisor page previews to {previewPath}, {loadingPreviewPath}, " +
                    $"{settingsPreviewPath}, {languagePreviewPath}, {usageGuidePreviewPath}, " +
                    $"and {advicePreviewPath}.");
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

        private static void RenderWindowPreview(
            Camera camera,
            RenderTexture renderTexture,
            Transform parent,
            string prefabPath,
            string outputPath)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException($"Could not load Addressable UI prefab at {prefabPath}.");
            }

            GameObject instance = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException($"Could not instantiate Addressable UI prefab at {prefabPath}.");
            }

            try
            {
                instance.transform.SetAsLastSibling();
                Canvas.ForceUpdateCanvases();
                RenderPreview(camera, renderTexture, outputPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void RenderPreview(Camera camera, RenderTexture renderTexture, string path)
        {
            Graphic[] graphics = UnityEngine.Object.FindObjectsByType<Graphic>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < graphics.Length; i++)
            {
                graphics[i].SetAllDirty();
            }

            Canvas.ForceUpdateCanvases();
            camera.Render();
            Canvas.ForceUpdateCanvases();
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

        private static void EnsureAddressableUiFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/UI"))
            {
                AssetDatabase.CreateFolder("Assets", "UI");
            }

            if (!AssetDatabase.IsValidFolder(AddressableUiFolderPath))
            {
                AssetDatabase.CreateFolder("Assets/UI", "Addressable");
            }
        }

        private static void BuildAndSaveWindowPrefab(
            FxTradeAdvisorApp app,
            Transform builderRoot,
            string buildMethodName,
            string prefabPath)
        {
            MethodInfo buildMethod = typeof(FxTradeAdvisorApp).GetMethod(
                buildMethodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (buildMethod == null)
            {
                throw new MissingMethodException(typeof(FxTradeAdvisorApp).FullName, buildMethodName);
            }

            int previousChildCount = builderRoot.childCount;
            buildMethod.Invoke(app, new object[] { builderRoot });
            if (builderRoot.childCount != previousChildCount + 1)
            {
                throw new InvalidOperationException($"{buildMethodName} did not create exactly one UI window.");
            }

            GameObject window = builderRoot.GetChild(previousChildCount).gameObject;
            try
            {
                MethodInfo bindTexts = typeof(FxTradeAdvisorApp).GetMethod(
                    "BindStaticUiTexts",
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (bindTexts == null)
                {
                    throw new MissingMethodException(typeof(FxTradeAdvisorApp).FullName, "BindStaticUiTexts");
                }

                bindTexts.Invoke(null, new object[] { window.transform });
                Canvas.ForceUpdateCanvases();
                EditorUtility.SetDirty(window);
                GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(window, prefabPath, out bool saved);
                if (!saved || savedPrefab == null)
                {
                    throw new InvalidOperationException($"Could not save Addressable UI prefab at {prefabPath}.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        private static void ConfigureAddressableUiGroup()
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                throw new InvalidOperationException("The project does not contain Addressable Asset settings.");
            }

            AddressableAssetGroup group = settings.FindGroup(AddressableUiGroupName);
            if (group == null)
            {
                group = settings.CreateGroup(
                    AddressableUiGroupName,
                    false,
                    false,
                    false,
                    null,
                    typeof(ContentUpdateGroupSchema),
                    typeof(BundledAssetGroupSchema));
            }

            BundledAssetGroupSchema bundledSchema = group.GetSchema<BundledAssetGroupSchema>();
            if (bundledSchema == null)
            {
                bundledSchema = group.AddSchema<BundledAssetGroupSchema>(false);
            }

            if (group.GetSchema<ContentUpdateGroupSchema>() == null)
            {
                group.AddSchema<ContentUpdateGroupSchema>(false);
            }

            bundledSchema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kLocalBuildPath);
            bundledSchema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kLocalLoadPath);
            bundledSchema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
            settings.AddLabel(AddressableUiLabel, false);

            ConfigureAddressableEntry(
                settings,
                group,
                SettingsWindowPrefabPath,
                FxTradeAdvisorApp.SettingsWindowAddress);
            ConfigureAddressableEntry(
                settings,
                group,
                LanguageWindowPrefabPath,
                FxTradeAdvisorApp.LanguageWindowAddress);
            ConfigureAddressableEntry(
                settings,
                group,
                UsageGuideWindowPrefabPath,
                FxTradeAdvisorApp.UsageGuideWindowAddress);
            ConfigureAddressableEntry(
                settings,
                group,
                AdviceWindowPrefabPath,
                FxTradeAdvisorApp.AdviceWindowAddress);

            EditorUtility.SetDirty(bundledSchema);
            EditorUtility.SetDirty(group);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        private static void ConfigureAddressableEntry(
            AddressableAssetSettings settings,
            AddressableAssetGroup group,
            string assetPath,
            string address)
        {
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrWhiteSpace(guid))
            {
                throw new InvalidOperationException($"Could not resolve an asset GUID for {assetPath}.");
            }

            AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group, false, false);
            entry.SetAddress(address, false);
            entry.SetLabel(AddressableUiLabel, true, true, false);
        }

        private static void SetPrivateField(FxTradeAdvisorApp app, string fieldName, object value)
        {
            FieldInfo field = typeof(FxTradeAdvisorApp).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException(typeof(FxTradeAdvisorApp).FullName, fieldName);
            }

            field.SetValue(app, value);
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
            return LoadIconSprite(SettingsIconPath);
        }

        private static Sprite LoadUsageGuideIcon()
        {
            AssetDatabase.ImportAsset(UsageGuideIconPath, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(UsageGuideIconPath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException($"Could not import the toolbar icon at {UsageGuideIconPath}.");
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 512;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(UsageGuideIconPath);
            if (sprite == null)
            {
                throw new InvalidOperationException($"Could not load the toolbar icon at {UsageGuideIconPath}.");
            }

            return sprite;
        }

        private static Sprite LoadIconSprite(string assetPath)
        {
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            UnityEngine.Object[] iconAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            for (int i = 0; i < iconAssets.Length; i++)
            {
                if (iconAssets[i] is Sprite sprite)
                {
                    return sprite;
                }
            }

            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException($"Could not import the toolbar icon at {assetPath}.");
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 512;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();

            Sprite importedSprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (importedSprite == null)
            {
                throw new InvalidOperationException($"Could not load the toolbar icon at {assetPath}.");
            }

            return importedSprite;
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
