using UnityEditor;
using System.IO;
using UnityEngine;
using System.Linq;
using UnityEditor.Compilation;

namespace Translator
{
    public static class TranslatorManager
    {
        private const string ConfigPath = "Assets/Editor/Translator.conf";

        [MenuItem("Translator/TranslateAll")]
        private static void TranslateAll()
        {
            var config = GetConfig();
            string[] paths = Directory.GetFiles(config.SourcePath, "*.compute", SearchOption.AllDirectories)
                .Where(p => !p.Contains("Shaders"))
                .ToArray();

            foreach (var path in paths)
            {
                string assetPath = path.Replace('\\', '/');

                string source = File.ReadAllText(assetPath);

                if (source.StartsWith("#translate"))
                {
                    string translated = HLSLTranslator.Translate(source, config);

                    if (translated == null)
                        Debug.LogWarning($"Translator manager: {assetPath} is empty");

                    string outputPath = Path.Combine(config.TargetPath, Path.GetFileName(assetPath));

                    File.WriteAllText(outputPath, translated);
                    Debug.Log($"Translator manager: translated {assetPath}");
                }
            }
        }

        [InitializeOnLoad]
        public static class HlslTranslatorStartup
        {
            [System.Obsolete]
            static HlslTranslatorStartup()
            {
                CompilationPipeline.assemblyCompilationStarted += _ => TranslateAll();
            }
        }

        private static TranslatorConfig GetConfig()
        {
            if (!File.Exists(ConfigPath))
            {
                Debug.LogError("Translator: no config found");
                return new TranslatorConfig();
            }

            string json = File.ReadAllText(ConfigPath);
            return JsonUtility.FromJson<TranslatorConfig>(json);
        }
    }

    public class TranslatorConfig
    {
        public string SourcePath;
        public string TargetPath;
        public bool AddKernels;
        public int ThreadGroupX;
        public int ThreadGroupY;
        public int ThreadGroupZ;
    }
}