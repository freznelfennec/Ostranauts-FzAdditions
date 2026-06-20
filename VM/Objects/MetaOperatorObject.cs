using Freznel.FzAdditions.VM.Annotation;
using Freznel.FzAdditions.VM.Execution;
using Freznel.FzAdditions.VM.Interface;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Text;

namespace Freznel.FzAdditions.VM.Objects
{
    public enum MetaOperator
    {
        Invalid = 0,
        [Word("\\")]
        EscapeNext = 1,
        [Word("[")]
        StartList = 2,
        [Word("]")]
        EndList = 3,
        [Word("$")]
        Call = 4,
        [Word("$/cc")]
        CallCC = 5,
    }

    [ProtoContract]
    public class MetaOperatorObject : VMObject, IEvaluatable
    {
        [ProtoMember(1)]
        private MetaOperator _operator;

        public MetaOperator Operator => _operator;

        public MetaOperatorObject(MetaOperator @operator)
        {
            _operator = @operator;
        }

        public MetaOperatorObject() { }

        public void Evaluate(VirtualMachine vm)
        {
            VMObject a;
            switch (_operator)
            {
                case MetaOperator.EscapeNext:
                    VMFrame frame = vm.PeekFrame();
                    if (frame == null || !(frame is IReadableFrame readableFrame)) throw new VMException("Escape operator may not be executed from a non-readable frame", this);
                    if (frame.Finished) throw new VMException("Attempted to escape end of frame", this);
                    a = readableFrame.Next();
                    if (!(a is IEscapable escapable)) throw new VMException($"Attempted to escape non-escapable object '{a}'", this);
                    escapable.Escape(vm);
                    break;
                case MetaOperator.Call:
                    a = vm.PopOperand();
                    if (!(a is ICallable callable)) throw new VMException($"Attempted to call non-callable object '{a}'", this);
                    callable.Call(vm);
                    break;




                default:
                    throw new VMException($"Attempted to evaluate invalid meta operator ${_operator}", this);
            }
        }

        public override int CollectSize(int limit) => 4;

        public override VMObject Clone() => this;

        public override string ToString() => _operator.ToString();
    }
}
