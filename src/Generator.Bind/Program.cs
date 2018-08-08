/* Copyright (c) 2006, 2007 Stefanos Apostolopoulos
 * See license.txt for license info
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Bind.Baking;
using Bind.Baking.Overloading;
using Bind.Generators;
using Bind.Generators.ES;
using Bind.Generators.GL2;
using Bind.Translation.Mappers;
using Bind.Typemap;
using Bind.XML.Documentation;
using Bind.XML.Overrides;
using Bind.XML.Signatures;
using Bind.XML.Signatures.Functions;
using CommandLine;
using JetBrains.Annotations;

namespace Bind
{
    /// <summary>
    /// Main class for the program.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Gets the command-line arguments that were passed to the program.
        /// </summary>
        internal static CommandLineArguments Arguments { get; private set; }

        private static readonly List<IGeneratorSettings> Generators = new List<IGeneratorSettings>();

        private static readonly IDictionary<string, IReadOnlyList<ApiProfile>> _cachedProfiles =
            new Dictionary<string, IReadOnlyList<ApiProfile>>();

        private static readonly IDictionary<string, IReadOnlyDictionary<TypeSignature, TypeSignature>> _cachedTypemaps =
            new Dictionary<string, IReadOnlyDictionary<TypeSignature, TypeSignature>>();

        private static async Task<int> Main(string[] args)
        {
            Console.WriteLine($"OpenGL binding generator {Assembly.GetExecutingAssembly().GetName().Version} for OpenTK.");
            Console.WriteLine("For comments, bugs and suggestions visit http://github.com/opentk/opentk");
            Console.WriteLine();

            Parser.Default.ParseArguments<CommandLineArguments>(args)
                .WithParsed(r => Arguments = r);

            if (Arguments is null)
            {
                return 1;
            }

            CreateGenerators();

            var stopwatch = Stopwatch.StartNew();
            var generatorTasks = Generators.Select(GenerateBindingsAsync);
            await Task.WhenAll(generatorTasks);
            stopwatch.Stop();

            Console.WriteLine();
            Console.WriteLine("Bindings generated in {0} seconds.", stopwatch.Elapsed.Ticks / 10000000.0);

            return 0;
        }

        private static async Task GenerateBindingsAsync([NotNull] IGeneratorSettings generatorSettings)
        {
            var signaturePath = Path.Combine(Arguments.InputPath, generatorSettings.SpecificationFile);
            if (!_cachedProfiles.TryGetValue(signaturePath, out var profiles))
            {
                profiles = SignatureReader.GetAvailableProfiles(signaturePath).ToList();
            }

            var profileOverrides = OverrideReader
                .GetProfileOverrides(generatorSettings.OverrideFiles.ToArray())
                .ToList();

            var baker = new ProfileBaker(profiles, profileOverrides);
            var bakedProfile = baker.BakeProfile
            (
                generatorSettings.ProfileName,
                generatorSettings.Versions,
                generatorSettings.BaseProfileName
            );

            var documentationPath = Path.Combine
            (
                Arguments.DocumentationPath,
                generatorSettings.SpecificationDocumentationPath
            );

            var docs = DocumentationReader.ReadProfileDocumentation(documentationPath);
            var bakedDocs = new DocumentationBaker(bakedProfile).BakeDocumentation(docs);

            var languageTypemapPath = Path.Combine(Arguments.InputPath, generatorSettings.LanguageTypemap);
            if (!_cachedTypemaps.TryGetValue(languageTypemapPath, out var languageTypemap))
            {
                using (var fs = File.OpenRead(languageTypemapPath))
                {
                    languageTypemap = new TypemapReader().ReadTypemap(fs);
                }
            }

            var apiTypemapPath = Path.Combine(Arguments.InputPath, generatorSettings.APITypemap);
            if (!_cachedTypemaps.TryGetValue(apiTypemapPath, out var apiTypemap))
            {
                using (var fs = File.OpenRead(apiTypemapPath))
                {
                    apiTypemap = new TypemapReader().ReadTypemap(fs);
                }
            }

            var bakedMap = TypemapBaker.BakeTypemaps(apiTypemap, languageTypemap);

            var mapper = new ProfileMapper(bakedMap);
            var mappedProfile = mapper.Map(bakedProfile);

            var overloadedProfile = OverloadBaker.BakeOverloads(mappedProfile);

            var bindingsWriter = new BindingsWriterAsync(generatorSettings, overloadedProfile, bakedDocs);
            await bindingsWriter.WriteBindingsAsync();
        }

        private static void CreateGenerators()
        {
            if (Arguments.TargetAPIs.Contains(TargetAPI.All))
            {
                Generators.Add(new GL2GeneratorSettings());
                Generators.Add(new GLCore4GeneratorSettings());
                Generators.Add(new ES10GeneratorSettings());
                Generators.Add(new ES11GeneratorSettings());
                Generators.Add(new ES2GeneratorSettings());
                Generators.Add(new ES3GeneratorSettings());
                Generators.Add(new ES31GeneratorSettings());
            }
            else
            {
                foreach (var targetAPI in Arguments.TargetAPIs)
                {
                    switch (targetAPI)
                    {
                        case TargetAPI.GL2:
                        {
                            Generators.Add(new GL2GeneratorSettings());
                            break;
                        }
                        case TargetAPI.GL3:
                        case TargetAPI.GL4:
                        {
                            Generators.Add(new GLCore4GeneratorSettings());
                            break;
                        }
                        case TargetAPI.ES10:
                        {
                            Generators.Add(new ES10GeneratorSettings());
                            break;
                        }
                        case TargetAPI.ES11:
                        {
                            Generators.Add(new ES11GeneratorSettings());
                            break;
                        }
                        case TargetAPI.ES20:
                        {
                            Generators.Add(new ES2GeneratorSettings());
                            break;
                        }
                        case TargetAPI.ES30:
                        {
                            Generators.Add(new ES3GeneratorSettings());
                            break;
                        }
                        case TargetAPI.ES31:
                        {
                            Generators.Add(new ES31GeneratorSettings());
                            break;
                        }
                    }
                }
            }
        }
    }
}
