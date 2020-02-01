﻿// Copyright (c) 2017-2020 Katy Coe - https://www.djkaty.com - https://github.com/djkaty
// All rights reserved

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CommandLine;
using Il2CppInspector.Reflection;

namespace Il2CppInspector
{
    public class App
    {
        private class Options
        {
            [Option('i', "bin", Required = true, HelpText = "IL2CPP binary file input", Default = "libil2cpp.so")]
            public string BinaryFile { get; set; }

            [Option('m', "metadata", Required = true, HelpText = "IL2CPP metadata file input", Default = "global-metadata.dat")]
            public string MetadataFile { get; set; }

            [Option('c', "cs-out", Required = false, HelpText = "C# output file (when using single-file layout) or path (when using per namespace, assembly or class layout)", Default = "types.cs")]
            public string CSharpOutPath { get; set; }

            [Option('p', "py-out", Required = false, HelpText = "IDA Python script output file", Default = "ida.py")]
            public string PythonOutFile { get; set; }

            [Option('e', "exclude-namespaces", Required = false, Separator = ',', HelpText = "Comma-separated list of namespaces to suppress in C# output, or 'none' to include all namespaces",
                Default = new [] {
                    "System",
                    "Mono",
                    "Microsoft.Win32",
                    "Unity",
                    "UnityEditor",
                    "UnityEngine",
                    "UnityEngineInternal",
                    "AOT",
                    "JetBrains.Annotations"
                })]
            public IEnumerable<string> ExcludedNamespaces { get; set; }

            [Option('l', "layout", Required = false, HelpText = "Partitioning of C# output ('single' = single file, 'namespace' = one file per namespace in folders, 'assembly' = one file per assembly, 'class' = one file per class in namespace folders, 'tree' = one file per class in assembly and namespace folders)", Default = "single")]
            public string LayoutSchema { get; set; }

            [Option('s', "sort", Required = false, HelpText = "Sort order of type definitions in C# output ('index' = by type definition index, 'name' = by type name). No effect when using file-per-class or tree layout", Default = "index")]
            public string SortOrder { get; set; }

            [Option('f', "flatten", Required = false, HelpText = "Flatten the namespace hierarchy into a single folder rather than using per-namespace subfolders. Only used when layout is per-namespace or per-class. Ignored for tree layout")]
            public bool FlattenHierarchy { get; set; }

            [Option('n', "suppress-metadata", Required = false, HelpText = "Diff tidying: suppress method pointers, field offsets and type indices from C# output. Useful for comparing two versions of a binary for changes with a diff tool")]
            public bool SuppressMetadata { get; set; }

            [Option('k', "must-compile", Required = false, HelpText = "Compilation tidying: try really hard to make code that compiles. Suppress generation of code for items with CompilerGenerated attribute. Comment out attributes without parameterless constructors or all-optional constructor arguments. Don't emit add/remove/raise on events. Specify AttributeTargets.All on classes with AttributeUsage attribute. Force auto-properties to have get accessors. Force regular properties to have bodies. Suppress global::Locale classes.")]
            public bool MustCompile { get; set; }

            [Option("separate-attributes", Required = false, HelpText = "Place assembly-level attributes in their own AssemblyInfo.cs files. Only used when layout is per-assembly or tree")]
            public bool SeparateAssemblyAttributesFiles { get; set; }

            [Option('j', "project", Required = false, HelpText = "Create a Visual Studio solution and projects. Implies --layout tree, --must-compile and --separate-attributes")]
            public bool CreateSolution { get; set; }

            [Option("unity-path", Required = false, HelpText = "Path to Unity editor (when using --project). Wildcards select last matching folder in alphanumeric order", Default = @"C:\Program Files\Unity\Hub\Editor\*")]
            public string UnityPath { get; set; }

            [Option("unity-assemblies", Required = false, HelpText = "Path to Unity script assemblies (when using --project). Wildcards select last matching folder in alphanumeric order", Default = @"C:\Program Files\Unity\Hub\Editor\*\Editor\Data\Resources\PackageManager\ProjectTemplates\libcache\com.unity.template.3d-*\ScriptAssemblies")]
            public string UnityAssembliesPath { get; set; }
        }

        // Adapted from: https://stackoverflow.com/questions/16376191/measuring-code-execution-time
        public class Benchmark : IDisposable 
        {
            private readonly Stopwatch timer = new Stopwatch();
            private readonly string benchmarkName;

            public Benchmark(string benchmarkName)
            {
                this.benchmarkName = benchmarkName;
                timer.Start();
            }

            public void Dispose() 
            {
                timer.Stop();
                Console.WriteLine($"{benchmarkName}: {timer.Elapsed.TotalSeconds:N2} sec");
            }
        }

        public static int Main(string[] args) =>
            Parser.Default.ParseArguments<Options>(args).MapResult(
                options => Run(options),
                _ => 1);

