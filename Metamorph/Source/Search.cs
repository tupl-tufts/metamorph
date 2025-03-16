using DafnyTestGeneration;
using Microsoft.Dafny;
using IdentifierExpr = Microsoft.Dafny.IdentifierExpr;
using Type = Microsoft.Dafny.Type;

namespace Synthesis; 

public abstract class Search {
  
  public static string SourceFile = null!;
  // Search nodes will be sorted using EstimatedDistanceToStartState * HeuristicWeight + DistanceToEndState
  // HeuristicWeight == 1 is A* search
  private const double HeuristicWeight = 2;
  public const uint DefaultTimeLimit = 150;
  private const uint SimplificationTimeLimit = 40;

  private record SearchNode(List<Statement> Solution, List<Method> Methods, State State, int EstimatedDistanceToStartState, int DistanceToEndState) {
    public readonly List<Statement> Solution = Solution;
    public readonly List<Method> Methods = Methods;
    public readonly State State = State;
    public int EstimatedDistanceToStartState = EstimatedDistanceToStartState;
    public readonly int DistanceToEndState = DistanceToEndState;
  }

  public enum Outcome {
    Success, Timeout, Fail
  }

  public class Result {
    public readonly Outcome Outcome;
    public readonly TimeSpan RunningTime;

    public Result(Outcome outcome, TimeSpan runningTime) {
      Outcome = outcome;
      RunningTime = runningTime;
    }
  }

  public static async Task<Result> SynthesizeAsync(Driver.Options options) {
    var evaluationBegan = DateTime.Now;
    SourceFile = new FileInfo(options.InputFile).FullName;
    VerificationUtils.Init();
    var source = await new StreamReader(SourceFile).ReadToEndAsync();
    var dafnyOptions = DafnyOptions.Create(new StringWriter(), TextReader.Null, Array.Empty<string>());
    var uri = new Uri(SourceFile);
    var consoleErrorReporter = new ConsoleErrorReporter(dafnyOptions);
    
    var resolvedProgram = Utils.Parse(consoleErrorReporter, source, resolve: true, uri: uri);
    var resolvedTargetModule = resolvedProgram.DefaultModuleDef.Children.OfType<LiteralModuleDecl>().First();
    var resolvedTarget = FindMemberDeclsWithAttributes(resolvedTargetModule, "synthesize").OfType<Predicate>().First(predicate => options.MethodName == null || 
      (predicate.Attributes != null && predicate.Attributes.Args != null && predicate.Attributes.Args.Count > 0 &&  (string)(predicate.Attributes.Args[0] as StringLiteralExpr)?.Value! == options.MethodName));
    var targetClass = (resolvedTarget!.Formals.First().Type.AsNonNullRefType.ResolvedClass as NonNullTypeDecl)?.Class as ClassDecl;
    if (options.LoadHeuristics != null) {
      Heuristic.LoadAll(options, resolvedProgram);
    }
    var targetType = new UserDefinedType(Token.NoToken, targetClass!.FullDafnyName, new List<Type>());
    var endState = GetState(targetType, resolvedTarget!.Formals.First().Name, resolvedTarget.Body);
    var result = await SynthesizeHelperAsync(options, targetClass, endState, "result", resolvedProgram);
    if (result == null && DateTime.Now - options.StartTime > new TimeSpan(options.TimeLimit * TimeSpan.TicksPerSecond)) {
      Driver.Log.Warn($"Have reached the allotted time limit of {options.TimeLimit} seconds. Terminating the search for solution.");
      await Console.Out.WriteLineAsync($"Have reached the allotted time limit of {options.TimeLimit} seconds. Terminating the search for solution.");
      return new Result(Outcome.Timeout, DateTime.Now - options.StartTime);
    }
    if (result != null) {
      var methodName = options.MethodName == null ? "solution" : options.MethodName;
      var methodBody = $"static method {methodName}() returns (result:{targetClass.Name})\n" +
                       $"ensures fresh(result) && {resolvedTarget.Name}(result)\n" +
                       $"{{\n" +
                       $"{string.Join("\n", result.ConvertAll(statement => Printer.StatementToString(DafnyOptions.Default, statement)))}\n" +
                       $"}}";
      Driver.Log.Info($"Have found the following solution!\n{methodBody}");
      await Console.Out.WriteLineAsync($"{methodBody}");
      Driver.Log.Info($"Total time spend on synthesis: {DateTime.Now - evaluationBegan}");
      foreach (VerificationUtils.QueryType queryType in Enum.GetValues(typeof(VerificationUtils.QueryType))) {
        Driver.Log.Info(
          $"Total number of {queryType} queries to Dafny: {VerificationUtils.DafnyQueryCount[queryType]} ({VerificationUtils.DafnyQueryTime[queryType]})");
      }
      return new Result(Outcome.Success, DateTime.Now - options.StartTime);
    }
    
    Driver.Log.Info("Have enumerated all possible states and could not find a solution.");
    Driver.Log.Info($"Total time spend on synthesis: {DateTime.Now - evaluationBegan}");
    foreach (VerificationUtils.QueryType queryType in Enum.GetValues(typeof(VerificationUtils.QueryType))) {
      Driver.Log.Info(
        $"Total number of {queryType} queries to Dafny: {VerificationUtils.DafnyQueryCount[queryType]} ({VerificationUtils.DafnyQueryTime[queryType]})");
    }
    await Console.Out.WriteLineAsync("Failed to synthesized a solution.");
    return new Result(Outcome.Fail, DateTime.Now - options.StartTime);
  }

