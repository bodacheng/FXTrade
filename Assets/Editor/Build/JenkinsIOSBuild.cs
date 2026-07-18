using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEditor.Build.Reporting;

namespace TestFXTrade.Editor.Build
{
    public static class JenkinsIOSBuild
    {
        private const string ProductName = "TestFXTrade";
        private const string BundleIdentifier = "com.BO.TestFXTrade";
        private const string AppleTeamId = "S88E744TXJ";
        private const string DefaultOutputPath = "build_ios/Export";
        private const string DefaultBundleVersion = "1.0";

        public static void BuildIOS()
        {
            string outputPath = ReadArgument("-OutputPath", DefaultOutputPath).Trim();
            if (string.IsNullOrEmpty(outputPath))
            {
                throw new ArgumentException("-OutputPath cannot be empty.");
            }

            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS))
            {
                throw new InvalidOperationException("Unable to switch the active build target to iOS.");
            }

            ApplyPlayerSettings();

            outputPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(outputPath);

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = GetEnabledScenes(),
                locationPathName = outputPath,
                target = BuildTarget.iOS,
                targetGroup = BuildTargetGroup.iOS,
                options = BuildOptions.StrictMode
            });

            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"iOS build failed: {summary.result}. Errors={summary.totalErrors}, Warnings={summary.totalWarnings}");
            }

            UnityEngine.Debug.Log($"iOS Xcode project exported to {outputPath}");
        }

        private static void ApplyPlayerSettings()
        {
            PlayerSettings.productName = ProductName;
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.iOS, BundleIdentifier);
            PlayerSettings.bundleVersion = ReadNonEmptyArgument("-bundleVersion", DefaultBundleVersion);
            PlayerSettings.iOS.buildNumber = ReadNonEmptyArgument("-buildNumber", "1");
            PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneOnly;
            PlayerSettings.iOS.appleDeveloperTeamID = AppleTeamId;
            PlayerSettings.iOS.appleEnableAutomaticSigning = true;
            PlayerSettings.iOS.iOSManualProvisioningProfileID = string.Empty;
            PlayerSettings.iOS.iOSManualProvisioningProfileType = ProvisioningProfileType.Distribution;
        }

        [PostProcessBuild]
        public static void ApplyIOSExportCompliance(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS)
            {
                return;
            }

            string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
            if (!File.Exists(plistPath))
            {
                throw new FileNotFoundException("Generated iOS Info.plist was not found.", plistPath);
            }

            PlistDocument plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            plist.root.SetBoolean("ITSAppUsesNonExemptEncryption", false);
            plist.WriteToFile(plistPath);
        }

        private static string[] GetEnabledScenes()
        {
            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToArray();

            if (scenes.Length == 0)
            {
                throw new InvalidOperationException("No enabled scenes found in EditorBuildSettings.");
            }

            return scenes;
        }

        private static string ReadNonEmptyArgument(string name, string defaultValue)
        {
            string value = ReadArgument(name, defaultValue).Trim();
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException($"{name} cannot be empty.");
            }

            return value;
        }

        private static string ReadArgument(string name, string defaultValue)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
                {
                    return i + 1 < args.Length ? args[i + 1] : defaultValue;
                }

                string prefix = name + "=";
                if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return arg.Substring(prefix.Length);
                }
            }

            return defaultValue;
        }
    }
}
