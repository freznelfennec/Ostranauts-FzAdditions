using Freznel.FzAdditions.VM.Annotation;
using System;
using System.Collections.Generic;
using System.Text;

namespace Freznel.FzAdditions.VM.Enum
{
    public enum BinaryOperator
    {
        Invalid = 0,
        [Word("+")]
        Add = 1,
        [Word("-")]
        Subtract = 2,
        [Word("*")]
        Multiply = 3,
        [Word("/")]
        Divide = 4,
        [Word("mod")]
        Mod = 5,
        [Word("and")]
        And = 6,
        [Word("nand")]
        Nand = 7,
        [Word("or")]
        Or = 8,
        [Word("xor")]
        Xor = 9,
        [Word("?eq")]
        Equal = 10,
        [Word("?ne")]
        NotEqual = 11,
        [Word("?gt")]
        GreaterThan = 12,
        [Word("?lt")]
        LessThan = 13,
        [Word("?geq")]
        GreaterThanOrEqualTo = 14,
        [Word("?leq")]
        LessThanOrEqualTo = 15
    }
}
