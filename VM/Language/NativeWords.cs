using Freznel.FzAdditions.VM.Annotation;
using Freznel.FzAdditions.VM.Enum;
using Freznel.FzAdditions.VM.Objects;
using Freznel.FzAdditions.VM.Objects.Operator;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Freznel.FzAdditions.VM.Language
{
    public static class NativeWords
    {
        public static Dictionary<string, Func<VMObject>> Words = new();

        private static void AddEnumWords<T>(T val, VMObject obj) where T : System.Enum
        {
            Type enumType = typeof(T);
            MemberInfo memberInfo = enumType.GetMember(val.ToString()).FirstOrDefault();
            if (memberInfo == null) { FzAdditions.Logger.LogError($"Failed to get enum member {val.ToString()} for operator enum {enumType.Name}"); return; }
            var attrs = (WordAttribute[])memberInfo.GetCustomAttributes(typeof(WordAttribute), false);
            foreach (var attr in attrs)
            {
                string word = attr.Word;
                if (Words.ContainsKey(word)) { FzAdditions.Logger.LogError($"Operator enum {enumType.Name} attempted to override existing native word {word}"); continue; }
                Words.Add(word, () => obj);
            }
        }

        static NativeWords()
        {
            //Unary Operators
            foreach (var op in (UnaryOperator[])System.Enum.GetValues(typeof(UnaryOperator))) AddEnumWords(op, new UnaryOperatorObject(op));

            //Binary Operators
            foreach (var op in (BinaryOperator[])System.Enum.GetValues(typeof(BinaryOperator))) AddEnumWords(op, new BinaryOperatorObject(op));

            //Meta operators
            foreach (var op in (MetaOperator[])System.Enum.GetValues(typeof(MetaOperator))) AddEnumWords(op, new MetaOperatorObject(op));






            FzAdditions.Logger.LogInfo($"Registered {Words.Count} native words");
        }
    }
}
