include "../Definitions.dfy"

module Problem {

    import opened Definitions

    predicate {:synthesize} Goal(t:BinaryTree) reads t, t.Repr {
        && t.IsValid()
        && t.View == [0, 1, 2, 3]
        && t.Height == 2
    }

}