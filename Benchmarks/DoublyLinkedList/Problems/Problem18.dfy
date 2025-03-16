include "../Definitions.dfy"

module Problem {

    import opened Definitions

    predicate {:synthesize} Goal(dll:List) reads dll, dll.Repr {
        dll.IsValid() && dll.View() == [0, 1, 2, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18]
    }
}