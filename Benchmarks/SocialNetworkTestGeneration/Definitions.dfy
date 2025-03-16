module Definitions {


  datatype User = User(friends: set<nat>, requests: set<nat>)

  class SocialNetwork {
    ghost var users: map<nat, User>

    constructor {:extern} ()
      ensures this.users == map[]
    {
      this.users := map[];
    }

    method {:use} {:extern} {:axiom} AddUser(name: nat)
      requires IsValid()
      requires name !in users
      modifies this
      ensures IsValid()
      ensures forall u :: u in old(users) <==> u in users && u != name
      ensures forall u :: u in old(users) ==> old(users)[u] == users[u]
      ensures name in users && 
        users[name].friends == {} && 
        users[name].requests == {} 

    method {:use} {:extern} {:axiom} RequestConnect(from: nat, to: nat)
      requires IsValid()
      requires from != to
      requires from in users && to in users && from !in users[to].friends && to !in users[from].friends && to !in users[from].requests
      modifies this
      ensures IsValid()
      ensures forall u :: u in users <==> u in old(users)
      ensures forall u :: (u in old(users) && u != to) ==> users[u] == old(users)[u]
      ensures to in users && 
        users[to].friends == old(users[to]).friends && 
        users[to].requests == old(users)[to].requests + {from}

    method {:use} {:extern} {:axiom} RemoveUser(name: nat)
      requires IsValid()
      requires forall other :: other in users ==> name !in users[other].friends && name !in users[other].requests
      requires name in users && users[name].friends == {} && users[name].requests == {}
      modifies this
      ensures IsValid()
      ensures forall u :: u in users <==> u != name && u in old(users)
      ensures forall u :: u in users ==> users[u] == old(users)[u]

    method {:use} {:extern} {:axiom} RequestResponse(from: nat, to: nat, response: bool)
      requires IsValid()
      requires from != to
      requires from in users && to in users && to in users[from].requests && to !in users[from].friends && from !in users[to].friends
      modifies this
      ensures IsValid()

      ensures forall u :: u in users <==> u in old(users)
      ensures forall u :: ((u in old(users) && u != from && u != to)
        ==> users[u] == old(users)[u])
      ensures !response ==> users[from] == old(users)[from]
      ensures !response ==> users[to] == old(users)[to]
      ensures response ==> users[to] == User(
          old(users)[to].friends + {from}, 
          old(users)[to].requests)
      ensures response ==> users[from] == User(
          old(users)[from].friends + {to}, 
          old(users)[from].requests - {to}) 
      
    ghost predicate IsValid() reads this {
      && (forall u1, u2 :: (u1 in users && u2 in users[u1].requests) ==> (u2 in users && u1 != u2))
      && (forall u1, u2 :: (u1 in users && u2 in users[u1].friends) ==> (u2 in users && u1 != u2 && u1 in users[u2].friends))
    }

    predicate {:extern} {:axiom} UserInDatabase(user:nat) 
      ensures UserInDatabase(user) <==> user in users

    function {:extern} {:axiom} GetFriends(user:nat):seq<nat>
      requires UserInDatabase(user)
      requires IsValid()
      reads this
      ensures forall u :: u in users[user].friends <==> u in GetFriends(user)
      ensures |users[user].friends| == |GetFriends(user)|
      ensures forall u :: u in GetFriends(user) ==> u in users

    function {:extern} {:axiom} FriendsBetween(u1:nat, u2:int):seq<nat>
      reads this
      requires IsValid()
      requires u1 in users
      requires u2 in users
      requires u1 != u2 && u2 !in users[u1].friends
      ensures forall u :: (u in users[u1].friends && u2 in users[u].friends) <==> u in FriendsBetween(u1, u2)

    function {:extern} {:axiom} AllOtherUsers(user:nat):seq<nat>
      requires user in users
      reads this
      ensures |users| == |AllOtherUsers(user)| + 1
      ensures forall u :: u in users && u != user<==> u in AllOtherUsers(user)
    
  }
    

  /*
    Recommends a friend to a user. 
    The recommended friend must be a friend of user's friend.
    It is also guaranteed to be the person with whom the user shares most friends.
  */
  method {:testEntry} RecommendFriend(user: nat, network: SocialNetwork) 
    returns (best: int)
    requires network.IsValid()
    requires forall user :: user in network.users ==> user < 6
  {
    print "Starting...\n";
    if !network.UserInDatabase(user) {
        print "No user with id ", user, ".\n";
        return -1; // User not in database
    }
  
    best := -1;
    var max := 0;

    var allUsers := network.AllOtherUsers(user);
    print |allUsers|;
    for i := 0 to |allUsers| 
      invariant forall j :: 0 <= j < i ==> (
       || allUsers[j] == user 
       || allUsers[j] in network.users[user].friends 
       || |network.FriendsBetween(user, allUsers[j])| <= max)
      invariant best != -1 ==> (
       && best in network.users 
       && best != user 
       && best !in network.users[user].friends
       && |network.FriendsBetween(user, best)| == max 
       && (exists u :: u in network.users && u in network.users[user].friends && best in network.users[u].friends))
      invariant best == -1 ==> max == 0
    {
      if (allUsers[i] in network.GetFriends(user)) {
        print "Cannot recommend ", i, " since they are already a friend.\n";
        continue; // Cannot recommend self or a user that is already a friend
      }
      if (|network.FriendsBetween(user, allUsers[i])| > max) {
        assert |network.FriendsBetween(user, allUsers[i])| > 0;
        ghost var link := network.FriendsBetween(user, allUsers[i])[0];
        assert link in network.users[user].friends && allUsers[i] in network.users[link].friends;
        print "New best recommendation is ", allUsers[i], ".\n";
        max := |network.FriendsBetween(user, allUsers[i])|;
        best := allUsers[i];
      }
    }
    assert forall j :: j in network.users ==> (
       || j == user 
       || j in network.users[user].friends 
       || |network.FriendsBetween(user, j)| <= max);
    if (best == -1) {
      print "No friends of friends to recommend.\n";
    } else {
      print "Recommending", best;
    }
    return best;
  }
}
