module Definitions {

    class Node<V> {
        var next:Node?<V>
        var prev:Node?<V>
        var value:V

        constructor(next:Node?<V>, prev:Node?<V>, value:V) 
            ensures this.next == next
            ensures this.value == value
            ensures this.prev == prev
        {
            this.next := next;
            this.value := value;
            this.prev := prev;
        }
    }

    class List {
        var head:Node?<int>
        var tail:Node?<int>
        var Repr:seq<Node<int>>

        constructor() 
            ensures Repr == [] && IsValid()
        {
            head := null;
            tail := null;
            Repr := [];
        }

        function View():seq<int> reads this, Repr
        ensures |View()| == |Repr|
        ensures forall i :: 0 <= i < |View()| ==> View()[i] == Repr[i].value
        {
            ViewHelper(Repr)
        }

        function ViewHelper(s: seq<Node<int>>):seq<int>
            reads s 
            ensures |ViewHelper(s)| == |s|
            ensures forall i :: 0 <= i < |ViewHelper(s)| ==> ViewHelper(s)[i] == s[i].value
        {
            if s == [] then []
            else [s[0].value] + ViewHelper(s[1..])
        }

        method {:use} PushBack(value:int) 
            requires IsValid()
            modifies this, Repr
            ensures IsValid()
            ensures View() == old(View()) + [value]
            ensures Repr[..|Repr| - 1] == old(Repr)
            ensures fresh(Repr[|Repr| - 1])
        {
            var node := new Node(null, tail, value);
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

        method {:use} PushFront(value:int) 
            requires IsValid()
            modifies this, Repr
            ensures IsValid()
            ensures View() == [value] + old(View())
            ensures Repr[1..] == old(Repr)
            ensures fresh(Repr[0])
        {
            var node := new Node(head, null, value);
            if head == null {
                tail := node;
                head := node;
                Repr := [node];
            } else {
                head.prev := node;
                head := node;
                Repr := [node] + Repr;
            }
        }

        method {:use} PopFront() returns (value:int)
            requires IsValid()
            requires |Repr| > 0
            modifies this, Repr
            ensures IsValid()
            ensures [value] + View() == old(View())
            ensures Repr == old(Repr)[1..]
        {
            value := head.value;
            if head == tail {
                tail := null;
            } else {
                head.next.prev := null;
            }
            head := head.next;
            Repr := Repr[1..];
        }

        method {:use} PopBack() returns (value:int)
            requires IsValid()
            requires |Repr| > 0
            modifies this, Repr
            ensures IsValid()
            ensures var length := |old(Repr)| - 1; Repr == old(Repr)[..length]
            ensures View() + [value] == old(View())
        {
            value := tail.value;
            if head == tail {
                head := null;
            } else {
                tail.prev.next := null;
            }
            tail := tail.prev;
            var length := |Repr| - 1;
            Repr := Repr[..length];
        }

        function NextOf(i:nat): Node?<int>
            requires i < |Repr|
            requires |Repr| > 0
            reads this
        {
            if i == |Repr| - 1 then null else Repr[i + 1]
        }

        function PrevOf(i:nat): Node?<int>
            requires i < |Repr|
            requires |Repr| > 0
            reads this
        {
            if i == 0 then null else Repr[i - 1]
        }

        predicate IsValid() reads this, Repr 
        {
            || ( && |Repr| == 0 
                && head == null 
                && tail == null)
            || ( && |Repr| > 0 
                && head == Repr[0] 
                && tail == Repr[|Repr| - 1]
                && forall i :: 0 <= i < |Repr| ==> (
                    && Repr[i].prev == PrevOf(i)
                    && Repr[i].next == NextOf(i)
                ))

        }

        /*static method SanityCheck() {
            var t := new List();
            t.PushBack(2); // 2
            t.PushBack(3); // 2, 3
            t.PushFront(1); // 1, 2, 3
            var x := t.PopBack(); // returns 3
            var y := t.PopFront(); // returns 1
            var z := t.PopFront(); // returns 2
            assert x == 3;
            assert y == 1;
            assert z == 2;
        }*/
    }
}
