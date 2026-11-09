// Usage: interopgen <gameDir> <unityVersion e.g. 6000.3.18f1> <unityLibsDir> <outDir> [dummyDir]
// Mirrors BepInEx be.793 Il2CppInteropManager.RunCpp2Il + RunIl2CppInteropGenerator.
using System;
using System.IO;
using AssetRipper.Primitives;
using Cpp2IL.Core;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.InstructionSets;
using Cpp2IL.Core.OutputFormats;
using Cpp2IL.Core.ProcessingLayers;
using Il2CppInterop.Generator;
using Il2CppInterop.Generator.Runners;
using LibCpp2IL;

var (gameDir, ver, libs, outDir) = (args[0], args[1], args[2], args[3]);
var dummyDir = args.Length > 4 ? args[4] : null;

InstructionSetRegistry.RegisterInstructionSet<X86InstructionSet>(DefaultInstructionSets.X86_32);
InstructionSetRegistry.RegisterInstructionSet<X86InstructionSet>(DefaultInstructionSets.X86_64);
LibCpp2IlBinaryRegistry.RegisterBuiltInBinarySupport();
Cpp2IL.Core.Logging.Logger.WarningLog += (m, s) => Console.WriteLine($"[cpp2il warn {s}] {m.Trim()}");
Cpp2IL.Core.Logging.Logger.ErrorLog += (m, s) => Console.WriteLine($"[cpp2il err {s}] {m.Trim()}");

var sw = System.Diagnostics.Stopwatch.StartNew();
Cpp2IlApi.InitializeLibCpp2Il(Path.Combine(gameDir, "GameAssembly.dll"),
    Path.Combine(gameDir, "fm_Data/il2cpp_data/Metadata/global-metadata.dat"), UnityVersion.Parse(ver), false);
var layers = new System.Collections.Generic.List<Cpp2IlProcessingLayer> { new AttributeInjectorProcessingLayer() };
foreach (var l in layers) l.PreProcess(Cpp2IlApi.CurrentAppContext, layers);
foreach (var l in layers) l.Process(Cpp2IlApi.CurrentAppContext);
var asms = new AsmResolverDllOutputFormatDefault().BuildAssemblies(Cpp2IlApi.CurrentAppContext);
Console.WriteLine($"cpp2il: {asms.Count} assemblies in {sw.Elapsed}");
if (dummyDir != null)
{
    Directory.CreateDirectory(dummyDir);
    foreach (var a in asms) a.Write(Path.Combine(dummyDir, $"{a.Name}.dll"));
}
LibCpp2IlMain.Reset();
Cpp2IlApi.CurrentAppContext = null;

Directory.CreateDirectory(outDir);
Il2CppInteropGenerator.Create(new GeneratorOptions
{
    GameAssemblyPath = Path.Combine(gameDir, "GameAssembly.dll"), // ScanMethodRefs default = true on x64
    Source = asms,
    OutputDir = outDir,
    UnityBaseLibsDir = libs,
}).AddInteropAssemblyGenerator().Run();
Console.WriteLine($"interop done in {sw.Elapsed}");
