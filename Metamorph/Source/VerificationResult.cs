using Microsoft.Dafny;
using Microsoft.Dafny.LanguageServer.CounterExampleGeneration;
using Formal = Microsoft.Dafny.Formal;
using MapType = Microsoft.Dafny.MapType;
using Token = Microsoft.Dafny.Token;
using Type = Microsoft.Dafny.Type;
using IdentifierExpr = Microsoft.Dafny.IdentifierExpr;

namespace Synthesis;

/// <summary>
/// Represents the result of a Dafny query.
/// If the result is a counterexample, this file converts the corresponding Dafny counterexample model into assumptions.
/// </summary>
public class VerificationResult {

  public enum Status {
    Timeout,
    Verified,
    Counterexample
  }

  public readonly Status ResultStatus;
  private readonly DafnyModel? model;
  private readonly List<Formal> formals;
  private Counterexample? forward = null;
  private Counterexample? backward = null;
  public VerificationResult(Status status, Method method, DafnyModel? model = null) {
    ResultStatus = status;
    this.model = model;
    formals = method.Ins.ToList();
  }

  public State? CounterexampleFor(string formalName, bool isForwardSynthesis=false) {
    if (isForwardSynthesis) {
      forward ??= new Counterexample(this, true);
      return forward.GetFor(formalName);
    }
    backward ??= new Counterexample(this, false);
    return backward.GetFor(formalName);
  }

  public class Counterexample {
    private readonly Dictionary<string, State> counterexample;

    public State? GetFor(string formalName) {
      if (!counterexample.ContainsKey(formalName)) {
        return null;
      }
      var copiedState = new State(counterexample[formalName].Type, formalName);
      foreach (var indexedProperty in counterexample[formalName].Keys) {
        copiedState[indexedProperty] = counterexample[formalName][indexedProperty];
      }
      return copiedState;
    }

    /// <summary>
    /// Populate the Counterexample dictionary, which maps a formal in the original method to a state that constrains that
    /// formal's value
    /// </summary>
    public Counterexample(VerificationResult result, bool isForwardSynthesis) {
      int stateId = isForwardSynthesis ? result.model!.States.Count() - 1 : 0;
      counterexample = new Dictionary<string, State>();
      var vars = result.model!.States?[stateId]?.ExpandedVariableSet(-1);
      if (vars == null) {
        return;
      }

      foreach (var partialValue in vars.Where(partialValue =>
                 partialValue.Type is SetType || partialValue.Type is MapType)) {
        var elements = partialValue.Constraints
          .OfType<ContainmentConstraint>()
          .Where(containment => containment.IsIn && Equals(containment.Set, partialValue))
          .Select(containment => containment.Element).ToList();
        if (elements.Count == 0 && partialValue.Constraints.OfType<LiteralExprConstraint>()
              .Any(constraint => constraint.LiteralExpr is SetDisplayExpr or MapDisplayExpr)) {
          continue;
        }

        if (partialValue.Type is SetType) {
          var _ = new SetDisplayConstraint(partialValue, elements);
        } else {
          var _ = new MapKeysDisplayConstraint(partialValue, elements);
        }
      }

      var allConstraints = vars.SelectMany(var => var.Constraints).ToHashSet()
        .Where(constraint => constraint is not IdentifierExprConstraint)
        .Where(constraint =>
          constraint is not ContainmentConstraint containmentConstraint || containmentConstraint.IsIn)
        .Where(constraint => constraint is not FunctionCallConstraint functionCallConstraint ||
                             ((functionCallConstraint.DefinedValue.Type == Type.Bool || functionCallConstraint.DefinedValue.Type is UserDefinedType) &&
                              functionCallConstraint.ReferencedValues.Count() == 1))
        .ToList();

      foreach (var formal in result.formals) {

        Dictionary<PartialValue, Expression> definitions = new();
        var formalPartialValue = result.model.States?[stateId].KnownVariableNames.Keys.FirstOrDefault(partialValue =>
          result.model.States[stateId].KnownVariableNames[partialValue].Contains(formal.Name));
        if (formalPartialValue == null) {
          continue;
        }

        definitions[formalPartialValue] = new IdentifierExpr(Token.NoToken, formal.Name);
        definitions[formalPartialValue].Type = formalPartialValue.Type;

        var constraints = new List<Constraint>();
        constraints.AddRange(allConstraints);
        constraints = Constraint.ResolveAndOrder(definitions, constraints, false);

        var constraintsAsExpressions = new List<Expression>();
        var constraintsAsStrings = new HashSet<String>();
        foreach (var constraint in constraints) {
          var constraintAsExpression = constraint.AsExpression(definitions);
          if (constraintAsExpression == null || constraint is TypeTestConstraint ||
              constraint is DatatypeConstructorCheckConstraint) {
            continue;
          }

          constraintAsExpression =
            new VerificationUtils.IdentifierToThisExpressionConverter(formal.Name).CloneExpr(constraintAsExpression);
          var constraintAsString = constraintAsExpression.ToString();
          if (constraintsAsStrings.Contains(constraintAsString)) {
            continue;
          }

          constraintsAsStrings.Add(constraintAsString);
          constraintsAsExpressions.Add(constraintAsExpression);
        }

        var state = new State(formalPartialValue.Type, formal.Name);
        for (int i = 0; i < constraintsAsExpressions.Count; i++) {
          // optimization, we convert 'A' !in users  to 'A' in users
          // and A != B to A == B, and update the property map accordingly
          if (constraintsAsExpressions[i] is BinaryExpr binaryExpr && binaryExpr.Op == BinaryExpr.Opcode.NotIn) {
            constraintsAsExpressions[i] =
              new BinaryExpr(binaryExpr.tok, BinaryExpr.Opcode.In, binaryExpr.E0, binaryExpr.E1);
            constraintsAsExpressions[i].Type = Type.Bool;
            state[new IndexedProperty(Property.GetProperty(formalPartialValue.Type, constraintsAsExpressions[i]), i)] =
              false;
            continue;
          }

          if (constraintsAsExpressions[i] is BinaryExpr a && a.Op == BinaryExpr.Opcode.Neq) {
            constraintsAsExpressions[i] = new BinaryExpr(a.tok, BinaryExpr.Opcode.Eq, a.E0, a.E1);
            constraintsAsExpressions[i].Type = Type.Bool;
            state[new IndexedProperty(Property.GetProperty(formalPartialValue.Type, constraintsAsExpressions[i]), i)] =
              false;
            continue;
          }

          state[new IndexedProperty(Property.GetProperty(formalPartialValue.Type, constraintsAsExpressions[i]), i)] =
            true;
        }

        counterexample[formal.Name] = state;
      }
    }
  }
}