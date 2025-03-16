module Imports {
  datatype ExitCode = 
    | OK 
    | STACK_OVERFLOW
    | STACK_UNDERFLOW
    | INTEGER_OVERFLOW
    | INTEGER_UNDERFLOW
    | DIV_BY_ZERO
    | INVALID_JUMP
    | INVALID_MEMORY_LOAD
    | INVALID_MEMORY_STORE
}

module Definitions {

  import opened Imports

  class VM {
    static const MAX_WORD:nat := 65535
    static const MAX_STACK_SIZE:nat := 10
    static const MEMORY_SIZE := 5
    var data : seq<nat>
    var stack : seq<nat>
    var exitCode : ExitCode
    // we model program counter in such a way that:
    // -- pc == 0 corresponds to the end of the program
    // -- every method corresponds to some pc > 0
    // -- pc < 0 can only occur due to an invalid jump
    var pc:int 


    constructor(pc:nat)
      ensures |stack| == 0
      ensures IsValid()
      ensures this.pc == pc
      ensures IsRunning()
    {
      this.data := [0, 1, 2, 3, 4];
      this.pc := pc;
      stack := [];
      exitCode := OK;
    }

    predicate IsValid()
      reads this
    {
      && |data| == MEMORY_SIZE
      && |stack| <= MAX_STACK_SIZE
      // enumerating possible stack locations to trigger 
      // corresponding quantifiers. This should ideally be 
      // automated (since we know apriori that the stack's 
      // size is limited.)
      && (|stack| <= 0 || stack[0] <= MAX_WORD)
      && (|stack| <= 1 || stack[1] <= MAX_WORD)
      && (|stack| <= 2 || stack[2] <= MAX_WORD)
      && (|stack| <= 3 || stack[3] <= MAX_WORD)
      && (|stack| <= 4 || stack[4] <= MAX_WORD)
      && (|stack| <= 5 || stack[5] <= MAX_WORD)
      && (|stack| <= 6 || stack[6] <= MAX_WORD)
      && (|stack| <= 7 || stack[7] <= MAX_WORD)
      && (|stack| <= 8 || stack[8] <= MAX_WORD)
      && (|stack| <= 9 || stack[9] <= MAX_WORD)
      && (forall i :: 0 <= i < |stack| ==> stack[i] <= MAX_WORD)
      && (forall i :: 0 <= i < |data| ==> data[i] <= MAX_WORD)
    }

    predicate IsRunning() reads this {
        exitCode == OK
    }

    method {:use} Push(word:nat)
      requires IsValid()
      requires IsRunning()
      requires word <= MAX_WORD
      requires pc > 0
      modifies this
      ensures IsValid()
      ensures data == old(data)
      ensures pc == old(pc) - 1
      ensures |old(stack)| < MAX_STACK_SIZE  <==> 
        (&& IsRunning()
         && |stack| == |old(stack)| + 1
         && stack[0] == word
         && forall i :: 1 <= i < |stack| ==> old(stack)[i - 1] == stack[i]
        )
      ensures |old(stack)| == MAX_STACK_SIZE <==> 
        (&& exitCode == STACK_OVERFLOW
         && |stack| == |old(stack)|
         && forall i :: 0 <= i < |stack| ==> stack[i] == old(stack)[i]
        )
    {
      pc := pc - 1;
      if |stack| == MAX_STACK_SIZE {
        exitCode := STACK_OVERFLOW;
      } else {
        stack := [word] + stack;
      }
    }

    method {:use} Pop() returns (word:nat)
      requires IsValid()
      requires IsRunning()
      requires pc > 0
      modifies this
      ensures IsValid()
      ensures data == old(data)
      ensures pc == old(pc) - 1
      ensures |old(stack)| == 0 <==> (
        && exitCode == STACK_UNDERFLOW
        && |old(stack)| == |stack|
        && forall i :: 0 <= i < |stack| ==> stack[i] == old(stack)[i])
      ensures |old(stack)| != 0  ==> (
        && IsValid()
        && IsRunning()
        && word == old(stack)[0]
        && |stack| == |old(stack)| - 1
        && forall i :: 0 <= i < |stack| ==> old(stack)[i+1] == stack[i])
    {
      pc := pc - 1;
      if |stack| == 0 {
        exitCode := STACK_UNDERFLOW;
        word := 0;
      } else {
        word := stack[0];
        stack := stack[1..];
      }
    }

