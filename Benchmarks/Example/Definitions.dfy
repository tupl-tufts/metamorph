module Definitions {
  class SomeType {
    var field:int;
    // must include a constructor for each class:
    constructor() {}; 
    method {:use} SetField(i:int) 
      modifies this
      ensures field == i // API methods need to have specifications
    { this.field := i }
  }
}