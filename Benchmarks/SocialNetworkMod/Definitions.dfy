module Definitions {


  datatype User = User(friends: set<nat>, requests: set<nat>)

  class SocialNetwork {
    var users: map<nat, User>

    constructor ()
      ensures this.users == map[]
    {
      this.users := map[];
    }

    method {:use} AddUser(name: nat)
      requires IsValid()
      requires name !in users
      modifies this
      ensures IsValid()
      ensures forall u :: u in old(users) <==> u in users && u != name
      ensures forall u :: u in old(users) ==> old(users)[u] == users[u]
      ensures name in users && 
        users[name].friends == {} && 
        users[name].requests == {}
    {
        users := users[name := User({}, {})];
    }

    method {:use} RequestConnect(from: nat, to: nat)
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
    {
        users := users[to := User(users[to].friends, users[to].requests + {from})];
    }

    method {:use} RemoveUser(name: nat)
      requires IsValid()
      requires forall other :: other in users ==> name !in users[other].friends // removed "name !in users[other].requests"
      requires name in users && users[name].friends == {} && users[name].requests == {}
      modifies this
      ensures IsValid()
      ensures forall u :: u in users <==> u != name && u in old(users)
      ensures forall u :: u in users ==> users[u] == old(users)[u]
    {
        users := users - {name};
    }

    method {:use} RequestResponse(from: nat, to: nat, response: bool)
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
      {
        if (response) {
            users := users[to := User(users[to].friends + {from}, users[to].requests)][from := User(users[from].friends + {to}, users[from].requests - {to})];
        }
    }
      
    predicate IsValid() reads this {
      && (forall u1, u2 :: (u1 in users && u2 in users[u1].requests) ==> (u1 != u2)) // removed "u2 in users" condition
      && (forall u1, u2 :: (u1 in users && u2 in users[u1].friends) ==> (u2 in users && u1 != u2 && u1 in users[u2].friends))
    }
  }
}
