using UnityEditor;
using System.IO;
using UnityEngine;
using System.Linq;
using UnityEditor.Compilation;

namespace Translator
{
    public static class TranslatorManager
    {
        private const string ConfigPath = "Assets/HLSLTranslator/Translator.conf";
        private const string CodeStoreName = "SourceCodes";

        [MenuItem("Translator/TranslateAll")]
        public static void TranslateAllMenu()
        {
            TranslateAll();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            RestoreCode();
            // CompilationPipeline.RequestScriptCompilation();
        }

        public static string GetTranslatedVer(string path)
        {
            string assetPath = path.Replace('\\', '/');
            string source = File.ReadAllText(assetPath);

            if (!source.StartsWith("#translate"))
            {
                Debug.LogWarning($"Translator manager: trying to get translated version of non shlsl file at {path}");
                return null;
            }

            return HLSLTranslator.Translate(source, GetConfig());
        }

        public static void TranslateAll()
        {
            var config = GetConfig();
            string[] paths = Directory.GetFiles(config.SourcePath, "*.compute", SearchOption.AllDirectories).ToArray();

            File.WriteAllText(config.BackupPath, "");
            File.WriteAllText(config.TemporaryPath, "");

            foreach (var path in paths)
            {
                string assetPath = path.Replace('\\', '/');
                string source = File.ReadAllText(assetPath);

                if (source.StartsWith("#translate"))
                {
                    string currFiles = SessionState.GetString(CodeStoreName, "");
                    SessionState.SetString(CodeStoreName, currFiles + source + "||||" + path + "||||");
                    string translated = HLSLTranslator.Translate(source, config);

                    if (translated == null)
                        Debug.LogWarning($"Translator manager: {assetPath} is empty");

                    File.AppendAllText(config.BackupPath, source + System.Environment.NewLine + "\n---------------------------------------------\n\n");
                    File.AppendAllText(config.TemporaryPath, translated + System.Environment.NewLine + "\n----------------------------------------------\n\n");
                    File.WriteAllText(path, translated);
                }
            }
        }

        [UnityEditor.Callbacks.DidReloadScripts]
        public static void RestoreCode()
        {
            string txt = SessionState.GetString(CodeStoreName, null);

            if (txt.IsEmpty())
            {
                Debug.LogWarning("Translator manager: couldn't retrieve the source codes");
                SessionState.SetString(CodeStoreName, "");
                return;
            }

            string[] sourcesNPaths = txt.Split("||||");
            string[] sourceCodes = new string[(sourcesNPaths.Length + 1) / 2];
            string[] sourcePaths = new string[(sourcesNPaths.Length + 1) / 2];

            for (int i = 0; i < sourcesNPaths.Length; i++)
            {
                if (i % 2 == 0)
                    sourceCodes[i / 2] = sourcesNPaths[i];

                if (i % 2 == 1)
                    sourcePaths[i / 2] = sourcesNPaths[i];
            }

            for (int i = 0; i < sourceCodes.Length; i++)
            {
                if (!sourcePaths[i].IsEmpty() && sourceCodes[i].IsEmpty())
                    Debug.LogWarning($"Translator manager: no source code is retrieved for {sourcePaths[i]}");

                if (sourcePaths[i].IsEmpty() && !sourceCodes[i].IsEmpty())
                    Debug.LogWarning($"Translator manager: source code doesn't have a destination");

                else if (!sourcePaths[i].IsEmpty() && !sourceCodes[i].IsEmpty())
                    File.WriteAllText(sourcePaths[i], sourceCodes[i]);
            }

            SessionState.SetString(CodeStoreName, "");
        }

        [InitializeOnLoad]
        public static class HlslTranslatorStartup
        {
            [System.Obsolete]
            static HlslTranslatorStartup()
            {
                CompilationPipeline.compilationStarted += _ => TranslateAll();
                EditorApplication.playModeStateChanged += state =>
                {
                    if (state == PlayModeStateChange.ExitingEditMode)
                        TranslateAllMenu();
                };
            }
        }

        internal static TranslatorConfig GetConfig()
        {
            if (!File.Exists(ConfigPath))
            {
                Debug.LogError("Translator: no config found");
                return new TranslatorConfig();
            }

            string json = File.ReadAllText(ConfigPath);
            return JsonUtility.FromJson<TranslatorConfig>(json);
        }

        private static bool IsEmpty(this string str) => str == null || str == "";
    }

    public class TranslatorConfig
    {
        public string SourcePath;
        public string TargetPath;
        public string BackupPath;
        public string TemporaryPath;
        public bool AddKernels;
        public int ThreadGroupX;
        public int ThreadGroupY;
        public int ThreadGroupZ;
    }
}