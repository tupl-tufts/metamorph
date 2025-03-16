#nullable disable

using Microsoft.Dafny;
using Type = Microsoft.Dafny.Type;

namespace Synthesis;

public class DafnyQuery {

  public const string ReceiverName = "receiver"; // the name of the object being modified/constructed
  private const string ArgumentNamePrefix = "argument_"; // method arguments' names start with this prefix
  public const string DefaultMethodName = "synthesized"; // method that forms the Dafny query will have this name
  
  private static int nextId = 0; 
  public readonly int id; // the identifier associated with the Dafny query, which is unique by default
  
  private readonly string qualifiedClassName; // the name of the class whose instance is being created/modified
  private readonly string qualifiedSynthesizedClassName; // the name of the class where the synthesized method should be added
  private readonly State before; // state of the object prior to methods calls
  private readonly State after; // state to be asserted after the method calls
  private readonly List<AssumeStmt> additionalAssumptions; // assumptions to insert at the beginning of the query
  private readonly List<AssertStmt> additionalAssertions; // assertions to add at the end
  private readonly List<Formal> additionalFormals; // formal parameters to add to the method
  private readonly List<Method> methods; // methods being called after assumptions and before assertions

  public static void Clear() {
    nextId = 0;
  }

  
  /// <summary>
  /// Default constructor, which automatically sets the query's id to a new unique value
  /// </summary>
  public DafnyQuery(
    string qualifiedClassName,
    string qualifiedSynthesizedClassName,
    IEnumerable<Method> methods,
    State before, State after,
    List<AssumeStmt> additionalAssumptions = null,
    List<AssertStmt> additionalAssertions = null,
    List<Formal> additionalFormals = null)
    : this(Interlocked.Increment(ref nextId), qualifiedClassName, qualifiedSynthesizedClassName, methods, before, after, additionalAssumptions,
      additionalAssertions, additionalFormals) {
  }

  public DafnyQuery(
    int id, 
    string qualifiedClassName, 
    string qualifiedSynthesizedClassName,
    IEnumerable<Method> methods, 
    State before,
    State after, 
    List<AssumeStmt> additionalAssumptions=null,
    List<AssertStmt> additionalAssertions=null, 
    List<Formal> additionalFormals=null) {
    this.qualifiedClassName = qualifiedClassName;
    this.qualifiedSynthesizedClassName = qualifiedSynthesizedClassName;
    this.methods = methods.ToList();
    this.before = before;
    this.after = after;
    this.id = id;
    this.additionalAssumptions = additionalAssumptions ?? new List<AssumeStmt>();
    this.additionalAssertions = additionalAssertions ?? new List<AssertStmt>();
    // The next two lines ensure that the assertions are not ignored downstream in VerificationUtils
    this.additionalAssertions.ForEach(assertion =>
      assertion.Attributes = new Attributes(VerificationUtils.KeepAssertionAttribute, new(), null));
    this.additionalFormals = additionalFormals ?? new List<Formal>();
  }

  /// <summary>
  /// Verify the assertions in this query (both those arising from the after state and the additionalAssertions field)
  /// hold once we assume all available assumptions and perform the specified method calls.
  /// </summary>
  /// If <param name="assumeAllPreconditions"></param> is true, assume all requires clauses and wellformedness checks.
  /// <returns>Verification status of the query</returns>
  public async Task<VerificationResult.Status> VerifyAsync(VerificationUtils.QueryType queryType, bool assumeAllPreconditions, uint timeLimit) {
    return (await VerifyHelperAsync(queryType, assumeAllPreconditions, timeLimit)).result.ResultStatus;
  }

  /// <summary>
  /// A helper function for the above that returns not only the verification status but also the counterexample, if
  /// present, and the method that was constructed to perform the query
  /// </summary>
  private async Task<(VerificationResult result, Method method)> VerifyHelperAsync(VerificationUtils.QueryType queryType, bool assumeAllPreconditions, uint timeLimit) {
    var formalsDictionary = new Dictionary<int, Dictionary<IndexedProperty, List<Formal>>>();
    var assumption = before.AsAssumption(formalsDictionary, ReceiverName);
    foreach (var assumeStmt in additionalAssumptions) {
      assumption.Insert(0, assumeStmt);
    }

    var assertion = after.AsAssertion(formalsDictionary, ReceiverName);
    foreach (var assertStmt in additionalAssertions) {
      assertion.Insert(0, assertStmt);
    }

    var formals = formalsDictionary.Values.SelectMany(i => i.Values.SelectMany(formal => formal)).ToList();
    foreach (var formal in additionalFormals) {
      if (formals.Any(existing => existing.Name == formal.Name)) {
        continue;
      }

      formals.Add(formal);
    }

    var modifies = new List<FrameExpression>();
    var methodCalls = ConstructMethodCalls(formals, modifies, assumeAllPreconditions);
    var method = CreateSynthesizedMethod(
      assumption.Concat(methodCalls).Concat(assertion),
      formals,
      new Uri(Search.SourceFile),
      !methods.OfType<Constructor>().Any(),
      modifies);
    var result = await VerificationUtils.VerifyMethodAsync(queryType, qualifiedSynthesizedClassName, method, assumeAllPreconditions, timeLimit);
    return new(result, method);
  }

