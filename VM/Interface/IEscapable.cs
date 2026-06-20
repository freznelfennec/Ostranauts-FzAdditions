using Freznel.FzAdditions.VM.Execution;
using System;
using System.Collections.Generic;
using System.Text;

namespace Freznel.FzAdditions.VM.Interface
{
    public interface IEscapable
    {
        public void Escape(VirtualMachine vm);
    }
}
