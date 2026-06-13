using Freznel.FzAdditions.VM.Execution;
using Freznel.FzAdditions.VM.Objects.Frame;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Text;

namespace Freznel.FzAdditions.VM
{
    [ProtoContract]
    [ProtoInclude(32, typeof(ExecuteListFrame))]
    public abstract class VMFrame
    {
        public abstract bool Finished { get; }
        public abstract int CollectSize(int limit);
        public abstract void Execute(VirtualMachine vm);
    }
}