    method {:use} Add() 
      requires IsValid()
      requires IsRunning()
      requires pc > 0
      modifies this
      ensures IsValid()
      ensures data == old(data)
      ensures pc == old(pc) - 1
      ensures |old(stack)| < 2 <==> (
        && exitCode == STACK_UNDERFLOW
        && |old(stack)| == |stack|
        && forall i :: 0 <= i < |stack| ==> stack[i] == old(stack)[i]
      )
      ensures (&& 2 <= |old(stack)|
               && old(stack)[0] + old(stack)[1] > MAX_WORD) <==>
              (&& exitCode == INTEGER_OVERFLOW
               && |old(stack)| == |stack|
               && forall i :: 0 <= i < |stack| ==> stack[i] == old(stack)[i])
      ensures (&& 2 <= |old(stack)|
               && old(stack)[0] + old(stack)[1] <= MAX_WORD) <==> 
              (&& IsValid() 
               && IsRunning()
               && |stack| == |old(stack)| - 1
               && stack[0] == old(stack)[0] + old(stack)[1]
               && forall i :: 1 <= i < |stack| ==> stack[i] == old(stack)[i + 1])
    {
      pc := pc - 1;
      if |stack| < 2 {
        exitCode := STACK_UNDERFLOW;
      } else if (stack[0] + stack[1] > MAX_WORD) {
        exitCode := INTEGER_OVERFLOW;
      } else {
        stack := [stack[0] + stack[1]] + stack[2..];
      }
    }

    method {:use} Sub() 
      requires IsValid()
      requires IsRunning()
      requires pc > 0
      modifies this
      ensures IsValid()
      ensures data == old(data)
      ensures pc == old(pc) - 1
      ensures |old(stack)| < 2 <==> (
        && exitCode == STACK_UNDERFLOW
        && |old(stack)| == |stack|
        && forall i :: 0 <= i < |stack| ==> stack[i] == old(stack)[i]
      )
      ensures (&& 2 <= |old(stack)|
               && old(stack)[0] < old(stack)[1]) <==>
               (&& exitCode == INTEGER_UNDERFLOW
                && |old(stack)| == |stack|
                && forall i :: 0 <= i < |stack| ==> stack[i] == old(stack)[i])
      ensures (&& 2 <= |old(stack)|
               && old(stack)[0] >= old(stack)[1]) <==> 
              (&& IsValid() 
               && IsRunning()
               && |stack| == |old(stack)| - 1
               && stack[0] == old(stack)[0] - old(stack)[1]
               && forall i :: 1 <= i < |stack| ==> stack[i] == old(stack)[i + 1])
    {
      pc := pc - 1;
      if |stack| < 2 {
        exitCode := STACK_UNDERFLOW;
      } else if (stack[0] < stack[1]) {
        exitCode := INTEGER_UNDERFLOW;
      } else {
        stack := [stack[0] - stack[1]] + stack[2..];
      }
    }

    method {:use} Div() 
      requires IsValid()
      requires IsRunning()
      requires pc > 0
      modifies this
      ensures IsValid()
      ensures data == old(data)
      ensures pc == old(pc) - 1
      ensures |old(stack)| < 2 <==> (
        && exitCode == STACK_UNDERFLOW
        && |old(stack)| == |stack|
        && forall i :: 0 <= i < |stack| ==> stack[i] == old(stack)[i]
      )
      ensures (&& 2 <= |old(stack)|
               && old(stack)[1] == 0) <==>
               (&& exitCode == DIV_BY_ZERO
                && |old(stack)| == |stack|
                && forall i :: 0 <= i < |stack| ==> stack[i] == old(stack)[i])
      ensures (&& 2 <= |old(stack)|
               && old(stack)[1] != 0) <==> 
              (&& IsValid() 
               && IsRunning()
               && |stack| == |old(stack)| - 1
               && stack[0] == old(stack)[0] / old(stack)[1]
               && forall i :: 1 <= i < |stack| ==> stack[i] == old(stack)[i + 1])
    {
      pc := pc - 1;
      if |stack| < 2 {
        exitCode := STACK_UNDERFLOW;
      } else if (stack[1] == 0) {
        exitCode := DIV_BY_ZERO;
      } else {
        stack := [stack[0] / stack[1]] + stack[2..];
      }
    }

    method {:use} Mul() 
      requires IsValid()
      requires IsRunning()
      requires pc > 0
      modifies this
      ensures IsValid()
      ensures data == old(data)
      ensures pc == old(pc) - 1
      ensures |old(stack)| < 2 <==> (
        && exitCode == STACK_UNDERFLOW
        && |old(stack)| == |stack|
        && forall i :: 0 <= i < |stack| ==> stack[i] == old(stack)[i]
      )
      ensures (&& 2 <= |old(stack)|
               && old(stack)[0] * old(stack)[1] > MAX_WORD) <==>
               (&& exitCode == INTEGER_OVERFLOW
                && |old(stack)| == |stack|
                && forall i :: 0 <= i < |stack| ==> stack[i] == old(stack)[i])
      ensures (&& 2 <= |old(stack)|
               && old(stack)[0] * old(stack)[1] <= MAX_WORD) <==> 
              (&& IsValid() 
               && IsRunning()
               && |stack| == |old(stack)| - 1
               && stack[0] == old(stack)[0] * old(stack)[1]
               && forall i :: 1 <= i < |stack| ==> stack[i] == old(stack)[i + 1])
    {
      pc := pc - 1;
      if |stack| < 2 {
        exitCode := STACK_UNDERFLOW;
      } else if (stack[0] * stack[1] > MAX_WORD) {
        exitCode := INTEGER_OVERFLOW;
      } else {
        stack := [stack[0] * stack[1]] + stack[2..];
      }
    }

