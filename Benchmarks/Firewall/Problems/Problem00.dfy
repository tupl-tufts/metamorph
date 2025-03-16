include "../Definitions.dfy"

module Problem {

  import opened Definitions

  predicate {:synthesize} Goal(d:Database)
    reads d
  {
    && d.D == {1}
    && d.CS == {}
    && d.C == {1}
  }
}