using Freznel.FzAdditions.VM.Execution;
using System;
using System.Collections.Generic;
using System.Text;

namespace Freznel.FzAdditions.VM.Interface
{
    public interface IEvaluatable
    {
        public void Evaluate(VirtualMachine vm);
    }
}
