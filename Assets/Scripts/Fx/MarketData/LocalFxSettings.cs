using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TestFXTrade.Fx.MarketData
{
    public sealed class LocalFxSettings
    {
        public const string ApiKeyVariableName = "TWELVE_DATA_API_KEY";
        public const string OpenAiApiKeyVariableName = "OPENAI_API_KEY";

        public LocalFxSettings(string apiKey, string sourceLabel)
            : this(apiKey, sourceLabel, string.Empty, "local .env")
        {
        }

        public LocalFxSettings(string apiKey, string sourceLabel, string openAiApiKey, string openAiSourceLabel)
        {
            ApiKey = apiKey ?? string.Empty;
            SourceLabel = sourceLabel ?? string.Empty;
            OpenAiApiKey = openAiApiKey ?? string.Empty;
            OpenAiSourceLabel = openAiSourceLabel ?? string.Empty;
        }

        public string ApiKey { get; }

        public string SourceLabel { get; }

        public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

        public string OpenAiApiKey { get; }

        public string OpenAiSourceLabel { get; }

        public bool HasOpenAiApiKey => !string.IsNullOrWhiteSpace(OpenAiApiKey);

        public static LocalFxSettings Load()
        {
            string marketApiKey = string.Empty;
            string marketSourceLabel = "local .env";
            string openAiApiKey = string.Empty;
            string openAiSourceLabel = "local .env";

            foreach (EnvCandidate candidate in GetCandidateEnvPaths())
            {
                if (!File.Exists(candidate.Path))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(marketApiKey))
                {
                    marketApiKey = ReadEnvValue(candidate.Path, ApiKeyVariableName);
                    if (!string.IsNullOrWhiteSpace(marketApiKey))
                    {
                        marketSourceLabel = candidate.Label;
                    }
                }

                if (string.IsNullOrWhiteSpace(openAiApiKey))
                {
                    openAiApiKey = ReadEnvValue(candidate.Path, OpenAiApiKeyVariableName);
                    if (!string.IsNullOrWhiteSpace(openAiApiKey))
                    {
                        openAiSourceLabel = candidate.Label;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(marketApiKey))
            {
                marketApiKey = Environment.GetEnvironmentVariable(ApiKeyVariableName);
                if (!string.IsNullOrWhiteSpace(marketApiKey))
                {
                    marketSourceLabel = "environment variable";
                }
            }

            if (string.IsNullOrWhiteSpace(openAiApiKey))
            {
                openAiApiKey = Environment.GetEnvironmentVariable(OpenAiApiKeyVariableName);
                if (!string.IsNullOrWhiteSpace(openAiApiKey))
                {
                    openAiSourceLabel = "environment variable";
                }
            }

            return new LocalFxSettings(marketApiKey, marketSourceLabel, openAiApiKey, openAiSourceLabel);
        }

        private static IEnumerable<EnvCandidate> GetCandidateEnvPaths()
        {
            List<EnvCandidate> paths = new List<EnvCandidate>();
            AddCandidate(paths, Path.Combine(GetProjectRootPath(), ".env"), "project .env");
            AddCandidate(paths, Path.Combine(Directory.GetCurrentDirectory(), ".env"), "current directory .env");
            AddCandidate(paths, Path.Combine(Application.persistentDataPath, ".env"), "persistent .env");
            return paths;
        }

        private static string GetProjectRootPath()
        {
            string dataPath = Application.dataPath;
            if (string.IsNullOrWhiteSpace(dataPath))
            {
                return Directory.GetCurrentDirectory();
            }

            DirectoryInfo assetsDirectory = Directory.GetParent(dataPath);
            return assetsDirectory == null ? Directory.GetCurrentDirectory() : assetsDirectory.FullName;
        }

        private static void AddCandidate(List<EnvCandidate> paths, string path, string label)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception)
            {
                return;
            }

            for (int i = 0; i < paths.Count; i++)
            {
                if (string.Equals(paths[i].Path, fullPath, StringComparison.Ordinal))
                {
                    return;
                }
            }

            paths.Add(new EnvCandidate(fullPath, label));
        }

        private readonly struct EnvCandidate
        {
            public EnvCandidate(string path, string label)
            {
                Path = path;
                Label = label;
            }

            public string Path { get; }

            public string Label { get; }
        }

        private static string ReadEnvValue(string path, string variableName)
        {
            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.StartsWith("export ", StringComparison.Ordinal))
                {
                    line = line.Substring("export ".Length).Trim();
                }

                int separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, separatorIndex).Trim();
                if (!string.Equals(key, variableName, StringComparison.Ordinal))
                {
                    continue;
                }

                string value = line.Substring(separatorIndex + 1).Trim();
                return TrimQuotes(value);
            }

            return string.Empty;
        }

        private static string TrimQuotes(string value)
        {
            if (value.Length >= 2)
            {
                char first = value[0];
                char last = value[value.Length - 1];
                if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
                {
                    return value.Substring(1, value.Length - 2);
                }
            }

            return value;
        }
    }
}
