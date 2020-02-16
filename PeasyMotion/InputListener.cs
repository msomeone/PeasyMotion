using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
//using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;

namespace PeasyMotion
{
    class InputListener : IOleCommandTarget
    {
        readonly IVsTextView vsTextView;
        ITextView textView;
        IOleCommandTarget nextCommandHandler;

        public event KeyPressEventHandler KeyPressed;

        /// <summary>
        /// Add this filter to the chain of Command Filters
        /// </summary>
        internal InputListener(IVsTextView vsTextView_, ITextView textView_)
        {
            vsTextView = vsTextView_;
            textView = textView_;
        }

        public void AddFilter()
        {
            vsTextView.AddCommandFilter(this, out nextCommandHandler);

        }

        public void RemoveFilter()
        {
            vsTextView.RemoveCommandFilter(this);
            nextCommandHandler = null;
        }
        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return nextCommandHandler.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText);
        }

        /// <summary>
        /// Get user input. 
        /// IOleCommandTarget.Exec() function
        /// </summary>
        public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            int hr = VSConstants.S_OK;

            if ((new[] {
                4, // tab
                7, // left arrow
                11, // up arrow
                9, // right arrow
                13, // down arrow
                103, // escape
            }).Contains((int)nCmdID))
            {
                // send '\0' so we can abort
                KeyPressed?.Invoke(this,new KeyPressEventArgs('\0'));
                return hr;
            }

            char typedChar;
            if (TryGetTypedChar(pguidCmdGroup, nCmdID, pvaIn, out typedChar))
            {
                KeyPressed?.Invoke(this, new KeyPressEventArgs(typedChar));
                return hr;
            }

            ThreadHelper.ThrowIfNotOnUIThread();
            hr = nextCommandHandler.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);

            return hr;
        }

        /// <summary>
        /// Try to get the keypress value. Returns 0 if attempt fails
        /// </summary>
        /// <param name="typedChar">Outputs the value of the typed char</param>
        /// <returns>Boolean reporting success or failure of operation</returns>
        bool TryGetTypedChar(Guid cmdGroup, uint nCmdID, IntPtr pvaIn, out char typedChar)
        {
            typedChar = char.MinValue;
            //Debug.WriteLine("InputListener.cs | TryGetTypedChar | nCmdId " + nCmdID);
            //Debug.WriteLine("InputListener.cs | TryGetTypedChar | pvaIn " + pvaIn);

            if (cmdGroup != VSConstants.VSStd2K || nCmdID != (uint)VSConstants.VSStd2KCmdID.TYPECHAR)
                return false;


            typedChar = (char)(ushort)Marshal.GetObjectForNativeVariant(pvaIn);
            return true;
        }
    }
}