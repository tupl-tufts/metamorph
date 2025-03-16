include "[File]"

module Baseline {

    import opened Definitions
    import opened Problem

    datatype MethodCall = AddUser(name:nat) | RequestConnect(from:nat, to:nat) | RemoveUser(name:nat) | RequestResponse(from:nat, to:nat, response:bool)

    predicate CanRemove(network:SocialNetwork, name:nat) 
        reads network
        ensures CanRemove(network, name) <==> (forall other :: other in network.users ==> name !in network.users[other].friends && name !in network.users[other].requests)
    {
        forall other :: other in network.users ==> name !in network.users[other].friends && name !in network.users[other].requests
    }

    method {:testInline [BASELINE_INLINING_DEPTH]} SynthesizeRecursive(s:seq<MethodCall>, network:SocialNetwork)
    returns (success:bool) 
    modifies network
    {
        if |s| == 0 {
            return true;
        }
        var nextMethodCall := s[0];
        match nextMethodCall {
            case AddUser(name) => 
                if network.IsValid() && name !in network.users{
                    network.AddUser(name);
                } else {
                    return false;
                }
            case RemoveUser(name) => 
                if network.IsValid() && name in network.users && network.users[name] == User({}, {}) && CanRemove(network, name) {
                    network.RemoveUser(name);
                } else {
                    return false;
                }
            case RequestConnect(from, to) => 
                if network.IsValid() && from != to && from in network.users && to in network.users && from !in network.users[to].friends && to !in network.users[from].friends && to !in network.users[from].requests {
                    network.RequestConnect(from, to);
                } else {
                    return false;
                }
            case RequestResponse(from, to, response) => 
                if network.IsValid() && from in network.users && to in network.users && to in network.users[from].requests && to !in network.users[from].friends && from !in network.users[to].friends {
                    network.RequestResponse(from, to, response);
                } else {
                    return false;
                }
        }
        success := SynthesizeRecursive(s[1..], network);
        return success;
    }

    method {:testEntry} Synthesize(s:seq<MethodCall>) {
        var network := new SocialNetwork();
        var success := SynthesizeRecursive(s, network);
        if (!success || !Goal(network)) {
            return;
        }
        print("Synthesis goal reached");
    }

}
