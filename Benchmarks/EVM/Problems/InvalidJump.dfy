include "../Definitions.dfy"

module Problem {

  import opened Definitions
  import opened Imports

  predicate {:synthesize} Goal(result:VM)
    reads result
  {
    result.exitCode == INVALID_JUMP
  }

  method Solution() returns (vm:VM)
    ensures Goal(vm)
  {
    vm := new VM(1);
    vm.Jump(1);
  }

}