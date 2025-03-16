include "../Definitions.dfy"

module Problem {

    import opened Definitions

    predicate {:synthesize} Goal(arr:Array) reads arr { 
      && !arr.frozen
      && |arr.data| == 8
      && arr.data[0] == 0
      && arr.data[1] == 0
      && arr.data[2] == 0
      && arr.data[3] == 0
      && arr.data[4] == 0
      && arr.data[5] == 0
      && arr.data[6] == 0
      && arr.data[7] == 0
    }

    method solution() returns (arr:Array)
      ensures Goal(arr) 
    {
      arr := new Array(8);
      arr.Unfreeze();
      arr.Fill(0);
      arr.Unfreeze();
    }
}