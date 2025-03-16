include "../Definitions.dfy"

module Problem {

    import opened Definitions

    predicate {:synthesize} Goal(arr:Array) reads arr { 
      && |arr.data| == 10
      && arr.data[0] == 0
      && arr.data[1] == 1
      && arr.data[2] == 2
      && arr.data[3] == 3
      && arr.data[4] == 4
      && arr.data[5] == 5
      && arr.data[6] == 6
      && arr.data[7] == 7
      && arr.data[8] == 8
      && arr.data[9] == 9
    }
}