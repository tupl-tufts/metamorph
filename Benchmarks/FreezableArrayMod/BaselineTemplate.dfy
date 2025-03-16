include "[File]"

module Baseline {

    import opened Definitions
    import opened Problem

    datatype MethodCall = Freeze | Unfreeze | Put(i:nat, val:nat) | Fill(val:nat)

    method {:testInline [BASELINE_INLINING_DEPTH]} SynthesizeRecursive(s:seq<MethodCall>, arr:Array)
        returns (success:bool) 
        modifies arr
    {
        if |s| == 0 {
            return true;
        }
        var nextMethodCall := s[0];
        match nextMethodCall {
            case Freeze => 
                arr.Freeze();
            case Unfreeze => 
                arr.Unfreeze();
            case Put(i:nat, val:nat) => 
                if !arr.frozen && i < |arr.data| {
                    arr.Put(i, val);
                } else {
                    return false;
                }
            case Fill(val:nat) =>
               if !arr.frozen && arr.Uninitialized() {
                    arr.Fill(val);
               } else {
                    return false;
               }
        }
        success := SynthesizeRecursive(s[1..], arr);
        return success;
    }

    method {:testEntry} Synthesize(s:seq<MethodCall>, len:nat) {
        var arr := new Array(len);
        var success := SynthesizeRecursive(s, arr);
        if (!success || !Goal(arr)) {
            return;
        }
        print("Synthesis goal reached");
    }

}