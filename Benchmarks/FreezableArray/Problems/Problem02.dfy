include "../Definitions.dfy"

module Problem {

    import opened Definitions

    predicate {:synthesize} Goal(arr:Array) reads arr { 
      && |arr.data| == 2
      && arr.data[0] == 0
      && arr.data[1] == 1
    }
}