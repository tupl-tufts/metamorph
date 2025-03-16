include "../Definitions.dfy"

module Problem {

    import opened Definitions

    predicate {:synthesize} Goal(db:SocialNetwork) reads db {
      db.users.Keys == {0, 1, 2, 3} &&
      1 in db.users[0].friends &&
      0 in db.users[1].friends &&
      1 in db.users[2].friends &&
      2 in db.users[1].friends &&
      2 in db.users[3].friends &&
      3 in db.users[2].friends &&
      3 in db.users[0].friends &&
      0 in db.users[3].friends
    }
}