using Google.OrTools.LinearSolver;
using Microsoft.Dafny;
using NLog;

namespace Synthesis;

public class Heuristic {

  private Driver.Options options;
  // The heuristic attempts to estimate the maximum number of properties of the same kind that a single method call
  // can affect. Estimating the exact number can be costly (requires iterative queries to Dafny), so we put a cap 
  // on this. If a method can affect as many as AffectedPropertiesCap properties of some kind at the same time,
  // we simply assume the method can affect infinitely many of them (an example of such a method would be a method
  // that zeroes all the values in an array).
  private const int AffectedPropertiesCap = 3;

  //private readonly State startState; // the initial state of the object
  private readonly Method[] methods; // methods available to the synthesis tool
  public IEnumerable<Method> Methods => methods.ToList();
  private string type => constructor.EnclosingClass.FullDafnyName; // We want to have a separate Heuristic object for each class
  private readonly Constructor constructor; // 

  private readonly Dictionary<Property, PropertyValue> valueAtStart = new();

  private static readonly Dictionary<string, Heuristic> ClassNameToHeuristic = new();

  // queryResults[PropertyKind, From, To, Method] = number of properties of kind propertyKind that method Method
  // can transform from value From to value To
  private readonly Dictionary<Tuple<Property, PropertyValue, PropertyValue, Method>, int> queryResults = new();

  private Dictionary<Property, HashSet<Property>> propertiesToIndex = new(); 
  // maps parent properties to all concrete properties of this type that have not been indexed yet

  private Property? currentPropertyUnderAnalysis = null;

  private const uint QueryTimeLimit = 40;
    
  private enum PropertyValue {
    True,
    False,
    Undefined,
    NotTrue,
    NotFalse,
    Unknown
  }

  private static PropertyValue GetPropertyValue(string name) {
    foreach (PropertyValue value in Enum.GetValues(typeof(PropertyValue))) {
      if (string.Equals(value.ToString(), name, StringComparison.OrdinalIgnoreCase)) {
        return value;
      }
    }
    Driver.Log.Error($"Could not find property value type {name}");
    return PropertyValue.Unknown;
  }

  public static Heuristic Get(Driver.Options options, ClassDecl classDecl) {
    if (!ClassNameToHeuristic.ContainsKey(classDecl.FullDafnyName)) {
      ClassNameToHeuristic[classDecl.FullDafnyName] = new Heuristic(options, classDecl);
    }
    return ClassNameToHeuristic[classDecl.FullDafnyName];
  }

  public static void Clear() {
    ClassNameToHeuristic.Clear();
  }

  private Heuristic(Driver.Options options, ClassDecl classDecl) {
    // here we assume every class has a default consturctor, 
    // and the constructors do not have attributes
    if(classDecl.Members.OfType<Constructor>().All(ctor => ctor.Name != "_ctor")) {
      Driver.Log.Fatal($"Class {classDecl.FullDafnyName} does not have a constructor named _ctor.");
      Environment.Exit(1);
    }

    foreach (var ctor in classDecl.Members.OfType<Constructor>()) {
      var attrs = ctor.Attributes;
      while (attrs != null) {
        if (attrs.Name == "use") {
          Driver.Log.Fatal("Constructors cannot have {:use} attributes");
          Environment.Exit(1);
        }
        attrs = attrs.Prev;
      }
    }
    if (classDecl.Members.OfType<Constructor>().Count() > 1) {
      Driver.Log.Warn("Named constructors will not be considered, only the default constructor will be used");
    }
    constructor = classDecl.Members.OfType<Constructor>().First(); 
    // TODO: we do support returning objects from methods for now (we support primitive types like int)
    // not sure if it's easy to tell from the AST level
    methods = Search.FindMemberDeclsWithAttributes(classDecl, "use").OfType<Method>().ToArray();
    this.options = options;
  }

