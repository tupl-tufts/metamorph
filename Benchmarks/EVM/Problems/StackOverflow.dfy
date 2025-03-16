include "../Definitions.dfy"

module Problem {

  import opened Definitions
  import opened Imports

  predicate {:synthesize} Goal(result:VM)
    reads result
  {
    result.exitCode == STACK_OVERFLOW
  }

  method Solution() returns (vm:VM)
    ensures Goal(vm)
  {
    vm := new VM(11);
    vm.Push(2);
    vm.Push(2);
    vm.Push(2);
    vm.Push(2);
    vm.Push(2);
    vm.Push(2);
    vm.Push(2);
    vm.Push(2);
    vm.Push(2);
    vm.Push(2);
    vm.Push(2);
  }
}