using System.Text.RegularExpressions;
using Microsoft.Boogie;
using Microsoft.Boogie.SMTLib;
using Microsoft.Dafny;
using Microsoft.Dafny.LanguageServer.CounterExampleGeneration;
using IdentifierExpr = Microsoft.Dafny.IdentifierExpr;
using LiteralExpr = Microsoft.Dafny.LiteralExpr;
using Program = Microsoft.Dafny.Program;
using Token = Microsoft.Dafny.Token;

namespace Synthesis; 

/// <summary>
/// This class collects most of the functionality for interacting with the Dafny verifier
/// </summary>
public abstract class VerificationUtils {
  
  public enum QueryType { Regular, Simplify, Heuristic}
  
  
  private static readonly Regex SynthesizedMethodRegex = new ("(static method "+DafnyQuery.DefaultMethodName+"(.|\n)*?[\t \n]})", RegexOptions.Multiline);
  public const string KeepAssertionAttribute = "keepAssertion";
  public static Dictionary<QueryType, int> DafnyQueryCount { get; private set; } = new();
  public static Dictionary<QueryType, TimeSpan> DafnyQueryTime { get; private set; } = new(); // total time that Dafny queries took
  private static Program unresolvedProgram = null!; // a copy of the original program, unresolved, not to be modified
  

  /// <summary>
  /// Read and parse the original unresolved program. This is later reused in VerifyMethodAsync()
  /// </summary>
  public static void Init() {
    DafnyQueryCount = new Dictionary<QueryType, int>();
    DafnyQueryTime = new();
    foreach (QueryType queryType in Enum.GetValues(typeof(QueryType))) {
      DafnyQueryTime[queryType] = new(0);
      DafnyQueryCount[queryType] = 0;
    }
    var options = DafnyOptions.Create(new StringWriter(), TextReader.Null, Array.Empty<string>());
    var uri = new Uri(Search.SourceFile);
    var source = new StreamReader(Search.SourceFile).ReadToEnd();
    var errorReporter = new ConsoleErrorReporter(options);
    unresolvedProgram = DafnyTestGeneration.Utils.Parse(errorReporter, source, resolve: false, uri: uri);
  }

  /// <summary>
  /// Find a class in the unresolvedProgram that has the given <param name="qualifiedClassName"></param>.
  /// </summary>
  public static ClassDecl? FindClass(string qualifiedClassName, Program program) {
    var path = qualifiedClassName.Split(".");
    ModuleDefinition module = program.DefaultModuleDef;
    foreach (var moduleName in path[..^1]) {
      module = module.Children.OfType<LiteralModuleDecl>()
        .Select(decl => decl.ModuleDef).First(m => m.Name == moduleName);
    }
    return module.Children.OfType<ClassDecl>()
      .FirstOrDefault(c => c.Name == path[^1]);
  }
  
  /// <summary>
  /// Setup DafnyOptions to prepare for model extraction
  /// TODO: This is copied from a private procedure within DafnyTestGeneration - try to just use that in the future
  /// </summary>
  private static void SetupForCounterexamples(DafnyOptions options, uint timeLimit) {
    options.NormalizeNames = false;
    options.EmitDebugInformation = true;
    options.ErrorTrace = 1;
    options.EnhancedErrorMessages = 1;
    options.ModelViewFile = "-";
    options.Prune = options.TestGenOptions.ForcePrune;
    options.TraceProofObligations = false;
    options.TimeLimit = timeLimit;
  }

  private static bool AttributesIncludeKeepAssertionAttribute(QKeyValue? attributes) {
    if (attributes == null) {
      return false;
    }
    if (attributes.Key == KeepAssertionAttribute) {
      return true;
    }
    return AttributesIncludeKeepAssertionAttribute(attributes.Next);
  }

  /// <summary>
  /// Assume all preconditions and well-formed-ness checks in the program leaving a single assertion goal in the end. 
  /// </summary>
  private static void AssumeAllPreconditions(Microsoft.Boogie.Program program) {
    foreach (var implementation in program.Implementations) {
      foreach (var block in implementation.Blocks) {
        var toRemove = block.cmds.OfType<AssertCmd>()
          .Where(cmd => !AttributesIncludeKeepAssertionAttribute(cmd.Attributes)).ToList();
        foreach (var assertCmd in toRemove) {
          var index = block.cmds.IndexOf(assertCmd);
          block.cmds.RemoveAt(index);
          block.cmds.Insert(index, new AssumeCmd(assertCmd.tok, assertCmd.Expr, assertCmd.Attributes));
        }
      }
    }
  }
  
