include "../Definitions.dfy" // include the API
module Problem {
  import opened Definitions  // import the API
  predicate {:synthesize} Goal(result:SomeType)
    reads result
  { result.field = 3 /* example synthesis goal */ }
}