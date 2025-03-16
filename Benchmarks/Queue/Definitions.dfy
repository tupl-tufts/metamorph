module Definitions {

  class Queue {
    var head: Node?<int>
    var tail: Node?<int>

    var Repr: seq<Node<int>>
    // Should I do the spine thing?

    constructor()
      ensures Repr == [] && IsValid()
    {
      head := null;
      tail := null;
      Repr := [];
    }

    function NextOf(i:nat): Node?<int>
      requires i < |Repr|
      requires |Repr| > 0
      reads this
    {
      if i == |Repr| - 1 then null else Repr[i + 1]
    }

    predicate IsValid()
      reads this, Repr
    {
      || ( && |Repr| == 0
           && head == null
           && tail == null)
      || ( && |Repr| > 0
           && head == Repr[0]
           && tail == Repr[|Repr| - 1]
           && forall i :: 0 <= i < |Repr| ==> (
                              && Repr[i].next == NextOf(i)
                            ))
    }

    function View():seq<int> reads this, Repr
      ensures |View()| == |Repr|
      ensures forall i :: 0 <= i < |View()| ==> View()[i] == Repr[i].data
    {
      ViewHelper(Repr)
    }

    function ViewHelper(s: seq<Node<int>>):seq<int>
      reads s
      ensures |ViewHelper(s)| == |s|
      ensures forall i :: 0 <= i < |ViewHelper(s)| ==> ViewHelper(s)[i] == s[i].data
    {
      if s == [] then []
      else [s[0].data] + ViewHelper(s[1..])
    }

    method {:use} Rotate()
      requires IsValid()
      requires 0 < |Repr|
      modifies this, Repr
      ensures IsValid()  // && fresh(footprint - old(footprint))
      ensures |Repr| == old(|Repr|)
      ensures Repr[|Repr| - 1].data == old(Repr)[0].data
      ensures Repr[..|Repr| - 1] == old(Repr)[1..]
      // We cannot write the postcondition as
      // ensures Repr == old(Repr)[1..] + old(Repr)[..1]
      // since it is comparing on the heap Object `Node`
      // If we want to write Repr as seq<T>, then we need other ghost
      // field to keep track of the heap, which is closer to the original
      // Queue implementation
    {
      var t := Dequeue();
      assert Repr == old(Repr)[1..] ;
      Enqueue(t);
    }

    method {:use} IsEmpty() returns (isEmpty: bool)
      requires IsValid()
      ensures isEmpty <==> |Repr| == 0
    {
      isEmpty := head == null;
    }

    // Enqueue is like PushBack
    method {:use} Enqueue(t: int)
      requires IsValid()
      modifies this, Repr
      ensures View() == old(View()) + [t]
      ensures IsValid()
      ensures Repr[..|Repr| - 1] == old(Repr)
      ensures fresh(Repr[|Repr| - 1])

    {
      var node := new Node<int>(null, t);
      if head == null {
        tail := node;
        head := node;
        Repr := [node];
      } else {
        tail.next := node;
        tail := node;
        Repr := Repr + [node];
      }
    }


    method {:use} Front() returns (t: int)
      requires IsValid()
      requires 0 < |Repr|
      ensures t == Repr[0].data
    {
      t := head.data;
    }

    method {:use} Dequeue() returns (value: int)
      requires IsValid()
      requires |Repr| > 0
      modifies this, Repr
      ensures IsValid()
      ensures [value] + View() == old(View())
      ensures Repr == old(Repr)[1..]
      ensures value == old(Repr)[0].data
    {
      value := head.data;
      if head == tail {
        tail := null;
      }
      head := head.next;
      Repr := Repr[1..];
    }
  }

  // method referential_equality() {
  //   var n1 := new Node<int>(null, 1);
  //   var n2 := new Node<int>(null, 1);
  //   assert n1 != n2;
  // }

  class Node<T(0)> {
    var data: T
    var next: Node?<T>

    constructor(next: Node?<T>, data: T)
      ensures this.next == next
      ensures this.data == data
    {
      this.next := next;
      this.data := data;
    }
  }
}