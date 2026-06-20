using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Freznel.FzAdditions.VM.Compiler
{
    /*  
     *  #Add definition
     *  : three 1 2 + ;
     *  
     *  #Simple control flow
     *  
     *  &a @ 10 ?gt #Is local variable a greater than 10?
     *  { "Greater than 10" print } #Run if true
     *  { "Less than 10" print } #Run if false
     *  ? #Ternary operator
     *  $ #Eval operator
     *  
     */

    public class Tokenizer
    {
        public enum TokenType
        {
            Invalid,
            Word,
            String,
            Number,
            OpenBrace,
            CloseBrace,
            VariableRef
        }

        public record Token
        {
            public TokenType Type {  get; private set; }
            public object Value { get; private set; }
            public int BlockIndex { get; private set; }
            public int Line { get; private set; }

            public int DebugTag => (BlockIndex << 16) & Line;

            public Token(TokenType type, object value, int blockIndex, int line)
            {
                Type = type;
                Value = value;
                BlockIndex = blockIndex;
                Line = line;
            }
        }



        private static readonly char ESCAPE = '\\';

        private static readonly char LIST_START = '{';
        private static readonly char LIST_END = '}';

        private static readonly char CHAR_STR = '"';

        private readonly StreamReader reader;
        private readonly LinkedList<Token> queue;
        private int line;
        private int blockIndex;
        private StringBuilder sb;

        public Tokenizer(Stream stream, Encoding encoding, int blockIndex)
        {
            this.queue = new LinkedList<Token>();
            this.reader = new StreamReader(stream, encoding);
            this.blockIndex = blockIndex;
            this.line = 1;
            this.sb = new StringBuilder();
        }
        public Tokenizer(Stream stream, Encoding encoding) : this(stream, encoding, -1) { }

        public bool HasNext()
        {
            if (queue.Count > 0) return true;
            var tkn = ReadNextToken();
            if (tkn == null) return false;
            queue.AddLast(tkn);
            return true;
        }

        public Token Next()
        {
            if (queue.Count > 0)
            {
                var last = queue.Last.Value;
                queue.RemoveLast();
                return last;
            }
            return ReadNextToken();
        }

        public Token Peek(int idx)
        {
            while (queue.Count < idx + 1)
            {
                var tkn = ReadNextToken();
                if (tkn == null) break;
                queue.AddLast(tkn);
            }
            if (queue.Count < idx + 1) return null;
            LinkedListNode<Token> node = queue.Last;
            for (int i=0; i<idx; i++) node = node.Previous;
            return (node == null) ? null : node.Value;
        }

        public Token Peek() => Peek(0);

        private void ReadWhitespace()
        {
            int c;
            while ((c = reader.Read()) != -1)
            {
                if (c == '\n') line++;
                if (char.IsWhiteSpace((char)c)) continue;
            }
        }

        private void ThrowException(string msg, int line) => throw new VMException($"{msg} (block {blockIndex} line {line})");

        private Token ReadNextToken()
        {
            ReadWhitespace();
            int startLine = line;
            int nextChar = reader.Read();
            if (nextChar == -1) return null;
            if (nextChar == CHAR_STR) return new Token(TokenType.String, ReadString(nextChar), blockIndex, line);
            string word = ReadWord(nextChar);
            Token tkn = null;
            if (TryReadNumber(word, out tkn)) return tkn;
            return ProcessWord(word);
        }

        private Token ProcessWord(string word)
        {
            if (word.Length == 1)
            {
                if (word[0] == LIST_START) return new Token(TokenType.OpenBrace, word, blockIndex, line);
                else if (word[0] == LIST_END) return new Token(TokenType.OpenBrace, word, blockIndex, line);
            }
            else if (word.Length > 1 && word.StartsWith('&')) //Variable refs
            {
                return new Token(TokenType.VariableRef, word.Substring(1), blockIndex, line);
            }
            return null;
        }

        private string ReadString(int closeChar)
        {
            sb.Clear();
            int c;
            bool ended = false;
            int startLine = line;

            while ((c = reader.Read()) != -1)
            {
                if (c == '\n') line++;
                if (c == closeChar) { ended = true; break; }
                if (c == ESCAPE)
                {
                    int esc = reader.Read();
                    if (esc == -1) ThrowException("Unterminated string literal", startLine);
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\'': sb.Append('\''); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case '\\': sb.Append("\\"); break;
                        default: ThrowException("Invalid escape sequence: " + (char)c, line); break;
                    }
                }
                else
                {
                    sb.Append((char)c);
                }
            }

            if (!ended) ThrowException("Unterminated string literal", startLine);
            return sb.ToString();
        }

        private string ReadWord(int startChar)
        {
            sb.Clear();
            sb.Append((char)startChar);
            int c;
            while ((c = reader.Read()) != -1)
            {
                if (char.IsWhiteSpace((char)c)) break;
                sb.Append((char)c);
            }
            return sb.ToString();
        }

        private bool TryReadNumber(string word, out Token token)
        {
            if (char.IsDigit(word[0]) || word[0] == '-')
            {
                bool negative = word[0] == '-';
                string numStr = negative ? word.Substring(1) : word;
                try
                {
                    if (numStr.StartsWith("0x"))
                    {
                        numStr = numStr.Substring(16);
                        double num = (double)(Convert.ToInt64(numStr, 2) & Constants.DoubleMantissaMask);
                        if (negative) num *= -1;
                        token = new Token(TokenType.Number, num, blockIndex, line);
                        return true;
                    }
                    else if (numStr.StartsWith("0b"))
                    {
                        numStr = numStr.Substring(2);
                        double num = (double)(Convert.ToInt64(numStr, 2) & Constants.DoubleMantissaMask);
                        if (negative) num *= -1;
                        token = new Token(TokenType.Number, num, blockIndex, line);
                        return true;
                    }
                    else if (double.TryParse(numStr, out double num))
                    {
                        if (negative) num *= -1;
                        token = new Token(TokenType.Number, num, blockIndex, line);
                        return true;
                    }
                }
                catch (Exception e)
                {
                    if (e is not FormatException or OverflowException or ArgumentOutOfRangeException) throw;
                }

            }
            token = null;
            return false;
        }




    }
}
