include "../Definitions.dfy"

module Problem {

    import opened Definitions

    predicate {:synthesize} Goal(t:BinaryTree) reads t, t.Repr {
        && t.IsValid()
        && t.View == [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18]
        && t.Height == 4
    }
}