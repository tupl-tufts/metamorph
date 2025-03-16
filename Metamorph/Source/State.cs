using Microsoft.Dafny;
using Type = Microsoft.Dafny.Type;

namespace Synthesis;

public class State : SortedDictionary<IndexedProperty, bool> {

  public readonly string Type;
  public readonly string ReceiverName;
  
  // If Negate is false, the state is described by the conjunction of its properties
  // Otherwise, the state is described by a disjunction of the negation of the properties
  private readonly bool Negated = false; 
  
  public const string FormalNamePrefix = "formal_";
  public const string AssumptionDescribesArgumentAttribute = "attribute";
  public const string AssumptionDescribesFormalAttribute = "formal";

  private static readonly Dictionary<int, State> idToState = new();
  private static int nextUniqueId;
  private readonly int uniqueId = nextUniqueId++;

  public new static void Clear() {
    idToState.Clear();
    nextUniqueId = 0;
  }

  private State(bool negated, string type, string receiverName) {
    this.Type = type;
    this.ReceiverName = receiverName;
    this.Negated = negated;
    idToState[uniqueId] = this; // TODO: This is dangerous because we allow modifying state objects
  }
  
  public State(string type, string receiverName):this(false, type, receiverName) { }

  public State Negate() {
    var negation = new State(!Negated, Type, ReceiverName);
    foreach (var key in Keys) {
      negation[key] = this[key];
    }
    return negation;
  }

  public static State GetById(int id) {
    return idToState[id];
  }

  public State(Type type, string receiverName):this(type.ToString(), receiverName) { }
  public override bool Equals(object? obj) {
    if (obj is not State other ||
        other.Count != Count) {
      return false;
    }

    for (int i = 0; i < Count; i++) {
      if (this.ElementAt(i).Key != other.ElementAt(i).Key ||
          this.ElementAt(i).Value != other.ElementAt(i).Value) {
        return false;
      }
    }

    return true;
  }

  public override int GetHashCode() {
    int hasCode = 0;
    foreach (var b in Values) {
      hasCode <<= hasCode;
      if (b) {
        hasCode += 1;
      }
    }
    return hasCode;
  }

  /// <summary>
  /// Emit a list of statements that constrains the object to this state.
  /// The last statement is an assumption. Store unconstrained formal variables
  /// referenced in the assumption <param name="formals"></param> list.
  /// </summary>
  public List<Statement> AsAssumption(Dictionary<int, Dictionary<IndexedProperty, List<Formal>>> formals, string receiverName) {
    if (Negated) {
      return AsReversedAssumption(formals, receiverName);
    }
    if (Count == 0) {
      return new List<Statement>();
    }
    var expressions = AsExpressions(out var statements, formals, receiverName);
    Attributes? assumptionDescribesArgument = null;
    if (!receiverName.StartsWith(DafnyQuery.ReceiverName)) {
      assumptionDescribesArgument = new Attributes(AssumptionDescribesArgumentAttribute, new() {new LiteralExpr(Token.NoToken, uniqueId)}, null);

    }

    foreach (var expression in expressions) {
      statements.Add(new AssumeStmt(RangeToken.NoToken, expression, assumptionDescribesArgument));
    }
    return statements;
  }
  
  private List<Statement> AsReversedAssumption(Dictionary<int, Dictionary<IndexedProperty, List<Formal>>> formals, string receiverName) {
    if (Count == 0) {
      return new List<Statement> {new AssumeStmt(RangeToken.NoToken, new LiteralExpr(Token.NoToken, false), null)};
    }
    var expression = MakeConjunction(AsExpressions(out var statements, formals, receiverName), new Cloner());
    statements.Add(new AssumeStmt(
      RangeToken.NoToken, 
      new UnaryOpExpr(Token.NoToken, UnaryOpExpr.Opcode.Not, expression),
      null));
    return statements;
  }
  
  /// <summary>
  /// Emit a list of statements that constrains the state opposite to this.
  /// The last statement is an assertion. Store unconstrained formal variables
  /// referenced in the assumption <param name="formals"></param> list.
  /// </summary>
  private List<Statement> AsReversedAssertion(Dictionary<int, Dictionary<IndexedProperty, List<Formal>>> formals, string receiverName) {
    if (Count == 0) {
      return new List<Statement> { new AssertStmt(RangeToken.NoToken, new LiteralExpr(Token.NoToken, false), null, null, new Attributes(VerificationUtils.KeepAssertionAttribute, new(), null))};
    } 
    var expression = MakeConjunction(AsExpressions(out var statements, formals, receiverName), new Cloner());
    statements.Add(new AssertStmt(
      RangeToken.NoToken, 
      new UnaryOpExpr(Token.NoToken, UnaryOpExpr.Opcode.Not, expression), 
      null, null, new Attributes(VerificationUtils.KeepAssertionAttribute, new(), null)));
    return statements;
  }
  