  private static async Task<List<Statement>?> SynthesizeHelperAsync(Driver.Options options, ClassDecl resolvedClassDeclaration, State endState, string receiverName, Program resolvedProgram) {
    var evaluationBegan = DateTime.Now;
    Dictionary<VerificationUtils.QueryType, int> priorDafnyQueryCount = new();
    Dictionary<VerificationUtils.QueryType, TimeSpan> priorDafnyQueryTime = new();
    foreach (VerificationUtils.QueryType queryType in Enum.GetValues(typeof(VerificationUtils.QueryType))) {
      priorDafnyQueryTime[queryType] = VerificationUtils.DafnyQueryTime[queryType];
      priorDafnyQueryCount[queryType] = VerificationUtils.DafnyQueryCount[queryType];
    }
    
    var heuristic = Heuristic.Get(options, resolvedClassDeclaration);
    await heuristic.UpdateHeuristicWithNewPropertiesAsync(endState.Keys.Select(indexedProperty => indexedProperty.Property).ToList());
    var targetType = new UserDefinedType(Token.NoToken, resolvedClassDeclaration.FullDafnyName, new List<Type>());
    var fringe = new PriorityQueue<SearchNode, double>();
    var explored = new HashSet<State>() {endState}; // States already explored
    var endStateEstimate = heuristic.EstimateDistanceFromStartState(endState);
    Driver.Log.Info($"Initial heuristic value is {endStateEstimate}");
    fringe.Enqueue(new SearchNode(new List<Statement>(), new List<Method>(), endState, endStateEstimate, 0), endStateEstimate);
    List<Statement> solution = new List<Statement>();
    while (fringe.Count != 0) {
      var next = fringe.Dequeue();
      // since the heuristic is guaranteed to give a lower bound,
      // the query below will only succeed if the lower bound is 0
      Driver.Log.Info($"Expanding method sequence {string.Join(", ", next.Methods.Select(method => method.Name))} -- " +
               $"estimated distance to start = {next.EstimatedDistanceToStartState}, " +
               $"distance to end = {next.DistanceToEndState}");
      if (next.EstimatedDistanceToStartState == 0) {
        var constructor = resolvedClassDeclaration.Members.OfType<Constructor>().First();
        var query2 = new DafnyQuery(resolvedClassDeclaration.FullDafnyName, resolvedClassDeclaration.FullDafnyName,
          new List<Method> { constructor }, new State(targetType, ""),
          next.State);
        var constraints = await query2.InferMethodArgumentsAndObjectStateAsync(VerificationUtils.QueryType.Regular, DefaultTimeLimit, false);
        if (constraints.method != null) {
          var updateStatements = new SolutionFormatter().Format(constraints.method.Body.Body, receiverName).Concat(next.Solution);
          var methodBody = $"{{\n" +
                           $"{string.Join("\n", updateStatements.Select(statement => Printer.StatementToString(DafnyOptions.Default, statement)))}\n" +
                           $"}}";
          Driver.Log.Info($"Have found the following solution to a subproblem!\n{methodBody}");
          Driver.Log.Info($"Time spend on subproblem: {DateTime.Now - evaluationBegan}");
          foreach (VerificationUtils.QueryType queryType in Enum.GetValues(typeof(VerificationUtils.QueryType))) {
            Driver.Log.Info(
              $"Number of {queryType} queries to Dafny used to solve subproblem: {VerificationUtils.DafnyQueryCount[queryType] - priorDafnyQueryCount[queryType]} ({VerificationUtils.DafnyQueryTime[queryType] - priorDafnyQueryTime[queryType]})");
          }
          solution = updateStatements.ToList();
          break;
        }
      }
      foreach (var method in heuristic.Methods) {
        if (DateTime.Now - options.StartTime > new TimeSpan(options.TimeLimit * TimeSpan.TicksPerSecond)) {
          return null;
        }

        if (explored.Count > 1) {
          var heuristicImproved = await heuristic.TryImproveHeuristicAsync();
          if (heuristicImproved) {
            var newFringe = new PriorityQueue<SearchNode, double>();
            while (fringe.Count != 0) {
              var searchNode = fringe.Dequeue();
              var newEstimate = heuristic.EstimateDistanceFromStartState(searchNode.State);
              newFringe.Enqueue(searchNode with { EstimatedDistanceToStartState = newEstimate },
                newEstimate * HeuristicWeight + searchNode.DistanceToEndState);
            }
 
            fringe = newFringe;
          }
        }

        Driver.Log.Debug("Trying method sequence: " + string.Join(", ", next.Methods.Prepend(method).Select(method => method.Name)));
        
        
        var query = new DafnyQuery(resolvedClassDeclaration.FullDafnyName, resolvedClassDeclaration.FullDafnyName,
          new List<Method> { method }, next.State.Negate(),
          next.State);
        var negation = await query.InferMethodArgumentsAndObjectStateAsync(VerificationUtils.QueryType.Regular, DefaultTimeLimit, false);
        var previous = negation.state;
        if (previous == null) {
          continue;
        }

        // create a state defined only by the properties shared between next and previous:
        var previousSimplified = new State(targetType, "");
        foreach (var indexedProperty in previous.Keys.OrderBy(key => key.Index)) {
          // TODO: there might be well-formedness issues arising here, need to double-check this is sound
          var matchingProperty = next.State.Keys.FirstOrDefault(key => key.Property == indexedProperty.Property);
          if (matchingProperty == null) {
            continue;
          }
          if (next.State[matchingProperty] == previous[indexedProperty]) {
            previousSimplified[indexedProperty] = previous[indexedProperty];
          }
        }

        // Reuse the method synthesized above for transforming previous to next 
        // and see if the same method transforms previousSimplified to next
        List<AssumeStmt> argumentAssumptions = new List<AssumeStmt>();
        List<Formal> argumentFormals = new List<Formal>();
        if (method.Ins.Any()) {
          var lastArgumentAssumption = negation.method.Body.Body.OfType<AssumeStmt>()
            .Last(statement => statement.Attributes != null && statement.Attributes.Name == State.AssumptionDescribesArgumentAttribute);
          var lastArgumentAssumptionIndex = negation.method.Body.Body.IndexOf(lastArgumentAssumption);
          argumentAssumptions = negation.method.Body.Body.Take(lastArgumentAssumptionIndex + 1).OfType<AssumeStmt>()
            .ToList();
          argumentFormals = negation.method.Ins.Where(formal => formal.Name.StartsWith(State.FormalNamePrefix)).ToList();
        }
        Driver.Log.Debug("Trying simplification:");
        var querySimplified = new DafnyQuery(query.id, resolvedClassDeclaration.FullDafnyName, resolvedClassDeclaration.FullDafnyName,
          new List<Method> { method }, previousSimplified, next.State, argumentAssumptions, null, argumentFormals);
        var canBeSimplified = await querySimplified.VerifyAsync(VerificationUtils.QueryType.Simplify, false, SimplificationTimeLimit);
        if (canBeSimplified == VerificationResult.Status.Verified) {
          negation = new(previousSimplified, negation.method);
          previous = previousSimplified;
        }
      
        if (explored.Contains(previous)) {
          continue;
        }

        var queryMethod = negation.method;
        explored.Add(previous);
        
        await heuristic.UpdateHeuristicWithNewPropertiesAsync(previous.Keys.Select(indexedProperty => indexedProperty.Property).ToList());
        

        var stateEstimate = heuristic.EstimateDistanceFromStartState(previous);
        Driver.Log.Info($"The following method sequence is possible (heuristic={stateEstimate}): " +
                 string.Join(", ", next.Methods.Prepend(method).Select(method => method.Name)));
        Driver.Log.Info($"New state is {previous}");

        var statements = new SolutionFormatter().Format(queryMethod.Body.Body, receiverName).Concat(next.Solution).ToList();
        fringe.Enqueue(
          new SearchNode(statements, next.Methods.Prepend(method).ToList(), previous, stateEstimate,
            next.DistanceToEndState + 1),
          stateEstimate * HeuristicWeight + next.DistanceToEndState + 1);
        
      }
    }

    if (!solution.Any()) {
      Driver.Log.Info("Have enumerated all possible states and could not find a solution to a subproblem.");
      Driver.Log.Info($"Time spend on subproblem: {DateTime.Now - evaluationBegan}");
      foreach (VerificationUtils.QueryType queryType in Enum.GetValues(typeof(VerificationUtils.QueryType))) {
        Driver.Log.Info(
          $"Number of {queryType} queries to Dafny used to solve subproblem: {VerificationUtils.DafnyQueryCount[queryType] - priorDafnyQueryCount[queryType]} ({VerificationUtils.DafnyQueryTime[queryType] - priorDafnyQueryTime[queryType]})");

      }
      return null;
    }

    var statementsToReplace = new HashSet<Statement>();
    foreach (var statement in solution) {
      if (statement is AssumeStmt && statement.Attributes != null &&
          statement.Attributes.Name == State.AssumptionDescribesArgumentAttribute &&
          statementsToReplace.All(toReplace => toReplace.Attributes.Args.First().ToString() != statement.Attributes.Args.First().ToString()) &&
          VerificationUtils.FindClass(State.GetById(int.Parse(statement.Attributes.Args.First().ToString()!)).Type, resolvedProgram) != null) {
        statementsToReplace.Add(statement);
      }
    }

    foreach (var statement in statementsToReplace) {
      var index = solution.IndexOf(statement);
      var subProblemState = State.GetById(int.Parse(statement.Attributes.Args.First().ToString()!));
      solution.RemoveAll(stmt => stmt.Attributes != null && statement.Attributes.Args.First().ToString() == stmt.Attributes.Args.First().ToString());
      var subProblemClassDeclaration = VerificationUtils.FindClass(subProblemState.Type, resolvedProgram);
      var replacement = await SynthesizeHelperAsync(options, subProblemClassDeclaration!, subProblemState, subProblemState.ReceiverName, resolvedProgram);
      if (replacement == null) {
        if (DateTime.Now - options.StartTime <= new TimeSpan(options.TimeLimit * TimeSpan.TicksPerSecond)) {
          Driver.Log.Error("Cannot solve a subproblem!");
        }
        return null;
      }
      var creationStatement = replacement.OfType<UpdateStmt>().First(updateStmt => updateStmt.Lhss.First() is IdentifierExpr identifierExpr &&
        identifierExpr.Name == subProblemState.ReceiverName);
      var creationStatementIndex = replacement.IndexOf(creationStatement);
      replacement.Remove(creationStatement);
      replacement.Insert(creationStatementIndex, new VarDeclStmt(RangeToken.NoToken, new List<LocalVariable>() {new(RangeToken.NoToken, subProblemState.ReceiverName, new InferredTypeProxy(), false)}, creationStatement));
      for (int i = 0; i < replacement.Count; i++) {
        solution.Insert(index + i, replacement[i]);
      }
    }
    return solution;

  }

