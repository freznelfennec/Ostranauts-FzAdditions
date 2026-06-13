using System;
using System.Collections.Generic;
using System.Text;

namespace Freznel.FzAdditions.VM
{
    public class VMException : Exception
    {
        public VMException(string message) : base(message) { }
    }
}
