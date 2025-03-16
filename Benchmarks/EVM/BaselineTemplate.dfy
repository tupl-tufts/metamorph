include "[File]"

module Baseline {

    import opened Definitions
    import opened Problem

    datatype MethodCall = Push(word:nat) | Pop | Add | Sub | Div | Mul |Jump(word:nat) | Jz(word:nat) | Load(word:nat) | Store(word:nat) | NOP

    method {:testInline [BASELINE_INLINING_DEPTH]} SynthesizeRecursive(s:seq<MethodCall>, vm:VM)
        returns (success:bool) 
        modifies vm
    {
        if |s| == 0 {
            return true;
        }
        var nextMethodCall := s[0];
        match nextMethodCall {
            case Push(word) =>
                if (vm.IsValid() && vm.IsRunning() && word <= VM.MAX_WORD && vm.pc > 0) {
                    vm.Push(word);
                } else {
                    return false;
                }
            case Pop =>
                if (vm.IsValid() && vm.IsRunning() && vm.pc > 0) {
                    var _ := vm.Pop();
                } else {
                    return false;
                }
            case Add =>
                if (vm.IsValid() && vm.IsRunning() && vm.pc > 0) {
                    vm.Add();
                } else {
                    return false;
                }
            case Div =>
                if (vm.IsValid() && vm.IsRunning() && vm.pc > 0) {
                    vm.Div();
                } else {
                    return false;
                }
            case Mul =>
                if (vm.IsValid() && vm.IsRunning() && vm.pc > 0) {
                    vm.Mul();
                } else {
                    return false;
                }
            case Sub =>
                if (vm.IsValid() && vm.IsRunning() && vm.pc > 0) {
                    vm.Sub();
                } else {
                    return false;
                }
            case Jump(word:nat) =>
                if (vm.IsValid() && vm.IsRunning() && vm.pc > 0) {
                    vm.Jump(word);
                } else {
                    return false;
                }
            case Jz(word:nat) =>
                if (vm.IsValid() && vm.IsRunning() && vm.pc > 0) {
                    vm.Jz(word);
                } else {
                    return false;
                }
            case Load(word:nat) =>
                if (vm.IsValid() && vm.IsRunning() && vm.pc > 0) {
                    vm.Load(word);
                } else {
                    return false;
                }
            case Store(word:nat) =>
                if (vm.IsValid() && vm.IsRunning() && vm.pc > 0) {
                    vm.Store(word);
                } else {
                    return false;
                }
            case NOP() =>
                if (vm.IsValid() && vm.IsRunning() && vm.pc > 0) {
                    vm.NOP();
                } else {
                    return false;
                }
        }
        success := SynthesizeRecursive(s[1..], vm);
        return success;
    }

    method {:testEntry} Synthesize(s:seq<MethodCall>, pc:nat) {
        var list := new VM(pc);
        var success := SynthesizeRecursive(s, list);
        if (!success || !Goal(list)) {
            return;
        }
        print("Synthesis goal reached");
    }

}