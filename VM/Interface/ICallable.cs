using Freznel.FzAdditions.VM.Execution;
using System;
using System.Collections.Generic;
using System.Text;

namespace Freznel.FzAdditions.VM.Interface
{
    public interface ICallable
    {
        public void Call(VirtualMachine vm);
    }
}
