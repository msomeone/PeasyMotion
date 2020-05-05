using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using System.Runtime.InteropServices;

namespace PeasyMotion
{
    // based on code from VsVim.
    /* VsVim
    Copyright 2012 Jared Parsons

    Licensed under the Apache License, Version 2.0 (the "License");
    you may not use this file except in compliance with the License.
    You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0


    Unless required by applicable law or agreed to in writing, software
    distributed under the License is distributed on an "AS IS" BASIS,
    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
    See the License for the specific language governing permissions and
    limitations under the License.
    */

    public static class Extensions
    {
        // i wish we could simply do th:is:
        //VsShellUtilities.GetWindowObject(wf).Caption = some caption;
        // but its impossible. Exception is thrown  - invalid operation, bla bla.

        public static void SetDocumentWindowFrameCaptionWithLabel(this IVsWindowFrame wf, 
                    string vanillaTabCaption, string jumpLabel)
        {
            //TODO: (check for length (what if vanilla caption is shorter than label & decor ???!!!)
            var newCaption = PeasyMotionEdAdornment.getDocumentTabCaptionWithLabel(vanillaTabCaption, jumpLabel);
            wf.SetProperty((int)VsFramePropID.OverrideCaption, newCaption);
            //Debug.WriteLine($"WindowFrame oldCaption={vanillaTabCaption} => newCaption={newCaption}");
        }

        public static void RemoveJumpLabelFromDocumentWindowFrameCaption(this IVsWindowFrame wf, string vanillaTabCaption)
        {
            wf.SetProperty((int)VsFramePropID.OverrideCaption, null);
            //wf.SetProperty((int)VsFramePropID.Caption, vanillaTabCaption); //TODO: checl if we really need this
        }

        public static Result<IVsWindowFrame> GetWindowFrame(this IVsTextView textView)
        {
            var textViewEx = textView as IVsTextViewEx;
            if (textViewEx == null)
            {
                return Result.Error;
            }

            return textViewEx.GetWindowFrame();
        }

        public static Result<IVsWindowFrame> GetWindowFrame(this IVsTextViewEx textViewEx)
        {
            if (!ErrorHandler.Succeeded(textViewEx.GetWindowFrame(out object frame)))
            {
                return Result.Error;
            }

            var vsWindowFrame = frame as IVsWindowFrame;
            if (vsWindowFrame == null)
            {
                return Result.Error;
            }

            return Result.CreateSuccess(vsWindowFrame);
        }

        public static Result<IVsCodeWindow> GetCodeWindow(this IVsWindowFrame vsWindowFrame)
        {
            var iid = typeof(IVsCodeWindow).GUID;
            var ptr = IntPtr.Zero;
            try
            {
                var hr = vsWindowFrame.QueryViewInterface(ref iid, out ptr);
                if (ErrorHandler.Failed(hr))
                {
                    return Result.CreateError(hr);
                }

                return Result.CreateSuccess((IVsCodeWindow)Marshal.GetObjectForIUnknown(ptr));
            }
            catch (Exception e)
            {
                // Venus will throw when querying for the code window
                return Result.CreateError(e);
            }
            finally
            {
                if (ptr != IntPtr.Zero)
                {
                    Marshal.Release(ptr);
                }
            }
        }

        public static IVsTextView GetPrimaryTextView(this IVsWindowFrame windowFrame)
        {
            /* 
            object docView;
            int hresult = windowFrame.GetProperty((int)__VSFPROPID.VSFPROPID_DocView, out docView);
 
            if (ErrorHandler.Failed(hresult)) {
                return null;
            }
 
            IVsTextView viewAdapter = docView as IVsTextView;
            if (viewAdapter != null) {
                return viewAdapter;
            }
 
            IVsCodeWindow codeWindow = docView as IVsCodeWindow;
            if (codeWindow != null) {
                IVsTextView codeView;
                if (ErrorHandler.Succeeded(codeWindow.GetPrimaryView(out codeView)) && codeView != null) {
                    return codeView;
                }
            }
 
            return null;
            */
            return VsShellUtilities.GetTextView(windowFrame);
        }

        public static Result<IVsTextView> GetPrimaryView(this IVsCodeWindow vsCodeWindow)
        {
            var hr = vsCodeWindow.GetPrimaryView(out IVsTextView vsTextView);
            if (ErrorHandler.Failed(hr))
            {
                return Result.CreateError(hr);
            }

            return Result.CreateSuccessNonNull(vsTextView);
        }

        public static Result<IWpfTextView> GetPrimaryTextView(this IVsCodeWindow codeWindow, IVsEditorAdaptersFactoryService factoryService)
        {
            var result = GetPrimaryView(codeWindow);
            if (result.IsError)
            {
                return Result.CreateError(result.HResult);
            }

            var textView = factoryService.GetWpfTextViewNoThrow(result.Value);
            return Result.CreateSuccessNonNull(textView);
        }

        public static Result<List<IVsWindowFrame>> GetDocumentWindowFrames(this IVsUIShell vsShell)
        {
            var hr = vsShell.GetDocumentWindowEnum(out IEnumWindowFrames enumFrames);
            return ErrorHandler.Failed(hr) ? Result.CreateError(hr) : enumFrames.GetContents();
        }

        public static Result<List<IVsWindowFrame>> GetDocumentWindowFrames(this IVsUIShell4 vsShell, __WindowFrameTypeFlags flags)
        {
            var hr = vsShell.GetWindowEnum((uint)flags, out IEnumWindowFrames enumFrames);
            return ErrorHandler.Failed(hr) ? Result.CreateError(hr) : enumFrames.GetContents();
        }

        public static Result<List<IVsWindowFrame>> GetContents(this IEnumWindowFrames enumFrames)
        {
            var list = new List<IVsWindowFrame>();
            var array = new IVsWindowFrame[16];
            while (true)
            {
                var hr = enumFrames.Next((uint)array.Length, array, out uint num);
                if (ErrorHandler.Failed(hr))
                {
                    return Result.CreateError(hr);
                }

                if (0 == num)
                {
                    return list;
                }

                for (var i = 0; i < num; i++)
                {
                    list.Add(array[i]);
                }
            }
        }

        public static IWpfTextView GetWpfTextViewNoThrow(this IVsEditorAdaptersFactoryService editorAdapter, IVsTextView vsTextView)
        {
            try {
                return editorAdapter.GetWpfTextView(vsTextView);
            } catch {
                return null;
            }
        }
    }
}
