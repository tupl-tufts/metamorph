module Definitions {

  class Array {

    /*ghost*/ var data:seq<int>
    /*ghost*/ var frozen:bool 
    
    constructor (len:nat)
        ensures frozen
        ensures |data| == len
        ensures Uninitialized() 
    {
      data := seq(len, i => -1);
      frozen := true;
    }
    
    predicate Uninitialized() 
        reads this
    {
         forall i:nat :: i < |data| ==> data[i] == -1
    }

    method {:use} Put(i:nat, val:nat)
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
    
    method {:use} Fill(val:nat)
      requires !frozen
      requires Uninitialized()
      modifies this
      ensures |data| == |old(data)|
      ensures forall i:nat :: i < |data| ==> data[i] == val
      ensures frozen
    {
      data := seq(|data|, i => val);
      frozen := true;
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
