include "../Definitions.dfy"

module Problem {

  import opened Definitions
  import opened Imports

  predicate {:synthesize} Goal(result:VM)
    reads result
  {
    result.exitCode == STACK_UNDERFLOW
  }

  method Solution() returns (vm:VM)
    ensures Goal(vm)
  {
    vm := new VM(1);
    var _ := vm.Pop();
  }
}