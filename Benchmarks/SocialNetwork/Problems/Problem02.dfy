include "../Definitions.dfy"

module Problem {

    import opened Definitions

    predicate {:synthesize} Goal(db:SocialNetwork) reads db {
      db.users.Keys == {0, 1} &&
      1 in db.users[0].friends &&
      0 in db.users[1].friends
    }
}