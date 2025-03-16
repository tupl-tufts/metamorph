include "../Definitions.dfy"

module Problem {

  import opened Definitions

  predicate {:synthesize} Goal(d:Database)
    reads d
  {
    && d.D == {1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11}
    && d.CS == {(1, 2), (2, 3), (3, 4), (4, 5), (5, 6), (6, 7), (7, 8), (8, 9), (9, 10), (10, 11), (11, 1)}
    && d.C == {1}
  }
}