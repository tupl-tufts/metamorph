include "../Definitions.dfy"

module Problem {

    import opened Definitions

    predicate {:synthesize} Goal(arr:Array) reads arr { 
      && !arr.frozen
      && |arr.data| == 11
      && arr.data[0] == 0
      && arr.data[1] == 0
      && arr.data[2] == 0
      && arr.data[3] == 0
      && arr.data[4] == 0
      && arr.data[5] == 0
      && arr.data[6] == 0
      && arr.data[7] == 0
      && arr.data[8] == 0
      && arr.data[9] == 0
      && arr.data[10] == 0
    }

    method solution() returns (arr:Array)
      ensures Goal(arr) 
    {
      arr := new Array(11);
      arr.Unfreeze();
      arr.Fill(0);
      arr.Unfreeze();
    }
}