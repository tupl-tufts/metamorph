include "../Definitions.dfy"

module Problem {

  import opened Definitions
  import opened Imports

  predicate {:synthesize} Goal(result:VM)
    reads result
  {
    result.exitCode == INVALID_MEMORY_STORE
  }

  method Solution() returns (vm:VM)
    ensures Goal(vm)
  {
    vm := new VM(2);
    vm.Push(1);
    vm.Store(11);
  }
}