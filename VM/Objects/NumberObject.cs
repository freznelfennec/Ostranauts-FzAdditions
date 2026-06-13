using Freznel.FzAdditions.VM.Execution;
using Freznel.FzAdditions.VM.Interface;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Text;

namespace Freznel.FzAdditions.VM.Objects
{
    [ProtoContract]
    public class NumberObject : VMObject, IEvaluatable
    {
        public double Value => _value;

        [ProtoMember(1)]
        private readonly double _value; //Write-only

        public NumberObject(double value)
        {
            _value = value;
        }

        public NumberObject() { }

        public override int CollectSize(int limit) => 8;

        public override VMObject Clone() => this;

        public override string ToString() => _value.ToString();

        public void Evaluate(VirtualMachine vm) => vm.PushOperand(this);
    }
}
