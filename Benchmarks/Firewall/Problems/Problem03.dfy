include "../Definitions.dfy"

module Problem {

  import opened Definitions

  predicate {:synthesize} Goal(d:Database)
    reads d
  {
    && d.D == {1, 2, 3, 4}
    && d.CS == {(1, 2), (2, 3), (3, 4), (4, 1)}
    && d.C == {1}
  }
}