  /// <summary> Some CallStack magic to make sure Dafny to Boogie translator does not crash </summary>
  private static readonly TaskScheduler LargeThreadScheduler =
    CustomStackSizePoolTaskScheduler.Create(0x10000000, Environment.ProcessorCount);
  private static readonly TaskFactory LargeStackFactory = new(CancellationToken.None,
    TaskCreationOptions.DenyChildAttach, TaskContinuationOptions.None, LargeThreadScheduler);

  /// <summary>
  /// Attempt to verify a given method in Dafny.
  /// </summary>
  /// <param name="queryType"></param> The purpose of the Dafny query
  /// <param name="qualifiedClasName"></param> The name of the class in which to put the method to be verified
  /// <param name="method"></param> The method to be verified (its AST is temporarily copied over to unresolvedProgram)
  /// <param name="assumeAllPreconditions"></param> If true, assume all preconditions and well-formed-ness checks
  /// <param name="timeLimit"></param> Put a time limit on how long the query is allowed to run for
  /// <returns></returns>
  public static async Task<VerificationResult> VerifyMethodAsync(
    QueryType queryType,
    string qualifiedClasName, 
    Method method, 
    bool assumeAllPreconditions,
    uint timeLimit)
  {
    // Make a note of the time the query started:
    DafnyQueryCount[queryType]++;
    DateTime verificationBegan = DateTime.Now;
    // Setup DafnyOptions:
    var options = DafnyOptions.Create(new StringWriter(), TextReader.Null, Array.Empty<string>());
    options.ApplyDefaultOptions();
    options.ProcsToCheck = new List<string>();
    SetupForCounterexamples(options, timeLimit);
    // Create a new DafnyProgram that contains the method provided as argument. Parse it, resolved it, translate it
    var classDecl = FindClass(qualifiedClasName, unresolvedProgram);
    var topLevelModule = classDecl!.EnclosingModuleDefinition;
    while (!topLevelModule.EnclosingModule.IsDefaultModule) {
      topLevelModule = topLevelModule.EnclosingModule;
    }
    classDecl.Members.Add(method);

    var writer = new StringWriter();
    var printer = new Printer(writer, options);
    printer.PrintProgram(unresolvedProgram, false);
    classDecl.Members.Remove(method);
    var sourceAsString = writer.ToString();

    if (Driver.Log.IsTraceEnabled) {
      var updateStatements = new Search.SolutionFormatter().Format(method.Body.Body, DafnyQuery.ReceiverName, true);
      string signature;
      var ins = string.Join(", ", method.Ins.Select(formal => $"{formal.Name}: {formal.Type}"));
      // assumption: outs and modifies are mutually exclusive
      if (method.Outs.Count > 0) {
        var outs = string.Join(", ", method.Outs.Select(formal => $"{formal.Name}: {formal.Type}"));
        signature = $"static method {DafnyQuery.DefaultMethodName}({ins}) returns ({outs})";
      } else if (method.Mod != null) {
        // TODO: not sure if this is the right way to print the expression for `modifies`
        var mod = string.Join(", ", method.Mod.Expressions.Select(expr => Printer.ExprToString(options, expr.OriginalExpression)));
        signature = $"static method {DafnyQuery.DefaultMethodName}({ins})\n    modifies {mod}";
      } else {
        signature = $"static method {DafnyQuery.DefaultMethodName}({ins})";
      }

      Driver.Log.Trace($"Verifying the body of the following method:\n" +
                       $"{signature} {{" +
                       $"{string.Join("\n", updateStatements.ConvertAll(statement => Printer.StatementToString(DafnyOptions.Default, statement)))}" +
                       $"}}");

      // use the following if you want to print the actual method being queried:
      Driver.Log.Trace($"Verifying the body of the following method (literal):\n {SynthesizedMethodRegex.Match(sourceAsString).Groups[1]}");
    }
    var program = DafnyTestGeneration.Utils.Parse(
      new ConsoleErrorReporter(options), 
      sourceAsString, 
      resolve: false, 
      uri: new Uri(Search.SourceFile));
    await LargeStackFactory.StartNew(() => new ProgramResolver(program).Resolve(new CancellationToken()));
    var boogiePrograms = await LargeStackFactory.StartNew(
      () => SynchronousCliCompilation.Translate(options, program).ToList());
    var boogieProgram = boogiePrograms.First(tuple => tuple.Item1 == topLevelModule.Name).Item2;
    options.ProcsToCheck = boogieProgram.Implementations
      .Select(implementation => implementation.VerboseName)
      .Where(name => name.Split(" ").First().Contains(qualifiedClasName + "." + method.Name) && name.Contains("correctness")).ToList();
    // Now attempt to verify the code with Boogie
    var verificationWriter = new StringWriter();
    var resultString = "";
    using (var engine = ExecutionEngine.CreateWithoutSharedCache(options)) {
      var guid = Guid.NewGuid().ToString();
        if (assumeAllPreconditions) {
          AssumeAllPreconditions(boogieProgram);
        }
        new ProofDependencyIdRemover().VisitProgram(boogieProgram);
        boogieProgram.Resolve(options);
        boogieProgram.Typecheck(options);
        engine.EliminateDeadVariables(boogieProgram); 
        engine.CollectModSets(boogieProgram);
        engine.Inline(boogieProgram);
        var taskResult = await Task.WhenAny(engine.InferAndVerify(verificationWriter, boogieProgram,
            new PipelineStatistics(), null,
            _ => { }, guid),
          Task.Delay(TimeSpan.FromSeconds(timeLimit)));
        resultString += verificationWriter.ToString();
        if (taskResult is not Task<PipelineOutcome>) {
          // TODO: Can we support periodical timeouts?
          Driver.Log.Warn("Encountered a timeout");
          DafnyQueryTime[queryType] += DateTime.Now - verificationBegan;
          return new VerificationResult(VerificationResult.Status.Timeout, method);
        }
    }
    if (resultString.Length == 0) {
      DafnyQueryTime[queryType] += DateTime.Now - verificationBegan;
      return new VerificationResult(VerificationResult.Status.Verified, method);
    }

    // TODO: There will be a way to get model models without parsing in Dafny 4.4+.
    var dafnyModel = DafnyModel.ExtractModel(options, resultString);
    DafnyQueryTime[queryType] += DateTime.Now - verificationBegan;
    return new VerificationResult(VerificationResult.Status.Counterexample, method, dafnyModel);
  }
  
