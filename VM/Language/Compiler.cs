using Freznel.FzAdditions.VM.Compiler;
using Freznel.FzAdditions.VM.Objects;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using static Freznel.FzAdditions.VM.Compiler.Tokenizer;

namespace Freznel.FzAdditions.VM.Language
{
    public class CompileList : List<VMObject>
    {
        public Token StartToken { get; set; }
    }

    public static class Compiler
    {

        private static void ThrowException(string msg, Token token) => throw new VMException($"{msg} (block {token.BlockIndex} line {token.Line})");

        public static List<VMObject> Compile(string str)
        {
            byte[] buf = Encoding.UTF8.GetBytes(str);
            return Compile(new Tokenizer(new MemoryStream(buf), Encoding.UTF8, -1));
        }

        public static List<VMObject> Compile(Tokenizer tokenizer)
        {
            if (!tokenizer.HasNext()) return null;

            Stack<CompileList> stack = new Stack<CompileList>();
            stack.Push(new CompileList() { StartToken = tokenizer.Peek() });

            Token tkn;

            while ((tkn = tokenizer.Next()) != null)
            {
                var top = stack.Peek();
                switch(tkn.Type)
                {
                    case TokenType.OpenBrace:
                        stack.Push(new CompileList() { StartToken = tkn });
                        break;
                    case TokenType.CloseBrace:
                        if (stack.Count <= 1) ThrowException("Expected {", tkn);
                        var list = stack.Pop();
                        //TODO: Add list to parent stack as a list object
                        break;
                    case TokenType.Number:
                        top.Add(new NumberObject((double)tkn.Value) { DebugTag = tkn.DebugTag });
                        break;
                    case TokenType.String:
                        //TODO: String objects
                        break;
                    case TokenType.Word:
                        string word = (string)tkn.Value;
                        if (NativeWords.Words.TryGetValue(word, out var nativeWord))
                        {
                            top.Add(nativeWord());
                        }
                        else
                        {
                            //TODO: Execute definition object
                        }
                        break;
                    default:
                        ThrowException($"Invalid token '{tkn.Value}'", tkn);
                        break;
                }

            }

            if (stack.Count > 1)
            {
                CompileList list = stack.Pop();
                ThrowException("Unterminated compile-time list", list.StartToken);
            }

            return stack.Pop();
        }




    }
}