  /// <summary>
  /// Infer method arguments and the initial object state that ensure the correctness of the Dafny Query.
  /// This requires two queries to the solver. First, we assert the opposite of what we need, then we take the
  /// counterexample and reconstruct from it the arguments and the state. Next, we substitute these in and verify that
  /// they do indeed make the query go through. 
  /// </summary>
  /// <returns></returns>
  public async Task<(State state, Method method)> InferMethodArgumentsAndObjectStateAsync(VerificationUtils.QueryType queryType, uint timeLimit, bool getStateAfter) {
    if (additionalAssertions.Count > 0) {
      // To support additionalAssertions here, we would have to join together the after state and the
      // additionalAssertions into one expression, which is tedious and currently has not uses
      Driver.Log.Fatal("Attempted to infer method arguments when additionaAssertions is not empty.");
      Environment.Exit(1); 
    }
    var reversedDafnyQuery = new DafnyQuery(id, qualifiedSynthesizedClassName, qualifiedSynthesizedClassName, methods, before, after.Negate(),
      additionalAssumptions, new List<AssertStmt>(), additionalFormals);
    var queryResult = await reversedDafnyQuery.VerifyHelperAsync(queryType, true, timeLimit);
    if (queryResult.result.ResultStatus != VerificationResult.Status.Counterexample) {
      return new(null, null); // No fitting method arguments exist or there is a timeout
    }

    if (methods.First() is Constructor) { // when the first method is a constructor, there is no initial object state
      // TODO: this can potentially be optimized when methods.All(method => method.Ins.Count == 0)
      var transformingMethod = await GetTransformingMethodFromCounterexampleAsync(queryType, queryResult.result, queryResult.method, timeLimit);
      if (getStateAfter) {
        return new(queryResult.result.CounterexampleFor(ReceiverName, true), transformingMethod);
      }
      return new(null, transformingMethod);
    }
    
    var formalsDictionary = new Dictionary<int, Dictionary<IndexedProperty, List<Formal>>>();
    var assumption = queryResult.result.CounterexampleFor(ReceiverName)!.AsAssumption(formalsDictionary, ReceiverName);
    var assertion = after.Negate().AsAssertion(formalsDictionary, ReceiverName);
    var formals = formalsDictionary.Values.SelectMany(i => i.Values.SelectMany(formal => formal)).ToList();
    
    List<FrameExpression> modifies = new List<FrameExpression>();
    var methodCalls = ConstructMethodCalls(formals, modifies, assumeAllRequires: false);
    var method = CreateSynthesizedMethod(
      assumption.Concat(methodCalls).Concat(assertion),
      formals,
      new Uri(Search.SourceFile),
      true,
      modifies);
    method = await GetTransformingMethodFromCounterexampleAsync(queryType, queryResult.result, method, timeLimit);
    if (method == null) {
      return new(null, null);
    }

    if (getStateAfter) {
      return new(queryResult.result.CounterexampleFor(ReceiverName, true), method);
    }
    return new(queryResult.result.CounterexampleFor(ReceiverName), method);
  }
  
  
  // -------------------------------------------------------------------------------------------------------------------
  // PRIVATE HELPER METHODS --------------------------------------------------------------------------------------------
  // -------------------------------------------------------------------------------------------------------------------

  
  /// <summary>
  /// Take a dafny query and a counterexample from the opposite of this query and substitute information from the
  /// counterexample about the method arguments into this query, then verify it.
  /// </summary>
  /// <param name="previousResult">result of the opposite query, which includes the counterexample</param>
  /// <param name="method">method to insert new assumptions into</param>
  /// <param name="timeLimit">how long the solver query is allowed to run for</param>
  /// <returns></returns>
  private async Task<Method> GetTransformingMethodFromCounterexampleAsync(
    VerificationUtils.QueryType queryType,
    VerificationResult previousResult,
    Method method,
    uint timeLimit) {
    var formalsDictionary = new Dictionary<int, Dictionary<IndexedProperty, List<Formal>>>();
    var nextStatementId = 0;
    foreach (var formal in method.Ins.Where(formal => formal.Name.StartsWith(ArgumentNamePrefix + id + "_"))) {
      var state = previousResult.CounterexampleFor(formal.Name);
      if (state != null) {
        var stateAsAssumption = state.AsAssumption(formalsDictionary, formal.Name);
        for (int i = 0; i < stateAsAssumption.Count; i++) {
          method.Body.Body.Insert(nextStatementId++, stateAsAssumption[i]);
        }
      }
    }

    method.Ins.AddRange(formalsDictionary.Values.SelectMany(i => i.Values.SelectMany(formal => formal))
      .Where(formal => method.Ins.All(existing => existing.Name != formal.Name)));
    // We need to revert the assertion here:
    var oldAssertions = method.Body.Body.OfType<AssertStmt>().ToList();
    Expression newAssertion = null;
    foreach (var assertion in oldAssertions) {
      method.Body.Body.Remove(assertion);
      var negatedAssertExpression = assertion.Expr is UnaryOpExpr { Op: UnaryOpExpr.Opcode.Not } unaryOpExpr
        ? unaryOpExpr.E
        : new UnaryOpExpr(Token.NoToken, UnaryOpExpr.Opcode.Not, assertion.Expr);
      if (newAssertion == null) {
        newAssertion = negatedAssertExpression;
      } else {
        newAssertion = new BinaryExpr(Token.NoToken, BinaryExpr.Opcode.Or, newAssertion, negatedAssertExpression);
      }
    }

    method.Body.Body.Add(new AssertStmt(RangeToken.NoToken, newAssertion, null, null,
      new Attributes(VerificationUtils.KeepAssertionAttribute, new(), null)));
    var result = await VerificationUtils.VerifyMethodAsync(queryType, qualifiedSynthesizedClassName, method, false, timeLimit);
    if (result.ResultStatus == VerificationResult.Status.Verified) {
      return method;
    }

    return null;
  }