  /// <summary>
  /// Uses integer programming to combined known constraints and return a lower bound on the number of methods
  /// necessary to transform the object from its start state to the provided end state.
  /// </summary>
  public int EstimateDistanceFromStartState(State endState) {
    if (options.DisableHeuristic) {
      return 0;
    }
    if (methods.Length == 0) {
      Driver.Log.Info($"There are no methods available for class {constructor.EnclosingClass.FullDafnyName}, so we make no heuristic estimates.");
      return 0;
    }
    Driver.Log.Debug($"Using integer programming to estimating the distance to {endState}");
    var relevantProperties = new Dictionary<Property, (int FToT, int TToF, int NToT, int NToF, int NTToT, int NFtoF)>();
    foreach (var indexedProperty in endState.Keys) {
      var parent = indexedProperty.Property.Parent;
      PropertyValue startingValue = PropertyValue.Unknown;
      if (propertiesToIndex.ContainsKey(parent)) {
        continue;
      }
      if (valueAtStart.ContainsKey(parent) && valueAtStart[parent] != PropertyValue.Unknown) {
        startingValue = valueAtStart[parent];
      } 
      if (valueAtStart.ContainsKey(indexedProperty.Property) 
          && (startingValue == PropertyValue.Unknown || startingValue == PropertyValue.NotFalse || startingValue == PropertyValue.NotTrue)) {
        startingValue = valueAtStart[indexedProperty.Property];
      }

      if (startingValue == PropertyValue.Unknown) {
        continue;
      }

      if ((endState[indexedProperty] && startingValue is PropertyValue.True or PropertyValue.Unknown or PropertyValue.NotFalse) || 
          (!endState[indexedProperty] && (startingValue is PropertyValue.False or PropertyValue.Unknown or PropertyValue.NotTrue))) {
        continue;
      }

      var newCounts = relevantProperties.TryGetValue(parent, out var property) ? property : new(0, 0, 0, 0, 0, 0);
      if (endState[indexedProperty]) {
        if (startingValue == PropertyValue.False) {
          newCounts = new(newCounts.FToT + 1, newCounts.TToF, newCounts.NToT, newCounts.NToF, newCounts.NTToT, newCounts.NFtoF);
        } else if (startingValue == PropertyValue.Undefined) {
          newCounts = new(newCounts.FToT, newCounts.TToF, newCounts.NToT + 1, newCounts.NToF, newCounts.NTToT, newCounts.NFtoF);
        } else {
          newCounts = new(newCounts.FToT, newCounts.TToF, newCounts.NToT, newCounts.NToF, newCounts.NTToT + 1, newCounts.NFtoF);
        }
      } else {
        if (startingValue == PropertyValue.True) {
          newCounts = new(newCounts.FToT, newCounts.TToF + 1, newCounts.NToT, newCounts.NToF, newCounts.NTToT, newCounts.NFtoF);
        } else if (startingValue == PropertyValue.Undefined) {
          newCounts = new(newCounts.FToT, newCounts.TToF, newCounts.NToT, newCounts.NToF + 1, newCounts.NTToT, newCounts.NFtoF);
        } else {
          newCounts = new(newCounts.FToT, newCounts.TToF, newCounts.NToT, newCounts.NToF, newCounts.NTToT, newCounts.NFtoF + 1);
        }
      }

      relevantProperties[parent] = newCounts;
    }

    if (options.SUSHI) {
      var result = 0;
      foreach (var parent in relevantProperties.Keys) {
        result += relevantProperties[parent].FToT + relevantProperties[parent].TToF + relevantProperties[parent].NToT +
               relevantProperties[parent].NToF + relevantProperties[parent].NTToT + relevantProperties[parent].NFtoF;
      }

      return result;
    }

    var solver = Solver.CreateSolver("SCIP");
    var vars = new Dictionary<Method, Variable> {
      [methods[0]] = solver.MakeIntVar(0.0, double.PositiveInfinity, methods[0].Name)
    };
    LinearExpr sum = vars[methods[0]];
    for (int i = 1; i < methods.Length; i++) {
      vars[methods[i]] = solver.MakeIntVar(0.0, double.PositiveInfinity, methods[i].Name);
      sum += vars[methods[i]];
    }

    foreach (var property in relevantProperties.Keys) {
      AddConstraint(property, relevantProperties[property].NToT, PropertyValue.Undefined, PropertyValue.True, vars, solver);
      AddConstraint(property, relevantProperties[property].NToF, PropertyValue.Undefined, PropertyValue.False, vars, solver);
      AddConstraint(property, relevantProperties[property].TToF, PropertyValue.True, PropertyValue.False, vars, solver);
      AddConstraint(property, relevantProperties[property].FToT, PropertyValue.False, PropertyValue.True, vars, solver);
      AddTwoWayConstraint(property, relevantProperties[property].NTToT, PropertyValue.True, vars, solver);
      AddTwoWayConstraint(property, relevantProperties[property].NFtoF, PropertyValue.False, vars, solver);
    }

    Driver.Log.Trace($"Minimizing value: {sum}");
    solver.Minimize(sum);
    var resultStatus = solver.Solve();
    if (resultStatus != Solver.ResultStatus.OPTIMAL) {
      Driver.Log.Error("The integer programming problem does not have a solution!");
      return 0;
    }

    Driver.Log.Debug($"The integer programming gives the lower bound of {solver.Objective().Value()} methods.");
    foreach (var var in vars.Keys) {
      Driver.Log.Debug($"In particular, need at least {vars[var].SolutionValue()} calls to {var.Name}");
    }

    return (int)solver.Objective().Value();
  }


