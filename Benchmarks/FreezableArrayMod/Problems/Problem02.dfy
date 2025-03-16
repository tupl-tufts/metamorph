include "../Definitions.dfy"

module Problem {

    import opened Definitions

    predicate {:synthesize} Goal(arr:Array) reads arr { 
      && !arr.frozen
      && |arr.data| == 2
      && arr.data[0] == 0
      && arr.data[1] == 0
    }

    method solution() returns (arr:Array)
      ensures Goal(arr) 
    {
      arr := new Array(2);
      arr.Unfreeze();
      arr.Put(0, 0);
      arr.Put(1, 0);
    }
}