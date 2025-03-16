using System.Collections.Generic;
using System.Numerics;
using System.Linq;

namespace Definitions {

  public partial class SocialNetwork {

    public Dictionary<BigInteger, (HashSet<BigInteger> friends, HashSet<BigInteger> requests)> users;

    public SocialNetwork() {
      users = new Dictionary<BigInteger, (HashSet<BigInteger> friends, HashSet<BigInteger> requests)>();
    }

    public void AddUser(BigInteger name) {
      users[name] = (new HashSet<BigInteger>(), new HashSet<BigInteger>());
    }

    public void RemoveUser(BigInteger name) {
      users.Remove(name);
    }
    
    public void RequestConnect(BigInteger from, BigInteger to) {
      users[to].requests.Add(from);
    }

    public void RequestResponse(BigInteger from, BigInteger to, bool response) {
      users[from].requests.Remove(to);
      if (response) {
        users[from].friends.Add(to);
        users[to].friends.Add(from);
      }
    }

    public bool UserInDatabase(BigInteger name) {
      return users.ContainsKey(name);
    }
    
    public Dafny.ISequence<BigInteger> GetFriends(BigInteger name) {
      var asArray = new BigInteger[users[name].friends.Count];
      int i = 0;
      foreach (var user in users[name].friends) {
        asArray[i++] = user;
      }
      return Dafny.Sequence<BigInteger>.FromArray(asArray);
    }
    
    public Dafny.ISequence<BigInteger> AllOtherUsers(BigInteger user) {
      var asArray = new BigInteger[users.Count - 1];
      int i = 0;
      foreach (var other in users.Keys) {
        if (user == other) {
          continue;
        }
        asArray[i++] = other;
      }
      return Dafny.Sequence<BigInteger>.FromArray(asArray);
    }
    
    public Dafny.ISequence<BigInteger> FriendsBetween(BigInteger one, BigInteger two) {
      var inbetween = new HashSet<BigInteger>();
      foreach (var user in users[one].friends) {
        if (users[user].friends.Contains(two)) {
          inbetween.Add(user);
        }
      }
      var asArray = new BigInteger[ inbetween.Count];
      int i = 0;
      foreach (var user in  inbetween) {
        asArray[i++] = user;
      }
      return Dafny.Sequence<BigInteger>.FromArray(asArray);
    }
  }
} // end of namespace Definitions