  /// <summary>
  /// Adds a constraint to the integer programming problem that puts a lower bound on the number of methods
  /// necessary to transform <param name="property"></param> from state <param name="from"></param> to state
  /// <param name="to"></param>.
  /// </summary>
  /// <returns></returns>
  private void AddConstraint(Property property,
    int constraint,
    PropertyValue from,
    PropertyValue to,
    IReadOnlyDictionary<Method, Variable> vars,
    Solver solver) {

    if (constraint <= 0) {
      return;
    }

    LinearExpr partial1 = null!;
    LinearExpr partial2 = null!;
    foreach (var method in methods) {
      if (queryResults[new(property, from, to, method)] == 0 &&
          queryResults[new(property, ThirdOneOut(from, to), to, method)] == 0 &&
          queryResults[new(property, from, ThirdOneOut(from, to), method)] == 0) {
        continue; // unnecessary but makes debugging easier
      }
      var directTerm = vars[method] * queryResults[new(property, from, to, method)];
      var partialTerm1 = vars[method] * queryResults[new(property, from, ThirdOneOut(from, to), method)];
      var partialTerm2 = vars[method] * queryResults[new(property, ThirdOneOut(from, to), to, method)];
      if (partial1 == null) {
        partial1 = directTerm + partialTerm1;
        partial2 = directTerm + partialTerm2;
      } else {
        partial1 += directTerm + partialTerm1;
        partial2 += directTerm + partialTerm2;
      }
    }

    var constraint1 = partial1 >= constraint;
    var constraint2 = partial2 >= constraint;
    Driver.Log.Trace($"Adding solver constraint for property {property} from {from} to {to}: {partial1} >= {constraint}");
    Driver.Log.Trace($"Adding solver constraint for property {property} from {from} to {to}: {partial2} >= {constraint}");
    solver.Add(constraint1);
    solver.Add(constraint2);
  }
  
  private void AddTwoWayConstraint(Property property,
    int constraint,
    PropertyValue to,
    IReadOnlyDictionary<Method, Variable> vars,
    Solver solver) {

    if (constraint <= 0) {
      return;
    }

    PropertyValue from1 = to == PropertyValue.True ? PropertyValue.False : PropertyValue.True;
    PropertyValue from2 = ThirdOneOut(from1, to);

    LinearExpr partial1 = null!;
    LinearExpr partial2 = null!;
    LinearExpr partial3 = null!;
    LinearExpr partial4 = null!;
    foreach (var method in methods) {
      if (queryResults[new(property, from1, to, method)] == 0 &&
          queryResults[new(property, from2, to, method)] == 0 &&
          queryResults[new(property, from1, from2, method)] == 0 &&
          queryResults[new(property, from2, from1, method)] == 0) {
        continue; // unnecessary but makes debugging easier
      }
      var directTerm1 = vars[method] * queryResults[new(property, from1, to, method)];
      var directTerm2 = vars[method] * queryResults[new(property, from2, to, method)];
      var partialTerm1To2 = vars[method] * queryResults[new(property, from1, from2, method)];
      var partialTerm2To1 = vars[method] * queryResults[new(property, from2, from1, method)];
      if (partial1 == null) {
        partial1 = directTerm1 + directTerm2 + partialTerm1To2 + partialTerm2To1;
        partial2 = 2 * directTerm1 + partialTerm1To2 + directTerm2;
        partial3 = 2 * directTerm2 + partialTerm2To1 + directTerm1;
        partial4 = 2 * directTerm2 + 2 * directTerm1;
      } else {
        partial1 += directTerm1 + directTerm2 + partialTerm1To2 + partialTerm2To1;
        partial2 += 2 * directTerm1 + partialTerm1To2 + directTerm2;
        partial3 += 2 * directTerm2 + partialTerm2To1 + directTerm1;
        partial4 += 2 * directTerm2 + 2 * directTerm1;
      }
    }

    var constraint1 = partial1 >= constraint;
    var constraint2 = partial2 >= constraint;
    var constraint3 = partial3 >= constraint;
    var constraint4 = partial4 >= constraint;
    Driver.Log.Trace($"Adding solver constraint for property {property} from uncertain to {to}: {partial1} >= {constraint}");
    Driver.Log.Trace($"Adding solver constraint for property {property} from uncertain to {to}: {partial2} >= {constraint}");
    Driver.Log.Trace($"Adding solver constraint for property {property} from uncertain to {to}: {partial3} >= {constraint}");
    Driver.Log.Trace($"Adding solver constraint for property {property} from uncertain to {to}: {partial4} >= {constraint}");
    solver.Add(constraint1);
    solver.Add(constraint2);
    solver.Add(constraint3);
    solver.Add(constraint4);
  }

