using System;
using System.Collections.Generic;
using System.Text;

namespace Freznel.FzAdditions.VM
{
    public class VMException : Exception
    {
        public VMObject Obj { get; private set; }
        public VMException(string message) : base(message) { }
        public VMException(string message, VMObject obj) : base(message)
        {
            Obj = obj;
        }
    }
}
