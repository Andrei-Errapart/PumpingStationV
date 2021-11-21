using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ControlPanel
{
    /// <summary>
    /// Array of bits.
    /// </summary>
    public class BitArray
    {
        public readonly int Length;

        public bool this[int Index]
        {
            set
            {
                int array_index = Index / 32;
                if (value)
                {
                    _Bits[Index / 32] |= (UInt32)(1 << (Index & 0x1F));
                }
                else
                {
                    _Bits[Index / 32] &= (UInt32)((~1) << (Index & 0x1F));
                }
            }
            get
            {
                return (_Bits[Index / 32] & (1 << (Index & 0x1F))) != 0;
            }
        }

        public BitArray(int Length)
        {
            this.Length = Length;
            _Bits = new UInt32[(Length + 31) / 32];
        }

        private readonly UInt32[] _Bits;
    }
}