  public async Task UpdateHeuristicWithNewPropertiesAsync(List<Property> properties) {
    if (options.DisableHeuristic) {
      return;
    }
    foreach (var property in properties) {
      var parent = property.Parent;
      if (valueAtStart.ContainsKey(parent)) {
        await UpdateStartStateWithNewPropertiesAsync(new List<Property>() {property}, false);
      } else {
        if (parent.Assignments.Count() > 3) {
          continue;
        }
        if (!propertiesToIndex.ContainsKey(parent)) {
          propertiesToIndex[parent] = new HashSet<Property>();
        }
        propertiesToIndex[parent].Add(property);
      }
    }
  }

  public static void SaveAll(Synthesis.Driver.Options options, Program resolvedProgram) {
    if (options.HeursticDir == null) {
      return;
    }
    if (!Directory.Exists(options.HeursticDir)) {
      Directory.CreateDirectory(options.HeursticDir);
    }
    foreach (var className in ClassNameToHeuristic.Keys) {
      var fileName = Path.Combine(options.HeursticDir, $"{className}.dfy");
      ClassNameToHeuristic[className].Save(fileName, resolvedProgram);
    }
  }

  public static void LoadAll(Synthesis.Driver.Options options, Program resolvedProgram) {
    if (options.HeursticDir == null) {
      return;
    }
    foreach (var fileName in Directory.EnumerateFiles(options.HeursticDir)) {
      if (!fileName.EndsWith(".dfy")) {
        continue;
      }
      var className = Path.GetFileName(fileName)[..^4];
      var classDeclaration = VerificationUtils.FindClass(className, resolvedProgram);
      if (classDeclaration != null) {
        ClassNameToHeuristic[className] = Load(fileName, options, classDeclaration);
      }
    }
  }

  private void Save(string fileName, Program resolvedProgram) {
    using StreamWriter writer = new StreamWriter(fileName);
    var id = 0;
    var includes = Path.GetRelativePath(Path.GetFullPath(Path.GetDirectoryName(fileName)!), Path.GetFullPath(Search.SourceFile));
    writer.WriteLine($"include \"{includes}\""); // TODO: find a better way to print this
    foreach (var module in resolvedProgram.RawModules()) {
      if (module is DefaultModuleDefinition) {
        continue;
      }
      writer.WriteLine($"import opened {module.Name}");
    }
    foreach (var key in queryResults.Keys) {
      var (property, from, to, methodCall) = key;
      var name = $"Change_{methodCall.Name}_{queryResults[key]}_{from}_{to}_{id++}";
      writer.WriteLine(property.AsMethod(name));
    }

    foreach (var (property, value) in valueAtStart) {
      var name = $"AtStart_{value}_{id++}";
      if (property.Parent == property) {
        name += "_parent";
      } else {
        name += "_concrete";
      }
      writer.WriteLine(property.AsMethod(name));
    }
  }

  private static Heuristic Load(string filename, Synthesis.Driver.Options options, ClassDecl classDecl) {
    var heuristic = new Heuristic(options, classDecl);
    var uri = new Uri(Path.GetFullPath(filename));
    var source = new StreamReader(filename).ReadToEnd();
    var errorReporter = new ConsoleErrorReporter(DafnyOptions.Default);
    var resolvedProgram = DafnyTestGeneration.Utils.Parse(errorReporter, source, resolve: true, uri: uri);
    var converter = new VerificationUtils.IdentifierToThisExpressionConverter(DafnyQuery.ReceiverName);
    foreach (var info in resolvedProgram.DefaultModuleDef.DefaultClass.Members) {
      if (info is not Method signature) {
        continue;
      }

      var signatureParts = signature.Name.Split("_");
      var type = new UserDefinedType(Token.NoToken, classDecl.FullDafnyName, new List<Microsoft.Dafny.Type>());
      switch (signatureParts[0]) {
        case "Change":
          var methodCall = signatureParts[1];
          var count = Int16.Parse(signatureParts[2]);
          var from = GetPropertyValue(signatureParts[3]);
          var to = GetPropertyValue(signatureParts[4]);
          var propertyToChange = Property.GetProperty(type, converter.CloneExpr(((signature.Body.Body.First() as AssumeStmt)!).Expr));
          heuristic.queryResults[new Tuple<Property, PropertyValue, PropertyValue, Method>
            (propertyToChange.Parent, from, to, heuristic.Methods.First(method => method.Name == methodCall))] = count;
          break;
        case "AtStart":
          var value = GetPropertyValue(signatureParts[1]);
          var isParent = signatureParts[3] == "parent";
          var propertyAtStart = Property.GetProperty(type, converter.CloneExpr((signature.Body.Body.First() as AssumeStmt)!.Expr));
          if (isParent) {
            propertyAtStart = propertyAtStart.Parent;
          }
          heuristic.valueAtStart[propertyAtStart] = value;
          break;
        default:
          Driver.Log.Error("Unexpected methods in heuristic file.");
          break;
      }
    }
    return heuristic;
  }

