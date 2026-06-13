using System;
using System.Collections.Generic;
using System.Text;

namespace Freznel.FzAdditions.VM.Execution
{
    public class VirtualMachine
    {
        public int ExecutionBudget { get; set; }
        public bool Finished => _frames.Count == 0;

        private List<VMObject> _operands;
        private List<VMFrame> _frames;

        public VirtualMachine()
        {
            _operands = new List<VMObject>();
            _frames = new List<VMFrame>();
        }

        public void Run(int budget)
        {
            ExecutionBudget = budget;
            Run();
        }

        public void Run()
        {
            while (_frames.Count > 0 && ExecutionBudget > 0)
            {
                VMFrame topFrame = PeekFrame(0);
                if (topFrame.Finished) { PopFrame(); continue; }
                topFrame.Execute(this);
            }
        }

        //Operand Stack Methods
        public int OperandsCount() => _operands.Count;
        public void PushOperand(VMObject operand) { _operands.Add(operand); }
        public VMObject PopOperand()
        {
            if (_operands.Count == 0) return null;
            int idx = _operands.Count - 1;
            var last = _operands[idx];
            _operands.RemoveAt(idx);
            return last;
        }
        public VMObject PeekOperand(int topIdx)
        {
            if (_operands.Count < topIdx + 1) return null;
            int idx = _operands.Count - (topIdx + 1);
            return _operands[idx];
        }
        public bool PokeOperand(int topIdx, VMObject item)
        {
            if (_operands.Count < topIdx + 1) return false;
            int idx = _operands.Count - (topIdx + 1);
            _operands[idx] = item;
            return true;
        }

        //Frame Stack Methods
        public int FramesCount() => _frames.Count;
        public void PushFrame(VMFrame operand) { _frames.Add(operand); }
        public VMFrame PopFrame()
        {
            if (_frames.Count == 0) return null;
            int idx = _frames.Count - 1;
            var last = _frames[idx];
            _frames.RemoveAt(idx);
            return last;
        }
        public VMFrame PeekFrame(int topIdx)
        {
            if (_frames.Count < topIdx + 1) return null;
            int idx = _frames.Count - (topIdx + 1);
            return _frames[idx];
        }
        public bool PokeFrame(int topIdx, VMFrame item)
        {
            if (_frames.Count < topIdx + 1) return false;
            int idx = _frames.Count - (topIdx + 1);
            _frames[idx] = item;
            return true;
        }





    }
}
