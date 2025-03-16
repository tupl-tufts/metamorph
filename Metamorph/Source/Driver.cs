using System.Text.RegularExpressions;
using CommandLine;
using DafnyTestGeneration;
using Microsoft.Dafny;
using Microsoft.Extensions.Configuration;
using NLog;
using NLog.Extensions.Logging;
using Parser = CommandLine.Parser;

namespace Synthesis;

public abstract class Driver {
  
  public static readonly Logger Log;
  
  static Driver() {
    var config = new ConfigurationBuilder()
      .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
      .Build();
    LogManager.Configuration = new NLogLoggingConfiguration(config.GetSection("NLog"));
    GlobalDiagnosticsContext.Set("startTime", DateTime.Now.ToString("yyyy-MM-dd_HH:mm:ss"));
    GlobalDiagnosticsContext.Set("stage", "StartUp");
    Log = LogManager.GetCurrentClassLogger();
  }
  
  public class Options {

    [Option('i', "input", Required = true, HelpText = "Dafny file with the synthesis problem.")]
    public string InputFile { get; set; } = "";
    
    [Option('g', "goal", Required = false, HelpText = "Name of the synthesis goal (if multiple synthesis annotated predicates exist).")]
    public string? MethodName { get; set; } = null;

    [Option(
      "noDistanceMetric",
      Default = false,
      HelpText = "Disables the distance metrics (for ablation study).")]
    public bool DisableHeuristic { get; set; }

    [Option(
      "pretrain",
      Required = false,
      Default = null,
      HelpText = "Pretrain Metamorph on an API and save the learned facts to the specified directory")]
    public string? PreTrain { get; set; }

    [Option(
      't', 
      "timeLimit",
      Default = int.MaxValue,
      HelpText = "Time limit (in seconds) after which the process will be killed on each synthesis problem.")]
    public int TimeLimit { get; set; }
    
    [Option(
      "loadPretrained",
      Required = false, 
      Default = null,
      HelpText = "Load pretrained data from directory")]
    public string? LoadHeuristics { get; set; }
    
    [Option(
      "greedy",
      Default = false,
      HelpText = "Use the greedy distance metric")]
    public bool SUSHI { get; set; }

    public DateTime StartTime = DateTime.Now;
    public string? HeursticDir = null;
  }

  private static async Task Main(string[] args) {
    await Parser.Default.ParseArguments<Options>(args).WithParsedAsync(ProcessOptionsAsync);
  }

  private static Program? GetResolvedProgram(string inputFile, out string errorMessage) {
    var sourceFile = new FileInfo(inputFile).FullName;
    var source = new StreamReader(sourceFile).ReadToEnd();
    var dafnyOptions = DafnyOptions.Create(new StringWriter(), TextReader.Null, Array.Empty<string>());
    var uri = new Uri(sourceFile);
    var consoleErrorReporter = new ConsoleErrorReporter(dafnyOptions);
    var resolvedProgram = Utils.Parse(consoleErrorReporter, source, resolve: true, uri: uri);
    if (consoleErrorReporter.HasErrors) {
      errorMessage = $"Error parsing Dafny program in file {inputFile}";
      return null;
    }
    errorMessage = "";
    return resolvedProgram;
  }

  private static bool IsValidInputFile(Options options, out string errorMessage) {
    if (!File.Exists(options.InputFile)) {
      errorMessage = $"Error: Could not find file or directory {options.InputFile}";
      return false;
    }
    if (!options.InputFile.EndsWith(".dfy")) {
      errorMessage = $"Error: {options.InputFile} is not a .dfy file or a directory.";
      return false;
    }

    var resolvedProgram = GetResolvedProgram(options.InputFile, out errorMessage);
    if (resolvedProgram == null) {
      return false;
    }
    var resolvedTargetModule = resolvedProgram.DefaultModuleDef.Children.OfType<LiteralModuleDecl>().First();
    var resolvedTarget = Search.FindMemberDeclsWithAttributes(resolvedTargetModule, "synthesize")
      .OfType<Predicate>().FirstOrDefault(predicate => options.MethodName == null || 
                                                       (predicate.Attributes != null && predicate.Attributes.Args != null && predicate.Attributes.Args.Count > 0 && (string?)(predicate.Attributes.Args[0] as StringLiteralExpr)?.Value == options.MethodName));
    if ((options.PreTrain == null) && (resolvedTarget == null || !resolvedTarget.IsStatic ||
        resolvedTarget.Formals.Count != 1)) {
      errorMessage = $"Error: {options.InputFile} must have a static predicate {options.MethodName ?? ""} that takes a single input parameter and is annotated with the {{synthesize}} attribute.";
      return false;
    }
    errorMessage = "";
    return true;
  }

  private static async Task ProcessOptionsAsync(Options options) {

    if (   (options.PreTrain != null       && (options.LoadHeuristics != null || options.DisableHeuristic || options.SUSHI))
        || (options.LoadHeuristics != null && (options.PreTrain != null       || options.DisableHeuristic || options.SUSHI))
        || (options.DisableHeuristic     && (options.LoadHeuristics != null   || options.PreTrain != null || options.SUSHI))
        || (options.SUSHI                && (options.LoadHeuristics != null   || options.PreTrain != null || options.DisableHeuristic))) {
      Log.Fatal("loadHeuristic, pretrain, disableHeuristic, and SUSHI and mutually exclusive options");
      Environment.Exit(1);
    }

    options.HeursticDir = options.PreTrain ?? options.LoadHeuristics;

    if (options.HeursticDir != null && !Directory.Exists(Path.GetDirectoryName(options.HeursticDir))) {
      Log.Fatal($"Cannot find parent directory {Path.GetDirectoryName(options.HeursticDir)}");
      Environment.Exit(1);
    }
    
    if (options.HeursticDir != null && !Directory.Exists(options.HeursticDir)) {
      Directory.CreateDirectory(options.HeursticDir);
    }
    
    if (!IsValidInputFile(options, out var errorMessage)) {
      await Console.Error.WriteLineAsync(errorMessage);
      Environment.Exit(1);
    }
    
    var success = true;
    
    GlobalDiagnosticsContext.Set("stage", new Regex("[/\\\\]").Replace(options.InputFile, "$"));
    options.StartTime = DateTime.Now;
    var resolvedProgram = GetResolvedProgram(options.InputFile, out var errorMessage2);
    if (resolvedProgram == null) {
      await Console.Error.WriteLineAsync(errorMessage2);
      Environment.Exit(1);
    }
    if (options.PreTrain != null) {
      await HeuristicLearner.LearnHeuristicsAsync(options, resolvedProgram, options.InputFile);
      return;
    }
    var result = await Search.SynthesizeAsync(options);
    success = success && result.Outcome == Search.Outcome.Success;
    if (!success) {
      Environment.Exit(1);
    }
  }
}
