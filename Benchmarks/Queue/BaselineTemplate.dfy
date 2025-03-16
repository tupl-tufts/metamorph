// TODO: Modify this

include "[File]"

module Baseline {

    import opened Definitions
    import opened Problem

    datatype MethodCall = Rotate | IsEmpty | Enqueue(i: int) | Dequeue | Front

    method {:testInline [BASELINE_INLINING_DEPTH]} SynthesizeRecursive(s:seq<MethodCall>, queue:Queue)
        returns (success:bool) 
        modifies queue, queue.Repr
    {
        if |s| == 0 {
            return true;
        }
        var nextMethodCall := s[0];
        match nextMethodCall {
            case Rotate => 
                if queue.IsValid() && 0 < |queue.Repr|{
                    queue.Rotate();
                } else {
                    return false;
                }
            case IsEmpty => 
                if queue.IsValid() {
                    var _ := queue.IsEmpty();
                } else {
                    return false;
                }
            case Enqueue(i) => 
                if queue.IsValid() {
                    queue.Enqueue(i);
                } else {
                    return false;
                }
            case Dequeue => 
                if queue.IsValid() && 0 < |queue.Repr| {
                    var _ := queue.Dequeue();
                } else {
                    return false;
                }
            case Front =>
                if queue.IsValid() && 0 < |queue.Repr| {
                    var _ := queue.Front();
                } else {
                    return false;
                }
        }
        success := SynthesizeRecursive(s[1..], queue);
        return success;
    }

    method {:testEntry} Synthesize(s:seq<MethodCall>) {
        var queue := new Queue();
        var success := SynthesizeRecursive(s, queue);
        if (!success || !Goal(queue)) {
            return;
        }
        print("Synthesis goal reached");
    }

}