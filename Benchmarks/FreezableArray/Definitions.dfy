module Definitions {

  class Array {

    /*ghost*/ var data:seq<int>
    /*ghost*/ var frozen:bool 
    
    constructor (len:nat)
        ensures frozen
        ensures |data| == len
        ensures forall i:nat :: i < |data| ==> data[i] == -1
    {
      data := seq(len, i => -1);
      frozen := true;
    }

    method {:use} Put(i:nat, val:int)
      requires i < |data| 
      requires !frozen
      modifies this
      ensures data == 
        old(data[i := val])
      ensures |data| == |old(data)|
      ensures data[i] == val
      ensures forall j:nat :: j < |data| && i != j ==> data[j] == old(data)[j]
      ensures !frozen
    {
      data := data[i := val];
    }

    method {:use} Freeze()
      modifies this
      ensures frozen
      ensures data == old(data) 
    {
      frozen := true;
    }

    method {:use} Unfreeze()
      modifies this
      ensures !frozen
      ensures data == old(data) 
    {
      frozen := false;
    }
    
  }
}