  /// <summary>
  /// Return true if the heuristic has been improved and we should reevaluate existing states
  /// </summary>
  /// <returns></returns>
  public async Task<bool> TryImproveHeuristicAsync(bool countIncrementalProgress=false) {
    if (options.DisableHeuristic || options.LoadHeuristics != null) {
      return false;
    }
    if (propertiesToIndex.Keys.Count == 0) {
      return false;
    }

    if (currentPropertyUnderAnalysis == null) {
      currentPropertyUnderAnalysis = propertiesToIndex.Keys.MaxBy(parentProperty => parentProperty.Assignments.Count == 0 ? 1.0 : Math.Pow(propertiesToIndex[parentProperty].Count, 1.0/(parentProperty.Assignments.Count)));
      Driver.Log.Warn($"Selecting property {currentPropertyUnderAnalysis} for heuristic analysis");
    }

    var dafnyQueriesMade = false;
    dafnyQueriesMade = dafnyQueriesMade || await UpdateStartStateWithNewPropertiesAsync(propertiesToIndex[currentPropertyUnderAnalysis!].ToList(), true);
    if (dafnyQueriesMade) {
      return countIncrementalProgress;
    }

    if (!options.SUSHI) {
      dafnyQueriesMade = dafnyQueriesMade ||
                         await UpdateQueriesMapWithNewPropertiesAsync(
                           new List<Property>() { currentPropertyUnderAnalysis! }, true);
      if (dafnyQueriesMade) {
        return countIncrementalProgress;
      }
    }

    // --- Until here
    if (currentPropertyUnderAnalysis != null) {
      propertiesToIndex.Remove(currentPropertyUnderAnalysis);
    }
    currentPropertyUnderAnalysis = null;
    await TryImproveHeuristicAsync();
    return true;
  }
  
  // <summary>
  // This method estimates the number of properties of kind <param name="property"></param> that the method
  // <param name="method"></param> can transform from <param name="from"></param> to <param name="to"></param>.
  // <param name="affectPropertyCap"></param> puts an upper bound on the number of Dafny queries this method will make
  // and also on the number of properties that this method can be found to transform at the time (if we reach
  // AffectedPropertiesCap, this number is deemed to be infinity)
  // Comment from the meeting Dec 18 2023:
  // Suppose we want to transform the property from undefined to true (e.g. 'A' in users['B'].friends).
  // We can bound the minimum number of require method calls in two ways:
  // 1) Either find methods that directly map the property from undefined to true.
  //    For each such method, assume that every method call to this method does map the maximum possible number of properties from undefined to true.
  //    Let's call number on NUllToTrue
  /// 2) Or find methods that map from undefined to false, then from false to true.
  //    Assume that every call to every one of these methods does indeed map the property from undefined to false (NullToFalse) or from false to true (FalseToTrue).
  //    The maximum number of properties flipped from undefined to true is then the minimum of these two number, min(NullToFalse, FalseToTrue).
  //    Then the lower bound on the number of methods you need in total is NUllToTrue + min(NullToFalse, FalseToTrue)
  // The constraint is then NUllToTrue + min(NullToFalse, FalseToTrue) >= the number of properties that have to be flipped.
  // You can encode it as conjunction of : NUllToTrue + NullToFalse >= N
  //                                       NUllToTrue + FalseToTrue >= N
  // </summary>
  /// <returns>True iff any Dafny queries have been made</returns>
  private async Task<bool> UpdateQueriesMapWithOnePropertyAsync(Property property, Method method, PropertyValue from,
    PropertyValue to, int affectPropertyCap) {
    var queryKey = Tuple.Create(property, from, to, method);
    if (queryResults.ContainsKey(queryKey)) {
      return false;
    }

    queryResults[queryKey] = 0;
    var beforeState = new State(type, "");
    var afterState = new State(type, "");
    Expression? assumeBefore = null;
    if (from == PropertyValue.Undefined) {
      assumeBefore = new LiteralExpr(Token.NoToken, true);
    }

    Expression? assertInstead = null;
    if (to == PropertyValue.Undefined) {
      assertInstead = new LiteralExpr(Token.NoToken, true);
    }

    for (int propertyCount = 0; propertyCount < affectPropertyCap; propertyCount++) {
      var wellFormed = WellFormedNess.GetWellFormedNessCondition(property,
        $"{State.FormalNamePrefix}{propertyCount}_{property.Id}_",
        DafnyQuery.ReceiverName);
      var notWellFormed = new UnaryOpExpr(Token.NoToken, UnaryOpExpr.Opcode.Not, wellFormed);
      if (wellFormed is LiteralExpr && (from == PropertyValue.Undefined || to == PropertyValue.Undefined)) {
        return false;
      }

      if (from == PropertyValue.True) {
        beforeState[new IndexedProperty(property, propertyCount)] = true;
      } else if (from == PropertyValue.False) {
        beforeState[new IndexedProperty(property, propertyCount)] = false;
      } else {
        assumeBefore = new BinaryExpr(Token.NoToken, BinaryExpr.Opcode.And, assumeBefore, notWellFormed);
      }

      if (to == PropertyValue.True) {
        afterState[new IndexedProperty(property, propertyCount)] = true;
      } else if (to == PropertyValue.False) {
        afterState[new IndexedProperty(property, propertyCount)] = false;
      } else {
        assertInstead = new BinaryExpr(Token.NoToken, BinaryExpr.Opcode.And, assertInstead, wellFormed);
      }

      var query = new DafnyQuery(
        method.EnclosingClass.FullDafnyName,
        method.EnclosingClass.FullDafnyName,
        new List<Method>() { method },
        beforeState, 
        afterState.Count == 0 ? new State(type, "") : afterState.Negate(), 
        assumeBefore == null ? null : new List<AssumeStmt> {new(RangeToken.NoToken, assumeBefore, null)}, 
        assertInstead == null ? null : new List<AssertStmt> {new(RangeToken.NoToken, assertInstead, null, null, null)});
      var verificationResult = await query.VerifyAsync(VerificationUtils.QueryType.Heuristic, true, QueryTimeLimit);
      if (verificationResult == VerificationResult.Status.Verified) {
        break;
      }
      
      if (verificationResult == VerificationResult.Status.Timeout) {
        queryResults[queryKey] = AffectedPropertiesCap;
        break;
      }

      queryResults[queryKey] += 1;
    }

    if (queryResults[queryKey] == AffectedPropertiesCap) {
      queryResults[queryKey] = 10_000; // essentially infinity
      Driver.Log.Debug(
        $"Assuming that a single call to {method} can flip an arbitrary number of properties of the form {property} from {from} to {to}.");
    } else if (queryResults[queryKey] > 0) {
      Driver.Log.Debug(
        $"A single call to {method} can flip up to {queryResults[queryKey]} properties of the form {property} from {from} to {to}.");
    }
    return true;
  }

