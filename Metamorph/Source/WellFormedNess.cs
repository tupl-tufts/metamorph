using Microsoft.Dafny;

namespace Synthesis; 

/// <summary>
/// The purpose of this class is to compute the conditions under which a given property is defined (aka well-formed).
/// For example, for a property `someMap["key"] == "value"`, the appropriate condition is `"key" in someMap`
/// </summary>
public abstract class WellFormedNess: Cloner {
  
  public static Expression GetWellFormedNessCondition(Property property, string prefix, string receiverName) {
    var propertyExpression = property.PrefixWith(prefix, receiverName);
    return new WellFormedNessHelper().GetWellFormedNessCondition(propertyExpression.expression);
  }
  
  private class WellFormedNessHelper : Cloner {
    
    private Expression? condition;

    internal Expression GetWellFormedNessCondition(Expression expression) {
      condition = new LiteralExpr(Token.NoToken, true);
      CloneExpr(expression);
      return condition;
    }
    
    /// <summary> This version of CloneExpr appends well-formed-ness condition to the Condition field </summary>
    public override Expression CloneExpr(Expression expr) {
      if (expr is QuantifierExpr quantifierExpr) {
        var oldCondition = condition;
        condition = new LiteralExpr(Token.NoToken, true);
        CloneExpr(quantifierExpr.Term);
        if (expr is ExistsExpr existsExpr) {
          condition = new ExistsExpr(Token.NoToken, existsExpr.RangeToken, existsExpr.BoundVars, existsExpr.Range,
            condition, existsExpr.Attributes);
        } else if (expr is ForallExpr forallExpr) {
          condition = new ForallExpr(Token.NoToken, forallExpr.RangeToken, forallExpr.BoundVars, forallExpr.Range,
            condition, forallExpr.Attributes);
        }
        condition = new BinaryExpr(Token.NoToken, BinaryExpr.Opcode.And, oldCondition, condition);
        return expr;
      }
      // TODO: Implement well-formed-ness checks for sequences, nullability, division, etc.
      if (expr is SeqSelectExpr mapSelectExpr && mapSelectExpr.Seq.Type is MapType) {
        CloneExpr(mapSelectExpr.Seq);
        CloneExpr(mapSelectExpr.E0);
        condition = new BinaryExpr(Token.NoToken, BinaryExpr.Opcode.And, condition,
          new BinaryExpr(Token.NoToken, BinaryExpr.Opcode.In, mapSelectExpr.E0, mapSelectExpr.Seq));
        return expr;
      }
      if (expr is SeqSelectExpr seqSelectExpr && seqSelectExpr.Seq.Type is SeqType) {
        CloneExpr(seqSelectExpr.Seq);
        CloneExpr(seqSelectExpr.E0);
        var cardinality = new UnaryOpExpr(Token.NoToken, UnaryOpExpr.Opcode.Cardinality, seqSelectExpr.Seq);
        condition = new BinaryExpr(Token.NoToken, BinaryExpr.Opcode.And, condition,
          new BinaryExpr(Token.NoToken, BinaryExpr.Opcode.Gt, cardinality, seqSelectExpr.E0));
        return expr;
      }
      if (expr is MemberSelectExpr memberSelectExpr && memberSelectExpr.Obj.Type is UserDefinedType userDefinedType &&
          userDefinedType.Name.EndsWith("?")) {
        CloneExpr(memberSelectExpr.Obj);
        condition = new BinaryExpr(Token.NoToken, BinaryExpr.Opcode.And, condition,
          new BinaryExpr(Token.NoToken, BinaryExpr.Opcode.Neq, memberSelectExpr.Obj, new LiteralExpr(Token.NoToken)));
      }

      return base.CloneExpr(expr);
    }
  }
}