  private static State GetState(Type targetType, string receiverName, Expression? constraint) {
    var state = new State(targetType, "");
    if (constraint == null) {
      return state;
    }
    int id = 0;
    var converter = new VerificationUtils.IdentifierToThisExpressionConverter(receiverName);
    foreach (var subGoal in GetSubGoals(constraint)) {
      var property = Property.GetProperty(targetType, converter.CloneExpr(subGoal));
      state[new IndexedProperty(property, id++)] = true;
    }
    return state;
  }
  
  private static List<Expression> GetSubGoals(Expression goal) {
    if (goal is not BinaryExpr { Op: BinaryExpr.Opcode.And } binaryExpr) {
      return new() { goal };
    }
    List<Expression> result = new();
    result.AddRange(GetSubGoals(binaryExpr.E0));
    result.AddRange(GetSubGoals(binaryExpr.E1));
    return result;
  }

  public static IEnumerable<MemberDecl> FindMemberDeclsWithAttributes(TopLevelDecl decl, string attribute) {
    var result = new List<MemberDecl>();
    if (decl is LiteralModuleDecl moduleDecl) {
      foreach (var innerDecl in moduleDecl.ModuleDef.TopLevelDecls) {
        result.AddRange(FindMemberDeclsWithAttributes(innerDecl, attribute));
      }
    }

    if (decl is TopLevelDeclWithMembers withMembers) {
      foreach (var innerDecl in withMembers.Members) {
        result.AddRange(FindMemberDeclsWithAttributes(innerDecl, attribute));
      }
    }

    return result;
  }

