using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Translator
{
    public static class HLSLTranslator
    {
        private static readonly HashSet<string> dataTypes = new()
        {
            // scalars
            "float", "int", "uint", "bool", "double", "half",
            // vectors
            "float2", "float3", "float4",
            "int2",   "int3",   "int4",
            "uint2",  "uint3",  "uint4",
            "bool2",  "bool3",  "bool4",
            // matrices
            "float2x2", "float3x3", "float4x4",
            // buffers
            "RWStructuredBuffer", "StructuredBuffer",
            "RWTexture2D", "Texture2D", "Texture3D",
            "RWBuffer", "Buffer",
            // other
            "SamplerState", "cbuffer",
        };

        private static readonly HashSet<string> bufferTypes = new()
        {
            "RWStructuredBuffer", "StructuredBuffer",
            "RWTexture2D", "Texture2D", "Texture3D",
            "RWBuffer", "Buffer",
        };

        private static TranslatorConfig conf;

        public static string Translate(string sourceCode, TranslatorConfig config)
        {
            conf = config;

            List<string> kernels = new();
            StringBuilder sb = new();

            int insideBrackets = 0;
            string indexer = "";

            string[] lines = sourceCode.Split('\n')[1..];
            if (lines.Length == 0) return null;
            string line = "";

            for (int i = 0; i < lines.Length; i++)
            {
                line = lines[i];
                line = line.Trim();

                if (line.StartsWith("//"))
                    continue;

                if (line == "")
                {
                    sb.Append("\n");
                    continue;
                }

                // kernels and dependencies
                if (line.StartsWith("#"))
                {
                    if (line.Contains("pragma kernel"))
                    {
                        kernels.Add(line.Split(' ').Last());
                        continue;
                    }

                    sb.Append(line, insideBrackets);
                    continue;
                }

                // handle brackets
                if (line.Contains("{"))
                {
                    sb.Append(line, insideBrackets);
                    insideBrackets++;
                    continue;
                }

                if (line.Contains("}"))
                {
                    insideBrackets--;

                    if (insideBrackets < 0)
                        Debug.LogError($"Translator: unexpected '}}' at source line {i + 1} — bracket mismatch");

                    sb.Append(line, insideBrackets);
                    continue;
                }

                if (insideBrackets == 0)
                {
                    // variables
                    (string, bool) result = HandleVariables(line);
                    if (result.Item2)
                    {
                        if (line.EndsWith("="))
                        {
                            sb.Append(line, insideBrackets);
                            int c = 1;

                            while (!line.Contains("}"))
                            {
                                if (i + c >= lines.Length)
                                {
                                    Debug.LogError($"Translator: unclosed array initializer starting at source line {i + 1}");
                                    break;
                                }
                                line = lines[i + c];
                                sb.Append(line, insideBrackets);
                                c++;
                            }

                            i += c - 1;
                            continue;
                        }

                        else
                        {
                            if (result.Item2)
                            {
                                sb.Append(result.Item1, insideBrackets);
                                continue;
                            }
                        }
                    }

                    // functions
                    if (ContainsDatatype(line) && !IsVariable(line) && line.Contains("=>"))
                    {
                        string func = line[..(line.IndexOf(')') + 1)] + "\n";
                        func += "{\n";
                        func += "    " + line[(line.IndexOf("=>") + 3)..];
                        func += "\n}\n";
                        sb.Append(func);
                        continue;
                    }

                    //kernels
                    if (line.Contains("threads"))
                    {
                        if (i + 1 >= lines.Length)
                        {
                            Debug.LogError($"Translator: kernel declaration at source line {i + 1} has no following function line");
                            continue;
                        }
                        line = HandleKernles(line, lines[i + 1], kernels, ref indexer, ref insideBrackets);
                        i += 2;
                        sb.Append(line);
                        continue;
                    }
                }

                if (insideBrackets > 0)
                {
                    if (line.Contains($"[{indexer}]"))
                        line = line.Replace($"[{indexer}]", "[id.x]");

                    if (line.Contains("for (") || line.Contains("for("))
                    {
                        string[] words = line.Split(' ');
                        int range = Array.IndexOf(words, "->");

                        if (range == -1)
                        {
                            Debug.LogError($"Translator: for-loop at source line {i + 1} is missing '->' range operator: \"{line}\"");
                            sb.Append(line, insideBrackets);
                            continue;
                        }

                        char ind = line[line.IndexOf('(') + 1];
                        string min = words[range - 1];
                        string max = words[range + 1][..(words[range + 1].Length - 1)];
                        string operation = $"{ind}++";

                        if (line.Count(c => c == ';') == 2)
                            operation = line[(line.LastIndexOf(';') + 1)..(line.Length - 2)];

                        line = $"for (uint {ind} = {min}; {ind} < {max}; {operation})";
                        sb.Append(line, insideBrackets);
                        continue;
                    }
                }

                sb.Append(line, insideBrackets);
            }

            sb.Insert(0, "\n");

            if (kernels.Count == 0)
                Debug.LogWarning("Translator: there are no kernels");

            for (int i = kernels.Count - 1; i >= 0; i--)
                sb.Insert(0, $"#pragma kernel {kernels[i]}\n");

            return sb.ToString();
        }

        private static string HandleKernles(string line, string nextLine, List<string> kernels, ref string indexer, ref int insideBrackets)
        {
            string result = "";

            if (line.Contains(","))
                Debug.LogError("Translator: multi dimentional kernels aren't supported yet");

            string[] words = nextLine.Split(' ');

            if (words.Length < 2)
            {
                Debug.LogError($"Translator: kernel function line has too few tokens: \"{nextLine}\"");
                return result;
            }

            kernels.Add(words[1]);

            if (!nextLine.Contains('('))
            {
                Debug.LogError($"Translator: kernel function line is missing '(': \"{nextLine}\"");
                return result;
            }

            result = $"[numthreads({conf.ThreadGroupX},{conf.ThreadGroupY},{conf.ThreadGroupZ})]\n";
            result += nextLine[..(nextLine.IndexOf('(') + 1)] + "uint3 id : SV_DispatchThreadID)" + "\n";
            result += "{" + "\n";

            if (!line.Contains('(') || !line.Contains(')'))
            {
                Debug.LogError($"Translator: kernel size declaration is missing parentheses: \"{line}\"");
                return result;
            }

            result += $"    if (id.x >= {line[(line.IndexOf('(') + 1)..line.IndexOf(')')]}) return;\n\n";

            if (nextLine.Contains("=>"))
            {
                result += "    " + nextLine[(nextLine.IndexOf("=>") + 3)..] + "\n";
                int rawIndex = Array.IndexOf(words, "(indexer");
                
                if (rawIndex == -1 || rawIndex + 2 >= words.Length)
                {
                    Debug.LogError($"Translator: could not locate indexer parameter in kernel line: \"{nextLine}\"");
                    return result;
                }

                int indexerIndex = rawIndex + 2;
                string indexerStr = words[indexerIndex];

                if (indexerStr.Length < 1)
                {
                    Debug.LogError($"Translator: indexer token is empty in kernel line: \"{nextLine}\"");
                    return result;
                }

                indexer = indexerStr[..(indexerStr.Length - 1)];
                result = result.Replace($"[{indexer}]", "[id.x]");
                result += "}\n\n";
            }

            else
            {
                string lastWord = words[^1];
                if (lastWord.Length < 2)
                {
                    Debug.LogError($"Translator: last token in kernel function line is too short to extract indexer: \"{nextLine}\"");
                    return result;
                }
                indexer = lastWord[..(lastWord.Length - 2)];
                insideBrackets++;
            }

            return result;
        }

        // returns the line and if its a variable
        private static (string, bool) HandleVariables(string line)
        {
            if (!IsVariable(line))
                return (line, false);

            if (line.StartsWith("static"))
            {
                line = line[..6] + " const " + line[7..];
                return (line, true);
            }

            if (line.Contains("variable"))
            {
                line.Replace("variable", "");
                return (line, true);
            }

            if (line.Contains("const"))
                return (line, true);

            if (!bufferTypes.Any(s => line.Contains(s)))
                line = "const " + line;

            return (line, true);
        }
        
        private static bool IsVariable(string line)
        {
            if (line.Contains("=>") || !line.EndsWith(";"))
                return false;

            bool isVariable = false;
            foreach (var type in dataTypes)
            {
                if (line.Contains(type))
                {
                    isVariable = true;
                    break;
                }
            }

            return isVariable;
        }

        private static bool ContainsDatatype(string line)
        {
            bool contains = false;
            foreach (var type in dataTypes)
            {
                if (line.Contains(type))
                {
                    contains = true;
                    break;
                }
            }

            return contains;
        }

        private static void Append(this StringBuilder sb, string text, int indents)
        {
            string str = "";

            for (int i = 0; i < indents; i++)
                str += "    ";

            sb.Append(str + text + "\n");
        }
    }
}