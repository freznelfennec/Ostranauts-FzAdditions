using System;
using System.Collections.Generic;
using System.Text;

namespace Freznel.FzAdditions.VM.Interface
{
    public interface IReadableFrame //Used my meta operators
    {
        public int Length { get; } //Returns the number of VMObjects in the frame
        public int Position { get; } //Returns the position of the next VMObject to be evaluated (0-based)
        

        public VMObject Peek(int index = 0); //Return the VMObject at Position + index
        public VMObject Next(); //Advance Position by 1 and return the VMObject at the previous position

    }
}
