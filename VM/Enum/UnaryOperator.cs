using Freznel.FzAdditions.VM.Annotation;
using System;
using System.Collections.Generic;
using System.Text;

namespace Freznel.FzAdditions.VM.Enum
{
    public enum UnaryOperator
    {
        Invalid = 0,
        [Word("length")]
        [Word("abs")]
        Length = 1,
        [Word("negate")]
        SignedNegate = 2,
        [Word("invert")]
        UnsignedNegate = 3
    }
}
