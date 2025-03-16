include "../Definitions.dfy"

module Problem {

    import opened Definitions

    predicate {:synthesize} Goal(arr:Array) reads arr { 
      && !arr.frozen
      && |arr.data| == 1
      && arr.data[0] == 0
    }

    method solution() returns (arr:Array)
      ensures Goal(arr) 
    {
      arr := new Array(1);
      arr.Unfreeze();
      arr.Put(0, 0);
    }
}