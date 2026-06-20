using Freznel.FzAdditions.VM.Objects;
using Freznel.FzAdditions.VM.Objects.Operator;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Text;

namespace Freznel.FzAdditions.VM
{
    [ProtoContract]
    [ProtoInclude(32, typeof(NumberObject))]
    [ProtoInclude(33, typeof(UnaryOperatorObject))]
    [ProtoInclude(34, typeof(BinaryOperatorObject))]
    [ProtoInclude(35, typeof(MetaOperatorObject))]
    public abstract class VMObject
    {
        [ProtoMember(1)]
        public int DebugTag { get; set; }

        public abstract int CollectSize(int limit);

        public abstract VMObject Clone();

        public abstract override string ToString();
    }
}
