include "../Definitions.dfy"

module Problem {

    import opened Definitions

    predicate {:synthesize} Goal(db:SocialNetwork) reads db {
      db.users.Keys == {0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19} &&
      1 in db.users[0].friends &&
      0 in db.users[1].friends &&
      1 in db.users[2].friends &&
      2 in db.users[1].friends &&
      2 in db.users[3].friends &&
      3 in db.users[2].friends &&
      3 in db.users[4].friends &&
      4 in db.users[3].friends &&
      5 in db.users[4].friends &&
      4 in db.users[5].friends &&
      6 in db.users[5].friends &&
      5 in db.users[6].friends &&
      6 in db.users[7].friends &&
      7 in db.users[6].friends &&
      8 in db.users[7].friends &&
      7 in db.users[8].friends &&
      8 in db.users[9].friends &&
      9 in db.users[8].friends &&
      9 in db.users[10].friends &&
      10 in db.users[9].friends &&
      10 in db.users[12].friends &&
      12 in db.users[10].friends &&
      11 in db.users[12].friends &&
      12 in db.users[11].friends &&
      13 in db.users[11].friends &&
      11 in db.users[13].friends &&
      14 in db.users[13].friends &&
      13 in db.users[14].friends &&
      15 in db.users[14].friends &&
      14 in db.users[15].friends &&
      16 in db.users[15].friends &&
      15 in db.users[16].friends &&
      17 in db.users[16].friends &&
      16 in db.users[17].friends &&
      17 in db.users[18].friends &&
      18 in db.users[17].friends &&
      18 in db.users[0].friends &&
      0 in db.users[18].friends
    }
}