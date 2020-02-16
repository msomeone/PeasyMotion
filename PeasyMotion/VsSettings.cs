// Based on EditorHostFactory from VsVim
// And VsSettings from VsTeXCommentsExtension

/* VsSettings 
The MIT License (MIT)

Copyright (c) 2016 Hubert Kindermann

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

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

using Microsoft.VisualStudio.Threading;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.ComponentModel.Composition.Primitives;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.Win32;
using System;
using System.Drawing;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Media;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;

using System.Threading;
using System.Windows.Threading;
using wpf = System.Windows.Media;

namespace PeasyMotion
{
    public sealed partial class EditorHostFactory
    {
        /// <summary>
        /// Beginning in 15.0 the editor took a dependency on JoinableTaskContext.  Need to provide that 
        /// export here. 
        /// </summary>
        private sealed class JoinableTaskContextExportProvider : ExportProvider
        {
            internal static string TypeFullName => typeof(JoinableTaskContext).FullName;
            private readonly Export _export;
            private readonly JoinableTaskContext _context;

            internal JoinableTaskContextExportProvider()
            {
                _export = new Export(TypeFullName, GetValue);
                _context =  Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskContext;
            }

            protected override IEnumerable<Export> GetExportsCore(ImportDefinition definition, AtomicComposition atomicComposition)
            {
                if (definition.ContractName == TypeFullName)
                { 
                    yield return _export;
                }
            }

            private object GetValue() => _context;
        }
    }

    public sealed class VsSettings : IDisposable, INotifyPropertyChanged
    {
        private static readonly SolidColorBrush DefaultForegroundBrush = new SolidColorBrush(wpf.Colors.Sienna);
        private static readonly SolidColorBrush DefaultBackgroundBrush = new SolidColorBrush(wpf.Colors.GreenYellow);
        private static readonly Dictionary<IWpfTextView, VsSettings> Instances = new Dictionary<IWpfTextView, VsSettings>();

        public static bool IsInitialized { get; private set; }
        private static IEditorFormatMapService editorFormatMapService;
        private static IServiceProvider serviceProvider;

        private readonly IWpfTextView textView;
        private readonly IEditorFormatMap editorFormatMap;

        public event PropertyChangedEventHandler PropertyChanged;

        private SolidColorBrush jumpLabelFirstMotionColorFg;
        public SolidColorBrush JumpLabelFirstMotionForegroundColor
        {
            get { return jumpLabelFirstMotionColorFg; }
            private set { 
                bool notify = JumpLabelFirstMotionForegroundColor == null || value == null || value.Color != JumpLabelFirstMotionForegroundColor.Color;
                jumpLabelFirstMotionColorFg = value; 
                jumpLabelFirstMotionColorFg.Freeze();
                Trace.Debug($"VsSettings.Property Set FG={JumpLabelFirstMotionForegroundColor.Color} notify={notify}");
                if (notify) OnPropertyChanged(nameof(JumpLabelFirstMotionForegroundColor)); 
            }
        }
        private SolidColorBrush jumpLabelFirstMotionColorBg;
        public SolidColorBrush JumpLabelFirstMotionBackgroundColor
        {
            get { return jumpLabelFirstMotionColorBg; }
            private set { 
                bool notify = JumpLabelFirstMotionBackgroundColor == null || value == null || value.Color != JumpLabelFirstMotionBackgroundColor.Color;
                jumpLabelFirstMotionColorBg = value;
                jumpLabelFirstMotionColorBg.Freeze();
                Trace.Debug($"VsSettings.Property Set BG={JumpLabelFirstMotionBackgroundColor.Color} notify={notify}");
                if (notify) OnPropertyChanged(nameof(JumpLabelFirstMotionBackgroundColor)); 
            }
        }

        private SolidColorBrush jumpLabelFinalMotionColorFg;
        public SolidColorBrush JumpLabelFinalMotionForegroundColor
        {
            get { return jumpLabelFinalMotionColorFg; }
            private set { 
                bool notify = JumpLabelFinalMotionForegroundColor == null || value == null || value.Color != JumpLabelFinalMotionForegroundColor.Color;
                jumpLabelFinalMotionColorFg = value; 
                jumpLabelFinalMotionColorFg.Freeze();
                Trace.Debug($"VsSettings.Property Set FG={JumpLabelFinalMotionForegroundColor.Color} notify={notify}");
                if (notify) OnPropertyChanged(nameof(JumpLabelFinalMotionForegroundColor)); 
            }
        }
        private SolidColorBrush jumpLabelFinalMotionColorBg;
        public SolidColorBrush JumpLabelFinalMotionBackgroundColor
        {
            get { return jumpLabelFinalMotionColorBg; }
            private set { 
                bool notify = JumpLabelFinalMotionBackgroundColor == null || value == null || value.Color != JumpLabelFinalMotionBackgroundColor.Color;
                jumpLabelFinalMotionColorBg = value;
                jumpLabelFinalMotionColorBg.Freeze();
                Trace.Debug($"VsSettings.Property Set BG={JumpLabelFinalMotionBackgroundColor.Color} notify={notify}");
                if (notify) OnPropertyChanged(nameof(JumpLabelFinalMotionBackgroundColor)); 
            }
        }

        // https://stackoverflow.com/questions/10283206/setting-getting-the-class-properties-by-string-name
        public object this[string propertyName] 
        {
            get{
                // probably faster without reflection:
                // like:  return Properties.Settings.Default.PropertyValues[propertyName] 
                // instead of the following
                Type myType = typeof(VsSettings);                   
                PropertyInfo myPropInfo = myType.GetProperty(propertyName);
                return myPropInfo.GetValue(this, null);
            } 
            set{
                Type myType = typeof(VsSettings);                   
                PropertyInfo myPropInfo = myType.GetProperty(propertyName);
                myPropInfo.SetValue(this, value, null);
            }
        }

        public static void NotiifyInstancesFmtPropertyChanged(string propertyName, System.Windows.Media.Color value) 
        {
            Trace.Debug($"GeneralOptions.SetColor -> VsSettings.NotiifyInstancesFmtPropertyChanged color={value}");
            lock (Instances)
            {
                var sb = new SolidColorBrush(value); 
                sb.Freeze();
                foreach (var i in Instances) { 
                    i.Value[propertyName] = sb; 
                }
            }
        }

        public static void NotifyInstancesPropertyColorSourceChanged(string propertyName, string newColorSource)
        {
            Trace.Debug($"GeneralOptions.SetColor -> VsSettings.NotifyJumpLabelColorSourceChanged newColorSource={newColorSource}");
            lock (Instances)
            {
                foreach (var i in Instances) { 
                    i.Value.FetchColor(propertyName, newColorSource);
                }
            }
        }


        public static VsSettings GetOrCreate(IWpfTextView textView)
        {
            lock (Instances)
            {
                if (!Instances.TryGetValue(textView, out VsSettings settings))
                {
                    settings = new VsSettings(textView);
                    Instances.Add(textView, settings);
                }
                return settings;
            }
        }

        public static void Initialize(IServiceProvider serviceProviderx, IEditorFormatMapService editorFormatMapService)
        {
            if (IsInitialized)
                throw new InvalidOperationException($"{nameof(VsSettings)} class is already initialized.");

            IsInitialized = true;

            DefaultForegroundBrush.Freeze();
            DefaultBackgroundBrush.Freeze();

            VsSettings.editorFormatMapService = editorFormatMapService;
            VsSettings.serviceProvider = serviceProviderx;
            GeneralOptions.Instance.LoadColors(VsSettings.serviceProvider);
        }

        public VsSettings(IWpfTextView textView)
        {
            Debug.Assert(IsInitialized);

            this.textView = textView;
            editorFormatMap = editorFormatMapService.GetEditorFormatMap(textView);
            ReloadColors();

            editorFormatMap.FormatMappingChanged += OnFormatItemsChanged;
        }

        private void ReloadColors()
        {
            //ighContrastSelectionFg = GetBrush(editorFormatMap, "Selected Text in High Contrast", BrushType.Foreground, textView);
            //ighContrastSelectionBg = GetBrush(editorFormatMap, "Selected Text in High Contrast", BrushType.Background, textView);
            FetchColor(nameof(JumpLabelFirstMotionForegroundColor), GeneralOptions.Instance.JumplabelFirstMotionColorSource);
            FetchColor(nameof(JumpLabelFirstMotionBackgroundColor), GeneralOptions.Instance.JumplabelFirstMotionColorSource);
            Trace.Debug($"JUMP LABEL FG={JumpLabelFirstMotionForegroundColor.Color} BG={JumpLabelFirstMotionBackgroundColor.Color}");
            FetchColor(nameof(JumpLabelFinalMotionForegroundColor), GeneralOptions.Instance.JumplabelFinalMotionColorSource);
            FetchColor(nameof(JumpLabelFinalMotionBackgroundColor), GeneralOptions.Instance.JumplabelFinalMotionColorSource);
            Trace.Debug($"JUMP LABEL FG={JumpLabelFinalMotionForegroundColor.Color} BG={JumpLabelFinalMotionBackgroundColor.Color}");
        }

        private void FetchColor(string colorPropertyName, string sourceName)
        {
            BrushType brushType;
            if (colorPropertyName.Contains("Background")) {
                brushType = BrushType.Background;
            } else if (colorPropertyName.Contains("Foreground")) {
                brushType = BrushType.Foreground;
            } else {
                var msg = $"PeasyMotion: VsSettings - unable to deduce brush type from color property name={colorPropertyName}!";
                Debug.Fail(msg);
                throw new Exception(msg);
            }
            Trace.Debug($"VsSettings.FetchColor settings COLOR={colorPropertyName} BRUSH_TYPE={brushType}, Source={sourceName}");
            this[colorPropertyName] = GetBrush(editorFormatMap, sourceName, brushType, textView);
        }

        private void OnFormatItemsChanged(object sender, FormatItemsEventArgs args)
        {
            if (args.ChangedItems.Any(i => i == JumpLabelFirstMotionFormatDef.FMT_NAME))
            {
                ReloadColors();
                Trace.Debug("VsSettings.OnFormatItemsChanged, FG={JumpLabelFirstMotionForegroundColor.Color} BG={JumpLabelFirstMotionBackgroundColor}");
                Trace.Debug("VsSettings.OnFormatItemsChanged Setting GeneralOptions FG & BG");
                GeneralOptions.Instance.JumpLabelFirstMotionForegroundColor = GeneralOptions.toDrawingColor(this.JumpLabelFirstMotionForegroundColor.Color);
                GeneralOptions.Instance.JumpLabelFirstMotionBackgroundColor = GeneralOptions.toDrawingColor(this.JumpLabelFirstMotionBackgroundColor.Color);
            }
            else if (args.ChangedItems.Any(i => i == JumpLabelFinalMotionFormatDef.FMT_NAME))
            {
                ReloadColors();
                Trace.Debug("VsSettings.OnFormatItemsChanged, FG={JumpLabelFinalMotionForegroundColor.Color} BG={JumpLabelFinalMotionBackgroundColor}");
                Trace.Debug("VsSettings.OnFormatItemsChanged Setting GeneralOptions FG & BG");
                GeneralOptions.Instance.JumpLabelFinalMotionForegroundColor = GeneralOptions.toDrawingColor(this.JumpLabelFinalMotionForegroundColor.Color);
                GeneralOptions.Instance.JumpLabelFinalMotionBackgroundColor = GeneralOptions.toDrawingColor(this.JumpLabelFinalMotionBackgroundColor.Color);
            }
        }

        private static SolidColorBrush GetBrush(IEditorFormatMap editorFormatMap, string propertyName, BrushType type, IWpfTextView textView)
        {
            var props = editorFormatMap.GetProperties(propertyName);
            var typeText = type.ToString();

            object value = null;
            if (props.Contains(typeText))
            {
                value = props[typeText];
            }
            else
            {
                typeText += "Color";
                if (props.Contains(typeText))
                {
                    value = props[typeText];
                    if (value is wpf.Color)
                    {
                        var color = (wpf.Color)value;
                        var cb = new SolidColorBrush(color);
                        cb.Freeze();
                        value = cb;
                    }
                }
                else
                {
                    //Background is often not found in editorFormatMap. Don't know why :(
                    if (type == BrushType.Background)
                    {
                        value = textView.Background;
                    }
                }
            }

            return (value as SolidColorBrush) ?? (type == BrushType.Background ? DefaultBackgroundBrush : DefaultForegroundBrush);
        }

        private void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public void Dispose()
        {
            editorFormatMap.FormatMappingChanged -= OnFormatItemsChanged;
        }

        private enum BrushType
        {
            Foreground,
            Background
        }
    }

    public sealed partial class EditorHostFactory
    {
        internal static Version VisualStudioVersion => new Version(16, 0, 0, 0);
        internal static Version VisualStudioThreadingVersion => new Version(16, 0, 0, 0);

        internal static string[] CoreEditorComponents =
            new[]
            {
                "Microsoft.VisualStudio.Platform.VSEditor.dll",
                "Microsoft.VisualStudio.Text.Internal.dll",
                "Microsoft.VisualStudio.Text.Logic.dll",
                "Microsoft.VisualStudio.Text.UI.dll",
                "Microsoft.VisualStudio.Text.UI.Wpf.dll",
                "Microsoft.VisualStudio.Language.dll",
            };

        private readonly List<ComposablePartCatalog> _composablePartCatalogList = new List<ComposablePartCatalog>();
        private readonly List<ExportProvider> _exportProviderList = new List<ExportProvider>();

        public EditorHostFactory()
        {
            BuildCatalog();
        }

        public void Add(ComposablePartCatalog composablePartCatalog)
        {
            _composablePartCatalogList.Add(composablePartCatalog);
        }

        public void Add(ExportProvider exportProvider)
        {
            _exportProviderList.Add(exportProvider);
        }

        public CompositionContainer CreateCompositionContainer()
        {
            var catalog = new AggregateCatalog(_composablePartCatalogList.ToArray());
            return new CompositionContainer(catalog, _exportProviderList.ToArray());
        }

        private void BuildCatalog()
        {
            var editorAssemblyVersion = new Version(VisualStudioVersion.Major, 0, 0, 0);
            AppendEditorAssemblies(editorAssemblyVersion);
            AppendEditorAssembly("Microsoft.VisualStudio.Threading", VisualStudioThreadingVersion);
            _exportProviderList.Add(new JoinableTaskContextExportProvider());
            _composablePartCatalogList.Add(new AssemblyCatalog(typeof(EditorHostFactory).Assembly));
        }

        private void AppendEditorAssemblies(Version editorAssemblyVersion)
        {
            foreach (var name in CoreEditorComponents)
            {
                var simpleName = Path.GetFileNameWithoutExtension(name);
                AppendEditorAssembly(simpleName, editorAssemblyVersion);
            }
        }

        private void AppendEditorAssembly(string name, Version version)
        {
            var assembly = GetEditorAssembly(name, version);
            _composablePartCatalogList.Add(new AssemblyCatalog(assembly));
        }

        private static Assembly GetEditorAssembly(string assemblyName, Version version)
        {
            //var qualifiedName = $"{assemblyName}, Version={version}, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a, processorArchitecture=MSIL";
            var qualifiedName = $"{assemblyName}, Version={version}, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a";
            //var A = Assembly.GetExecutingAssembly().GetReferencedAssemblies();
            //foreach(var f in A) { Trace.Debug(f); }
            //foreach(var f in Assembly.GetExecutingAssembly().GetFiles()) { Trace.Debug(f.Name); }
            return Assembly.Load(qualifiedName);
        }
    }
}
/*
mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089
Microsoft.VisualStudio.OLE.Interop, Version=7.1.40304.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089
Microsoft.VisualStudio.TextManager.Interop, Version=7.1.40304.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Microsoft.VisualStudio.Text.UI, Version=16.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
PresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35
System.Xaml, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089
System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089
WindowsBase, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35
PresentationCore, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35
EnvDTE, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
System.Core, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089
System.ComponentModel.Composition, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089
Microsoft.VisualStudio.Shell.15.0, Version=16.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Microsoft.VisualStudio.Editor, Version=16.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Microsoft.VisualStudio.Text.Logic, Version=16.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Microsoft.VisualStudio.Text.UI.Wpf, Version=16.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Microsoft.VisualStudio.Shell.Framework, Version=16.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Microsoft.VisualStudio.Threading, Version=16.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Microsoft.VisualStudio.ComponentModelHost, Version=16.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Microsoft.VisualStudio.Text.Data, Version=16.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Microsoft.VisualStudio.CoreUtility, Version=16.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Microsoft.VisualStudio.Shell.Interop.8.0, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Microsoft.CSharp, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
System.Design, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Microsoft.VisualStudio.Validation, Version=15.3.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
*/