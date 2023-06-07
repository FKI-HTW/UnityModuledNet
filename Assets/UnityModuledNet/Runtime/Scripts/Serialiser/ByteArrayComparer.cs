using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CENTIS.UnityModuledNet.Serialiser
{
    public class ByteArrayComparer : EqualityComparer<byte[]>
    {
        public override bool Equals(byte[] left, byte[] right)
        {
            if (left == null || right == null)
                return left == right;
            if (ReferenceEquals(left, right))
                return true;
            if (left.Length != right.Length)
                return false;
            return left.SequenceEqual(right);
        }

        public override int GetHashCode(byte[] obj)
        {
            if (obj == null)
                throw new ArgumentNullException("obj is null!");
            return obj.Length;
        }
    }
}