  private static IEnumerable<MemberDecl> FindMemberDeclsWithAttributes(MemberDecl member, string attribute) {
    var attributes = member.Attributes;
    while (attributes != null) {
      if (attributes.Name == attribute) {
        return new List<MemberDecl> { member };
      }

      attributes = attributes.Prev;
    }
    return new List<MemberDecl>();
  }


  public class SolutionFormatter : Cloner {

    private Dictionary<string, Expression> substMap = new();

    // Perform constant propagation on the solution to prepare it for printing in human-readable format
    public List<Statement> Format(List<Statement> solution, string receiverName, bool intermediate = false) {
      // Create a substitution dictionary that maps an identifier to the constant value it should correspond to
      substMap = new Dictionary<string, Expression>();
      var statementsToPrint = new List<Statement>();
      foreach (var statement in solution) {
        // only propagate clauses like `assume var_a == lit || var_b`
        if (statement is AssumeStmt { Expr: BinaryExpr 
              { Op: BinaryExpr.Opcode.Eq, E0: IdentifierExpr identifierExpr, E1: LiteralExpr or IdentifierExpr or DatatypeValue or SeqDisplayExpr} binaryExpr }) {
          substMap[identifierExpr.Name] = binaryExpr.E1;
        } else {
          statementsToPrint.Add(statement);
        }
      }

      // Propagate constants through substMap:
      var propagationShouldContinue = true;
      while (propagationShouldContinue) {
        propagationShouldContinue = false;
        foreach (var identifierName in substMap.Keys) {
          if (substMap[identifierName] is IdentifierExpr identifierExpr && substMap.ContainsKey(identifierExpr.Name)) {
            substMap[identifierName] = substMap[identifierExpr.Name];
            propagationShouldContinue = true;
          }
        }
      }
      
      substMap[DafnyQuery.ReceiverName] = new IdentifierExpr(Token.NoToken, receiverName);
      // Now apply the substitution map to all method calls:
      var identifierExpressionFinder = new IdentifierExpressionFinder();
      if (intermediate) {
        return statementsToPrint.Select(CloneStmt).Where(stmt => identifierExpressionFinder.IdentifierExprFound(stmt)).ToList();
      }
      return statementsToPrint
        .Select(statement => CloneStmt(statement))
        .Where(statement =>  statement is not AssertStmt && (statement is not AssumeStmt assumeStmt || (assumeStmt.Attributes?.Name == State.AssumptionDescribesArgumentAttribute && identifierExpressionFinder.IdentifierExprFound(statement)))).ToList();
    }

    public override Expression CloneExpr(Expression expr) {
      if (expr is IdentifierExpr identifierExpr && substMap.TryGetValue(identifierExpr.Name, out var cloneExpr)) {
        if (cloneExpr.ToString() != expr.ToString()) {
          return CloneExpr((cloneExpr));
        }
        return cloneExpr;
      }
      return base.CloneExpr(expr);
    }
  }
  
  public class IdentifierExpressionFinder : Cloner {

    private bool identifierExprFound = false;

    public bool IdentifierExprFound(Statement statement) {
      identifierExprFound = false;
      CloneStmt(statement);
      return identifierExprFound;
    }

    public override Expression CloneExpr(Expression expr) {
      if (expr is IdentifierExpr) {
        identifierExprFound = true;
      }
      return base.CloneExpr(expr);
    }
  }

}