  /// <summary>
  /// Creates a method that corresponds to the proposed dafny query
  /// </summary>
  private Method CreateSynthesizedMethod(
    IEnumerable<Statement> statements,
    IEnumerable<Formal> formals,
    Uri uri,
    bool receiverAsFormal,
    List<FrameExpression> modifies) {
    var receiver = new Formal(Token.NoToken, ReceiverName,
      new UserDefinedType(Token.NoToken, qualifiedClassName.Split(".").Last(), null), receiverAsFormal, false, null);
    var method = new Method(
      new RangeToken(Token.NoToken, Token.NoToken),
      new Name(new RangeToken(Token.NoToken, Token.NoToken), DefaultMethodName),
      true,
      false,
      new List<TypeParameter>(),
      receiverAsFormal ? formals.Append(receiver).ToList() : formals.ToList(),
      !receiverAsFormal ? new List<Formal> { receiver } : new(),
      new(),
      new(),
      new Specification<FrameExpression>(modifies, null),
      new(),
      new Specification<Expression>(new List<Expression>(), null),
      new BlockStmt(new RangeToken(Token.NoToken, Token.NoToken), statements.ToList()),
      null,
      null
    );
    method.Tok.Uri = uri;
    return method;
  }

  /// <summary> Constructs a series of method calls and assumes the requires clauses, when necessary. </summary>
  private List<Statement> ConstructMethodCalls(List<Formal> formals, 
    List<FrameExpression> modifies,
    bool assumeAllRequires) {
    var statements = new List<Statement>();
    for (int i = methods.Count - 1; i >= 0; i--) {
      var newFormals = new List<Formal>();
      for (int j = 0; j < methods[i].Ins.Count; j++) {
        newFormals.Add(new Formal(
          Token.NoToken,
          $"{ArgumentNamePrefix}{id}_{i}_{j}",
          methods[i].Ins[j].Type,
          true,
          false,
          null));
      }

      formals.AddRange(newFormals);

      var receiver = new IdentifierExpr(Token.NoToken,
        new Formal(Token.NoToken, ReceiverName,
          new UserDefinedType(Token.NoToken, qualifiedClassName.Split(".").Last() + "?", null),
          true, true, null));
      var requiresExpression = RequiresToExpression(methods[i], newFormals, receiver);
      var substMap = new Dictionary<string, string>();
      var modifiesConstructor = new ModifiesConstructor();
      for (int k = 0; k < newFormals.Count; k++) {
        substMap[methods[i].Ins[k].Name] = newFormals[k].Name;
      }

      modifies.AddRange(methods[i].Mod.Expressions
        .ConvertAll(frameExpr => modifiesConstructor.FormatFrameExpression(frameExpr, substMap, receiver.Name)));
      if (methods[i] is Constructor constructor) {
        statements.Insert(0, new UpdateStmt(new RangeToken(Token.NoToken, Token.NoToken),
          new List<Expression> { receiver },
          new List<AssignmentRhs> {
            new TypeRhs(
              Token.NoToken,
              new UserDefinedType(Token.NoToken, constructor.EnclosingClass.Name, null),
              newFormals.Select(formal => new ActualBinding(
                  null,
                  new IdentifierExpr(Token.NoToken, formal.Name)))
                .ToList())
          }));
        if (assumeAllRequires) {
          statements.Insert(0, new AssumeStmt(RangeToken.NoToken, requiresExpression, null));
        }

        continue;
      }

      var updateStmt = new UpdateStmt(new RangeToken(Token.NoToken, Token.NoToken),
        methods[i].Outs.ConvertAll(_ => new IdentifierExpr(Token.NoToken, "_") as Expression),
        new List<AssignmentRhs> {
          new ExprRhs(new ApplySuffix(
            Token.NoToken,
            null,
            new ExprDotName(Token.NoToken, receiver, methods[i].Name, null),
            newFormals.Select(formal =>
              new ActualBinding(null, new IdentifierExpr(Token.NoToken, formal.Name))).ToList(),
            Token.NoToken))
        });
      if (methods[i].Outs.Count > 0) {
        var varDeclStmt = new VarDeclStmt(RangeToken.NoToken,
          methods[i].Outs.ConvertAll(_ => new LocalVariable(RangeToken.NoToken, "_", new InferredTypeProxy(), false)),
          updateStmt);
        statements.Insert(0, varDeclStmt);
      } else {
        statements.Insert(0, updateStmt);
      }

      if (assumeAllRequires) {
        statements.Insert(0, new AssumeStmt(RangeToken.NoToken, requiresExpression, null));
      }

    }

    return statements;
  }
  