  /// <returns>True iff any Dafny queries have been made</returns>
  private async Task<bool> UpdateQueriesMapWithNewPropertiesAsync(List<Property> properties, bool returnAfterFirstDafnyQuery) {
    var dafnyQueryMade = false;
    foreach (var property in properties.ConvertAll(property => property.Parent)) {
      foreach (var method in methods) {
        var propertyFlipCap = property.Assignments.Count == 0 ? 1 : AffectedPropertiesCap;
        dafnyQueryMade = await UpdateQueriesMapWithOnePropertyAsync(property, method, PropertyValue.False, PropertyValue.True,
          propertyFlipCap) || dafnyQueryMade;
        dafnyQueryMade = await UpdateQueriesMapWithOnePropertyAsync(property, method, PropertyValue.True, PropertyValue.False,
          propertyFlipCap) || dafnyQueryMade;
        dafnyQueryMade = await UpdateQueriesMapWithOnePropertyAsync(property, method, PropertyValue.Undefined,
          PropertyValue.True, propertyFlipCap) || dafnyQueryMade;
        dafnyQueryMade =  await UpdateQueriesMapWithOnePropertyAsync(property, method, PropertyValue.Undefined,
          PropertyValue.False, propertyFlipCap) || dafnyQueryMade;
        dafnyQueryMade = await UpdateQueriesMapWithOnePropertyAsync(property, method, PropertyValue.False,
          PropertyValue.Undefined, propertyFlipCap) || dafnyQueryMade;
        dafnyQueryMade = await UpdateQueriesMapWithOnePropertyAsync(property, method, PropertyValue.True,
          PropertyValue.Undefined, propertyFlipCap) || dafnyQueryMade;
        if (dafnyQueryMade && returnAfterFirstDafnyQuery) {
          return dafnyQueryMade;
        }
      }
    }
    return dafnyQueryMade;
  }