  /// <summary>
  /// Emit a list of statements that constrains the state to this.
  /// The last statement is an assertion. Store unconstrained formal variables
  /// referenced in the assumption <param name="formals"></param> list.
  /// </summary>
  public List<Statement> AsAssertion(Dictionary<int, Dictionary<IndexedProperty, List<Formal>>> formals, string receiverName) {
    if (Negated) {
      return AsReversedAssertion(formals, receiverName);
    }
    if (Count == 0) {
      return new List<Statement> { new AssertStmt(RangeToken.NoToken, new LiteralExpr(Token.NoToken, true), null, null, new Attributes(VerificationUtils.KeepAssertionAttribute, new(), null))};
    } 
    var expressions = AsExpressions(out var statements, formals, receiverName);
    foreach (var expression in expressions) {
      statements.Add(new AssertStmt(
        RangeToken.NoToken, 
        expression, 
        null, null, new Attributes(VerificationUtils.KeepAssertionAttribute, new(), null)));
    } 
    return statements;
  }

  /// <summary>
  /// Emit an expression that, if true, constrains the object to this state.
  /// Store variable declarations/updates and unconstrained formal variables
  /// referenced in the expression in <param name="statements"></param> and
  /// <param name="formals"></param> lists.
  /// </summary>
  private List<Expression> AsExpressions(out List<Statement> statements, Dictionary<int, Dictionary<IndexedProperty, List<Formal>>> formals, string receiverName) {
    statements = new List<Statement>();
    var expressions = new List<Expression>();
    // TODO: Bring back well-formedness sorting
    //var propertiesOrder = Property.AllConcreteProperties(Keys.Select(indexedProperty => indexedProperty.Property).ToList()).ToList();
    //var indexedProperties = Keys.OrderBy(indexedProperty => propertiesOrder.IndexOf(indexedProperty.Property));
    var indexedProperties = Keys.OrderBy(indexedProperty => indexedProperty.Index);
    foreach (IndexedProperty property in indexedProperties) {
      var value = this[property];
      // the first suffix is the Index of the IndexedProperty (because the same property can appear multiple times)
      // the second index of the property (so data[?] = ? will have same property id)
      var normalizedProperty = property.Property.PrefixWith($"{FormalNamePrefix}{property.Index}_{property.Property.Id}_", receiverName);
      var expression = normalizedProperty.expression;
      if (!value) {
        expression = new UnaryOpExpr(Token.NoToken, UnaryOpExpr.Opcode.Not, expression);
      }
      expressions.Add(expression);
      Dictionary<IndexedProperty, Expression> formalsNotEqual = new();
      if (!formals.ContainsKey(property.Property.Id)) {
        formals[property.Property.Id] = new();
      } 
      if (!formals[property.Property.Id].ContainsKey(property)) {
        formals[property.Property.Id][property] = new();
      }
      foreach (var index in formals[property.Property.Id].Keys) {
        if (index == property) {
          continue;
        }
        formalsNotEqual[index] = new LiteralExpr(Token.NoToken, false);
      }

      bool emitFormalsNotEqual = false;
      foreach (var assignment in normalizedProperty.assignments) {
        if (formals[property.Property.Id][property].Any(formal => formal.Name == assignment.formal.Name)) {
          continue;
        }
        formals[property.Property.Id][property].Add(assignment.formal);
        if (assignment.value != null) {
          statements.Add(new AssumeStmt(RangeToken.NoToken,
            new BinaryExpr(Token.NoToken, BinaryExpr.Opcode.Eq,
              new IdentifierExpr(Token.NoToken, assignment.formal.Name),
              assignment.value), new Attributes(AssumptionDescribesFormalAttribute, new List<Expression>(), null)));
        } else {
          foreach (var index in formals[property.Property.Id].Keys) {
            if (index == property) {
              continue;
            }
            emitFormalsNotEqual = true;
            formalsNotEqual[index] = new BinaryExpr(Token.NoToken, BinaryExpr.Opcode.Or, formalsNotEqual[index], 
              new UnaryOpExpr(Token.NoToken, UnaryOpExpr.Opcode.Not,
                new BinaryExpr(Token.NoToken, BinaryExpr.Opcode.Eq, 
                  new IdentifierExpr(Token.NoToken, assignment.formal.Name),
                  new IdentifierExpr(Token.NoToken, $"{FormalNamePrefix}{index.Index}_{property.Property.Id}_" + assignment.formal.Name.Split("_").Last()))));
          }
        }
      }

      if (emitFormalsNotEqual) {
        foreach (var formalsNotEqualExpression in formalsNotEqual.Values) {
          statements.Add(new AssumeStmt(RangeToken.NoToken, formalsNotEqualExpression, null));
        }
      }
    }

    return expressions;
  }
  
  /// <summary>
  /// Return an expression that is a conjunction of the provided boolean
  /// subexpressions. Instead of directly reusing the existing AST nodes for
  /// the subexpressions, copy them with the cloner
  /// </summary>
  private static Expression MakeConjunction(List<Expression> subexpressions, Cloner cloner) {
    if (subexpressions.Count >= 2) {
      return new BinaryExpr(Token.NoToken, BinaryExpr.Opcode.And,
        MakeConjunction(subexpressions.Take(subexpressions.Count / 2).ToList(), cloner),
        MakeConjunction(subexpressions.TakeLast(subexpressions.Count - subexpressions.Count / 2).ToList(), cloner));
    }
    return cloner.CloneExpr(subexpressions.First());
  }

  public override string ToString() {
    if (Count == 0) {
      return "any state";
    }
    return string.Join(", ", Keys.Select(key => (this[key] ? key.ToString(): "¬(" + key + ")") + ""));
  }
}
