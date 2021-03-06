﻿using System;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace PeasyMotion
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the
    /// IVsPackage interface and uses the registration attributes defined in the framework to
    /// register itself and its components with the shell. These attributes tell the pkgdef creation
    /// utility what data to put into .pkgdef file.
    /// </para>
    /// <para>
    /// To get loaded into VS, the package must be referred by &lt;Asset Type="Microsoft.VisualStudio.VsPackage" ...&gt; in .vsixmanifest file.
    /// </para>
    /// </remarks>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
    [Guid(PeasyMotionPackage.PackageGuidString)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideOptionPage(typeof(DialogPageProvider.General), "PeasyMotion options", "General", 101, 106, true)]
    public sealed class PeasyMotionPackage : AsyncPackage
    {
        public const bool MeasureExecTime = false;
        /// <summary>
        /// PeasyMotionPackage GUID string.
        /// </summary>
        public const string PackageGuidString = "4fa9da7b-5f7c-4d43-8a46-9326a6eb6eab";

        #region Package Members

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token to monitor for initialization cancellation, which can occur when VS is shutting down.</param>
        /// <param name="progress">A provider for progress updates.</param>
        /// <returns>A task representing the async work of package initialization, or an already completed task if there is none. Do not return null from this method.</returns>
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            // When initialized asynchronously, the current thread may be a background thread at this point.
            // Do any initialization that requires the UI thread after switching to the UI thread.
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            await PeasyMotionActivate.InitializeAsync(this).ConfigureAwait(true);

            await base.InitializeAsync(cancellationToken, progress);
        }

            //TODO: test editor command & input field
            //TODO: how to DocTabNavJump from non-text editors ?!?? how to listen to kb globally?
            //TODO: how to cancel jump if text view was switched? | HANDLE WpfTextView change / focus change!
            //TODO: hot keys fucked up!
            //TODO: MAYBE PROVIDE AN OPTION TO CONFIGURE LABEL TEXT DECORATION???  for getDocumentTabCaptionWithLabel

            //TODO: HOW TO SETUP AN INDEX?!?!?!?!? (find a way to measure distance between current document and target tab)
            ///      v-----^ <--- related issues
            //TODO: separate property for Tab Label assignment Algo selection???

        #endregion
    }
}
