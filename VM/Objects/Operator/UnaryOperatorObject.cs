using Freznel.FzAdditions.VM.Enum;
using Freznel.FzAdditions.VM.Execution;
using Freznel.FzAdditions.VM.Interface;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Text;

namespace Freznel.FzAdditions.VM.Objects.Operator
{
    [ProtoContract]
    public class UnaryOperatorObject : VMObject, IEvaluatable
    {
        [ProtoMember(1)]
        private UnaryOperator _operator; //Write-only
        public UnaryOperator Operator { get; }

        public UnaryOperatorObject(UnaryOperator @operator)
        {
            Operator = @operator;
        }

        public UnaryOperatorObject() { }

        public override VMObject Clone() => this;

        public override int CollectSize(int limit) => 8;

        public override string ToString() => "Operator: " + _operator.ToString();

        public void Evaluate(VirtualMachine vm)
        {
            if (vm.OperandsCount() < 2) throw new VMException($"{_operator} operator expected at least 1 operand");
            VMObject a = vm.PopOperand();
            vm.PushOperand(VMOperatorUtil.Operate(_operator, a));
        }
    }
}
