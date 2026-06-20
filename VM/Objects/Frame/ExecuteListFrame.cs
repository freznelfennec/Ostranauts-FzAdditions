using Freznel.FzAdditions.VM.Execution;
using Freznel.FzAdditions.VM.Interface;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Text;

namespace Freznel.FzAdditions.VM.Objects.Frame
{
    [ProtoContract]
    public class ExecuteListFrame : VMFrame, IReadableFrame
    {
        [ProtoMember(1)]
        private List<VMObject> _contents;
        [ProtoMember(2)]
        private int _next;
        [ProtoMember(3)]
        private int _debugTag;

        public override bool Finished => _next >= (_contents?.Count ?? 0);

        public int Length => _contents?.Count ?? 0;

        public int Position => _next;
        public int DebugTag => _debugTag;

        public ExecuteListFrame() { }

        public ExecuteListFrame(List<VMObject> contents, int next)
        {
            _contents = contents;
            _next = next;
        }

        public ExecuteListFrame(List<VMObject> contents)
        {
            _contents = contents;
            _next = 0;
        }

        public override int CollectSize(int limit)
        {
            int size = 0;
            foreach (VMObject item in _contents)
            {
                size += item.CollectSize(limit - size);
                if (size > limit) break;
            }
            return size;
        }

        public override void Execute(VirtualMachine vm)
        {
            while (!Finished && vm.PeekFrame(0) == this && vm.ExecutionBudget > 0)
            {
                VMObject nextObj = _contents[_next++];
                if (!(nextObj is IEvaluatable eval)) throw new VMException($"Attempted to evaluate {nextObj.GetType()} '{nextObj}'");
                eval.Evaluate(vm);
                vm.ExecutionBudget--;
            }
        }

        public VMObject Peek(int index = 0)
        {
            if (_contents == null) return null;
            return _contents[_next + index];
        }

        public VMObject Next() => _contents[_next++];
    }
}
