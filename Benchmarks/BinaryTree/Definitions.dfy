module Definitions {

    class BinaryTree {

        var left:BinaryTree?
        var right:BinaryTree?
        /*ghost*/ var Repr:set<BinaryTree>
        /*ghost*/  var View:seq<int>
        /*ghost*/  var Height:nat
        var value:int

        /*ghost*/ predicate {:fuel 5, 6} IsValid() 
            reads this, Repr
        {
            && this in Repr
            && (left != null ==> left in Repr)
            && (right != null ==> right in Repr)
            && ReprOf(left) * ReprOf(right) == {}
            && Repr == ReprOf(left) + ReprOf(right) + {this}
            && this !in ReprOf(left)
            && this !in ReprOf(right)
            && (left != null ==> left.IsValid())
            && (right != null ==> right.IsValid())
            && (var leftViewSize := if left == null then 0 else |left.View|;
            var rightViewSize := if right == null then 0 else |right.View|;
            && |View| == leftViewSize + rightViewSize + 1
            && (left != null ==> (forall i :: 0 <= i < |left.View| ==> 
                                    View[i] == left.View[i]))
            && View[leftViewSize] == value
            && (right != null ==> (forall i :: 0 <= i < |right.View| ==> 
                                    View[i + 1 + leftViewSize] == right.View[i])))
            && (var leftHeight := if left == null then 0 else left.Height + 1;
                var rightHeight := if right == null then 0 else right.Height + 1;
                Height == if leftHeight > rightHeight then leftHeight else rightHeight)
        }

        /*ghost*/ function ReprOf(tree: BinaryTree?):set<BinaryTree> 
            reads tree
        {
            if tree == null then {} else tree.Repr
        }

        /*ghost*/ function ViewOf(tree:BinaryTree?):seq<int> 
            reads tree
        {
            if tree == null then [] else tree.View
        }

        constructor(left:BinaryTree?, right:BinaryTree?, value:int)
            requires (if left == null then {} else left.Repr) * (if right == null then {} else right.Repr) == {}
            requires left == null || left.IsValid()
            requires right == null || right.IsValid()
            ensures this.left == left
            ensures this.right == right
            ensures this.value == value
            ensures IsValid()
        {
            this.left := left;
            this.right := right;
            this.value := value;
            Repr := {this} + (if left == null then {} else left.Repr) + (if right == null then {} else right.Repr);
            View := (if left == null then [] else left.View) + [value] + (if right == null then [] else right.View);
            var leftHeight := if left == null then 0 else left.Height + 1;
            var rightHeight := if right == null then 0 else right.Height + 1;
            Height := if leftHeight > rightHeight then leftHeight else rightHeight;
        }
    }
}
