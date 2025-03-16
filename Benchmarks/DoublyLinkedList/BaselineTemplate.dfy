include "[File]"

module Baseline {

    import opened Definitions
    import opened Problem

    datatype MethodCall = PushFront(i:int) | PushBack(i:int) | PopFront(i:int) | PopBack(i:int)

    method {:testInline [BASELINE_INLINING_DEPTH]} SynthesizeRecursive(s:seq<MethodCall>, list:List)
    returns (success:bool) 
    modifies list, list.Repr
    {
        if |s| == 0 {
            return true;
        }
        var nextMethodCall := s[0];
        match nextMethodCall {
            case PushFront(i) => 
                if list.IsValid() {
                    list.PushFront(i);
                } else {
                    return false;
                }
            case PushBack(i) => 
                if list.IsValid() {
                    list.PushBack(i);
                } else {
                    return false;
                }
            case PopFront(i) => 
                if list.IsValid() && |list.Repr| > 0 {
                    var _ := list.PopFront();
                } else {
                    return false;
                }
            case PopBack(i) => 
                if list.IsValid() && |list.Repr| > 0 {
                    var _ := list.PopBack();
                } else {
                    return false;
                }
        }
        success := SynthesizeRecursive(s[1..], list);
        return success;
    }

    method {:testEntry} Synthesize(s:seq<MethodCall>) {
        var list := new List();
        var success := SynthesizeRecursive(s, list);
        if (!success || !Goal(list)) {
            return;
        }
        print("Synthesis goal reached");
    }

}