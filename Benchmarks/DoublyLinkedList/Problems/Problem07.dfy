include "../Definitions.dfy"

module Problem {

    import opened Definitions

    predicate {:synthesize} Goal(dll:List) reads dll, dll.Repr {
        dll.IsValid() && dll.View() == [0, 1, 2, 4, 5, 6, 7]
    }
}