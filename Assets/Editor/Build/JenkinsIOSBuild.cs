using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace TestFXTrade.Editor.Build
{
    public static class JenkinsIOSBuild
    {
        private const string OutputPathArgument = "-OutputPath";

        public static void BuildIOS()
        {
            string outputPath = ReadArgument(OutputPathArgument, "build_ios/Export");
            outputPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(outputPath);

            ApplyPlayerSettings();

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = GetEnabledScenes(),
                locationPathName = outputPath,
                target = BuildTarget.iOS,
                targetGroup = BuildTargetGroup.iOS,
                options = GetBuildOptions()
            };

            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS);

            BuildReport report = BuildPipeline.BuildPlayer(options);
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
            string productName = ReadArgument("-productName", string.Empty);
            if (!string.IsNullOrWhiteSpace(productName))
            {
                PlayerSettings.productName = productName.Trim();
            }

            string bundleIdentifier = ReadArgument("-bundleIdentifier", string.Empty);
            if (!string.IsNullOrWhiteSpace(bundleIdentifier))
            {
                PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.iOS, bundleIdentifier.Trim());
            }

            string bundleVersion = ReadArgument("-bundleVersion", string.Empty);
            if (!string.IsNullOrWhiteSpace(bundleVersion))
            {
                PlayerSettings.bundleVersion = bundleVersion.Trim();
            }

            string buildNumber = ReadArgument("-buildNumber", string.Empty);
            if (!string.IsNullOrWhiteSpace(buildNumber))
            {
                PlayerSettings.iOS.buildNumber = buildNumber.Trim();
            }

            ApplySigningSettings();
            AssetDatabase.SaveAssets();
        }

        private static void ApplySigningSettings()
        {
            bool automaticSigning = ReadBoolArgument("-automaticSigning", true);
            PlayerSettings.iOS.appleEnableAutomaticSigning = automaticSigning;

            string teamId = ReadArgument("-appleTeamId", string.Empty);
            if (!string.IsNullOrWhiteSpace(teamId))
            {
                PlayerSettings.iOS.appleDeveloperTeamID = teamId.Trim();
            }

            if (automaticSigning)
            {
                return;
            }

            string profileSpecifier = ReadArgument("-provisioningProfileSpecifier", string.Empty);
            if (!string.IsNullOrWhiteSpace(profileSpecifier))
            {
                PlayerSettings.iOS.iOSManualProvisioningProfileID = profileSpecifier.Trim();
            }

            string profileType = ReadArgument("-provisioningProfileType", "Distribution");
            PlayerSettings.iOS.iOSManualProvisioningProfileType =
                ParseProvisioningProfileType(profileType);
        }

        private static ProvisioningProfileType ParseProvisioningProfileType(string value)
        {
            if (string.Equals(value, "Development", StringComparison.OrdinalIgnoreCase))
            {
                return ProvisioningProfileType.Development;
            }

            return ProvisioningProfileType.Distribution;
        }

        private static BuildOptions GetBuildOptions()
        {
            BuildOptions options = BuildOptions.StrictMode;
            if (ReadBoolArgument("-developmentBuild", false))
            {
                options |= BuildOptions.Development | BuildOptions.AllowDebugging;
            }

            return options;
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

        private static bool ReadBoolArgument(string name, bool defaultValue)
        {
            string value = ReadArgument(name, defaultValue ? "true" : "false");
            return bool.TryParse(value, out bool parsed) ? parsed : defaultValue;
        }
    }
}
