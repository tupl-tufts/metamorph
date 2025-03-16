namespace Synthesis;

public record IndexedProperty(Property Property, int Index) : IComparable<IndexedProperty> {
  public int CompareTo(IndexedProperty? other) {
    if (ReferenceEquals(this, other)) {
      return 0;
    }

    if (ReferenceEquals(null, other)) {
      return 1;
    }

    var propertyComparison = Property.CompareTo(other.Property);
    if (propertyComparison != 0) {
      return propertyComparison;
    }

    return Index.CompareTo(other.Index);
  }

  public override string ToString() {
    if (Property.Parent != Property) {
      return Property.OriginalExpression.ToString();
    }
    var normalizedProperty =  Property.PrefixWith(
      "default" + Index + "_" + Property.Id + "_", 
      DafnyQuery.ReceiverName);
    return normalizedProperty.expression.ToString();
  }
}
