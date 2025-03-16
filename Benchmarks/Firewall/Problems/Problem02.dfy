include "../Definitions.dfy"

module Problem {

  import opened Definitions

  predicate {:synthesize} Goal(d:Database)
    reads d
  {
    && d.D == {1, 2, 3}
    && d.CS == {(1, 2), (2, 3), (3, 1)}
    && d.C == {1}
  }
}