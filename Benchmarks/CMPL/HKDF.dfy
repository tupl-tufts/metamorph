// Copyright Amazon.com Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

/*
 * Implementation of the https://tools.ietf.org/html/rfc5869 HMAC-based key derivation function
 */

module Imports {
    type uint8 = i:nat | i < 256 witness 0
    const INT32_MAX_LIMIT:nat := 2147483647

    datatype Digests = 
        | SHA_256
        | SHA_384
        | SHA_512

    // Hash length in octets (bytes), e.g. GetHashLength(SHA_256) ==> 256 bits = 32 bytes ==> n = 32
    function GetHashLength(digest: Digests): int
    {
        match digest
        case SHA_256 => 32
        case SHA_384 => 48
        case SHA_512 => 64
    }
}

module HMAC {

  import opened Imports

  class {:extern "HMac"} HMac {

    // These functions are used to model the extern state
    // https://github.com/dafny-lang/dafny/wiki/Modeling-External-State-Correctly
    function {:extern} GetKey(): seq<uint8> reads this
    function {:extern} GetDigest(): Digests reads this
    function {:extern} GetInputSoFar(): seq<uint8> reads this

    constructor {:extern} (digest: Digests)
      ensures this.GetDigest() == digest
      ensures this.GetInputSoFar() == []

    method {:extern "Init"} {:use} Init(key: seq<uint8>)
      requires |key| == 16 || |key| == 32 || |key| == 48 || |key| == 64
      modifies this
      ensures this.GetKey() == key;
      ensures this.GetDigest() == old(this.GetDigest())
      ensures this.GetInputSoFar() == []

    method {:extern "BlockUpdate"} {:use} Update(input: seq<uint8>)
      requires |this.GetKey()| > 0
      requires |input| < INT32_MAX_LIMIT
      modifies this
      ensures this.GetInputSoFar() == old(this.GetInputSoFar()) + input
      ensures this.GetDigest() == old(this.GetDigest())
      ensures this.GetKey() == old(this.GetKey())

    method {:extern "GetResult"} {:use} GetResult() returns (s: seq<uint8>)
      requires |this.GetKey()| > 0
      modifies this
      ensures |s| == GetHashLength(this.GetDigest())
      ensures this.GetInputSoFar() == []
      ensures this.GetDigest() == old(this.GetDigest())
      ensures this.GetKey() == old(this.GetKey())
      ensures this.HashSignature(old(this.GetInputSoFar()), s);

    predicate {:axiom} HashSignature(message: seq<uint8>, s: seq<uint8>)

  }
}
module HKDF {

    import opened HMAC
    import opened Imports

    datatype Option<T> = None | Some(value:T)


    function Fill<T>(value: T, n: nat): seq<T>
        ensures |Fill(value, n)| == n
        ensures forall i :: 0 <= i < n ==> Fill(value, n)[i] == value
    {
        seq(n, _ => value)
    }

    method {:testEntry} Extract(salt: seq<uint8>, ikm: seq<uint8>, ghost digest: Digests, hmac: HMac) returns (prk: seq<uint8>)
        requires hmac.GetDigest() == digest
        requires |salt| != 0
        requires |salt| == 16 || |salt| == 32 || |salt| == 48 || |salt| == 64
        requires |ikm| < INT32_MAX_LIMIT
        modifies hmac
        ensures GetHashLength(hmac.GetDigest()) == |prk|
        ensures hmac.GetKey() == salt
        ensures hmac.GetDigest() == digest
    {
        // prk = HMAC-Hash(salt, ikm)
        hmac.Init(salt);
        hmac.Update(ikm);
        assert hmac.GetInputSoFar() == ikm;

        prk := hmac.GetResult();
        return prk;
    }

    // T is relational since the external hashMethod hmac.GetKey() ensures that the input and output of the hash method are in the relation hmac.HashSignature
    // T depends on Ti and Ti depends on hmac.HashSignature
    ghost predicate T(hmac: HMac, info: seq<uint8>, n: nat, res: seq<uint8>)
        requires 0 <= n < 256
        decreases n
    {
        if n == 0 then
        [] == res
        else
        var nMinusOne := n - 1;
        exists prev1, prev2 :: T(hmac, info, nMinusOne, prev1) && Ti(hmac, info, n, prev2) && prev1 + prev2 == res
    }

    ghost predicate Ti(hmac: HMac, info: seq<uint8>, n: nat, res: seq<uint8>)
        requires 0 <= n < 256
        decreases n, 1
    {
        if n == 0 then
        res == []
        else
        exists prev :: PreTi(hmac, info, n, prev) &&  hmac.HashSignature(prev, res)
    }

