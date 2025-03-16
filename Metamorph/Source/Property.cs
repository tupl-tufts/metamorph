#nullable disable
using Microsoft.Dafny;
using Type = Microsoft.Dafny.Type;

namespace Synthesis; 

// Represents a property about the object's state
public class Property: IComparable<Property> {
  
  private static readonly Dictionary<string, Dictionary<string, List<Property>>> TypeToKeyToProperties = new();
  private static int nextId;
  
  public readonly Expression OriginalExpression;
  // boolean expression that describes the object. It must not contain any primitive constants
  private readonly Expression expression;

  private readonly string key; // string representation of the expression above
  public readonly int Id;

  // all primitive constants or bound variables found in the expression.
  // If value is null, then the property should quantify over all possible values of that formal variable
  public readonly List<(Formal formal, Expression value)> Assignments;
  public Property Parent;
  public readonly string type;
  public HashSet<Property> Children;

  public static void Clear() {
    TypeToKeyToProperties.Clear();
    nextId = 0;
  }
  
  /// <summary>
  /// Example:
  /// Suppose originalExpression is data[5] == 3.
  /// This will return a property associated with this expression and it will set its parent to a property
  /// data[tmp0] == tmp1, where tmp0 and tmp1 are unbound.
  /// </summary>
  /// <returns></returns>
  public static Property GetProperty(Type type, Expression originalExpression) {
    var normalizer = new ExpressionNormalizer();
    var expression = normalizer.CloneExpr(originalExpression);
    var assignments = normalizer.assignemnts.Values.ToList();
    var result = new Property(type, expression, assignments, originalExpression);
    if (!TypeToKeyToProperties.ContainsKey(type.ToString())) {
      TypeToKeyToProperties[type.ToString()] = new();
    }
    if (!TypeToKeyToProperties[type.ToString()].ContainsKey(result.key)) {
      TypeToKeyToProperties[type.ToString()][result.key] = new List<Property>();
    }

    var id = TypeToKeyToProperties[type.ToString()][result.key].IndexOf(result);
    if (id != -1) {
      return TypeToKeyToProperties[type.ToString()][result.key][id];
    }

    TypeToKeyToProperties[type.ToString()][result.key].Add(result);
    result.Parent = 
      assignments.Any(assignment => assignment.value != null) ? 
        GetProperty(type, expression) : 
        result;
    result.Parent.Children.Add(result);
    return result;
  }

  public int CompareTo(Property other) {
    if (other.type != type) {
      return string.Compare(type, other.type);
    }
    if (other.key != key) {
      return -string.Compare(key, other.key, StringComparison.Ordinal);
    }
    if (other.Assignments.Count != Assignments.Count) {
      return Assignments.Count.CompareTo(other.Assignments.Count);
    }
    for (int i = 0; i < Assignments.Count; i++) {
      if (Assignments[i].formal.Name != other.Assignments[i].formal.Name) {
        return string.Compare(Assignments[i].formal.Name,
          other.Assignments[i].formal.Name, StringComparison.Ordinal);
      }
      if (Assignments[i].formal.Type.ToString() !=
          other.Assignments[i].formal.Type.ToString()) {
        return string.Compare(Assignments[i].formal.Type.ToString(),
          other.Assignments[i].formal.Type.ToString(), StringComparison.Ordinal);
      }
      if (Assignments[i].value == null && other.Assignments[i].value == null) {
        continue;
      }
      if (Assignments[i].value == null) {
        return -1;
      }
      if (other.Assignments[i].value == null) {
        return 1;
      }
      if (Assignments[i].value?.ToString() !=
          other.Assignments[i].value?.ToString()) {
        return string.Compare(Assignments[i].value?.ToString(),
          other.Assignments[i].value?.ToString(), StringComparison.Ordinal);
      }
    }
    return 0;
  }

  public override bool Equals(object obj) {
    if (obj is not Property other) {
      return false;
    }
    return CompareTo(other) == 0;
  }

  public override int GetHashCode() {
    return key.GetHashCode();
  }

  private Property(Type type, Expression expression, List<(Formal formal, Expression value)> assignments, Expression originalExpression) {
    this.type = type.ToString();
    this.expression = expression;
    Id = nextId++;
    OriginalExpression = originalExpression;
    Assignments = assignments;
    Children = new HashSet<Property>();
    key = Printer.ExprToString(DafnyOptions.Default, expression);
  }

  public (Expression expression, List<(Formal formal, Expression value)> assignments) PrefixWith(string prefix, string receiverName) {
    var normalizer = new ExpressionNormalizer(prefix, Assignments, receiverName);
    var newExpression = normalizer.CloneExpr(expression);
    return new (newExpression, normalizer.assignemnts.Values.ToList());
  }

  public override string ToString() {
    return OriginalExpression.ToString();
  }
  
