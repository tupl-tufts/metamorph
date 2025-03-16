module Definitions {

  class Database {

    var D:set<int> // Devices
    var CS:set<(int, int)> // CanSend
    var C: set<int> // central devices, modeled explicitly

    predicate IsValid() 
      reads this
    {
      && (forall sr :: sr in CS ==> (sr.0 in D && sr.1 in D))
      && (forall d :: d in C ==> d in D)
    }

    method {:use} AddDevice(d:int)
      modifies this
      requires IsValid()
      requires d !in D
      ensures IsValid()
      ensures old(CS) == CS
      ensures d in D
      ensures C == old(C)
      ensures D == old(D) + {d}
      ensures forall x :: x in old(D) ==> x in D
      ensures forall x :: x != d && x in old(D) ==> x in D
    {
      D := D + {d};
    }

    // TODO: remove this method?
    // the reason we cannot model diff_device faithfully as the original
    // cobalt paper is that we cannot model the concrete value
    // for now, we make the add_device have the similar funcitonality as
    // diff_device (I'm not sure the incentive of having both diff_device and
    // add_device in the cobalt benchmark)
    method {:use} DiffDevice(d:int) returns (v:int)
      requires exists v :: v in D && v != d
      ensures v in D && v != d
    {
      v :| v in D && v != d;
    }

    // TODO: add pre/post conditions according to C
    // (the original cobalt paper doesn't seem to model the effect of CanSend to Central
    // but maybe I missed something in vcencode.ml)
    method {:use} AddConnection(sr:(int, int))
      requires IsValid()
      requires sr.0 in D
      requires sr.1 in D
      modifies this
      ensures IsValid()
      ensures D == old(D)
      ensures sr in CS
      ensures forall x :: x in old(CS) ==> x in CS
      ensures forall x :: x != sr && x !in old(CS) ==> x !in CS
      ensures CS == old(CS) + {sr}
      ensures C == old(C)
    {
      CS := CS + {sr};
    }

    method {:use} MakeCentral(d:int)
      requires IsValid()
      requires d in D
      ensures IsValid()
      modifies this
      ensures D == old(D)
      ensures CS == old(CS)
      ensures d in C
      ensures C == old(C) + {d}
      ensures forall x :: x in old(C) ==> x in C
      ensures forall x :: x != d && x in old(C) ==> x in C
    {
      C := C + {d};
    }

    method {:use} DeleteDevice(d:int, y:int)
      requires IsValid()
      requires d in D
      requires y in D && y != d && y in C
      modifies this
      ensures IsValid()
      ensures d in old(C) ==> C == old(C) - {d}
      ensures d !in old(C) ==> C == old(C)
      ensures forall x :: x in C ==> x in old(C)
      ensures forall x :: x != d && x in old(C) ==> x in C
      ensures d !in D
      ensures forall x :: x in D ==> x in old(D)
      ensures forall x :: x != d && x !in D ==> x !in old(D)
      ensures old(D) == D + {d}
      ensures forall x :: x in CS ==> x in old(CS)
      ensures forall x:(int, int) :: x.0 != d && x.1 != d && x !in CS ==> x !in old(CS)
      ensures forall x:(int, int) :: (x.0 == d || x.1 == d) ==> x !in CS
    {
      DeleteDeviceFromCS(d);
      if (d in C) {
        C := C - {d};
      }
      D := D - {d};
    }

    // TODO: simplify this?
    method DeleteDeviceFromCS(d:int)
      requires IsValid()
      requires d in D
      modifies this
      ensures IsValid()
      ensures old(C) == C
      ensures forall x :: x in CS ==> x in old(CS)
      ensures forall d :: d in D <==> d in old(D)
      ensures forall x:(int, int) :: x.0 != d && x.1 != d && x !in old(CS) ==> x !in CS
      ensures forall x:(int, int) :: (x.0 == d || x.1 == d) ==> x !in CS
      ensures forall x:(int, int) :: x.0 != d && x.1 != d && x !in CS ==> x !in old(CS)
    {
      var ss := CS;
      while ss != {}
        decreases |ss|
        invariant old(C) == C
        invariant forall d :: d in D <==> d in old(D)
        invariant ss <= CS
        invariant forall x:(int, int) :: x.0 != d && x.1 != d && x !in old(CS) ==> x !in CS
        invariant forall x:(int, int) :: (x.0 == d || x.1 == d) ==> x !in CS - ss
        invariant forall x:(int, int) :: x.0 != d && x.1 != d && x !in CS ==> x !in old(CS)
      {
        var i: (int, int) :| i in ss;
        if i.0 == d || i.1 == d {
          CS := CS - {i};
        }
        ss := ss - {i};
      }
    }

    constructor()
      ensures D == {} && CS == {} && C == {} 
    {
      D := {};
      CS := {};
      C := {};
    }
  }

}