        private static int Run(Options options) {
            // Banner
            var asmInfo = FileVersionInfo.GetVersionInfo(System.Reflection.Assembly.GetEntryAssembly().Location);
            Console.WriteLine(asmInfo.ProductName);
            Console.WriteLine("Version " + asmInfo.ProductVersion);
            Console.WriteLine(asmInfo.LegalCopyright);
            Console.WriteLine("");

            // Check excluded namespaces
            if (options.ExcludedNamespaces.Count() == 1 && options.ExcludedNamespaces.First().ToLower() == "none")
                options.ExcludedNamespaces = new List<string>();

            // Check files
            if (!File.Exists(options.BinaryFile)) {
                Console.Error.WriteLine($"File {options.BinaryFile} does not exist");
                return 1;
            }
            if (!File.Exists(options.MetadataFile)) {
                Console.Error.WriteLine($"File {options.MetadataFile} does not exist");
                return 1;
            }

            // Creating a Visual Studio solution requires Unity assembly references
            var unityPath = string.Empty;
            var unityAssembliesPath = string.Empty;

            if (options.CreateSolution) {
                unityPath = FindPath(options.UnityPath);
                unityAssembliesPath = FindPath(options.UnityAssembliesPath);

                if (!Directory.Exists(unityPath)) {
                    Console.Error.WriteLine($"Unity path {unityPath} does not exist");
                    return 1;
                }
                if (!File.Exists(unityPath + @"\Editor\Data\Managed\UnityEditor.dll")) {
                    Console.Error.WriteLine($"No Unity installation found at {unityPath}");
                    return 1;
                }
                if (!Directory.Exists(unityAssembliesPath)) {
                    Console.Error.WriteLine($"Unity assemblies path {unityAssembliesPath} does not exist");
                    return 1;
                }
                if (!File.Exists(unityAssembliesPath + @"\UnityEngine.UI.dll")) {
                    Console.Error.WriteLine($"No Unity assemblies found at {unityAssembliesPath}");
                    return 1;
                }

                Console.WriteLine("Using Unity editor at " + unityPath);
                Console.WriteLine("Using Unity assemblies at " + unityAssembliesPath);
            }

            // Analyze data
            List<Il2CppInspector> il2cppInspectors;
            using (var il2cppTimer = new Benchmark("Analyze IL2CPP data"))
                il2cppInspectors = Il2CppInspector.LoadFromFile(options.BinaryFile, options.MetadataFile);

            if (il2cppInspectors == null)
                Environment.Exit(1);

            // Write output file
            int i = 0;
            foreach (var il2cpp in il2cppInspectors) {
                // Create model
                Il2CppModel model;
                using (var modelTimer = new Benchmark("Create type model"))
                    model = new Il2CppModel(il2cpp);

                // C# signatures output
                using (var signaturesDumperTimer = new Benchmark("Generate C# code")) {
                    var writer = new Il2CppCSharpDumper(model) {
                        ExcludedNamespaces = options.ExcludedNamespaces.ToList(),
                        SuppressMetadata = options.SuppressMetadata,
                        MustCompile = options.MustCompile
                    };

                    var imageSuffix = i++ > 0 ? "-" + (i - 1) : "";

                    var csOut = options.CSharpOutPath;
                    if (csOut.ToLower().EndsWith(".cs"))
                        csOut = csOut.Insert(csOut.Length - 3, imageSuffix);
                    else
                        csOut += imageSuffix;

                    if (options.CreateSolution)
                        writer.WriteSolution(csOut, unityPath, unityAssembliesPath);

                    else
                        switch (options.LayoutSchema.ToLower(), options.SortOrder.ToLower()) {
                            case ("single", "index"):
                                writer.WriteSingleFile(csOut, t => t.Index);
                                break;
                            case ("single", "name"):
                                writer.WriteSingleFile(csOut, t => t.Name);
                                break;

                            case ("namespace", "index"):
                                writer.WriteFilesByNamespace(csOut, t => t.Index, options.FlattenHierarchy);
                                break;
                            case ("namespace", "name"):
                                writer.WriteFilesByNamespace(csOut, t => t.Name, options.FlattenHierarchy);
                                break;

                            case ("assembly", "index"):
                                writer.WriteFilesByAssembly(csOut, t => t.Index, options.SeparateAssemblyAttributesFiles);
                                break;
                            case ("assembly", "name"):
                                writer.WriteFilesByAssembly(csOut, t => t.Name, options.SeparateAssemblyAttributesFiles);
                                break;

                            case ("class", _):
                                writer.WriteFilesByClass(csOut, options.FlattenHierarchy);
                                break;

                            case ("tree", _):
                                writer.WriteFilesByClassTree(csOut, options.SeparateAssemblyAttributesFiles);
                                break;
                        }
                }

                // IDA Python script output
                using (var scriptDumperTimer = new Benchmark("IDA Python Script Dumper")) {
                    var idaWriter = new Il2CppIDAScriptDumper(model);
                    idaWriter.WriteScriptToFile(options.PythonOutFile);
                }
            }

            // Success exit code
            return 0;
        }

        private static string FindPath(string pathWithWildcards) {
            var absolutePath = Path.GetFullPath(pathWithWildcards);

            if (absolutePath.IndexOf("*", StringComparison.Ordinal) == -1)
                return absolutePath;

            Regex sections = new Regex(@"((?:[^*]*)\\)((?:.*?)\*.*?)(?:$|\\)");
            var matches = sections.Matches(absolutePath);

            var pathLength = 0;
            var path = "";
            foreach (Match match in matches) {
                path += match.Groups[1].Value;
                var search = match.Groups[2].Value;

                var dir = Directory.GetDirectories(path, search, SearchOption.TopDirectoryOnly)
                    .OrderByDescending(x => x)
                    .FirstOrDefault();

                path = dir + @"\";
                pathLength += match.Groups[1].Value.Length + match.Groups[2].Value.Length + 1;
            }

            if (pathLength < absolutePath.Length)
                path += absolutePath.Substring(pathLength);

            return path;
        }
    }
}
