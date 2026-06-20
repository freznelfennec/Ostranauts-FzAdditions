using Freznel.FzAdditions.VM.Enum;
using System;
using System.Collections.Generic;
using System.Text;

namespace Freznel.FzAdditions.VM.Annotation
{
    [AttributeUsage(AttributeTargets.Class)]
    public class OperatorSetAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class UnaryOperatorAttribute : Attribute
    {
        public UnaryOperator Operator { get; private set; }

        public UnaryOperatorAttribute(UnaryOperator @operator)
        {
            Operator = @operator;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class BinaryOperatorAttribute : Attribute
    {
        public BinaryOperator Operator { get; private set; }
        public bool Commutative { get; private set; }

        public BinaryOperatorAttribute(BinaryOperator @operator, bool commutative = false)
        {
            Operator = @operator;
            Commutative = commutative;
        }
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    public class WordAttribute : Attribute
    {
        public string Word { get; private set; }

        public WordAttribute(string word)
        {
            Word = word;
        }
    }

}