        // return T (i)
    ghost predicate PreTi(hmac: HMac, info: seq<uint8>, n: nat, res: seq<uint8>)
        requires 1 <= n < 256
        decreases n, 0
    {
        var nMinusOne := n - 1;
        exists prev | Ti(hmac, info, nMinusOne, prev) :: res == prev + info + [(n as uint8)]
    }

    method {:testEntry} Expand(prk: seq<uint8>, info: seq<uint8>, expectedLength: int, digest: Digests, ghost salt: seq<uint8>, hmac: HMac) returns (okm: seq<uint8>, ghost okmUnabridged: seq<uint8>)
        requires hmac.GetDigest() == digest
        requires 1 <= expectedLength <= 255 * GetHashLength(hmac.GetDigest())
        requires |salt| != 0
        requires hmac.GetKey() == salt
        requires |info| < INT32_MAX_LIMIT
        requires GetHashLength(hmac.GetDigest()) == |prk|
        modifies hmac
        ensures |okm| == expectedLength
        ensures hmac.GetKey() == prk
        ensures hmac.GetDigest() == digest
        ensures var n := (GetHashLength(digest) + expectedLength - 1) / GetHashLength(digest);
        && T(hmac, info, n, okmUnabridged)
        && (|okmUnabridged| <= expectedLength ==> okm == okmUnabridged)
        && (expectedLength < |okmUnabridged| ==> okm == okmUnabridged[..expectedLength])
    {
        // N = ceil(L / Hash Length)
        var hashLength := GetHashLength(digest);
        var n := (hashLength + expectedLength - 1) / hashLength;
        assert 0 <= n < 256;

        // T(0) = empty string (zero length)
        hmac.Init(prk);
        var t_prev := [];
        var t_n := t_prev;

        // T = T(0) + T (1) + T(2) + ... T(n)
        // T(1) = HMAC-Hash(PRK, T(1) | info | 0x01)
        // ...
        // T(n) = HMAC- Hash(prk, T(n - 1) | info | 0x0n)
        var i := 1;
        while i <= n
        invariant 1 <= i <= n + 1
        invariant |t_prev| == if i == 1 then 0 else hashLength
        invariant hashLength == |prk|
        invariant |t_n| == (i - 1) * hashLength
        invariant hmac.GetKey() == prk
        invariant hmac.GetDigest() == digest
        invariant hmac.GetInputSoFar() == []
        invariant T(hmac, info, i - 1, t_n)
        invariant Ti(hmac, info, i - 1, t_prev)
        {
        hmac.Update(t_prev);
        hmac.Update(info);
        hmac.Update([i as uint8]);
        assert hmac.GetInputSoFar() == t_prev + info + [i as uint8];

        // Add additional verification for T(n): github.com/awslabs/aws-encryption-sdk-dafny/issues/177
        t_prev := hmac.GetResult();
        // t_n == T(i - 1)
        assert Ti(hmac, info, i, t_prev);

        t_n := t_n + t_prev;
        // t_n == T(i) == T(i - 1) + Ti(i)
        i := i + 1;
        assert T(hmac, info, i - 1, t_n);
        }

        // okm = first L (expectedLength) bytes of T(n)
        okm := t_n;
        okmUnabridged := okm;
        assert T(hmac, info, n, okmUnabridged);

        if expectedLength < |okm| {
        okm := okm[..expectedLength];
        }
    }

    /*
    * The RFC 5869 KDF. Outputs L bytes of output key material.
    */
    method {:testEntry} Hkdf(digest: Digests, salt: Option<seq<uint8>>, ikm: seq<uint8>, info: seq<uint8>, L: int) returns (okm: seq<uint8>)
        requires 0 <= L <= 255 * GetHashLength(digest)
        requires salt.None? || |salt.value| == 16 || |salt.value| == 32 || |salt.value| == 48 || |salt.value| == 64
        requires |info| < INT32_MAX_LIMIT
        requires |ikm| < INT32_MAX_LIMIT
        ensures |okm| == L
    {
        if L == 0 {
        return [];
        }
        var hmac := new HMac(digest);
        var hashLength := GetHashLength(digest);

        var nonEmptySalt: seq<uint8>;
        match salt {
        case None =>
            nonEmptySalt := Fill(0, hashLength);
        case Some(s) =>
            nonEmptySalt := s;
        }

        var prk := Extract(nonEmptySalt, ikm, digest, hmac);
        ghost var okmUnabridged;
        okm, okmUnabridged := Expand(prk, info, L, digest, nonEmptySalt, hmac);
    }
}
