include "../Definitions.dfy"

module Problem {

  import opened Definitions
  import opened Imports

  predicate {:synthesize} Goal(result:VM)
    reads result
  {
    result.exitCode == INTEGER_OVERFLOW
  }

  method Solution() returns (vm:VM)
    ensures Goal(vm)
  {
    vm := new VM(3);
    vm.Push(65535);
    vm.Push(3);
    vm.Mul();
  }

}