using System.Collections.Concurrent;
using Microsoft.Dafny;
using Type = Microsoft.Dafny.Type;

namespace Synthesis;

/// <summary>
/// This class separates the heuristic learning stage from the rest of the algorithm.
/// It performs forward synthesis starting from an arbitrary state,
/// explores possible states by applying methods, improves the heuristic, and saves it to a file.
/// </summary>
public class HeuristicLearner {
  
  public static async Task LearnHeuristicsAsync(Driver.Options options, Program resolvedProgram, string filename) {
    Search.SourceFile = new FileInfo(filename).FullName;
    VerificationUtils.Init();
    // Process each class in the program that has instance methods annotated with {:use}
    var classes = GetClassesWithUseMethods(resolvedProgram).ToList();
    if (classes.Count == 0) {
      Driver.Log.Warn("No classes with instance methods annotated with {:use} found.");
      return;
    }
    Driver.Log.Info($"Starting heuristic learning for classes: {string.Join(", ", classes.Select(c => c.FullDafnyName))}");
    var tasks = classes.Select(classDecl => LearnHeuristicForClassAsync(options, classDecl, resolvedProgram));
    await Task.WhenAll(tasks);
  }

  private static IEnumerable<ClassDecl> GetClassesWithUseMethods(Program resolvedProgram) {
    // Find all class declarations that have instance methods annotated with {:use}
    var classes = new HashSet<ClassDecl>();
    foreach (var module in resolvedProgram.Modules()) {
      foreach (var topLevelDecl in module.TopLevelDecls) {
        if (topLevelDecl is ClassDecl classDecl) {
          var hasUseMethod =  Search.FindMemberDeclsWithAttributes(classDecl, "use").OfType<Method>().Count() != 0;
          if (hasUseMethod) {
            classes.Add(classDecl);
          }
        }
      }
    }
    return classes;
  }

  private static async Task LearnHeuristicForClassAsync(Driver.Options options, ClassDecl classDecl, Program resolvedProgram) {
    var heuristic = Heuristic.Get(options, classDecl);
    var targetType = new UserDefinedType(Token.NoToken, classDecl.FullDafnyName, new List<Type>());
    var initialState = new State(targetType, "");
    await PerformForwardExplorationAsync(options, classDecl, initialState, heuristic, resolvedProgram);
    Heuristic.SaveAll(options, resolvedProgram);
  }

  private static async Task PerformForwardExplorationAsync(Driver.Options options, ClassDecl classDecl, State initialState, Heuristic heuristic, Program resolvedProgram) {
    // Initialize the fringe for BFS
    var fringe = new Queue<(State state, int depth)>();
    var explored = new ConcurrentDictionary<State, bool>();
    fringe.Enqueue((initialState, 0));
    explored[initialState] = true;
    int depthLimit = 1; // Set a reasonable depth limit to prevent combinatorial explosion
    while (fringe.Count > 0) {
      var (state, currentDepth) = fringe.Dequeue();
      if (currentDepth >= depthLimit) {
        continue;
      }
      // For each method, apply it to the state
      foreach (var method in heuristic.Methods) {
        // Construct a DafnyQuery to simulate applying the method
        Driver.Log.Info($"Trying out {method.Name}");
        var query = new DafnyQuery(classDecl.FullDafnyName, classDecl.FullDafnyName,new List<Method> { method }, state, new State(state.Type, ""), null, null, null);
        // Try to infer method arguments and resulting state
        var result = await query.InferMethodArgumentsAndObjectStateAsync(VerificationUtils.QueryType.Regular, Search.DefaultTimeLimit, true);
        if (result.state != null) {
          var newState = result.state;
          // Update heuristic with new properties from newState
          await heuristic.UpdateHeuristicWithNewPropertiesAsync(newState.Keys.Select(k => k.Property).ToList());
          // If newState is not already explored, add it to fringe
          if (explored.TryAdd(newState, true)) {
            fringe.Enqueue((newState, currentDepth + 1));
            Driver.Log.Info($"New state is {newState}");
          }
        }
      }
    }
    // After exploration, try to improve the heuristic
    bool heuristicImproved = true;
    while (heuristicImproved) {
      heuristicImproved = await heuristic.TryImproveHeuristicAsync(countIncrementalProgress:true);
    }
  }
}