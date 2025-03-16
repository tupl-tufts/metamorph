include "../Definitions.dfy"

module Problem {

    import opened Definitions

    predicate {:synthesize} Goal(db:SocialNetwork) reads db {
      true
    }
}