using System;
using System.Collections.Generic;
using System.Text;

namespace LaserConvertProcess
{
    /// <summary>
    /// Represents a message generated during STEP processing.
    /// </summary>
    public record ProcessMessage(string Text, bool IsDebugOnly);

    public class StepReturn
    {
        public int ReturnCode { get; set; } = 0;
        public string SVGContents { get; set; } = "";
        
        /// <summary>
        /// All messages generated during processing.
        /// Contains both regular and debug-only messages.
        /// </summary>
        public List<ProcessMessage> Messages { get; } = new List<ProcessMessage>();
    }
}