  /// <summary>
  /// This visitor removes all ProofDependency annotations from the AST, thereby preventing duplicate ids errors
  /// during verification.
  /// </summary>
  private class ProofDependencyIdRemover : StandardVisitor {
    public override Absy Visit(Absy node) {
      if (node is ICarriesAttributes carriesAttributes) {
        carriesAttributes.Attributes = RemoveIdAttribute(carriesAttributes.Attributes, "id");
      }
      return base.Visit(node);
    }

    public override Implementation VisitImplementation(Implementation node) {
      node.LocVars = VisitVariableSeq(node.LocVars);
      node.Blocks = VisitBlockList(node.Blocks);
      if (node.Proc != null) {
        node.Proc = (Procedure)node.Proc.StdDispatch(this);
      }
      node = (Implementation) VisitDeclWithFormals(node);
      return node;
    }

    private QKeyValue? RemoveIdAttribute(QKeyValue? attributes, string attributeName) {
      if (attributes == null) {
        return null;
      }
      if (attributes.Key == attributeName) {
        return RemoveIdAttribute(attributes.Next, attributeName);
      }
      attributes.Next = RemoveIdAttribute(attributes.Next, attributeName);
      return attributes;
    }
  }
  
  /// <summary>
  /// This pass replaces all references to a given variable with a this expression
  /// </summary>
  public class IdentifierToThisExpressionConverter : Cloner {

    private readonly string? identifierName;

    public IdentifierToThisExpressionConverter(string identifierName) {
      this.identifierName = identifierName;
    }

    public override Expression CloneExpr(Expression expr) {
      if ((expr is IdentifierExpr identifierExpr && identifierExpr.Name == identifierName) || 
          ((expr is NameSegment nameSegment && nameSegment.Name == identifierName))) {
        var thisExpr = new ThisExpr(Token.NoToken);
        if (expr.Type != null) {
          thisExpr.Type = expr.Type;
        }
        return thisExpr;
      }
      if (expr is LiteralExpr) {
        var clonedExpr = base.CloneExpr(expr);
        clonedExpr.Type = expr.Type;
        return clonedExpr;
      }
      var result = base.CloneExpr(expr);
      if (expr != null && expr.Type != null) {
        result.Type = expr.Type;
      }
      return result;
    }
  }
  
  /// <summary>
  /// This pass replaces all references to a given variable with a this expression
  /// </summary>
  public class ThisToIdentifierExpressionConverter : Cloner {

    private IdentifierExpr identifierExpr;

    public ThisToIdentifierExpressionConverter(string identifierName) {
      identifierExpr = new IdentifierExpr(Token.NoToken, identifierName);
    }

    public override Expression CloneExpr(Expression expr) {
      if (expr is ThisExpr or ImplicitThisExpr) {
        if (identifierExpr.Type == null && expr.Type != null) {
          identifierExpr.Type = expr.Type;
        }
        return identifierExpr;
      }
      
      if (expr is LiteralExpr) {
        var clonedExpr = base.CloneExpr(expr);
        clonedExpr.Type = expr.Type;
        return clonedExpr;
      }
      var result = base.CloneExpr(expr);
      if (expr != null && expr.Type != null) {
        result.Type = expr.Type;
      }
      return result;
    }
  }
}
