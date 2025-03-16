using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using Dafny;

namespace HMAC {
    public partial class HMac {
        private Imports._IDigests _digest;
        private List<BigInteger> _inputSoFar;
        
        private List<BigInteger> _key;

        public HMac(Imports._IDigests digest) {
            _digest = digest;
            _inputSoFar = new List<BigInteger>();
        }

        public void Init(ISequence<BigInteger> key) {
          var keyArray = key.Elements;
          if (keyArray.Length != 16 && keyArray.Length != 32 && keyArray.Length != 48 && keyArray.Length != 64) {
            throw new ArgumentException("Should not be allowed by Dafny.");
          }
          _key = new List<BigInteger>(keyArray);
          _inputSoFar = new List<BigInteger>();
        }

        public void BlockUpdate(ISequence<BigInteger> input) {
          if (_key == null || _key.Count == 0) {
            throw new ArgumentException("Should not be allowed by Dafny.");
          }

          if (_inputSoFar.Count >= 2147483647) {
            throw new ArgumentException("Should not be allowed by Dafny.");
          }
          
          _inputSoFar.AddRange(input.Elements);
        }

        public ISequence<BigInteger> GetResult() {
          if (_key == null || _key.Count == 0) {
            throw new ArgumentException("Should not be allowed by Dafny.");
          }
          _inputSoFar = new List<BigInteger>();
          var result = new BigInteger[(int)Imports.__default.GetHashLength(_digest)];
          return Sequence<BigInteger>.FromArray(result);
        }

        public ISequence<BigInteger> GetInputSoFar() {
            return Sequence<BigInteger>.FromArray(_inputSoFar.ToArray());
        }

        public Imports._IDigests GetDigest() {
            return _digest;
        }

        public ISequence<BigInteger> GetKey() {
          return Sequence<BigInteger>.FromArray(_key.ToArray());
        }
    }
}