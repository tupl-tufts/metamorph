include "[File]"

module Baseline {

    import opened Definitions
    import opened Problem

    datatype Tree = Null | Value(name:int)
    datatype MethodCall = Constructor(name:int, left:Tree, right:Tree, value:int)

    method {:testInline [BASELINE_INLINING_DEPTH]} SynthesizeRecursive(s:seq<MethodCall>, env:map<int, BinaryTree>, lastConstructed:Tree)
        returns (binaryTree:BinaryTree?) 
        requires lastConstructed.Value? ==> lastConstructed.name in env
        requires forall m1, m2 :: m1 in s && m2 in s && m1 != m2 ==> m1.name != m2.name
    {
        if |s| == 0 {
            if lastConstructed.Null? {
                return null;
            }
            return env[lastConstructed.name];
        }
        if ((s[0].left.Value? && s[0].left.name !in env) || (s[0].right.Value? && s[0].right.name !in env)) {
            return null;
        }
        var left := if s[0].left.Null? then null else env[s[0].left.name];
        var right := if s[0].right.Null? then null else env[s[0].right.name];
        if (((if left == null then {} else left.Repr) * (if right == null then {} else right.Repr) != {}) 
         || (left != null && !left.IsValid()) 
         || (right != null && !right.IsValid())) {
            return null;
        }
        var newTree := new BinaryTree(left, right, s[0].value);
        var newEnv := env[s[0].name := newTree];
        var result := SynthesizeRecursive(s[1..], newEnv, Value(s[0].name));
        return result;
    }

    method {:testEntry} Synthesize(s:seq<MethodCall>) {
        var tree := SynthesizeRecursive(s, map[], Null);
        if (tree == null || !Goal(tree)) {
            return;
        }
        print("Synthesis goal reached");
    }

}