  public string AsMethod(string methodName) {
    var converter = new VerificationUtils.ThisToIdentifierExpressionConverter(DafnyQuery.ReceiverName);
    if (Children.Any(child => child != this)) {
      return Children.Last().AsMethod(methodName);
    }
    var receiver = new Formal(Token.NoToken, DafnyQuery.ReceiverName, 
      new UserDefinedType(Token.NoToken, type.Split(".").Last(), null), true, false, null);
    var expression = converter.CloneExpr(OriginalExpression);
    var formals = new List<Formal>() {receiver};
    var assumption = new List<Statement>() {new AssumeStmt(RangeToken.NoToken, expression, null)};
    var method = new Method(
      new RangeToken(Token.NoToken, Token.NoToken),
      new Name(new RangeToken(Token.NoToken, Token.NoToken), methodName),
      true,
      false,
      new List<TypeParameter>(),
      formals,
      new(),
      new(),
      new(),
      new Specification<FrameExpression>(), 
      new(),
      new Specification<Expression>(new List<Expression>(), null),
      new BlockStmt(new RangeToken(Token.NoToken, Token.NoToken), assumption),
      null,
      null
    );
    using (var wr = new StringWriter()) {
      var pr = new Printer(wr, DafnyOptions.Default);
      pr.PrintMethod(method, 0, false);
      return Printer.ToStringWithoutNewline(wr);
    }
  }

  private class ExpressionNormalizer : Cloner {

    public readonly Dictionary<string, (Formal formal, Expression value)> assignemnts = new();
    private readonly Dictionary<string, Expression> previous= new();
    private readonly string prefix;
    private readonly Expression receiver;

    public ExpressionNormalizer(string prefix, List<(Formal formal, Expression value)> previous, string receiverName) {
      this.prefix = prefix;
      foreach (var assignment in previous) {
        this.previous[assignment.formal.Name] = assignment.value;
      }
      receiver = receiverName != null ? new IdentifierExpr(Token.NoToken, receiverName) : null;
    }

    public ExpressionNormalizer() {
      prefix = "default";
      receiver = null;
    }

    public override Expression CloneExpr(Expression expr) {
      if (expr == null) {
        return null;
      }

      if (expr is ThisExpr or ImplicitThisExpr && receiver != null) {
        if (receiver.Type == null && expr.Type != null) {
          receiver.Type = expr.Type;
        }
        return receiver;
      }

      if (expr is ImplicitThisExpr) {
        var newThisExpr = new ThisExpr(Token.NoToken);
        if (expr.Type != null) {
          newThisExpr.Type = expr.Type;
        }
        return newThisExpr;
      }

      if (expr is NameSegment && expr.Resolved != null) {
        var tempResult = CloneExpr(expr.Resolved);
        if (expr.Resolved.Type != null) {
          tempResult.Type = expr.Resolved.Type;
        } else if (expr.Type != null) {
          tempResult.Type = expr.Type;
        }
        return tempResult;
      }

      if (expr.Type is not IntType &&
          expr.Type is not UserDefinedType { Name: "nat" } &&
          expr.Type is not UserDefinedType { Name: "string" } &&
          expr.Type is not SeqType { Arg: CharType } &&
          expr.Type is not CharType &&
          expr.Type is not BoolType) {
        var tempResult = base.CloneExpr(expr);
        if (expr.Type != null) {
          tempResult.Type = expr.Type;
        }
        return tempResult;
      }

      switch (expr) {
        case IdentifierExpr identifierExpr: {
          if (!assignemnts.ContainsKey(identifierExpr.Name)) {
            if (!previous.ContainsKey(identifierExpr.Name)) {
              assignemnts[identifierExpr.Name] =
                new(
                  new Formal(Token.NoToken, prefix + assignemnts.Count,
                    expr.Type, false, false, null), null);
            } else {
              assignemnts[identifierExpr.Name] =
                new(
                  new Formal(Token.NoToken, prefix + assignemnts.Count,
                    expr.Type, false, false, null), previous[identifierExpr.Name]);
            }
          }

          var newIdentifier = new IdentifierExpr(Token.NoToken, assignemnts[identifierExpr.Name].formal);
          newIdentifier.Type = expr.Type;
          return newIdentifier;
        }
        case LiteralExpr literalExpr: {
          var name = prefix + assignemnts.Count;
          assignemnts[name] = new(new Formal(Token.NoToken, name, expr.Type, false, false, null), literalExpr);
          var newIdentifier = new IdentifierExpr(Token.NoToken, assignemnts[name].formal);
          newIdentifier.Type = expr.Type;
          return newIdentifier;
        }
      }
      
      var result =  base.CloneExpr(expr);
      result.Type = expr.Type;
      return result;
    }
  }

}
