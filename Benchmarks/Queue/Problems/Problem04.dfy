include "../Definitions.dfy"

module Problem {

    import opened Definitions

    predicate {:synthesize} Goal(queue:Queue) reads queue, queue.Repr {
        queue.IsValid() && queue.View() == [0, 1, 2, 4]
    }
}