  /// <summary>
  /// Here we figure out the value of the given set of properties at the start
  /// </summary>
  /// <returns>True iff any Dafny queries have been made</returns>
  private async Task<bool> UpdateStartStateWithNewPropertiesAsync(List<Property> properties, bool returnAfterFirstPropertyUpdatesWithDafny) {
    var dafnyQueryHasBeenMade = false;
    foreach (var property in properties) {
      if (dafnyQueryHasBeenMade && returnAfterFirstPropertyUpdatesWithDafny) {
        return true;
      }
      var parent = property.Parent;
      if (parent == property) {
        valueAtStart[parent] = PropertyValue.Unknown;
      }
      if (!valueAtStart.ContainsKey(parent)) {
        dafnyQueryHasBeenMade = true;
        var trueState = new State(type, "");
        trueState[new IndexedProperty(parent, 0)] = true;
        
        var formalsDictionary = new Dictionary<int, Dictionary<IndexedProperty, List<Formal>>>();
        trueState.AsAssumption(formalsDictionary, DafnyQuery.ReceiverName);
        var formals  = formalsDictionary.Values.SelectMany(i => i.Values.SelectMany(formal => formal))
          .ToList();

        
        var wellFormed = WellFormedNess.GetWellFormedNessCondition(parent,
          $"{State.FormalNamePrefix}0_{parent.Id}_",
          DafnyQuery.ReceiverName);
        var notWellFormed = new UnaryOpExpr(Token.NoToken, UnaryOpExpr.Opcode.Not, wellFormed);
        
        var wellFormedQuery = new DafnyQuery(constructor.EnclosingClass.FullDafnyName, constructor.EnclosingClass.FullDafnyName, new List<Method> { constructor },
          new State(type, ""), new State(type, ""), null, new List<AssertStmt>{new(RangeToken.NoToken, wellFormed, null, null, null)}, formals);
        var isWellFormed = await wellFormedQuery.VerifyAsync(VerificationUtils.QueryType.Heuristic,true, QueryTimeLimit);
        if (isWellFormed != VerificationResult.Status.Verified) {
          var malformedQuery = new DafnyQuery(constructor.EnclosingClass.FullDafnyName, constructor.EnclosingClass.FullDafnyName,new List<Method> { constructor },
            new State(type, ""), new State(type, ""), null, new List<AssertStmt>{new(RangeToken.NoToken, notWellFormed, null, null, null)}, formals);
          var isMalformed = await malformedQuery.VerifyAsync(VerificationUtils.QueryType.Heuristic,true, QueryTimeLimit);
          if (isMalformed == VerificationResult.Status.Verified) {
            valueAtStart[parent] = PropertyValue.Undefined;
            Driver.Log.Debug($"Property of the form {parent} is always undefined in the beginning.");
            continue;
          }
          
          valueAtStart[parent] = PropertyValue.Unknown;
          Driver.Log.Debug($"Property of the form {parent} is might vary in the beginning.");
        }

        var trueQuery = new DafnyQuery(constructor.EnclosingClass.FullDafnyName, constructor.EnclosingClass.FullDafnyName, new List<Method> { constructor },
          new State(type, ""), trueState);
        var isTrue = await trueQuery.VerifyAsync(VerificationUtils.QueryType.Heuristic,true, QueryTimeLimit);
        if (isTrue == VerificationResult.Status.Verified) {
          if (!valueAtStart.ContainsKey(parent)) {
            valueAtStart[parent] = PropertyValue.True;
            Driver.Log.Debug($"Property of the form {parent} is always true in the beginning.");
            continue;
          }
          valueAtStart[parent] = PropertyValue.NotFalse;
          Driver.Log.Debug($"Property of the form {parent} is never false in the beginning.");
        } else {

          var falseState = new State(type, "");
          falseState[new IndexedProperty(parent, 0)] = false;
          var falseQuery = new DafnyQuery(constructor.EnclosingClass.FullDafnyName, constructor.EnclosingClass.FullDafnyName, new List<Method> { constructor },
            new State(type, ""), falseState);
          var isFalse = await falseQuery.VerifyAsync(VerificationUtils.QueryType.Heuristic,true, QueryTimeLimit);
          if (isFalse == VerificationResult.Status.Verified) {
            if (!valueAtStart.ContainsKey(parent)) {
              valueAtStart[parent] = PropertyValue.False;
              Driver.Log.Debug($"Property of the form {parent} is always false in the beginning.");
              continue;
            }
            valueAtStart[parent] = PropertyValue.NotTrue;
            Driver.Log.Debug($"Property of the form {parent} is never true in the beginning.");
          } else {
            Driver.Log.Debug($"The value of properties of the form {parent} might vary in the beginning.");
            valueAtStart[parent] = PropertyValue.Unknown;
          }
        }
      }

      if (valueAtStart[parent] == PropertyValue.True 
          || valueAtStart[parent] == PropertyValue.False 
          || valueAtStart[parent] == PropertyValue.Undefined
          || valueAtStart.ContainsKey(property)) {
        continue;
      }

      dafnyQueryHasBeenMade = true;
      var concreteTrueState = new State(type, "");
      concreteTrueState[new IndexedProperty(property,0)] = true;
      
      var formalsDictionaryConcrete = new Dictionary<int, Dictionary<IndexedProperty, List<Formal>>>();
      concreteTrueState.AsAssumption(formalsDictionaryConcrete, DafnyQuery.ReceiverName);
      var formalsConcrete = formalsDictionaryConcrete.Values.SelectMany(i => i.Values.SelectMany(formal => formal))
        .ToList();
      var formalAssignments = concreteTrueState
        .AsAssumption(new(), "")
        .OfType<AssumeStmt>()
        .Where(assumption => assumption.Attributes != null && assumption.Attributes.Name == State.AssumptionDescribesFormalAttribute).ToList();

      var wellFormedConcrete = WellFormedNess.GetWellFormedNessCondition(property,
        $"{State.FormalNamePrefix}0_{property.Id}_",
        DafnyQuery.ReceiverName);
      var notWellFormedConcrete = new UnaryOpExpr(Token.NoToken, UnaryOpExpr.Opcode.Not, wellFormedConcrete);


      var wellFormedQueryConcrete = new DafnyQuery(constructor.EnclosingClass.FullDafnyName, constructor.EnclosingClass.FullDafnyName, new List<Method> { constructor },
        new State(type, ""), new State(type, ""), formalAssignments, new List<AssertStmt>{new(RangeToken.NoToken, wellFormedConcrete, null, null, null)}, formalsConcrete);
      var isWellFormedConcrete = await wellFormedQueryConcrete.VerifyAsync(VerificationUtils.QueryType.Heuristic,true, QueryTimeLimit);
      if (isWellFormedConcrete != VerificationResult.Status.Verified) {
        var malformedQueryConcrete = new DafnyQuery(constructor.EnclosingClass.FullDafnyName, constructor.EnclosingClass.FullDafnyName, new List<Method> { constructor },
          new State(type, ""), new State(type, ""), formalAssignments, new List<AssertStmt>{new(RangeToken.NoToken, notWellFormedConcrete, null, null, null)}, formalsConcrete);
        var isMalformedConcrete = await malformedQueryConcrete.VerifyAsync(VerificationUtils.QueryType.Heuristic,true, QueryTimeLimit);
        if (isMalformedConcrete == VerificationResult.Status.Verified) {
          valueAtStart[property] = PropertyValue.Undefined;
          Driver.Log.Debug($"Property {property} is undefined in the beginning.");
          continue;
        }
        valueAtStart[property] = PropertyValue.Unknown;
        Driver.Log.Debug($"Property {property} might vary in the beginning.");
      }

      var concreteTrueQuery = new DafnyQuery(constructor.EnclosingClass.FullDafnyName,
        constructor.EnclosingClass.FullDafnyName,
        new List<Method> { constructor },
        new State(type, ""), concreteTrueState);
      var isConcreteTrue = await concreteTrueQuery.VerifyAsync(VerificationUtils.QueryType.Heuristic,true, QueryTimeLimit);
      if (isConcreteTrue == VerificationResult.Status.Verified) {
        if (!valueAtStart.ContainsKey(property)) {
          valueAtStart[property] = PropertyValue.True;
          Driver.Log.Debug($"Property {property} is true in the beginning.");
        } else {
          valueAtStart[property] = PropertyValue.NotFalse;
          Driver.Log.Debug($"Property {property} is not false in the beginning.");
        }
        continue;
      }

      var isConcreteFalseState = new State(type, "");
      isConcreteFalseState[new IndexedProperty(property, 0)] = false;
      var isConcreteFalseQuery = new DafnyQuery(constructor.EnclosingClass.FullDafnyName,
        constructor.EnclosingClass.FullDafnyName,
        new List<Method> { constructor },
        new State(type, ""), isConcreteFalseState);
      var isConcreteFalse = await isConcreteFalseQuery.VerifyAsync(VerificationUtils.QueryType.Heuristic,true, QueryTimeLimit);
      if (isConcreteFalse == VerificationResult.Status.Verified) {
        if (!valueAtStart.ContainsKey(property)) {
          valueAtStart[property] = PropertyValue.False;
          Driver.Log.Debug($"Property {property} is false in the beginning.");
        } else {
          valueAtStart[property] = PropertyValue.NotTrue;
          Driver.Log.Debug($"Property {property} is not true in the beginning.");
        }
        continue;
      }

      Driver.Log.Debug($"The value of property {property} might vary in the beginning.");
      valueAtStart[property] = PropertyValue.Unknown;

    }

    return dafnyQueryHasBeenMade;
  }

  private PropertyValue ThirdOneOut(PropertyValue one, PropertyValue two) {
    if (one != PropertyValue.True && two != PropertyValue.True) {
      return PropertyValue.True;
    }

    if (one != PropertyValue.False && two != PropertyValue.False) {
      return PropertyValue.False;
    }

    return PropertyValue.Undefined;
  }
}
