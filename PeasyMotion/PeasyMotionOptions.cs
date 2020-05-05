//#define MEASUREEXECTIME
//#define DEBUG_COLOR_STYLE_OPTIONS

// Options based on VSSDK Options code + some stackoverflow recipes and few classes from VsVim :}
// original ColorKey, ColorInfo, LoadColor*, SaveColor from VsVim/Src/VsVimShared/Implementation/OptionPages/DefaultOptionPage.cs
// 
/* VsVim licence:
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

using System.Diagnostics;
using System.ComponentModel;
using Microsoft.VisualStudio.Shell;
using PeasyMotion.Options;
using System.Drawing;
using System.Collections.Generic;
using Microsoft.VisualStudio.ComponentModelHost;
using System;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.TextManager.Interop;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Collections;
using System.Windows.Media;
using System.Drawing.Design;
using System.Runtime.CompilerServices;

namespace PeasyMotion
{

public enum JumpLabelAssignmentAlgorithm
{
    CaretRelative,
    ViewportRelative
}

public class TextEditorFontsAndColorsItemsList 
{
    private static List<string> _colorableItemsCached = new List<string>();
    public static List<string> ColorableItemsCached
    {
        get {
            if (_colorableItemsCached.Count == 0) {
                obtainItems();
            }
            return _colorableItemsCached;
        }       
    }

    private static void obtainItems() {
        #if MEASUREEXECTIME
        var watch = System.Diagnostics.Stopwatch.StartNew();
        #endif
        ThreadHelper.ThrowIfNotOnUIThread();

         try {
            var dte = Package.GetGlobalService(typeof(SDTE)) as EnvDTE.DTE;
            var props = dte.Properties["FontsAndColors", "TextEditor"];
            var fac = (EnvDTE.FontsAndColorsItems)props.Item("FontsAndColorsItems").Object;
            var enumfac = fac.GetEnumerator();
            while (false != enumfac.MoveNext())
            {
                var i = enumfac.Current; var i2 = (EnvDTE.ColorableItems)i;
                _colorableItemsCached.Add(i2.Name);
            }
            //var colors = ConvertDTEColor(((EnvDTE.ColorableItems)fac.Item("Plain Text")).Foreground);
            //FontFamily = props.Item("FontFamily").Value.ToString(); //FontSize = (float)(short)props.Item("FontSize").Value; //FontBold = colors.Bold;
            //ForeColor = ColorTranslator.FromOle((int)colors.Foreground); //BackColor = ColorTranslator.FromOle((int)colors.Background);
            //colors = (EnvDTE.ColorableItems)fac.Item("Selected Text");
            //HighlightFontBold = colors.Bold; //HighlightForeColor = ColorTranslator.FromOle((int)colors.Foreground); //HighlightBackColor = ColorTranslator.FromOle((int)colors.Background);
        } catch (Exception ex) {
            Debug.WriteLine("Error loading text editor font and colors");
            Debug.WriteLine(ex.ToString());
        }
        #if MEASUREEXECTIME
        watch.Stop();
        Debug.WriteLine($"PeasyMotion TextEditorFontsAndColorsItemsList obtaining FontsAndColorsItems took {watch.ElapsedMilliseconds} ms");
        #endif
    }
}

// GitHub search helper (spent ~5 hours googling out this simple snippet T_T)
// provides ComboBox DropDown for string list Property converter DialogPage List<string> List<String> PorpertyGrid Options Dialog ProvideOptionPage
// TypeConverter ListBox blah blah any other search keywords?
public class TextEditorClassificationStringConverter : StringConverter
{
    public override Boolean GetStandardValuesSupported(ITypeDescriptorContext context) { return true; }
    public override Boolean GetStandardValuesExclusive(ITypeDescriptorContext context) { return true; }
    public override TypeConverter.StandardValuesCollection GetStandardValues(ITypeDescriptorContext context) {
        return new StandardValuesCollection(new List<string>(TextEditorFontsAndColorsItemsList.ColorableItemsCached));
    }
}

readonly struct ColorKey
{
    internal readonly string Name;
    internal readonly bool IsForeground;
    internal ColorKey(string name, bool isForeground) { Name = name; IsForeground = isForeground; }
    internal static ColorKey Foreground(string name) { return new ColorKey(name, isForeground: true); }
    internal static ColorKey Background(string name) { return new ColorKey(name, isForeground: false); }
}

sealed class ColorInfo
{
    internal readonly ColorKey ColorKey;
    internal readonly System.Drawing.Color OriginalColor;
    internal bool IsValid;
    internal System.Drawing.Color Color;

    internal ColorInfo(ColorKey colorKey, System.Drawing.Color color, bool isValid = true) { ColorKey = colorKey; OriginalColor = color; Color = color; IsValid = isValid; }
}

internal class GeneralOptions : BaseOptionModel<GeneralOptions>
{
    internal const string _PkgVersion = "1.4.56";
    public static string getCurrentVersion() { return _PkgVersion; }

    private static readonly ColorKey s_jumpLabelFirstMotionColorBg = ColorKey.Background(JumpLabelFirstMotionFormatDef.FMT_NAME);
    private static readonly ColorKey s_jumpLabelFirstMotionColorFg = ColorKey.Foreground(JumpLabelFirstMotionFormatDef.FMT_NAME);
    private static readonly ColorKey s_jumpLabelFinalMotionColorBg = ColorKey.Background(JumpLabelFinalMotionFormatDef.FMT_NAME);
    private static readonly ColorKey s_jumpLabelFinalMotionColorFg = ColorKey.Foreground(JumpLabelFinalMotionFormatDef.FMT_NAME);

    private static readonly ReadOnlyCollection<ColorKey> s_colorKeyList = new ReadOnlyCollection<ColorKey>(new[]
    {
        s_jumpLabelFirstMotionColorBg,
        s_jumpLabelFirstMotionColorFg,
        s_jumpLabelFinalMotionColorBg,
        s_jumpLabelFinalMotionColorFg,
    });

    private readonly Dictionary<ColorKey, ColorInfo> colorMap = new Dictionary<ColorKey, ColorInfo>();
    
    public GeneralOptions()
    {
        foreach (var colorKey in s_colorKeyList) {
            colorMap[colorKey] = new ColorInfo(colorKey, System.Drawing.Color.Black);
        }
    }

    private bool isJumpLabelFirstMotionColorSourceItsOwn() {
        return JumplabelFirstMotionColorSource == JumpLabelFirstMotionFormatDef.FMT_NAME;
    }

    [HiddenOption()]
    protected string installedVersion{ get; set; } = "0.0.0";

    public void setInstalledVersionToCurrentPkgVersion() { installedVersion = _PkgVersion; }
    public string getInstalledVersionStr() { return installedVersion; }

    [Category("General")]
    [DisplayName("First motion jump label background color")]
    [DisableOptionSerialization()]// no need to serialize this property, as it is stored in Fonts & Colors
    [Description("!!! ATTENTION !!! \nChanging this color makes sense only when\n 'Fetch 'first motion' jump label colors from' is equal to " + JumpLabelFirstMotionFormatDef.FMT_NAME)]
    public System.Drawing.Color JumpLabelFirstMotionBackgroundColor
    {
        get { return GetColor(s_jumpLabelFirstMotionColorBg); }
        set { 
#if DEBUG_COLOR_STYLE_OPTIONS
            Debug.WriteLine($"GeneralOptions.JumpLabelFirstMotionBackgroundColor property set color={value}");
            if (JumpLabelFirstMotionBackgroundColor != value) {
                Debug.WriteLine($"GeneralOptions.JumpLabelFirstMotionBackgroundColor property differs by val. Calling GeneralOptions.SetColor");
            }
#endif
            SetColor(isJumpLabelFirstMotionColorSourceItsOwn(), nameof(JumpLabelFirstMotionBackgroundColor), s_jumpLabelFirstMotionColorBg, value);
        }
    }



    [Category("General")]
    [DisplayName("First motion jump label foreground color")]
    [DisableOptionSerialization()]// no need to serialize this property, as it is stored in Fonts & Colors
    [Description("!!! ATTENTION !!! \nChanging this color makes sense only when\n 'Fetch 'first motion' jump label colors from' is equal to " + JumpLabelFirstMotionFormatDef.FMT_NAME)]
    public System.Drawing.Color JumpLabelFirstMotionForegroundColor
    {
        get { return GetColor(s_jumpLabelFirstMotionColorFg); }
        set { 
#if DEBUG_COLOR_STYLE_OPTIONS
            Debug.WriteLine($"GeneralOptions.JumpLabelFirstMotionForegroundColor property set color={value}");
            if (JumpLabelFirstMotionForegroundColor != value) {
                Debug.WriteLine($"GeneralOptions.JumpLabelFirstMotionForegroundColor property differs by val. Calling GeneralOptions.SetColor");
            }
#endif
            SetColor(isJumpLabelFirstMotionColorSourceItsOwn(), nameof(JumpLabelFirstMotionForegroundColor), s_jumpLabelFirstMotionColorFg, value);
        }
    }



    private bool isJumpLabelFinalMotionColorSourceItsOwn() {
        return JumplabelFinalMotionColorSource == JumpLabelFinalMotionFormatDef.FMT_NAME;
    }

    [Category("General")]
    [DisplayName("Final motion jump label background color")]
    [DisableOptionSerialization()]// no need to serialize this property, as it is stored in Fonts & Colors
    [Description("!!! ATTENTION !!! \nChanging this color makes sense only when\n 'Fetch 'final motion' jump label colors from' is equal to " + JumpLabelFinalMotionFormatDef.FMT_NAME)]
    public System.Drawing.Color JumpLabelFinalMotionBackgroundColor
    {
        get { return GetColor(s_jumpLabelFinalMotionColorBg); }
        set { 
#if DEBUG_COLOR_STYLE_OPTIONS
            Debug.WriteLine($"GeneralOptions.JumpLabelFinalMotionBackgroundColor property set color={value}");
            if (JumpLabelFinalMotionBackgroundColor != value) {
                Debug.WriteLine($"GeneralOptions.JumpLabelFinalMotionBackgroundColor property differs by val. Calling GeneralOptions.SetColor");
            }
#endif
            SetColor(isJumpLabelFinalMotionColorSourceItsOwn(), nameof(JumpLabelFinalMotionBackgroundColor), s_jumpLabelFinalMotionColorBg, value);
        }
    }


    [Category("General")]
    [DisplayName("Final motion jump label foreground color")]
    [DisableOptionSerialization()]// no need to serialize this property, as it is stored in Fonts & Colors
    [Description("!!! ATTENTION !!! \nChanging this color makes sense only when\n 'Fetch 'Final motion' jump label colors from' is equal to " + JumpLabelFinalMotionFormatDef.FMT_NAME)]
    public System.Drawing.Color JumpLabelFinalMotionForegroundColor
    {
        get { return GetColor(s_jumpLabelFinalMotionColorFg); }
        set { 
#if DEBUG_COLOR_STYLE_OPTIONS
            Debug.WriteLine($"GeneralOptions.JumpLabelFinalMotionForegroundColor property set color={value}");
            if (JumpLabelFinalMotionForegroundColor != value) {
                Debug.WriteLine($"GeneralOptions.JumpLabelFinalMotionForegroundColor property differs by val. Calling GeneralOptions.SetColor");
            }
#endif
            SetColor(isJumpLabelFinalMotionColorSourceItsOwn(), nameof(JumpLabelFinalMotionForegroundColor), s_jumpLabelFinalMotionColorFg, value);
        }
    }




    [Category("General")]
    [DisplayName("Jump label assignment algorithm")]
    [Description(
            "Affects jump label placement behaviour.\n" +
            "- CaretRelative - place shortest jump labels closer to caret,\n" +
            "label positions and values may be not reproducible if cursor moved slightly.\n" +
            "Sensivity to caret position can be adjusted using Caret position sensivity option.\n" +
            "Jump labels will be reproducible for caret positions differing \u00B1sensivity chars.\n" +
            "- ViewportRelative - assign labels starting from text viewport top, ignoring caret position."
            )]
    //[TypeConverter(typeof(EnumConverter))]
    [DefaultValue(JumpLabelAssignmentAlgorithm.CaretRelative)]
    public JumpLabelAssignmentAlgorithm jumpLabelAssignmentAlgorithm { get; set; } = JumpLabelAssignmentAlgorithm.CaretRelative;

    [Category("General")]
    [DisplayName("Caret position sensivity")]
    [Description(
            "Sensivity to caret position during jump label assignment.\n" +
            "Jumplabels will be reproducible for caret positions differing \u00B1sensivity chars.\n"
            )]
    [DefaultValue(0)]
    public int caretPositionSensivity { get; set; } = 0;



    private String jumplabelFirstMotionColorSource = JumpLabelFirstMotionFormatDef.FMT_NAME;
    [Category("General")]
    [DisplayName("Fetch 'first motion' jump label colors from")]
    [Description("Live preview available!\nJust iinvoke PeasyMotion and goto Tools->Options and adjust style with live preview.\n" +
                 "When is not equal to " + JumpLabelFirstMotionFormatDef.FMT_NAME + " one can sync label color style to other classification items from Tools->Options->Fonts And Colors->Text Editor.\n" +
                 "When equal to " + JumpLabelFirstMotionFormatDef.FMT_NAME + " one can configure classification style manually trough Tools->Options->PeasyMotion or\nTools->Options->Fonts And Colors->Text Editor->'PeasyMotion First Motion Jump label color'.")]
    [DefaultValue(JumpLabelFirstMotionFormatDef.FMT_NAME)] // by default we stick with peasy motion colors
    [TypeConverter(typeof(TextEditorClassificationStringConverter))]
    public String JumplabelFirstMotionColorSource { 
        get { return jumplabelFirstMotionColorSource; } 
        set { 
            if (TextEditorFontsAndColorsItemsList.ColorableItemsCached.Contains(value)) {
                jumplabelFirstMotionColorSource = value; 
            } else {
                jumplabelFirstMotionColorSource = JumpLabelFirstMotionFormatDef.FMT_NAME;
                Debug.WriteLine($"Trying to set jump label color source to unexistant source value = {value}. Ignoring!");
            }
            VsSettings.NotifyInstancesPropertyColorSourceChanged(nameof(JumpLabelFirstMotionForegroundColor), value);
            VsSettings.NotifyInstancesPropertyColorSourceChanged(nameof(JumpLabelFirstMotionBackgroundColor), value);
        } 
    }

    private String jumplabelFinalMotionColorSource = JumpLabelFinalMotionFormatDef.FMT_NAME;
    [Category("General")]
    [DisplayName("Fetch 'Final motion' jump label colors from")]
    [Description("Live preview available!\nJust iinvoke PeasyMotion and goto Tools->Options and adjust style with live preview.\n" +
                 "When is not equal to " + JumpLabelFinalMotionFormatDef.FMT_NAME + " one can sync label color style to other classification items from Tools->Options->Fonts And Colors->Text Editor.\n" +
                 "When equal to " + JumpLabelFinalMotionFormatDef.FMT_NAME + " one can configure classification style manually trough Tools->Options->PeasyMotion or\nTools->Options->Fonts And Colors->Text Editor->'PeasyMotion Final Motion Jump label color'.")]
    [DefaultValue(JumpLabelFinalMotionFormatDef.FMT_NAME)] // by default we stick with peasy motion colors
    [TypeConverter(typeof(TextEditorClassificationStringConverter))]
    public String JumplabelFinalMotionColorSource { 
        get { return jumplabelFinalMotionColorSource; } 
        set { 
            if (TextEditorFontsAndColorsItemsList.ColorableItemsCached.Contains(value)) {
                jumplabelFinalMotionColorSource = value; 
            } else {
                jumplabelFinalMotionColorSource = JumpLabelFinalMotionFormatDef.FMT_NAME;
                Trace.WriteLine($"Trying to set jump label color source to unexistant source value = {value}. Ignoring!");
            }
            VsSettings.NotifyInstancesPropertyColorSourceChanged(nameof(JumpLabelFinalMotionForegroundColor), value);
            VsSettings.NotifyInstancesPropertyColorSourceChanged(nameof(JumpLabelFinalMotionBackgroundColor), value);
        } 
    }

    public static System.Windows.Media.Color fromDrawingColor(System.Drawing.Color c) {
        return System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B);
    }
    public static System.Drawing.Color toDrawingColor(System.Windows.Media.Color c) {
        return System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
    }

    private System.Drawing.Color GetColor(ColorKey colorKey) { 
#if DEBUG_COLOR_STYLE_OPTIONS 
        Debug.WriteLine($"GeneralOptions.GetColor keyName={colorKey.Name}" + (colorKey.IsForeground? "FG":"BG") + $" color={colorMap[colorKey].Color}");
#endif
        return colorMap[colorKey].Color; 
    }

    private void SetColor(bool sendNotification, string propertyName, ColorKey colorKey, System.Drawing.Color value) { 
#if DEBUG_COLOR_STYLE_OPTIONS 
        Debug.WriteLine($"GeneralOptions.SetColor keyName={colorKey.Name}" + (colorKey.IsForeground? "FG":"BG") + $" color={value} Notify={sendNotification}");
#endif
        colorMap[colorKey].Color = value; 
        if (sendNotification) {
            VsSettings.NotiifyInstancesFmtPropertyChanged(propertyName, fromDrawingColor(value));
        }
    }
        
    public void LoadColors(IServiceProvider Site)
    {
#if DEBUG_COLOR_STYLE_OPTIONS 
        Debug.WriteLine($"GeneralOptions.LoadColors");
#endif
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            var guid = Microsoft.VisualStudio.Editor.DefGuidList.guidTextEditorFontCategory;
            var flags = __FCSTORAGEFLAGS.FCSF_LOADDEFAULTS;//| __FCSTORAGEFLAGS.FCSF_NOAUTOCOLORS;
            var vsStorage = (IVsFontAndColorStorage)(Site.GetService(typeof(SVsFontAndColorStorage)));
            ErrorHandler.ThrowOnFailure(vsStorage.OpenCategory(ref guid, (uint)flags));
            LoadColorsCore(Site, vsStorage);
            ErrorHandler.ThrowOnFailure(vsStorage.CloseCategory());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PeasyMotion exception in Options.LoadColors: {ex.ToString()}");
        }
    }

    private void LoadColorsCore(IServiceProvider Site, IVsFontAndColorStorage vsStorage)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        foreach (var colorKey in s_colorKeyList)
        {
            ColorInfo colorInfo;
            try
            {
                var color = LoadColor(Site, vsStorage, colorKey);
                colorInfo = new ColorInfo(colorKey, color);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PeasyMotion exception in Options.LoadColorsCore: {ex.ToString()}");
                colorInfo = new ColorInfo(colorKey, System.Drawing.Color.Black, isValid: false);
            }

            colorMap[colorKey] = colorInfo;
        }
    }

    public void SaveColors(IServiceProvider Site)
    {
        Debug.WriteLine($"GeneralOptions.SaveColors");
        ThreadHelper.ThrowIfNotOnUIThread();
        if (Site == null)
        {
            return;
        }

        try
        {
            var guid = Microsoft.VisualStudio.Editor.DefGuidList.guidTextEditorFontCategory;
            var flags = __FCSTORAGEFLAGS.FCSF_LOADDEFAULTS | __FCSTORAGEFLAGS.FCSF_PROPAGATECHANGES;
            var vsStorage = (IVsFontAndColorStorage)(Site.GetService(typeof(SVsFontAndColorStorage)));
            var vsStorageCache = (IVsFontAndColorCacheManager )(Site.GetService(typeof(SVsFontAndColorCacheManager)));

            ErrorHandler.ThrowOnFailure(vsStorage.OpenCategory(ref guid, (uint)flags));
            foreach (var colorInfo in colorMap.Values)
            {
                SaveColor(vsStorage, colorInfo.ColorKey, colorInfo.Color);
            }
            ErrorHandler.ThrowOnFailure(vsStorage.CloseCategory());

            if (vsStorageCache != null) {
                vsStorageCache.ClearAllCaches();
                vsStorageCache.RefreshCache(Microsoft.VisualStudio.Editor.DefGuidList.guidTextEditorFontCategory);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PeasyMotion exception in Options.SaveColors(): {ex.ToString()}");
        }
    }

    private static System.Drawing.Color LoadColor(IServiceProvider Site, IVsFontAndColorStorage vsStorage, ColorKey colorKey)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var arr = new ColorableItemInfo[1];
        ErrorHandler.ThrowOnFailure(vsStorage.GetItem(colorKey.Name, arr));

        var isValid = colorKey.IsForeground ? arr[0].bForegroundValid : arr[0].bBackgroundValid;
        if (isValid == 0)
        {
            throw new Exception();
        }

        var colorRef = colorKey.IsForeground ? arr[0].crForeground : arr[0].crBackground;
        var color = FromColorRef(Site, vsStorage, colorRef);

        Debug.WriteLine($"GeneralOptions.LoadColoR keyName={colorKey.Name} color={color}");
        return color;
    }

    private static void SaveColor(IVsFontAndColorStorage vsStorage, ColorKey colorKey, System.Drawing.Color color)
    {
        Debug.WriteLine($"GeneralOptions.SaveColoR keyName={colorKey.Name} color={color}");
        ThreadHelper.ThrowIfNotOnUIThread();
        ColorableItemInfo[] arr = new ColorableItemInfo[1];
        ErrorHandler.ThrowOnFailure(vsStorage.GetItem(colorKey.Name, arr));
        if (colorKey.IsForeground)
        {
            arr[0].bForegroundValid = 1;
            arr[0].crForeground = (uint)ColorTranslator.ToWin32(color);
        }
        else
        {
            arr[0].bBackgroundValid = 1;
            arr[0].crBackground = (uint)ColorTranslator.ToWin32(color);
        }
        ErrorHandler.ThrowOnFailure(vsStorage.SetItem(colorKey.Name, arr));
    }

    private static System.Drawing.Color FromColorRef(IServiceProvider Site, IVsFontAndColorStorage vsStorage, uint colorValue)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var vsUtil = (IVsFontAndColorUtilities)vsStorage;
        ErrorHandler.ThrowOnFailure(vsUtil.GetColorType(colorValue, out int type));
        switch ((__VSCOLORTYPE)type)
        {
            case __VSCOLORTYPE.CT_SYSCOLOR:
            case __VSCOLORTYPE.CT_RAW:
                return ColorTranslator.FromWin32((int)colorValue);
            case __VSCOLORTYPE.CT_COLORINDEX:
                {
                    var array = new COLORINDEX[1];
                    ErrorHandler.ThrowOnFailure(vsUtil.GetEncodedIndex(colorValue, array));
                    ErrorHandler.ThrowOnFailure(vsUtil.GetRGBOfIndex(array[0], out uint rgb));
                    return ColorTranslator.FromWin32((int)rgb);
                };
            case __VSCOLORTYPE.CT_VSCOLOR:
                {
                    var vsUIShell = (IVsUIShell2)Site.GetService(typeof(SVsUIShell));
                    ErrorHandler.ThrowOnFailure(vsUtil.GetEncodedVSColor(colorValue, out int index));
                    ErrorHandler.ThrowOnFailure(vsUIShell.GetVSSysColorEx(index, out uint rgbValue));
                    return ColorTranslator.FromWin32((int)rgbValue);
                };
            case __VSCOLORTYPE.CT_AUTOMATIC:
            case __VSCOLORTYPE.CT_TRACK_BACKGROUND:
            case __VSCOLORTYPE.CT_TRACK_FOREGROUND:
            case __VSCOLORTYPE.CT_INVALID:
                return System.Drawing.Color.Transparent;
            default:
                return System.Drawing.Color.Black;
        }
    }
}

internal class DialogPageProvider
{
    public class General : BaseOptionPage<GeneralOptions> 
    { 
        protected override void OnActivate(CancelEventArgs e)
        {
            Debug.WriteLine($"GeneralOptions OnActivate");
            ThreadHelper.ThrowIfNotOnUIThread();
            base.OnActivate(e);
            (base._model as GeneralOptions).LoadColors(this.Site);
        }

        protected override void OnApply(PageApplyEventArgs e)
        {
            Debug.WriteLine($"GeneralOptions OnApply");
            ThreadHelper.ThrowIfNotOnUIThread();
            base.OnApply(e);
            (base._model as GeneralOptions).SaveColors(this.Site);
        }
    }
}

}