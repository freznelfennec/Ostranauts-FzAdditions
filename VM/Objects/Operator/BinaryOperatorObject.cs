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
    public class BinaryOperatorObject : VMObject, IEvaluatable
    {
        [ProtoMember(1)]
        private BinaryOperator _operator; //Write-only
        public BinaryOperator Operator { get; }

        public BinaryOperatorObject(BinaryOperator @operator)
        {
            _operator = @operator;
        }

        public BinaryOperatorObject() { }

        public override VMObject Clone() => this;

        public override int CollectSize(int limit) => 8;

        public override string ToString() => "Operator: " + _operator.ToString();

        public void Evaluate(VirtualMachine vm)
        {
            if (vm.OperandsCount() < 2) throw new VMException($"{_operator} operator expected at least 2 operands");
            VMObject b = vm.PopOperand();
            VMObject a = vm.PopOperand();
            vm.PushOperand(VMOperatorUtil.Operate(_operator, a, b));
        }
    }
}
