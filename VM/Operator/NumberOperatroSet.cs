using Freznel.FzAdditions.VM.Annotation;
using Freznel.FzAdditions.VM.Enum;
using Freznel.FzAdditions.VM.Objects;
using System;
using System.Collections.Generic;
using System.Text;

namespace Freznel.FzAdditions.VM.Operator
{
    [OperatorSet]
    public static class NumberOperatroSet
    {
        private static readonly long DoubleMantissaMask = (1L << 52) - 1L;

        [UnaryOperator(UnaryOperator.Length)]
        public static VMObject NumberLength(NumberObject a) => a.Value >= 0 ? a : new NumberObject(-a.Value);

        [UnaryOperator(UnaryOperator.UnsignedNegate)]
        public static VMObject NumberUnsignedNegate(NumberObject a) => new NumberObject(~(BitConverter.DoubleToInt64Bits(a.Value) & DoubleMantissaMask));

        [UnaryOperator(UnaryOperator.SignedNegate)]
        public static VMObject NumberSignedNegate(NumberObject a) => a.Value == 0 ? a : new NumberObject(-a.Value);

        [BinaryOperator(BinaryOperator.Add, true)]
        public static VMObject NumberAdd(NumberObject a, NumberObject b) => new NumberObject(a.Value + b.Value);




    }
}