  public override string ToString() {
    return $"{before} -> {string.Join(", ", methods.ConvertAll(method => method.Name))} -> {after}";
  }

  
  // -------------------------------------------------------------------------------------------------------------------
  // UTILITIES FOR AST MANIPULATION ------------------------------------------------------------------------------------
  // -------------------------------------------------------------------------------------------------------------------


  /// <summary>
  /// Converts requires clauses on a method to an expression that, if asserted to be true, allows executing the method.
  /// </summary>
  private static Expression RequiresToExpression(
    Method method, 
    List<Formal> formals, 
    IdentifierExpr receiver) {
    Expression expression = new LiteralExpr(Token.NoToken, true);
    Dictionary<IVariable, Expression> substMap = new();
    for (int i = 0; i < method.Ins.Count; i++) {
      substMap[method.Ins[i]] = new IdentifierExpr(Token.NoToken, formals[i]);
    }
    var substituter = new Substituter(receiver, substMap, new());
    foreach (var clause in method.Req) {
      expression = new BinaryExpr(Token.NoToken, BinaryExpr.Opcode.And, expression,
        substituter.Substitute(clause.E));
    }
    return expression;
  }


  private class ModifiesConstructor : Cloner {
    
    public ModifiesConstructor():base(true, true) {}

    private Dictionary<string, string> substMap = new();
    private string receiverName;

    // Perform constant propagation on the solution to prepare it for printing in human-readable format
    public FrameExpression FormatFrameExpression(
      FrameExpression expression, 
      Dictionary<string, string> substMap,
      string receiverName) {
      // Create a substitution dictionary that maps an identifier to the constant value it should correspond to
      this.substMap = substMap;
      this.receiverName = receiverName;
      return new FrameExpression(Token.NoToken, CloneExpr(expression.E.Resolved), expression.FieldName);
    }

    public override Expression CloneExpr(Expression expr) {
      if (expr is IdentifierExpr identifierExpr && substMap.TryGetValue(identifierExpr.Name, out _)) {
        return new IdentifierExpr(Token.NoToken, substMap[identifierExpr.Name]);
      }

      if (expr is ThisExpr) {
        return new IdentifierExpr(Token.NoToken, receiverName);
      }
      return base.CloneExpr(expr);
    }
  }
}