    method {:use} Jump(k:nat)
      requires IsValid()
      requires IsRunning()
      requires pc > 0
      modifies this
      ensures IsValid()
      ensures data == old(data)
      ensures pc == old(pc) - 1 - k
      ensures |stack| == |old(stack)|
      ensures forall i :: 0 <= i < |stack| ==> stack[i] == old(stack)[i]
      ensures old(pc) - 1 - k >= 0 ==> IsValid()
      ensures old(pc) - 1 - k < 0 <==> exitCode == INVALID_JUMP
      ensures old(pc) - 1 - k >= 0 <==> IsRunning()
    {
      pc := pc - 1 - k;
      if (pc < 0) {
        exitCode := INVALID_JUMP;
      }
    }

    method {:use} Jz(k:nat)
      requires IsValid()
      requires IsRunning()
      requires pc > 0
      modifies this
      ensures IsValid()
      ensures data == old(data)
      ensures |stack| == 0 <==> exitCode == STACK_UNDERFLOW
      ensures (|stack| > 0 && stack[0] == 0 && old(pc) - 1 - k < 0) <==>
               exitCode == INVALID_JUMP
      ensures (|stack| > 0 && (stack[0] != 0 || old(pc) - 1 - k >= 0)) <==>
               IsRunning()
      ensures |stack| == |old(stack)|
      ensures forall i :: 0 <= i < |stack| ==> stack[i] == old(stack)[i]
      ensures |old(stack)| > 0 ==> (pc == (
        if old(stack)[0] == 0 then old(pc) - 1 - k else old(pc) - 1
      ))
    {
      if |stack| == 0 {
        exitCode := STACK_UNDERFLOW;
        return;
      } 
      if stack[0] == 0 {
        pc := pc - 1 - k;
      } else {
        pc := pc - 1;
      }
      if pc < 0 {
        exitCode := INVALID_JUMP;
      }
    }

    method {:use} NOP() 
      requires IsValid()
      requires IsRunning()
      requires pc > 0
      modifies this
      ensures IsValid()
      ensures data == old(data)
      ensures IsValid()
      ensures |stack| == |old(stack)|
      ensures IsRunning()
      ensures forall i :: 0 <= i < |stack| ==> stack[i] == old(stack)[i]
      ensures pc == old(pc) - 1
    {
      pc := pc - 1;
    }

    method {:use} Store(at:nat)
      requires IsValid()
      requires IsRunning()
      requires pc > 0
      modifies this
      ensures IsValid()
      ensures pc == old(pc) - 1
      ensures |old(stack)| == 0 <==> 
              (&& exitCode == STACK_UNDERFLOW
               && |old(stack)| == |stack|
               && forall i :: 0 <= i < |stack| ==> stack[i] == old(stack)[i])
      ensures (|old(stack)| > 0 && at >= |data|) <==> 
              (&& exitCode == INVALID_MEMORY_STORE
               && |old(stack)| == |stack|
               && forall i :: 0 <= i < |stack| ==> stack[i] == old(stack)[i])
      ensures (|old(stack)| > 0 && at < |data|) <==> (
        && |stack| == |old(stack)| - 1
        && |data| == |old(data)|
        && data[at] == old(stack)[0]
        && IsRunning()
        && (forall i :: 0 <= i < |stack| ==> stack[i] == old(stack)[i+1])
        && forall i :: (0 <= i < |data| && i != at) ==> data[i] == old(data)[i]
      )
    {
      pc := pc - 1;
      if |stack| == 0 {
        exitCode := STACK_UNDERFLOW;
      } else if at >= |data| {
        exitCode := INVALID_MEMORY_STORE;
      } else {
        data := data[..at] + [stack[0]] + data[at+1..];
        stack := stack[1..];
      }
    }

    method {:use} Load(at:nat)
      requires IsValid()
      requires IsRunning()
      requires pc > 0
      modifies this
      ensures IsValid()
      ensures pc == old(pc) - 1
      ensures |old(stack)| == MAX_STACK_SIZE <==> 
              (&& exitCode == STACK_OVERFLOW
               && |old(stack)| == |stack|
               && forall i :: 0 <= i < |stack| ==> stack[i] == old(stack)[i])
      ensures (|old(stack)| < MAX_STACK_SIZE && at >= |data|) <==> 
              (&& exitCode == INVALID_MEMORY_LOAD
               && |old(stack)| == |stack|
               && forall i :: 0 <= i < |stack| ==> stack[i] == old(stack)[i])
      ensures (|old(stack)| < MAX_STACK_SIZE && at < |data|) <==> (
        && |stack| == |old(stack)| + 1
        && IsRunning()
        && data == old(data)
        && stack[0]== old(data)[at]
        && (forall i :: 1 <= i < |stack| ==> stack[i] == old(stack)[i-1])
      )
    {
      pc := pc - 1;
      if |stack| == MAX_STACK_SIZE {
        exitCode := STACK_OVERFLOW;
      } else if at >= |data| {
        exitCode := INVALID_MEMORY_LOAD;
      } else {
        stack := [data[at]] + stack;
      }
    }

  }
}